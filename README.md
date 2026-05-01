# HorrorFriday

> **Discover your next horror obsession.**
> AI-powered movie discovery for the genre that never sleeps.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Blazor WASM](https://img.shields.io/badge/Blazor-WebAssembly-5C2D91?style=flat-square&logo=blazor&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-pgvector-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![OpenAI](https://img.shields.io/badge/OpenAI-Embeddings-412991?style=flat-square&logo=openai&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)

---

## What is HorrorFriday?

HorrorFriday is a full-stack web application for discovering horror and dark cinema. It combines a classic keyword search with **semantic AI search** — meaning you can describe a vibe, a feeling, or a concept, and the engine finds films that actually match, not just titles.

The database holds **~300,000 movies** with vector embeddings generated via OpenAI. Every title, overview, genre, and keyword is compressed into a 1536-dimensional embedding so that a query like *"claustrophobic survival horror with a bleak ending"* returns results that a keyword filter never could.

Users can build a personal library (watchlist, watched, watching, dropped), rate films, leave notes, and tag entries however they like.

---

## Features

| Area | What's included |
|---|---|
| **Search** | Keyword search + semantic AI search (pgvector cosine similarity) |
| **Filters** | Genre tri-state (include / neutral / exclude), year range, min rating, language, sort |
| **Library** | Per-user watchlist with status, rating (0–10), notes, and free-form tags |
| **Auth** | Email/password registration + Google OAuth, JWT (15 min) + refresh token (7 days) |
| **Remember Me** | Persists session in `localStorage`; session-only mode via `sessionStorage` |
| **Account** | Change username, email, password — all with uniqueness checks and policy enforcement |
| **Password recovery** | Secure email-based reset with SHA-256 hashed tokens (1-hour expiry) |
| **GDPR** | Cookie consent banner, full Privacy Policy and Cookie Policy in Italian |
| **Design** | Dark cinematic theme, responsive, single CSS file |

---

## Tech Stack

### Backend — `HorrorFriday.API`
- **ASP.NET Core 10** Web API
- **Npgsql** — raw SQL, no ORM (positional parameters only)
- **pgvector** — semantic vector search inside PostgreSQL
- **BCrypt.Net** — password hashing
- **JWT Bearer** — stateless authentication
- **Google.Apis.Auth** — Google OAuth token verification
- **System.Net.Mail** — transactional email via SMTP

### Frontend — `HorrorFriday.Web`
- **Blazor WebAssembly** (standalone, .NET 10)
- Separate `.razor` / `.razor.cs` code-behind for every component
- JS Interop for browser storage, click-outside detection, and Google Identity

### Database
- **PostgreSQL** with the **pgvector** extension
- ~300k movies, each with a `vector(1536)` embedding column
- Raw SQL migrations in `migrations/`

### Data Pipeline — `HorrorFriday.Importer`
- .NET 10 console app
- Reads a TMDB CSV dataset, generates OpenAI embeddings in batches of 50, bulk-loads into PostgreSQL
- Resumable: skips already-processed records on restart

---

## Getting Started

### Prerequisites

| Tool | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ |
| [PostgreSQL](https://www.postgresql.org/download/) | 16+ with pgvector extension |
| [OpenAI API key](https://platform.openai.com/api-keys) | For embedding generation (Importer only) |
| Gmail account | For SMTP transactional email (App Password required) |

> **pgvector install:** after PostgreSQL is running, execute `CREATE EXTENSION IF NOT EXISTS vector;` once on your database.

---

### 1. Clone & set up the database

```bash
git clone https://github.com/C-Gian/HorrorFriday.git
cd HorrorFriday
```

Create the database and run the migrations:

```sql
CREATE DATABASE horrorfriday;

-- then connect to horrorfriday and run:
\i migrations/001_password_reset_tokens.sql
```

---

### 2. Configure the API

The API reads secrets from `appsettings.Development.json` (gitignored — create it yourself):

```
HorrorFriday.API/appsettings.Development.json
```

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Username=postgres;Password=YOUR_PASSWORD;Database=horrorfriday"
  },
  "Jwt": {
    "Key": "your-256-bit-secret-key-here"
  },
  "App": {
    "BaseUrl": "https://localhost:7151"
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": "587",
    "Username": "your-email@gmail.com",
    "Password": "your-gmail-app-password",
    "From": "your-email@gmail.com"
  }
}
```

> **Gmail App Password**: Google account → Security → 2-Step Verification → App Passwords. Generate one for "Mail".

---

### 3. Configure the frontend

The Blazor app needs the Google Client ID for the OAuth button. Create this file (gitignored):

```
HorrorFriday.Web/wwwroot/appsettings.json
```

```json
{
  "Google": {
    "ClientId": "your-google-client-id.apps.googleusercontent.com"
  }
}
```

> **Google Client ID**: [Google Cloud Console](https://console.cloud.google.com/) → APIs & Services → Credentials → OAuth 2.0 Client IDs. Authorized origins: `https://localhost:7151`.

---

### 4. Run locally

Open two terminals:

```bash
# Terminal 1 — API
cd HorrorFriday.API
dotnet run
# → http://localhost:5038
# → Swagger: http://localhost:5038/swagger
```

```bash
# Terminal 2 — Web
cd HorrorFriday.Web
dotnet run
# → https://localhost:7151
```

---

### 5. Import movie data (optional — first run only)

The importer bulk-loads movies from a TMDB CSV dataset into PostgreSQL with embeddings.

```bash
# Create the .env file
cp HorrorFriday.Importer/.env.example HorrorFriday.Importer/.env
# Fill in HF_DB and HF_OPENAI_KEY in the .env file

# Run the importer
cd HorrorFriday.Importer
dotnet run
```

> The first run processes ~300k movies and can take several hours depending on OpenAI API throughput. The importer is resumable — restart it at any time and it picks up where it left off.

---

## Project Structure

```
HorrorFriday/
├── HorrorFriday.API/
│   ├── Controllers/          # AuthController, AccountController, MoviesController, UserMoviesController
│   ├── Services/             # AuthService, TokenService, MovieService, UserMovieService,
│   │                         # PasswordResetService, EmailService, EmbeddingService
│   ├── Models/               # DTOs and request/response models
│   ├── Helpers/              # PasswordPolicy (shared validation rules)
│   └── appsettings.json      # Non-sensitive config skeleton
│
├── HorrorFriday.Web/
│   ├── Pages/                # One .razor + .razor.cs per page
│   │   ├── Home.razor        # /  — search & browse
│   │   ├── Login.razor       # /login
│   │   ├── ForgotPassword    # /forgot-password
│   │   ├── ResetPassword     # /reset-password?token=...
│   │   ├── Profile.razor     # /profile
│   │   ├── Settings.razor    # /settings
│   │   ├── MovieDetail.razor # /movie/{id}
│   │   ├── Privacy.razor     # /privacy
│   │   └── CookiePolicy      # /cookies
│   ├── Shared/               # AppNavbar, AppFooter, MovieCard, CookieBanner, ...
│   ├── Services/             # AuthService (client-side JWT + storage routing)
│   ├── Helpers/              # PasswordPolicy (mirrors backend rules)
│   └── wwwroot/
│       └── css/app.css       # All styles — single file, dark cinematic theme
│
├── HorrorFriday.Importer/    # One-time bulk import utility — DO NOT MODIFY
│
├── migrations/               # Raw SQL files — run manually against PostgreSQL
│
└── horrorfriday_context.md   # Project context & session notes for AI-assisted dev
```

---

## API Reference

Full interactive docs available at `http://localhost:5038/swagger` when running locally.

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `POST` | `/api/auth/register` | — | Create account |
| `POST` | `/api/auth/login` | — | Login, get JWT + refresh token |
| `POST` | `/api/auth/refresh` | — | Rotate refresh token |
| `POST` | `/api/auth/google` | — | Google OAuth login |
| `POST` | `/api/account/forgot-password` | — | Request password reset email |
| `POST` | `/api/account/reset-password` | — | Confirm token and set new password |
| `PUT` | `/api/account/change-password` | JWT | Change password |
| `PUT` | `/api/account/change-email` | JWT | Change email |
| `PUT` | `/api/account/change-username` | JWT | Change username |
| `POST` | `/api/movies/search` | — | Search with filters + optional semantic query |
| `GET` | `/api/movies/{id}` | — | Movie detail |
| `GET` | `/api/movies/genres` | — | All genres |
| `GET` | `/api/user/movies` | JWT | User library (filterable by status) |
| `PUT` | `/api/user/movies/{movieId}` | JWT | Add or update a library entry |
| `DELETE` | `/api/user/movies/{movieId}` | JWT | Remove from library |

---

## Environment Variables

A complete reference for every secret the project needs:

| File | Key | Description |
|---|---|---|
| `appsettings.Development.json` | `ConnectionStrings.Default` | PostgreSQL connection string |
| `appsettings.Development.json` | `Jwt.Key` | HS256 signing key (min 32 chars) |
| `appsettings.Development.json` | `App.BaseUrl` | Frontend base URL (for password reset links) |
| `appsettings.Development.json` | `Smtp.*` | Gmail SMTP host, port, credentials |
| `wwwroot/appsettings.json` | `Google.ClientId` | Google OAuth client ID |
| `.env` (Importer only) | `HF_DB` | PostgreSQL connection string |
| `.env` (Importer only) | `HF_OPENAI_KEY` | OpenAI API key for embedding generation |

All files containing real values are gitignored. Only templates/skeletons are committed.

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">Built with a love for horror cinema and a hatred for bad recommendations.</p>
