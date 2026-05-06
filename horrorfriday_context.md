# HorrorFriday — Project Context

> **Come usare questo file**: Leggerlo all'inizio di ogni sessione per avere il contesto completo.
> Quando l'utente dice "aggiorna il file .md", aggiorna questo file — mai duplicare, mai creare altri file.
> Se una feature passa da TODO → DONE, spostala. File rinominato → aggiornalo qui. Mai duplicare, solo aggiornare.
> Livello di dettaglio moderato: abbastanza per codare correttamente, non un log riga per riga.

---

## Overview

HorrorFriday è un'app web di scoperta film horror/dark con ricerca semantica AI, librerie utente e feature social (future). Target: utenti italofoni, GDPR-compliant, production-ready SaaS.

---

## Tech Stack

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core 10 Web API |
| Frontend | Blazor WebAssembly (standalone), .NET 10 |
| Database | PostgreSQL + pgvector |
| Data Access | **Raw Npgsql only — NO Entity Framework** |
| Auth | JWT (15 min) + Refresh Token (7 days) stored in localStorage/sessionStorage |
| Email | System.Net.Mail (Gmail SMTP App Password) |
| Embeddings | OpenAI text-embedding-3-small (1536 dims) |

---

## Solution Structure

```
HorrorFriday.CsvImporter/   → CSV import tool: bulk insert da Kaggle CSV, diff, enrichment
HorrorFriday.TmdbImporter/  → TMDB API import tool: discover, backfill, daily delta, dump import
HorrorFriday.API/           → Backend
HorrorFriday.Web/           → Frontend Blazor WASM
migrations/                 → Raw SQL migration files (run manualmente in ordine)

RETIRED (non modificare, tenuti solo come riferimento):
HorrorFriday.Importer/      → original Kaggle importer (superceded by CsvImporter)
HorrorFriday.TmdbSync/      → original TMDB sync (superceded by TmdbImporter)
```

Il `.slnx` include solo `CsvImporter` e `TmdbImporter` — i vecchi progetti sono esclusi.

---

## Database

**Connection string** (local dev):
`Host=localhost;Port=5432;Username=postgres;Password=YOUR_PASSWORD;Database=horrorfriday`

Credenziali in `HorrorFriday.TmdbImporter/.env` e `HorrorFriday.CsvImporter/.env` (gitignored).

### Migrations (eseguire in ordine)

| File | Status | Descrizione |
|---|---|---|
| `migrations/001_password_reset_tokens.sql` | ✅ Applied | Tabella password_reset_tokens |
| `migrations/002_tmdb_sync_tables.sql` | ✅ Applied | media_type, watch_providers, movie_certifications, tmdb_sync_log |
| `migrations/003_movie_enrichment_columns.sql` | ✅ Applied | budget, revenue, cast_list, director, dop, writers, producers, imdb_rating, imdb_votes |
| `migrations/004_widen_certification.sql` | ✅ Applied | Colonna certification → TEXT |
| `migrations/005_tmdb_dump_schema.sql` | ✅ Applied | backdrop_path, homepage, collection_*, spoken_languages, production_* + tmdb_dump_progress |
| `migrations/006_tv_dump_schema.sql` | ❌ **NON APPLICATA** — eseguire prima di Mode [6] | Tabella tmdb_tv_dump_progress |

### Tabella `movies` — tutte le colonne

✅ = esposta nel MovieDto API | 🔧 = in DB ma non nel DTO | — = non esposta (uso interno)

