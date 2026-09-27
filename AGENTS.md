# ControlFS: notes for coding agents

ControlFS is a controller-first Windows file manager with a built-in extractor (WinUI 3, .NET 10, C#).
The product spec lives outside the repo; the rules that matter are summarized here and in `docs/`.

## What you can and cannot verify

- **The app runs only on Windows.** `src/ControlFS.App` targets `net10.0-windows` with the Windows App SDK.
  It *compiles* elsewhere (PRI/manifest steps are skipped), but it can't run. The **CI** workflow builds and tests on Windows.
- **Tests run on Windows in CI** (`dotnet test tests/ControlFS.UnitTests`); ControlFS targets Windows only. They cover policies, input, the on-screen
  keyboard, the extractor on real temp files, and end-to-end journeys driven only by semantic actions. Run them before every push.
- **Windows-only behavior** (known folders, junctions, Mark of the Web, long paths) lives in `tests/ControlFS.WindowsIntegrationTests`.
- **Controllers, TVs, DPI** can't be tested in CI. Add manual checks to `docs/TESTING.md` instead of claiming they work.

## Layout

- `src/ControlFS.Core`: models, semantic actions, input normalization/router, on-screen keyboard, policies, contracts. No WinUI/SDL/SharpCompress.
- `src/ControlFS.Application`: `AppController` (presentation state), lists, panes, archive tree, modals, operation queue.
- `src/ControlFS.Infrastructure.*`: Windows filesystem/settings, archives (SharpCompress + `SafeExtractor`), SDL3 input.
- `src/ControlFS.App`: WinUI window and views, built in C# (see `docs/decisions/0003`).
- `build/Publish-ControlFS.ps1` and `.github/workflows/release.yml` publish a release when a `v*` tag is pushed on `main`. **Never create or push tags** unless the maintainer asks for a release.

## Branching: Gitflow

ControlFS follows [Gitflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow), with one
temporary rule:

> **Before 1.0 (current phase):** there is no `develop` branch. `feature/*` and `hotfix/*` pull requests go straight into
> `main`; releases are tagged on `main`. The full model below (with `develop` and `release/*`) starts with the first
> stable release.


- `main`: released history only. Every commit on `main` is a release and is tagged `vX.Y.Z[-pre]`.
- `develop`: integration branch for the next release.
- `feature/<issue>-<short-name>` (e.g. `feature/11-rename`): branch from `develop`, pull request back into `develop`. Features never touch `main`.
- `release/X.Y.Z[-pre]`: branch from `develop` when a release is ready; only fixes, docs and changelog entries. Pull request into `main`, tag the merge on `main` (this publishes the release), then merge `main` back into `develop`.
- `hotfix/X.Y.Z[-pre]`: branch from `main` for urgent fixes; pull request into `main`, tag, then merge back into `develop`.

CI runs only at the Gitflow integration points: pull requests (build + tests), tags on `main` (release, which builds the packages and launches the real app before publishing) and CodeQL on `main`/weekly. Pull requests don't build packages; the Smoke workflow is manual.

**Releases:** merge pull requests as they are ready; publish a release only when the maintainer asks. Versions stay `0.x.y-alpha.N` until the maintainer says to go to 1.0. Releases are published as regular (not pre-release) GitHub releases so the newest shows as "Latest". Tags that don't point to a commit on `main` fail the release workflow.

## Conventions

- Screens only see `InputAction`; never check vendor buttons in UI code.
- Security rules (names, containment, conflicts, limits) stay in Core/Infrastructure, never in views.
- Destructive tests only in temp dirs (`TempDir`). Never real user data.
- **Tests earn their place:** add one when it reproduces a real bug (before the fix), protects a security boundary
  (extraction containment, update signature, executables), covers a new format/feature path, or proves something only
  Windows shows. Don't add trivial variations (extra cases that hit the same branch).
- Don't mark anything as supported without a test; update `docs/archive-support.md` and `docs/controller-compatibility.md` with observed results.
- **Changelog:** user-visible changes go under `## [Unreleased]` in both `CHANGELOG.md` (Portuguese) and `CHANGELOG.en-US.md`.
- **Docs come in pairs:** `README.md` / `README.pt-BR.md`, `docs/GUIDE.md` / `docs/GUIDE.pt-BR.md`.
- **Commits and PR titles:** Conventional Commits in English (`fix: …`, `feat: …`, `docs: …`), one topic per PR.
- Never commit secrets, tokens, certificates, personal paths or user data. The update signing key exists only in the
  `UPDATE_SIGNING_KEY` Actions secret; the public key is in `src/ControlFS.Infrastructure.Updates/UpdateTrust.cs`.
  Changing it breaks automatic updates for every installed copy (see `docs/decisions/0005`).
