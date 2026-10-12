/**
 * Orc Horde FPS: serves the browser build and keeps each player's campaign save.
 *
 * No accounts. A player's first device makes up an id and a secret; the save is kept
 * under the id, and only requests carrying the secret may read or change it. The id
 * and secret together are the player's "save code", which they can enter on another
 * device to carry the campaign over.
 *
 *   GET /api/save/:id   Authorization: Bearer <secret>
 *     200 { revision, updatedAt, data } | 404 nothing saved | 403 wrong secret
 *   PUT /api/save/:id   Authorization: Bearer <secret>   body { revision, updatedAt, data }
 *     200 { revision } stored
 *     409 { revision, updatedAt, data } the server's copy is as new or newer: nothing stored
 *     403 wrong secret | 400 malformed | 413 too large
 *
 * The first PUT for an id claims it for that secret. A write must carry a higher
 * revision than the stored one, checked in the same statement that writes it, so two
 * devices racing can't overwrite a newer copy with an older one.
 */

export interface Env {
  DB: D1Database;
  ASSETS: Fetcher;
}

/** Ids and secrets are URL-safe base64, as the game makes them. */
const ID = /^[A-Za-z0-9_-]{16,64}$/;
const SECRET = /^[A-Za-z0-9_-]{16,128}$/;
/** A full campaign with every map's layout is a few tens of KB; this is plenty. */
const MAX_BYTES = 512 * 1024;

type Envelope = { revision: number; updatedAt: number; data: string };
type Row = { owner: string; revision: number; updated_at: number; data: string };

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const match = url.pathname.match(/^\/api\/save\/([^/]+)$/);
    if (url.pathname.startsWith("/api/")) {
      if (!match) return json({ error: "not found" }, 404);
      try {
        return await save(request, env, match[1]);
      } catch (err) {
        console.error("save failed", err);
        return json({ error: "server error" }, 500);
      }
    }
    return env.ASSETS.fetch(request);
  },
} satisfies ExportedHandler<Env>;

async function save(request: Request, env: Env, id: string): Promise<Response> {
  if (!ID.test(id)) return json({ error: "bad id" }, 400);
  const secret = (request.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
  if (!SECRET.test(secret)) return json({ error: "missing or malformed secret" }, 403);
  const owner = await sha256(secret);

  const row = await env.DB.prepare("SELECT owner, revision, updated_at, data FROM saves WHERE id = ?")
    .bind(id).first<Row>();
  if (row && !(await same(row.owner, owner))) return json({ error: "forbidden" }, 403);

  if (request.method === "GET") {
    if (!row) return json({ error: "no save" }, 404);
    return json(envelope(row));
  }

  if (request.method !== "PUT") return json({ error: "method not allowed" }, 405, { Allow: "GET, PUT" });

  const text = await request.text();
  if (text.length > MAX_BYTES) return json({ error: "save too large" }, 413);
  const body = parse(text);
  if (!body) return json({ error: "malformed save" }, 400);

  if (!row) {
    // First write: claims the id for this secret. If another request claimed it a moment
    // ago, the insert does nothing and we answer as for any older write.
    const claimed = await env.DB.prepare(
      "INSERT INTO saves (id, owner, revision, updated_at, data) VALUES (?, ?, ?, ?, ?) ON CONFLICT(id) DO NOTHING",
    ).bind(id, owner, body.revision, body.updatedAt, body.data).run();
    if (claimed.meta.changes === 1) return json({ revision: body.revision });
    return conflictOrForbidden(env, id, owner);
  }

  if (body.revision <= row.revision) return json(envelope(row), 409);
  // Newer: store it, but only if nothing newer arrived since we read the row.
  const updated = await env.DB.prepare(
    "UPDATE saves SET revision = ?, updated_at = ?, data = ?, stored_at = ? WHERE id = ? AND owner = ? AND revision < ?",
  ).bind(body.revision, body.updatedAt, body.data, Date.now(), id, owner, body.revision).run();
  if (updated.meta.changes === 1) return json({ revision: body.revision });
  return conflictOrForbidden(env, id, owner);
}

async function conflictOrForbidden(env: Env, id: string, owner: string): Promise<Response> {
  const now = await env.DB.prepare("SELECT owner, revision, updated_at, data FROM saves WHERE id = ?").bind(id).first<Row>();
  if (!now) return json({ error: "server error" }, 500);
  if (!(await same(now.owner, owner))) return json({ error: "forbidden" }, 403);
  return json(envelope(now), 409);
}

/** A save as the game sent it: a non-negative revision and the document, which must itself be JSON. */
function parse(text: string): Envelope | null {
  let body: unknown;
  try { body = JSON.parse(text); } catch { return null; }
  if (typeof body !== "object" || body === null) return null;
  const { revision, updatedAt, data } = body as Record<string, unknown>;
  if (!Number.isInteger(revision) || (revision as number) < 0) return null;
  if (typeof data !== "string") return null;
  try { JSON.parse(data); } catch { return null; }
  return { revision: revision as number, updatedAt: Number.isFinite(updatedAt) ? Number(updatedAt) : Date.now(), data };
}

function envelope(row: Row): Envelope {
  return { revision: row.revision, updatedAt: row.updated_at, data: row.data };
}

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...headers },
  });
}

async function sha256(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** Compares two hashes without giving away, by timing, how much of them matched. */
async function same(a: string, b: string): Promise<boolean> {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
