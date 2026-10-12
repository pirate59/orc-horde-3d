-- One campaign save per anonymous player.
--   id         the player's id, made up by their first device
--   owner      SHA-256 of their secret: only that secret may read or change the save
--   revision   goes up on every change; a write that isn't newer is refused
--   updated_at when the player's device last changed it (Unix ms)
--   data       the save document itself, as the game wrote it
CREATE TABLE saves (
  id TEXT PRIMARY KEY,
  owner TEXT NOT NULL,
  revision INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  data TEXT NOT NULL,
  created_at INTEGER NOT NULL DEFAULT (unixepoch() * 1000),
  stored_at INTEGER NOT NULL DEFAULT (unixepoch() * 1000)
);
