-- Migration: Create chat support tables
-- Description: Adds chat_threads, chat_messages, chat_participants used by chat support

CREATE TABLE IF NOT EXISTS chat_threads (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    order_id UUID NOT NULL,
    customer_id UUID NOT NULL,
    status INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS chat_messages (
    id UUID PRIMARY KEY,
    thread_id UUID NOT NULL,
    sender_id UUID NOT NULL,
    sender_role INTEGER NOT NULL,
    content_type INTEGER NOT NULL,
    body TEXT NOT NULL,
    sent_at TIMESTAMPTZ NOT NULL,
    status INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS chat_participants (
    id UUID PRIMARY KEY,
    thread_id UUID NOT NULL REFERENCES chat_threads(id) ON DELETE CASCADE,
    user_id UUID NOT NULL,
    role INTEGER NOT NULL,
    display_name TEXT NOT NULL
);

-- Ensure legacy tables missing columns are patched before indexing.
ALTER TABLE chat_participants
    ADD COLUMN IF NOT EXISTS user_id UUID;

CREATE INDEX IF NOT EXISTS idx_chat_messages_thread_id_sent_at
    ON chat_messages(thread_id, sent_at);

CREATE UNIQUE INDEX IF NOT EXISTS idx_chat_participants_thread_id_user_id
    ON chat_participants(thread_id, user_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_chat_threads_tenant_order_customer
    ON chat_threads(tenant_id, order_id, customer_id);
