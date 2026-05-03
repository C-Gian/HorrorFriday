-- ============================================================
-- Migration 003: Movie enrichment columns
-- Run ONCE before the CSV enrichment script.
-- Adds crew/cast fields and financial data that were in the
-- new Kaggle CSV but never in the original movies table.
-- ============================================================

ALTER TABLE movies
    ADD COLUMN IF NOT EXISTS budget              BIGINT,
    ADD COLUMN IF NOT EXISTS revenue             BIGINT,
    ADD COLUMN IF NOT EXISTS cast_list           TEXT,
    ADD COLUMN IF NOT EXISTS director            TEXT,
    ADD COLUMN IF NOT EXISTS director_of_photography TEXT,
    ADD COLUMN IF NOT EXISTS writers             TEXT,
    ADD COLUMN IF NOT EXISTS producers           TEXT,
    ADD COLUMN IF NOT EXISTS music_composer      TEXT,
    ADD COLUMN IF NOT EXISTS imdb_rating         NUMERIC(4,2),
    ADD COLUMN IF NOT EXISTS imdb_votes          INTEGER;
