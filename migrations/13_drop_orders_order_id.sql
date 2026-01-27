-- Migration: Drop legacy orders.order_id column
-- Description: Remove legacy order_id column that conflicts with the current Order model.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'order_id'
    ) THEN
        ALTER TABLE orders
            DROP COLUMN order_id;
    END IF;
END $$;
