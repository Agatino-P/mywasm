# Blazor WASM → GitHub Pages via GitHub Actions — Recap

A start-to-finish reference for deploying a standalone Blazor WebAssembly app to
GitHub Pages using a GitHub Actions pipeline. The app is deliberately tiny; the
real subject is the Actions workflow and the Pages-specific plumbing.

**End result:** push to `main` → Actions builds and publishes → GitHub Pages
serves the static output at `https://<user>.github.io/<repo>/`.

---

## 0. Mental model (the one idea to keep)

GitHub Pages is **static file hosting with no server you control**. Almost every
quirk below follows from that single fact:

- You can't run nginx, a .NET API, or any server-side process there.
- Deep-link refreshes 404 (no server-side routing) → handled with a `404.html` trick.
- The app's base path is an **environment concern**, not a source constant — it
  differs between local (`/`) and Pages (`/<repo>/`), so it's injected at deploy time.

Blazor WebAssembly fits because `dotnet publish` emits pure static files
(`wwwroot/` of html/js/wasm/dll) — no runtime needed on the host.

---

## 1. Scaffold the project

```bash
mkdir -p ~/src/00_poc && cd ~/src/00_poc
dotnet new blazorwasm -o mywasm        # standalone Blazor WebAssembly template
cd mywasm
dotnet run                             # optional: preview locally at the printed URL
```

The template ships with sample pages (Counter, Weather) and Bootstrap. For a
learning sandbox it's fine to trim it down to a `Home` page plus a `Counter`
component — enough to prove the WASM runtime actually booted in the browser.

Project shape (minimal version):

```
mywasm/
├── mywasm.csproj
├── Program.cs
├── App.razor
├── _Imports.razor
├── Layout/
│   └── MainLayout.razor
├── Pages/
│   ├── Home.razor
│   └── Counter.razor
└── wwwroot/
    ├── index.html
    ├── 404.html
    └── css/app.css
```

---

## 2. Pages-specific files (the plumbing that makes Pages work)

These four additions are what separate "runs locally" from "works on Pages."
Each one maps directly to the no-server mental model.

> **What changes vs. a stock `dotnet new blazorwasm`** — everything in this
> section plus the workflow is *net-new*; a fresh template has none of it:
>
> | Item | Stock template | For GitHub Pages |
> |---|---|---|
> | `.github/workflows/deploy.yml` | absent | **add** — build + deploy pipeline |
> | `.nojekyll` | absent | **add** — keep `_framework/` |
> | `.gitattributes` (`*.js binary`) | absent | **add** — avoid integrity-check breakage |
> | `wwwroot/404.html` + restore script in `index.html` | absent | **add** — SPA deep-link routing |
> | `<base href="/">` | present, left as-is | **rewritten at deploy** to `/<repo>/` |
> | `.gitignore` | present | unchanged (ignores `bin/`, `obj/`) |
>
> The C# / Razor app code itself needs **no changes** to run on Pages — the work
> is entirely in deployment plumbing.

### `wwwroot/index.html` — leave `<base href="/">` alone

```html
<base href="/" />
```

Keep it `/` in source. The browser resolves every relative asset
(`_framework/blazor.webassembly.js`, etc.) against this. On Pages the app lives
under `/<repo>/`, so the deploy step rewrites this tag to `/<repo>/`. **Do not
hardcode the repo path in source** — that's the environment-leaks-into-code smell.

### `.nojekyll` (empty file)

By default Pages can run output through Jekyll, which **ignores folders starting
with `_`** — including Blazor's `_framework/`. An empty `.nojekyll` disables that.
With the modern Actions deploy path Jekyll doesn't actually run, but include it
(or `touch` it into the published output) so the build is safe either way.

### `.gitattributes`

```
*.js binary
```

Git converts JS line endings (CRLF→LF) by default. That changes file hashes,
which breaks Blazor's **client-side integrity checks**, so the app silently fails
to boot. Marking JS as binary stops Git touching it. Must exist **before** the
first commit of the JS assets.

### `wwwroot/404.html` — SPA deep-link redirect

Pages has no server routing, so refreshing `/<repo>/counter` returns 404. The
`404.html` stashes the requested URL in `sessionStorage` and bounces to the app
root; a small script in `index.html` restores the URL before Blazor routes.
(Pattern: `rafrex/spa-github-pages`.)

---

## 3. The GitHub Actions workflow

`.github/workflows/deploy.yml`:

