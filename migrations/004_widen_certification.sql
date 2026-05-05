-- ============================================================
-- Migration 004: Widen certification column
-- Run ONCE before resuming Mode [3] backfill.
-- TMDB returns certification strings longer than VARCHAR(20)
-- for some countries (e.g. "Unrestricted Public Exhibition").
-- ============================================================

ALTER TABLE movie_certifications
    ALTER COLUMN certification TYPE TEXT;
