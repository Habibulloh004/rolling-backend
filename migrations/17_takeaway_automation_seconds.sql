ALTER TABLE takeaway_automation_settings
    ADD COLUMN IF NOT EXISTS preparing_delay_seconds INTEGER,
    ADD COLUMN IF NOT EXISTS ready_for_pickup_delay_seconds INTEGER;

UPDATE takeaway_automation_settings
SET
    preparing_delay_seconds = COALESCE(
        preparing_delay_seconds,
        GREATEST(COALESCE(preparing_delay_minutes, 0), 0) * 60
    ),
    ready_for_pickup_delay_seconds = COALESCE(
        ready_for_pickup_delay_seconds,
        GREATEST(COALESCE(ready_for_pickup_delay_minutes, 0), 0) * 60
    );

UPDATE takeaway_automation_settings
SET
    preparing_delay_seconds = 300
WHERE preparing_delay_seconds IS NULL;

UPDATE takeaway_automation_settings
SET
    ready_for_pickup_delay_seconds = 1800
WHERE ready_for_pickup_delay_seconds IS NULL;

ALTER TABLE takeaway_automation_settings
    ALTER COLUMN preparing_delay_seconds SET DEFAULT 300,
    ALTER COLUMN ready_for_pickup_delay_seconds SET DEFAULT 1800;

ALTER TABLE takeaway_automation_settings
    ALTER COLUMN preparing_delay_seconds SET NOT NULL,
    ALTER COLUMN ready_for_pickup_delay_seconds SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'takeaway_automation_settings_preparing_delay_seconds_check'
    ) THEN
        ALTER TABLE takeaway_automation_settings
            ADD CONSTRAINT takeaway_automation_settings_preparing_delay_seconds_check
            CHECK (preparing_delay_seconds >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'takeaway_automation_settings_ready_for_pickup_delay_seconds_check'
    ) THEN
        ALTER TABLE takeaway_automation_settings
            ADD CONSTRAINT takeaway_automation_settings_ready_for_pickup_delay_seconds_check
            CHECK (ready_for_pickup_delay_seconds >= 0);
    END IF;
END $$;
