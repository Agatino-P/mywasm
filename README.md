# mywasm

A minimal standalone Blazor WebAssembly app set up to deploy to GitHub Pages via
GitHub Actions. The point of this repo is to learn Actions end-to-end — the app
itself is deliberately tiny (a Home page and a Counter to prove WASM is live).

## What's where

- `.github/workflows/deploy.yml` — the build + deploy pipeline (the thing to study).
- `wwwroot/index.html` — `<base href="/">`; the workflow rewrites it to `/mywasm/` at deploy time.
- `wwwroot/404.html` — SPA deep-link redirect (Pages has no server-side routing).
- `.gitattributes` — `*.js binary`, so Git doesn't mangle line endings and break integrity checks.
- `.nojekyll` — stops Pages stripping the `_framework` folder.

## Run locally (optional — only if you have the .NET 8 SDK)

You do NOT need .NET locally; CI builds it. But to preview before pushing:

    dotnet run

Then open the printed localhost URL. Locally the base href is `/`, so routing works as-is.

## Pushing to your PERSONAL GitHub (not the company account)

This folder ships WITHOUT a `.git` directory, so there's no remote or credential
baggage from wherever it was created. Initialize fresh and point it at your
personal account. Pick whichever auth path matches your setup:

### Option A — GitHub CLI with an explicit personal login

    cd ~/src/00_poc/mywasm
    git init -b main
    git add .
    git commit -m "Initial Blazor WASM + Pages workflow"

    # Log in as your PERSONAL account in a separate auth context:
    gh auth login                       # choose GitHub.com > your personal account
    gh repo create mywasm --public --source=. --remote=origin --push

### Option B — HTTPS remote with a personal access token (no shared credential helper)

    cd ~/src/00_poc/mywasm
    git init -b main
    git add . && git commit -m "Initial Blazor WASM + Pages workflow"

    # Create an empty repo named mywasm on your personal account first (web UI), then:
    git remote add origin https://github.com/<your-personal-user>/mywasm.git

    # Push using the token inline so it doesn't touch your global git credential store:
    git -c credential.helper= \
        -c http.https://github.com/.extraheader="AUTHORIZATION: basic $(printf '%s' '<personal-user>:<personal-PAT>' | base64)" \
        push -u origin main

### Option C — SSH with a dedicated personal key

If you have a personal SSH key (e.g. `~/.ssh/id_personal`), add a host alias in
`~/.ssh/config`:

    Host github-personal
        HostName github.com
        User git
        IdentityFile ~/.ssh/id_personal
        IdentitiesOnly yes

Then:

    git remote add origin git@github-personal:<your-personal-user>/mywasm.git
    git push -u origin main

> Tip: set a repo-local identity so commits aren't attributed to your work email:
>
>     git config user.name  "Your Name"
>     git config user.email "your-personal@email"

## Turn on Pages (one-time, in the repo settings)

After the first push:

1. Repo **Settings > Pages > Build and deployment > Source** = **GitHub Actions**.
2. The `deploy.yml` workflow runs on push to `main`; watch it in the **Actions** tab.
3. Site goes live at `https://<your-personal-user>.github.io/mywasm/`.

If you name the repo something other than `mywasm`, update `REPO_NAME` in
`deploy.yml` to match.
