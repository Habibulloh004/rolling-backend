CREATE TABLE IF NOT EXISTS admin_credentials (
    id BIGSERIAL PRIMARY KEY,
    login VARCHAR(128) NOT NULL,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    name VARCHAR(128) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_admin_credentials_login_lower
    ON admin_credentials (LOWER(login));

CREATE TABLE IF NOT EXISTS admin_refresh_tokens (
    id BIGSERIAL PRIMARY KEY,
    admin_credential_id BIGINT NOT NULL REFERENCES admin_credentials(id) ON DELETE CASCADE,
    token_hash VARCHAR(256) NOT NULL,
    replaced_by_token_hash VARCHAR(256) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_admin_refresh_tokens_token_hash
    ON admin_refresh_tokens (token_hash);

CREATE INDEX IF NOT EXISTS ix_admin_refresh_tokens_admin_expires
    ON admin_refresh_tokens (admin_credential_id, expires_at DESC);
