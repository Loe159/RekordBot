# RekordBot agent instructions

RekordBot is a .NET CLI that reads audio/Rekordbox analysis and writes cues and metadata directly to a Rekordbox 6 database.

## Before coding

1. Read `Docs/agent/00_overview.md` and `Docs/agent/01_modules.md`.
2. Reuse the catalog in `Docs/agent/04_reuse_catalog.md` before creating a model, parser, mapping, or utility.
3. Read conventions in `Docs/agent/03_coding_patterns.md`.
4. Read `Docs/agent/07_danger_zones.md` before any database, ANLZ, audio, credential, or cue change.
5. For the AI workflow, follow `Docs/agent/08_workflow_adaptation.md`; do not implement against the incompatible schema 1.0.

## Mandatory rules

- Preserve the dirty working tree and all unrelated user changes.
- Never read, print, copy, or commit secret values. Do not inspect `.env`.
- Never test against a live Rekordbox database. Use a copied fixture.
- Keep Rekordbox closed for real database operations and retain a verified backup.
- Make every new mutation honor dry-run and use a clear transaction boundary.
- Keep network and stem processing opt-in and outside the AI import path.
- Add tests for idempotency, rollback, and preservation of user data.

## Commands

- Restore: `dotnet restore`
- Build: `dotnet build -c Release`
- Test: `dotnet test /p:CollectCoverage=true CueGen.Test\CueGen.Test.csproj`

The isolated SDK is at `@DEPENDENCIES_ROOT/dotnet-sdk-8`. See `Docs/agent/06_build_test_run.md` for environment variables and caveats.
