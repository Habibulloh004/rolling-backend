-- Migration: Add created_at to notifications
-- Description: Adds created_at timestamp for sorting and auditing notifications

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
