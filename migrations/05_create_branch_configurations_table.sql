-- Migration: Create branch_configurations table
-- Description: Creates the branch_configurations table for storing restaurant branch settings

CREATE TABLE IF NOT EXISTS branch_configurations (
    id SERIAL PRIMARY KEY,
    spot_id INTEGER NOT NULL UNIQUE,

    -- Localized Names
    name_en VARCHAR(255) NOT NULL,
    name_ru VARCHAR(255),
    name_uz VARCHAR(255),

    -- Localized Short Description
    short_description_en TEXT,
    short_description_ru TEXT,
    short_description_uz TEXT,

    -- Status
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_temporarily_closed BOOLEAN NOT NULL DEFAULT FALSE,

    -- Closure Message (Localized)
    closure_message_en TEXT,
    closure_message_ru TEXT,
    closure_message_uz TEXT,

    -- Operating Hours
    open_time VARCHAR(10),
    close_time VARCHAR(10),
    timezone VARCHAR(50),
    is_24_hours BOOLEAN NOT NULL DEFAULT FALSE,

    -- Delivery Settings
    delivery_fee DECIMAL(18, 2),
    free_delivery_threshold DECIMAL(18, 2),
    min_order_amount DECIMAL(18, 2),
    max_order_amount DECIMAL(18, 2),
    eta_min_minutes INTEGER,
    eta_max_minutes INTEGER,
    eta_display_text_en VARCHAR(100),
    eta_display_text_ru VARCHAR(100),
    eta_display_text_uz VARCHAR(100),
    delivery_radius DOUBLE PRECISION,
    is_delivery_enabled BOOLEAN NOT NULL DEFAULT TRUE,

    -- Pickup Settings
    is_pickup_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    pickup_eta_minutes INTEGER,
    pickup_discount_percentage DECIMAL(5, 2),
    pickup_discount_text_en VARCHAR(255),
    pickup_discount_text_ru VARCHAR(255),
    pickup_discount_text_uz VARCHAR(255),

    -- Contact Information
    phone VARCHAR(50),
    alternate_phone VARCHAR(50),
    email VARCHAR(255),
    whatsapp VARCHAR(50),
    telegram VARCHAR(100),
    instagram VARCHAR(100),

    -- Location
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    address_en TEXT,
    address_ru TEXT,
    address_uz TEXT,
    city_en VARCHAR(100),
    city_ru VARCHAR(100),
    city_uz VARCHAR(100),
    district_en VARCHAR(100),
    district_ru VARCHAR(100),
    district_uz VARCHAR(100),
    landmark_en VARCHAR(255),
    landmark_ru VARCHAR(255),
    landmark_uz VARCHAR(255),
    google_maps_url TEXT,
    yandex_maps_url TEXT,

    -- Payment Configuration
    accepts_cash BOOLEAN NOT NULL DEFAULT TRUE,
    accepts_card BOOLEAN NOT NULL DEFAULT TRUE,
    accepts_online_payment BOOLEAN NOT NULL DEFAULT TRUE,
    accepts_apple_pay BOOLEAN NOT NULL DEFAULT FALSE,
    accepts_click BOOLEAN NOT NULL DEFAULT TRUE,
    accepts_payme BOOLEAN NOT NULL DEFAULT TRUE,
    accepts_uzum BOOLEAN NOT NULL DEFAULT FALSE,
    card_on_delivery BOOLEAN NOT NULL DEFAULT TRUE,

    -- Feature Flags
    is_dine_in_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    is_reservation_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    is_pre_order_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    is_loyalty_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_promo_codes_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_gift_cards_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    is_catering_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    show_ratings BOOLEAN NOT NULL DEFAULT TRUE,
    show_wait_time BOOLEAN NOT NULL DEFAULT TRUE,

    -- Media Assets
    logo_url TEXT,
    banner_url TEXT,
    thumbnail_url TEXT,
    gallery_urls_json TEXT,

    -- Ordering & Metadata
    sort_order INTEGER NOT NULL DEFAULT 0,
    tags_json TEXT,
    metadata_json TEXT,

    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_branch_configurations_is_active ON branch_configurations(is_active);
CREATE INDEX IF NOT EXISTS idx_branch_configurations_sort_order ON branch_configurations(sort_order);

-- Comments
COMMENT ON TABLE branch_configurations IS 'Restaurant branch configuration settings for multi-location support';
COMMENT ON COLUMN branch_configurations.spot_id IS 'Poster POS spot ID for integration';
COMMENT ON COLUMN branch_configurations.is_temporarily_closed IS 'Set to true when branch is temporarily closed but not permanently';
COMMENT ON COLUMN branch_configurations.is_24_hours IS 'If true, open/close time are ignored';
COMMENT ON COLUMN branch_configurations.delivery_radius IS 'Delivery radius in kilometers';
COMMENT ON COLUMN branch_configurations.pickup_discount_percentage IS 'Discount percentage for pickup orders (e.g., 10.00 for 10%)';
COMMENT ON COLUMN branch_configurations.gallery_urls_json IS 'JSON array of image URLs for gallery';
COMMENT ON COLUMN branch_configurations.tags_json IS 'JSON array of tags for filtering/categorization';
COMMENT ON COLUMN branch_configurations.metadata_json IS 'JSON object for additional custom metadata';
