using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Exercises the real EF 10 tracker, UserService and exception handler. Every ADO operation
// stays inside these scripted fakes; no SqlConnection, socket, database or web host is opened.
public static class UserUpdateMocks
{
    public static async Task RunAsync()
    {
        await RejectedAsync(true, false);
        await RejectedAsync(null, false);
        await RejectedAsync(false, true);
        await SameEmailAsync();
        await ForbiddenAsync(false);
        await ForbiddenAsync(true);
        await UnauthorizedAsync();
    }

    private static async Task RejectedAsync(bool? verified, bool verifiedWhileLocking)
    {
        var fixture=new Fixture(verified);
        if(verifiedWhileLocking)
            fixture.OnLock=()=>{fixture.User.EmailVerified=true;fixture.User.EmailVerifiedAt=Fixture.Epoch;};
        await using var db=fixture.Context();
        var service=fixture.Service(db);
        Exception? failure=null;
        try { await service.UpdateAsync(42,new UpdateUserRequest("changed","other@example.test","9123456789")); }
        catch(Exception ex) { failure=ex; }
        Program.Check(failure is BusinessRuleException,"verified or newly verified direct email edit reaches business-rule rejection");
        await ExpectStatusAsync(failure!,409);
        Program.Check(fixture.LockCalls==1 && fixture.ReadsAfterLock>=2 && fixture.TransactionDisposed,
            "rejection follows lock and persisted refresh and disposes transaction");
        Program.Check(fixture.SaveCalls==0 && !fixture.Committed && fixture.User.Email=="owner@example.test"
            && fixture.User.EmailVerified==(verifiedWhileLocking?true:verified)
            && fixture.User.EmailVerifiedAt==(verifiedWhileLocking?Fixture.Epoch:verified==true?Fixture.Epoch:null)
            && fixture.User.RoleId==2 && fixture.User.FullName=="owner" && fixture.User.Phone=="9111111111"
            && fixture.User.PendingEmail=="pending@example.test" && fixture.AuditUntouched,
            "rejected edit preserves email epoch role profile pending request audit and session identity");
    }

    private static async Task SameEmailAsync()
    {
        var fixture=new Fixture(true);
        await using var db=fixture.Context();
        Program.Check(await fixture.Service(db).UpdateAsync(42,
            new UpdateUserRequest("updated profile","OWNER@example.test","9222222222")),
            "legitimate case-insensitive same-email update succeeds");
        Program.Check(fixture.SaveCalls==1 && fixture.Committed && fixture.TransactionDisposed
            && fixture.User.FullName=="updated profile" && fixture.User.Phone=="9222222222",
            "same-email profile change saves once and commits");
        Program.Check(fixture.User.Email=="owner@example.test" && fixture.User.EmailVerified==true
            && fixture.User.EmailVerifiedAt==Fixture.Epoch && fixture.User.RoleId==2
            && fixture.User.PendingEmail=="pending@example.test" && fixture.AuditUntouched,
            "same-email edit preserves verification role pending request audit and session identity");
    }

    private static async Task ForbiddenAsync(bool roleNameChanged)
    {
        var fixture=new Fixture(true);
        fixture.OnLock=()=>{
            if(roleNameChanged) fixture.Roles[2].Name=RoleNames.SuperAdmin;
            else fixture.User.RoleId=3;
        };
        await using var db=fixture.Context();
        Exception? failure=null;
        try { await fixture.Service(db).UpdateAsync(42,new UpdateUserRequest("changed",fixture.User.Email,fixture.User.Phone)); }
        catch(Exception ex) { failure=ex; }
        Program.Check(failure is ForbiddenException,"authorization sees persisted role ID and refreshed role name under lock");
        await ExpectStatusAsync(failure!,403);
        Program.Check(fixture.SaveCalls==0 && !fixture.Committed && fixture.TransactionDisposed,
            "concurrent role restriction cannot save a profile update");
    }

    private static async Task UnauthorizedAsync()
    {
        var fixture=new Fixture(true);
        await using var db=fixture.Context();
        Exception? failure=null;
        try { await fixture.Service(db,RoleNames.Landlord).UpdateAsync(42,new UpdateUserRequest("changed",fixture.User.Email,fixture.User.Phone)); }
        catch(Exception ex) { failure=ex; }
        Program.Check(failure is ForbiddenException && fixture.SaveCalls==0 && fixture.TransactionDisposed,
            "ordinary account still cannot manage another user");
    }

