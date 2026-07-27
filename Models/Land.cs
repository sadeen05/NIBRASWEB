using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Land
{
    public int Id { get; set; }

    public int LandlordId { get; set; }

    public int RegionId { get; set; }

    public string LandNumber { get; set; } = null!;

    public decimal AreaDonum { get; set; }

    public decimal SlopePercentage { get; set; }

    public decimal DistanceToGridKm { get; set; }

    public decimal SolarIrradiance { get; set; }

    public decimal ElevationM { get; set; }

    public bool IsDeleted { get; set; }

    public int LandStatusId { get; set; }

    public bool DataVerifiedByAdmin { get; set; }

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<DeletionRequest> DeletionRequests { get; set; } = new List<DeletionRequest>();

    public virtual ICollection<LandDocument> LandDocuments { get; set; } = new List<LandDocument>();

    public virtual LandStatus LandStatus { get; set; } = null!;

    public virtual ICollection<LandStatusHistory> LandStatusHistories { get; set; } = new List<LandStatusHistory>();

    public virtual User Landlord { get; set; } = null!;

    public virtual ICollection<Offer> Offers { get; set; } = new List<Offer>();

    public virtual Region Region { get; set; } = null!;
}
