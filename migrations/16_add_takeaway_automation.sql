ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS takeaway_accepted_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS takeaway_preparing_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS takeaway_ready_for_pickup_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS ix_orders_takeaway_automation_active
    ON orders (service_mode, status, takeaway_accepted_at, created_at);

CREATE TABLE IF NOT EXISTS takeaway_automation_settings (
    id INTEGER PRIMARY KEY,
    preparing_delay_minutes INTEGER NOT NULL DEFAULT 5,
    ready_for_pickup_delay_minutes INTEGER NOT NULL DEFAULT 30,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (preparing_delay_minutes >= 0),
    CHECK (ready_for_pickup_delay_minutes >= 0)
);

INSERT INTO takeaway_automation_settings (
    id,
    preparing_delay_minutes,
    ready_for_pickup_delay_minutes
)
VALUES (1, 5, 30)
ON CONFLICT (id) DO NOTHING;
