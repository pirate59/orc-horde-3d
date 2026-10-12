# Building and deploying

Orc Horde FPS is a Unity project that ships two ways:

- **In the browser.** A WebGL 2 build, served by a Cloudflare Worker (`web/worker`) that also keeps players' cloud saves in a D1 database. This is what Cloudflare deploys from this repo.
- **On the desktop.** A macOS player, used for development and the automated smoke tests.

## How a deploy works

Cloudflare's build machines **can't run Unity**: it needs a licensed editor and several minutes of full-editor build time. So the work is split:

| Step | Where | What |
|---|---|---|
| 1. Build the game | Your machine, with Unity | `ProjectSetup.BuildWeb` writes the browser build into **`web/worker/public/`** |
| 2. Commit it | Your machine | `web/worker/public/` is **checked in**: it is what gets deployed |
| 3. Push | GitHub | Cloudflare Workers Builds sees the push |
| 4. Deploy | Cloudflare | Installs wrangler, applies database migrations, uploads `public/` and the Worker |

**If you change anything the game is built from** (C#, shaders, textures, sounds, maps, the page template in `Assets/WebGLTemplates`), rebuild and commit `web/worker/public/` in the same push. If you don't, Cloudflare deploys the previous build. Changes only to `web/worker/src` (the save API) need no Unity build.

## Requirements

- **Unity 6000.6.4f1** with the **WebGL Build Support** module (and **Mac Build Support** for the desktop player). Install both through Unity Hub.
- **Node 20+** for the Worker and wrangler.
- A **Cloudflare account**, for deploying.

No Unity packages beyond the built-in modules; everything else is built at runtime from code.

## Build the browser version

```bash
Unity -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildWeb -logFile Logs/buildweb.log
```

`Unity` is the editor binary, on macOS for example:

```bash
"/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildWeb -logFile Logs/buildweb.log
```

It takes one to two minutes and writes `web/worker/public/`: `index.html` and `Build/public.{loader.js, framework.js.unityweb, wasm.unityweb, data.unityweb}`, about 14 MB in all. `grep "Web build" Logs/buildweb.log` reports the result. From the editor, the same build is `ProjectSetup.BuildWeb` (it isn't on a menu).

How the build is set up (all in `Assets/Editor/ProjectSetup.cs`):
- **Gzip with Unity's decompression fallback.** The loader unpacks the files itself, so they need no `Content-Encoding` header. Workers static assets doesn't set one for precompressed files.
- **Every file well under Workers' 25 MiB per-file limit** (the largest is about 8 MB).
- **WebGL 2 (OpenGL ES 3) only.** The page tells players on older browsers that they need it.

Then commit it:

```bash
git add web/worker/public && git commit -m "Rebuild the browser version"
```

`web/deploy.sh` does the build, the migrations and a deploy from your machine in one go. That's useful for deploying without going through GitHub; for the GitHub flow, build and commit as above.

## Cloudflare Workers Builds settings

In the Cloudflare dashboard, open the Worker, then **Settings → Builds**:

| Setting | Value |
|---|---|
| Git repository | this repo |
| Production branch | `main` |
| **Root directory** | `web/worker` |
| Build command | `npm ci` |
| **Deploy command** | `npx wrangler d1 migrations apply orc-horde-saves --remote && npx wrangler deploy` |
| **Non-production branch deploy command** | `npx wrangler versions upload` |
| Build watch paths (optional) | `web/worker/*`. Only pushes that touch the Worker or the committed build then deploy |

Notes:
- **The Worker's name must match.** `wrangler.jsonc` names it `orc-horde-fps`. If the Worker in the dashboard has another name, change `name` in `wrangler.jsonc` to match, or Cloudflare will refuse the deploy.
- **Branches deploy as preview versions, without migrations.** Previews use the same D1 database as production (D1 bindings aren't per-branch), so a branch must never run migrations: only `main` does. Preview versions get their own URL and never take production traffic.
- **No secrets or environment variables are needed.** The Worker's only binding is the D1 database, declared in `wrangler.jsonc`.

## One-time setup: the save database

Before the first deploy, create the D1 database and record its id. On your machine:

```bash
cd web/worker && npm install && npx wrangler login
```

```bash
cd web/worker && npx wrangler d1 create orc-horde-saves
```

Copy the `database_id` it prints into `web/worker/wrangler.jsonc`, replacing `00000000-0000-0000-0000-000000000000`, and commit it. The id isn't a secret: it only works with your account's credentials.

**Until this is done, Cloudflare deploys from this repo will fail.** They fail cleanly: nothing is changed.

Migrations live in `web/worker/migrations/` and are applied in order by the deploy command. To change the schema, add a new numbered file; never edit one that has been applied.

## Run the browser version locally

No Cloudflare account needed. The Worker runs with a local copy of D1:

```bash
cd web/worker && npm install && npm run db:local && npm run dev
```

Open http://localhost:8787. That's the committed build in `public/`, with working cloud saves against the local database. With it running, `npm test` (in `web/worker`) checks the save API.

## Build the desktop player (macOS)

```bash
Unity -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildMac -logFile Logs/build.log
```

This writes `Builds/OrcHordeFPS.app`, a development build that isn't committed. Saves stay on the machine; add `-cloud http://localhost:8787` (or your deployed URL) to sync them with a Worker. The smoke tests in the README run against this build.

## Saves, briefly

A campaign is one JSON document with a revision number. It's kept in the browser's IndexedDB (via PlayerPrefs) and copied to D1 by the Worker under an anonymous player id and secret. A newer revision always wins, and a save code moves a campaign between devices. The README's "Browser build and Cloudflare" section has the details and the API.

## Troubleshooting

- **The page loads but shows the old game.** `web/worker/public/` wasn't rebuilt and committed with the change. Rebuild, commit, push.
- **Deploy fails on the D1 binding.** The `database_id` in `wrangler.jsonc` is still the placeholder, or belongs to a different account.
- **Deploy fails with "Worker name mismatch".** Make `name` in `wrangler.jsonc` match the Worker in the dashboard.
- **"This browser can't run the game".** The browser has no WebGL 2, or it's turned off; current Chrome, Edge, Firefox and Safari all have it.
- **The page is blank after a deploy.** Check the browser console. A 404 on `Build/public.*` means `public/` wasn't committed.
- **The repository grows with every rebuild.** Each rebuild of `web/worker/public` adds about 14 MB of history. If that becomes a problem, the alternative is to build in a GitHub Action with a Unity license (for example GameCI) and deploy from there instead of committing the build.
