using System.Data;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NIBRAS;
using NIBRAS.API.Controllers;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

public static class GovernanceMocks
{
    private static User Actor() => new() { Id=900, RoleId=4, Role=new Role {Id=4,Name=RoleNames.SuperAdmin},
        FullName="Original actor", Email="actor@example.test", Phone="9000000000", PasswordHash="unused" };
    public static async Task RunAsync()
    {
        var writeRule=typeof(CriteriaController).GetMethod(nameof(CriteriaController.Create))!
            .GetCustomAttributes<AuthorizeAttribute>().Single();
        Program.Check(writeRule.Roles==RoleNames.SuperAdmin,"criteria write endpoint permits only SuperAdmin");
        foreach(var role in new[]{RoleNames.Admin,RoleNames.Investor,RoleNames.Landlord,""}) {
            await using var db=new ModelOnlyFactory().CreateDbContext([]);
            Exception? failure=null;
            try {await new CriteriaService(db,new TestUser(900,role)).CreateAsync(new(){Reason="change",ExpectedCurrentId=1});}
            catch(Exception e){failure=e;}
            Program.Check(failure is ForbiddenException,"non-SuperAdmin criteria service write refused before any connection");
        }
        var fixture=new OfflineDatabase();fixture.Users.Add(Actor());
        var old=new LandCriterion {Id=7,MinAreaDonum=20,MaxSlopePct=10,Reason="original",UpdatedById=900};
        fixture.Criteria.Add(old);
        var clock=new TestClock(new DateTimeOffset(2030,1,2,3,4,5,TimeSpan.Zero));
        LandCriterionDto created;
        await using(var db=fixture.Context()) {
            created=await new CriteriaService(db,new TestUser(900,RoleNames.SuperAdmin),clock)
                .CreateAsync(new(){ExpectedCurrentId=7,MinAreaDonum=30,MaxSlopePct=9,Reason="engineering review"});
        }
        Program.Check(fixture.Committed && fixture.Disposed && fixture.Isolation==IsolationLevel.Serializable && fixture.LockedRange,
            "criteria append holds serialized update/range locks and commits");
        Program.Check(created.Id==8 && created.PreviousCriterionId==7 && created.PreviousSnapshot?.Id==7
            && created.Changes!.Single(c => c.Field=="minAreaDonum") is {OldValue:20,NewValue:30},
            "criteria old and new values and record IDs are exposed from immutable records");
        Program.Check(created.UpdatedById==900 && created.UpdatedByName=="Original actor" &&
            created.UpdatedByRole==RoleNames.SuperAdmin && created.UpdatedAt==clock.GetUtcNow().UtcDateTime &&
            created.ActorSnapshotRecorded,"criteria captures actual actor role name and exact UTC");
        fixture.Users[0].FullName="Later name";
        var latest=CriteriaService.ToDto(fixture.Criteria[^1],old);
        Program.Check(latest.UpdatedByName=="Original actor" && old.MinAreaDonum==20 && fixture.Criteria.Count==2,
            "later actor/profile changes preserve historical criteria and old threshold");
        await using(var db=fixture.Context()) {
            Exception? failure=null;
            try {await new CriteriaService(db,new TestUser(900,RoleNames.SuperAdmin)).CreateAsync(new(){ExpectedCurrentId=7,Reason="stale"});}
            catch(Exception e){failure=e;}
            Program.Check(failure is BusinessRuleException && fixture.Criteria.Count==2,"stale expected record conflicts without a lost-update append");
        }
        foreach(int? expected in new int?[]{null,0,6}) {
            Exception? failure=null;
            try {CriteriaService.EnsureExpectedCurrent(expected,7);}catch(Exception e){failure=e;}
            Program.Check(failure is BusinessRuleException,"omitted or stale criteria baseline conflicts");
        }
        var legacy=CriteriaService.ToDto(old,null);
        Program.Check(!legacy.ActorSnapshotRecorded && legacy.PreviousSnapshot is null &&
            legacy.UpdatedByRole is null,"legacy criteria does not fabricate historical actor or predecessor");
        await using(var db=fixture.Context()) {
            fixture.Users[0].Role.Name=RoleNames.Admin;
            Exception? failure=null;
            try {await new CriteriaService(db,new TestUser(900,RoleNames.SuperAdmin)).CreateAsync(new(){ExpectedCurrentId=8,Reason="role changed"});}
            catch(Exception e){failure=e;}
            Program.Check(failure is ForbiddenException && fixture.Criteria.Count==2,"persisted actor role revocation blocks criteria write");
        }
        var land=new Land {Id=101,LandlordId=42,LandNumber="isolated",AreaDonum=25,
            Status=LandStatus.Verified,VerifiedAgainstCriterionId=7};
        fixture.Lands.Add(land);
        await using(var db=fixture.Context()) {
            var result=await new LandService(db,new TestUser(900,RoleNames.SuperAdmin),null!,new TestEnvironment()).GetEligibilityAsync(land.Id);
            Program.Check(result.CriterionId==7 && result.Results.Single(r => r.Criterion=="area").Result=="Passed",
                "real eligibility service preserves pinned criteria after replacement");
        }
        land.Status=LandStatus.Draft;land.VerifiedAgainstCriterionId=null;
        await using(var db=fixture.Context()) {
            var result=await new LandService(db,new TestUser(900,RoleNames.SuperAdmin),null!,new TestEnvironment()).GetEligibilityAsync(land.Id);
            Program.Check(result.CriterionId==8 && result.Results.Single(r => r.Criterion=="area").Result=="Failed",
                "unpinned draft uses current thresholds without altering historical criteria");
        }
        await AppendOnlyAsync();
        await ApprovalAsync();
        await using(var db=fixture.Context()) {
            var auditClock=new TestClock(new DateTimeOffset(2030,2,3,4,5,6,TimeSpan.Zero));
            await new UserSecurityEventService(db,auditClock).RecordAsync(42,UserSecurityEventTypes.EmailChanged);
            Program.Check(fixture.Events.Last().CreatedAt==auditClock.GetUtcNow().UtcDateTime,
                "security audit uses the isolated deterministic clock without changing production defaults");
        }
    }

