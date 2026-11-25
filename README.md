# Wavelength — ASP.NET MVC 5 Web Application

Share it. Save it. Play it. The full implementation of the Wavelength analysis report: all **19 tables**,
all **38 pages**, the track approval workflow, the artist studio and the moderation panel, in the same
brutalist design as the Task 1 landing page.

| Layer | Technology |
|---|---|
| Framework | **ASP.NET MVC 5.2.9** on .NET Framework 4.8 (C#) |
| Data | Entity Framework 6.4 (code first) + **SQL Server LocalDB** (`App_Data\Wavelength.mdf`) |
| Auth | OWIN cookie authentication, PBKDF2 password hashing, role-based `[Authorize(Roles = ...)]` |
| Front end | Razor views + HTML helpers, Tailwind CSS (compiled), jQuery 3.7 (+ validation, unobtrusive AJAX), Vue 3 (player) |

> **Changing the code?** Read [AGENTS.md](AGENTS.md) first: build/test commands, the MVC 5 / C# 7.3 rules and the design conventions (coding agents read it automatically).

## Run it (Windows, Visual Studio 2019/2022)

1. Open `Wavelength.sln`.
2. Press **F5**. NuGet restores the packages, IIS Express starts on http://localhost:5281.
3. The first request creates the LocalDB database `App_Data\Wavelength.mdf` with the 19 tables and fills it
   with demo data (takes a few seconds). If the model changes, the database is re-created automatically.

Requirements: Visual Studio with the "ASP.NET and web development" workload (includes .NET Framework 4.8,
IIS Express and SQL Server Express LocalDB).

### Without Windows (macOS / Linux, Docker Desktop or OrbStack)

```bash
docker compose up --build        # -> http://localhost:5281  (first start ~20 s)
```

This runs the same MVC 5 app on Mono + XSP and SQL Server 2022 in containers (`docker/Dockerfile`, `docker-compose.yml`).

### Demo accounts

Password for every account: **`Wavelength123!`**

| Email | Role | Try |
|---|---|---|
| admin@wavelength.app | admin | /admin, users, genres, inbox, audit log |
| mod@wavelength.app | moderator | /admin/review (2 tracks waiting), /admin/reports (3 open) |
| kora@wavelength.app | artist (verified) | /studio, upload a track |
| selin@wavelength.app | artist | a rejected track with the moderator's reason |
| djatlas@wavelength.app | artist | a draft track |
| listener@wavelength.app | listener | library, playlists, history, notifications |

There is no mail server: the "forgot password" page shows the reset link directly (`Wavelength:ShowResetLink` in Web.config).

## The brief's checklist

| # | Specification | Where |
|---|---|---|
| 1 | ASP.NET MVC 5 Web Application (full code) | `Wavelength.Web/Wavelength.Web.csproj` (MVC 5 project type, packages.config, Web.config, Global.asax, App_Start) |
| 2 | Images, CSS and JavaScript in the project | `Content/` (site.css, fonts, img), `Scripts/` (jQuery, validation, unobtrusive AJAX, Vue, site.js, player.js), `media/` (covers, demo audio). No CDN, works offline. |
| 3 | Controllers, models and views run without errors | 18 controllers, models in `Models/` + `Data/`, 61 views; checked by the test suite (below) |
| 4 | Tables in a local SQL Server database | LocalDB `App_Data\Wavelength.mdf`, created by EF6 (`Data/AppDbContext.cs`); DDL in `Database/wavelength-schema.sql` |
| 5 | Tables filled with test data | `Data/WavelengthInitializer.cs`: 19 users, 8 artists, 23 tracks, albums, playlists, 30 days of listening history, comments, reports, messages… |
| 6 | Layout | `Views/Shared/_Layout.cshtml` (+ nav child action `PartialsController.Nav`, `_Footer`) |
| 7 | Forms validated | DataAnnotations on every form model → jQuery unobtrusive client validation **and** server validation (`ModelState`), plus extra server rules (unique email/username, file type/size, state rules) |
| 8 | Helpers and Razor | Built-in helpers (`Html.BeginForm`, `TextBoxFor`, `ValidationMessageFor`, `ActionLink`, `Partial`, `Action`, `AntiForgeryToken`…), custom HTML helpers in `Infrastructure/HtmlExtensions.cs` (`Html.BrutField`, `Html.Cover`, `Html.Avatar`, `Html.StatusBadge`…), Razor `@helper`s in `App_Code/Ui.cshtml` |
| 9 | Sign in / sign up | `/login`, `/register` (listener or artist), `/forgot-password`, `/reset-password` (`AccountController`) |
| 10 | Role-based access | `[Authorize]`, `[Authorize(Roles = AppRoles.Artist)]`, `AppRoles.Staff`, `AppRoles.Admin`; table 3.5 of the report |
| Opt. 1 | Search with AJAX helpers + jQuery | `/search`: `Ajax.BeginForm` updates `#search-results`, jQuery submits while typing and on filter clicks |
| Opt. 2 | Custom routing | `App_Start/RouteConfig.cs` (`/s/{token}`, `/genres/{slug}`, `/users/{username}` with constraints, catch-all 404) + attribute routes on controllers |

## Pages (38)

**Public (18):** `/` Home · `/about` · `/contact` · `/register` · `/login` · `/forgot-password` (+ `/reset-password`) ·
`/explore` · `/search` · `/tracks/{id}` · `/artists/{id}` · `/albums/{id}` · `/genres/{slug}` · `/charts` ·
`/playlists/{id}` · `/s/{token}` · `/users/{username}` · `/terms` · `/403`, `/404`

**Listener (7):** `/library` · `/library/playlists` · `/playlists/new`, `/playlists/{id}/edit` · `/history` · `/following` · `/notifications` · `/settings`

**Artist studio (6):** `/studio` · `/studio/upload` · `/studio/tracks` · `/studio/tracks/{id}` · `/studio/albums` · `/studio/profile`

**Moderation & admin (7):** `/admin` · `/admin/review` · `/admin/reports` · `/admin/users` · `/admin/genres` · `/admin/messages` · `/admin/audit`

## How the report maps to the code

| Report | Code |
|---|---|
| 2. Database tables (19) | `Data/Entities.cs` ([Table] names as in the report, snake_case columns), `Data/AppDbContext.cs` (keys, relations), CHECK constraints in `Data/WavelengthInitializer.cs` |
| 4.2 Activity: upload and publish | `StudioController` (upload → pending + notify moderators) → `AdminController` review (approve / reject with reason + notify artist) → studio edit & resubmit |
| 4.3 Sequence: play a track | `Scripts/player.js` → `GET /api/tracks/{id}` (published only) → after 30 s or at the end `POST /api/plays` → `INSERT listening_history` + `plays_count + 1` (`ApiController`) |
| 4.4 State: track status | draft → pending → published / rejected, published ↔ hidden (report upheld / restored), rejected → draft / pending |
| Listening-history features | charts, artist dashboard, admin dashboard, explore recommendations |
| Audit | every moderator/admin action writes `audit_logs` |

## Tests

`tests/Wavelength.Tests` holds ~250 black-box tests (xUnit, .NET 8). Each test class starts the MVC 5 app in a
Docker container with its own fresh, seeded SQL Server database and drives it over HTTP like a browser
(forms with antiforgery tokens, cookies, uploads, the JSON API), then checks the database. They cover every page,
the role matrix, the play sequence, the upload → review → publish/reject workflow, the track state machine,
moderation, administration and account deletion.

```bash
docker compose up -d sql
dotnet test tests/Wavelength.Tests
```

`tools/check.sh` compiles the C# code **and all Razor views** for .NET Framework 4.8 / C# 7.3 on any OS
(`tools/razor-gen` turns the views into C# the same way ASP.NET does at runtime).

## Project layout

```
Wavelength.sln
Wavelength.Web/                 the ASP.NET MVC 5 application
  App_Start/                    RouteConfig (custom routes), FilterConfig (error handling, antiforgery)
  App_Code/Ui.cshtml            Razor @helper functions
  Controllers/                  18 controllers (one per page group) + ApiController (JSON for the player)
  Data/                         entities (19 tables), EF6 context, database initializer + demo data
  Infrastructure/               BaseController, custom HTML helpers, filters
  Models/                       view models and form models (DataAnnotations)
  Services/                     auth (password hashing, OWIN sign-in), notifications, audit, uploads, cleanup
  Views/                        Razor views (Shared/_Layout, partials, one folder per controller)
  Content/ Scripts/ media/      CSS + fonts + images, JavaScript, demo covers and audio
  Styles/site.css               Tailwind source (npm run build:css rebuilds Content/site.css)
  Web.config                    connection string (LocalDB), settings, IIS config
Database/                       SQL schema (reference), DBML, ER diagram
docker/ docker-compose.yml      run on macOS/Linux
tests/Wavelength.Tests/         black-box test suite
tools/                          compile check for macOS/Linux (check.sh, razor-gen)
```

## Security notes

- Passwords are hashed with ASP.NET Identity's `PasswordHasher` (PBKDF2); plain passwords are never stored.
- Every POST needs an antiforgery token (`@Html.AntiForgeryToken()` in forms, `X-CSRF-TOKEN` header for AJAX), enforced by a global filter.
- Uploads are checked by extension, MIME type and size (audio ≤ 20 MB, images ≤ 4 MB) and stored under random names.
- Razor encodes all output; user data reaches Vue widgets only as JSON props, never as templates.
- Banned users are signed out on their next request; password reset links are signed, expire after one hour and stop working once the password changes.
