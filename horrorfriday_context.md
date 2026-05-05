# HorrorFriday — Project Context

> **How to use this file**: Read it at the start of every session to get full context.
> When the user says **"aggiorna il file .md"** or something similar, update this file in place — never duplicate.
> If a feature moves from TODO → DONE, move it. If a file is renamed, rename it here. Never duplicate something already here, just update it if it exists.
> Keep detail level moderate: enough context to code correctly, not a line-by-line log.
> If we did something that would require an edit of the README or would be cool in the README, edit and update it.

---

## Overview

HorrorFriday is a horror/dark movie discovery web app with semantic AI search, user libraries, and social features (future). Target: Italian-speaking users, GDPR-compliant, production-ready SaaS.

---

## Tech Stack

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core 8 Web API |
| Frontend | Blazor WebAssembly (standalone) |
| Database | PostgreSQL + pgvector |
| Data Access | **Raw Npgsql only — NO Entity Framework** |
| Auth | JWT (15 min) + Refresh Token (7 days) stored in localStorage/sessionStorage |
| Email | System.Net.Mail (Gmail SMTP App Password) |
| Embeddings | OpenAI text-embedding-3-small (1536 dims) — used only in CsvImporter |

---

## Solution Structure

```
HorrorFriday.CsvImporter/   → CSV import tool: bulk insert from Kaggle CSV, diff, enrichment, new-records import
HorrorFriday.TmdbImporter/  → TMDB API import tool: discover, backfill, daily delta, dump import
HorrorFriday.API/           → Backend
HorrorFriday.Web/           → Frontend Blazor WASM
migrations/                 → Raw SQL migration files (run manually in order)

RETIRED (do not modify, kept for reference only):
HorrorFriday.Importer/      → original Kaggle importer (superceded by CsvImporter)
HorrorFriday.TmdbSync/      → original TMDB sync (superceded by TmdbImporter)
```

The `.slnx` references `CsvImporter` and `TmdbImporter` only — old projects are excluded.

---

## Database

**Connection string** (local dev):
`Host=localhost;Port=5432;Username=postgres;Password=YOUR_PASSWORD;Database=horrorfriday`

Credentials are stored in `HorrorFriday.TmdbImporter/.env` and `HorrorFriday.CsvImporter/.env` (gitignored).

### Migrations (run in order)

| File | Status | Description |
|---|---|---|
| `migrations/001_password_reset_tokens.sql` | ✅ Applied | password_reset_tokens table |
| `migrations/002_tmdb_sync_tables.sql` | ✅ Applied | media_type column, unique index (tmdb_id, media_type), watch_providers, movie_watch_providers, movie_certifications, tmdb_sync_log |
| `migrations/003_movie_enrichment_columns.sql` | ✅ Applied | budget, revenue, cast_list, director, dop, writers, producers, music_composer, imdb_rating, imdb_votes on movies |
| `migrations/004_widen_certification.sql` | ✅ Applied | ALTER certification column to TEXT (was VARCHAR(20), too short for some TMDB values) |
| `migrations/005_tmdb_dump_schema.sql` | ✅ Applied | New columns on movies (backdrop_path, homepage, collection_*, spoken_languages, production_*) + tmdb_dump_progress table (movies only) |
| `migrations/006_tv_dump_schema.sql` | ❌ **NOT YET APPLIED** — run before Mode [6] | tmdb_tv_dump_progress table (checkpoint for TV dump import) |

### Current Tables

**movies** — ~335k+ rows (movies only; TV rows TBD after Mode [6] run)
- Core: `tmdb_id`, `media_type` ('movie'|'tv'), `title`, `original_title`, `overview`
- Dates: `release_year`, `release_date`
- Stats: `vote_average`, `vote_count`, `popularity`
- Details: `runtime_minutes`, `status`, `original_language`, `is_adult`, `tagline`, `poster_path`, `imdb_id`
- Enrichment (from CSV): `budget`, `revenue`, `cast_list`, `director`, `director_of_photography`, `writers`, `producers`, `music_composer`, `imdb_rating`, `imdb_votes`
- Extended (from migration 005): `backdrop_path`, `homepage`, `collection_id`, `collection_name`, `spoken_languages` (TEXT, comma-sep), `production_companies` (TEXT, comma-sep), `production_countries` (TEXT, comma-sep)
- AI: `embedding vector(1536)`
- Unique index: `(tmdb_id, media_type)`
- TV note: for TV shows, `director` column stores creator name(s) (from `created_by` field or crew "Creator" role)

