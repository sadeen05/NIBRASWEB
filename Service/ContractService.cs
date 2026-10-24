using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;
using NibrasWeb.Service;

namespace NIBRAS.API.Services;

/// <summary>
/// Step 6 (Contracts MVP). Contract lifecycle: create from an Accepted offer (Admin),
/// both parties sign, Admin reviews (approve -> Active / reject -> Rejected), and Admin
/// terminates an Active contract. Review/terminate release the linked grid reservation and
/// return the land to Verified. Cancellation, force-terminate, background jobs and
/// ProjectApproval tracking are intentionally out of scope.
/// Step 7 hardening (F1/F2): creation refuses an expired hold, approval clears the
/// reservation's hold expiry, and rejection is permitted before both parties sign.
/// </summary>
public class ContractService : IContractService
{
    private const int MaxPageSize = 50;
    private const int ReleaseReasonMaxLength = 300; // GridCapacityReservation.ReleaseReason (max 300)
    private const string OfferContractConflictMessage = "A contract already exists for this offer.";

    private readonly NebrasdbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IOfferService _offerService;

    public ContractService(NebrasdbContext context, ICurrentUser currentUser, IOfferService offerService)
    {
        _context = context;
        _currentUser = currentUser;
        _offerService = offerService;
    }

    public async Task<ContractListResponse> GetAllAsync(int pageNumber, int pageSize, string? status)
    {
        if (pageNumber < 1)
            throw new ValidationFailedException("Page number must be at least 1.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationFailedException("Page size must be between 1 and 50.");

        var statusFilter = ParseStatus(status);

        var query = _context.Contracts
            .AsNoTracking()
            .Include(c => c.Land)
            .Include(c => c.Investor)
            .Include(c => c.Landlord)
            .Include(c => c.OfferVersion)
            .AsQueryable();

        if (!IsAdmin)
        {
            var userId = RequireUserId();
            query = _currentUser.Role == RoleNames.Landlord
                ? query.Where(c => c.LandlordId == userId)
                : _currentUser.Role == RoleNames.Investor
                    ? query.Where(c => c.InvestorId == userId)
                    : query.Where(c => false);
        }

        if (statusFilter is { } filter)
            query = query.Where(c => c.Status == filter);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // SEC-14 style guard: an offset beyond Int32 cannot exist in any result set, so it
        // yields an empty page instead of a negative OFFSET that SQL Server rejects.
        var offset = (long)(pageNumber - 1) * pageSize;
        if (offset > int.MaxValue)
            return new ContractListResponse([], pageNumber, pageSize, totalCount, totalPages);

        var contracts = await query
            .OrderBy(c => c.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync();

        return new ContractListResponse(contracts.Select(ToDto).ToList(), pageNumber, pageSize, totalCount, totalPages);
    }

    public async Task<ContractDto> GetByIdAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);
        return ToDto(contract);
    }

    public async Task<ContractDto> CreateAsync(CreateContractRequest request)
    {
        EnsureAdmin("Only admins can create contracts.");
        var adminId = RequireUserId();

        var offer = await _context.Offers
            .Include(o => o.Land)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId)
            ?? throw new NotFoundException("Offer not found.");

        if (offer.Status != OfferStatus.Accepted)
            throw new BusinessRuleException("Only an accepted offer can become a contract.");

        var hasLiveContract = await _context.Contracts.AnyAsync(c =>
            c.OfferId == offer.Id &&
            (c.Status == ContractStatus.PendingSignatures || c.Status == ContractStatus.Active));
        if (hasLiveContract)
            throw new BusinessRuleException(OfferContractConflictMessage);

        var land = offer.Land;
        if (land.Status != LandStatus.UnderContract)
            throw new BusinessRuleException("The land is not under contract.");

