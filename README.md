# MediaPager.App.Core

The host-side **domain** for MediaPager. Plugins never reference this; hosts (the web API,
future agents/scanners, launchers) reference it directly. It owns the data model, the
database, the settings stores, and the plugin host.

## What's inside

| Area | Namespace | Contents |
|---|---|---|
| **Domain model** | `MediaPager.App.Core.Models` | EF entities + the single `AuthDbContext`: catalogs, catalog items, users (invite-only ASP.NET Identity `AppUser`). |
| **Migrations** | `MediaPager.App.Core.Migrations` | EF Core SQLite migrations. Applied at startup via `MigrateAsync`; PostgreSQL migrations live in the API assembly. |
| **Settings** | `MediaPager.App.Core.Services` | `IRuntimeSettings`/`RuntimeSettingKeys` (whitelisted runtime settings), `PluginSettingsService` (free-form `plugins.*` settings), and `PluginDeployer` (shared install pipeline: clone → publish → copy dll into a plugins directory). |
| **Plugin host** | `MediaPager.App.Core.Plugins` | `PluginRegistry`/`IPluginHost` (loaded-plugin catalog) + `PluginActivityStore` (generic plugin jobs and notifications) + `PluginCatalog` + dependency-ordered `PluginLoader`. |
| **Subtitles** | `MediaPager.App.Core.Subtitles` | `WebVtt.ConvertSrt` — host-side SRT→WebVTT conversion for browser `<track>` elements. |

## Domain model highlights

- **Single `AuthDbContext`** (EF Core 10); SQLite and PostgreSQL providers use separate migrations for the same model. Lookups are seeded at startup, idempotently.
- When changing the data model, scaffold a migration for **both** providers. SQLite migrations
  stay in Core; PostgreSQL migrations and its model snapshot are in the API assembly:

  ```sh
  MEDIAPAGER_Database__Provider=Sqlite dotnet ef migrations add AddFeature \
    --project MediaPager.App.Core/MediaPager.App.Core.csproj \
    --startup-project MediaPager.App.Api/MediaPager.App.Api.csproj

  MEDIAPAGER_DB_PATH= MEDIAPAGER_Database__Provider=PostgreSQL \
    MEDIAPAGER_ConnectionStrings__AuthDatabase='Host=localhost;Database=mediapager;Username=postgres;Password=postgres' \
    dotnet ef migrations add AddFeature \
    --project MediaPager.App.Api/MediaPager.App.Api.csproj \
    --startup-project MediaPager.App.Api/MediaPager.App.Api.csproj \
    --output-dir Migrations/PostgreSql
  ```

  Verify both snapshots after a model change:

  ```sh
  MEDIAPAGER_Database__Provider=Sqlite dotnet ef migrations has-pending-model-changes \
    --project MediaPager.App.Core/MediaPager.App.Core.csproj \
    --startup-project MediaPager.App.Api/MediaPager.App.Api.csproj

  MEDIAPAGER_DB_PATH= MEDIAPAGER_Database__Provider=PostgreSQL \
    MEDIAPAGER_ConnectionStrings__AuthDatabase='Host=localhost;Database=mediapager;Username=postgres;Password=postgres' \
    dotnet ef migrations has-pending-model-changes \
    --project MediaPager.App.Api/MediaPager.App.Api.csproj \
    --startup-project MediaPager.App.Api/MediaPager.App.Api.csproj
  ```
- **Catalogs** — user-owned libraries ("My Movies") typed by `CatalogType` (Movies, TV Shows,
  Music, Podcasts, Audiobooks, Books), each with a derived `MediaType` (Video/Audio/Book).
  `Catalog.Path` is a folder for the future local scanner. Nav order = personal
  `CatalogOrder` overrides + creation order.
- **CatalogItem** — one row per library entry (movie/show/episode/track/book); `StoragePath`
  unique, `ParentId` for show→season→episode; editable metadata, tags, per-user ratings,
  poster/backdrop artwork.
- **Users** — invite-only Identity, JWT with `scope` claims (`admin:super` bypasses all
  policies).

## Plugin settings

`PluginSettingsService` backs `IPluginSettingsStore`: keys are `plugins.<pluginKey>.<name>`,
bypass the `IRuntimeSettings` whitelist, and live in the runtime-settings DB (scoped
`AuthDbContext` via `IServiceScopeFactory`, so the singleton store works per-request). App
configuration (`plugins:<pluginKey>:<name>`) is the fallback.

## Building

```sh
dotnet build MediaPager.App.Core/MediaPager.App.Core.csproj
```

Migrations are added from the superproject with the API as the startup project:

```sh
dotnet ef migrations add <Name> --project MediaPager.App.Core/MediaPager.App.Core.csproj \
    --startup-project MediaPager.App.Api/MediaPager.App.Api.csproj
```
