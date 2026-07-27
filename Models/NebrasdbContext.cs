using Microsoft.EntityFrameworkCore;

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
            optionsBuilder.UseSqlServer("Server=SADEEN\\SQLEXPRESS;Database=NEBRASDB;Trusted_Connection=True;TrustServerCertificate=True;");
        }
    }

    public virtual DbSet<Contract> Contracts { get; set; }

    public virtual DbSet<ContractReview> ContractReviews { get; set; }

    public virtual DbSet<ContractStatus> ContractStatuses { get; set; }

    public virtual DbSet<DeletionRequest> DeletionRequests { get; set; }

    public virtual DbSet<DocumentType> DocumentTypes { get; set; }

    public virtual DbSet<Grid> Grids { get; set; }

    public virtual DbSet<GridCapacityReservation> GridCapacityReservations { get; set; }

    public virtual DbSet<Land> Lands { get; set; }

    public virtual DbSet<LandCriterion> LandCriteria { get; set; }

    public virtual DbSet<LandDocument> LandDocuments { get; set; }

    public virtual DbSet<LandStatus> LandStatuses { get; set; }

    public virtual DbSet<LandStatusHistory> LandStatusHistories { get; set; }

    public virtual DbSet<Offer> Offers { get; set; }

    public virtual DbSet<OfferStatus> OfferStatuses { get; set; }

    public virtual DbSet<OfferVersion> OfferVersions { get; set; }

    public virtual DbSet<Region> Regions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<TariffBracket> TariffBrackets { get; set; }

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
            entity.Property(e => e.StatusId)
                .HasDefaultValue(1)
                .HasColumnName("STATUS_ID");
            entity.Property(e => e.OfferVersionId).HasColumnName("OFFER_VERSION_ID");
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

            entity.HasOne(d => d.Status).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.StatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONTRACTS_STATUS");

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
        });

        modelBuilder.Entity<ContractReview>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CONTRACT__3214EC273AB66E59");

            entity.ToTable("CONTRACT_REVIEWS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.Decision)
                .HasMaxLength(20)
                .HasColumnName("DECISION");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("REASON");
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

        modelBuilder.Entity<ContractStatus>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CONTRACT__3214EC27CE14508F");

            entity.ToTable("CONTRACT_STATUSES");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("NAME");
        });

        modelBuilder.Entity<DeletionRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__DELETION__3214EC278A03ACC4");

            entity.ToTable("DELETION_REQUESTS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AdminComment)
                .HasMaxLength(500)
                .HasColumnName("ADMIN_COMMENT");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("REASON");
            entity.Property(e => e.RequestedById).HasColumnName("REQUESTED_BY_ID");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending")
                .HasColumnName("STATUS");

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
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("NAME");
        });

        modelBuilder.Entity<Grid>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__GRIDS__3214EC27C5B24747");

            entity.ToTable("GRIDS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CapacityMw)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("CAPACITY_MW");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("NAME");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active")
                .HasColumnName("STATUS");

            entity.HasOne(d => d.Region).WithMany(p => p.Grids)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GRIDS_REGIONS");
        });

        modelBuilder.Entity<GridCapacityReservation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__GRID_CAP__3214EC27C682FA92");

            entity.ToTable("GRID_CAPACITY_RESERVATIONS");

            entity.HasIndex(e => e.ContractId, "UQ__GRID_CAP__3F5DFF15DEC96C5D").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ContractId).HasColumnName("CONTRACT_ID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.GridId).HasColumnName("GRID_ID");
            entity.Property(e => e.ReservedMw)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("RESERVED_MW");

            entity.HasOne(d => d.Contract).WithOne(p => p.GridCapacityReservation)
                .HasForeignKey<GridCapacityReservation>(d => d.ContractId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GCR_CONTRACTS");

            entity.HasOne(d => d.Grid).WithMany(p => p.GridCapacityReservations)
                .HasForeignKey(d => d.GridId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GCR_GRIDS");
        });

        modelBuilder.Entity<Land>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LANDS__3214EC278FA5F43E");

            entity.ToTable("LANDS");

            entity.HasIndex(e => new { e.LandNumber, e.RegionId }, "UQ_LAND_NUMBER_REGION").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AreaDonum)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("AREA_DONUM");
            entity.Property(e => e.DataVerifiedByAdmin).HasColumnName("DATA_VERIFIED_BY_ADMIN");
            entity.Property(e => e.DistanceToGridKm)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("DISTANCE_TO_GRID_KM");
            entity.Property(e => e.ElevationM)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("ELEVATION_M");
            entity.Property(e => e.IsDeleted).HasColumnName("IS_DELETED");
            entity.Property(e => e.LandNumber)
                .HasMaxLength(50)
                .HasColumnName("LAND_NUMBER");
            entity.Property(e => e.LandStatusId).HasColumnName("LAND_STATUS_ID");
            entity.Property(e => e.LandlordId).HasColumnName("LANDLORD_ID");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.SlopePercentage)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("SLOPE_PERCENTAGE");
            entity.Property(e => e.SolarIrradiance)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("SOLAR_IRRADIANCE");
            entity.Property(e => e.DocumentStorageLocation)
                .HasMaxLength(500)
                .HasColumnName("DOCUMENT_STORAGE_LOCATION");
            entity.Property(e => e.VerifiedAgainstCriterionId).HasColumnName("VERIFIED_AGAINST_CRITERION_ID");

            entity.HasOne(d => d.LandStatus).WithMany(p => p.Lands)
                .HasForeignKey(d => d.LandStatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_STATUS");

            entity.HasOne(d => d.Landlord).WithMany(p => p.Lands)
                .HasForeignKey(d => d.LandlordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_LANDLORDS");

            entity.HasOne(d => d.Region).WithMany(p => p.Lands)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_REGIONS");

            entity.HasOne(d => d.VerifiedAgainstCriterion)
                .WithMany()
                .HasForeignKey(d => d.VerifiedAgainstCriterionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LANDS_VERIFIED_CRITERION");
        });

        modelBuilder.Entity<LandCriterion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_CRI__3214EC276031D9CC");

            entity.ToTable("LAND_CRITERIA");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.MaxGridDistanceKm)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("MAX_GRID_DISTANCE_KM");
            entity.Property(e => e.MaxSlopePct)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("MAX_SLOPE_PCT");
            entity.Property(e => e.MinAreaDonum)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("MIN_AREA_DONUM");
            entity.Property(e => e.MinElevationM)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("MIN_ELEVATION_M");
            entity.Property(e => e.MinSolarIrradiance)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("MIN_SOLAR_IRRADIANCE");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("UPDATED_AT");
            entity.Property(e => e.UpdatedById).HasColumnName("UPDATED_BY_ID");

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
            entity.Property(e => e.FilePath)
                .HasMaxLength(500)
                .HasColumnName("FILE_PATH");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending")
                .HasColumnName("STATUS");
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("UPLOADED_AT");
            entity.Property(e => e.Version)
                .HasDefaultValue(1)
                .HasColumnName("VERSION");

            entity.HasOne(d => d.DocumentType).WithMany(p => p.LandDocuments)
                .HasForeignKey(d => d.DocumentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LD_DOCTYPES");

            entity.HasOne(d => d.Land).WithMany(p => p.LandDocuments)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LD_LANDS");
        });

        modelBuilder.Entity<LandStatus>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_STA__3214EC27D4FB7967");

            entity.ToTable("LAND_STATUSES");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("NAME");
        });

        modelBuilder.Entity<LandStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LAND_STA__3214EC27F466086B");

            entity.ToTable("LAND_STATUS_HISTORY");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CHANGED_AT");
            entity.Property(e => e.ChangedById).HasColumnName("CHANGED_BY_ID");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("REASON");
            entity.Property(e => e.StatusId).HasColumnName("STATUS_ID");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.LandStatusHistories)
                .HasForeignKey(d => d.ChangedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSH_USERS");

            entity.HasOne(d => d.Land).WithMany(p => p.LandStatusHistories)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSH_LANDS");
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFERS__3214EC277FE0E225");

            entity.ToTable("OFFERS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.InvestorId).HasColumnName("INVESTOR_ID");
            entity.Property(e => e.IsDeleted).HasColumnName("IS_DELETED");
            entity.Property(e => e.LandId).HasColumnName("LAND_ID");
            entity.Property(e => e.RequiredCapacityMw)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("REQUIRED_CAPACITY_MW");
            entity.Property(e => e.StatusId)
                .HasDefaultValue(1)
                .HasColumnName("STATUS_ID");
            entity.Property(e => e.AcceptedVersionId).HasColumnName("ACCEPTED_VERSION_ID");

            entity.HasOne(d => d.Investor).WithMany(p => p.Offers)
                .HasForeignKey(d => d.InvestorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_INVESTORS");

            entity.HasOne(d => d.Land).WithMany(p => p.Offers)
                .HasForeignKey(d => d.LandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_LANDS");

            entity.HasOne(d => d.Status).WithMany(p => p.Offers)
                .HasForeignKey(d => d.StatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_STATUS");

            entity.HasOne(d => d.AcceptedVersion)
                .WithMany()
                .HasForeignKey(d => d.AcceptedVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OFFERS_ACCEPTED_VERSION");
        });

        modelBuilder.Entity<OfferStatus>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFER_ST__3214EC27D14BDC1A");

            entity.ToTable("OFFER_STATUSES");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.NameStatus)
                .HasMaxLength(50)
                .HasColumnName("NAME");
        });

        modelBuilder.Entity<OfferVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__OFFER_VE__3214EC27145811E1");

            entity.ToTable("OFFER_VERSIONS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");
            entity.Property(e => e.CreatedById).HasColumnName("CREATED_BY_ID");
            entity.Property(e => e.DurationYears).HasColumnName("DURATION_YEARS");
            entity.Property(e => e.InstallationCost)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("INSTALLATION_COST");
            entity.Property(e => e.LandlordSharePct)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("LANDLORD_SHARE_PCT");
            entity.Property(e => e.OfferId).HasColumnName("OFFER_ID");
            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500)
                .HasColumnName("REJECTION_REASON");
            entity.Property(e => e.StartDate).HasColumnName("START_DATE");
            entity.Property(e => e.VersionNumber).HasColumnName("VERSION_NUMBER");
            entity.Property(e => e.SolarCellCapacityKw)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("SOLAR_CELL_CAPACITY_KW");
            entity.Property(e => e.ExpectedAnnualRevenue)
                .HasColumnType("decimal(14, 3)")
                .HasColumnName("EXPECTED_ANNUAL_REVENUE");
            entity.Property(e => e.EffectiveCostPerKw)
                .HasColumnType("decimal(10, 3)")
                .HasColumnName("EFFECTIVE_COST_PER_KWH");
            entity.Property(e => e.PaybackPeriodMonths)
                .HasColumnType("decimal(8, 2)")
                .HasColumnName("PAYBACK_PERIOD_MONTHS");

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
            entity.Property(e => e.NameAr)
                .HasMaxLength(100)
                .HasColumnName("NAME_AR");
            entity.Property(e => e.NameEn)
                .HasMaxLength(100)
                .HasColumnName("NAME_EN");
            entity.Property(e => e.PeakSunHoursPerDay)
                .HasColumnType("decimal(5, 2)")
                .HasDefaultValue(5.5m)
                .HasColumnName("PEAK_SUN_HOURS_PER_DAY");
            entity.Property(e => e.WheelingFeePerKwh)
                .HasColumnType("decimal(10, 3)")
                .HasDefaultValue(0m)
                .HasColumnName("WHEELING_FEE_PER_KWH");
            entity.Property(e => e.LossPercentage)
                .HasColumnType("decimal(5, 2)")
                .HasDefaultValue(0m)
                .HasColumnName("LOSS_PERCENTAGE");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__ROLES__3214EC276782D7D4");

            entity.ToTable("ROLES");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("NAME");
        });

        modelBuilder.Entity<TariffBracket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__TARIFF_BRACKETS");

            entity.ToTable("TARIFF_BRACKETS");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.RegionId).HasColumnName("REGION_ID");
            entity.Property(e => e.FromKwh).HasColumnName("FROM_KWH");
            entity.Property(e => e.ToKwh).HasColumnName("TO_KWH");
            entity.Property(e => e.RatePerKwh)
                .HasColumnType("decimal(10, 3)")
                .HasColumnName("RATE_PER_KWH");
            entity.Property(e => e.EffectiveFrom).HasColumnName("EFFECTIVE_FROM");
            entity.Property(e => e.EffectiveTo).HasColumnName("EFFECTIVE_TO");

            entity.HasOne(d => d.Region).WithMany(p => p.TariffBrackets)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TB_REGIONS");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__USERS__3214EC27D0F84BA1");

            entity.ToTable("USERS");

            entity.HasIndex(e => e.Email, "UQ__USERS__161CF724F5C148C0").IsUnique();

            entity.HasIndex(e => e.Phone, "UQ__USERS__D4FA0A2658412415").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("EMAIL");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("FULL_NAME");
            entity.Property(e => e.IsDeleted).HasColumnName("IS_DELETED");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("PASSWORD_HASH");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("PHONE");
            entity.Property(e => e.RoleId).HasColumnName("ROLE_ID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("CREATED_AT");

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

        modelBuilder.Entity<LandStatus>().HasData(
            new LandStatus { Id = 1, Name = LandStatusNames.Draft },
            new LandStatus { Id = 2, Name = LandStatusNames.PendingVerification },
            new LandStatus { Id = 3, Name = LandStatusNames.Verified },
            new LandStatus { Id = 4, Name = LandStatusNames.Rejected }
        );

        modelBuilder.Entity<ContractStatus>().HasData(
            new ContractStatus { Id = 1, Name = ContractStatusNames.PendingSignatures },
            new ContractStatus { Id = 2, Name = ContractStatusNames.Active },
            new ContractStatus { Id = 3, Name = ContractStatusNames.Terminated },
            new ContractStatus { Id = 4, Name = ContractStatusNames.Rejected }
        );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
