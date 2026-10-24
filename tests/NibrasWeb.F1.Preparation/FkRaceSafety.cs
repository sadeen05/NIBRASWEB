using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NIBRAS.Models;

namespace NibrasWeb.F1.Preparation;

// This entire file belongs only to the unpublished test executable.
public sealed record FixtureSnapshot(int Id, string Name, bool Required);
public sealed class FixtureReceipt
{
    internal FixtureReceipt(int id, string name, string marker) { Id=id; Name=name; Marker=marker; }
    public int Id { get; }
    public string Name { get; }
    public string Marker { get; }
}
public sealed class CommittedFixtureDeletion
{
    internal CommittedFixtureDeletion(FixtureReceipt receipt) => Receipt=receipt;
    public FixtureReceipt Receipt { get; }
}
public sealed class FixtureOwnership
{
    private readonly string marker;
    private readonly HashSet<FixtureReceipt> created=new(ReferenceEqualityComparer.Instance);
    public FixtureOwnership(string marker)
    {
        if (!marker.StartsWith("f1host-",StringComparison.Ordinal) ||
            !Guid.TryParseExact(marker[7..],"N",out _))
            throw new InvalidOperationException("Invalid run marker.");
        this.marker=marker;
    }
    // Call ONLY after the INSERT has succeeded and assigned the identity.
    public FixtureReceipt RecordCreation(int id,string name)
    {
        if(id<=0 || !name.StartsWith(marker+"-doctype-",StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[(marker.Length+9)..],"N",out _) ||
            created.Any(r=>r.Id==id || r.Name==name))
            throw new InvalidOperationException("Invalid or duplicate creation receipt.");
        var receipt=new FixtureReceipt(id,name,marker);
        created.Add(receipt); return receipt;
    }
    public void Verify(FixtureReceipt receipt)
    {
        if(!created.Contains(receipt) || receipt.Marker!=marker)
            throw new InvalidOperationException("Unrecognized fixture creation receipt.");
    }
}
public interface IFixtureDeletionStore
{
    Task<IFixtureDeletionTransaction> BeginSerializableAsync(CancellationToken token);
}
public interface IFixtureDeletionTransaction : IAsyncDisposable
{
    IsolationLevel Isolation { get; }
    Task<FixtureSnapshot?> LockAndReadAsync(int id,CancellationToken token);
    Task<int> CountReferencesAsync(int id,CancellationToken token);
    Task<int> DeleteGuardedAsync(int id,string name,CancellationToken token);
    Task CommitAsync(CancellationToken token);
    Task RollbackAsync(CancellationToken token);
}
public static class FkTaskFence
{
    // If an uncooperative upload/SQL task cannot be joined, merely throwing would leak it.
    // The separately approved runtime executable must stop nonzero instead of continuing or
    // disposing resources underneath it. No other process is killed; --static injects a fake.
    public static void StopIsolatedHost(string reason)
    {
        Console.Error.WriteLine("FAIL isolated FK harness cleanup incomplete; integrity review required.");
        Environment.Exit(74);
        throw new InvalidOperationException("Unreachable isolated-host stop.");
    }
    public static async Task<T> AwaitAsync<T>(Task<T> work,TimeSpan budget,Action cancel,
        TimeSpan grace,CancellationToken caller=default,Action<string>? terminal=null)
    {
        using var timer=new CancellationTokenSource();
        using var alarmSource=CancellationTokenSource.CreateLinkedTokenSource(timer.Token,caller);
        var alarm=Task.Delay(budget,alarmSource.Token);
        var expired=await Task.WhenAny(work,alarm)!=work;
        if(expired)
        {
            try { cancel(); } catch { /* Still join the operation before reporting timeout. */ }
            if(await Task.WhenAny(work,Task.Delay(grace))!=work)
            {
                try { (terminal ?? StopIsolatedHost)("cleanup deadline"); }
                catch { if(!work.IsCompleted) StopIsolatedHost("terminal failed with active operation"); }
                // Static tests complete the fake operation here. A real terminal never returns.
                if(await Task.WhenAny(work,Task.Delay(grace))!=work) StopIsolatedHost("terminal returned with active operation");
            }
        }
        timer.Cancel();
        T result;
        try { result=await work; } // Always observes faults/cancellation before any disposal.
        catch
        {
            if(expired && !caller.IsCancellationRequested) throw new TimeoutException("FK operation deadline.");
            throw;
        }
        caller.ThrowIfCancellationRequested();
        if(expired) throw new TimeoutException("FK operation deadline.");
        return result;
    }
    public static async Task AwaitAsync(Task work,TimeSpan budget,Action cancel,TimeSpan grace,
        CancellationToken caller=default,Action<string>? terminal=null)
    {
        async Task<bool> Observe() { await work; return true; }
        await AwaitAsync(Observe(),budget,cancel,grace,caller,terminal);
    }
}
public static class GuardedFixtureDeletion
{
    public static async Task<CommittedFixtureDeletion> DeleteAsync(FixtureOwnership ownership,
        FixtureReceipt receipt,IFixtureDeletionStore store,CancellationToken token=default,
        TimeSpan? budget=null,Action<string>? terminal=null)
    {
        ownership.Verify(receipt); // No transaction may open for unregistered evidence.
        var limit=budget ?? TimeSpan.FromSeconds(15);
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(token);
        operation.CancelAfter(limit);
        IFixtureDeletionTransaction? tx=null;
        var committed=false;
        try
        {
            // Capture late acquisition too, so a deadline cannot lose an open transaction.
            async Task Begin() { tx=await store.BeginSerializableAsync(operation.Token); }
            await FkTaskFence.AwaitAsync(Begin(),limit,operation.Cancel,TimeSpan.FromSeconds(5),token,terminal);
            var current=tx ?? throw new InvalidOperationException("No fixture transaction.");
            if(current.Isolation!=IsolationLevel.Serializable) throw new InvalidOperationException("Unverified transaction isolation.");
            var actual=await FkTaskFence.AwaitAsync(current.LockAndReadAsync(receipt.Id,operation.Token),
                limit,operation.Cancel,TimeSpan.FromSeconds(5),token,terminal);
            if(actual is null || actual.Id!=receipt.Id ||
                !string.Equals(actual.Name,receipt.Name,StringComparison.Ordinal) || actual.Required)
                throw new InvalidOperationException("Independent fixture ownership check failed.");
            var references=await FkTaskFence.AwaitAsync(current.CountReferencesAsync(receipt.Id,operation.Token),
                limit,operation.Cancel,TimeSpan.FromSeconds(5),token,terminal);
            if(references!=0) throw new InvalidOperationException("Fixture reference state is not empty.");
            var affected=await FkTaskFence.AwaitAsync(current.DeleteGuardedAsync(receipt.Id,receipt.Name,operation.Token),
                limit,operation.Cancel,TimeSpan.FromSeconds(5),token,terminal);
            if(affected!=1) throw new InvalidOperationException("Guarded deletion did not affect exactly one row.");
            await FkTaskFence.AwaitAsync(current.CommitAsync(operation.Token),limit,operation.Cancel,
                TimeSpan.FromSeconds(5),token,terminal);
            committed=true;
            return new CommittedFixtureDeletion(receipt);
        }
        finally
        {
            if(tx is not null)
            {
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    if(!committed)
                        await FkTaskFence.AwaitAsync(tx.RollbackAsync(cleanup.Token),TimeSpan.FromSeconds(5),
                            cleanup.Cancel,TimeSpan.FromSeconds(5),terminal:terminal);
                }
                finally
                {
                    await FkTaskFence.AwaitAsync(tx.DisposeAsync().AsTask(),TimeSpan.FromSeconds(5),
                        cleanup.Cancel,TimeSpan.FromSeconds(5),terminal:terminal);
                }
            }
        }
    }
}

