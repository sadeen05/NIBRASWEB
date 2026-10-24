using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NIBRAS;
using NIBRAS.Models;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// SOURCE PREPARATION ONLY: never called by --static or the model factory.
// This mode creates only prefix-owned fixtures; it does not seed, migrate, reset or clean history.
public static class SqlAcceptance
{
    internal const string Connection="Server=SADEEN\\SQLEXPRESS;Database=NIBRAS_F1_Verify;Integrated Security=True;TrustServerCertificate=True";
    private static readonly string Prefix="f1host-"+Guid.NewGuid().ToString("N");
    private static readonly string Password="F1!"+Guid.NewGuid().ToString("N");
    private static readonly EmailOptions Settings=new(){ResendCooldownSeconds=0,ResendDailyCap=100,FrontendBaseUrl="http://localhost"};
    internal static NebrasdbContext Context(DocumentInsertBarrier? barrier=null)
    {
        var builder=new DbContextOptionsBuilder<NebrasdbContext>().UseSqlServer(Connection,o=>o.CommandTimeout(15));
        if(barrier is not null)builder.AddInterceptors(barrier);
        return new(builder.Options);
    }
    private static EmailVerificationService Tokens(NebrasdbContext db,TestClock clock)=>new(db,Options.Create(Settings),NullLogger<EmailVerificationService>.Instance,clock);
    internal static TestClock Clock()=>new(new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero));
    internal static async Task<User> FixtureAsync(NebrasdbContext db,bool verified,string role="Landlord")
    {
        var suffix=Guid.NewGuid().ToString("N");
        var user=new User{RoleId=await db.Roles.Where(r=>r.Name==role).Select(r=>r.Id).SingleAsync(),
            FullName=Prefix,Email=Prefix+"-"+suffix+"@example.test",Phone="9"+Random.Shared.NextInt64(100000000,999999999),
            PasswordHash=BCrypt.Net.BCrypt.HashPassword(Password,workFactor:12),EmailVerified=verified,
            EmailVerifiedAt=verified?Clock().GetUtcNow().UtcDateTime:null,IsApproved=true};
        db.Users.Add(user);await db.SaveChangesAsync();return user;
    }
    public static async Task RunAsync()
    {
        foreach(var change in new[]{false,true})
        foreach(var seconds in new[]{-1,0,1})
        {
            await using var db=Context();var clock=Clock();var service=Tokens(db,clock);
            var user=await FixtureAsync(db,change);var original=user.Email;
            var issued=change?await service.IssueEmailChangeTokenAsync(user.Id,Prefix+Guid.NewGuid().ToString("N")+"@example.test",Password)
                :await service.IssueRegistrationTokenAsync(user.Id);
            Program.Check(issued.Succeeded,"expiry fixture issued");
            var hours=change?1:24;
            Program.Check(issued.Issued!.ExpiresAt==clock.GetUtcNow().UtcDateTime.AddHours(hours),"exact production TTL persisted");
            var purpose=change?EmailVerificationPurposes.EmailChange:EmailVerificationPurposes.Registration;
            clock.Advance(TimeSpan.FromHours(hours)+TimeSpan.FromSeconds(seconds));
            var consumed=await service.ConsumeTokenAsync(issued.Issued.Plaintext,purpose);
            Program.Check(consumed.Outcome==(seconds<0?VerificationOutcome.Succeeded:VerificationOutcome.Expired),"expiry boundary rejects at and after expiry");
            await db.Entry(user).ReloadAsync();
            Program.Check(seconds<0?user.EmailVerified==true:(change?user.Email==original:user.EmailVerified==false),"expiry account state preserved");
        }
        await CancellationAsync();
        await InFlightAsync();
        await StaleProfileAsync();
        await ForeignKeyAsync();
        await LandConcurrencyAcceptance.RunAsync();
        await LandRequirementConcurrencyAcceptance.RunAsync();
        await PhoneConflictAcceptance.RunAsync();
        await AdminInvitationAcceptance.RunAsync();
    }
    private static async Task CancellationAsync()
    {
        await using var db=Context();var clock=Clock();var service=Tokens(db,clock);
        var user=await FixtureAsync(db,true);var active=user.Email;var epoch=user.EmailVerifiedAt;
        var one=await service.IssueEmailChangeTokenAsync(user.Id,Prefix+"cancel@example.test",Password);
        var count=await db.UserSecurityEvents.CountAsync(e=>e.UserId==user.Id);
        await service.CancelDeliveryAsync(user.Id,one.Issued!.Plaintext,EmailVerificationPurposes.EmailChange);
        await db.Entry(user).ReloadAsync();
        Program.Check(user.Email==active&&user.EmailVerifiedAt==epoch&&user.PendingEmail is null,"cancelled request preserves active identity");
        Program.Check((await service.ConsumeTokenAsync(one.Issued.Plaintext,EmailVerificationPurposes.EmailChange)).Outcome==VerificationOutcome.AlreadyUsed,"cancelled token unusable");
        Program.Check(await db.UserSecurityEvents.CountAsync(e=>e.UserId==user.Id)>=count+1,"cancellation retains audit and appends retirement");
        var old=await service.IssueEmailChangeTokenAsync(user.Id,Prefix+"old@example.test",Password);
        var newer=await service.IssueEmailChangeTokenAsync(user.Id,Prefix+"new@example.test",Password);
        await service.CancelDeliveryAsync(user.Id,old.Issued!.Plaintext,EmailVerificationPurposes.EmailChange);
        await db.Entry(user).ReloadAsync();
        Program.Check(user.PendingEmail==Prefix+"new@example.test","older cancellation cannot clear newer request");
        Program.Check((await service.ConsumeTokenAsync(newer.Issued!.Plaintext,EmailVerificationPurposes.EmailChange)).Succeeded,"newer token survives older cancellation");
    }
    private static async Task InFlightAsync()
    {
        foreach(var change in new[]{false,true})
        {
            await using var db=Context();var clock=Clock();var verification=Tokens(db,clock);
            var user=await FixtureAsync(db,change);var original=user.Email;var epoch=user.EmailVerifiedAt;
            var mailer=new CancellableMailer();using var abort=new CancellationTokenSource();
            Task send;
            if(change)
                send=new EmailChangeService(verification,mailer,new UserSecurityEventService(db,clock),Options.Create(Settings),
                    NullLogger<EmailChangeService>.Instance,clock).RequestAsync(user.Id,Prefix+"inflight@example.test",Password,cancellationToken:abort.Token);
            else
                send=new RegistrationVerificationService(db,verification,mailer,new UserSecurityEventService(db,clock),Options.Create(Settings),
                    NullLogger<RegistrationVerificationService>.Instance,clock).SendAsync(user.Id,cancellationToken:abort.Token);
            await mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));abort.Cancel();
            var cancelled=false;try{await send;}catch(OperationCanceledException){cancelled=true;}
            Program.Check(cancelled,"in-flight cancellation propagates after verified cleanup");
            await db.Entry(user).ReloadAsync();
            Program.Check(user.Email==original&&user.EmailVerifiedAt==epoch&&user.PendingEmail is null,"in-flight cancellation preserves identity and clears only its pending address");
            Program.Check(await db.EmailVerificationTokens.Where(t=>t.UserId==user.Id).AllAsync(t=>t.ConsumedAt!=null),"interrupted delivery has no redeemable token");
            Program.Check(await db.UserSecurityEvents.AnyAsync(e=>e.UserId==user.Id&&e.EventType==UserSecurityEventTypes.VerificationTokenIssued)
                &&await db.UserSecurityEvents.AnyAsync(e=>e.UserId==user.Id&&e.EventType==UserSecurityEventTypes.VerificationTokenInvalidated),"in-flight cancellation preserves issuance and retirement audit");
        }
    }

    private static async Task StaleProfileAsync()
    {
        await using var stale=Context();await using var fresh=Context();var clock=Clock();
        var user=await FixtureAsync(stale,false);
        var actor=await FixtureAsync(fresh,true,RoleNames.SuperAdmin);
        // The first context holds an unverified snapshot before a second context confirms it.
        var token=await Tokens(fresh,clock).IssueRegistrationTokenAsync(user.Id);
        Program.Check((await Tokens(fresh,clock).ConsumeTokenAsync(token.Issued!.Plaintext,EmailVerificationPurposes.Registration)).Succeeded,"profile-race fixture confirmed by second context");
        var service=new UserService(stale,NullLogger<UserService>.Instance,new TestUser(actor.Id,RoleNames.SuperAdmin),
            Tokens(stale,clock),null!,null!); // These delivery dependencies are unused by UpdateAsync.
        var rejected=false;
        try{await service.UpdateAsync(user.Id,new UpdateUserRequest(user.FullName,Prefix+"overwrite@example.test",user.Phone));}
        catch(BusinessRuleException){rejected=true;}
        Program.Check(rejected,"stale unverified snapshot cannot overwrite newly verified address");
        await stale.Entry(user).ReloadAsync();
        Program.Check(user.EmailVerified==true&&user.Email!=Prefix+"overwrite@example.test","concurrent verification preserved");
        Program.Check(await service.UpdateAsync(user.Id,new UpdateUserRequest(Prefix+" profile",user.Email,user.Phone)),"legitimate same-email profile update preserved");
    }
    private static async Task ForeignKeyAsync()
    {
        await using var fixture=Context();
        var owner=await FixtureAsync(fixture,true);
        var ownership=new FixtureOwnership(Prefix);
        var typeName=Prefix+"-doctype-"+Guid.NewGuid().ToString("N");
        var type=new DocumentType{Name=typeName,IsRequiredForVerification=false};

        var land=new Land{LandlordId=owner.Id,LandNumber=Prefix,AreaDonum=25,Status=LandStatus.Draft,
            GovernorateId=await fixture.Governorates.Select(g=>g.Id).FirstAsync(),Village=Prefix,Basin=Prefix,ParcelNumber=Prefix};
        fixture.Add(type);fixture.Add(land);await fixture.SaveChangesAsync();
        // Register only the just-inserted identity, before constructing any deletion request.
        var receipt=ownership.RecordCreation(type.Id,type.Name);
        using var barrier=new DocumentInsertBarrier(receipt);
        await using var uploadDb=Context(barrier);
        var root=Path.Combine(Path.GetTempPath(),Prefix+"-documents");
        var env=new TestEnvironment{ContentRootPath=Path.GetTempPath()};
        var storage=new DocumentStorageService(Options.Create(new DocumentStorageOptions{StorageRoot=root}),env,NullLogger<DocumentStorageService>.Instance);
        var service=new LandService(uploadDb,new TestUser(owner.Id,"Landlord"),storage,env);
        var bytes=System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");
        using var stream=new MemoryStream(bytes);
        var file=new FormFile(stream,0,bytes.Length,"file","fixture.pdf");
        var failure=await FkUploadLifecycle.RunAsync(
            startUpload:()=>service.UploadDocumentAsync(land.Id,type.Id,file),
            waitForBarrier:barrier.WaitForArrivalAsync,
            deleteParent:token=>GuardedFixtureDeletion.DeleteAsync(ownership,receipt,new EfFixtureDeletionStore(fixture),token),
            release:barrier.Release,abort:barrier.Abort,
            inspectAfterJoin:async token=>{
                Program.Check(!await fixture.LandDocuments.AnyAsync(d=>d.LandId==land.Id,token),"FK rollback leaves no document row");
                Program.Check(!Directory.Exists(root)||!Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(),"FK rollback leaves no orphan file");
            });
        Program.Check(failure is ValidationFailedException,"genuine post-lookup FK failure is controlled");
        // Keep fixture/audit history and the empty test root; no historical file is deleted.

    }
}
