-- Run with psql -v message_id='UUID' -v capacity=5000 -f deploy/sql/requeue-dead.sql
\set ON_ERROR_STOP on
BEGIN;
SELECT pg_advisory_xact_lock(742341902);
UPDATE outgoing_messages
SET status = 'pending', attempts = 0, next_attempt_at = now(),
    dead_at = NULL, locked_by = NULL, lease_until = NULL, last_error = NULL
WHERE id = :'message_id'::uuid AND status = 'dead'
  AND COALESCE(last_error, '') NOT LIKE 'legacy-room-message:%'
  AND (SELECT count(*) FROM outgoing_messages WHERE status IN ('pending', 'processing')) < :capacity
RETURNING id, sequence, recipient_jid, status;
COMMIT;
