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

        // Device.Suite is a separate optional link to a UserProfile row with
        // Kind = Suite, used for "located in this sub-area". No inverse navigation
        // on UserProfile — we query devices by SuiteId on demand. SetNull on
        // delete so removing a suite leaves devices intact at the site.
        b.Entity<Device>()
            .HasOne(d => d.Suite).WithMany()
            .HasForeignKey(d => d.SuiteId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<UserProfile>()
            .HasOne(u => u.Site).WithMany(s => s.Users)
            .HasForeignKey(u => u.SiteId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<UserProfile>()
            .HasOne(u => u.Department).WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-referencing FK: a Person can belong to a Suite (also a UserProfile row).
        // Deleting a Suite orphans its members rather than cascading.
        b.Entity<UserProfile>()
            .HasOne(u => u.Suite).WithMany(s => s.Members)
            .HasForeignKey(u => u.SuiteId)
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
    /// Add columns introduced after the initial schema. EnsureCreated() only
    /// creates tables that don't yet exist — it does NOT add new columns to
    /// existing tables. This helper runs idempotent ALTER TABLE statements so
    /// the live SQLite database stays in sync without dropping data.
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

        await EnsureColumnAsync("Devices", "GrantOrDeptFund", "TEXT NULL");

        // Suites-as-UserProfile: Kind distinguishes Person from Suite; SuiteId is
        // a self-referencing FK to a Suite row that the Person belongs to.
        await EnsureColumnAsync("UserProfiles", "Kind", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync("UserProfiles", "SuiteId", "INTEGER NULL");

        // Devices can also be located in a Suite (independent of AssignedUser).
        await EnsureColumnAsync("Devices", "SuiteId", "INTEGER NULL");
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

        await db.SaveChangesAsync();
    }
}