**genres, movie_genres** — genre catalogue + movie↔genre join

**keywords, movie_keywords** — keyword catalogue + movie↔keyword join

**users** — id, username, email, password_hash (bcrypt, NULL for Google OAuth), display_name, created_at

**user_movies** — user_id, movie_id, status (`watchlist|watched|watching|dropped`), user_rating, notes, added_at, updated_at

**user_movie_tags** — user_id, movie_id, tag — many tags per library entry

**refresh_tokens** — id, user_id, token, expires_at, created_at

**password_reset_tokens** — id, user_id, token_hash (SHA-256 hex), expires_at (1h), used_at, created_at

**watch_providers** — id, tmdb_provider_id (UNIQUE), provider_name, logo_path

**movie_watch_providers** — movie_id, provider_id, region, provider_type ('stream'|'rent'|'buy'|'ads') — PK on all four

**movie_certifications** — movie_id, region, certification TEXT — PK on (movie_id, region)
- Note: sentinel row ("N/A", "NR") inserted for 404s and failed records so they are not endlessly re-queued

**tmdb_sync_log** — audit log per sync run (type, started_at, finished_at, counts, errors)

**tmdb_dump_progress** — singleton row (id=1 enforced by CHECK), `last_processed_tmdb_id`, `updated_at` — checkpoint for Mode [5] movie dump import. **Completed** (all movies processed).

**tmdb_tv_dump_progress** — ❌ does not exist yet (created by migration 006). Same structure as above but for Mode [6] TV dump import.

---

## Development Rules & Best Practices

### Code
- **Always** separate `.razor` and `.razor.cs` — no inline C# in markup
- All code and identifiers in **English**; UI text in **Italian**
- Raw Npgsql with positional params `$1, $2, ...` — never string interpolation in SQL
- No Entity Framework, no ORM
- Services registered in `Program.cs` via DI
- Sensitive config (SMTP password, JWT secret) in `appsettings.Development.json` (gitignored), never in `appsettings.json`

### Known C# Gotcha — TimeSpan in raw string literals
**NEVER** do `{elapsed:hh\\:mm\\:ss}` inside a `$"""..."""` raw string literal — backslashes are literal characters there, not escape sequences, and `TimeSpan.ToString` will throw `FormatException`.
**Always** pre-format: `var elapsedStr = elapsed.ToString(@"hh\:mm\:ss");` then use `{elapsedStr}` in the raw string.

### Security
- Passwords: BCrypt hashing, policy enforced both frontend and backend (8–30 chars, upper+lower+digit)
- Reset tokens: raw URL-safe base64 token in email, only SHA-256 hash stored in DB
- JWT in localStorage/sessionStorage (never HTTP cookie); HTTPS enforced
- API always returns 200 on forgot-password (no email existence leak)
- `[Authorize]` on all account mutation endpoints

### UI/UX Rules
- **Dark cinematic theme** — single CSS file `HorrorFriday.Web/wwwroot/css/app.css`
- No component-scoped CSS, no inline styles except layout tables in legal pages
- Password fields always have eye-toggle button (`.pw-wrap` / `.pw-toggle`)
- Forms validate client-side first, then show API errors inline (never alert/modal)
- Loading states on all async buttons (`IsLoading` bool, disabled during submit)
- Responsive design: mobile-first where possible
- Legal pages (`/privacy`, `/cookies`) use `.legal-*` CSS classes

---

## Implemented Features

### Auth (API + Web)
- Register, Login, Google OAuth login
- JWT + Refresh token, auto-refresh
- Remember Me: checkbox on login; `true` → localStorage, `false` → sessionStorage
- Forgot Password → email with SHA-256 hashed reset link (1h expiry)
- Reset Password page (`/reset-password?token=...`)
- `HasPassword` bool on UserDto: false for Google OAuth users

### Account Settings (`/settings`)
- Change username (3–32 chars, uniqueness check)
- Change email (uniqueness check, requires current password — classic users only)
- Change password (current + new + confirm, policy enforced — classic users only)
- Google users see a "managed by Google" message instead of password/email panels