```
id                    ✅  PK interno
tmdb_id               ✅  TMDB numeric ID
media_type            ✅  'movie' | 'tv'
title                 ✅
original_title        ✅
overview              ✅
tagline               ✅  solo film
release_year          ✅  short, estratto da release_date
release_date          🔧  full DateOnly
runtime_minutes       ✅  film: totale. TV: durata media episodio
vote_average          ✅  0.0–10.0
vote_count            ✅
popularity            ✅  score TMDB, nessun range fisso
status                ✅  Released|In Production|Post Production|Planned|Canceled|Rumored / Returning Series|Ended|Canceled
original_language     ✅  ISO 639-1 (en, it, ja...)
poster_path           ✅  path TMDB → https://image.tmdb.org/t/p/{size}{path}
backdrop_path         ✅  immagine 16:9 orizzontale, presente per import Mode 5/6
imdb_id               ✅  es. tt0078748
imdb_rating           ✅  (da migration 003)
imdb_votes            ✅  (da migration 003)
director              ✅  nomi comma-separated (film: regista, TV: creator)
cast_list             ✅  top ~10 attori, comma-separated
director_of_photography ✅
writers               ✅
producers             ✅
music_composer        ✅
budget                ✅  solo film, USD, spesso 0 (non pubblico)
revenue               ✅  solo film, USD box office
homepage              ✅  solo film, URL sito ufficiale
collection_id         ✅  solo film, TMDB collection numeric ID
collection_name       ✅  solo film, es. "Alien Collection"
spoken_languages      ✅  comma-separated ISO codes
production_companies  ✅  comma-separated nomi
production_countries  ✅  comma-separated ISO codes
is_adult              🔧  bool, sempre false nel nostro import
embedding             —   vector(1536), OpenAI text-embedding-3-small, per semantic search
```

**TMDB Image URLs:** `https://image.tmdb.org/t/p/{size}{path}`
- Poster: w92 w185 w342 w500 w780 original
- Backdrop: w300 w780 w1280 original

### Altre tabelle

**genres, movie_genres** — catalogo generi + join movie↔genre

**keywords, movie_keywords** — catalogo keyword + join movie↔keyword

**movie_certifications** — (movie_id, region, certification TEXT) — PK su (movie_id, region)
- Nota: sentinel row ("N/A", "NR") inserita per 404 e record falliti, evita re-queue infinita

**movie_watch_providers** — (movie_id, provider_id, region, provider_type: stream|rent|buy|ads) — PK su tutti e quattro

**watch_providers** — (id, tmdb_provider_id UNIQUE, provider_name, logo_path)

**users** — id, username, email, password_hash (bcrypt, NULL per Google OAuth), display_name, created_at

**user_movies** — (user_id, movie_id, status `to_watch|watching|watched|dropped`, user_rating, notes, added_at, updated_at)

**user_movie_tags** — user_id, movie_id, tag — più tag per voce di libreria

**refresh_tokens** — id, user_id, token, expires_at, created_at

**password_reset_tokens** — id, user_id, token_hash (SHA-256 hex), expires_at (1h), used_at, created_at

**tmdb_sync_log** — audit log per run di sync

**tmdb_dump_progress** — singleton (id=1), `last_processed_tmdb_id`, `updated_at` — checkpoint Mode [5]. **Completato.**

**tmdb_tv_dump_progress** — ❌ non esiste ancora (creata da migration 006). Stesso schema di dump_progress ma per Mode [6].

---

## Development Rules & Best Practices

### Codice
- **Sempre** separare `.razor` e `.razor.cs` — niente C# inline nel markup
- Tutto il codice e gli identificatori in **inglese**; testo UI in **italiano**
- Raw Npgsql con parametri posizionali `$1, $2, ...` — mai string interpolation nel SQL
- No Entity Framework, no ORM — `NpgsqlDataSource` + `NpgsqlCommand`
- Servizi registrati in `Program.cs` via DI
- Config sensibile (SMTP password, JWT secret) in `appsettings.Development.json` (gitignored)
- **TmdbImporter**: nessun NuGet Pgvector — passare embedding come stringa `[f1,f2,...]` e castare con `$1::vector` in SQL
- **TmdbImporter .env**: valori senza virgolette. Chiavi: `HF_DB`, `TMDB_API_KEY`, `HF_OPENAI_KEY`
- **Non modificare** `HorrorFriday.Importer` (legacy, tenuto solo come riferimento)