```yaml
name: Deploy Blazor WASM to GitHub Pages

on:
  push:
    branches: [main]
  workflow_dispatch:        # manual trigger from the Actions tab

permissions:                # what GITHUB_TOKEN needs for a Pages deploy
  contents: read
  pages: write
  id-token: write

concurrency:                # one deploy at a time; don't cancel an in-flight one
  group: pages
  cancel-in-progress: false

env:
  REPO_NAME: mywasm         # must match the repo name for the base href

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publish
        run: dotnet publish mywasm.csproj -c Release -o release

      - name: Add .nojekyll
        run: touch release/wwwroot/.nojekyll

      - name: Rewrite base href            # "/" -> "/<repo>/" in the PUBLISHED html
        uses: SteveSandersonMS/ghaction-rewrite-base-href@v1
        with:
          html_path: release/wwwroot/index.html
          base_href: /${{ env.REPO_NAME }}/

      - uses: actions/upload-pages-artifact@v3
        with:
          path: release/wwwroot

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

### How to read it

- **Two jobs.** `build` produces a Pages artifact; `deploy` (which `needs: build`)
  publishes it. Splitting them is the standard modern Pages pattern.
- **`permissions`.** The deploy uses OIDC (`id-token: write`) plus `pages: write`.
  Without these the deploy step can't authenticate to Pages.
- **No `gh-pages` branch.** `upload-pages-artifact` + `deploy-pages` ship an
  artifact directly. Older tutorials that push to a `gh-pages` branch are outdated.
- **The base-href rewrite touches build output only**, never your source — so
  local (`/`) and Pages (`/mywasm/`) stay correct from one codebase. This
  inject-env-at-deploy pattern is the documented Microsoft approach.

---

## 4. Push to a *personal* GitHub account (without disturbing a work login)

Goal: push from a machine logged into a *company* GitHub, to a *personal* repo,
without the company credentials leaking in and without saving a token to the
macOS keychain.

```bash
# Commit with a repo-local identity (overrides the global work identity here only)
cd ~/src/00_poc/mywasm
git init -b main
git config user.name  "Your Name"
git config user.email "your-personal@email.com"
git add .
git commit -m "Initial Blazor WASM + Pages workflow"
git log --format='%an <%ae>' -1        # verify attribution before pushing

# Remote on the personal account
git remote add origin https://github.com/<personal-user>/<repo>.git

# Push with the keychain helper DISABLED for this one command
git -c credential.helper= push -u origin main
#   Username: <personal-user>
#   Password: <the PAT>   (input hidden; nothing is stored)
```

**Why `-c credential.helper=`:** an empty value resets Git's credential helper
list for just this invocation, so the macOS keychain helper can't save the token.
Git prompts interactively, authenticates once, and forgets it — no token in
`.git/config`, the keychain, or shell history.

### The PAT (fine-grained, scoped to one repo)

GitHub → Settings → Developer settings → **Fine-grained tokens**:

- **Repository access:** Only select repositories → the one repo.
- **Permissions:**
  - **Contents:** Read and write — needed to push.
  - **Workflows:** Read and write — **required because the repo contains
    `.github/workflows/`.** GitHub rejects pushes that add/modify workflow files
    if the token lacks this (classic-token equivalent: the `workflow` scope).
- Short expiration is fine for an experiment.

---

## 5. Enable Pages and run

1. Push to `main` — this triggers the workflow automatically.
2. Repo → **Settings → Pages → Build and deployment → Source → "GitHub Actions".**
   This must be set or the deploy job fails (see gotchas).
3. Watch the **Actions** tab. When green, the site is live at
   `https://<user>.github.io/<repo>/`.
4. **Verify:** load the page and click the counter button. A working button
   proves the WASM runtime booted *and* the base-href rewrite resolved assets
   correctly under the subpath.

---

## 6. Gotchas actually hit (and what they mean)

| Symptom | Cause | Fix |
|---|---|---|
| `Failed to create deployment (status: 404) ... Ensure GitHub Pages has been enabled` | Pages source not set to "GitHub Actions" before the deploy job ran | Set Settings → Pages → Source = GitHub Actions, then Re-run all jobs |
| Push rejected mentioning `workflow` scope | PAT can't modify workflow files | Regenerate token with Workflows: Read/Write (fine-grained) |
| Blank page, 404s on `_framework/*` | base href still `/`, or Jekyll stripped `_framework` | Confirm the rewrite step ran; ensure `.nojekyll` is in the output |
| Deep-link refresh 404s | No server-side routing on Pages | `404.html` + restore script in `index.html` |
| `Node.js 20 actions are deprecated` warning | Pinned actions still bundle Node 20 | Non-blocking for now; bump action versions when newer ones ship (see next steps) |
| `This repository moved` notice on push | Remote URL casing differs from canonical account casing | `git remote set-url origin https://github.com/<CanonicalUser>/<repo>.git` |

Two naming facts worth memorizing:
- The Pages URL **lowercases the username** regardless of profile casing:
  `https://<user-lowercased>.github.io/<repo>/`.
- `REPO_NAME` in the workflow **must equal the repo name** so the base href matches.

---

## 7. Next steps / hardening

- **Pin the third-party action to a commit SHA** instead of `@v1`
  (`SteveSandersonMS/ghaction-rewrite-base-href`) — supply-chain hygiene for any
  action you don't own.
- **Bump action versions** as Node-24-compatible releases land (the deprecation
  warning calls out `checkout`, `setup-dotnet`, `upload-pages-artifact`,
  `deploy-pages`, and the base-href action).
- **Custom domain** (optional): a `CNAME` makes the app serve from a domain root,
  so `<base href="/">` is correct and the rewrite step becomes unnecessary.
- **A user/org site** (`<user>.github.io` repo) also serves from root — handy to
  know exists, but it's one-per-account, so it's the wrong default for multiple
  experiments.

---

## Appendix — transferable lessons

- "Works locally" vs "works deployed" often differs by **one environment value**
  (here, the base path). Inject it at deploy time; don't bake it into source.
- A green `build` with a red `deploy` usually means the artifact was fine and the
  problem is **permissions or a not-yet-enabled target** — read the deploy error,
  not the build.
- Scope credentials as narrowly as the task allows (one repo, minimum
  permissions, short expiry) and keep them out of persistent stores when a
  one-shot will do.