### User Movie Library
- Add/update/remove movies with status: watchlist, watched, watching, dropped
- User rating (0–10), notes
- Tags (user_movie_tags table)
- Endpoints: GET/PUT/DELETE `/api/user/movies/{movieId}`

### Search & Discovery
- Semantic search (pgvector cosine similarity)
- Keyword search
- Filters: genres (tri-state include/exclude), year range, min rating, language, sort
- Pagination

### GDPR Compliance
- Cookie consent banner (`CookieBanner.razor`) — checks `hf_cookie_consent` in localStorage
- `/privacy` — full Italian GDPR Privacy Policy
- `/cookies` — Italian Cookie Policy with storage table
- `AppFooter.razor` with links to both legal pages
- `MainLayout.razor` includes footer + banner globally

### Navigation
- `AppNavbar.razor` with profile dropdown
- Click-outside closes menu (JS interop, capture-phase listener)
- `AppFooter.razor` on all pages

### Data Import — Current State (as of 2026-05-05)

| Step | Status | Notes |
|---|---|---|
| Kaggle CSV bulk import (~298k rows) | ✅ Done | CsvImporter Mode [1]. Do NOT re-run. |
| CSV diff + new-records import (2,957 rows + embeddings) | ✅ Done | CsvImporter Modes [2]+[4]. |
| TMDB Discover Fast (~35k additional rows) | ✅ Done | TmdbImporter Mode [4]. |
| Migration 004 (widen certification column) | ✅ Done | Required before any backfill. |
| Migration 005 (extended movie columns + movie checkpoint) | ✅ Done | Required before Mode [5]. |
| TMDB Movie Dump Import (~900k entries) | ✅ Done | TmdbImporter Mode [5]. Completed in full. |
| Migration 006 (TV dump checkpoint table) | ❌ Not applied | Required before Mode [6]. Run `migrations/006_tv_dump_schema.sql`. |
| TMDB TV Dump Import (~230k entries) | ❌ Not run | TmdbImporter Mode [6]. Download dump file first. |
| TMDB Backfill certs + providers (Mode [3]) | ❌ Not run | For all ~335k+ movie rows + all new TV rows after Mode [6]. Fully resumable. Will take many hours. |

---

## Key Files