### C# Gotcha — TimeSpan in raw string literals
**MAI** fare `{elapsed:hh\\:mm\\:ss}` dentro un `$"""..."""` raw string literal — i backslash sono letterali, non escape.
**Sempre** pre-formattare: `var elapsedStr = elapsed.ToString(@"hh\:mm\:ss");` poi usare `{elapsedStr}`.

### Blazor
- `EventCallback` handler che usa JS interop prima di navigare → `async Task`, non `void`

### Security
- Password: BCrypt hashing, policy 8–30 chars, upper+lower+digit (frontend + backend)
- Reset token: token raw URL-safe base64 nell'email, solo hash SHA-256 nel DB
- JWT in localStorage/sessionStorage (mai HTTP cookie); HTTPS enforced
- API risponde sempre 200 su forgot-password (no email existence leak)
- `[Authorize]` su tutti gli endpoint di mutazione account

### UI/UX Rules
- **Dark cinematic theme** — singolo CSS file `HorrorFriday.Web/wwwroot/css/app.css`, niente CSS component-scoped
- Campi password: sempre eye-toggle button (`.pw-wrap` / `.pw-toggle`)
- Form: validazione client-side prima, poi errori API inline (mai alert/modal)
- Loading state su tutti i bottoni async (`IsLoading` bool, disabled durante submit)
- **Badge vs label**: badge flottanti (position:absolute) solo per il bottone stato libreria (top-left card). Indicatori tipo (TV/film) vanno inline nel testo overlay, non come badge flottanti.
- **Hide-from-results chips**: stile strikethrough quando attivi — come i genre excluded chips, NON sfondi colorati.

---

## CSS Architecture

Singolo file: `HorrorFriday.Web/wwwroot/css/app.css`

| Prefix | Sezione |
|--------|---------|
| `md-` | Movie detail page |
| `hf-` | Home page filtri |
| `hf-ss` | SearchableSelect component |
| `poster-card` | Movie/TV cards nella griglia |
| `hero-` | Home hero section |
| `search-` | Search bar area |
| `results-` | Griglia risultati + header |
| `account-` | Profile / Settings pages |
| `page-btn` | Paginazione |
| `legal-` | Pagine /privacy e /cookies |

Design tokens in `:root`: `--bg #0a0908`, `--surface #111110`, `--surface-alt #1a1918`, `--border #252321`, `--text #e8e0d4`, `--text-secondary #a89e90`, `--text-muted #6b6358`, `--accent #9b1c1c`, `--accent-light #d44a4a`, `--amber #c4952a`, `--font-display / --font-body: 'Space Grotesk'`

Nota: il CSS ha due blocchi `md-*` (un vecchio e un "Premium detail redesign" ~linea 2660). I modifier class `md-watch-card`, `md-link-card`, `md-scoreboard` non sono definiti esplicitamente ma le sezioni funzionano correttamente tramite la base class `md-section`.

---

## Implemented Features

### Auth (API + Web)
- Register, Login, Google OAuth login
- JWT + Refresh token, auto-refresh
- Remember Me: `true` → localStorage, `false` → sessionStorage
- Forgot Password → email con link reset SHA-256 hashato (1h expiry)
- Reset Password page (`/reset-password?token=...`)
- `HasPassword` bool su UserDto: false per Google OAuth users

### Account Settings (`/settings`)
- Cambio username (3–32 chars, uniqueness check)
- Cambio email (uniqueness check, richiede password attuale — solo utenti classici)
- Cambio password (attuale + nuova + confirm, policy enforced — solo utenti classici)
- Google users vedono "managed by Google" invece dei pannelli password/email

### User Movie Library
- Add/update/remove film con status: `to_watch`, `watching`, `watched`, `dropped`
- User rating (0–10), note
- Tags (tabella user_movie_tags)
- Endpoint: GET/PUT/DELETE `/api/user/movies/{movieId}`

### Search & Discovery (Home page `/`)
**Files:** `Home.razor` + `Home.razor.cs`

