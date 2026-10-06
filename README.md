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

- `Controllers/Api` - the actual JSON endpoints (`/api/events`, `/api/promotions`, `/api/rankings`)
- `Models/Domain` - EF Core entities (Promotion, Fighter, Event, Bout, UserFollow, RankingList)
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
cd src/WhoFights.Api
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

## Rankings

The scraper reads Wikipedia's [UFC rankings](https://en.wikipedia.org/wiki/UFC_rankings) and [List of current boxing rankings](https://en.wikipedia.org/wiki/List_of_current_boxing_rankings) into the Firestore `rankings` collection, and the daily sync mirrors it into `RankingLists` / `RankingEntries`. Three lists: `ufc` (the official UFC rankings), `ufc-meta` and `boxrec`. Wikipedia is CC BY-SA, so the site credits it wherever rankings are shown.

The rules, and where each lives:

- **Records: Tapology wins** (`FightRecords.Preferred`). A ranked fighter's record is our own Tapology record whenever we have one. Wikipedia's only fills in when we don't, because anyone can edit Wikipedia. Our Tapology record is the one printed on the fighter's newest card, which is their record going into that fight. The scraper only reads upcoming events, so after a fight the record stays one result behind until their next fight is announced. Every record is normalised to `W-L` or `W-L-D` (a trailing "(1 NC)" is dropped). Anything else is rejected rather than shown. The site shows records for MMA only.
- **Linking a ranked name to one of our fighters** (`FighterNameMatcher`). A link is made only when exactly one fighter fits, because no badge beats a badge on the wrong person. The matcher tries, in order:
  1. The same name, ignoring accents, case and punctuation.
  2. The same letters, spaced differently.
  3. The full name in the fighter's Tapology address. This catches nicknames: "Bia Mesquita" is `.../fighters/210835-beatriz-mesquita`.
  4. The same surname with a short-form first name.

  Linking reruns with every ranking sync, so fighters new to our cards get linked on the next run.
- **Badges on cards** (`RankingBadges`). MMA uses the official UFC list; boxing uses BoxRec ranks and the four sanctioning bodies' belts. A title beats a contender rank, and the official UFC list beats BoxRec. The Meta list and "top rated" aren't used on cards.
- **Bad or stale lists** (`RankingSyncService`). A list whose ranks don't add up is skipped and its previous version kept; ties like 3, 3, 5 are fine. A list that hasn't been refreshed for 14 days is retired.
- **Display order** (frontend). Heaviest division first, with the women's divisions after the men's.
- **Wikipedia links** (frontend `recordLink`). We store and serve the plain article link. The site adds the anchor so a fighter's name opens straight at their record: `#Mixed_martial_arts_record` for MMA, `#Professional_boxing_record` for boxing.

## Deployments

| Environment | API | Auth | Deploy trigger | Database |
|---|---|---|---|---|
| Staging | `whofights-api-staging.onrender.com` | `whofights-auth-staging.onrender.com` | Automatic, on every push to `main` | Neon project `whofights-staging` |
| Production | `whofights-api-production.onrender.com` | `whofights-auth-production.onrender.com` | Manual - click "Deploy" in the Render dashboard | Neon project `whofights-production` |

The frontend that consumes these lives at [whofights.com](https://whofights.com) (production) and `whofights-web-staging.onrender.com` (staging) - see the [whofights-web-ui](https://github.com/T3mon/whofights-web-ui) repo.

All of them run on Render's free tier - the first request after ~15 minutes of inactivity takes 30-50 seconds while the instance wakes up.

**Event sync** is one Render cron job for both environments, `whofights-sync-production`, daily at 06:00 UTC (deploy it manually after changes, like the other production services). It fetches the Firestore feed (events, plus the `rankings` collection the scraper builds from Wikipedia) once and writes it into every database it has a connection string for: `ConnectionStrings__DefaultConnection` (production) and `ConnectionStrings__Staging` (staging). One run instead of two matters because Render bills cron jobs per second and most of a run is container start-up. Each database is synced independently - if one fails, the other still gets the data and the run is reported as failed. Locally, `docker compose run --rm sync` syncs just the local database.

**Applying a new migration to a live database** is a manual step, not part of any deploy - run this locally against the target environment's connection string:

```bash
dotnet ef database update --connection "<that environment's connection string>"
```
