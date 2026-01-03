-- Migration: Create new order tables
-- Description: Creates orders, order_items, order_timeline_events tables matching iOS data structure

-- Orders table (main order entity matching iOS Order model)
CREATE TABLE IF NOT EXISTS orders (
    id VARCHAR(255) PRIMARY KEY,
    order_number VARCHAR(50) NOT NULL DEFAULT '',
    date TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),

    -- Financial Data
    subtotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    delivery_fee DECIMAL(18, 2) NOT NULL DEFAULT 0,
    discount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    total DECIMAL(18, 2) NOT NULL DEFAULT 0,

    -- Status (0=AwaitingPayment, 1=Pending, 2=Accepted, 3=Preparing, 4=OnTheWay, 5=Delivered, 6=Cancelled)
    status INTEGER NOT NULL DEFAULT 1,

    -- Delivery Information
    delivery_address TEXT NOT NULL DEFAULT '',
    delivery_latitude DOUBLE PRECISION,
    delivery_longitude DOUBLE PRECISION,
    delivery_address_comment TEXT,

    -- Payment Information
    payment_method VARCHAR(100) NOT NULL DEFAULT '',
    payment_method_id VARCHAR(10),
    payment_transaction_id VARCHAR(255),
    payment_error_code VARCHAR(50),
    payment_error_message TEXT,
    payment_attempts INTEGER NOT NULL DEFAULT 0,
    saved_card_token VARCHAR(255),

    -- Delivery Time
    estimated_delivery_time TIMESTAMP WITH TIME ZONE,
    actual_delivery_time TIMESTAMP WITH TIME ZONE,
    estimated_delivery_minutes_min INTEGER,
    estimated_delivery_minutes_max INTEGER,

    -- Branch Information
    branch_id VARCHAR(255),
    branch_name VARCHAR(255),
    branch_address TEXT,
    branch_phone VARCHAR(50),

    -- Poster Integration
    poster_spot_id VARCHAR(50),
    poster_incoming_order_id VARCHAR(50),
    poster_transaction_id VARCHAR(50),

    -- Loyalty Program
    counted_towards_loyalty BOOLEAN NOT NULL DEFAULT FALSE,
    loyalty_points_spent DECIMAL(18, 2),
    loyalty_points_earned_pending DECIMAL(18, 2),
    loyalty_points_earned_actual DECIMAL(18, 2),

    -- Receipt
    receipt_url TEXT,

    -- Contact Information
    phone VARCHAR(50) NOT NULL DEFAULT '',
    alternate_phone VARCHAR(50),
    first_name VARCHAR(100),
    last_name VARCHAR(100),

    -- Order Comment/Instructions
    comment TEXT,

    -- Courier Information
    courier_id VARCHAR(255),
    courier_name VARCHAR(255),
    courier_rating DOUBLE PRECISION,
    courier_photo_url TEXT,
    courier_vehicle VARCHAR(100),
    courier_license_plate VARCHAR(50),
    courier_phone VARCHAR(50),
    courier_latitude DOUBLE PRECISION,
    courier_longitude DOUBLE PRECISION,
    courier_location_last_updated TIMESTAMP WITH TIME ZONE,

    -- Service Mode (1 = dine-in, 2 = pickup, 3 = delivery)
    service_mode INTEGER NOT NULL DEFAULT 3,

    -- Promo Code
    promo_code VARCHAR(100),
    promo_discount_amount DECIMAL(18, 2),
    promo_discount_percentage DOUBLE PRECISION,

    -- User Information
    user_id VARCHAR(255),
    user_order_count INTEGER,

    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Indexes for orders table
CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
CREATE INDEX IF NOT EXISTS idx_orders_user_id ON orders(user_id);
CREATE INDEX IF NOT EXISTS idx_orders_phone ON orders(phone);
CREATE INDEX IF NOT EXISTS idx_orders_poster_spot_id ON orders(poster_spot_id);
CREATE INDEX IF NOT EXISTS idx_orders_poster_incoming_order_id ON orders(poster_incoming_order_id);
CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(date DESC);

-- Order items table (matching iOS OrderItem model)
CREATE TABLE IF NOT EXISTS order_items (
    id VARCHAR(255) PRIMARY KEY,
    order_id VARCHAR(255) NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    menu_item_id VARCHAR(255) NOT NULL DEFAULT '',
    name VARCHAR(255) NOT NULL DEFAULT '',
    quantity INTEGER NOT NULL DEFAULT 1,
    price DECIMAL(18, 2) NOT NULL DEFAULT 0,
    total_price DECIMAL(18, 2) NOT NULL DEFAULT 0,
    modifiers TEXT,
    modifier_id VARCHAR(255),
    image_url TEXT,
    is_bonus BOOLEAN NOT NULL DEFAULT FALSE,
    price_override DECIMAL(18, 2)
);

CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items(order_id);
CREATE INDEX IF NOT EXISTS idx_order_items_menu_item_id ON order_items(menu_item_id);

-- Order timeline events table (matching iOS TimelineEvent model)
CREATE TABLE IF NOT EXISTS order_timeline_events (
    id VARCHAR(255) PRIMARY KEY,
    order_id VARCHAR(255) NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL DEFAULT '',
    time TIMESTAMP WITH TIME ZONE,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    is_current BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_order_timeline_events_order_id ON order_timeline_events(order_id);
CREATE INDEX IF NOT EXISTS idx_order_timeline_events_sort_order ON order_timeline_events(order_id, sort_order);

-- Comments
COMMENT ON TABLE orders IS 'Customer orders from iOS app (matching iOS Order model)';
COMMENT ON TABLE order_items IS 'Order line items (matching iOS OrderItem model)';
COMMENT ON TABLE order_timeline_events IS 'Order status timeline events (matching iOS TimelineEvent model)';

COMMENT ON COLUMN orders.status IS '0=AwaitingPayment, 1=Pending, 2=Accepted, 3=Preparing, 4=OnTheWay, 5=Delivered, 6=Cancelled';
COMMENT ON COLUMN orders.payment_method_id IS '"1" for cash, "2" for card';
COMMENT ON COLUMN orders.service_mode IS '1=dine-in, 2=pickup, 3=delivery';
