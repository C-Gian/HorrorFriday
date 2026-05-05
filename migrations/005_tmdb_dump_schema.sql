-- ============================================================
-- Migration 005: TMDB Dump Import schema
-- Run ONCE before using TmdbImporter Mode [5].
-- Adds detail columns not available from the discover API
-- and a singleton checkpoint table for resumable dump import.
-- ============================================================

-- New detail columns on the movies table (all nullable, safe to add)
ALTER TABLE movies
    ADD COLUMN IF NOT EXISTS backdrop_path        TEXT,
    ADD COLUMN IF NOT EXISTS homepage             TEXT,
    ADD COLUMN IF NOT EXISTS collection_id        INTEGER,
    ADD COLUMN IF NOT EXISTS collection_name      TEXT,
    ADD COLUMN IF NOT EXISTS spoken_languages     TEXT,   -- comma-separated ISO 639-1 codes e.g. "en,fr"
    ADD COLUMN IF NOT EXISTS production_companies TEXT,   -- comma-separated company names
    ADD COLUMN IF NOT EXISTS production_countries TEXT;   -- comma-separated ISO 3166-1 codes e.g. "US,GB"

-- Singleton row tracking the last TMDB ID fully processed by the dump import.
-- On restart the service skips all IDs <= last_processed_tmdb_id.
CREATE TABLE IF NOT EXISTS tmdb_dump_progress (
    id                     INTEGER PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    last_processed_tmdb_id INTEGER NOT NULL DEFAULT 0,
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO tmdb_dump_progress (id, last_processed_tmdb_id)
VALUES (1, 0)
ON CONFLICT DO NOTHING;