```
CsvImporter (HorrorFriday.CsvImporter/):
  Program.cs                          → menu [1]-[4], config load, startup checks
  Models/Movie.cs                     → domain movie model
  Models/CsvMovieRecord.cs            → CsvHelper mapping for Kaggle CSV columns
  Services/CsvReaderService.cs        → RFC 4180 CSV parsing with state-machine multiline support
  Services/DatabaseService.cs         → all DB ops for CSV import
  Services/LocalStorageService.cs     → local file persistence between sessions
  Services/CsvAnalysisService.cs      → diff logic between old and new Kaggle CSVs
  Services/CsvEnrichmentService.cs    → enriches DB rows with new-CSV fields (budget, cast, etc.)
  Services/EmbeddingService.cs        → OpenAI text-embedding-3-small wrapper (batch + single)
  Services/NewRecordsImportService.cs → imports IDs from diff_only_in_new.txt with embeddings
  appsettings.json                    → CSV paths, data dir, diff output dir, batch size
  .env (gitignored)                   → HF_DB + HF_OPENAI_KEY

CsvImporter Modes:
  [1] Bulk CSV Import      — DONE, ~298k rows. Do not re-run.
  [2] CSV Diff             — compares old and new Kaggle CSVs, outputs diff_only_in_new.txt
  [3] CSV Enrichment       — enriches existing rows with new-CSV fields (budget, cast, etc.)
  [4] Import New Records   — inserts the 2,957 new records from diff_only_in_new.txt with embeddings

TmdbImporter (HorrorFriday.TmdbImporter/):
  Program.cs                               → menu [1]-[6], config load, startup checks, Mode [6] migration guard
  Models/TmdbModels.cs                     → all TMDB DTOs:
                                             - TmdbMovieDetailFull   (Mode [5]: movies with credits)
                                             - TmdbTvDetailFull      (Mode [6]: TV with credits, created_by)
                                             - TmdbDumpEntry         (movie dump line: id, adult, video, popularity, original_title)
                                             - TmdbTvDumpEntry       (TV dump line: id, adult, popularity, original_name — no video field)
                                             - TmdbCreator           (for created_by array in TV details)
                                             - SyncSettings, SyncStats
  Services/TmdbApiService.cs               → HTTP client; two call paths:
                                             - GetAsync<T>           (sequential, 250ms delay + SemaphoreSlim gate — Modes [1]-[4])
                                             - GetAsyncUnthrottled<T>(parallel-safe, no gate/delay, 429 retry — Modes [5],[6])
                                             Methods: GetMovieDetailFullParallelAsync, GetTvDetailFullParallelAsync
  Services/SyncDatabaseService.cs          → all DB ops:
                                             - InsertMovieFullAsync      (Mode [5] upsert with COALESCE)
                                             - InsertTvFullAsync         (Mode [6] upsert with COALESCE; creator → director column)
                                             - GetExistingMovieTmdbIdsAsync / GetExistingTvTmdbIdsAsync (display only)
                                             - GetDumpProgressAsync / UpdateDumpProgressAsync (movie checkpoint)
                                             - GetTvDumpProgressAsync / UpdateTvDumpProgressAsync (TV checkpoint)
                                             - TvDumpMigrationAppliedAsync (checks tmdb_tv_dump_progress table exists)
  Services/SyncOrchestrator.cs             → orchestrates sync modes [1]-[4]
  Services/TmdbDumpImportService.cs        → Mode [5]: parallel movie dump import
  Services/TmdbTvDumpImportService.cs      → Mode [6]: parallel TV dump import (mirrors Mode [5])
  appsettings.json                         → DelayBetweenRequestsMs (250), MinPopularity (0.3), MinRuntimeMinutes (40),
                                             DumpCheckpointEvery (100), DumpParallelism (15), DumpMaxRequestsPerSecond (15)
  .env (gitignored)                        → HF_DB + TMDB_API_KEY
  Data/ (gitignored)                       → dump files go here (both .json.gz and extracted .json supported)

TmdbImporter Modes:
  [1] Full Sync        — discover all years + immediate detail API per record + backfill. Very slow.
  [2] Daily Delta      — discover last N days + immediate detail. For daily cron job use.
  [3] Backfill         — fetch certs/providers for items missing them. FULLY RESUMABLE (Ctrl+C safe).
                         Cursor: LEFT JOIN movie_certifications WHERE NULL. Sentinel ("N/A","NR") on error.
                         Works for both movies AND TV shows (media_type agnostic query).
                         ⚠ Run migration 004 before first backfill run.
  [4] Discover Fast    — discover all years but NO per-record detail API call. Fast (~35k in hours).
                         Use [3] afterwards to fill certs/providers. FULLY RESUMABLE.
  [5] Dump Film        — ✅ COMPLETED. Streams official TMDB movie dump (~900k entries).
                         Pre-filters: adult=false, video=false, popularity≥0.3.
                         Post-filter: runtime≥40min. One detail API call per surviving ID.
                         Upsert with COALESCE. FULLY RESUMABLE via tmdb_dump_progress.
                         Parallel: 15 workers, TokenBucketRateLimiter at 15 req/s.
                         Dump file glob: *movie_ids*.json.gz or *movie_ids*.json in Data/.
                         ⚠ Requires migration 005.
  [6] Dump Serie TV    — ❌ NOT YET RUN. Streams official TMDB TV dump (~230k entries).
                         Pre-filter: adult=false, popularity≥0.3. No runtime filter (episode runtime varies).
                         Certifications from content_ratings (not release_dates).
                         Creator stored in director column (from created_by field, fallback to crew "Creator").
                         Upsert with COALESCE. FULLY RESUMABLE via tmdb_tv_dump_progress.
                         Same parallelism as Mode [5]: 15 workers, 15 req/s.
                         Dump file glob: *tv_series_ids*.json.gz or *tv_series_ids*.json in Data/.
                         ⚠ Requires migration 006. Download from:
                            http://files.tmdb.org/p/exports/tv_series_ids_MM_DD_YYYY.json.gz

API:
  Controllers/AuthController.cs          → register, login, refresh, google
  Controllers/AccountController.cs       → change-password, change-email, change-username, forgot/reset-password
  Services/AuthService.cs                → all user DB logic
  Services/PasswordResetService.cs       → token gen, email send, token validate
  Services/EmailService.cs               → IEmailService + SmtpEmailService
  Helpers/PasswordPolicy.cs              → shared validation logic
  Models/AuthModels.cs                   → all request/response DTOs

Web:
  Pages/Login.razor(.cs)                 → sign in / register, remember me, eye toggle
  Pages/ForgotPassword.razor(.cs)        → email form, always shows "check inbox"
  Pages/ResetPassword.razor(.cs)         → token from query param, new password form
  Pages/Settings.razor(.cs)             → username/email/password panels
  Pages/Privacy.razor                    → /privacy legal page
  Pages/CookiePolicy.razor               → /cookies legal page
  Shared/AppNavbar.razor(.cs)            → nav + profile menu + click-outside JS
  Shared/AppFooter.razor                 → footer with legal links
  Shared/CookieBanner.razor(.cs)         → GDPR consent banner
  Services/AuthService.cs                → client-side auth, storage routing
  Helpers/PasswordPolicy.cs              → same rules as backend
  Layout/MainLayout.razor                → @Body + AppFooter + CookieBanner
  wwwroot/css/app.css                    → all styles (single file)
```