    private static async Task ExpectStatusAsync(Exception failure,int expected)
    {
        using var services=new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var http=new DefaultHttpContext{RequestServices=services};
        await using var body=new MemoryStream();
        http.Response.Body=body;
        var handler=new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        Program.Check(await handler.TryHandleAsync(http,failure,CancellationToken.None)
            && http.Response.StatusCode==expected,"real exception handler returns expected isolated 409 or 403");
    }

    public class LockProbe : DispatchProxy
    {
        public Func<Task<bool>> Lock=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method?.Name!=nameof(IEmailVerificationService.LockAccountAsync) || args is not [int id, ..] || id!=42)
                throw new InvalidOperationException("Unexpected token or audit mutation in isolated profile test.");
            return Lock();
        }
    }

    private sealed class Fixture
    {
        public static readonly DateTime Epoch=new(2026,10,20,12,0,0,DateTimeKind.Utc);
        public User User;
        public readonly Dictionary<int,Role> Roles=new(){
            [2]=new(){Id=2,Name=RoleNames.Investor},[3]=new(){Id=3,Name=RoleNames.Admin}};
        public Action? OnLock;
        public int LockCalls,ReadsAfterLock,SaveCalls;
        public bool Locked,Committed,TransactionStarted,TransactionDisposed,AuditUntouched=true;
        public Fixture(bool? verified)=>User=new User{
            Id=42,RoleId=2,FullName="owner",Email="owner@example.test",Phone="9111111111",
            PasswordHash="isolated-unused",EmailVerified=verified,EmailVerifiedAt=verified==true?Epoch:null,
            PendingEmail="pending@example.test",IsApproved=true};
        public NebrasdbContext Context()=>new(new DbContextOptionsBuilder<NebrasdbContext>()
            .UseSqlServer(new MemoryConnection(this)).AddInterceptors(new SaveProbe(this)).Options);
        public UserService Service(NebrasdbContext db,string callerRole=RoleNames.Admin)
        {
            var proxy=DispatchProxy.Create<IEmailVerificationService,LockProbe>();
            ((LockProbe)(object)proxy).Lock=()=>{
                if(!TransactionStarted || TransactionDisposed) throw new InvalidOperationException("Lock outside transaction.");
                LockCalls++;Locked=true;OnLock?.Invoke();return Task.FromResult(true);
            };
            return new UserService(db,NullLogger<UserService>.Instance,new TestUser(900,callerRole),proxy,null!,null!);
        }

        public DbDataReader Read(string sql,DbParameterCollection parameters,Microsoft.EntityFrameworkCore.Metadata.IModel model)
        {
            if(!sql.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only scripted in-memory SELECT results are supported.");
            if(Locked) ReadsAfterLock++;
            var userRead=Regex.IsMatch(sql,@"FROM \[USERS\] AS \[\w+\]");
            var roleRead=Regex.IsMatch(sql,@"FROM \[ROLES\] AS \[\w+\]");
            if(!userRead && !roleRead) throw new InvalidOperationException("Unexpected mock table.");
            var keys=parameters.Cast<DbParameter>().Where(p=>p.Value is int).Select(p=>(int)p.Value!).ToArray();
            var strings=parameters.Cast<DbParameter>().Where(p=>p.Value is string).Select(p=>(string)p.Value!).ToArray();
            // The profile update's phone/address availability pre-checks are legitimate
            // existence reads on USERS. In the isolated fixture no other account exists, so
            // the predicate answers false; the identity parameter still must be the fixture's
            // user id and the shape still single-identity, so a wrongly-shaped query keeps
            // failing closed.
            if(userRead && Regex.IsMatch(sql,@"\bEXISTS\b") && keys.Length==1 && strings.Length is 1 or 2)
            {
                if(keys[0]!=User.Id)
                    throw new InvalidOperationException("Unexpected persisted identity lookup.");
                var bits=new DataTable();
                bits.Columns.Add("c0",typeof(bool));
                bits.Rows.Add(false);
                return bits.CreateDataReader();
            }
            if(keys.Length!=1 || keys[0]!=(userRead?User.Id:User.RoleId))
                throw new InvalidOperationException("Unexpected persisted identity lookup.");
            var projection=sql[..sql.IndexOf("FROM",StringComparison.Ordinal)];
            var roleAlias=Regex.Match(sql,@"(?:FROM|JOIN) \[ROLES\] AS \[(\w+)\]").Groups[1].Value;
            var fields=Regex.Matches(projection,@"\[(\w+)\]\.\[(\w+)\]");
            if(fields.Count==0) throw new InvalidOperationException("Unexpected mock projection.");
            var table=new DataTable();
            var values=new List<object>();
            foreach(Match field in fields)
            {
                var isRole=field.Groups[1].Value==roleAlias;
                var type=isRole?typeof(Role):typeof(User);
                var entity=model.FindEntityType(type)!;
                var tableId=StoreObjectIdentifier.Table(entity.GetTableName()!,entity.GetSchema());
                var property=entity.GetProperties().Single(p=>p.GetColumnName(tableId)==field.Groups[2].Value);
                table.Columns.Add("c"+values.Count,Nullable.GetUnderlyingType(property.ClrType)??property.ClrType);
                values.Add(property.PropertyInfo!.GetValue(isRole?Roles[User.RoleId]:User)??DBNull.Value);
            }
            table.Rows.Add(values.ToArray());
            return table.CreateDataReader();
        }
    }

    private sealed class SaveProbe(Fixture fixture) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result,CancellationToken token=default)
        {
            var entries=data.Context!.ChangeTracker.Entries().ToList();
            if(entries.Any(e=>e.Entity is Role && e.State!=EntityState.Unchanged) ||
                entries.Any(e=>e.Entity is not User && e.Entity is not Role && e.State!=EntityState.Unchanged))
            {fixture.AuditUntouched=false;throw new InvalidOperationException("Unexpected isolated write.");}
            var user=entries.Single(e=>e.Entity is User).Entity;
            foreach(var p in typeof(User).GetProperties().Where(p=>p.CanWrite&&(p.PropertyType.IsValueType||p.PropertyType==typeof(string))))
                p.SetValue(fixture.User,p.GetValue(user));
            fixture.SaveCalls++;
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }

    private sealed class MemoryConnection(Fixture fixture) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString {get;set;}="Server=isolated-mock;Database=isolated-mock";
        public override string Database=>"isolated-mock";
        public override string DataSource=>"no-network";
        public override string ServerVersion=>"16.0";
        public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;
        public override Task OpenAsync(CancellationToken token){token.ThrowIfCancellationRequested();Open();return Task.CompletedTask;}
        public override void Close()=>state=ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolation){fixture.TransactionStarted=true;return new MemoryTransaction(this,fixture,isolation);}
        protected override DbCommand CreateDbCommand()=>new MemoryCommand(this,fixture);
    }
    private sealed class MemoryTransaction(DbConnection connection,Fixture fixture,IsolationLevel isolation) : DbTransaction
    {
        public override IsolationLevel IsolationLevel=>isolation;
        protected override DbConnection DbConnection=>connection;
        public override void Commit()=>fixture.Committed=true;
        public override void Rollback(){ }
        protected override void Dispose(bool disposing){fixture.TransactionDisposed=true;base.Dispose(disposing);}
    }
    private sealed class MemoryCommand(MemoryConnection connection,Fixture fixture) : DbCommand
    {
        private readonly SqlCommand parameterOwner=new();
        [AllowNull] public override string CommandText {get;set;}="";
        public override int CommandTimeout {get;set;}
        public override CommandType CommandType {get;set;}
        public override bool DesignTimeVisible {get;set;}
        public override UpdateRowSource UpdatedRowSource {get;set;}
        protected override DbConnection? DbConnection {get;set;}=connection;
        protected override DbTransaction? DbTransaction {get;set;}
        protected override DbParameterCollection DbParameterCollection=>parameterOwner.Parameters;
        protected override DbParameter CreateDbParameter()=>new SqlParameter();
        public override void Cancel(){ }
        public override void Prepare(){ }
        public override int ExecuteNonQuery()=>throw new InvalidOperationException("External SQL is prohibited.");
        public override object ExecuteScalar()=>throw new InvalidOperationException("Unexpected scalar command.");
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            // Use the model only, never an actual database connection or database operation.
            using var modelContext=new NebrasdbContext(new DbContextOptionsBuilder<NebrasdbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=NIBRAS_F1_Verify;Integrated Security=True")
                .AddInterceptors(new NoConnections()).Options);
            return fixture.Read(CommandText,Parameters,modelContext.Model);
        }
        protected override void Dispose(bool disposing){if(disposing)parameterOwner.Dispose();base.Dispose(disposing);}
    }
}
