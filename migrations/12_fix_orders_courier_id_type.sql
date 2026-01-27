-- Migration: Fix orders.courier_id type for legacy schemas
-- Description: Ensure courier_id is VARCHAR to match app payloads

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'courier_id'
          AND data_type <> 'character varying'
    ) THEN
        ALTER TABLE orders
            ALTER COLUMN courier_id TYPE VARCHAR(255)
            USING courier_id::text;
    END IF;
END $$;