---

## TMDB Dump Import — Implementation Notes

### Architecture (shared by Mode [5] and Mode [6])

Both dump services use the same parallel pattern:
- `IAsyncEnumerable` source reads the dump file line-by-line (single-threaded)
- `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = DumpParallelism (15)` executes the body concurrently
- `TokenBucketRateLimiter` caps total throughput at `DumpMaxRequestsPerSecond (15)` req/s
- `ConcurrentDictionary<int, byte>` tracks in-flight IDs for safe checkpointing
- `GetAsyncUnthrottled<T>` in TmdbApiService bypasses the sequential gate; caller (rate limiter) manages rate

### Checkpoint safety
Safe checkpoint ID = `_inFlight.Keys.Min() - 1`. All IDs below the minimum in-flight ID are guaranteed complete. Written to DB every `DumpCheckpointEvery (100)` completions via `SemaphoreSlim(1,1)` to serialize DB writes. On restart, dump lines with ID ≤ checkpoint are skipped.

### Upsert strategy
`ON CONFLICT (tmdb_id, media_type) DO UPDATE SET field = COALESCE(EXCLUDED.field, movies.field)` — new data fills missing fields, never overwrites existing non-null values. Exception: `vote_average`, `vote_count`, `popularity`, `poster_path` always updated. `budget`/`revenue` use `NULLIF(value, 0)` since TMDB returns 0 for "unknown".

### Insert vs Update detection
`RETURNING id, (xmax = 0) AS is_new` — `xmax=0` on INSERT, non-zero on UPDATE. No extra SELECT needed.

### Movie vs TV differences
| | Mode [5] Movies | Mode [6] TV |
|---|---|---|
| Dump file | `movie_ids_*.json.gz` | `tv_series_ids_*.json.gz` |
| Dump entry model | `TmdbDumpEntry` (has `video`) | `TmdbTvDumpEntry` (no `video`) |
| Pre-filter | adult, video, popularity | adult, popularity |
| Post-filter | runtime ≥ 40 min | none |
| Detail model | `TmdbMovieDetailFull` | `TmdbTvDetailFull` |
| Certifications | `release_dates` (type 3 first) | `content_ratings` |
| "Director" field | crew job "Director" | `created_by` first, fallback crew "Creator" |
| Checkpoint table | `tmdb_dump_progress` | `tmdb_tv_dump_progress` |
| Sync log type | "dump" | "tv-dump" |

### DelayBetweenRequestsMs setting
The `DelayBetweenRequestsMs: 250` setting in appsettings.json applies **only to Modes [1]-[4]** (sequential `GetAsync<T>` with the SemaphoreSlim gate). Modes [5] and [6] ignore it completely — they use `GetAsyncUnthrottled<T>` and the `TokenBucketRateLimiter` instead.

---

## TODO — Data Import (immediate next steps)

### Step 1 — Apply migration 006
```sql
-- Run migrations/006_tv_dump_schema.sql on the horrorfriday DB
```

### Step 2 — Download TV dump file
```
http://files.tmdb.org/p/exports/tv_series_ids_MM_DD_YYYY.json.gz
→ save to HorrorFriday.TmdbImporter/Data/
  (use today's date in the filename; .json.gz or extracted .json both work)
```

