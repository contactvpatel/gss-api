/*
 * Idempotency keys for POST creates: one row per (user, Idempotency-Key) holding the first successful response, which a
 * retry with the same key and the same request replays instead of creating the resource again.
 *
 * The row is written in the same transaction as the create, so it exists exactly when the create committed. It has no
 * foreign key: the user is an SSO pid, not a row of this service. Rows older than the retention (24 hours) are ignored
 * by reads and deleted by IdempotencyCleanupHostedService; the createdat index serves that delete.
 *
 * Purely additive: the previous release neither reads nor writes the table. Re-runnable.
 */
CREATE TABLE IF NOT EXISTS gss.idempotencykey
(
    userid         integer                  NOT NULL,
    idempotencykey varchar(255)             NOT NULL,
    requesthash    char(64)                 NOT NULL,
    statuscode     integer                  NOT NULL,
    responsebody   text                     NOT NULL,
    location       text                     NULL,
    createdat      timestamp with time zone NOT NULL,
    CONSTRAINT pk_idempotencykey PRIMARY KEY (userid, idempotencykey)
);

CREATE INDEX IF NOT EXISTS idx_idempotencykey_createdat ON gss.idempotencykey (createdat);
