# ControlFS: notes for coding agents

ControlFS is a controller-first Windows file manager with a built-in extractor (WinUI 3, .NET 10, C#).
The product spec lives outside the repo; the rules that matter are summarized here and in `docs/`.

## What you can and cannot verify

- **The app runs only on Windows.** `src/ControlFS.App` targets `net10.0-windows` with the Windows App SDK.
  It *compiles* elsewhere (PRI/manifest steps are skipped), but it can't run. The **CI** workflow builds and tests on Windows.
- **The unit tests run anywhere:** `dotnet test tests/ControlFS.UnitTests`. They cover policies, input, the on-screen
  keyboard, the extractor on real temp files, and end-to-end journeys driven only by semantic actions. Run them before every push.
- **Windows-only behavior** (known folders, junctions, Mark of the Web, long paths) lives in `tests/ControlFS.WindowsIntegrationTests`.
- **Controllers, TVs, DPI** can't be tested in CI. Add manual checks to `docs/TESTING.md` instead of claiming they work.

## Layout

- `src/ControlFS.Core`: models, semantic actions, input normalization/router, on-screen keyboard, policies, contracts. No WinUI/SDL/SharpCompress.
- `src/ControlFS.Application`: `AppController` (presentation state), lists, panes, archive tree, modals, operation queue.
- `src/ControlFS.Infrastructure.*`: Windows filesystem/settings, archives (SharpCompress + `SafeExtractor`), SDL3 input.
- `src/ControlFS.App`: WinUI window and views, built in C# (see `docs/decisions/0003`).
- `build/Publish-ControlFS.ps1` and `.github/workflows/release.yml` publish a release when a `v*` tag is pushed. **Never create or push tags.**

## Conventions

- Screens only see `InputAction`; never check vendor buttons in UI code.
- Security rules (names, containment, conflicts, limits) stay in Core/Infrastructure, never in views.
- Destructive tests only in temp dirs (`TempDir`). Never real user data.
- Don't mark anything as supported without a test; update `docs/archive-support.md` and `docs/controller-compatibility.md` with observed results.
- **Changelog:** user-visible changes go under `## [Unreleased]` in both `CHANGELOG.md` (Portuguese) and `CHANGELOG.en-US.md`.
- **Docs come in pairs:** `README.md` / `README.pt-BR.md`, `docs/GUIDE.md` / `docs/GUIDE.pt-BR.md`.
- **Commits and PR titles:** Conventional Commits in English (`fix: …`, `feat: …`, `docs: …`), one topic per PR.
- Never commit secrets, tokens, certificates, personal paths or user data. The update signing key exists only in the
  `UPDATE_SIGNING_KEY` Actions secret; the public key is in `src/ControlFS.Infrastructure.Updates/UpdateTrust.cs`.
  Changing it breaks automatic updates for every installed copy (see `docs/decisions/0005`).
