using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class ContractCancellationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContractCancellationBackgroundService> _logger;

    public ContractCancellationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ContractCancellationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<NebrasdbContext>();
                var contractService = scope.ServiceProvider.GetRequiredService<IContractService>();

                var activeStatusId = await context.ContractStatuses
                    .Where(s => s.Name == ContractStatusNames.Active)
                    .Select(s => s.Id)
                    .FirstAsync(ct);

                var dueContractIds = await context.Contracts
                    .Where(c => c.StatusId == activeStatusId
                             && c.CancellationRequestedById != null
                             && !c.DisputeFlagged
                             && c.CancellationEffectiveDate <= DateTime.UtcNow)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                foreach (var contractId in dueContractIds)
                {
                    try
                    {
                        await contractService.TerminateAsync(contractId, -1,
                            "Auto-terminated after cancellation effective date.");
                        _logger.LogInformation("Auto-terminated contract {ContractId}", contractId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto-termination failed for contract {ContractId}", contractId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in contract cancellation background service tick.");
            }
        }
    }
}
