// Exercises the save API against a running Worker (npm run dev), default http://localhost:8787.
//   node test/saves.test.mjs [base-url]
const base = process.argv[2] ?? "http://localhost:8787";
const rand = (n) => Buffer.from(crypto.getRandomValues(new Uint8Array(n))).toString("base64url");
const id = rand(16), secret = rand(24), other = rand(24);
const doc = (rev) => JSON.stringify({ version: 1, revision: rev, keys: ["unlocked"], values: [String(rev)] });
let failed = 0;

async function call(method, who, body) {
  const res = await fetch(`${base}/api/save/${id}`, {
    method, headers: { Authorization: `Bearer ${who}`, "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return { status: res.status, body: await res.json().catch(() => null) };
}

function expect(name, got, want) {
  const ok = got === want;
  if (!ok) failed++;
  console.log(`${ok ? "ok  " : "FAIL"} ${name}: ${got}${ok ? "" : ` (wanted ${want})`}`);
}

expect("nothing saved yet", (await call("GET", secret)).status, 404);
expect("first save claims the id", (await call("PUT", secret, { revision: 1, updatedAt: Date.now(), data: doc(1) })).status, 200);
expect("read it back", (await call("GET", secret)).body?.revision, 1);
expect("another secret can't read it", (await call("GET", other)).status, 403);
expect("another secret can't write it", (await call("PUT", other, { revision: 9, updatedAt: 0, data: doc(9) })).status, 403);
expect("a newer save is stored", (await call("PUT", secret, { revision: 5, updatedAt: Date.now(), data: doc(5) })).status, 200);
const stale = await call("PUT", secret, { revision: 3, updatedAt: 0, data: doc(3) });
expect("an older save is refused", stale.status, 409);
expect("...and the newer copy comes back", stale.body?.revision, 5);
expect("an equal revision is refused", (await call("PUT", secret, { revision: 5, updatedAt: 0, data: doc(5) })).status, 409);
expect("malformed data is refused", (await call("PUT", secret, { revision: 6, updatedAt: 0, data: "{not json" })).status, 400);
expect("a missing secret is refused", (await fetch(`${base}/api/save/${id}`)).status, 403);
expect("a bad id is refused", (await fetch(`${base}/api/save/x`, { headers: { Authorization: `Bearer ${secret}` } })).status, 400);
expect("oversized saves are refused", (await call("PUT", secret, { revision: 7, updatedAt: 0, data: JSON.stringify({ k: "x".repeat(600000) }) })).status, 413);
expect("the game page is served", (await fetch(`${base}/`)).status, 200);
process.exit(failed ? 1 : 0);
