# Build, test, and run

All commands use the repository root as working directory unless stated otherwise.

## Prerequisites

- A .NET SDK capable of building `net6.0` and `netstandard2.1` projects.
- Rekordbox 6 for real use.
- Rekordbox must be shut down before the CLI touches `master.db` (`README.md`).
- Mixed in Key is needed only for its embedded cue/energy path; phrase cues can use Rekordbox analysis alone.
- Demucs/Python is required only for the optional stem feature, but no supported installation command is documented in this repository.

The audit machine uses .NET SDK 8.0.423 from `@DEPENDENCIES_ROOT/dotnet-sdk-8`. NuGet packages are isolated under `@DEPENDENCIES_ROOT/rekordbot-nuget`. Release build and all 26 NUnit tests pass. The build warns that `net6.0` is out of support and should be upgraded in a later phase.

## Restore

```powershell
dotnet restore
```

Source: `appveyor.yml`.

Purpose: restore NuGet packages for the solution. Use after cloning or changing package references.

Caveat: requires network/package-source access.

## Build

```powershell
dotnet build -c Release
```

Source: `appveyor.yml`.

Purpose: compile all projects in Release configuration.

## Test

```powershell
dotnet test /p:CollectCoverage=true CueGen.Test\CueGen.Test.csproj
```

Source: `appveyor.yml`.

Purpose: run NUnit tests and collect coverage.

Caveats:

- Tests copy database/audio/ANLZ fixtures into the output directory and generate JSON/database artifacts.
- `CueGen.Test/BeatportTests.cs` now verifies configuration and lazy authentication without network access.
- The tracked source references untracked C# files. Commit the reconciled source set before claiming clean-clone reproducibility.
- Do not run tests against a live Rekordbox database.

## Package library

```powershell
dotnet pack --include-symbols --include-source -c Release CueGen
```

Source: `appveyor.yml`.

Purpose: create CueGen NuGet and symbol packages under Release output folders.

## Publish console executable

```powershell
dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -c Release CueGen.Console
```

Source: `appveyor.yml`.

Purpose: produce the Windows self-contained CLI release.

The CI file also publishes `osx-x64` with the same flags. Packaging additionally requires `7z`.

## Run locally

`README.md` documents use of the published `CueGen.Console` executable and its options. A source-run command is not explicitly documented. The conventional command below is therefore uncertain until an SDK is installed:

```powershell
dotnet run --project CueGen.Console -- --help
```

For any real database operation, first use `--dryrun`, pass an explicit copied database with `--database`, and verify that the selected feature actually honors dry-run. Stem operations currently do not fully honor it.

## Configuration inputs

- CLI options are declared in `CueGen.Console/Program.cs`.
- Beatport username/password may be read from environment variables by `Program.cs`.
- A repository-root `.env` is loaded if present. Do not commit it.
- Soundcharts identifiers can be supplied as CLI values.
- The default database path is `%AppData%\Pioneer\rekordbox\master.db` on Windows or `$HOME/Library/Pioneer/rekordbox/master.db` on macOS.

## CI and generated outputs

- AppVeyor: `appveyor.yml`.
- Qodana: `qodana.yaml`.
- Coverage: `coverage*.xml`, ignored by `.gitignore`.
- Build output: `**/bin/`, `**/obj/`, `Release/`, package files, and publish folders.
