CREATE TABLE IF NOT EXISTS shutdown_mailboxes (
  id TEXT PRIMARY KEY,
  phone_hash TEXT NOT NULL,
  seen INTEGER NOT NULL,
  issued INTEGER NOT NULL DEFAULT 0,
  signature TEXT,
  delivered INTEGER NOT NULL DEFAULT 0
);
