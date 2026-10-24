using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

/// <summary>
/// Append-only snapshot of one administrative technical assessment of a land.
///
/// Every row records the <em>effective</em> values after that assessment
/// committed, so the full history can be reconstructed by reading the rows in
/// <see cref="AssessedAt"/> order and the value of a field at any point in time
/// is the value in the latest row at or before it. Actual nulls are preserved:
/// a missing measurement is stored as NULL and is never replaced by a zero or
/// any other default. Rows are never updated or deleted.
/// </summary>
public partial class LandAssessment
{
    public int Id { get; set; }

    public int LandId { get; set; }

    /// <summary>Actor taken from the authenticated token, never from the request.</summary>
    public int? CriterionId { get; set; }

    public decimal? AreaDonumSnapshot { get; set; }

    public virtual LandCriterion? Criterion { get; set; }

    public int AssessedById { get; set; }

    public DateTime AssessedAt { get; set; }

    public string? Note { get; set; }

    public int? RegionId { get; set; }

    public int? GridId { get; set; }

    public decimal? SlopePercentage { get; set; }

    public decimal? DistanceToGridKm { get; set; }

    public decimal? SolarIrradiance { get; set; }

    public decimal? ElevationM { get; set; }

    public virtual Land Land { get; set; } = null!;

    public virtual User AssessedBy { get; set; } = null!;
}
