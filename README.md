# Inventory

Internal IT inventory web app — devices, users, sites, audit history, bulk CSV/XLSX import.

- **Stack:** ASP.NET Core 8 (Razor Pages) + EF Core + SQLite
- **Auth:** Windows Authentication (Negotiate). Microsoft Entra SSO is planned for v2.
- **Hosting target:** Windows Server (IIS or as a Windows Service)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (development)
- [.NET 8 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) (server)

## Run locally

```powershell
cd src\Inventory.Web
dotnet run
```

Then open <http://localhost:5050>.

The SQLite database `inventory.db` is created automatically next to the binary on first run via `EnsureCreated()`. To reset, delete the file.

> **Auth note:** Negotiate auth requires the browser to send Windows credentials. Edge, Chrome, and Firefox do this automatically for `localhost`. If running over Kestrel without IIS, use a domain-joined machine (or sign in with a local account that exists in the host).

## Data model

| Entity | Key fields |
|---|---|
| **Device** | DeviceType (FK→DeviceTypeOption), Model, SerialNumber, AssetTag, Status (FK→DeviceStatusOption), LocationWithinSite, WindowsVersion, IsGrantFunded, AssignedUser, Site, audit fields |
| **UserProfile** | FullName, Username, Email, Department (FK→DepartmentOption), Site |
| **Site** | Name (unique), Address |
| **AuditEntry** | Device, TimestampUtc, ModifiedBy, Action (Created/Updated/Deleted/Imported), Changes (JSON) |
| **DeviceTypeOption / DeviceStatusOption / DepartmentOption** | Lookup tables editable via the **Settings** page |
| **CustomFieldDefinition / CustomFieldValue** | User-defined extra fields on Device, UserProfile, or Site |
| **FieldLabelOverride** | Renames built-in field labels for the entity it applies to |

Every device write produces an `AuditEntry`, viewable on the device's Details page.

## Bulk import — generic

- Endpoint: **Import** in the nav
- Formats: `.csv`, `.xlsx`
- Required columns: `DeviceType`, `Model`, plus at least one of `SerialNumber` or `AssetTag`
- Optional columns: `Status, LocationWithinSite, WindowsVersion, GrantOrDeptFund, AssignedUser, Site`
- Matching: existing devices matched by **SerialNumber** (then **AssetTag** if no serial). Match → update; otherwise insert.
- Sites and Users referenced in the import are auto-created if not found
- Status values: `InUse`, `Spare`, `InRepair`, `Retired`, `LostOrStolen`

A blank template can be downloaded from the Import page.

## Bulk import — Hope Health workbook

- Endpoint: **HH Workbook Import** (`/Import/HopeHealth`)
- For the `HH IT Inventory.xlsx` multi-sheet workbook
- **Always run with "Dry run" first** — it reports per-sheet insert/update/skip counts so you can spot bad data before committing
- Sheet handling:
  - Per-site sheets (Manning, Aiken, Lake City & LCSB, …) → sheet name is the Site
  - Aggregate sheets (`IT Master`, `New 2024`, `New 2025`, `Remote`) read the explicit `Location` column for Site
  - `IT Cage (In-House)` → site = "IT Cage (In-House)", devices imported from sectional banners
  - `IT Cage Master` and `South Inventory` → skipped
- Type code column (`WS / LT / Pntr / Monitor / Sw / …`) is normalized; raw value preserved on the device
- Anything in the user-name column becomes a `UserProfile` (including role labels like "Pharmacy", "EKG LCSB" — clean up via the Users page after)
- Rows in the `Remove from Inventory` column → `Status = Retired` and `RemovedFromInventory = true`
- "Skip IT Master" toggle on the page lets you avoid the consolidated master sheet if you only want per-site data

## Deploying to Windows Server (IIS)

1. Install the **.NET 8 ASP.NET Core Hosting Bundle** on the server.
2. From a dev machine: `dotnet publish src\Inventory.Web -c Release -o publish`
3. Copy `publish\` to the server (e.g. `C:\inetpub\Inventory`).
4. In IIS Manager: create an Application Pool with **No Managed Code**, identity **ApplicationPoolIdentity** (or a domain service account if SQLite needs to live on a network share — not recommended; keep `inventory.db` local).
5. Add a Site pointing at the publish folder; bind to port 80/443.
6. Enable **Windows Authentication** on the site, disable **Anonymous Authentication**.
7. Grant the App Pool identity Read/Write on the site folder so SQLite can create/open `inventory.db`.

## Settings

The **Settings** page (in the top nav) lets all signed-in staff manage:

- **Device Types** — list shown when adding/editing devices
- **Statuses** — including badge color; pre-seeded with In Use, Spare, In Repair, Retired, Lost or Stolen
- **Departments** — list shown on user profiles
- **Custom Fields** — add Text or Yes/No fields to Devices, Users, or Sites; values render below built-in fields on Create/Edit/Details
- **Field Labels** — rename any built-in field's display label (e.g. "Asset Tag" → "Property Tag")

Lookup deletes are blocked while in use; hide an option (uncheck Active) to keep history intact while removing it from new dropdowns.

## Path to v2 (Microsoft Entra SSO)

The auth handler is isolated in `Program.cs`. To swap from Windows auth to Entra ID:

1. Add packages: `Microsoft.Identity.Web` + `Microsoft.Identity.Web.UI`.
2. Replace the `AddAuthentication(NegotiateDefaults...).AddNegotiate()` call with `AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))`.
3. Add `AzureAd` config section (TenantId, ClientId, ClientSecret, Instance, CallbackPath).
4. `ICurrentUser.Name` already reads `User.Identity.Name`, which Entra populates with the user's UPN — no changes needed in audit/import code.

## File layout

```
src/Inventory.Web/
├── Program.cs                  # composition root
├── appsettings.json            # connection string, logging
├── Data/InventoryDbContext.cs  # EF Core context, indexes, FKs
├── Models/                     # Device, UserProfile, Site, AuditEntry
├── Services/                   # ICurrentUser, AuditService, ImportService
├── Pages/                      # Razor Pages (Devices, Users, Sites, Import)
└── wwwroot/css/site.css
```
