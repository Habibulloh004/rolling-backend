-- Migration: Update banners table - replace image_path and url with image_url
-- Description: Changes image storage from path to full URL, removes unused url column

-- Add new image_url column
ALTER TABLE banners ADD COLUMN IF NOT EXISTS image_url VARCHAR(500);

-- Migrate existing data (if needed)
-- UPDATE banners SET image_url = CONCAT('http://localhost:5020/', image_path) WHERE image_path IS NOT NULL;

-- Drop old columns
ALTER TABLE banners DROP COLUMN IF EXISTS image_path;
ALTER TABLE banners DROP COLUMN IF EXISTS url;

-- Add comment
COMMENT ON COLUMN banners.image_url IS 'Full URL to banner image (e.g., http://localhost:5020/uploads/image.jpg)';