public sealed class EfFixtureDeletionStore(NebrasdbContext context) : IFixtureDeletionStore
{
    public async Task<IFixtureDeletionTransaction> BeginSerializableAsync(CancellationToken token)
        =>new Transaction(context,await context.Database.BeginTransactionAsync(IsolationLevel.Serializable,token));
    private sealed class Transaction(NebrasdbContext db,IDbContextTransaction tx) : IFixtureDeletionTransaction
    {
        public IsolationLevel Isolation => tx.GetDbTransaction().IsolationLevel;
        public async Task<FixtureSnapshot?> LockAndReadAsync(int id,CancellationToken token)
        {
            // Independent DB read: never trust the fixture context's tracked entity.
            // XLOCK/HOLDLOCK retains the parent lock through COMMIT; SERIALIZABLE also
            // protects the reference predicate. No FK or constraint is disabled.
            var type=await db.DocumentTypes.FromSqlInterpolated(
                $"SELECT * FROM [DOCUMENT_TYPES] WITH (XLOCK, HOLDLOCK) WHERE [ID] = {id}")
                .AsNoTracking().SingleOrDefaultAsync(token);
            return type is null?null:new FixtureSnapshot(type.Id,type.Name,type.IsRequiredForVerification);
        }
        public Task<int> CountReferencesAsync(int id,CancellationToken token)
            =>db.LandDocuments.IgnoreQueryFilters().CountAsync(d=>d.DocumentTypeId==id,token);
        public Task<int> DeleteGuardedAsync(int id,string name,CancellationToken token)
            =>db.DocumentTypes.Where(t=>t.Id==id && t.Name==name && !t.IsRequiredForVerification &&
                !db.LandDocuments.IgnoreQueryFilters().Any(d=>d.DocumentTypeId==t.Id)).ExecuteDeleteAsync(token);
        public Task CommitAsync(CancellationToken token)=>tx.CommitAsync(token);
        public Task RollbackAsync(CancellationToken token)=>tx.RollbackAsync(token);
        public ValueTask DisposeAsync()=>tx.DisposeAsync();
    }
}
public static class FkUploadLifecycle
{
    public static async Task<Exception?> RunAsync(Func<Task> startUpload,
        Func<CancellationToken,Task> waitForBarrier,
        Func<CancellationToken,Task<CommittedFixtureDeletion>> deleteParent,
        Action<CommittedFixtureDeletion> release,Action abort,
        Func<CancellationToken,Task> inspectAfterJoin,CancellationToken caller=default,
        TimeSpan? operationBudget=null,TimeSpan? cleanupBudget=null,Action<string>? terminal=null)
    {
        var budget=operationBudget ?? TimeSpan.FromSeconds(15);
        var grace=cleanupBudget ?? TimeSpan.FromSeconds(45);
        using var stage=CancellationTokenSource.CreateLinkedTokenSource(caller);
        stage.CancelAfter(budget);
        Task? upload=null;
        Exception? setupError=null,uploadError=null,inspectionError=null;
        void RequestAbort() { try { abort(); } catch(Exception ex) { setupError ??= ex; } }
        try
        {
            upload=startUpload();
            await FkTaskFence.AwaitAsync(waitForBarrier(stage.Token),budget,stage.Cancel,grace,caller,terminal);
            var proof=await deleteParent(stage.Token);
            release(proof); // Proof is returned only after confirmed transaction COMMIT.
        }
        catch(Exception ex) { setupError=ex; }
        finally
        {
            if(setupError is not null || caller.IsCancellationRequested) RequestAbort();
            if(upload is not null)
            {
                try
                {
                    await FkTaskFence.AwaitAsync(upload,budget,RequestAbort,grace,caller,terminal);
                }
                catch(Exception ex) { uploadError=ex; }
            }
            // All ordinary paths reach this only with the started upload fully observed.
            using var cleanup=new CancellationTokenSource(budget);
            try
            {
                await FkTaskFence.AwaitAsync(inspectAfterJoin(cleanup.Token),budget,cleanup.Cancel,
                    grace,terminal:terminal);
            }
            catch(Exception ex) { inspectionError=ex; }
        }
        if(setupError is not null || inspectionError is not null)
            throw new AggregateException("FK setup or storage verification failed.",
                new[]{setupError,uploadError,inspectionError}.OfType<Exception>());
        return uploadError;
    }
}
