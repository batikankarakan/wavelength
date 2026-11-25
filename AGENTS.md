# AGENTS.md: working on Wavelength

Instructions for people **and coding agents** (Claude Code, Codex, Copilot, Cursor…) who build, run or change
this project. Read this before editing anything. The user-facing overview is in `README.md`.

## What this is

A university web programming project. The course brief **requires an ASP.NET MVC 5 Web Application**
(.NET Framework, SQL Server, Razor views with HTML helpers). Do not migrate it to ASP.NET Core, .NET 8,
Blazor, a SPA framework or another database, even if that looks like an improvement. It would fail the brief.

Mandatory items from the brief, which every change must keep working:
1. ASP.NET MVC 5 Web Application (full code)
2. All images, CSS and JavaScript inside the project (no CDNs; fonts are local too)
3. Controllers, models and views run without errors
4. All tables in a local SQL Server database (LocalDB)
5. Tables filled with test data
6. A layout (`Views/Shared/_Layout.cshtml`)
7. Forms validated (client + server)
8. HTML helpers and Razor code in views
9. Sign in / sign up pages
10. Role-based access to some pages

Optional items that are implemented and must stay working: AJAX helpers + jQuery search (`/search`), custom routing (`App_Start/RouteConfig.cs`).

## Build and run

**Windows (the target platform):** open `Wavelength.sln` in Visual Studio 2019/2022 (workload "ASP.NET and web
development") and press F5. NuGet restores `packages/`, IIS Express serves http://localhost:5281, and the first
request creates `App_Data\Wavelength.mdf` (LocalDB) with all tables and demo data.

**macOS / Linux (no Visual Studio):**

```bash
docker compose up --build            # app on http://localhost:5281 (Mono + XSP) + SQL Server 2022
tools/check.sh                       # compile C# + ALL Razor views for .NET Framework 4.8 / C# 7.3 (needs .NET 8 SDK)
```

`tools/check.sh` needs the NuGet packages in `packages/`. If they are missing (fresh clone, no Visual Studio),
download them from nuget.org into `packages/<Id>.<Version>/` (the `.nupkg` is a zip), using the versions listed in
`Wavelength.Web/packages.config`. Mono's `nuget` crashes under x86 emulation on Apple Silicon.

**Tests** (black-box, .NET 8 SDK; each test class starts the app with its own fresh, seeded database):

```bash
# Windows, no Docker: build the solution first (Visual Studio or msbuild), then
dotnet test tests/Wavelength.Tests   # app in IIS Express + database in LocalDB, classes run one after another

# macOS/Linux: app in a Mono container
docker compose up -d sql
dotnet test tests/Wavelength.Tests
```

CI (`.github/workflows/windows.yml`) builds with MSBuild and runs the whole suite on Windows on every push.

**Before you say a change is done:** `tools/check.sh` must print `Build succeeded` with 0 errors, and
`dotnet test tests/Wavelength.Tests` must pass. Views are compiled at runtime by ASP.NET, so on Windows a view
error only shows up when the page is opened. `tools/check.sh` catches those errors ahead of time.

Demo accounts (password `Wavelength123!`): `admin@`, `mod@`, `kora@` (artist), `selin@` (artist), `listener@wavelength.app`.

## Hard rules (these break the build or the runtime)

- **C# 7.3 only**, also inside `.cshtml`. No switch expressions, `is not`/`or` patterns, ranges `x[..n]`,
  `??=`, target-typed `new()`, records, `init`, file-scoped namespaces, `using var`, `string?` annotations,
  global usings. Tuples, `out var`, string interpolation, `?.` and `nameof` are fine.
- **.NET Framework 4.8 APIs only.** No `DateOnly`, no `System.Text.Json` (use Newtonsoft.Json), no ASP.NET Core
  types (`IActionResult`, `IFormFile`, tag helpers, `[FromBody]`, DI constructors). Uploads are `HttpPostedFileBase`.
- **Views use HTML helpers, never tag helpers:** `Html.BeginForm`, `Html.TextBoxFor`, `Html.ValidationMessageFor`,
  `Html.ActionLink`, `Html.Partial`, `Html.Action`… No `asp-for`, `<partial>` or `asp-antiforgery`.
- **Every POST form contains `@Html.AntiForgeryToken()`.** A global filter (`Infrastructure/Filters.cs`) rejects
  POSTs without a valid token with 400. AJAX calls send the layout's token as the `X-CSRF-TOKEN` header
  (`WL.api()` in `Scripts/site.js` and jQuery's `ajaxSetup` already do this).
- **Role names: use `AppRoles.Listener/Artist/Moderator/Admin/Staff`.** The class is not called `Roles`, because that
  collides with `System.Web.Security.Roles` inside views.
- **Entity Framework 6, not EF Core.** `using System.Data.Entity;` for `Include(x => …)` and `AsNoTracking()`.
  There is no `ExecuteUpdate`/`ExecuteDelete`: use `Db.Database.ExecuteSqlCommand("… WHERE id = @p0", id)` with
  the snake_case table/column names, or load and modify. Group dates with `DbFunctions.TruncateTime`. Sum
  possibly-empty sets with `.Sum(x => (int?)x.N) ?? 0`. Don't call C# methods (`Fmt.*`, `ToString()`, string
  formatting) inside LINQ-to-Entities queries; materialize with `ToList()` first. `User.ArtistProfile` is `[NotMapped]`,
  so query `Db.ArtistProfiles` by `UserId` instead.
- **Routing:** controllers use attribute routes (`[Route("explore")]`, `[RoutePrefix("studio")]`). Three actions are
  routed by `RouteConfig` and must **not** get `[Route]`: `ShareController.Open` (`/s/{token}`),
  `DiscoverController.Genre` (`/genres/{slug}`), `UsersController.Details` (`/users/{username}`). Child actions
  (`@Html.Action`) live in `PartialsController` and need the `partials/{action}` route.
- Return `NotFoundView()` / `ForbiddenView()` from `BaseController`, not `HttpNotFound()`.
- Don't name an action `Profile`, `Server`, `Audit` or `File`; those hide `Controller`/`BaseController` members.
  Use another method name plus `[ActionName("…")]`.
- **Files are UTF-8.** Web.config sets `fileEncoding="utf-8"` and views are saved with a UTF-8 BOM; keep it that way
  (otherwise Windows reads views as Windows-1252, special characters like ♪ ✓ → break, and Razor can print `@Model…` as text).
- **Binding redirects:** when you add or upgrade a NuGet package, make sure Web.config has the binding redirects
  Visual Studio would add (e.g. `Microsoft.Web.Infrastructure` 2.0). Mono ignores missing redirects, Windows fails with HTTP 500.
- Don't reference anything from a CDN. New libraries go into `Scripts/` or `Content/` as files.

## Conventions

- **Controllers** inherit `Infrastructure/BaseController`, which provides `Db` (one `AppDbContext` per request),
  `Auth`, `Notes` (notifications), `Audit`, `Cleanup`, `Files`, `CurrentUserId`, `Flash(msg)`, `SafeRedirect`,
  `NotFoundView`, `ForbiddenView` and `CamelJson` (camelCase JSON for JavaScript).
- **Access:** `[Authorize]` for listener pages, `[Authorize(Roles = AppRoles.Artist)]` for the studio,
  `AppRoles.Staff` for moderation, `AppRoles.Admin` for administration. Unauthorized signed-in users are redirected to
  `/403`, visitors to `/login` (`Startup.cs`).
- **Forms:** every form model has DataAnnotations with friendly `ErrorMessage`s, so jQuery unobtrusive validation
  works in the browser and `ModelState` on the server. Extra rules (uniqueness, file checks, state rules) go in the
  action with `ModelState.AddModelError`. Some forms bind with a prefix (`[Bind(Prefix = "Form")]`), so their
  fields are posted as `Form.Title`, `NewAlbum.Title`, `NewGenre.Name` and so on.
- **Helpers:** custom HTML helpers in `Infrastructure/HtmlExtensions.cs` (`Html.BrutField`, `Html.Cover`,
  `Html.Avatar`, `Html.StatusBadge`, `Html.Sticker`, `Html.NavLink`); Razor `@helper`s in `App_Code/Ui.cshtml`
  (`Ui.SectionHead`, `Ui.EmptyState`, `Ui.StatCard`). Views that use custom HtmlHelpers need
  `@using Wavelength.Web.Infrastructure`.
- **Layout keys:** `ViewBag.Title`, `ViewBag.Nav` (home | explore | charts | search | library | studio | admin),
  `ViewBag.HideFooter`.
- **Track status** follows the report's state diagram (`TrackStatus`: draft → pending → published/rejected,
  published ↔ hidden). Only published tracks are public. Moderator/admin actions write `audit_logs` and notify
  users via `Notes.Add(...)` (texts are in `NotificationService.Describe`).
