# WhoFights API

Read-only JSON API aggregating upcoming combat sports events (UFC, ONE, RIZIN, BKFC, and other tracked promotions), scraped from [Tapology](https://www.tapology.com). Built to be the shared backend for whatever frontends come later (web, mobile, Telegram bot) rather than bundled with any one of them.

This is one repo in a multi-repo project - for the full system architecture, why it's shaped this way, and a guide for onboarding a new contributor, see the **[project wiki](https://github.com/T3mon/whofights-api/wiki)**. Everything below is specific to this repo.

## Tech stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 10, Web API only (no server-rendered UI) |
| ORM / migrations | EF Core + Npgsql |
| Database | PostgreSQL, hosted on [Neon](https://neon.tech) - one project per environment |
| Hosting | [Render](https://render.com), Docker-based Web Services |
| Event data source | Firebase Firestore, fed by a separate scraper project |
| API docs | Swagger / OpenAPI at `/swagger` (Staging only, disabled in Production) |

## Project layout

- `Controllers/Api` - the actual JSON endpoints (`/api/events`, `/api/promotions`)
- `Models/Domain` - EF Core entities (Promotion, Fighter, Event, Bout, UserFollow)
- `Models/Api` - response DTOs
- `Services/Firestore` - reads and parses the raw Firestore feed
- `Services/Sync` - transforms that feed and upserts it into Postgres
- `Data/ApplicationDbContext.cs` - EF Core context
- `Migrations/` - schema history

> Note: this project was scaffolded from ASP.NET Core's MVC + Identity template, and some of that (`Views/`, `Areas/Identity`, `HomeController`, `wwwroot/lib`) is still present but unused now that this is API-only. Pending cleanup - safe to ignore for now, safe to delete later.

## Local development

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Docker Desktop](https://www.docker.com/products/docker-desktop/), the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`).

```bash
git clone https://github.com/T3mon/whofights-api.git
cd whofights-api

# Start a local, throwaway Postgres (isolated - not Neon, not shared with anyone)
docker compose up -d postgres

# Apply the schema
cd src/WhoFights.Web
dotnet ef database update

# Run it
dotnet run
```

Visit `http://localhost:5080/swagger` (port comes from `Properties/launchSettings.json`) to browse and test the API.

## Environment variables

| Variable | Purpose | Example |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Postgres connection string (Npgsql format, not a URL) | `Host=...;Port=5432;Database=...;Username=...;Password=...;Ssl Mode=Require` |
| `ASPNETCORE_ENVIRONMENT` | Which environment this instance is | `Development`, `Staging`, or `Production` |
| `Cors__AllowedOrigins` | Frontend origins allowed to call the API | `["http://localhost:5173"]` |

`Firestore:ProjectId` and `Firestore:CollectionName` are already set in `appsettings.json` (not secret - it's a public-read project) and don't need overriding.

## Deployments

| Environment | API | Auth | Deploy trigger | Database |
|---|---|---|---|---|
| Staging | `whofights-api-staging.onrender.com` | `whofights-auth-staging.onrender.com` | Automatic, on every push to `main` | Neon project `whofights-staging` |
| Production | `whofights-api-production.onrender.com` | `whofights-auth-production.onrender.com` | Manual - click "Deploy" in the Render dashboard | Neon project `whofights-production` |

The frontend that consumes these lives at [whofights.com](https://whofights.com) (production) and `whofights-web-staging.onrender.com` (staging) - see the [whofights-web-ui](https://github.com/T3mon/whofights-web-ui) repo.

All of them run on Render's free tier - the first request after ~15 minutes of inactivity takes 30-50 seconds while the instance wakes up.

**Applying a new migration to a live database** is a manual step, not part of any deploy - run this locally against the target environment's connection string:

```bash
dotnet ef database update --connection "<that environment's connection string>"
```
