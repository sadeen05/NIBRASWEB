using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

public class GridCapacityReservations : IGridCapacityReservations
{
    private readonly IRepository<GridCapacityReservations> _gridCapacityReservationsRepository;
    private readonly ILogger<GridCapacityReservations> _logger;

    public GridCapacityReservations (IRepository<Grid> gridRepository, ILogger<GridService> logger)
    {
        _gridCapacityReservationsRepository = GridCapacityReservations;
        _logger = logger;
    }

    public async Task<List<>>



}