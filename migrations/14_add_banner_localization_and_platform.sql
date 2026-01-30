-- Migration: Add language-specific and platform-specific fields for banners
-- Description: Adds support for different image URLs and deeplinks per language and platform

-- Add language-specific image URLs (JSON storage for flexibility)
ALTER TABLE banners ADD COLUMN IF NOT EXISTS image_urls JSONB DEFAULT '{}';

-- Add language-specific deeplinks (JSON storage for flexibility)
ALTER TABLE banners ADD COLUMN IF NOT EXISTS deeplinks JSONB DEFAULT '{}';

-- Add platform-specific fields (mobile app vs website)
-- Platform structure: { "mobile": {...}, "web": {...} }
ALTER TABLE banners ADD COLUMN IF NOT EXISTS platforms JSONB DEFAULT '{}';

-- Comments for documentation
COMMENT ON COLUMN banners.image_urls IS 'Language-specific image URLs: {"en": "url", "ru": "url", "uz": "url"}';
COMMENT ON COLUMN banners.deeplinks IS 'Language-specific deeplinks: {"en": "path", "ru": "path", "uz": "path"}';
COMMENT ON COLUMN banners.platforms IS 'Platform-specific configuration: {"mobile": {"imageUrls": {...}, "deeplinks": {...}}, "web": {"imageUrls": {...}, "deeplinks": {...}}}';

-- Create index on platforms for faster filtering
CREATE INDEX IF NOT EXISTS idx_banners_platforms ON banners USING GIN (platforms);

-- Migrate existing data: copy current image_url and path to new structures
UPDATE banners
SET
    image_urls = CASE
        WHEN image_url IS NOT NULL AND image_url != ''
        THEN jsonb_build_object(lang, image_url)
        ELSE '{}'::jsonb
    END,
    deeplinks = CASE
        WHEN path IS NOT NULL AND path != ''
        THEN jsonb_build_object(lang, path)
        ELSE '{}'::jsonb
    END,
    platforms = jsonb_build_object(
        'mobile', jsonb_build_object(
            'imageUrls', CASE WHEN image_url IS NOT NULL AND image_url != '' THEN jsonb_build_object(lang, image_url) ELSE '{}'::jsonb END,
            'deeplinks', CASE WHEN path IS NOT NULL AND path != '' THEN jsonb_build_object(lang, path) ELSE '{}'::jsonb END
        ),
        'web', jsonb_build_object(
            'imageUrls', CASE WHEN image_url IS NOT NULL AND image_url != '' THEN jsonb_build_object(lang, image_url) ELSE '{}'::jsonb END,
            'deeplinks', CASE WHEN path IS NOT NULL AND path != '' THEN jsonb_build_object(lang, path) ELSE '{}'::jsonb END
        )
    )
WHERE image_urls = '{}'::jsonb;
