using Microsoft.EntityFrameworkCore;
using NibrasWeb.Enums;
using NIBRAS.Models;

namespace NIBRAS.Models;

public partial class NebrasdbContext : DbContext
{
    public NebrasdbContext()
    {
    }

    public NebrasdbContext(DbContextOptions<NebrasdbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // No hard-coded fallback target: the database must be supplied through
            // ConnectionStrings:DefaultConnection and validated by DatabaseTargetGuard.
            throw new InvalidOperationException(
                "NebrasdbContext was created without configured options. Configure the database target through " +
                "ConnectionStrings:DefaultConnection (see DatabaseTargetGuard); no fallback database is used.");
        }
    }

    public virtual DbSet<Contract> Contracts { get; set; }
    public virtual DbSet<ContractReview> ContractReviews { get; set; }
    public virtual DbSet<ContractVersion> ContractVersions { get; set; }
    public virtual DbSet<ContractRevision> ContractRevisions { get; set; }
    public virtual DbSet<ContractRevisionConsent> ContractRevisionConsents { get; set; }
    public virtual DbSet<DeletionRequest> DeletionRequests { get; set; }
    public virtual DbSet<DocumentType> DocumentTypes { get; set; }
    public virtual DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
    public virtual DbSet<Grid> Grids { get; set; }
    public virtual DbSet<GridCapacityReservation> GridCapacityReservations { get; set; }
    public virtual DbSet<Governorate> Governorates { get; set; }
    public virtual DbSet<Land> Lands { get; set; }
    public virtual DbSet<LandAssessment> LandAssessments { get; set; }
    public virtual DbSet<LandChangeRequest> LandChangeRequests { get; set; }
    public virtual DbSet<LandCriterion> LandCriteria { get; set; }
    public virtual DbSet<LandDocument> LandDocuments { get; set; }
    public virtual DbSet<LandStatusHistory> LandStatusHistories { get; set; }
    public virtual DbSet<Offer> Offers { get; set; }
    public virtual DbSet<OfferNegotiationHistory> OfferNegotiationHistories { get; set; }
    public virtual DbSet<OfferVersion> OfferVersions { get; set; }
    public virtual DbSet<Region> Regions { get; set; }
    public virtual DbSet<Role> Roles { get; set; }
    public virtual DbSet<UserSecurityEvent> UserSecurityEvents { get; set; }
    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CONTRACT__3214EC2738767384");

            entity.ToTable("CONTRACTS");

            entity.HasIndex(e => e.OfferId, "UQ__CONTRACT__96E0508AA6D26B86").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AdminReviewedAt).HasColumnName("ADMIN_REVIEWED_AT");
            entity.Property(e => e.AdminSignedAt).HasColumnName("ADMIN_SIGNED_AT");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.InvestorId).HasColumnName("INVESTOR_ID");
            entity.Property(e => e.InvestorSignedAt).HasColumnName("INVESTOR_SIGNED_AT");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.LandlordId).HasColumnName("LANDLORD_ID");
            entity.Property(e => e.LandlordSignedAt).HasColumnName("LANDLORD_SIGNED_AT");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");

            entity.Property(e => e.Status)
                .HasColumnName("STATUS_ID");

            entity.Property(e => e.OfferVersionId).HasColumnName("OFFER_VERSION_ID");
            entity.Property(e => e.CriterionId).HasColumnName("CRITERION_ID");
            entity.Property(e => e.NoticePeriodDays)
                .HasDefaultValue(90)
                .HasColumnName("NOTICE_PERIOD_DAYS");
            entity.Property(e => e.CancellationRequestedById).HasColumnName("CANCELLATION_REQUESTED_BY_ID");
            entity.Property(e => e.CancellationRequestedAt).HasColumnName("CANCELLATION_REQUESTED_AT");
            entity.Property(e => e.CancellationEffectiveDate).HasColumnName("CANCELLATION_EFFECTIVE_DATE");
            entity.Property(e => e.InvestorPenaltyAmount)
                .HasColumnType("decimal(14, 3)")
                .HasColumnName("INVESTOR_PENALTY_AMOUNT");
            entity.Property(e => e.DisputeFlagged)
                .HasDefaultValue(false)
                .HasColumnName("DISPUTE_FLAGGED");
            entity.Property(e => e.CompensationAmount)
                .HasColumnType("decimal(14, 3)")
                .HasColumnName("COMPENSATION_AMOUNT");

            entity.HasOne(d => d.Investor).WithMany(p => p.ContractInvestors)
                .HasForeignKey(d => d.InvestorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_INVESTORS");

            entity.HasOne(d => d.Land).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_LANDS");

            entity.HasOne(d => d.Landlord).WithMany(p => p.ContractLandlords)
                .HasForeignKey(d => d.LandlordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_LANDLORDS");

            entity.HasOne(d => d.Offer).WithOne(p => p.Contract)
                .HasForeignKey<Contract>(d => d.OfferId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_OFFERS");

            entity.HasOne(d => d.OfferVersion)
                .WithMany()
                .HasForeignKey(d => d.OfferVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_OFFER_VERSION");

            entity.HasOne(d => d.CancellationRequestedBy)
                .WithMany(p => p.CancellationRequestedContracts)
                .HasForeignKey(d => d.CancellationRequestedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_CANCELLATION_REQUESTER");

            entity.HasOne(d => d.Criterion)
                .WithMany()
                .HasForeignKey(d => d.CriterionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_CRITERIA");
        });

        modelBuilder.Entity<ContractReview>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CONTRACT__3214EC273AB66E59");
            entity.ToTable("CONTRACT_REVIEWS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.Decision).HasMaxLength(20).HasColumnName("DECISION");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.ReviewerId).HasColumnName("REVIEWER_ID");

            entity.HasOne(d => d.Contract).WithMany(p => p.ContractReviews)
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CR_CONTRACTS");

            entity.HasOne(d => d.Reviewer).WithMany(p => p.ContractReviews)
                .HasForeignKey(d => d.ReviewerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CR_USERS");
        });

        modelBuilder.Entity<ContractVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CONTRACT_VERSIONS");
            entity.ToTable("CONTRACT_VERSIONS");

            // One immutable row per draft version; event rows may share a version number.
            entity.HasIndex(e => new { e.ContractId, e.VersionNumber })
                .HasDatabaseName("UX_CV_DRAFT_VERSION")
                .HasFilter("[VERSION_KIND] = 'Draft'")
                .IsUnique();
            entity.HasIndex(e => new { e.ContractId, e.CreatedAt })
                .HasDatabaseName("IX_CV_CONTRACT_CREATED");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.VersionNumber).HasColumnName("VERSION_NUMBER");
            entity.Property(e => e.VersionKind).HasMaxLength(20).HasColumnName("VERSION_KIND");
            entity.Property(e => e.EventType).HasMaxLength(30).HasColumnName("EVENT_TYPE");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("STATUS");
            entity.Property(e => e.PreviousVersionId).HasColumnName("PREVIOUS_VERSION_ID");
            entity.Property(e => e.TargetVersionId).HasColumnName("TARGET_VERSION_ID");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");
            entity.Property(e => e.OfferVersionId).HasColumnName("OFFER_VERSION_ID");
            entity.Property(e => e.TermsSnapshot).HasColumnType("nvarchar(max)").HasColumnName("TERMS_SNAPSHOT");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.CreatedById).HasColumnName("CREATED_BY_ID");
            entity.Property(e => e.CreatedByNameSnapshot).HasMaxLength(100).HasColumnName("CREATED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.CreatedByRoleSnapshot).HasMaxLength(50).HasColumnName("CREATED_BY_ROLE_SNAPSHOT");
            entity.Property(e => e.CreatedAt).HasColumnName("CREATED_AT");

            entity.HasOne<Contract>().WithMany()
                .HasForeignKey(e => e.ContractId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CV_CONTRACTS");

            entity.HasOne<Offer>().WithMany()
                .HasForeignKey(e => e.OfferId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CV_OFFERS");

            entity.HasOne<OfferVersion>().WithMany()
                .HasForeignKey(e => e.OfferVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CV_OFFER_VERSION");

            entity.HasOne<ContractVersion>().WithMany()
                .HasForeignKey(e => e.PreviousVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CV_PREVIOUS");

            entity.HasOne<ContractVersion>().WithMany()
                .HasForeignKey(e => e.TargetVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CV_TARGET");
        });

        modelBuilder.Entity<ContractRevision>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_CONTRACT_REVISIONS");
            entity.ToTable("CONTRACT_REVISIONS");

            // At most one open revision per contract. Terminal rows have CLOSED_AT set.
            entity.HasIndex(e => e.ContractId)
                .HasDatabaseName("UX_CR_OPEN_CONTRACT")
                .HasFilter("[CLOSED_AT] IS NULL")
                .IsUnique();
            entity.HasIndex(e => new { e.ContractId, e.ProposedAt })
                .HasDatabaseName("IX_CR_CONTRACT_PROPOSED");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.BaseDraftVersionId).HasColumnName("BASE_DRAFT_VERSION_ID");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("STATUS");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.ProposedTermsSnapshot).HasColumnType("nvarchar(max)").HasColumnName("PROPOSED_TERMS_SNAPSHOT");
            entity.Property(e => e.ProposedById).HasColumnName("PROPOSED_BY_ID");
            entity.Property(e => e.ProposedByNameSnapshot).HasMaxLength(100).HasColumnName("PROPOSED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.ProposedByRoleSnapshot).HasMaxLength(50).HasColumnName("PROPOSED_BY_ROLE_SNAPSHOT");
            entity.Property(e => e.ProposedAt).HasColumnName("PROPOSED_AT");
            entity.Property(e => e.AdminReviewedById).HasColumnName("ADMIN_REVIEWED_BY_ID");
            entity.Property(e => e.AdminReviewedByNameSnapshot).HasMaxLength(100).HasColumnName("ADMIN_REVIEWED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.AdminReviewedByRoleSnapshot).HasMaxLength(50).HasColumnName("ADMIN_REVIEWED_BY_ROLE_SNAPSHOT");
            entity.Property(e => e.AdminReviewedAt).HasColumnName("ADMIN_REVIEWED_AT");
            entity.Property(e => e.AdminDecision).HasMaxLength(30).HasColumnName("ADMIN_DECISION");
            entity.Property(e => e.EscalationReason).HasMaxLength(500).HasColumnName("ESCALATION_REASON");
            entity.Property(e => e.SuperAdminDecidedById).HasColumnName("SUPERADMIN_DECIDED_BY_ID");
            entity.Property(e => e.SuperAdminDecidedByNameSnapshot).HasMaxLength(100).HasColumnName("SUPERADMIN_DECIDED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.SuperAdminDecidedByRoleSnapshot).HasMaxLength(50).HasColumnName("SUPERADMIN_DECIDED_BY_ROLE_SNAPSHOT");
            entity.Property(e => e.SuperAdminDecidedAt).HasColumnName("SUPERADMIN_DECIDED_AT");
            entity.Property(e => e.SuperAdminDecision).HasMaxLength(30).HasColumnName("SUPERADMIN_DECISION");
            entity.Property(e => e.SuperAdminReason).HasMaxLength(500).HasColumnName("SUPERADMIN_REASON");
            entity.Property(e => e.NewOfferVersionId).HasColumnName("NEW_OFFER_VERSION_ID");
            entity.Property(e => e.NewDraftVersionId).HasColumnName("NEW_DRAFT_VERSION_ID");
            entity.Property(e => e.ClosedAt).HasColumnName("CLOSED_AT");

            entity.HasOne<Contract>().WithMany()
                .HasForeignKey(e => e.ContractId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_CONTRACTS");

            entity.HasOne<ContractVersion>().WithMany()
                .HasForeignKey(e => e.BaseDraftVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_BASE_DRAFT");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.ProposedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_PROPOSED_BY");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.AdminReviewedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_ADMIN_REVIEWED_BY");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.SuperAdminDecidedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_SUPERADMIN_DECIDED_BY");

            entity.HasOne<OfferVersion>().WithMany()
                .HasForeignKey(e => e.NewOfferVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_NEW_OFFER_VERSION");

            entity.HasOne<ContractVersion>().WithMany()
                .HasForeignKey(e => e.NewDraftVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_NEW_DRAFT");
        });

        modelBuilder.Entity<ContractRevisionConsent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_CONTRACT_REVISION_CONSENTS");
            entity.ToTable("CONTRACT_REVISION_CONSENTS");

            entity.HasIndex(e => new { e.RevisionId, e.PartyUserId })
                .HasDatabaseName("UX_CRC_REVISION_PARTY")
                .IsUnique();
            entity.HasIndex(e => e.RevisionId)
                .HasDatabaseName("IX_CRC_REVISION");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.RevisionId).HasColumnName("REVISION_ID");
            entity.Property(e => e.PartyUserId).HasColumnName("PARTY_USER_ID");
            entity.Property(e => e.PartyNameSnapshot).HasMaxLength(100).HasColumnName("PARTY_NAME_SNAPSHOT");
            entity.Property(e => e.PartyRoleSnapshot).HasMaxLength(50).HasColumnName("PARTY_ROLE_SNAPSHOT");
            entity.Property(e => e.Decision).HasMaxLength(20).HasColumnName("DECISION");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.CreatedAt).HasColumnName("CREATED_AT");

            entity.HasOne(d => d.Revision).WithMany(p => p.Consents)
                .HasForeignKey(d => d.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_CONSENT_REVISION");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(d => d.PartyUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CONTRACT_REV_CONSENT_PARTY");
        });

        modelBuilder.Entity<DeletionRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__DELETION__3214EC278A03ACC4");
            entity.ToTable("DELETION_REQUESTS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AdminComment).HasMaxLength(500).HasColumnName("ADMIN_COMMENT");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.RequestedById).HasColumnName("REQUESTED_BY_ID");
            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Pending").HasColumnName("STATUS");

            entity.HasOne(d => d.Land).WithMany(p => p.DeletionRequests)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DELETION_LANDS");

            entity.HasOne(d => d.RequestedBy).WithMany(p => p.DeletionRequests)
                .HasForeignKey(d => d.RequestedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DELETION_USERS");
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__DOCUMENT__3214EC27579B58DC");
            entity.ToTable("DOCUMENT_TYPES");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("NAME");
            entity.Property(e => e.IsRequiredForVerification)
                .HasDefaultValue(false)
                .HasColumnName("IS_REQUIRED_FOR_VERIFICATION");
        });

        modelBuilder.Entity<Grid>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__GRIDS__3214EC27C5B24747");
            entity.ToTable("GRIDS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CapacityMw).HasColumnType("decimal(10, 2)").HasColumnName("CAPACITY_MW");
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("NAME");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Active").HasColumnName("STATUS");

            entity.HasOne(d => d.Region).WithMany(p => p.Grids)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GRIDS_REGIONS");
        });

        modelBuilder.Entity<GridCapacityReservation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__GRID_CAP__3214EC27C682FA92");
            entity.ToTable("GRID_CAPACITY_RESERVATIONS");
            entity.HasIndex(e => e.OfferId, "UQ__GRID_CAP__OFFER_ID").IsUnique();
            entity.HasIndex(e => e.ContractId, "UQ_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID")
                .IsUnique()
                .HasFilter("[CONTRACT_ID] IS NOT NULL");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");
            entity.Property(e => e.ReservationType).HasMaxLength(20).HasColumnName("RESERVATION_TYPE");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.ReleasedAt).HasColumnName("RELEASED_AT");
            entity.Property(e => e.ReleaseReason).HasMaxLength(300).HasColumnName("RELEASE_REASON");
            entity.Property(e => e.ExpiresAt).HasColumnName("EXPIRES_AT");
            entity.Property(e => e.GridId).HasColumnName("GRID_ID");
            entity.Property(e => e.ReservedMw).HasColumnType("decimal(10, 2)").HasColumnName("RESERVED_MW");

            entity.HasOne(d => d.Contract).WithOne(p => p.GridCapacityReservation)
                .HasForeignKey<GridCapacityReservation>(d => d.ContractId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GCR_CONTRACTS");

            entity.HasOne(d => d.Grid).WithMany(p => p.GridCapacityReservations)
                .HasForeignKey(d => d.GridId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GCR_GRIDS");

            entity.HasOne(d => d.Offer).WithOne(p => p.GridCapacityReservation)
                .HasForeignKey<GridCapacityReservation>(d => d.OfferId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GCR_OFFERS");
        });

        modelBuilder.Entity<Governorate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_GOVERNORATES");
            entity.ToTable("GOVERNORATES");
            entity.HasIndex(e => e.Code, "UQ_GOVERNORATES_CODE").IsUnique();
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Code).HasMaxLength(10).HasColumnName("CODE");
            entity.Property(e => e.NameEn).HasMaxLength(100).HasColumnName("NAME_EN");
            entity.Property(e => e.NameAr).HasMaxLength(100).HasColumnName("NAME_AR");
        });

        modelBuilder.Entity<Land>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LANDS__3214EC278FA5F43E");
            entity.ToTable("LANDS");
            // Legacy region-based uniqueness applies only to records that still carry a
            // region; records created through the governorate flow are keyed on the
            // governorate instead. SQL Server treats NULL as a value in a unique index,
            // so without the IS NOT NULL filters the legacy (NULL-governorate) rows and
            // the new (NULL-region) rows would each be mutually constrained.
            entity.HasIndex(e => new { e.LandNumber, e.RegionId }, "UQ_LAND_NUMBER_REGION")
                .IsUnique()
                .HasFilter("[REGION_ID] IS NOT NULL");
            entity.HasIndex(e => new { e.RegionId, e.Village, e.Basin, e.ParcelNumber }, "UQ_LAND_LOCATION")
                .IsUnique()
                .HasFilter("[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL");
            entity.HasIndex(e => new { e.GovernorateId, e.Village, e.Basin, e.ParcelNumber }, "UQ_LAND_LOCATION_GOV")
                .IsUnique()
                .HasFilter("[IS_DELETED] = 0 AND [GOVERNORATE_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL");
            entity.HasIndex(e => new { e.LandNumber, e.GovernorateId }, "UQ_LAND_NUMBER_GOV")
                .IsUnique()
                .HasFilter("[GOVERNORATE_ID] IS NOT NULL");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AreaDonum).HasColumnType("decimal(10, 2)").HasColumnName("AREA_DONUM");
            entity.Property(e => e.DataVerifiedByAdmin).HasColumnName("DATA_VERIFIED_BY_ADMIN");
            entity.Property(e => e.DistanceToGridKm).HasColumnType("decimal(10, 2)").HasColumnName("DISTANCE_TO_GRID_KM");
            entity.Property(e => e.ElevationM).HasColumnType("decimal(10, 2)").HasColumnName("ELEVATION_M");
            entity.Property(e => e.IsDeleted).HasColumnName("IS_DELETED");
            entity.Property(e => e.IsHiddenFromInvestors)
                .HasDefaultValue(false)
                .HasColumnName("IS_HIDDEN_FROM_INVESTORS");
            entity.Property(e => e.LandNumber).HasMaxLength(50).HasColumnName("LAND_NUMBER");
            entity.Property(e => e.Village).HasMaxLength(100).HasColumnName("VILLAGE");
            entity.Property(e => e.Basin).HasMaxLength(100).HasColumnName("BASIN");
            entity.Property(e => e.ParcelNumber).HasMaxLength(100).HasColumnName("PARCEL_NUMBER");
            entity.Property(e => e.Status).HasColumnName("LAND_STATUS_ID");
            entity.Property(e => e.LandlordId).HasColumnName("LANDLORD_ID");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.GridId).HasColumnName("GRID_ID");
            entity.Property(e => e.GovernorateId).HasColumnName("GOVERNORATE_ID");
            entity.Property(e => e.Description).HasMaxLength(1000).HasColumnName("DESCRIPTION");
            entity.Property(e => e.SlopePercentage).HasColumnType("decimal(5, 2)").HasColumnName("SLOPE_PERCENTAGE");
            entity.Property(e => e.SolarIrradiance).HasColumnType("decimal(10, 2)").HasColumnName("SOLAR_IRRADIANCE");
            entity.Property(e => e.DocumentStorageLocation).HasMaxLength(500).HasColumnName("DOCUMENT_STORAGE_LOCATION");
            entity.Property(e => e.VerifiedAgainstCriterionId).HasColumnName("VERIFIED_AGAINST_CRITERION_ID");

            entity.HasOne(d => d.Landlord).WithMany(p => p.Lands)
                .HasForeignKey(d => d.LandlordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_LANDLORDS");

            entity.HasOne(d => d.Region).WithMany(p => p.Lands)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_REGIONS");

            entity.HasOne(d => d.Grid).WithMany(p => p.Lands)
                .HasForeignKey(d => d.GridId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_GRIDS");

            entity.HasOne(d => d.Governorate).WithMany(p => p.Lands)
                .HasForeignKey(d => d.GovernorateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_GOVERNORATES");

            entity.HasOne(d => d.VerifiedAgainstCriterion)
                .WithMany()
                .HasForeignKey(d => d.VerifiedAgainstCriterionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_VERIFIED_CRITERION");
        });

        modelBuilder.Entity<LandAssessment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_LAND_ASSESSMENTS");
            entity.Property(e => e.CriterionId).HasColumnName("CRITERION_ID");
            entity.Property(e => e.AreaDonumSnapshot).HasColumnType("decimal(10, 2)").HasColumnName("AREA_DONUM_SNAPSHOT");
            entity.HasOne(e => e.Criterion).WithMany().HasForeignKey(e => e.CriterionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_LA_CRITERIA");
            entity.ToTable("LAND_ASSESSMENTS");
            entity.HasIndex(e => e.LandId, "IX_LAND_ASSESSMENTS_LAND_ID");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.AssessedById).HasColumnName("ASSESSED_BY_ID");
            entity.Property(e => e.AssessedAt).HasColumnName("ASSESSED_AT");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("NOTE");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.GridId).HasColumnName("GRID_ID");
            entity.Property(e => e.SlopePercentage).HasColumnType("decimal(5, 2)").HasColumnName("SLOPE_PERCENTAGE");
            entity.Property(e => e.DistanceToGridKm).HasColumnType("decimal(10, 2)").HasColumnName("DISTANCE_TO_GRID_KM");
            entity.Property(e => e.SolarIrradiance).HasColumnType("decimal(10, 2)").HasColumnName("SOLAR_IRRADIANCE");
            entity.Property(e => e.ElevationM).HasColumnType("decimal(10, 2)").HasColumnName("ELEVATION_M");

            entity.HasOne(d => d.Land).WithMany(p => p.LandAssessments)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LA_LANDS");

            entity.HasOne(d => d.AssessedBy).WithMany()
                .HasForeignKey(d => d.AssessedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LA_USERS");
        });

        modelBuilder.Entity<LandChangeRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_LAND_CHANGE_REQUESTS");
            entity.ToTable("LAND_CHANGE_REQUESTS");
            entity.HasIndex(e => e.LandId, "IX_LAND_CHANGE_REQUESTS_LAND_ID");
            entity.HasIndex(e => e.Status, "IX_LAND_CHANGE_REQUESTS_STATUS");
            // At most one change request may await a decision per land. The filter uses the
            // stored string status verbatim ("Pending"), so the index is the database-level
            // guarantee, not just the application pre-check; a losing concurrent insert is
            // translated to a 409 by SubmitChangeRequestAsync.
            entity.HasIndex(e => e.LandId, "UX_LAND_CHANGE_REQUESTS_PENDING_LAND")
                .IsUnique()
                .HasFilter("[STATUS] = N'Pending'");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.RequestedById).HasColumnName("REQUESTED_BY_ID");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending").HasColumnName("STATUS");
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("(getdate())").HasColumnName("REQUESTED_AT");
            entity.Property(e => e.ReviewedById).HasColumnName("REVIEWED_BY_ID");
            entity.Property(e => e.ReviewedAt).HasColumnName("REVIEWED_AT");
            entity.Property(e => e.DecisionReason).HasMaxLength(500).HasColumnName("DECISION_REASON");
            entity.Property(e => e.OldSnapshot).HasColumnName("OLD_SNAPSHOT");
            entity.Property(e => e.NewSnapshot).HasColumnName("NEW_SNAPSHOT");
            entity.Property(e => e.SupportingDocuments).HasColumnName("SUPPORTING_DOCUMENTS");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("ROW_VERSION");

            entity.HasOne(d => d.Land).WithMany(p => p.LandChangeRequests)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LCR_LANDS");

            entity.HasOne(d => d.RequestedBy).WithMany()
                .HasForeignKey(d => d.RequestedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LCR_REQUESTER");

            entity.HasOne(d => d.ReviewedBy).WithMany()
                .HasForeignKey(d => d.ReviewedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LCR_REVIEWER");
        });

        modelBuilder.Entity<LandCriterion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_CRI__3214EC276031D9CC");
            entity.ToTable("LAND_CRITERIA");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.MaxGridDistanceKm).HasColumnType("decimal(10, 2)").HasColumnName("MAX_GRID_DISTANCE_KM");
            entity.Property(e => e.MaxSlopePct).HasColumnType("decimal(5, 2)").HasColumnName("MAX_SLOPE_PCT");
            entity.Property(e => e.MinAreaDonum).HasColumnType("decimal(10, 2)").HasColumnName("MIN_AREA_DONUM");
            entity.Property(e => e.MinElevationM).HasColumnType("decimal(10, 2)").HasColumnName("MIN_ELEVATION_M");
            entity.Property(e => e.MinSolarIrradiance).HasColumnType("decimal(10, 2)").HasColumnName("MIN_SOLAR_IRRADIANCE");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(getdate())").HasColumnName("UPDATED_AT");
            entity.Property(e => e.UpdatedById).HasColumnName("UPDATED_BY_ID");
            entity.Property(e => e.PreviousCriterionId).HasColumnName("PREVIOUS_CRITERION_ID");
            entity.Property(e => e.UpdatedByNameSnapshot).HasMaxLength(100).HasColumnName("UPDATED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.UpdatedByRoleSnapshot).HasMaxLength(50).HasColumnName("UPDATED_BY_ROLE_SNAPSHOT");
            entity.HasOne<LandCriterion>().WithMany().HasForeignKey(e => e.PreviousCriterionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_LC_PREVIOUS");


            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.LandCriteria)
                .HasForeignKey(d => d.UpdatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LC_USERS");
        });

        modelBuilder.Entity<LandDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_DOC__3214EC27A38FC9A9");
            entity.ToTable("LAND_DOCUMENTS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.DocumentTypeId).HasColumnName("DOCUMENT_TYPE_ID");
            entity.Property(e => e.FilePath).HasMaxLength(500).HasColumnName("FILE_PATH");
            entity.Property(e => e.OriginalFileName).HasMaxLength(255).HasColumnName("ORIGINAL_FILE_NAME");
            entity.Property(e => e.ContentType).HasMaxLength(100).HasColumnName("CONTENT_TYPE");
            entity.Property(e => e.FileSizeBytes).HasColumnName("FILE_SIZE_BYTES");
            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("SHA256");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.ReviewedById).HasColumnName("REVIEWED_BY_ID");
            entity.Property(e => e.ReviewedAt).HasColumnName("REVIEWED_AT");
            entity.Property(e => e.ReviewNote).HasMaxLength(500).HasColumnName("REVIEW_NOTE");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending").HasColumnName("STATUS");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(getdate())").HasColumnName("UPLOADED_AT");
            entity.Property(e => e.Version).HasDefaultValue(1).HasColumnName("VERSION");

            entity.HasOne(d => d.DocumentType).WithMany(p => p.LandDocuments)
                .HasForeignKey(d => d.DocumentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LD_DOCTYPES");

            entity.HasOne(d => d.Land).WithMany(p => p.LandDocuments)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LD_LANDS");

            entity.HasOne(d => d.ReviewedBy).WithMany()
                .HasForeignKey(d => d.ReviewedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LD_REVIEWED_BY");
        });

        modelBuilder.Entity<LandStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_STA__3214EC27F466086B");
            entity.ToTable("LAND_STATUS_HISTORY");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(getdate())").HasColumnName("CHANGED_AT");
            entity.Property(e => e.ChangedById).HasColumnName("CHANGED_BY_ID");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("REASON");
            entity.Property(e => e.Status).HasColumnName("STATUS_ID");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.LandStatusHistories)
                .HasForeignKey(d => d.ChangedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSH_USERS");

            entity.HasOne(d => d.Land).WithMany(p => p.LandStatusHistories)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSH_LANDS");
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_EMAIL_VERIFICATION_TOKENS");
            entity.ToTable("EMAIL_VERIFICATION_TOKENS");
            // The hash is the only way a token row can be looked up, so it is unique: two
            // different tokens can never share a hash, and the plaintext token is never stored.
            entity.HasIndex(e => e.TokenHash, "UQ_EMAIL_VERIFICATION_TOKENS_HASH").IsUnique();
            // At most one live token per account and purpose. The issuance retires every earlier
            // token of that purpose (consumed or expired) in the same transaction before it
            // inserts the new one, so the slot is always free. A filtered index may not use
            // GETDATE(), so the expiry of a token is handled by that retirement and not by the
            // filter.
            entity.HasIndex(e => new { e.UserId, e.Purpose }, "UX_EMAIL_VERIFICATION_TOKENS_LIVE")
                .IsUnique()
                .HasFilter("[CONSUMED_AT] IS NULL");
            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ExpiresAt },
                "IX_EMAIL_VERIFICATION_TOKENS_USER");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.UserId).HasColumnName("USER_ID");
            // Enum-like values follow the existing schema convention: a fixed nvarchar(40)
            // column whose value is validated in the application against
            // Enums\EmailVerificationPurposes / Enums\UserSecurityEventTypes. The schema carries
            // no CHECK constraint for them, exactly like LAND_CHANGE_REQUESTS.STATUS and
            // LAND_DOCUMENTS.STATUS.
            entity.Property(e => e.Purpose).HasMaxLength(40).HasColumnName("PURPOSE");
            entity.Property(e => e.TokenHash).HasColumnType("varbinary(32)").HasColumnName("TOKEN_HASH");
            entity.Property(e => e.PreviousEmail).HasMaxLength(255).HasColumnName("PREVIOUS_EMAIL");
            entity.Property(e => e.ExpiresAt).HasColumnName("EXPIRES_AT");
            entity.Property(e => e.ConsumedAt).HasColumnName("CONSUMED_AT");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.RequestIp).HasMaxLength(45).HasColumnName("REQUEST_IP");

            entity.HasOne(d => d.User).WithMany(p => p.EmailVerificationTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EVT_USERS");
        });

        modelBuilder.Entity<UserSecurityEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_USER_SECURITY_EVENTS");
            entity.ToTable("USER_SECURITY_EVENTS");
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_USER_SECURITY_EVENTS_USER");
            entity.HasIndex(e => new { e.EventType, e.CreatedAt }, "IX_USER_SECURITY_EVENTS_TYPE");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.UserId).HasColumnName("USER_ID");
            entity.Property(e => e.EventType).HasMaxLength(40).HasColumnName("EVENT_TYPE");
            entity.Property(e => e.ActorId).HasColumnName("ACTOR_ID");
            entity.Property(e => e.ActorNameSnapshot).HasMaxLength(100).HasColumnName("ACTOR_NAME_SNAPSHOT");
            entity.Property(e => e.ActorRoleSnapshot).HasMaxLength(50).HasColumnName("ACTOR_ROLE_SNAPSHOT");
            entity.Property(e => e.Ip).HasMaxLength(45).HasColumnName("IP");
            entity.Property(e => e.Details).HasMaxLength(500).HasColumnName("DETAILS");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");

            // Two relationships to USERS (the account and the actor) are configured explicitly
            // so the foreign keys are unambiguous.
            entity.HasOne(d => d.User).WithMany(p => p.UserSecurityEvents)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_USE_USERS");

            entity.HasOne(d => d.Actor).WithMany()
                .HasForeignKey(d => d.ActorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_USE_ACTOR");
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFERS__3214EC277FE0E225");
            entity.ToTable("OFFERS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.InvestorId).HasColumnName("INVESTOR_ID");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.RequiredCapacityMw).HasColumnType("decimal(10, 2)").HasColumnName("REQUIRED_CAPACITY_MW");
            entity.Property(e => e.Status).HasColumnName("STATUS_ID");
            entity.Property(e => e.ConnectionMechanism).HasColumnName("CONNECTION_MECHANISM");
            entity.Property(e => e.CurrentAmount).HasColumnType("decimal(14, 3)").HasColumnName("CURRENT_AMOUNT");
            entity.Property(e => e.LastProposedById).HasColumnName("LAST_PROPOSED_BY_ID");
            entity.Property(e => e.UpdatedAt).HasColumnName("UPDATED_AT");
            entity.Property(e => e.AcceptedVersionId).HasColumnName("ACCEPTED_VERSION_ID");
            entity.Property(e => e.RelatedToRejectedOfferId).HasColumnName("RELATED_TO_REJECTED_OFFER_ID");

            // Keep the pre-existing FK index on LAND_ID explicit so the F-2 migration adds only
            // the new unique index and does not silently drop this (EF would treat it as redundant
            // because the composite index below is prefixed by LAND_ID).
            entity.HasIndex(e => e.LandId, "IX_OFFERS_LAND_ID");

            // BR-O2 (Step 5 F-2): at most one ACTIVE offer per (land, investor). Active statuses
            // are PendingLandlordResponse(1), PendingInvestorResponse(2), PendingAdminApproval(3),
            // Accepted(4). Terminal statuses (RejectedBy*, WithdrawnByInvestor, Closed) are
            // excluded by the filter, so the same investor may bid again once an offer leaves the
            // active set, and different investors/lands are unaffected.
            entity.HasIndex(e => new { e.LandId, e.InvestorId }, "UX_OFFERS_ACTIVE_INVESTOR_LAND")
                .IsUnique()
                .HasFilter("[STATUS_ID] IN (1, 2, 3, 4)");

            entity.HasOne(d => d.Investor).WithMany(p => p.Offers)
                .HasForeignKey(d => d.InvestorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_INVESTORS");

            entity.HasOne(d => d.Land).WithMany(p => p.Offers)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_LANDS");

            entity.HasOne(d => d.AcceptedVersion)
                .WithMany()
                .HasForeignKey(d => d.AcceptedVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_ACCEPTED_VERSION");

            entity.HasOne(d => d.RelatedToRejectedOffer)
                .WithMany()
                .HasForeignKey(d => d.RelatedToRejectedOfferId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_RELATED_REJECTED");
        });

        modelBuilder.Entity<OfferVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFER_VE__3214EC27145811E1");
            entity.ToTable("OFFER_VERSIONS");
            entity.HasIndex(e => e.OfferId, "UX_OFFER_VERSIONS_CURRENT_OFFER")
                .IsUnique()
                .HasFilter("[IS_CURRENT] = 1");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");
            entity.Property(e => e.CreatedById).HasColumnName("CREATED_BY_ID");
            entity.Property(e => e.DurationYears).HasColumnName("DURATION_YEARS");
            entity.Property(e => e.InstallationCost).HasColumnType("decimal(12, 2)").HasColumnName("INSTALLATION_COST");
            entity.Property(e => e.LandlordSharePct).HasColumnType("decimal(5, 2)").HasColumnName("LANDLORD_SHARE_PCT");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");
            entity.Property(e => e.RejectionReason).HasMaxLength(500).HasColumnName("REJECTION_REASON");
            entity.Property(e => e.StartDate).HasColumnName("START_DATE");
            entity.Property(e => e.VersionNumber).HasColumnName("VERSION_NUMBER");
            entity.Property(e => e.SolarCellCapacityKw).HasColumnType("decimal(10, 2)").HasColumnName("SOLAR_CELL_CAPACITY_KW");
            entity.Property(e => e.ExpectedAnnualRevenue).HasColumnType("decimal(14, 3)").HasColumnName("EXPECTED_ANNUAL_REVENUE");
            entity.Property(e => e.EffectiveCostPerKw).HasColumnType("decimal(10, 3)").HasColumnName("EFFECTIVE_COST_PER_KWH");
            entity.Property(e => e.PaybackPeriodMonths).HasColumnType("decimal(8, 2)").HasColumnName("PAYBACK_PERIOD_MONTHS");
            entity.Property(e => e.Message).HasMaxLength(500).HasColumnName("MESSAGE");
            entity.Property(e => e.IsCurrent).HasColumnName("IS_CURRENT");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.OfferVersions)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OV_USERS");

            entity.HasOne(d => d.Offer).WithMany(p => p.OfferVersions)
                .HasForeignKey(d => d.OfferId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OV_OFFERS");
        });

        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__REGIONS__3214EC27BEC00DCF");
            entity.ToTable("REGIONS");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.NameAr).HasMaxLength(100).HasColumnName("NAME_AR");
            entity.Property(e => e.NameEn).HasMaxLength(100).HasColumnName("NAME_EN");
            entity.Property(e => e.PeakSunHoursPerDay).HasColumnType("decimal(5, 2)").HasDefaultValue(5.5m).HasColumnName("PEAK_SUN_HOURS_PER_DAY");
            entity.Property(e => e.WheelingFeePerKwh).HasColumnType("decimal(10, 3)").HasDefaultValue(0m).HasColumnName("WHEELING_FEE_PER_KWH");
            entity.Property(e => e.LossPercentage).HasColumnType("decimal(5, 2)").HasDefaultValue(0m).HasColumnName("LOSS_PERCENTAGE");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__ROLES__3214EC276782D7D4");
            entity.ToTable("ROLES");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name).HasMaxLength(50).HasColumnName("NAME");
        });

        modelBuilder.Entity<OfferNegotiationHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFER_NEG_HISTORY");
            entity.ToTable("OFFER_NEGOTIATION_HISTORY");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");
            entity.Property(e => e.ActionType).HasColumnName("ACTION_TYPE");
            entity.Property(e => e.ActorId).HasColumnName("ACTOR_ID");
            entity.Property(e => e.Amount).HasColumnType("decimal(14, 3)").HasColumnName("AMOUNT");
            entity.Property(e => e.Message).HasMaxLength(500).HasColumnName("MESSAGE");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");

            entity.HasOne(d => d.Actor).WithMany(p => p.OfferNegotiationHistories)
                .HasForeignKey(d => d.ActorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ONH_USERS");

            entity.HasOne(d => d.Offer).WithMany(p => p.OfferNegotiationHistories)
                .HasForeignKey(d => d.OfferId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ONH_OFFERS");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__USERS__3214EC27D0F84BA1");
            entity.ToTable("USERS");
            entity.HasIndex(e => e.Email, "UQ__USERS__161CF724F5C148C0").IsUnique();
            entity.HasIndex(e => e.Phone, "UQ__USERS__D4FA0A2658412415").IsUnique();
            // One account may hold at most one pending address, and one address may be pending
            // for at most one account. The filter keeps every existing row out of the index
            // (PENDING_EMAIL is NULL for all of them), so the index is created without touching
            // a single row. The service writes and clears PENDING_EMAIL inside the token
            // transaction, so the index simply follows the row.
            entity.HasIndex(e => e.PendingEmail, "UX_USERS_PENDING_EMAIL")
                .IsUnique()
                .HasFilter("[PENDING_EMAIL] IS NOT NULL");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Email).HasMaxLength(255).HasColumnName("EMAIL");
            entity.Property(e => e.FullName).HasMaxLength(100).HasColumnName("FULL_NAME");
            entity.Property(e => e.IsDeleted).HasColumnName("IS_DELETED");
            entity.Property(e => e.IsApproved).HasDefaultValue(false).HasColumnName("IS_APPROVED");
            entity.Property(e => e.ApprovedById).HasColumnName("APPROVED_BY_ID");
            entity.Property(e => e.ApprovedByNameSnapshot).HasMaxLength(100).HasColumnName("APPROVED_BY_NAME_SNAPSHOT");
            entity.Property(e => e.ApprovedByRoleSnapshot).HasMaxLength(50).HasColumnName("APPROVED_BY_ROLE_SNAPSHOT");
            entity.Property(e => e.ApprovedAt).HasColumnName("APPROVED_AT");
            entity.Property(e => e.CompanyName).HasMaxLength(200).HasColumnName("COMPANY_NAME");
            entity.Property(e => e.CompanyRegistrationNumber).HasMaxLength(100).HasColumnName("COMPANY_REGISTRATION_NUMBER");
            entity.Property(e => e.PasswordHash).HasMaxLength(255).HasColumnName("PASSWORD_HASH");
            entity.Property(e => e.Phone).HasMaxLength(20).HasColumnName("PHONE");
            entity.Property(e => e.RoleId).HasColumnName("ROLE_ID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("CREATED_AT");

            // Email-ownership state. The column is nullable on purpose: NULL means "pre-existing
            // account, verified under the rules in force before the verification feature
            // existed", so no existing row needs a backfill and no value is fabricated for it.
            // Only an explicit FALSE blocks the account, and only a verification can set it.
            entity.Property(e => e.EmailVerified).HasColumnName("EMAIL_VERIFIED");
            entity.Property(e => e.EmailVerifiedAt).HasColumnName("EMAIL_VERIFIED_AT");
            entity.Property(e => e.PendingEmail).HasMaxLength(255).HasColumnName("PENDING_EMAIL");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_USERS_ROLES");

            });

        // === SEED DATA ===
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = RoleNames.Landlord },
            new Role { Id = 2, Name = RoleNames.Investor },
            new Role { Id = 3, Name = RoleNames.Admin },
            new Role { Id = 4, Name = RoleNames.SuperAdmin }
        );

        modelBuilder.Entity<DocumentType>().HasData(
            new DocumentType { Id = 1, Name = "TitleDeed", IsRequiredForVerification = true },
            new DocumentType { Id = 2, Name = "EncumbranceStatement", IsRequiredForVerification = true }
        );

        // The twelve official Jordanian governorates (ISO 3166-2:JO codes without the
        // "JO-" prefix). Fixed reference data: no region backfill and no dependency on
        // the pre-existing REGIONS rows.
        modelBuilder.Entity<Governorate>().HasData(
            new Governorate { Id = 1, Code = "AM", NameEn = "Amman", NameAr = "عمّان" },
            new Governorate { Id = 2, Code = "IR", NameEn = "Irbid", NameAr = "إربد" },
            new Governorate { Id = 3, Code = "AZ", NameEn = "Zarqa", NameAr = "الزرقاء" },
            new Governorate { Id = 4, Code = "BA", NameEn = "Balqa", NameAr = "البلقاء" },
            new Governorate { Id = 5, Code = "MA", NameEn = "Mafraq", NameAr = "المفرق" },
            new Governorate { Id = 6, Code = "KA", NameEn = "Karak", NameAr = "الكرك" },
            new Governorate { Id = 7, Code = "JA", NameEn = "Jerash", NameAr = "جرش" },
            new Governorate { Id = 8, Code = "MD", NameEn = "Madaba", NameAr = "مادبا" },
            new Governorate { Id = 9, Code = "AJ", NameEn = "Ajloun", NameAr = "عجلون" },
            new Governorate { Id = 10, Code = "AQ", NameEn = "Aqaba", NameAr = "العقبة" },
            new Governorate { Id = 11, Code = "MN", NameEn = "Ma'an", NameAr = "معان" },
            new Governorate { Id = 12, Code = "AT", NameEn = "Tafilah", NameAr = "الطفيلة" }
        );

        modelBuilder.Entity<LandCriterion>().HasData(new LandCriterion
        {
            Id = 1,
            MinAreaDonum = 20,
            MaxSlopePct = 10,
            MaxGridDistanceKm = 15,
            MinSolarIrradiance = 5.0m,
            MinElevationM = null,
            UpdatedById = -1,
            Reason = "ILLUSTRATIVE PLACEHOLDER values for development; replace after engineering validation"
        });

        modelBuilder.Entity<User>().HasData(new User
        {
            Id = -1,
            RoleId = 4,
            FullName = "System",
            Email = "system@nibras.internal",
            Phone = "0000000000",
            PasswordHash = "N/A",
            IsDeleted = false,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}