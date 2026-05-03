# HorrorFriday — Project Context

> **How to use this file**: Read it at the start of every session to get full context.
> When the user says **"aggiorna il file .md"** or something similar, update this file in place — never duplicate.
> If a feature moves from TODO → DONE, move it. If a file is renamed, rename it here, in general never duplicate something already here, just update it if it exists.
> Keep detail level moderate: enough context to code correctly, not a line-by-line log.
> if we did something that would required an edit of readme or it would be cool to be in the readme, edit it and update it!
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
| Embeddings | OpenAI text-embedding-ada-002 (vector 1536) |

---

## Solution Structure

```
HorrorFriday.Importer   → DO NOT MODIFY (Kaggle import, done)
HorrorFriday.TmdbSync   → TMDB daily sync tool (console, manually triggered)
HorrorFriday.API        → Backend
HorrorFriday.Web        → Frontend Blazor WASM
migrations/             → Raw SQL migration files (run manually)
```

---

## Database

**Connection string** (local dev):
`Host=localhost;Port=5432;Username=postgres;Password=YOUR_PASSWORD;Database=horrorfriday`

### Tables

**movies** — ~298k rows, `embedding vector(1536)`, `poster_path` (TMDB-derived, must be removed before commercializing)

**genres, keywords, movie_genres, movie_keywords**

**users**
- id, username, email, password_hash (bcrypt, nullable = Google OAuth), display_name, created_at
- `password_hash IS NOT NULL` → classic auth user; NULL → Google OAuth user

**user_movies**
- user_id, movie_id, status (`watchlist|watched|watching|dropped`), user_rating, notes, added_at, updated_at
- Tags: implemented via separate `user_movie_tags` table (tag text, many per entry)

**refresh_tokens** — id, user_id, token, expires_at, created_at

**password_reset_tokens** — id, user_id, token_hash (SHA-256 hex), expires_at (1h), used_at, created_at
- Migration: `migrations/001_password_reset_tokens.sql` — **must be run manually**

**watch_providers** — id, tmdb_provider_id (UNIQUE), provider_name, logo_path

**movie_watch_providers** — movie_id, provider_id, region, provider_type ('stream'|'rent'|'buy'|'ads') — PK on all four

**movie_certifications** — movie_id, region, certification — PK on (movie_id, region)

**tmdb_sync_log** — audit log of every sync run (type, started_at, finished_at, counts, errors)

- Migration: `migrations/002_tmdb_sync_tables.sql` — **must be run manually before first TmdbSync run**
- Also adds `media_type` column to `movies` and changes unique constraint to `(tmdb_id, media_type)`

---

## Development Rules & Best Practices

### Code
- **Always** separate `.razor` and `.razor.cs` — no inline C# in markup
- All code and identifiers in **English**; UI text in **Italian**
- Raw Npgsql with positional params `$1, $2, ...` — never string interpolation in SQL
- No Entity Framework, no ORM
- Services registered in `Program.cs` via DI
- Sensitive config (SMTP password, JWT secret) in `appsettings.Development.json` (gitignored), never in `appsettings.json`

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

---

## Key Files

