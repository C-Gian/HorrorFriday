-- ============================================================
-- Migration 006: TV Dump Import schema
-- Run ONCE before using TmdbImporter Mode [6].
-- Adds a checkpoint table for the resumable TV series dump import.
-- ============================================================

CREATE TABLE IF NOT EXISTS tmdb_tv_dump_progress (
    id                     INTEGER PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    last_processed_tmdb_id INTEGER NOT NULL DEFAULT 0,
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO tmdb_tv_dump_progress (id, last_processed_tmdb_id)
VALUES (1, 0)
ON CONFLICT DO NOTHING;
