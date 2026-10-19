using System;
using System.Collections.Generic;
using NibrasWeb.Enums;

namespace NIBRAS.Models;

public partial class Land
{
    public int Id { get; set; }

    public int LandlordId { get; set; }

    /// <summary>
    /// Assigned by an admin technical assessment. Null until assessed; never
    /// substituted with a default.
    /// </summary>
    public int? RegionId { get; set; }

    /// <summary>
    /// Assigned by an admin technical assessment. Null until assessed; never
    /// substituted with a default.
    /// </summary>
    public int? GridId { get; set; }

    /// <summary>
    /// Jordanian governorate chosen by the landlord at registration. Null only
    /// for records created before governorates existed.
    /// </summary>
    public int? GovernorateId { get; set; }

    public virtual Grid? Grid { get; set; }

    public string LandNumber { get; set; } = null!;

    public string? Village { get; set; }

    public string? Basin { get; set; }

    public string? ParcelNumber { get; set; }

    /// <summary>Optional free-text description supplied by the landlord.</summary>
    public string? Description { get; set; }

    public decimal AreaDonum { get; set; }

    /// <summary>
    /// Technical measurements recorded by an admin assessment. Null means
    /// "not yet assessed" and is never substituted with a zero default.
    /// </summary>
    public decimal? SlopePercentage { get; set; }

    public decimal? DistanceToGridKm { get; set; }

    public decimal? SolarIrradiance { get; set; }

    public decimal? ElevationM { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// True while this approved land has an open change request. Investors cannot
    /// browse or open the land for the whole review, and the flag is cleared when the
    /// SuperAdmin decides the request. The land status stays <c>Verified</c> so the
    /// existing offer/contract transitions are unaffected.
    /// </summary>
    public bool IsHiddenFromInvestors { get; set; }

    public LandStatus Status { get; set; }

    public bool DataVerifiedByAdmin { get; set; }

    public string? DocumentStorageLocation { get; set; }

    public int? VerifiedAgainstCriterionId { get; set; }

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<DeletionRequest> DeletionRequests { get; set; } = new List<DeletionRequest>();

    public virtual ICollection<LandAssessment> LandAssessments { get; set; } = new List<LandAssessment>();

    public virtual ICollection<LandChangeRequest> LandChangeRequests { get; set; } = new List<LandChangeRequest>();

    public virtual ICollection<LandDocument> LandDocuments { get; set; } = new List<LandDocument>();

    public virtual ICollection<LandStatusHistory> LandStatusHistories { get; set; } = new List<LandStatusHistory>();

    public virtual User Landlord { get; set; } = null!;

    public virtual ICollection<Offer> Offers { get; set; } = new List<Offer>();

    public virtual Governorate? Governorate { get; set; }

    public virtual Region? Region { get; set; }

    public virtual LandCriterion? VerifiedAgainstCriterion { get; set; }
}
