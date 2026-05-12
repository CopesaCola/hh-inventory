using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options) { }

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<DeviceTypeOption> DeviceTypeOptions => Set<DeviceTypeOption>();
    public DbSet<DeviceStatusOption> DeviceStatusOptions => Set<DeviceStatusOption>();
    public DbSet<DepartmentOption> DepartmentOptions => Set<DepartmentOption>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<FieldLabelOverride> FieldLabelOverrides => Set<FieldLabelOverride>();
    public DbSet<LdapSettings> LdapSettings => Set<LdapSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Site>().HasIndex(s => s.Name).IsUnique();
        b.Entity<UserProfile>().HasIndex(u => u.Username);
        b.Entity<UserProfile>().HasIndex(u => u.Email);

        b.Entity<Device>().HasIndex(d => d.SerialNumber);
        b.Entity<Device>().HasIndex(d => d.AssetTag);

        b.Entity<DeviceTypeOption>().HasIndex(t => t.Name).IsUnique();
        b.Entity<DeviceStatusOption>().HasIndex(s => s.Name).IsUnique();
        b.Entity<DepartmentOption>().HasIndex(d => d.Name).IsUnique();

        b.Entity<Device>()
            .HasOne(d => d.AssignedUser).WithMany(u => u.Devices)
            .HasForeignKey(d => d.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Device>()
            .HasOne(d => d.Site).WithMany(s => s.Devices)
            .HasForeignKey(d => d.SiteId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Device>()
            .HasOne(d => d.DeviceType).WithMany(t => t.Devices)
            .HasForeignKey(d => d.DeviceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Device>()
            .HasOne(d => d.Status).WithMany(s => s.Devices)
            .HasForeignKey(d => d.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<UserProfile>()
            .HasOne(u => u.Site).WithMany(s => s.Users)
            .HasForeignKey(u => u.SiteId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<UserProfile>()
            .HasOne(u => u.Department).WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<AuditEntry>()
            .HasOne(a => a.Device).WithMany(d => d.AuditEntries)
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<AuditEntry>().HasIndex(a => a.TimestampUtc);

        b.Entity<CustomFieldDefinition>().HasIndex(c => new { c.EntityType, c.Name }).IsUnique();
        b.Entity<CustomFieldValue>().HasIndex(c => new { c.DefinitionId, c.EntityType, c.EntityId }).IsUnique();
        b.Entity<CustomFieldValue>()
            .HasOne(v => v.Definition).WithMany(d => d.Values)
            .HasForeignKey(v => v.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<FieldLabelOverride>().HasIndex(l => new { l.EntityType, l.FieldKey }).IsUnique();
    }

    /// <summary>
    /// Apply schema changes introduced after the initial database was created.
    /// EnsureCreated() only adds tables when NO tables exist, so on an upgraded
    /// database neither new columns nor whole new tables get picked up. This
    /// helper runs idempotent DDL (gated by pragma_table_info / sqlite_master)
    /// so the live database catches up without losing data.
    /// </summary>
    public static async Task EnsureSchemaAsync(InventoryDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        async Task EnsureColumnAsync(string table, string column, string definition)
        {
            using var check = conn.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;
            if (exists) return;

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
            await alter.ExecuteNonQueryAsync();
        }

        async Task EnsureTableAsync(string table, string createDdl)
        {
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            var p = check.CreateParameter(); p.ParameterName = "$name"; p.Value = table;
            check.Parameters.Add(p);
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;
            if (exists) return;

            using var create = conn.CreateCommand();
            create.CommandText = createDdl;
            await create.ExecuteNonQueryAsync();
        }

        await EnsureColumnAsync("Devices", "GrantOrDeptFund", "TEXT NULL");

        // LDAP / Active Directory integration. Singleton table; Id is always 1.
        await EnsureTableAsync("LdapSettings", @"
            CREATE TABLE ""LdapSettings"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LdapSettings"" PRIMARY KEY,
                ""IsEnabled"" INTEGER NOT NULL DEFAULT 0,
                ""Server"" TEXT NULL,
                ""Port"" INTEGER NOT NULL DEFAULT 389,
                ""UseSsl"" INTEGER NOT NULL DEFAULT 0,
                ""BindDn"" TEXT NULL,
                ""BindPasswordEncrypted"" BLOB NULL,
                ""BaseDn"" TEXT NULL,
                ""UserFilter"" TEXT NULL,
                ""FullNameAttribute"" TEXT NOT NULL DEFAULT 'displayName',
                ""UsernameAttribute"" TEXT NOT NULL DEFAULT 'sAMAccountName',
                ""EmailAttribute"" TEXT NOT NULL DEFAULT 'mail',
                ""DepartmentAttribute"" TEXT NOT NULL DEFAULT 'department',
                ""LastTestUtc"" TEXT NULL,
                ""LastTestMessage"" TEXT NULL,
                ""LastSyncUtc"" TEXT NULL,
                ""LastSyncMessage"" TEXT NULL
            );");
    }

    public static async Task SeedDefaultsAsync(InventoryDbContext db)
    {
        if (!await db.DeviceStatusOptions.AnyAsync())
        {
            db.DeviceStatusOptions.AddRange(
                new DeviceStatusOption { Name = "InUse", BadgeClass = "badge", DisplayOrder = 0 },
                new DeviceStatusOption { Name = "Spare", BadgeClass = "badge spare", DisplayOrder = 10 },
                new DeviceStatusOption { Name = "In Repair", BadgeClass = "badge repair", DisplayOrder = 20 },
                new DeviceStatusOption { Name = "Retired", BadgeClass = "badge retired", DisplayOrder = 30 },
                new DeviceStatusOption { Name = "Lost or Stolen", BadgeClass = "badge lost", DisplayOrder = 40 }
            );
        }

        if (!await db.DeviceTypeOptions.AnyAsync())
        {
            string[] defaults = { "Workstation", "Laptop", "Monitor", "Printer", "Server",
                                  "Switch", "UPS", "Phone", "Scanner", "Tablet" };
            for (int i = 0; i < defaults.Length; i++)
            {
                db.DeviceTypeOptions.Add(new DeviceTypeOption { Name = defaults[i], DisplayOrder = i * 10 });
            }
        }

        // LdapSettings is a singleton (Id = 1). Inserting an empty default row keeps
        // GetOrCreateAsync simple — no nullable handling at the call site.
        if (!await db.LdapSettings.AnyAsync())
        {
            db.LdapSettings.Add(new LdapSettings());
        }

        await db.SaveChangesAsync();
    }
}