        // The offer approval that moved the land under contract checked the evidence, but the
        // evidence can stop being valid before this creation: an admin can make a new document
        // type required (which is the only way an UnderContract land becomes outstanding, since
        // no review, upload or change-request decision can run on it), and a SuperAdmin can then
        // reject a document in the re-review that outstanding evidence opens. Creating the
        // contract anyway would commit a land under contract on evidence a reviewer has rejected,
        // so the same derived rule that gates OfferService.CreateAsync and
        // OfferService.AdminApproveAsync is re-checked here. Nothing is mutated: the accepted
        // offer, the reservation and the offer history are left untouched, and the admin can
        // create the contract once the corrected document is approved.
        //
        // Residual (see SECURITY_TRACEABILITY.md §3.3): the requirement change itself takes no lock
        // on LANDS or on the offer, so it can commit between this read and the contract insert. No
        // lock in this method can close that: a LANDS UPDLOCK would only serialize against a
        // re-review, which cannot start until such a change has already committed. The check is
        // therefore left with the other creation preconditions rather than inside the transaction.
        if (await _context.HasOutstandingRequiredDocumentAsync(land.Id))
            throw new BusinessRuleException(
                "This contract cannot be created while the land's required documents are being corrected.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Step 7 F1 (concurrency): pin this offer's active reservation row for the rest of the
        // transaction (repo convention: UPDLOCK, HOLDLOCK) so a concurrent admin release cannot
        // release the hold between this read and the ContractId attach below. Lock order: this
        // method takes the reservation lock before inserting the contract, while
        // GridCapacityReservationService.ReleaseAsync reads the CONTRACTS row before taking the
        // same reservation lock, so the two orderings can never form a cycle.
        var reservation = await _context.GridCapacityReservations
            .FromSqlInterpolated($"SELECT * FROM GRID_CAPACITY_RESERVATIONS WITH (UPDLOCK, HOLDLOCK) WHERE OFFER_ID = {offer.Id} AND RELEASED_AT IS NULL")
            .FirstOrDefaultAsync()
            ?? throw new BusinessRuleException("This offer has no active grid reservation.");

        // Step 7 F1: an expired hold cannot become a contract. An active reservation is
        // ReleasedAt IS NULL and (ExpiresAt IS NULL or ExpiresAt in the future).
        if (reservation.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("The grid reservation for this offer has expired.");

        var contract = new Contract
        {
            OfferId = offer.Id,
            LandId = land.Id,
            InvestorId = offer.InvestorId,
            LandlordId = land.LandlordId,
            OfferVersionId = offer.AcceptedVersionId,
            CriterionId = land.VerifiedAgainstCriterionId,
            Status = ContractStatus.PendingSignatures,
            CreatedAt = DateTime.UtcNow
            // NoticePeriodDays is intentionally left to its database default (90).
        };

        _context.Contracts.Add(contract);
        await SaveContractChangesAsync();

        reservation.ContractId = contract.Id;
        await _context.SaveChangesAsync();

        // Preserve the complete initial draft as immutable version 1.
        await RecordContractVersionAsync(contract, "Draft", "Created", contract.Status.ToString(), null, contract.CreatedAt ?? DateTime.UtcNow);
        await _context.SaveChangesAsync();

        // Established offer-closure path: Accepted -> Closed with a ClosedByContract history row.
        await _offerService.MarkOfferClosedAsync(offer.Id, contract.Id, adminId);

        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> SignAsync(int id)
    {
        var userId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent sign
        // requests for the same contract cannot both act on stale state.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);

        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new ForbiddenException("Only the contract parties can sign.");

        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only contracts pending signatures can be signed.");

        if (await HasOpenRevisionAsync(contract.Id))
            throw new BusinessRuleException(
                "A contract revision is in progress; resolve it before signing the contract.");

        var now = DateTime.UtcNow;
        if (userId == contract.InvestorId)
        {
            if (contract.InvestorSignedAt is not null)
                throw new BusinessRuleException("The investor has already signed this contract.");
            contract.InvestorSignedAt = now;
        }
        else
        {
            if (contract.LandlordSignedAt is not null)
                throw new BusinessRuleException("The landlord has already signed this contract.");
            contract.LandlordSignedAt = now;
        }

        // Signature events point at the exact draft version that was signed.
        await RecordContractVersionAsync(contract, "Event", "Signed", contract.Status.ToString(), null, now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> ReviewAsync(int id, ReviewContractRequest request)
    {
        EnsureAdmin("Only admins can review contracts.");
        var adminId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent
        // review/terminate requests serialize and cannot insert conflicting review rows.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();

        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only contracts pending signatures can be reviewed.");

        if (await HasOpenRevisionAsync(contract.Id))
            throw new BusinessRuleException(
                "A contract revision is in progress; resolve it before reviewing the contract.");

        var now = DateTime.UtcNow;

        if (request.Approve)
        {
            // Step 7 F2: approval still requires both parties to have signed.
            if (contract.InvestorSignedAt is null || contract.LandlordSignedAt is null)
                throw new BusinessRuleException("Both parties must sign before the contract can be reviewed.");

            contract.Status = ContractStatus.Active;
            contract.AdminReviewedAt = now;
            contract.AdminSignedAt = now;
            AddReview(contract.Id, adminId, ContractReviewDecisions.Approved, null, now);

            // Step 7 F1: an approved contract's reservation becomes permanent (no hold expiry).
            await ClearReservationExpiryAsync(contract);

            // BR-O6 / state machine: once the contract is Active the project is live, so the
            // competing offers on the land are closed (they were only frozen while the land was
            // UnderContract). Closed offers keep their history and are never resumed.
            await CloseCompetitorOffersAsync(contract, adminId, now);
            await RecordContractVersionAsync(contract, "Event", "Reviewed", contract.Status.ToString(), null, now);
        }
        else
        {
            // Step 7 F2: rejection requires a reason but NOT signatures, so an unsigned
            // contract can still be rejected (releasing the hold and returning the land).
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationFailedException("A rejection reason is required.");

            contract.Status = ContractStatus.Rejected;
            AddReview(contract.Id, adminId, ContractReviewDecisions.Rejected, request.Reason, now);
            await ReleaseReservationAsync(contract, request.Reason!, now);
            ReturnLandToVerified(contract.Land, adminId, "Land returned to Verified after the contract was rejected.", now);
            await RecordContractVersionAsync(contract, "Event", "Rejected", contract.Status.ToString(), request.Reason, now);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> TerminateAsync(int id, TerminateContractRequest request)
    {
        EnsureAdmin("Only admins can terminate contracts.");
        var adminId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent
        // review/terminate requests serialize and cannot both transition the same contract.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();

        if (contract.Status != ContractStatus.Active)
            throw new BusinessRuleException("Only active contracts can be terminated.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("A termination reason is required.");

        var now = DateTime.UtcNow;

        contract.Status = ContractStatus.Terminated;
        AddReview(contract.Id, adminId, ContractReviewDecisions.Terminated, request.Reason, now);
        await ReleaseReservationAsync(contract, request.Reason, now);
        ReturnLandToVerified(contract.Land, adminId, "Land returned to Verified after the contract was terminated.", now);
        await RecordContractVersionAsync(contract, "Event", "Terminated", contract.Status.ToString(), request.Reason, now);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<List<ContractReviewDto>> GetReviewsAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);

        var reviews = await _context.ContractReviews
            .AsNoTracking()
            .Include(r => r.Reviewer)
            .Where(r => r.ContractId == id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        return reviews.Select(r => new ContractReviewDto
        {
            Id = r.Id,
            Decision = r.Decision,
            Reason = r.Reason,
            ReviewerId = r.ReviewerId,
            ReviewerName = r.Reviewer?.FullName ?? string.Empty,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<List<ContractVersionDto>> GetVersionsAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);

        var versions = await _context.ContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == id)
            .OrderBy(v => v.Id)
            .ToListAsync();

        return versions.Select(v => new ContractVersionDto
        {
            Id = v.Id,
            ContractId = v.ContractId,
            VersionNumber = v.VersionNumber,
            VersionKind = v.VersionKind,
            EventType = v.EventType,
            Status = v.Status,
            PreviousVersionId = v.PreviousVersionId,
            TargetVersionId = v.TargetVersionId,
            OfferId = v.OfferId,
            OfferVersionId = v.OfferVersionId,
            Terms = TryDeserializeTerms(v.TermsSnapshot),
            Reason = v.Reason,
            CreatedById = v.CreatedById,
            CreatedByName = v.CreatedByNameSnapshot,
            CreatedByRole = v.CreatedByRoleSnapshot,
            CreatedAt = v.CreatedAt
        }).ToList();
    }

    public async Task<List<ContractRevisionDto>> GetRevisionsAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);

        var revisions = await _context.ContractRevisions
            .AsNoTracking()
            .Where(r => r.ContractId == id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        return await ToRevisionDtosAsync(revisions);
    }

    public async Task<ContractRevisionDto> RequestRevisionAsync(
        int id,
        CreateContractRevisionRequest request)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();
        await _context.Entry(contract).Reference(c => c.Offer).LoadAsync();

        // Rule: only the two contract parties may request a pre-signing revision.
        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new ForbiddenException("Only the contract parties can request a revision.");

        if (await HasOpenRevisionAsync(contract.Id))
            throw new BusinessRuleException("This contract already has an open revision.");

        var latestDraft = await EnsureRevisionEligibleAsync(contract, now);
        if (latestDraft.Id != request.ExpectedDraftVersionId)
            throw new BusinessRuleException(
                "This contract changed after you loaded it. Reload the current draft and try again.");

        var currentTerms = await LoadCurrentTermsAsync(contract);
        var proposedTerms = BuildProposedTerms(contract, request);
        if (!TermsDiffer(currentTerms, proposedTerms))
            throw new BusinessRuleException("The proposed revision must change at least one contract term.");

        var (actorName, actorRole) = await GetActorSnapshotAsync(userId);

        var revision = new ContractRevision
        {
            ContractId = contract.Id,
            BaseDraftVersionId = latestDraft.Id,
            Status = ContractRevisionStatuses.PendingPartyConsent,
            Reason = request.Reason.Trim(),
            ProposedTermsSnapshot = JsonSerializer.Serialize(proposedTerms),
            ProposedById = userId,
            ProposedByNameSnapshot = actorName,
            ProposedByRoleSnapshot = actorRole,
            ProposedAt = now
        };

        _context.ContractRevisions.Add(revision);
        await SaveRevisionChangesAsync();

        await RecordContractVersionAsync(
            contract,
            "Event",
            "RevisionRequested",
            contract.Status.ToString(),
            $"Revision #{revision.Id}: {revision.Reason}",
            now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetRevisionDtoByIdAsync(contract.Id, revision.Id);
    }

    public async Task<ContractRevisionDto> ConsentToRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionConsentRequest request)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        var revision = await LockRevisionAsync(id, revisionId);

        if (revision.Status != ContractRevisionStatuses.PendingPartyConsent)
            throw new BusinessRuleException("This revision is not awaiting party consent.");

        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new ForbiddenException("Only the contract parties can respond to a revision.");

        await EnsureRevisionDraftCurrentAsync(contract, revision, request.ExpectedDraftVersionId);

        var existingConsent = await _context.ContractRevisionConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RevisionId == revision.Id && c.PartyUserId == userId);
        if (existingConsent is not null)
            throw new BusinessRuleException("This party has already responded to this revision.");

        if (!request.Approved && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("A rejection reason is required.");

        var (actorName, actorRole) = await GetActorSnapshotAsync(userId);
        var decision = request.Approved
            ? ContractRevisionConsentDecisions.Approved
            : ContractRevisionConsentDecisions.Rejected;

        _context.ContractRevisionConsents.Add(new ContractRevisionConsent
        {
            RevisionId = revision.Id,
            PartyUserId = userId,
            PartyNameSnapshot = actorName,
            PartyRoleSnapshot = actorRole,
            Decision = decision,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedAt = now
        });
        await SaveRevisionChangesAsync();

        if (!request.Approved)
        {
            revision.Status = ContractRevisionStatuses.Rejected;
            revision.ClosedAt = now;
            await RecordContractVersionAsync(
                contract,
                "Event",
                "RevisionPartyRejected",
                contract.Status.ToString(),
                $"Revision #{revision.Id}: {request.Reason!.Trim()}",
                now);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return await GetRevisionDtoByIdAsync(id, revision.Id);
        }

        var consents = await _context.ContractRevisionConsents
            .AsNoTracking()
            .Where(c => c.RevisionId == revision.Id)
            .ToListAsync();

        if (consents.Count(c => c.Decision == ContractRevisionConsentDecisions.Approved) == 2 &&
            consents.Any(c => c.PartyUserId == contract.InvestorId) &&
            consents.Any(c => c.PartyUserId == contract.LandlordId))
        {
            revision.Status = ContractRevisionStatuses.PendingAdminReview;
        }

        await RecordContractVersionAsync(
            contract,
            "Event",
            "RevisionPartyApproved",
            contract.Status.ToString(),
            $"Revision #{revision.Id}: party approved the revised terms.",
            now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetRevisionDtoByIdAsync(id, revision.Id);
    }

    public async Task<ContractRevisionDto> AdminReviewRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionAdminDecisionRequest request)
    {
        EnsureAdmin("Only admins can review contract revisions.");
        var adminId = RequireUserId();
        var now = DateTime.UtcNow;
        var decision = (request.Decision ?? "").Trim();

        if (!string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decision, "Escalate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationFailedException("The admin decision must be Approve or Escalate.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();
        var revision = await LockRevisionAsync(id, revisionId);

        if (revision.Status != ContractRevisionStatuses.PendingAdminReview)
            throw new BusinessRuleException("This revision is not awaiting admin review.");

        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only a contract pending signatures can be revised.");
        if (contract.InvestorSignedAt is not null && contract.LandlordSignedAt is not null)
            throw new BusinessRuleException("Fully signed contracts cannot be revised; use the amendment workflow.");
        if (contract.AdminReviewedAt is not null || contract.AdminSignedAt is not null)
            throw new BusinessRuleException("Reviewed or finalised contracts cannot use the draft-revision workflow.");

        await EnsureRevisionDraftCurrentAsync(contract, revision, request.ExpectedDraftVersionId);
        await EnsureBothPartiesApprovedAsync(contract, revision);

        var (actorName, actorRole) = await GetActorSnapshotAsync(adminId);
        revision.AdminReviewedById = adminId;
        revision.AdminReviewedByNameSnapshot = actorName;
        revision.AdminReviewedByRoleSnapshot = actorRole;
        revision.AdminReviewedAt = now;

        if (string.Equals(decision, "Escalate", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationFailedException("An escalation reason is required.");

            revision.Status = ContractRevisionStatuses.PendingSuperAdminReview;
            revision.AdminDecision = "Escalated";
            revision.EscalationReason = request.Reason.Trim();

            AddReview(
                contract.Id,
                adminId,
                ContractReviewDecisions.RevisionEscalated,
                revision.EscalationReason,
                now);
            await RecordContractVersionAsync(
                contract,
                "Event",
                "RevisionEscalated",
                contract.Status.ToString(),
                $"Revision #{revision.Id}: {revision.EscalationReason}",
                now);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return await GetRevisionDtoByIdAsync(id, revision.Id);
        }

        revision.AdminDecision = "Approved";
        await ApproveRevisionCoreAsync(
            contract,
            revision,
            adminId,
            ContractReviewDecisions.RevisionApproved,
            string.IsNullOrWhiteSpace(request.Reason) ? revision.Reason : request.Reason.Trim(),
            now);

        await transaction.CommitAsync();
        return await GetRevisionDtoByIdAsync(id, revision.Id);
    }

    public async Task<ContractRevisionDto> SuperAdminDecideRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionSuperAdminDecisionRequest request)
    {
        if (_currentUser.Role != RoleNames.SuperAdmin)
            throw new ForbiddenException("Only a SuperAdmin can decide an escalated contract revision.");

        var superAdminId = RequireUserId();
        var now = DateTime.UtcNow;
        var decision = (request.Decision ?? "").Trim();

        if (!string.Equals(decision, "Continue", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decision, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationFailedException("The SuperAdmin decision must be Continue or Cancel.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("A SuperAdmin decision reason is required.");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();
        var revision = await LockRevisionAsync(id, revisionId);

        if (revision.Status != ContractRevisionStatuses.PendingSuperAdminReview)
            throw new BusinessRuleException("This revision is not awaiting a SuperAdmin decision.");

        await EnsureRevisionDraftCurrentAsync(contract, revision, request.ExpectedDraftVersionId);
        await EnsureBothPartiesApprovedAsync(contract, revision);

        var (actorName, actorRole) = await GetActorSnapshotAsync(superAdminId);
        revision.SuperAdminDecidedById = superAdminId;
        revision.SuperAdminDecidedByNameSnapshot = actorName;
        revision.SuperAdminDecidedByRoleSnapshot = actorRole;
        revision.SuperAdminDecidedAt = now;
        revision.SuperAdminReason = request.Reason.Trim();

        if (string.Equals(decision, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            revision.Status = ContractRevisionStatuses.Cancelled;
            revision.SuperAdminDecision = "Cancelled";
            revision.ClosedAt = now;

            contract.Status = ContractStatus.Cancelled;
            AddReview(
                contract.Id,
                superAdminId,
                ContractReviewDecisions.RevisionCancelled,
                revision.SuperAdminReason,
                now);

            // Reuse the established pre-signing rejection effects: release the linked
            // reservation (never delete it) and return the land to Verified. The offer stays
            // closed and no competing offer is reopened explicitly, matching rejection.
            await ReleaseReservationAsync(contract, revision.SuperAdminReason, now);
            ReturnLandToVerified(
                contract.Land,
                superAdminId,
                "Land returned to Verified after the contract revision was cancelled.",
                now);
            await RecordContractVersionAsync(
                contract,
                "Event",
                "RevisionCancelled",
                contract.Status.ToString(),
                $"Revision #{revision.Id}: {revision.SuperAdminReason}",
                now);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return await GetRevisionDtoByIdAsync(id, revision.Id);
        }

        revision.SuperAdminDecision = "Continued";
        await ApproveRevisionCoreAsync(
            contract,
            revision,
            superAdminId,
            ContractReviewDecisions.RevisionContinued,
            revision.SuperAdminReason,
            now);

        await transaction.CommitAsync();
        return await GetRevisionDtoByIdAsync(id, revision.Id);
    }

    private async Task ApproveRevisionCoreAsync(
        Contract contract,
        ContractRevision revision,
        int actorId,
        string reviewDecision,
        string? decisionReason,
        DateTime now)
    {
        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only a contract pending signatures can be revised.");
        if (contract.InvestorSignedAt is not null && contract.LandlordSignedAt is not null)
            throw new BusinessRuleException("Fully signed contracts cannot be revised; use the amendment workflow.");
        if (contract.AdminReviewedAt is not null || contract.AdminSignedAt is not null)
            throw new BusinessRuleException("Reviewed or finalised contracts cannot use the draft-revision workflow.");
        if (contract.Land is null || contract.Land.Status != LandStatus.UnderContract)
            throw new BusinessRuleException("The land is no longer under contract.");

        var latestDraft = await EnsureRevisionDraftCurrentAsync(
            contract,
            revision,
            revision.BaseDraftVersionId);
        await EnsureActiveReservationAsync(contract, now);

        // Lock the offer too: the accepted version must only change through this workflow.
        var offer = await _context.Offers
            .FromSqlInterpolated($"SELECT * FROM OFFERS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {contract.OfferId}")
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Offer not found.");

        if (offer.Status != OfferStatus.Closed)
            throw new BusinessRuleException("The offer is not in the contract-owned state.");
        if (offer.LandId != contract.LandId || offer.InvestorId != contract.InvestorId)
            throw new BusinessRuleException("The offer no longer matches this contract.");
        if (offer.AcceptedVersionId != contract.OfferVersionId ||
            latestDraft.OfferVersionId != contract.OfferVersionId)
        {
            throw new BusinessRuleException(
                "The accepted offer version changed after this revision was requested. Reload and try again.");
        }

        var versions = await _context.OfferVersions
            .Where(v => v.OfferId == offer.Id)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();

        var currentVersion = versions.FirstOrDefault(v => v.Id == contract.OfferVersionId)
            ?? throw new BusinessRuleException("The accepted offer version is no longer available.");
        var currentTerms = TermsFromOfferVersion(contract, currentVersion);

        var proposedTerms = TryDeserializeTerms(revision.ProposedTermsSnapshot)
            ?? throw new BusinessRuleException(
                "The stored revision terms cannot be read, so the revision cannot be approved.");
        if (proposedTerms.LandId != contract.LandId ||
            proposedTerms.InvestorId != contract.InvestorId ||
            proposedTerms.LandlordId != contract.LandlordId)
        {
            throw new BusinessRuleException("The stored revision terms do not match this contract.");
        }
        if (!TermsDiffer(currentTerms, proposedTerms))
            throw new BusinessRuleException("The proposed revision does not change any contract term.");

        var newVersionNumber = versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1;

        // The filtered unique index requires the previous current row to be persisted with
        // IsCurrent=false before the new current row is inserted.
        foreach (var current in versions.Where(v => v.IsCurrent))
            current.IsCurrent = false;
        await _context.SaveChangesAsync();

        var newVersion = new OfferVersion
        {
            OfferId = offer.Id,
            VersionNumber = newVersionNumber,
            LandlordSharePct = proposedTerms.LandlordSharePct,
            DurationYears = proposedTerms.DurationYears,
            StartDate = proposedTerms.StartDate,
            InstallationCost = proposedTerms.InstallationCost,
            SolarCellCapacityKw = proposedTerms.SolarCellCapacityKw,
            ExpectedAnnualRevenue = proposedTerms.ExpectedAnnualRevenue,
            EffectiveCostPerKw = proposedTerms.EffectiveCostPerKw,
            PaybackPeriodMonths = proposedTerms.PaybackPeriodMonths,
            Message = revision.Reason,
            IsCurrent = true,
            CreatedById = actorId,
            CreatedAt = now
        };
        _context.OfferVersions.Add(newVersion);
        await _context.SaveChangesAsync();

        offer.AcceptedVersionId = newVersion.Id;
        offer.CurrentAmount = proposedTerms.LandlordSharePct ?? offer.CurrentAmount;
        offer.LastProposedById = revision.ProposedById;
        offer.UpdatedAt = now;

        _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
        {
            OfferId = offer.Id,
            ActionType = OfferHistoryActionType.ApprovedByAdmin,
            ActorId = actorId,
            Amount = offer.CurrentAmount,
            Message = $"Contract revision approved: accepted offer version {newVersion.VersionNumber}.",
            CreatedAt = now
        });

        revision.Status = ContractRevisionStatuses.Approved;
        revision.ClosedAt = now;
        revision.NewOfferVersionId = newVersion.Id;

        AddReview(contract.Id, actorId, reviewDecision, decisionReason, now);

        // Record the superseded draft while the contract still points at it, so the event's
        // immutable terms snapshot describes the draft it acted on.
        await RecordContractVersionAsync(
            contract,
            "Event",
            "DraftSuperseded",
            contract.Status.ToString(),
            $"Draft v{latestDraft.VersionNumber} superseded by approved revision #{revision.Id}.",
            now);

        contract.OfferVersionId = newVersion.Id;
        contract.OfferVersion = newVersion;
        contract.NoticePeriodDays = proposedTerms.NoticePeriodDays;
        contract.InvestorSignedAt = null;
        contract.LandlordSignedAt = null;
        contract.AdminReviewedAt = null;
        contract.AdminSignedAt = null;

        var newDraft = await RecordContractVersionAsync(
            contract,
            "Draft",
            "Revised",
            contract.Status.ToString(),
            revision.Reason,
            now);

        await _context.SaveChangesAsync();
        revision.NewDraftVersionId = newDraft.Id;
        await _context.SaveChangesAsync();
    }

    private async Task<ContractVersion> EnsureRevisionEligibleAsync(Contract contract, DateTime now)
    {
        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only a contract pending signatures can be revised.");
        if (contract.AdminReviewedAt is not null || contract.AdminSignedAt is not null)
            throw new BusinessRuleException("Reviewed or finalised contracts cannot use the draft-revision workflow.");
        if (contract.InvestorSignedAt is not null && contract.LandlordSignedAt is not null)
            throw new BusinessRuleException("Fully signed contracts cannot be revised; use the amendment workflow.");
        if (contract.Land is null || contract.Land.Status != LandStatus.UnderContract)
            throw new BusinessRuleException("The land is no longer under contract.");

        var latestDraft = await GetLatestDraftAsync(contract.Id)
            ?? throw new BusinessRuleException("This contract has no recorded draft to revise.");
        if (latestDraft.OfferVersionId != contract.OfferVersionId)
            throw new BusinessRuleException("The current draft does not match the accepted offer version.");

        if (contract.Offer is null)
            contract.Offer = await _context.Offers
                .FirstOrDefaultAsync(o => o.Id == contract.OfferId)
                ?? throw new NotFoundException("Offer not found.");
        if (contract.Offer.Status != OfferStatus.Closed)
            throw new BusinessRuleException("The offer is not in the contract-owned state.");
        if (contract.Offer.AcceptedVersionId != contract.OfferVersionId ||
            contract.Offer.LandId != contract.LandId ||
            contract.Offer.InvestorId != contract.InvestorId)
        {
            throw new BusinessRuleException("The offer no longer matches this contract.");
        }

        await EnsureActiveReservationAsync(contract, now);
        return latestDraft;
    }

    private async Task<ContractVersion> EnsureRevisionDraftCurrentAsync(
        Contract contract,
        ContractRevision revision,
        int expectedDraftVersionId)
    {
        var latestDraft = await GetLatestDraftAsync(contract.Id)
            ?? throw new BusinessRuleException("This contract has no recorded draft to revise.");

        if (latestDraft.Id != expectedDraftVersionId ||
            latestDraft.Id != revision.BaseDraftVersionId ||
            latestDraft.OfferVersionId != contract.OfferVersionId)
        {
            throw new BusinessRuleException(
                "This contract changed after the revision was loaded. Reload the current draft and try again.");
        }

        return latestDraft;
    }

    private async Task EnsureBothPartiesApprovedAsync(Contract contract, ContractRevision revision)
    {
        var consents = await _context.ContractRevisionConsents
            .AsNoTracking()
            .Where(c => c.RevisionId == revision.Id)
            .ToListAsync();

        var hasInvestorApproval = consents.Any(c =>
            c.PartyUserId == contract.InvestorId &&
            c.Decision == ContractRevisionConsentDecisions.Approved);
        var hasLandlordApproval = consents.Any(c =>
            c.PartyUserId == contract.LandlordId &&
            c.Decision == ContractRevisionConsentDecisions.Approved);

        if (!hasInvestorApproval || !hasLandlordApproval)
            throw new BusinessRuleException(
                "Both the investor and the landlord must approve the revised terms before admin review.");
    }

    private async Task EnsureActiveReservationAsync(Contract contract, DateTime now)
    {
        var reservation = await _context.GridCapacityReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContractId == contract.Id && r.ReleasedAt == null);

        if (reservation is null)
            throw new BusinessRuleException(
                "This contract has no active grid reservation; the revision cannot proceed.");
        if (reservation.OfferId != contract.OfferId)
            throw new BusinessRuleException("The linked grid reservation does not match this contract.");
        if (reservation.ExpiresAt is { } expiresAt && expiresAt <= now)
            throw new BusinessRuleException(
                "The grid reservation for this contract has expired; the revision cannot proceed.");
    }

    private async Task<ContractVersion?> GetLatestDraftAsync(int contractId)
    {
        return await _context.ContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == contractId && v.VersionKind == "Draft")
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();
    }

    private async Task<ContractVersionTermsDto> LoadCurrentTermsAsync(Contract contract)
    {
        if (contract.OfferVersionId is not { } offerVersionId)
            throw new BusinessRuleException("The current contract terms are unavailable.");

        var offerVersion = contract.OfferVersion;
        if (offerVersion is null || offerVersion.Id != offerVersionId)
        {
            offerVersion = await _context.OfferVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == offerVersionId)
                ?? throw new BusinessRuleException("The current contract terms are unavailable.");
        }

        return TermsFromOfferVersion(contract, offerVersion);
    }

    private static ContractVersionTermsDto TermsFromOfferVersion(Contract contract, OfferVersion version) =>
        new()
        {
            OfferVersionId = version.Id,
            LandId = contract.LandId,
            InvestorId = contract.InvestorId,
            LandlordId = contract.LandlordId,
            NoticePeriodDays = contract.NoticePeriodDays,
            LandlordSharePct = version.LandlordSharePct,
            DurationYears = version.DurationYears,
            StartDate = version.StartDate,
            InstallationCost = version.InstallationCost,
            SolarCellCapacityKw = version.SolarCellCapacityKw,
            ExpectedAnnualRevenue = version.ExpectedAnnualRevenue,
            EffectiveCostPerKw = version.EffectiveCostPerKw,
            PaybackPeriodMonths = version.PaybackPeriodMonths
        };

    private static ContractVersionTermsDto BuildProposedTerms(
        Contract contract,
        CreateContractRevisionRequest request) =>
        new()
        {
            OfferVersionId = null,
            LandId = contract.LandId,
            InvestorId = contract.InvestorId,
            LandlordId = contract.LandlordId,
            NoticePeriodDays = request.NoticePeriodDays ?? contract.NoticePeriodDays,
            LandlordSharePct = request.LandlordSharePct,
            DurationYears = request.DurationYears,
            StartDate = request.StartDate,
            InstallationCost = request.InstallationCost,
            SolarCellCapacityKw = request.SolarCellCapacityKw,
            ExpectedAnnualRevenue = request.ExpectedAnnualRevenue,
            EffectiveCostPerKw = request.EffectiveCostPerKw,
            PaybackPeriodMonths = request.PaybackPeriodMonths
        };

    private static bool TermsDiffer(ContractVersionTermsDto current, ContractVersionTermsDto proposed) =>
        current.LandlordSharePct != proposed.LandlordSharePct ||
        current.DurationYears != proposed.DurationYears ||
        current.StartDate != proposed.StartDate ||
        current.InstallationCost != proposed.InstallationCost ||
        current.SolarCellCapacityKw != proposed.SolarCellCapacityKw ||
        current.ExpectedAnnualRevenue != proposed.ExpectedAnnualRevenue ||
        current.EffectiveCostPerKw != proposed.EffectiveCostPerKw ||
        current.PaybackPeriodMonths != proposed.PaybackPeriodMonths ||
        current.NoticePeriodDays != proposed.NoticePeriodDays;

    private async Task<ContractRevision> LockRevisionAsync(int contractId, int revisionId)
    {
        return await _context.ContractRevisions
            .FromSqlInterpolated($"SELECT * FROM CONTRACT_REVISIONS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {revisionId} AND CONTRACT_ID = {contractId}")
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Contract revision not found.");
    }

    private async Task<bool> HasOpenRevisionAsync(int contractId) =>
        await _context.ContractRevisions
            .AsNoTracking()
            .AnyAsync(r => r.ContractId == contractId && r.ClosedAt == null);

    private async Task<(string Name, string Role)> GetActorSnapshotAsync(int userId)
    {
        var actor = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
            ?? throw new NotFoundException("User not found.");

        return (actor.FullName, actor.Role?.Name ?? _currentUser.Role ?? string.Empty);
    }

    private async Task SaveRevisionChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UX_CR_OPEN_CONTRACT"))
        {
            throw new BusinessRuleException("This contract already has an open revision.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UX_CRC_REVISION_PARTY"))
        {
            throw new BusinessRuleException("This party has already responded to this revision.");
        }
    }

    private async Task<ContractRevisionDto> GetRevisionDtoByIdAsync(int contractId, int revisionId)
    {
        var revision = await _context.ContractRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == revisionId && r.ContractId == contractId)
            ?? throw new NotFoundException("Contract revision not found.");

        var dtos = await ToRevisionDtosAsync([revision]);
        return dtos[0];
    }

    private async Task<List<ContractRevisionDto>> ToRevisionDtosAsync(
        List<ContractRevision> revisions)
    {
        if (revisions.Count == 0)
            return [];

        var revisionIds = revisions.Select(r => r.Id).ToList();
        var consents = await _context.ContractRevisionConsents
            .AsNoTracking()
            .Where(c => revisionIds.Contains(c.RevisionId))
            .OrderBy(c => c.Id)
            .ToListAsync();

        var draftIds = revisions
            .SelectMany(r => new[] { (int?)r.BaseDraftVersionId, r.NewDraftVersionId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var draftNumbers = await _context.ContractVersions
            .AsNoTracking()
            .Where(v => draftIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNumber);

        var offerVersionIds = revisions
            .Where(r => r.NewOfferVersionId is not null)
            .Select(r => r.NewOfferVersionId!.Value)
            .Distinct()
            .ToList();
        var offerVersionNumbers = await _context.OfferVersions
            .AsNoTracking()
            .Where(v => offerVersionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNumber);

        return revisions.Select(r => new ContractRevisionDto
        {
            Id = r.Id,
            ContractId = r.ContractId,
            Status = r.Status,
            BaseDraftVersionId = r.BaseDraftVersionId,
            BaseDraftVersionNumber = draftNumbers.GetValueOrDefault(r.BaseDraftVersionId),
            ProposedTerms = TryDeserializeTerms(r.ProposedTermsSnapshot),
            Reason = r.Reason,
            ProposedById = r.ProposedById,
            ProposedByName = r.ProposedByNameSnapshot,
            ProposedByRole = r.ProposedByRoleSnapshot,
            ProposedAt = r.ProposedAt,
            AdminReviewedById = r.AdminReviewedById,
            AdminReviewedByName = r.AdminReviewedByNameSnapshot,
            AdminReviewedByRole = r.AdminReviewedByRoleSnapshot,
            AdminReviewedAt = r.AdminReviewedAt,
            AdminDecision = r.AdminDecision,
            EscalationReason = r.EscalationReason,
            SuperAdminDecidedById = r.SuperAdminDecidedById,
            SuperAdminDecidedByName = r.SuperAdminDecidedByNameSnapshot,
            SuperAdminDecidedByRole = r.SuperAdminDecidedByRoleSnapshot,
            SuperAdminDecidedAt = r.SuperAdminDecidedAt,
            SuperAdminDecision = r.SuperAdminDecision,
            SuperAdminReason = r.SuperAdminReason,
            NewOfferVersionId = r.NewOfferVersionId,
            NewOfferVersionNumber = r.NewOfferVersionId is { } ovId
                ? offerVersionNumbers.GetValueOrDefault(ovId)
                : null,
            NewDraftVersionId = r.NewDraftVersionId,
            NewDraftVersionNumber = r.NewDraftVersionId is { } dvId
                ? draftNumbers.GetValueOrDefault(dvId)
                : null,
            ClosedAt = r.ClosedAt,
            Consents = consents
                .Where(c => c.RevisionId == r.Id)
                .Select(c => new ContractRevisionConsentDto
                {
                    Id = c.Id,
                    PartyUserId = c.PartyUserId,
                    PartyName = c.PartyNameSnapshot,
                    PartyRole = c.PartyRoleSnapshot,
                    Decision = c.Decision,
                    Reason = c.Reason,
                    CreatedAt = c.CreatedAt
                })
                .ToList()
        }).ToList();
    }

    private static ContractVersionTermsDto? TryDeserializeTerms(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ContractVersionTermsDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Appends one immutable contract-version row. A <c>Draft</c> row is a real draft revision
    /// (version 1, then 2, ...). An <c>Event</c> row preserves a review, signature, rejection
    /// or termination and points at the exact draft version it acted on. The caller owns the
    /// transaction and the subsequent SaveChangesAsync.
    /// </summary>
    private async Task<ContractVersion> RecordContractVersionAsync(
        Contract contract,
        string versionKind,
        string? eventType,
        string status,
        string? reason,
        DateTime now)
    {
        var actorId = _currentUser.Id;
        string? actorName = null;
        string? actorRole = null;

        if (actorId is { } id)
        {
            var actor = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (actor is not null)
            {
                actorName = actor.FullName;
                actorRole = actor.Role?.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(actorRole))
            actorRole = _currentUser.Role;

        var latestDraft = await _context.ContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == contract.Id && v.VersionKind == "Draft")
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        var isDraft = string.Equals(versionKind, "Draft", StringComparison.Ordinal);
        var versionNumber = isDraft ? (latestDraft?.VersionNumber ?? 0) + 1 : latestDraft?.VersionNumber ?? 1;
        var previousVersionId = isDraft ? latestDraft?.Id : null;
        var targetVersionId = isDraft ? null : latestDraft?.Id;

        var offerVersion = contract.OfferVersion;
        if (offerVersion is null && contract.OfferVersionId is { } offerVersionId)
        {
            offerVersion = await _context.OfferVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == offerVersionId);
        }

        var terms = new ContractVersionTermsDto
        {
            OfferVersionId = contract.OfferVersionId,
            LandId = contract.LandId,
            InvestorId = contract.InvestorId,
            LandlordId = contract.LandlordId,
            NoticePeriodDays = contract.NoticePeriodDays,
            LandlordSharePct = offerVersion?.LandlordSharePct,
            DurationYears = offerVersion?.DurationYears,
            StartDate = offerVersion?.StartDate,
            InstallationCost = offerVersion?.InstallationCost,
            SolarCellCapacityKw = offerVersion?.SolarCellCapacityKw,
            ExpectedAnnualRevenue = offerVersion?.ExpectedAnnualRevenue,
            EffectiveCostPerKw = offerVersion?.EffectiveCostPerKw,
            PaybackPeriodMonths = offerVersion?.PaybackPeriodMonths
        };

        var version = new ContractVersion
        {
            ContractId = contract.Id,
            VersionNumber = versionNumber,
            VersionKind = isDraft ? "Draft" : "Event",
            EventType = eventType,
            Status = status,
            PreviousVersionId = previousVersionId,
            TargetVersionId = targetVersionId,
            OfferId = contract.OfferId,
            OfferVersionId = contract.OfferVersionId,
            TermsSnapshot = JsonSerializer.Serialize(terms),
            Reason = reason,
            CreatedById = actorId,
            CreatedByNameSnapshot = actorName,
            CreatedByRoleSnapshot = actorRole,
            CreatedAt = now
        };
        _context.ContractVersions.Add(version);
        return version;
    }

    private bool IsAdmin => _currentUser.Role is RoleNames.Admin or RoleNames.SuperAdmin;

    private void EnsureAdmin(string message)
    {
        if (!IsAdmin)
            throw new ForbiddenException(message);
    }

    private int RequireUserId() =>
        _currentUser.Id ?? throw new ForbiddenException("Authentication is required.");

    private async Task<Contract> LoadContractAsync(int id)
    {
        return await _context.Contracts
            .Include(c => c.Land)
            .Include(c => c.Investor)
            .Include(c => c.Landlord)
            .Include(c => c.OfferVersion)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException("Contract not found.");
    }

    // Step 6 M-1: take an update lock on the contract row and re-read the current persisted
    // state. Callers invoke this inside a transaction, so concurrent sign/review/terminate
    // requests for the same contract serialize on this lock and each acts on fresh state.
    private async Task<Contract> LockContractAsync(int id)
    {
        return await _context.Contracts
            .FromSqlInterpolated($"SELECT * FROM CONTRACTS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {id}")
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Contract not found.");
    }

    private void EnsureCanView(Contract contract)
    {
        var userId = RequireUserId();
        if (IsAdmin || contract.InvestorId == userId || contract.LandlordId == userId)
            return;
        throw new ForbiddenException("You are not authorized to view this contract.");
    }

    private static ContractStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        // Strict: only actual ContractStatus names are accepted (numbers are rejected).
        var match = Enum.GetNames<ContractStatus>()
            .FirstOrDefault(name => string.Equals(name, status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationFailedException($"Unknown contract status: {status}");

        return Enum.Parse<ContractStatus>(match);
    }

    private void AddReview(int contractId, int reviewerId, string decision, string? reason, DateTime now)
    {
        _context.ContractReviews.Add(new ContractReview
        {
            ContractId = contractId,
            ReviewerId = reviewerId,
            Decision = decision,
            Reason = reason,
            CreatedAt = now
        });
    }

    /// <summary>
    /// BR-O6 / state machine: when a contract becomes Active, every non-terminal competing
    /// offer on the same land is closed with a <c>CompetitorClosed</c> history row. Terminal
    /// statuses (already accepted/rejected/withdrawn/closed) are left untouched, and the
    /// contract's own offer is excluded.
    /// </summary>
    private async Task CloseCompetitorOffersAsync(Contract contract, int actorId, DateTime now)
    {
        var competitors = await _context.Offers
            .Where(o => o.LandId == contract.LandId
                        && o.Id != contract.OfferId
                        && o.Status != OfferStatus.Accepted
                        && o.Status != OfferStatus.RejectedByLandlord
                        && o.Status != OfferStatus.RejectedByInvestor
                        && o.Status != OfferStatus.RejectedByAdmin
                        && o.Status != OfferStatus.WithdrawnByInvestor
                        && o.Status != OfferStatus.Closed)
            .ToListAsync();

        foreach (var offer in competitors)
        {
            offer.Status = OfferStatus.Closed;
            offer.UpdatedAt = now;
            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.CompetitorClosed,
                ActorId = actorId,
                Amount = offer.CurrentAmount,
                Message = "Competing offer closed when the contract became active.",
                CreatedAt = now
            });
        }
    }

    /// <summary>
    /// Releases the reservation linked to this contract (never deletes it). No-op if the
    /// reservation is missing or already released.
    /// </summary>
    private async Task ReleaseReservationAsync(Contract contract, string reason, DateTime now)
    {
        var reservation = await _context.GridCapacityReservations
            .FirstOrDefaultAsync(r => r.ContractId == contract.Id);
        if (reservation is null || reservation.ReleasedAt is not null)
            return;

        reservation.ReleasedAt = now;
        reservation.ReleaseReason = reason.Length <= ReleaseReasonMaxLength
            ? reason
            : reason[..ReleaseReasonMaxLength];
    }

    /// <summary>
    /// Step 7 F1: clears the linked reservation's hold expiry when its contract is approved,
    /// so an Active contract's reservation no longer expires. No-op if the reservation is missing.
    /// </summary>
    private async Task ClearReservationExpiryAsync(Contract contract)
    {
        var reservation = await _context.GridCapacityReservations
            .FirstOrDefaultAsync(r => r.ContractId == contract.Id);
        if (reservation is null)
            return;

        reservation.ExpiresAt = null;
    }

    /// <summary>Returns the land to Verified and appends one land-status history row.</summary>
    private void ReturnLandToVerified(Land land, int actorId, string reason, DateTime now)
    {
        if (land.Status == LandStatus.Verified)
            return; // already Verified: avoid a duplicate history row

        land.Status = LandStatus.Verified;
        _context.LandStatusHistories.Add(new LandStatusHistory
        {
            LandId = land.Id,
            Status = LandStatus.Verified,
            ChangedById = actorId,
            Reason = reason,
            ChangedAt = now
        });
    }

    private async Task SaveContractChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ__CONTRACT__96E0508AA6D26B86"))
        {
            throw new BusinessRuleException(OfferContractConflictMessage);
        }
    }

    // Only the CONTRACTS unique index on OFFER_ID is translated to 409; any other database
    // failure keeps the generic error path.
    private static bool IsUniqueViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is SqlException sql &&
        (sql.Number == 2601 || sql.Number == 2627) &&
        sql.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);

    private async Task<ContractDto> ToDtoByIdAsync(int id) => ToDto(await LoadContractAsync(id));

    private static ContractDto ToDto(Contract contract)
    {
        var version = contract.OfferVersion;
        return new ContractDto
        {
            Id = contract.Id,
            OfferId = contract.OfferId,
            LandId = contract.LandId,
            LandLabel = BuildLandLabel(contract.Land),
            InvestorId = contract.InvestorId,
            InvestorName = contract.Investor?.FullName ?? string.Empty,
            LandlordId = contract.LandlordId,
            LandlordName = contract.Landlord?.FullName ?? string.Empty,
            Status = contract.Status.ToString(),
            OfferVersionId = contract.OfferVersionId,
            CriterionId = contract.CriterionId,
            CreatedAt = contract.CreatedAt,
            InvestorSignedAt = contract.InvestorSignedAt,
            LandlordSignedAt = contract.LandlordSignedAt,
            AdminReviewedAt = contract.AdminReviewedAt,
            AdminSignedAt = contract.AdminSignedAt,
            NoticePeriodDays = contract.NoticePeriodDays,
            LandlordSharePct = version?.LandlordSharePct,
            DurationYears = version?.DurationYears,
            StartDate = version?.StartDate,
            InstallationCost = version?.InstallationCost,
            SolarCellCapacityKw = version?.SolarCellCapacityKw,
            ExpectedAnnualRevenue = version?.ExpectedAnnualRevenue,
            EffectiveCostPerKw = version?.EffectiveCostPerKw,
            PaybackPeriodMonths = version?.PaybackPeriodMonths
        };
    }

    private static string BuildLandLabel(Land? land)
    {
        if (land is null)
            return string.Empty;

        var parts = new[] { land.Village, land.Basin, land.ParcelNumber }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" / ", parts);
    }
}
