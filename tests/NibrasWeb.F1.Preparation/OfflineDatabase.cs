using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using NIBRAS.Models;

namespace NibrasWeb.F1.Preparation;

// In-process ADO fakes only. No SqlConnection or socket is created.
// SELECTs are projected from EF's own mappings; unexpected SQL fails closed.
internal sealed class OfflineDatabase
{
    public List<User> Users { get; } = [];
    public List<Land> Lands { get; } = [];
    public List<LandCriterion> Criteria { get; } = [];
    public List<UserSecurityEvent> Events { get; } = [];
    public bool Committed, Disposed, LockedRange, TransactionStarted;
    public IsolationLevel Isolation;
    public int Saves;
    public NebrasdbContext Context() => new(new DbContextOptionsBuilder<NebrasdbContext>()
        .UseSqlServer(new Connection(this)).AddInterceptors(new Save(this)).Options);

    private DbDataReader Read(string sql, DbParameterCollection parameters)
    {
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected offline operation.");
        using var modelDb=new ModelOnlyFactory().CreateDbContext([]);
        var model=modelDb.Model;
        var roleAlias=Regex.Match(sql, @"(?:FROM|JOIN) \[ROLES\] AS \[(\w+)\]").Groups[1].Value;
        var source=sql.Contains("[LAND_CRITERIA]") ? typeof(LandCriterion) :
            sql.Contains("[USERS]") ? typeof(User) : sql.Contains("[ROLES]") ? typeof(Role) : sql.Contains("[LANDS]") ? typeof(Land) : throw new InvalidOperationException("Unexpected offline table.");
        var from=Regex.Match(sql,@"(?m)^FROM\s").Index;
        if(from<=0) throw new InvalidOperationException("Unexpected SELECT layout.");
        var projection=sql[..from];
        var fields=Regex.Matches(projection, @"\[(\w+)\]\.\[(\w+)\]");
        if (fields.Count==0) throw new InvalidOperationException("Unexpected offline projection.");
        LockedRange |= sql.Contains("UPDLOCK, HOLDLOCK");
        var aliases=Regex.Matches(sql,@"(?:FROM|JOIN) \[(\w+)\] AS \[(\w+)\]")
            .Cast<Match>().ToDictionary(m => m.Groups[2].Value,
                m => model.GetEntityTypes().Single(e => e.GetTableName()==m.Groups[1].Value).ClrType);
        var properties=fields.Cast<Match>().Select(m => {
            var type=aliases.GetValueOrDefault(m.Groups[1].Value) ?? source;
            var entity=model.FindEntityType(type)!;
            var table=StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return (type, property:entity.GetProperties().Single(p => p.GetColumnName(table)==m.Groups[2].Value));
        }).ToList();
        var tableResult=new DataTable();
        foreach(var (_,property) in properties)
            tableResult.Columns.Add("c"+tableResult.Columns.Count, property.ClrType.IsEnum ? Enum.GetUnderlyingType(property.ClrType) : Nullable.GetUnderlyingType(property.ClrType)??property.ClrType);
        IEnumerable<object> rows=source==typeof(User) ? Users : source==typeof(Role) ? Users.Select(u => u.Role).DistinctBy(r => r.Id) : source==typeof(Land) ? Lands : Criteria.OrderByDescending(c => c.Id);
        if (source==typeof(User)) {
            var ids=parameters.Cast<DbParameter>().Where(p => p.Value is int).Select(p => (int)p.Value!).ToList();
            if (ids.Count==1) rows=Users.Where(u => u.Id==ids[0]);
            else if (!sql.Contains("IN (") && !sql.Contains("OPENJSON")) throw new InvalidOperationException("Unexpected offline identity predicate.");
        }
        if (source==typeof(Role)) {
            var id=parameters.Cast<DbParameter>().Where(p => p.Value is int).Select(p => (int)p.Value!).Single();
            rows=rows.Cast<Role>().Where(r => r.Id==id);
        }
        if (source==typeof(LandCriterion) || source==typeof(Land)) {
            var ids=parameters.Cast<DbParameter>().Where(p => p.Value is int).Select(p => (int)p.Value!).Distinct().ToList();
            if(ids.Count==1) rows=rows.Where(r => r is LandCriterion c ? c.Id==ids[0] : ((Land)r).Id==ids[0]);
            if(source==typeof(LandCriterion)) rows=rows.Take(1);
        }
        foreach(var row in rows)
            tableResult.Rows.Add(properties.Select(p => ReadProperty(p.type,p.property,row)).ToArray());
        return tableResult.CreateDataReader();
    }

