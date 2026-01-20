-- Migration: Enable pgcrypto extension
-- Description: Required for gen_random_uuid() usage in notification_events

CREATE EXTENSION IF NOT EXISTS pgcrypto;
