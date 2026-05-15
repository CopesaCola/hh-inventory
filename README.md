# Inventory

Internal IT inventory web app — devices, user profiles, sites, audit history, bulk CSV/XLSX import, plus a small dashboard and global search.

- **Stack:** ASP.NET Core 8 (Razor Pages) + EF Core + SQLite. Excel parsing via ClosedXML.
- **Auth:** Windows Authentication (Negotiate). Microsoft Entra SSO is planned for v2.
- **Hosting target:** Windows Server (IIS or as a Windows Service).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (development)
- [.NET 8 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) (server)

## Run locally

```powershell
cd src\Inventory.Web
dotnet run
```

Then open <http://localhost:5050>.

The SQLite database `inventory.db` is created automatically next to the binary on first run. Three things happen at startup:

1. `EnsureCreated()` — creates any missing tables.
2. `EnsureSchemaAsync()` — adds any columns introduced after the initial schema (idempotent `ALTER TABLE`s, see [Schema migrations](#schema-migrations)).
3. `SeedDefaultsAsync()` — seeds default device statuses and types if those lookup tables are empty.

To reset, stop the app and delete `inventory.db`.

> **Auth note:** Negotiate requires the browser to send Windows credentials. Edge, Chrome, and Firefox do this automatically for `localhost`. If running over Kestrel without IIS, use a domain-joined machine (or sign in with a local account that exists on the host).

## App overview

**Dashboard (home page).** Three KPI tiles for total Devices / User Profiles / Sites (each clickable to its index), a horizontal bar chart of devices by status, a bar chart of devices by site (top 10 with an "Other (N sites)" bar collapsing the rest), and the 20 most recent audit entries. Removed devices are excluded from the totals and charts so the dashboard reflects the live, working inventory.

**Global search.** The input in the top nav suggests matching Devices, Users, and Sites in grouped sections as you type (≥2 chars). Pressing Enter or clicking "See all results" goes to `/Search`, which has the full results page with per-section CSV export. The dropdown uses `autocomplete="off"` so the browser's own history doesn't pollute the suggestions.

**Per-entity search & filters.** Each index page (`/Devices`, `/Users`, `/Sites`) has the same typeahead dropdown scoped to that entity in a single search row. Click **Filters** to open a collapsible panel below the search bar — Devices has Site / Type / Status, Users has Site, Sites has none. If the URL already carries an active filter, the panel auto-expands so you can see why results are filtered.

**Row navigation.** Every row in every index/search table is clickable and goes straight to the Details page for that entity. No per-row action buttons — Edit/Reassign/Delete live on the Details or Edit page. Ctrl/middle-click opens in a new tab.

**CSV export.** Every index page and the global Search page has an **Export CSV** button that respects the current query and filters.

**Destructive actions** (delete device / user / site, delete a lookup option in Settings, delete a custom field, restore database from backup) all prompt for confirmation before running.

## Data model

| Entity | Key fields |
|---|---|
| **Device** | DeviceType (FK→DeviceTypeOption), Model, SerialNumber, AssetTag, Status (FK→DeviceStatusOption), LocationWithinSite, WindowsVersion, IsGrantFunded, GrantOrDeptFund, RemovedFromInventory, AssignedUser, Site, audit fields |
| **UserProfile** | FullName, Username, Email, Department (FK→DepartmentOption), Site |
| **Site** | Name (unique), Address |
| **AuditEntry** | Device, TimestampUtc, ModifiedBy, Action (Created/Updated/Deleted/Imported), Changes (JSON) |
| **DeviceTypeOption / DeviceStatusOption / DepartmentOption** | Lookup tables editable via the **Settings** page |
| **CustomFieldDefinition / CustomFieldValue** | User-defined extra fields on Device, UserProfile, or Site |
| **FieldLabelOverride** | Renames built-in field labels for the entity it applies to |

Every device write produces an `AuditEntry`, viewable on the device's Details page.

**Grant/Dept funded.** When **Grant or Dept Funded** is set to **Yes** on Device Create/Edit, a follow-up text input ("Grant or Dept name") appears asking you to specify which grant or department (e.g. `HRSA`, `Pharmacy`, `Clinical Trials`). The text is cleared automatically if you switch the flag back to No.

**Removed devices.** `RemovedFromInventory` is set by the HopeHealth workbook import for rows in its `Remove from Inventory` column. There is no UI to toggle it manually. Removed devices are hidden from the Devices index, hidden from the dashboard totals/charts, and shown with a red **Removed** badge on the Site / User Details device tables and on the Device's own Details page.

**Cascade behavior on delete:**
- Deleting a **User** sets `Device.AssignedUserId = NULL` on their devices (devices stay, become unassigned).
- Deleting a **Site** sets both `Device.SiteId` and `UserProfile.SiteId` to NULL (devices/users stay, lose the location link).
- Deleting a **Device** removes its `AuditEntry` rows (cascade).
- Deleting a **DeviceType / Status / Department / CustomField** is permitted; references on existing rows become unset.

## Bulk import — generic

- Endpoint: **Import** in the nav (`/Import`)
- Formats: `.csv`, `.xlsx`
- Required columns: `DeviceType`, `Model`, plus at least one of `SerialNumber` or `AssetTag`
- Optional columns: `Status, LocationWithinSite, WindowsVersion, IsGrantFunded, AssignedUser, Site`
- Matching: existing devices matched by **SerialNumber** (then **AssetTag** if no serial). Match → update; otherwise insert.
- Sites and Users referenced in the import are auto-created if not found.
- Status values: `InUse`, `Spare`, `InRepair`, `Retired`, `LostOrStolen`.

A blank template can be downloaded from the Import page.

## Bulk import — Hope Health workbook

- Endpoint: **HH Workbook Import** (`/Import/HopeHealth`)
- For the `HH IT Inventory.xlsx` multi-sheet workbook
- **Always run with "Dry run" first** — it reports per-sheet insert/update/skip counts so you can spot bad data before committing.
- Sheet handling:
  - Per-site sheets (Manning, Aiken, Lake City & LCSB, …) → sheet name is the Site.
  - Aggregate sheets (`IT Master`, `New 2024`, `New 2025`, `Remote`) read the explicit `Location` column for Site.
  - `IT Cage (In-House)` → site = "IT Cage (In-House)", devices imported from sectional banners.
  - `IT Cage Master` and `South Inventory` → skipped.
- Type-code column (`WS / LT / Pntr / Monitor / Sw / …`) is normalized; the raw value is preserved on the device.
- Anything in the user-name column becomes a `UserProfile` (including role labels like "Pharmacy", "EKG LCSB" — clean up via the Users page after).
- Rows in the `Remove from Inventory` column → `RemovedFromInventory = true` and Status set to `Retired`. After import those rows are hidden from the Devices index but their badges still show on Site / User Details.
- Workbook's grant column populates both `IsGrantFunded = true` and the `GrantOrDeptFund` text field with the column's value.
- "Skip IT Master" toggle on the page lets you avoid the consolidated master sheet if you only want per-site data.

## Settings

The **Settings** page (in the top nav) lets all signed-in staff manage:

- **Device Types** — list shown when adding/editing devices.
- **Statuses** — including badge color; pre-seeded with In Use, Spare, In Repair, Retired, Lost or Stolen.
- **Departments** — list shown on user profiles.
- **Custom Fields** — add Text or Yes/No fields to Devices, Users, or Sites; values render below built-in fields on Create/Edit/Details.
- **Field Labels** — rename any built-in field's display label (e.g. "Asset Tag" → "Property Tag").
- **Database** — back up the SQLite file or restore from a backup (see below).

Lookup tables (Device Types / Statuses / Departments) support inline edit + delete with confirm. To preserve history while removing an option from new dropdowns, uncheck its **Active** flag instead of deleting.

## Database backup & restore

**Settings → Database** exposes two operations on the SQLite file backing the app.

**Backup.** Issues `VACUUM INTO` against the live database to produce a consistent, defragmented snapshot, then streams it to your browser as `inventory_backup_<timestamp>.db`. Safe to run while the app is in use (VACUUM INTO works with concurrent readers).

**Restore.** Upload any older `.db` backup. The page:

1. Validates the file starts with the SQLite magic header (`SQLite format 3\0`).
2. Opens the uploaded file in an isolated EF context and runs `EnsureCreated()` + `EnsureSchemaAsync()` + `SeedDefaultsAsync()` against it, so backups that predate the current schema get migrated forward (missing tables created, missing columns added, default lookups seeded) without losing existing data.
3. Calls `SqliteConnection.ClearAllPools()` to release file handles on the live DB.
4. Renames the existing `inventory.db` to `inventory_replaced_<timestamp>.db` (safety copy in the same folder).
5. Renames the migrated upload into place as `inventory.db`.

If anything goes wrong the page tries to roll back automatically and surfaces the error in a flash. Manual recovery if needed: stop the app, delete `inventory.db`, rename `inventory_replaced_<timestamp>.db` back to `inventory.db`, restart.

## Schema migrations

Schema changes after v1 are applied via `InventoryDbContext.EnsureSchemaAsync(db)`, which runs idempotent `ALTER TABLE ... ADD COLUMN` statements gated by a `pragma_table_info` lookup. The method runs at startup on the live DB and is also invoked on uploaded backups during Restore.

To add a new column in a future change, append one line to that method, e.g.:

```csharp
await EnsureColumnAsync("Devices", "WarrantyExpires", "TEXT NULL");
```

`EnsureCreated()` handles new tables on its own; only column additions need an entry here.

## Deploying to Windows Server (IIS)

1. Install the **.NET 8 ASP.NET Core Hosting Bundle** on the server.
2. From a dev machine: `dotnet publish src\Inventory.Web -c Release -o publish`
3. Copy `publish\` to the server (e.g. `C:\inetpub\Inventory`).
4. In IIS Manager: create an Application Pool with **No Managed Code**, identity **ApplicationPoolIdentity** (or a domain service account if SQLite needs to live on a network share — not recommended; keep `inventory.db` local).
5. Add a Site pointing at the publish folder; bind to port 80/443.
6. Enable **Windows Authentication** on the site, disable **Anonymous Authentication**.
7. Grant the App Pool identity Read/Write on the site folder so SQLite can create/open `inventory.db`.

Pre-deployment checklist: back up the live `inventory.db` via Settings → Database, then deploy. If the new build adds columns, startup will run `EnsureSchemaAsync` and the database will pick them up in place — no manual migration step.

## Path to v2 (Microsoft Entra SSO)

The auth handler is isolated in `Program.cs`. To swap from Windows auth to Entra ID:

1. Add packages: `Microsoft.Identity.Web` + `Microsoft.Identity.Web.UI`.
2. Replace the `AddAuthentication(NegotiateDefaults...).AddNegotiate()` call with `AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))`.
3. Add `AzureAd` config section (TenantId, ClientId, ClientSecret, Instance, CallbackPath).
4. `ICurrentUser.Name` already reads `User.Identity.Name`, which Entra populates with the user's UPN — no changes needed in audit/import code.

## File layout

```
src/Inventory.Web/
├── Program.cs                     # composition root, startup migrations, /api/search endpoints
├── appsettings.json               # connection string, logging
├── Data/InventoryDbContext.cs     # EF Core context, indexes, FKs, EnsureSchemaAsync, SeedDefaults
├── Models/                        # Device, UserProfile, Site, AuditEntry, lookup options, custom fields
├── Services/                      # ICurrentUser, AuditService, ImportService, HopeHealthImportService,
│                                  # CustomFieldService, LabelService, CsvExporter, LookupResolver
├── Pages/
│   ├── Index.cshtml(.cs)          # dashboard (KPIs, status/site charts, recent activity)
│   ├── Search.cshtml(.cs)         # global search results + CSV export per section
│   ├── Devices/                   # Index, Details, Create, Edit, Reassign
│   ├── Users/                     # Index, Details, Create, Edit
│   ├── Sites/                     # Index, Details, Create, Edit
│   ├── Import/                    # Index (generic CSV/XLSX), HopeHealth (workbook-specific)
│   ├── Settings/                  # Index, DeviceTypes, Statuses, Departments,
│   │                              # CustomFields, Labels, Database (backup/restore)
│   └── Shared/_Layout.cshtml      # nav, global search, flash messages
└── wwwroot/
    ├── css/site.css               # all styles (KPI tiles, bar charts, suggest dropdowns, …)
    └── js/site.js                 # row-click navigation, comboboxes, suggest dropdowns,
                                   # filter-panel toggle, grant-funded text-field show/hide
```
