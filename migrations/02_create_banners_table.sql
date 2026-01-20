-- Migration: Create banners table
-- Description: Adds support for marketing banners with image URLs

CREATE TABLE IF NOT EXISTS banners (
    id SERIAL PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    subtitle VARCHAR(255),
    description TEXT NOT NULL DEFAULT '',
    image_url VARCHAR(500),
    lang VARCHAR(10) NOT NULL DEFAULT 'ru',
    path VARCHAR(255),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Maintain compatibility with older schemas that still have the legacy columns.
ALTER TABLE IF EXISTS banners ADD COLUMN IF NOT EXISTS image_url VARCHAR(500);
ALTER TABLE IF EXISTS banners DROP COLUMN IF EXISTS url;
ALTER TABLE IF EXISTS banners DROP COLUMN IF EXISTS image_path;

-- Create index on lang for faster filtering
CREATE INDEX IF NOT EXISTS idx_banners_lang ON banners(lang);

-- Create index on is_active for faster filtering
CREATE INDEX IF NOT EXISTS idx_banners_is_active ON banners(is_active);

-- Create index on created_at for faster sorting
CREATE INDEX IF NOT EXISTS idx_banners_created_at ON banners(created_at DESC);

COMMENT ON TABLE banners IS 'Marketing banners displayed in the mobile app';
COMMENT ON COLUMN banners.id IS 'Primary key';
COMMENT ON COLUMN banners.title IS 'Banner title';
COMMENT ON COLUMN banners.subtitle IS 'Optional subtitle';
COMMENT ON COLUMN banners.description IS 'Banner content in HTML format';
COMMENT ON COLUMN banners.image_url IS 'Full URL to the banner image';
COMMENT ON COLUMN banners.lang IS 'Language code (en, ru, uz)';
COMMENT ON COLUMN banners.path IS 'Optional internal app path';
COMMENT ON COLUMN banners.created_at IS 'Timestamp when banner was created';
COMMENT ON COLUMN banners.is_active IS 'Whether the banner is active and should be displayed';
