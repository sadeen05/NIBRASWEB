using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class User
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public bool IsApproved { get; set; }

    public int? ApprovedById { get; set; }

    public string? ApprovedByNameSnapshot { get; set; }

    public string? ApprovedByRoleSnapshot { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Email-ownership state. <c>NULL</c> means "pre-existing account, verified under the
    /// rules in force before the verification feature existed" and is treated as verified, so
    /// no existing account is locked out and no value is fabricated for it. <c>false</c> means
    /// the account has to prove its address before it can be used; <c>true</c> means the active
    /// address is proven.
    /// </summary>
    public bool? EmailVerified { get; set; }

    /// <summary>When the active address was verified. NULL for a legacy or unverified account.</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>
    /// A requested new address that has not been verified yet. The active <see cref="Email"/>
    /// is only ever replaced by a successful verification of the pending address.
    /// </summary>
    public string? PendingEmail { get; set; }

    public string? CompanyName { get; set; }

    public string? CompanyRegistrationNumber { get; set; }

    public virtual ICollection<Contract> ContractInvestors { get; set; } = new List<Contract>();

    public virtual ICollection<Contract> ContractLandlords { get; set; } = new List<Contract>();

    public virtual ICollection<ContractReview> ContractReviews { get; set; } = new List<ContractReview>();

    public virtual ICollection<DeletionRequest> DeletionRequests { get; set; } = new List<DeletionRequest>();

    public virtual ICollection<LandCriterion> LandCriteria { get; set; } = new List<LandCriterion>();

    public virtual ICollection<LandStatusHistory> LandStatusHistories { get; set; } = new List<LandStatusHistory>();

    /// <summary>The single-use email-ownership tokens issued for this account.</summary>
    public virtual ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = new List<EmailVerificationToken>();

    /// <summary>The append-only security audit trail of this account.</summary>
    public virtual ICollection<UserSecurityEvent> UserSecurityEvents { get; set; } = new List<UserSecurityEvent>();

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();

    public virtual ICollection<OfferVersion> OfferVersions { get; set; } = new List<OfferVersion>();

    public virtual ICollection<Offer> Offers { get; set; } = new List<Offer>();

    public virtual ICollection<OfferNegotiationHistory> OfferNegotiationHistories { get; set; } = new List<OfferNegotiationHistory>();

    public virtual Role Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? DatefoBirth { get; set; }

    public virtual ICollection<Contract> CancellationRequestedContracts { get; set; } = new List<Contract>();
}