    private static object ReadProperty(Type type, Microsoft.EntityFrameworkCore.Metadata.IProperty property, object row)
    {
        object? entity=row;
        if(row is User user && type==typeof(Role)) entity=user.Role;
        if(row is Land land) entity=type==typeof(Region) ? land.Region : type==typeof(Governorate) ? land.Governorate : row;
        var value=entity is null ? null : property.PropertyInfo!.GetValue(entity);
        return value is Enum e ? Convert.ChangeType(e,Enum.GetUnderlyingType(e.GetType())) : value??DBNull.Value;
    }

    private sealed class Save(OfflineDatabase db) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token=default)
        {
            db.Saves++;
            foreach(var entry in data.Context!.ChangeTracker.Entries().ToList()) {
                if (entry.State==EntityState.Added && entry.Entity is LandCriterion c) {
                    entry.Property(nameof(LandCriterion.Id)).CurrentValue=db.Criteria.Select(x => x.Id).DefaultIfEmpty().Max()+1;
                    entry.Property(nameof(LandCriterion.Id)).IsTemporary=false;
                    db.Criteria.Add(c);
                } else if (entry.State==EntityState.Added && entry.Entity is UserSecurityEvent e) {
                    entry.Property(nameof(UserSecurityEvent.Id)).CurrentValue=db.Events.Count+1;
                    entry.Property(nameof(UserSecurityEvent.Id)).IsTemporary=false; db.Events.Add(e);
                } else if (entry.State==EntityState.Modified && entry.Entity is User user) {
                    var persisted=db.Users.Single(u => u.Id==user.Id);
                    foreach(var p in typeof(User).GetProperties().Where(p => p.CanWrite && (p.PropertyType.IsValueType || p.PropertyType==typeof(string))))
                        p.SetValue(persisted,p.GetValue(user));
                } else if (entry.State is not (EntityState.Unchanged or EntityState.Detached))
                    throw new InvalidOperationException("Unexpected offline write.");
                entry.State=EntityState.Unchanged;
            }
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
    private sealed class Connection(OfflineDatabase db) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString {get;set;}="Server=offline;Database=offline";
        public override string Database=>"offline";
        public override string DataSource=>"no-network";
        public override string ServerVersion=>"16.0";
        public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;
        public override Task OpenAsync(CancellationToken token){token.ThrowIfCancellationRequested();Open();return Task.CompletedTask;}
        public override void Close()=>state=ConnectionState.Closed;
        public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)
        {db.TransactionStarted=true;db.Isolation=level;return new Transaction(this,db,level);}
        protected override DbCommand CreateDbCommand()=>new Command(this,db);
    }
    private sealed class Transaction(DbConnection connection,OfflineDatabase db,IsolationLevel level) : DbTransaction
    {
        public override IsolationLevel IsolationLevel=>level;
        protected override DbConnection DbConnection=>connection;
        public override void Commit()=>db.Committed=true;
        public override void Rollback(){}
        protected override void Dispose(bool disposing){db.Disposed=true;base.Dispose(disposing);}
    }
    private sealed class Command(Connection connection,OfflineDatabase db) : DbCommand
    {
        private readonly SqlCommand owner=new(); // Parameter collection only, never connected.
        [AllowNull] public override string CommandText {get;set;}="";
        public override int CommandTimeout {get;set;}
        public override CommandType CommandType {get;set;}
        public override bool DesignTimeVisible {get;set;}
        public override UpdateRowSource UpdatedRowSource {get;set;}
        protected override DbConnection? DbConnection {get;set;}=connection;
        protected override DbTransaction? DbTransaction {get;set;}
        protected override DbParameterCollection DbParameterCollection=>owner.Parameters;
        protected override DbParameter CreateDbParameter()=>new SqlParameter();
        public override void Cancel(){}
        public override void Prepare(){}
        public override int ExecuteNonQuery()=>throw new InvalidOperationException("External SQL prohibited.");
        public override object ExecuteScalar()=>throw new InvalidOperationException("Unexpected offline scalar.");
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>db.Read(CommandText,Parameters);
        protected override void Dispose(bool disposing){if(disposing)owner.Dispose();base.Dispose(disposing);}
    }
}
