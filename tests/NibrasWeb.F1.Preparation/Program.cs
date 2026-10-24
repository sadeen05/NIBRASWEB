using System.Reflection;
using NIBRAS.API.Services;
using NibrasWeb.Enums;
namespace NibrasWeb.F1.Preparation;
public class VerificationProbe : DispatchProxy
{
    public Action<object?[]>? Cancel;
    protected override object? Invoke(MethodInfo? m,object?[]? a)
    {
        if(m!.Name!=nameof(IEmailVerificationService.CancelDeliveryAsync)) throw new InvalidOperationException("Unexpected dependency.");
        Cancel!(a!); return Task.CompletedTask;
    }
}
public static class Program
{
    private static int passed;
    public static void Check(bool value,string label)
    { if(!value){Console.Error.WriteLine("FAIL "+label);throw new InvalidOperationException("Assertion failed.");} Console.WriteLine("PASS "+label); Interlocked.Increment(ref passed); }
    public static async Task<int> Main(string[] args)
    {
        try {
            if(args.SequenceEqual(new[]{"--static"}))await StaticAsync();
            else if(args.SequenceEqual(new[]{"--sql-acceptance"}))await SqlAcceptance.RunAsync();
            else if(args.SequenceEqual(new[]{"--fake-smtp"}))await FakeSmtpAcceptance.RunAsync();
            else {Console.Error.WriteLine("Select --static. SQL and SMTP modes require separate approval.");return 2;}
            Console.WriteLine($"ACCEPTANCE_CHECKS PASS={passed} FAIL=0 MODE={args[0]}");
            return 0;
        }catch(Exception ex){Console.Error.WriteLine("FAIL "+ex.GetType().Name);if(args.SequenceEqual(new[]{"--static"}))Console.Error.WriteLine(ex.StackTrace);return 1;}
    }
    private static async Task StaticAsync()
    {
        var clock=new TestClock(new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero));
        var start=clock.GetUtcNow();clock.Advance(TimeSpan.FromHours(24));
        Check(clock.GetUtcNow()-start==TimeSpan.FromHours(24),"isolated clock advances exactly");
        var options=new EmailOptions{DeliveryMode="Smtp",SmtpHost="localhost",FromAddress="sender@example.test",FrontendBaseUrl="https://example.test",SmtpEnableSsl=false};
        var env=new TestEnvironment{EnvironmentName="Production"};
        var validator=new EmailOptionsValidator(env);
        Check(validator.Validate(null,options).Failed,"production rejects non-TLS SMTP");
        options.SmtpEnableSsl=true;
        Check(validator.Validate(null,options).Succeeded,"TLS production options accepted");
        options.DeliveryMode="Disabled";
        Check(validator.Validate(null,options).Failed,"production rejects Disabled delivery");
        env.EnvironmentName="Development";
        Check(validator.Validate(null,options).Succeeded,"Development Disabled preserved");
        Check(new EmailOptions().RegistrationTokenTtlHours==24&&new EmailOptions().EmailChangeTokenTtlHours==1,"TTL defaults retained");
        var verification=DispatchProxy.Create<IEmailVerificationService,VerificationProbe>();
        var retired=false;
        ((VerificationProbe)(object)verification).Cancel=a=>{
            Check(a[0] is int id && id==42 && a[1] is string exact && exact=="mock-only" && a[2] is string purpose && purpose==EmailVerificationPurposes.EmailChange,"exact cancellation account and purpose");
            var token=(CancellationToken)a[3]!;
            Check(!token.IsCancellationRequested&&token.CanBeCanceled,"independent bounded cleanup token");
            retired=true;
        };
        var mailer=new CancellableMailer();using var cancel=new CancellationTokenSource();
        var send=VerificationDelivery.SendAsync(verification,mailer,42,EmailVerificationPurposes.EmailChange,
            new IssuedVerificationToken("mock-only",clock.GetUtcNow().UtcDateTime),new EmailMessage("recipient@example.test","mock","mock"),cancel.Token);
        await mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();
        try{await send;Check(false,"cancellation propagated");}catch(OperationCanceledException){}
        Check(retired,"cancelled delivery retires exact issuance");
        ((VerificationProbe)(object)verification).Cancel=_=>throw new InvalidOperationException("mock cleanup failure");
        var other=new CancellableMailer();using var cancelled=new CancellationTokenSource();
        var failed=VerificationDelivery.SendAsync(verification,other,42,EmailVerificationPurposes.EmailChange,
            new IssuedVerificationToken("mock-only",clock.GetUtcNow().UtcDateTime),new EmailMessage("recipient@example.test","mock","mock"),cancelled.Token);
        await other.Entered.Task;cancelled.Cancel();
        var failClosed=false;try{await failed;}catch(InvalidOperationException){failClosed=true;}
        Check(failClosed,"restoration failure cannot report success");
        await UserUpdateMocks.RunAsync();
        await GovernanceMocks.RunAsync();
        await FkRaceMocks.RunAsync();
        await SmtpTaskContainment.RunMocksAsync();
        await AdminInvitationCancellationMocks.RunAsync();
        await SuperAdminBootstrapMocks.RunAsync();
        await HealthCheckMocks.RunAsync();
        ProductionHardeningChecks.Run();
    }
}
