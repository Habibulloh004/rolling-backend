-- Migration: Create core tables
-- Description: Creates transactions, courier_orders, users, notifications, times, notification_events tables

-- Transactions table (for payment processing)
CREATE TABLE IF NOT EXISTS transactions (
    id VARCHAR(255) PRIMARY KEY,
    transaction_id VARCHAR(255),
    user_id VARCHAR(255),
    order_details_json TEXT,
    status INTEGER NOT NULL DEFAULT 0,
    amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    order_id VARCHAR(255),
    create_time BIGINT NOT NULL DEFAULT 0,
    perform_time BIGINT NOT NULL DEFAULT 0,
    cancel_time BIGINT NOT NULL DEFAULT 0,
    reason INTEGER,
    provider VARCHAR(50) NOT NULL DEFAULT '',
    prepare_id VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Ensure legacy tables missing columns are patched before indexing.
ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS user_id VARCHAR(255),
    ADD COLUMN IF NOT EXISTS order_id VARCHAR(255),
    ADD COLUMN IF NOT EXISTS status INTEGER;

CREATE INDEX IF NOT EXISTS idx_transactions_user_id ON transactions(user_id);
CREATE INDEX IF NOT EXISTS idx_transactions_order_id ON transactions(order_id);
CREATE INDEX IF NOT EXISTS idx_transactions_status ON transactions(status);

-- Courier orders table (legacy orders for courier app)
CREATE TABLE IF NOT EXISTS courier_orders (
    id SERIAL PRIMARY KEY,
    order_id VARCHAR(255) NOT NULL DEFAULT '',
    courier_id BIGINT NOT NULL DEFAULT 0,
    order_data_json TEXT,
    products_json TEXT,
    status VARCHAR(50) NOT NULL DEFAULT 'waiting'
);

CREATE INDEX IF NOT EXISTS idx_courier_orders_courier_id ON courier_orders(courier_id);
CREATE INDEX IF NOT EXISTS idx_courier_orders_order_id ON courier_orders(order_id);
CREATE INDEX IF NOT EXISTS idx_courier_orders_status ON courier_orders(status);

-- Users table (Poster users/couriers)
CREATE TABLE IF NOT EXISTS users (
    user_id BIGINT PRIMARY KEY,
    name VARCHAR(255) NOT NULL DEFAULT '',
    login VARCHAR(255) NOT NULL DEFAULT '',
    role_name VARCHAR(100) NOT NULL DEFAULT '',
    role_id INTEGER NOT NULL DEFAULT 0,
    user_type INTEGER NOT NULL DEFAULT 0,
    access_mask BIGINT NOT NULL DEFAULT 0,
    last_in VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS idx_users_login ON users(login);
CREATE INDEX IF NOT EXISTS idx_users_role_id ON users(role_id);

-- Notifications table (push notification templates)
CREATE TABLE IF NOT EXISTS notifications (
    id SERIAL PRIMARY KEY,
    en_title VARCHAR(255) NOT NULL DEFAULT '',
    en_body TEXT NOT NULL DEFAULT '',
    ru_title VARCHAR(255) NOT NULL DEFAULT '',
    ru_body TEXT NOT NULL DEFAULT '',
    uz_title VARCHAR(255) NOT NULL DEFAULT '',
    uz_body TEXT NOT NULL DEFAULT ''
);

-- Business times table
CREATE TABLE IF NOT EXISTS times (
    id SERIAL PRIMARY KEY,
    opened_time VARCHAR(10) NOT NULL DEFAULT '09:00',
    closed_time VARCHAR(10) NOT NULL DEFAULT '23:00'
);

-- Insert default business hours
INSERT INTO times (opened_time, closed_time)
VALUES ('09:00', '23:00')
ON CONFLICT DO NOTHING;

-- Notification events table
CREATE TABLE IF NOT EXISTS notification_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    channel VARCHAR(255) NOT NULL DEFAULT '',
    title VARCHAR(255) NOT NULL DEFAULT '',
    message TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_notification_events_channel ON notification_events(channel);
CREATE INDEX IF NOT EXISTS idx_notification_events_created_at ON notification_events(created_at DESC);

-- Comments
COMMENT ON TABLE transactions IS 'Payment transactions for Click, Payme, Plum';
COMMENT ON TABLE courier_orders IS 'Orders assigned to couriers (legacy)';
COMMENT ON TABLE users IS 'Poster users including couriers';
COMMENT ON TABLE notifications IS 'Push notification templates (multilingual)';
COMMENT ON TABLE times IS 'Business operating hours';
COMMENT ON TABLE notification_events IS 'Notification event log';