- **Player:** any `[data-play="id"]` inside a `[data-tracklist]` queues that list; `data-like="id"`,
  `data-play-all="1,2,3"` and `data-confirm="…"` (on forms) are handled by `Scripts/site.js` / `Scripts/player.js`.
- **Vue islands:** `<div data-vue="name" data-props="@Fmt.Json(obj)">template</div>` plus
  `WL.island('name', {...})` in `@section Scripts`. In `.cshtml` write `v-on:click`, not `@click`. Never render user
  text inside a Vue template as raw markup; pass it through `data-props` (prevents template injection).
- Razor: a literal `@` in text is `@@` (e.g. `admin@@wavelength.app`).

## Design: keep the landing page style

All pages use the brutalist design of the original landing page. New UI must reuse it, not introduce a new look:
colors `paper #F2EDE2`, `ink #0A0A0A`, `volt #FFE500`, `cobalt #2B3BFF`, `flame #FF4A1C`, `mint #8CF5C0`;
fonts Anton (`display` headings, uppercase), Archivo (body), Space Mono (labels); 2px ink borders with hard
offset shadows (`.brut`, `.btn-volt`, `.btn-paper`, `.sticker`, `.chip`, `.field`, `.card`, `.tbl`, `.status-*`);
`data-reveal`, `data-hop`, `data-tilt` effects. Copy markup from an existing page of the same kind. The site is in English.

