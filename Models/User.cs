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

    public virtual ICollection<Contract> ContractInvestors { get; set; } = new List<Contract>();

    public virtual ICollection<Contract> ContractLandlords { get; set; } = new List<Contract>();

    public virtual ICollection<ContractReview> ContractReviews { get; set; } = new List<ContractReview>();

    public virtual ICollection<DeletionRequest> DeletionRequests { get; set; } = new List<DeletionRequest>();

    public virtual ICollection<LandCriterion> LandCriteria { get; set; } = new List<LandCriterion>();

    public virtual ICollection<LandStatusHistory> LandStatusHistories { get; set; } = new List<LandStatusHistory>();

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();

    public virtual ICollection<OfferVersion> OfferVersions { get; set; } = new List<OfferVersion>();

    public virtual ICollection<Offer> Offers { get; set; } = new List<Offer>();

    public virtual Role Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? DatefoBirth { get; set; }

}