### Step 3 — Run Mode [6] TV Dump Import
- Select `[6]` in TmdbImporter menu
- ~230k entries, ~15 req/s → estimated 4-5 hours
- Ctrl+C safe, fully resumable

### Step 4 — Run Mode [3] Backfill
- Fetches certifications + watch providers for all items lacking them
- Covers: all ~335k+ movie rows from Discover Fast (no certs yet) + all new TV rows from Mode [6]
- Select `[3]` in TmdbImporter menu — leave running, Ctrl+C safe
- Will take many hours; can run simultaneously with or after Mode [6]

---

## TODO — Technical (pre-deploy & improvements)

### Infrastructure / Deploy
- [ ] **Remove poster_path TMDB dependency** — `poster_path` points to `image.tmdb.org`. Before commercializing: download posters to own CDN or generate from self-hosted mirror.
- [ ] **Email verification on registration** — send confirmation email; block login until verified.
- [ ] **Account deletion** — `DELETE /api/account`, removes all user data within 30 days (GDPR Art. 17).
- [ ] **Rate limiting** — on auth endpoints (login, register, forgot-password) to prevent brute force.
- [ ] **Structured logging** — Serilog or similar, write to file + optional external service.
- [ ] **Health check endpoint** — `GET /health` for uptime monitoring.
- [ ] **CORS hardening** — lock `AllowedOrigins` to production domain before deploy.
- [ ] **CSP headers** — Content-Security-Policy, X-Frame-Options, etc.
- [ ] **Database indexes audit** — ensure all filtered/joined columns are indexed.
- [ ] **PostgreSQL backup** — automated daily backup before go-live.
- [ ] **CI/CD pipeline** — GitHub Actions for build + test on PR.
- [ ] **Production secrets** — move all secrets to environment variables or Azure Key Vault.
- [ ] **Deploy infrastructure** — hosting (Azure App Service / VPS / Railway), HTTPS, domain `horrorfriday.com`.
- [ ] **Search caching** — cache popular searches (Redis or in-memory).
- [ ] **API error handling** — global exception middleware with consistent error response shape.
- [ ] **Admin panel** (basic) — view user/movie count, flag/remove content.

---

## TODO — Functional (features)

### Core / Expected
- [ ] **User public profile page** — `/u/{username}` — watchlist, watched count, avg rating, favorite genres.
- [ ] **"Where to Watch"** — streaming platform availability (data already in DB via watch_providers tables).
- [ ] **Trailer embed** — YouTube trailer on movie detail page.
- [ ] **Movie detail page improvements** — full cast/crew, similar movies (embedding cosine), full description.
- [ ] **Similar movies** — on movie detail, show top-N most similar by vector distance.
- [ ] **Import/export library** — let users download their library as CSV/JSON.
- [ ] **Notifications** — in-app alerts (e.g., "new horror movies this week matching your taste").
- [ ] **Weekly email digest** — opt-in weekly email with top horror releases curated by embeddings.

### Differentiating / Original
- [ ] **"Horror DNA" profile** — analyze user's watched list to create a taste fingerprint (subgenres: slasher, folk horror, cosmic horror, etc.) — show as visual breakdown.
- [ ] **Mood-based search** — instead of keywords, user picks a mood ("vuoi avere paura davvero", "atmosfera anni 80", "horror psicologico lento") → curated semantic query.
- [ ] **"Tonight's Pick" feature** — AI-generated daily recommendation based on user history, day of week, season.
- [ ] **Community tags/votes** — users can tag movies with custom horror subgenre labels; popular tags shown on movie cards.
- [ ] **"HorrorFriday Originals" editorial** — curated lists by the team (Best Found Footage, Hidden Gems, etc.) on homepage.
- [ ] **User-created lists** — named collections ("Marathon di Dario Argento", "Film da fare ad Halloween").
- [ ] **Social following** — follow other users, see their recently watched, get recommendations from people with similar taste.
- [ ] **"Scared-o-meter" rating** — separate from quality rating — how scary is it? Shown as a horror-themed icon scale.
- [ ] **Trivia/Easter eggs per film** — short horror facts or behind-the-scenes notes per movie (editorially curated).
- [ ] **PWA / mobile experience** — installable as PWA with offline support for the library.