- Keyword search (`title ILIKE %q%`) + AI semantic search (pgvector `<=>` cosine, OpenAI text-embedding-3-small)
- Toggle AI mode (`IsAiMode`): placeholder diverso, hint strip con query di esempio, error strip su fallimento OpenAI
- **Media type toggle**: All / Film / Serie TV — stato `MediaType`
- **Genre chips**: tri-state (neutral → included → excluded) — `IncludedGenres` / `ExcludedGenres` HashSet, strikethrough quando esclusi
- **Hide from results** (solo logged-in): chips per stato libreria — `HideStatuses` HashSet, strikethrough quando attivi
- **Advanced filters** (collapsibile `hf-advanced`):
  - Anno da/a, Rating min/max, Max runtime — input text con clamp validation
  - Sort by (popularity/rating/year/title) + direction (asc/desc) — componente `CustomSelect`
  - Toggle include upcoming (`hf-toggle`)
  - Availability: region (`SearchableSelect`, auto-detect `navigator.language`) → providers (chips) + certifications (chips)
- **Clear filters + "N filters active"**: SEMPRE insieme in `hf-filters__footer-right` (footer del pannello advanced, a destra del toggle upcoming). `hf-filter-actions` contiene SOLO il toggle More/Fewer filters.
- `ActiveFilterCount`: proprietà computata, conta tutti i valori non-default

**Filter State Persistence (back navigation):**
- Click su card → `NavigateToMovie()` salva `FilterState` record in `sessionStorage["hf_filter_state"]`
- Home `OnInitializedAsync`: se la chiave esiste → ripristina tutto lo stato, cancella la chiave, ri-esegue la ricerca, scrolla alla Y salvata
- JS in `wwwroot/js/app.js`: `hfSaveState(k,v)`, `hfLoadState(k)`, `hfClearState(k)`, `hfScrollY()`, `hfScrollTo(y)`
- `FilterState` è un record privato in `Home.razor.cs`

**API endpoints:**
- `POST /api/movies/search` (body: `SearchRequest`)
- `GET /api/movies/genres`
- `GET /api/movies/regions`
- `GET /api/movies/providers?region=`
- `GET /api/movies/certifications?region=`
- Provider filtrati a ~35 piattaforme major via whitelist `MajorProviderNames` in `MovieService`
- Certification filtrate via dict `KnownCertifications` (region → set di cert valide)

### Movie Detail Page (`/movie/{id}`)
**Files:** `MovieDetail.razor` + `MovieDetail.razor.cs`
**CSS prefix:** `md-*` (tutti i vecchi selettori `movie-detail-*` / `detail-*` rimossi da `app.css`)
**Build:** ✅ CLEAN — 0 errori, 0 warning

**Layout hero:**
```
md-hero (backdrop fullwidth: backdrop_path → fallback poster_path, blurred + overlay + grain)
  md-topbar: [← Torna ai risultati]  [Sito ufficiale] [IMDb] [TMDB]
  md-hero-grid (3 col):
    md-poster-col (sticky)
    md-hero-main.md-glass-panel:
      md-meta-top: type badge (Film/Serie TV) | status | certification
      md-title, md-original-title, md-tagline
      md-quick-facts: Anno | Durata | Lingua | TMDB | IMDb
      md-genre-row: genre pills
      md-overview
    md-user-panel.md-glass-panel: status dropdown + rating 1-10 + note + salva/rimuovi

md-content (sotto l'hero):
  md-content-grid (2 col):
    md-content-main: Cast & Crew | Produzione | Keywords
    md-content-rail (sticky): Dove guardarlo | Rating | Link
```

**Helper methods in razor.cs:**
- `GetBackdropPath()` → `BackdropPath ?? PosterPath`
- `GetTmdbImageUrl(path, size)` → URL immagine TMDB completo
- `GetTmdbTitleUrl()` → link TMDB corretto (movie vs tv), usa `TmdbId` se disponibile
- `FormatRuntime(short minutes)` → "2h 14min"
- `FormatCsvValue(string?)` → split su virgola, max 4 valori, "N/D" se vuoto
- `GetPeopleCards()` → Director/Creator, Cast (6), Writers (3), DOP (2), Music (2)
- `ToggleListMenu()` / `GetSelectedStatusLabel()` / `GetStatusIcon(status)` — dropdown stato

