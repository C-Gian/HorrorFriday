-- ============================================================
-- Migration 002: TMDB Sync Support
-- Run ONCE before the first TMDB sync, after migration 001.
-- ============================================================

-- 1. Add media_type so movies and TV shows can coexist
--    All existing CSV-imported rows default to 'movie'.
ALTER TABLE movies ADD COLUMN IF NOT EXISTS media_type VARCHAR(10) NOT NULL DEFAULT 'movie';

-- 2. Replace the single-column unique constraint with (tmdb_id, media_type)
--    because TMDB can reuse the same numeric ID for a movie and a TV show.
ALTER TABLE movies DROP CONSTRAINT IF EXISTS movies_tmdb_id_key;
DROP INDEX IF EXISTS movies_tmdb_id_key;
CREATE UNIQUE INDEX IF NOT EXISTS uidx_movies_tmdb_id_media_type ON movies(tmdb_id, media_type);

-- 3. Streaming/rent/buy provider catalogue (Netflix, Prime Video, etc.)
CREATE TABLE IF NOT EXISTS watch_providers (
    id               SERIAL PRIMARY KEY,
    tmdb_provider_id INTEGER NOT NULL UNIQUE,
    provider_name    VARCHAR(200) NOT NULL,
    logo_path        VARCHAR(200)
);

-- 4. Which providers have each movie, in which region and access type
CREATE TABLE IF NOT EXISTS movie_watch_providers (
    movie_id      INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
    provider_id   INTEGER NOT NULL REFERENCES watch_providers(id),
    region        VARCHAR(10) NOT NULL,
    provider_type VARCHAR(10) NOT NULL CHECK (provider_type IN ('stream', 'rent', 'buy', 'ads')),
    PRIMARY KEY (movie_id, provider_id, region, provider_type)
);

-- 5. Age certification per movie/show per region (e.g. "R", "VM18", "TV-MA")
CREATE TABLE IF NOT EXISTS movie_certifications (
    movie_id      INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
    region        VARCHAR(10) NOT NULL,
    certification VARCHAR(20) NOT NULL,
    PRIMARY KEY (movie_id, region)
);

-- 6. Audit log for every sync run
CREATE TABLE IF NOT EXISTS tmdb_sync_log (
    id                     SERIAL PRIMARY KEY,
    sync_type              VARCHAR(30) NOT NULL, -- 'full' | 'daily' | 'backfill' | 'discover'
    started_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    finished_at            TIMESTAMPTZ,
    new_movies_inserted    INTEGER NOT NULL DEFAULT 0,
    new_tv_inserted        INTEGER NOT NULL DEFAULT 0,
    certifications_updated INTEGER NOT NULL DEFAULT 0,
    providers_updated      INTEGER NOT NULL DEFAULT 0,
    errors                 INTEGER NOT NULL DEFAULT 0,
    notes                  TEXT
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_movie_watch_providers_movie  ON movie_watch_providers(movie_id);
CREATE INDEX IF NOT EXISTS idx_movie_watch_providers_region ON movie_watch_providers(region, provider_type);
CREATE INDEX IF NOT EXISTS idx_movie_certifications_movie   ON movie_certifications(movie_id);
CREATE INDEX IF NOT EXISTS idx_movies_media_type            ON movies(media_type);