    private static async Task AppendOnlyAsync()
    {
        foreach(var entity in new object[]{
            new LandCriterion{Id=22,UpdatedById=900,Reason="historical"},
            new UserSecurityEvent{Id=23,UserId=42,EventType=UserSecurityEventTypes.AccountApproved},
            new LandAssessment{Id=24,LandId=1,AssessedById=900}})
        foreach(var state in new[]{EntityState.Modified,EntityState.Deleted}) {
            await using var db=new ModelOnlyFactory().CreateDbContext([]);
            db.Entry(entity).State=state;
            Exception? failure=null;
            try {await db.SaveChangesAsync();}catch(Exception e){failure=e;}
            Program.Check(failure is InvalidOperationException && failure.Message.Contains("append-only"),
                "historical criteria/security/assessment mutation refused before connection");
        }
    }

    public class ApprovalLock : DispatchProxy
    {
        public Func<Task<bool>> Acquire=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args) =>
            method?.Name==nameof(IEmailVerificationService.LockAccountAsync) ? Acquire() :
                throw new InvalidOperationException("Unexpected approval dependency.");
    }
    private static async Task ApprovalAsync()
    {
        var fixture=new OfflineDatabase();fixture.Users.Add(Actor());
        fixture.Users.Add(new User {Id=42,RoleId=2,Role=new Role{Id=2,Name=RoleNames.Investor},
            FullName="Investor",Email="investor@example.test",Phone="9111111111",PasswordHash="unused",
            EmailVerified=true,CompanyName="Test",CompanyRegistrationNumber="Test"});
        var proxy=DispatchProxy.Create<IEmailVerificationService,ApprovalLock>();
        ((ApprovalLock)(object)proxy).Acquire=()=> {
            if(!fixture.TransactionStarted)throw new InvalidOperationException("Approval lock requires transaction.");
            return Task.FromResult(true);
        };
        UserService Service(NebrasdbContext db) => new(db,NullLogger<UserService>.Instance,
            new TestUser(900,RoleNames.SuperAdmin),proxy,null!,null!);
        await using(var db=fixture.Context()) {
            Program.Check(await Service(db).ApproveAsync(42,900),"verified investor approval succeeds in isolated real service");
        }
        var approved=fixture.Users.Single(u => u.Id==42);
        Program.Check(approved.IsApproved && approved.ApprovedById==900 &&
            approved.ApprovedByNameSnapshot=="Original actor" && fixture.Events.Count==1,
            "approval persists actor snapshots with one append-only decision");
        var previousTime=approved.ApprovedAt;
        await using(var db=fixture.Context()) {
            Program.Check(await Service(db).RejectApprovalAsync(42,new(){Reason="documented decision"}),"reasoned investor rejection succeeds");
        }
        Program.Check(!approved.IsApproved && approved.ApprovedAt==previousTime && fixture.Events.Count==2 &&
            fixture.Events[1].EventType==UserSecurityEventTypes.AccountApprovalRejected &&
            fixture.Events[1].Details=="documented decision" && fixture.Events[0].EventType==UserSecurityEventTypes.AccountApproved,
            "rejection preserves previous approval and appends actual reason actor and UTC event");
        fixture.Users[0].FullName="Later actor name";
        await using(var db=fixture.Context()) await Service(db).ApproveAsync(42,900);
        Program.Check(fixture.Events.Count==3 && fixture.Events[0].ActorNameSnapshot=="Original actor" &&
            fixture.Events[2].ActorNameSnapshot=="Later actor name",
            "reapproval appends another event without rewriting historical actor identity");
        await using(var db=fixture.Context()) {
            Exception? failure=null;
            try {await Service(db).ApproveAsync(42,901);}catch(Exception e){failure=e;}
            Program.Check(failure is ForbiddenException && fixture.Events.Count==3,"supplied approval actor cannot impersonate another administrator");
            try {await Service(db).RejectApprovalAsync(42,new(){Reason=" "});failure=null;}catch(Exception e){failure=e;}
            Program.Check(failure is ValidationFailedException && fixture.Events.Count==3,"empty rejection reason cannot mutate approval or history");
        }
    }
}