**Status labels (italiano):** Da guardare / In visione / Visto / Abbandonato

**Dread Profile (computato, niente dati extra DB):**
- Audience confidence: `vote_average × 10` clampato 8–100
- Obscurity: `100 - log10(vote_count+1)/5 × 100` clampato 8–96
- Runtime pressure: `runtime_minutes / 180 × 100` clampato 10–100

**Navigation back:** `GoBack()` → `Navigation.NavigateTo("/")` → Home ripristina da sessionStorage

### Movie Cards (griglia)
**Files:** `MovieCard.razor` + `MovieCard.razor.cs`, usato in `Home.razor`
- `poster-card--movie` = bordo top rosso, `poster-card--tv` = bordo top blu
- Badge stato libreria: top-left, absolute, hidden fino a hover (se vuoto)
- Indicatore TV: chip inline nel testo poster-meta, NON badge flottante
- Status picker dropdown: `OpenStatusPickerId` gestito in Home (uno solo aperto alla volta)

### Shared Components
| Componente | File | Scopo |
|-----------|------|-------|
| `AppNavbar` | `Shared/AppNavbar.razor` | Nav top, stato auth, logout |
| `MovieCard` | `Shared/MovieCard.razor` | Poster card nella griglia |
| `LoadingSpinner` | `Shared/LoadingSpinner.razor` | Size=small/large |
| `CustomSelect` | `Shared/CustomSelect.razor` | Dropdown generico, `TItem` |
| `SearchableSelect` | `Shared/SearchableSelect.razor` | Dropdown searchable con backdrop, prefix CSS `hf-ss` |

### GDPR Compliance
- Cookie consent banner (`CookieBanner.razor`) — controlla `hf_cookie_consent` in localStorage
- `/privacy` — Privacy Policy italiana completa
- `/cookies` — Cookie Policy italiana con tabella storage
- `AppFooter.razor` con link a entrambe le pagine legali

---

## Key Files

```
CsvImporter (HorrorFriday.CsvImporter/):
  Program.cs                          → menu [1]-[4], config load, startup checks
  Services/DatabaseService.cs         → tutte le op DB per CSV import
  Services/EmbeddingService.cs        → wrapper OpenAI text-embedding-3-small
  .env (gitignored)                   → HF_DB + HF_OPENAI_KEY

TmdbImporter (HorrorFriday.TmdbImporter/):
  Program.cs                               → menu [1]-[7], config, migration guard
  Models/TmdbModels.cs                     → tutti i TMDB DTO
  Services/TmdbApiService.cs               → HTTP client; GetAsync (Modes 1-4, rate-limited) vs GetAsyncUnthrottled (Modes 5-6, parallel)
  Services/SyncDatabaseService.cs          → tutte le op DB (upsert, checkpoint, progress)
  Services/TmdbDumpImportService.cs        → Mode [5]: parallel movie dump
  Services/TmdbTvDumpImportService.cs      → Mode [6]: parallel TV dump
  appsettings.json                         → DelayBetweenRequestsMs(250), MinPopularity(0.3), MinRuntimeMinutes(40), DumpParallelism(15), DumpMaxRequestsPerSecond(15)
  .env (gitignored)                        → HF_DB + TMDB_API_KEY
  Data/ (gitignored)                       → dump files (.json.gz o .json estratto)

API:
  Controllers/MoviesController.cs         → search, getById, genres, regions, providers, certifications
  Controllers/AuthController.cs           → register, login, refresh, google
  Controllers/AccountController.cs        → change-password/email/username, forgot/reset-password
  Services/MovieService.cs                → SearchAsync (dynamic SQL builder), GetByIdAsync, filtri availability
  Services/AuthService.cs                 → tutta la logica DB utenti
  Models/MovieDto.cs                      → MovieDto + MovieWatchProviderDto + MovieCertificationDto
  Models/SearchRequest.cs                 → tutti i parametri di filtro

Web:
  Pages/Home.razor(.cs)                   → griglia ricerca + tutti i filtri
  Pages/MovieDetail.razor(.cs)            → pagina dettaglio film
  Pages/Login.razor(.cs)                  → sign in / register
  Pages/Settings.razor(.cs)              → username/email/password
  Pages/Privacy.razor / CookiePolicy.razor → pagine legali
  Shared/AppNavbar.razor(.cs)             → nav + profile menu
  Shared/MovieCard.razor(.cs)             → card nella griglia
  Models/MovieDto.cs                      → mirror del DTO API (stesso schema)
  Services/AuthService.cs                 → auth client-side, storage routing, SendAuthorizedAsync()
  wwwroot/css/app.css                     → tutti gli stili (file singolo)
  wwwroot/js/app.js                       → JS interop: sessionStorage, scroll, click-outside
```

