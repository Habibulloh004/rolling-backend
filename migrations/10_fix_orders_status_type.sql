-- Migration: Fix legacy orders status type
-- Description: Coerce orders.status to integer when stored as text/varchar

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'status'
          AND data_type IN ('text', 'character varying')
    ) THEN
        ALTER TABLE orders
            ALTER COLUMN status TYPE INTEGER
            USING (CASE WHEN status ~ '^[0-9]+$' THEN status::integer ELSE NULL END);
    END IF;
END $$;