```
TmdbSync (HorrorFriday.TmdbSync/):
  Program.cs                          → menu, config load, startup checks
  Models/TmdbModels.cs                → TMDB API response DTOs + SyncSettings + SyncStats
  Services/TmdbApiService.cs          → rate-limited TMDB HTTP client (250ms/req default)
  Services/SyncDatabaseService.cs     → all DB ops (insert, upsert certs/providers, backfill query)
  Services/SyncOrchestrator.cs        → 4 sync modes orchestration with progress display
  appsettings.json                    → non-secret config (delay, genres, regions, lookback)
  .env (gitignored)                   → HF_DB + TMDB_API_KEY secrets

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
  Pages/Settings.razor(.cs)              → username/email/password panels
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

## TODO — Technical (pre-deploy & improvements)

- [x] **TMDB bulk import**: `HorrorFriday.TmdbSync` — console tool with 4 modes (full sync, daily delta, backfill only, discover only). Inserts delta only (by tmdb_id+media_type). Handles movies + TV series. Adds certifications, watch providers. NO embeddings (no OpenAI cost). Self-resumable backfill.
- [ ] **Remove poster_path TMDB dependency**: `poster_path` in movies table points to `image.tmdb.org`. Before commercializing: download all posters to own CDN/storage and update column to own URLs. OR generate poster URLs dynamically from a self-hosted mirror.
- [ ] **Email verification on registration**: Send confirmation email; block login until verified.
- [ ] **Account deletion**: `DELETE /api/account` endpoint, removes all user data within 30 days (GDPR Art. 17).
- [ ] **Rate limiting**: On auth endpoints (login, register, forgot-password) to prevent brute force.
- [ ] **Structured logging**: Add Serilog or similar, write to file + optionally to a service.
- [ ] **Health check endpoint**: `GET /health` for uptime monitoring.
- [ ] **CORS hardening**: Lock `AllowedOrigins` to production domain before deploy.
- [ ] **CSP headers**: Add Content-Security-Policy, X-Frame-Options, etc.
- [ ] **Database indexes audit**: Ensure all filtered/joined columns are indexed.
- [ ] **PostgreSQL backup**: Automated daily backup strategy before go-live.
- [ ] **CI/CD pipeline**: GitHub Actions for build + test on PR.
- [ ] **Production secrets**: Move all secrets to environment variables or Azure Key Vault.
- [ ] **Deploy infrastructure**: Choose hosting (Azure App Service / VPS / Railway), configure HTTPS, domain `horrorfriday.com`.
- [ ] **Search caching**: Cache popular searches or movie detail responses (Redis or in-memory).
- [ ] **API error handling**: Global exception middleware with consistent error response shape.
- [ ] **Admin panel** (basic): View user count, movie count, flag/remove content.

---

## TODO — Functional (features)

### Core / Expected
- [ ] **User public profile page**: `/u/{username}` — watchlist, watched count, avg rating, favorite genres.
- [ ] **"Where to Watch"**: Streaming platform availability via TMDB/JustWatch API embed.
- [ ] **Trailer embed**: YouTube trailer on movie detail page.
- [ ] **Movie detail page improvements**: Full cast/crew, similar movies (embedding cosine), full description.
- [ ] **Similar movies**: On movie detail, show top-N most similar by vector distance.
- [ ] **Import/export library**: Let users download their library as CSV/JSON.
- [ ] **Notifications**: In-app alerts (e.g., "new horror movies this week matching your taste").
- [ ] **Weekly email digest**: Opt-in weekly email with top horror releases curated by embeddings.

### Differentiating / Original
- [ ] **"Horror DNA" profile**: Analyze user's watched list to create a taste fingerprint (subgenres: slasher, folk horror, cosmic horror, etc.) — show as a visual breakdown.
- [ ] **Mood-based search**: Instead of keywords, user picks a mood (es. "vuoi avere paura davvero", "atmosfera anni 80", "horror psicologico lento") and we run a curated semantic query.
- [ ] **"Tonight's Pick" feature**: AI-generated daily recommendation based on user history, day of week, season.
- [ ] **Community tags/votes**: Users can tag movies with custom horror subgenre labels; popular tags shown on movie cards.
- [ ] **"HorrorFriday Originals" editorial**: Curated lists by the team (Best Found Footage, Hidden Gems, etc.) shown on homepage.
- [ ] **User-created lists**: Beyond status tags — named collections ("Marathon di Dario Argento", "Film da fare ad Halloween").
- [ ] **Social following**: Follow other users, see their recently watched, get recommendations from people with similar taste.
- [ ] **"Scared-o-meter" rating**: Separate from quality rating — how scary is it? Shown as a horror-themed icon scale.
- [ ] **Trivia/Easter eggs per film**: Short horror facts or behind-the-scenes notes per movie (editorially curated).
- [ ] **PWA / mobile experience**: Make the app installable as a PWA with offline support for the library.
