using System.Data;
using NIBRAS;
namespace NibrasWeb.F1.Preparation;

// No EF context, SQL provider, sockets or filesystem are used by these tests.
public static class FkRaceMocks
{
    private sealed class Store : IFixtureDeletionStore, IFixtureDeletionTransaction
    {
        public IsolationLevel Isolation {get;set;}=IsolationLevel.Serializable;
        public FixtureSnapshot? Snapshot;
        public int References, Affected=1, Begins, Deletes, Commits, Rollbacks, Disposals;
        public bool ConcurrentReference, DeleteConflict, CommitFailure, RollbackFailure;
        public List<string> Trace {get;}=new();
        public Task<IFixtureDeletionTransaction> BeginSerializableAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Begins++; Trace.Add("serializable"); return Task.FromResult<IFixtureDeletionTransaction>(this); }
        public Task<FixtureSnapshot?> LockAndReadAsync(int id,CancellationToken token)
        { token.ThrowIfCancellationRequested(); Trace.Add("locked-read"); return Task.FromResult(Snapshot); }
        public Task<int> CountReferencesAsync(int id,CancellationToken token)
        { token.ThrowIfCancellationRequested(); Trace.Add("references"); return Task.FromResult(References); }
        public Task<int> DeleteGuardedAsync(int id,string name,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Deletes++; Trace.Add("delete");
            if(DeleteConflict) throw new InvalidOperationException("mock FK conflict");
            if(ConcurrentReference) References++;
            if(Snapshot is null || Snapshot.Id!=id || Snapshot.Name!=name || Snapshot.Required || References!=0)
                return Task.FromResult(0);
            return Task.FromResult(Affected);
        }
        public Task CommitAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Trace.Add("commit"); if(CommitFailure) throw new InvalidOperationException("mock commit uncertainty"); Commits++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken token)
        { Trace.Add("rollback"); Rollbacks++; if(RollbackFailure)throw new InvalidOperationException("mock rollback failure"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; Trace.Add("dispose"); return ValueTask.CompletedTask; }
    }
    private static (FixtureOwnership Owner,FixtureReceipt Receipt,Store Store) Fixture()
    {
        var marker="f1host-"+Guid.NewGuid().ToString("N");
        var owner=new FixtureOwnership(marker);
        var receipt=owner.RecordCreation(7,marker+"-doctype-"+Guid.NewGuid().ToString("N"));
        return(owner,receipt,new Store{Snapshot=new FixtureSnapshot(receipt.Id,receipt.Name,false)});
    }
    private static async Task<bool> RejectsAsync(Func<Task> action)
    { try { await action(); return false; } catch(Exception) { return true; } }
    public static async Task RunAsync()
    {
        var cases=0;
        {
            var f=Fixture();
            var proof=await GuardedFixtureDeletion.DeleteAsync(f.Owner,f.Receipt,f.Store);
            Program.Check(ReferenceEquals(proof.Receipt,f.Receipt)&&
                string.Join(",",f.Store.Trace)=="serializable,locked-read,references,delete,commit,dispose",
                "FK owned unreferenced fixture commits before proof");
            cases++;
        }
        foreach(var mode in new[]{"missing","id","name","marker","required","references","unknown-references","isolation","unknown-isolation","concurrent","zero","multiple","conflict","commit","rollback"})
        {
            var f=Fixture();
            switch(mode)
            {
                case "missing": f.Store.Snapshot=null; break;
                case "id": f.Store.Snapshot=f.Store.Snapshot! with{Id=8}; break;
                case "name": f.Store.Snapshot=f.Store.Snapshot! with{Name=f.Receipt.Name+"-wrong"}; break;
                case "marker": f.Store.Snapshot=f.Store.Snapshot! with{Name="f1host-"+Guid.NewGuid().ToString("N")+"-doctype-"+Guid.NewGuid().ToString("N")}; break;
                case "required": f.Store.Snapshot=f.Store.Snapshot! with{Required=true}; break;
                case "references": f.Store.References=1; break;
                case "unknown-references": f.Store.References=-1; break;
                case "isolation": f.Store.Isolation=IsolationLevel.ReadCommitted; break;
                case "unknown-isolation": f.Store.Isolation=IsolationLevel.Unspecified; break;
                // Force a changed reference state despite the simulated lock to exercise the
                // second restrictive predicate. Actual SQL lock exclusion is NOT proven here.
                case "concurrent": f.Store.ConcurrentReference=true; break;
                case "zero": f.Store.Affected=0; break;
                case "multiple": f.Store.Affected=2; break;
                case "conflict": f.Store.DeleteConflict=true; break;
                case "commit": f.Store.CommitFailure=true; break;
                case "rollback": f.Store.Affected=0; f.Store.RollbackFailure=true; break;
            }
            var rejected=await RejectsAsync(async()=>{await GuardedFixtureDeletion.DeleteAsync(f.Owner,f.Receipt,f.Store);});
            Program.Check(rejected&&f.Store.Commits==0&&f.Store.Rollbacks==1&&f.Store.Disposals==1,
                "FK deletion rejects "+mode+" and verifies transaction cleanup");
            cases++;
        }
        foreach(var mode in new[]{"unregistered","wrong-registry","duplicate-id","duplicate-name","invalid-id","invalid-marker","pre-cancel"})
        {
            var f=Fixture();
            var rejected=await RejectsAsync(async()=>{
                if(mode=="unregistered")
                    await GuardedFixtureDeletion.DeleteAsync(f.Owner,new FixtureReceipt(f.Receipt.Id,f.Receipt.Name,f.Receipt.Marker),f.Store);
                else if(mode=="wrong-registry")
                    await GuardedFixtureDeletion.DeleteAsync(new FixtureOwnership(f.Receipt.Marker),f.Receipt,f.Store);
                else if(mode=="duplicate-id") f.Owner.RecordCreation(f.Receipt.Id,f.Receipt.Name);
                else if(mode=="duplicate-name") f.Owner.RecordCreation(8,f.Receipt.Name);
                else if(mode=="pre-cancel") { using var stop=new CancellationTokenSource(); stop.Cancel(); await GuardedFixtureDeletion.DeleteAsync(f.Owner,f.Receipt,f.Store,stop.Token); }
                else if(mode=="invalid-id") f.Owner.RecordCreation(0,f.Receipt.Name);
                else _=new FixtureOwnership("not-a-run-marker");
            });
            Program.Check(rejected&&f.Store.Begins==0,"FK creation evidence rejects "+mode+" before transaction");
            cases++;
        }
        foreach(var mode in new[]{"success","barrier-fault","barrier-timeout","deletion-fault","release-fault","upload-fault","upload-timeout","caller-cancel","abort-fault","storage-fault","hard-cleanup"})
        {
            await LifecycleAsync(mode);
            cases++;
        }
        {
            var f=Fixture(); using var barrier=new DocumentInsertBarrier(f.Receipt);
            var paused=barrier.PauseAsync();
            await barrier.WaitForArrivalAsync(CancellationToken.None);
            var earlyDisposal=await RejectsAsync(()=>{barrier.Dispose();return Task.CompletedTask;});
            var proof=await GuardedFixtureDeletion.DeleteAsync(f.Owner,f.Receipt,f.Store);
            barrier.Release(proof);await paused;
            Program.Check(earlyDisposal&&paused.IsCompletedSuccessfully,"real FK barrier refuses early disposal and accepts committed proof");
            cases++;
        }
        {
            var f=Fixture();using var barrier=new DocumentInsertBarrier(f.Receipt);
            var paused=barrier.PauseAsync();await barrier.Arrived.Task;
            var other=Fixture();var wrong=await GuardedFixtureDeletion.DeleteAsync(other.Owner,other.Receipt,other.Store);
            var refused=await RejectsAsync(()=>{barrier.Release(wrong);return Task.CompletedTask;});
            barrier.Abort();await RejectsAsync(()=>paused);
            Program.Check(refused&&paused.IsCompleted,"real FK barrier rejects wrong proof and joins abort");
            cases++;
        }
        {
            var f=Fixture();using var barrier=new DocumentInsertBarrier(f.Receipt);
            barrier.Abort();
            var paused=barrier.PauseAsync();
            Program.Check(await RejectsAsync(()=>paused)&&paused.IsCompleted,"real FK barrier aborts a late arrival");
            cases++;
        }
        {
            var f=Fixture();using var barrier=new DocumentInsertBarrier(f.Receipt);
            using var stop=new CancellationTokenSource();
            var paused=barrier.PauseAsync(stop.Token);await barrier.Arrived.Task;stop.Cancel();
            Program.Check(await RejectsAsync(()=>paused)&&paused.IsCompleted,"real FK barrier joins caller cancellation");
            cases++;
        }
        Console.WriteLine("FK_SAFETY_MOCK PASS="+cases+" FAIL=0");
    }
    private static async Task LifecycleAsync(string mode)
    {
        var f=Fixture();var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller=new CancellationTokenSource();
        Task? upload=null;
        int active=0,aborts=0,releases=0,inspections=0,terminalCalls=0;
        var proofAfterCommit=false;
        Task Start()
        {
            async Task Upload()
            {
                Interlocked.Increment(ref active);
                try
                {
                    arrived.TrySetResult();
                    await gate.Task;
                    if(mode=="upload-fault") throw new IOException("mock upload fault");
                    throw new ValidationFailedException("mock controlled FK failure");
                }
                finally { Interlocked.Decrement(ref active); }
            }
            upload=Upload();return upload;
        }
        Task Wait(CancellationToken token)
        {
            if(mode is "barrier-fault" or "abort-fault")throw new InvalidOperationException("mock barrier failure");
            if(mode=="caller-cancel")caller.Cancel();
            return mode is "barrier-timeout" or "caller-cancel"
                ? Task.Delay(Timeout.InfiniteTimeSpan,token)
                : arrived.Task.WaitAsync(token);
        }
        async Task<CommittedFixtureDeletion> Delete(CancellationToken token)
        {
            if(mode=="deletion-fault")throw new InvalidOperationException("mock deletion refusal");
            return await GuardedFixtureDeletion.DeleteAsync(f.Owner,f.Receipt,f.Store,token);
        }
        void Release(CommittedFixtureDeletion proof)
        {
            if(mode=="release-fault")throw new InvalidOperationException("mock release refusal");
            proofAfterCommit=f.Store.Commits==1&&ReferenceEquals(proof.Receipt,f.Receipt);
            releases++;
            if(mode is not ("upload-timeout" or "hard-cleanup"))gate.TrySetResult();
        }
        void Abort()
        {
            aborts++;
            if(mode!="hard-cleanup")gate.TrySetCanceled();
            if(mode=="abort-fault")throw new InvalidOperationException("mock abort callback failure");
        }
        Task Inspect(CancellationToken token)
        {
            if(upload is null||!upload.IsCompleted||active!=0||token.IsCancellationRequested)
                throw new InvalidOperationException("mock leaked upload or cancelled inspection");
            inspections++;
            if(mode=="storage-fault")throw new InvalidOperationException("mock uncertain storage");
            return Task.CompletedTask;
        }
        void Terminal(string reason) { terminalCalls++;gate.TrySetCanceled(); }
        Exception? returned=null,thrown=null;
        try
        {
            returned=await FkUploadLifecycle.RunAsync(Start,Wait,Delete,Release,Abort,Inspect,caller.Token,
                TimeSpan.FromMilliseconds(150),TimeSpan.FromMilliseconds(150),Terminal);
        }
        catch(Exception ex) { thrown=ex; }
        var setupFailure=mode is "barrier-fault" or "barrier-timeout" or "deletion-fault" or
            "release-fault" or "caller-cancel" or "abort-fault" or "storage-fault";
        var classification=setupFailure ? thrown is AggregateException :
            mode=="upload-fault" ? returned is IOException :
            mode is "upload-timeout" or "hard-cleanup" ? returned is TimeoutException :
            returned is ValidationFailedException;
        Program.Check(classification&&upload is {IsCompleted:true}&&active==0&&inspections==1&&
            (releases==0||proofAfterCommit)&&(mode!="hard-cleanup"||terminalCalls==1)&&
            (mode=="success"||mode=="upload-fault"||mode=="storage-fault"||aborts>0),
            "FK lifecycle "+mode+" observes upload before storage/disposal");
    }
}