---

## TmdbImporter Modes

| Mode | Stato | Cosa fa |
|------|-------|---------|
| [1] Full Sync | — | Discover tutti gli anni + detail API immediato + backfill. Molto lento. |
| [2] Daily Delta | — | Discover ultimi N giorni + detail. Per cron job giornaliero. |
| [3] Backfill | — | Fetch cert/provider per item senza. RESUMABLE. Funziona per film E TV. ⚠ Richiede migration 004. |
| [4] Discover Fast | — | Discover tutti gli anni, nessuna chiamata API per record. Veloce. Poi usare [3] per certs/providers. RESUMABLE. |
| [5] Dump Film | ✅ COMPLETATO | Dump ufficiale TMDB (~900k). Pre-filter: adult=false, video=false, popularity≥0.3. Post-filter: runtime≥40min. Parallel 15 worker, 15 req/s. RESUMABLE via tmdb_dump_progress. ⚠ Richiede migration 005. |
| [6] Dump Serie TV | ❌ NON ESEGUITO | Dump ufficiale TMDB TV (~230k). Pre-filter: adult=false, popularity≥0.3. Cert da content_ratings. Creator → colonna director. RESUMABLE via tmdb_tv_dump_progress. ⚠ Richiede migration 006. Download: `http://files.tmdb.org/p/exports/tv_series_ids_MM_DD_YYYY.json.gz` |
| [7] Embedding Backfill | — | OpenAI text-embedding-3-small, batch 100, WHERE embedding IS NULL. RESUMABLE. |

**Nota:** `DelayBetweenRequestsMs` (250ms) si applica SOLO ai Mode [1]-[4] (GetAsync sequenziale). I Mode [5]-[6] usano `GetAsyncUnthrottled` + `TokenBucketRateLimiter`.

**Checkpoint safety (Mode 5+6):** Safe checkpoint ID = `_inFlight.Keys.Min() - 1`. Scritto ogni 100 completamenti via `SemaphoreSlim(1,1)`.

**Upsert strategy:** `ON CONFLICT DO UPDATE SET field = COALESCE(EXCLUDED.field, movies.field)` — i nuovi dati riempiono i campi mancanti, non sovrascrivono valori esistenti non-null. Eccezioni: `vote_average`, `vote_count`, `popularity`, `poster_path` sempre aggiornati.

---

## Stato Attuale & Lavori Pendenti

### Build
✅ `HorrorFriday.Web` e `HorrorFriday.API` compilano con 0 errori, 0 warning.