**CSS:** Tailwind is compiled into `Content/site.css`, which is committed. After adding or changing utility classes
in views, run `cd Wavelength.Web && npm install && npm run build:css`, otherwise new classes have no styles.
Component classes live in `Wavelength.Web/Styles/site.css`.

## Database changes

- Tables are defined in `Data/Entities.cs` (`[Table("snake_name")]`); columns become snake_case automatically
  (`Data/AppDbContext.cs`). The 19 tables match the analysis report, so don't rename or drop them casually.
- The initializer is `DropCreateDatabaseIfModelChanges`. When the model changes, the database is dropped and re-seeded
  on the next start. That is fine for development, but **all data in the local DB is lost**.
- When you add or change a table/column: update `Data/WavelengthInitializer.cs` (demo data, CHECK constraints),
  `Database/wavelength-schema.sql` (reference DDL), and the test-side model `tests/Wavelength.Tests/TestDb/`
  (EF Core mapping of the same tables used by the tests).
- Deleting rows: foreign keys have no cascade. Use `CleanupService` (DeleteTrack/DeleteUser/…) or extend it.

## Adding a page (checklist)

1. Action on the right controller (or a new controller inheriting `BaseController`) with `[Route]` and the right `[Authorize]`.
2. View/form models in `Models/` with DataAnnotations.
3. View in `Views/<Controller>/` using HTML helpers, `@Html.AntiForgeryToken()` in POST forms, `ViewBag.Title`/`Nav`.
4. Link it from the nav (`Views/Partials/_Nav.cshtml`) or the related page.
5. `npm run build:css` if you used new utility classes.
6. `tools/check.sh` with 0 errors, then add tests in `tests/Wavelength.Tests/Integration/<Area>/` and run them.

## Project map

```
Wavelength.Web/App_Start/      RouteConfig (custom routes), FilterConfig (error page, antiforgery filters)
Wavelength.Web/App_Code/       Razor @helpers (Ui.cshtml)
Wavelength.Web/Controllers/    one controller per page group; ApiController = JSON for the player/AJAX
Wavelength.Web/Data/           entities, EF6 context, initializer + demo data
Wavelength.Web/Infrastructure/ BaseController, HTML helpers, filters
Wavelength.Web/Models/         view/form models (Shared.cs = track/album/playlist list items + queries)
Wavelength.Web/Services/       auth (PBKDF2 + OWIN), notifications, audit, uploads, cleanup, formatting (Fmt)
Wavelength.Web/Views/          Razor views; Shared/_Layout, partials; Partials/_Nav
Wavelength.Web/Content|Scripts|media   CSS + fonts + images, JS (jQuery, Vue, site.js, player.js), demo media
Wavelength.Web/Web.config      LocalDB connection string, settings, IIS config
docker/, docker-compose.yml    Mono + SQL Server setup for macOS/Linux
tests/Wavelength.Tests/        black-box tests (Infrastructure/ starts the app per test class)
tools/                         check.sh (compile check), razor-gen (views → C#), compile-check project
Database/                      reference SQL schema, DBML, ER diagram
```

## Git

- Don't commit `packages/`, `bin/`, `obj/`, `node_modules/`, `App_Data/*.mdf` or files in `uploads/` (see `.gitignore`).
- Keep commits focused; describe what changed for the user (page, rule, fix) in the message.
