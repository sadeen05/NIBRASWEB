using Microsoft.EntityFrameworkCore;
using NibrasWeb.Enums;

namespace NIBRAS.Models;

public partial class NebrasdbContext
{
    private void EnsureAppendOnly()
    {
        if (ChangeTracker.Entries().Any(e =>
            e.Entity is LandCriterion or UserSecurityEvent or LandAssessment &&
            e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Criteria and security history are append-only.");
    }

    private List<UserSecurityEvent> NewActorEvents() => ChangeTracker.Entries<UserSecurityEvent>()
        .Where(e => e.State == EntityState.Added && e.Entity.ActorId.HasValue)
        .Select(e => e.Entity).ToList();

    private static void SnapshotActors(List<UserSecurityEvent> events, Dictionary<int, User> actors)
    {
        foreach (var entry in events) {
            if (!actors.TryGetValue(entry.ActorId!.Value, out var actor))
                throw new InvalidOperationException("The audit actor is unavailable.");
            entry.ActorNameSnapshot=actor.FullName;
            entry.ActorRoleSnapshot=actor.Role.Name;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnly();
        var events=NewActorEvents();
        if (events.Count > 0) {
            var ids=events.Select(e => e.ActorId!.Value).Distinct().ToList();
            SnapshotActors(events, Users.AsNoTracking().Include(u => u.Role)
                .Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id));
        }
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken=default)
    {
        EnsureAppendOnly();
        var events=NewActorEvents();
        if (events.Count > 0) {
            var ids=events.Select(e => e.ActorId!.Value).Distinct().ToList();
            SnapshotActors(events, await Users.AsNoTracking().Include(u => u.Role)
                .Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken));
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
