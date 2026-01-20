-- Migration: Add FCM token column to orders
-- Description: Adds fcm_token to store device push tokens for order notifications

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS fcm_token TEXT;

COMMENT ON COLUMN orders.fcm_token IS 'Device push token (FCM) for order notifications';