### Import dati (steps immediati)
| Step | Stato | Note |
|------|-------|------|
| Kaggle CSV bulk import (~298k righe) | ✅ Done | CsvImporter Mode [1]. Non rieseguire. |
| TMDB Movie Dump (~900k) | ✅ Done | TmdbImporter Mode [5]. Completato. |
| Migration 006 (TV dump checkpoint) | ❌ Da fare | Eseguire `migrations/006_tv_dump_schema.sql` prima del Mode [6] |
| TMDB TV Dump (~230k) | ❌ Da fare | TmdbImporter Mode [6]. Si è fermato a ~10% — da rieseguire dall'inizio (o dal checkpoint se applicabile). Scaricare dump file prima. |
| Embedding backfill | Manuale | Dopo ogni Mode [2] (daily delta), girare Mode [7] manualmente. Decisione: tenerlo manuale per ora. |
| Backfill cert/provider | ❌ Da fare | Mode [3] per tutti i film esistenti + nuovi TV dopo Mode [6] |

### Feature da costruire (non bloccanti)
1. **Titoli simili** — `ORDER BY embedding <=> (SELECT embedding FROM movies WHERE id=$1) LIMIT 12` — gratuito, zero costi, tutto già in casa
2. **Trailer** — fetch on-demand da TMDB `/movie/{id}/videos` (chiave YouTube). Richiede `TMDB_API_KEY` nella config API a runtime. Non ancora iniziato.
3. **Pagina profilo con statistiche** — aggregare `user_movies`: titoli visti, generi preferiti, distribuzione voti
4. **Error boundary globale** — `<ErrorBoundary>` in `MainLayout.razor`
5. **Loading skeleton** per la griglia cards
6. **Meta/OG tags** per SEO e link preview (Blazor WASM non fa SSR)
7. **Autocomplete** nella search bar

---

## TODO — Technical (pre-deploy & improvements)

- [ ] **Email verification** su registrazione
- [ ] **Account deletion** — GDPR Art. 17, rimuove tutti i dati entro 30 giorni
- [ ] **Rate limiting** su endpoint auth (login, register, forgot-password)
- [ ] **Structured logging** — Serilog o simile
- [ ] **Health check** — `GET /health` per uptime monitoring
- [ ] **CORS hardening** — lock `AllowedOrigins` al dominio production
- [ ] **CSP headers** — Content-Security-Policy, X-Frame-Options, ecc.
- [ ] **Database indexes audit** — tutti le colonne filtrate/join indicizzate
- [ ] **PostgreSQL backup** — backup giornaliero automatico prima del go-live
- [ ] **CI/CD pipeline** — GitHub Actions per build + test su PR
- [ ] **Production secrets** — ambiente variabili o Azure Key Vault
- [ ] **Deploy** — hosting (Azure App Service / VPS / Railway), HTTPS, dominio `horrorfriday.com`
- [ ] **Rimuovere dipendenza poster_path da TMDB** — scaricare poster su CDN propria prima della commercializzazione

---

## TODO — Features (complete)

### Core / Attese
- [ ] **User public profile** — `/u/{username}` — watchlist, conteggio visti, avg rating, generi preferiti
- [ ] **Import/export libreria** — CSV/JSON download
- [ ] **Notifiche** — in-app alerts (es. "nuovi horror questa settimana nel tuo gusto")
- [ ] **Weekly email digest** — opt-in settimanale con top horror curati da embedding

### Differenzianti / Originali
- [ ] **"Horror DNA" profile** — fingerprint del gusto utente (subgeneri: slasher, folk horror, cosmic horror...) — visual breakdown
- [ ] **Mood-based search** — l'utente sceglie un mood → query semantica curata
- [ ] **"Tonight's Pick"** — raccomandazione AI giornaliera basata su storia utente, giorno, stagione
- [ ] **Community tags/votes** — utenti taggano film con label subgenere horror; tag popolari sulle card
- [ ] **User-created lists** — collezioni nominate ("Marathon di Dario Argento", "Film da fare ad Halloween")
- [ ] **Social following** — segui altri utenti, vedi i loro ultimi visti, raccomandazioni da gusti simili
- [ ] **"Scared-o-meter" rating** — separato dal voto qualità — quanto fa paura? Scala icone horror
- [ ] **PWA** — installabile con offline support per la libreria
