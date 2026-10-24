using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NIBRAS.Models;
using NIBRAS.API.Services;
namespace NibrasWeb.F1.Preparation;
public sealed class TestClock(DateTimeOffset initial) : TimeProvider
{
    private long ticks=initial.UtcTicks;
    public override DateTimeOffset GetUtcNow()=>new(Interlocked.Read(ref ticks),TimeSpan.Zero);
    public void Advance(TimeSpan amount)=>Interlocked.Add(ref ticks,amount.Ticks);
}
public sealed class CancellableMailer : IEmailSender
{
    public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<EmailSendResult> SendAsync(EmailMessage message,CancellationToken token=default)
    { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan,token); return EmailSendResult.Success; }
}
public sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName {get;set;}=Environments.Development;
    public string ApplicationName {get;set;}="NibrasWeb.F1.Preparation";
    public string ContentRootPath {get;set;}=Path.GetTempPath();
    public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
}
public sealed class TestUser(int id,string role) : ICurrentUser
{ public int? Id=>id; public string? Role=>role; public string? Ip=>null; }
public sealed class NoConnections : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection c,ConnectionEventData d,InterceptionResult r)
        =>throw new InvalidOperationException("Database access prohibited during model inspection.");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c,ConnectionEventData d,InterceptionResult r,CancellationToken t=default)
        =>throw new InvalidOperationException("Database access prohibited during model inspection.");
}
// The CLI factory does not execute web Program, load credentials, connect or seed.
public sealed class ModelOnlyFactory : IDesignTimeDbContextFactory<NebrasdbContext>
{
    public NebrasdbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<NebrasdbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=NIBRAS_F1_Verify;Integrated Security=True;TrustServerCertificate=True")
        .AddInterceptors(new NoConnections()).Options);
}
public sealed class DocumentInsertBarrier(FixtureReceipt fixture) : SaveChangesInterceptor, IDisposable
{
    private readonly CancellationTokenSource abort=new();
    private readonly TaskCompletionSource<CommittedFixtureDeletion> resume=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    public TaskCompletionSource Arrived {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitForArrivalAsync(CancellationToken token)=>Arrived.Task.WaitAsync(token);
    public void Release(CommittedFixtureDeletion proof)
    {
        if(!ReferenceEquals(proof.Receipt,fixture)) throw new InvalidOperationException("Wrong deletion proof.");
        abort.Token.ThrowIfCancellationRequested();
        if(!resume.TrySetResult(proof)) throw new InvalidOperationException("Barrier already settled.");
    }
    public void Abort() { abort.Cancel(); resume.TrySetCanceled(abort.Token); }
    internal async Task PauseAsync(CancellationToken token=default)
    {
        Interlocked.Increment(ref active);
        try
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,abort.Token);
            linked.Token.ThrowIfCancellationRequested();
            Arrived.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(30),linked.Token);
            linked.Token.ThrowIfCancellationRequested();
        }
        finally { Interlocked.Decrement(ref active); }
    }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData d,InterceptionResult<int> r,CancellationToken t=default)
    {
        if(d.Context!.ChangeTracker.Entries<LandDocument>().Any(e=>e.State==EntityState.Added&&e.Entity.DocumentTypeId==fixture.Id))
            await PauseAsync(t);
        return r;
    }
    public void Dispose()
    {
        if(Volatile.Read(ref active)!=0) throw new InvalidOperationException("Cannot dispose an active upload barrier.");
        abort.Dispose();
    }
}
