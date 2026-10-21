CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    CREATE TABLE outgoing_messages (
        id uuid NOT NULL,
        sequence bigint GENERATED ALWAYS AS IDENTITY,
        source character varying(32) NOT NULL,
        room_jid character varying(512) NOT NULL,
        content text NOT NULL,
        status character varying(16) NOT NULL,
        attempts integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        locked_by character varying(128),
        lease_until timestamp with time zone,
        sent_at timestamp with time zone,
        dead_at timestamp with time zone,
        last_error character varying(512),
        CONSTRAINT "PK_outgoing_messages" PRIMARY KEY (id),
        CONSTRAINT ck_message_attempts CHECK (attempts >= 0),
        CONSTRAINT ck_message_status CHECK (status IN ('pending', 'processing', 'sent', 'dead'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_outgoing_active ON outgoing_messages (sequence) WHERE status IN ('pending', 'processing');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    CREATE INDEX "IX_outgoing_messages_dead_at" ON outgoing_messages (dead_at) WHERE status = 'dead';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    CREATE INDEX "IX_outgoing_messages_lease_until" ON outgoing_messages (lease_until) WHERE status = 'processing';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    CREATE INDEX "IX_outgoing_messages_sent_at" ON outgoing_messages (sent_at) WHERE status = 'sent';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021003113_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261021003113_InitialCreate', '10.0.6');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021024244_DirectUserRecipients') THEN
    ALTER TABLE outgoing_messages RENAME COLUMN room_jid TO recipient_jid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021024244_DirectUserRecipients') THEN
    UPDATE outgoing_messages
    SET status = 'dead',
        dead_at = COALESCE(dead_at, now()),
        locked_by = NULL,
        lease_until = NULL,
        last_error = left('legacy-room-message: ' || COALESCE(last_error, 'manual-review-required'), 512)
    WHERE status IN ('pending', 'processing', 'dead');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021024244_DirectUserRecipients') THEN
    CREATE INDEX "IX_outgoing_messages_recipient_jid_sequence" ON outgoing_messages (recipient_jid, sequence) WHERE status IN ('pending', 'processing');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021024244_DirectUserRecipients') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261021024244_DirectUserRecipients', '10.0.6');
    END IF;
END $EF$;
COMMIT;
