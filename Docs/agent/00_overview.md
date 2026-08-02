# Repository overview

## Audit scope

This audit describes the working tree observed on 2026-08-02. It does not change production code.

RekordBot is a fork of CueGen. It is a .NET console application that reads Mixed in Key or Rekordbox phrase data, then writes cues and selected metadata directly to a Rekordbox 6 SQLite database. The local branch is 11 commits ahead of `origin/master` and the working tree contains staged media/configuration plus untracked source files. Future work must preserve those changes.

The target workflow is documented in the `rekordbox-ai-worflow` README. Its desired model is newer than its checked-in schemas, taxonomy, tests, and agent instructions. See `Docs/agent/08_workflow_adaptation.md` before implementing an importer.

## Technology inventory

| Area | Evidence |
|---|---|
| Language | C# in `CueGen/`, `CueGen.Console/`, and `CueGen.Test/` |
| Runtime | `netstandard2.1` library and `net6.0` console/tests in the three `.csproj` files |
| Build | Visual Studio solution `CueGen.sln`; `dotnet` commands in `appveyor.yml` |
| CLI | `CueGen.Console/Program.cs` with `Mono.Options` |
| Persistence | `sqlite-net-sqlcipher` models mapped to Rekordbox tables in `CueGen/*.cs` |
| Binary formats | `BinarySerializer` models for Rekordbox ANLZ files in `CueGen/Analysis/` |
| Media tags | `TagLibSharp` reader in `CueGen/TagFile.cs` |
| Logging | NLog in the CLI and services |
| Tests | NUnit and golden JSON/database fixtures in `CueGen.Test/` |
| CI | AppVeyor build, test, package, publish, and release flow in `appveyor.yml` |

## Projects and entry points

- `CueGen/`: reusable library, database models, generator, ANLZ parser, metadata clients, and stem integration.
- `CueGen.Console/`: executable entry point in `Program.cs`.
- `CueGen.Test/`: NUnit tests with real-looking SQLite, audio, and ANLZ fixtures.
- `Docs/`: ANLZ format notes plus this audit.
- `MusicTests/`: local audio samples. These are staged or untracked and are not production source.

## Generated and local-only paths

- `**/bin/` and `**/obj/` are generated build outputs and are ignored.
- `.idea/`, `*.sln.DotSettings.user`, test result databases, and logs are local artifacts.
- `CueGen.Test/bin/` and `CueGen.Test/obj/` contain copied fixtures and generated test databases.
- `MusicTests/`, `.output.txt`, `identifier.sqlite`, and `nul` are not runtime modules.

## Current readiness

- The source has no JSON import boundary for the AI workflow.
- The current generator can read Rekordbox content, tags, beats, and phrases, and can write cues, track color, and My Tags.
- It has no status state machine, mood model, 1-5 rating writer, playlist writer, or READY gate.
- .NET SDK 8.0.423 is installed in `@DEPENDENCIES_ROOT/dotnet-sdk-8`; Release build succeeds.
- All 26 NUnit tests pass after reconciling the offline Beatport tests and phrase snapshots.
- Source files needed by tracked code still need to be committed before a clean clone is reproducible.
