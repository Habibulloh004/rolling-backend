-- Migration: Fix legacy orders id column types
-- Description: Ensure orders/id columns are VARCHAR for string-based lookups

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'id'
          AND data_type <> 'character varying'
    ) THEN
        IF EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'orders'
              AND column_name = 'id'
              AND is_identity = 'YES'
        ) THEN
            ALTER TABLE orders
                ALTER COLUMN id DROP IDENTITY IF EXISTS;
        END IF;
        ALTER TABLE orders
            ALTER COLUMN id TYPE VARCHAR(255)
            USING id::text;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'order_items'
          AND column_name = 'order_id'
          AND data_type <> 'character varying'
    ) THEN
        ALTER TABLE order_items
            ALTER COLUMN order_id TYPE VARCHAR(255)
            USING order_id::text;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'order_timeline_events'
          AND column_name = 'order_id'
          AND data_type <> 'character varying'
    ) THEN
        ALTER TABLE order_timeline_events
            ALTER COLUMN order_id TYPE VARCHAR(255)
            USING order_id::text;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'poster_transaction_id'
          AND data_type <> 'character varying'
    ) THEN
        ALTER TABLE orders
            ALTER COLUMN poster_transaction_id TYPE VARCHAR(50)
            USING poster_transaction_id::text;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'poster_incoming_order_id'
          AND data_type <> 'character varying'
    ) THEN
        ALTER TABLE orders
            ALTER COLUMN poster_incoming_order_id TYPE VARCHAR(50)
            USING poster_incoming_order_id::text;
    END IF;
END $$;
