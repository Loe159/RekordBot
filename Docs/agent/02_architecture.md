# Architecture

## Current runtime flow

```text
CLI arguments and environment
          |
          v
CueGen.Console/Program.cs
  - locate master.db
  - optionally back it up
          |
          v
CueGen/Generator.cs
  - load Content, Artist, Cue, ContentCue, SongMyTag
  - optionally call metadata/stem integrations
  - read media tags or Rekordbox ANLZ phrases
  - select, snap, color, and create cues
  - optionally update color and My Tags
          |
          v
Rekordbox SQLite tables and optional ANLZ/audio files
```

There is no web server, endpoint, dependency injection container, or background worker. The CLI constructs concrete dependencies directly.

## Persistence boundary

`Generator` builds a `SQLiteConnectionString` from `Config.DatabasePath` and opens read/write SQLCipher connections. `GetContents()` assembles an in-memory aggregate by joining table reads in application code. Mutations use `RunInTransaction` around individual operations, not one atomic transaction for a complete track import.

The CLI creates a timestamped database backup unless disabled. `README.md` requires Rekordbox to be shut down before execution.

## Cue flow

1. `Content.GetTag()` reads Serato/Mixed in Key markers when phrase mode is disabled.
2. `Content.GetAnlz()` reads `.DAT` or `.EXT` files when phrase mode is enabled.
3. `Generator.GetPhraseCuePoints()` groups Rekordbox phrases and maps their start beats to times.
4. Candidates are offset, snapped to the nearest bar, filtered by distance, and limited.
5. `Generator.CreateCue()` maps candidates to `djmdCue` rows.
6. `ContentCue.SetCues()` serializes the aggregate cue list to the `contentCue` JSON column.

Current slot assignment is sequential. It does not preserve requested A/C/E letters or validate role/name/color combinations.

## My Tag and color flow

- `CreateMyTagEnergy()` creates an `Energy` root with values 1-8.
- `CreateSongMyTagEnergy()` attaches one energy tag per content.
- `CreateMyTagGenre()` and `CreateSongMyTagGenre()` create and attach Beatport genre/subgenre tags.
- `CreateColorEnergy()` maps Mixed in Key energy 1-8 to `Content.ColorID`.

This model conflicts with the target workflow, where color is mood, `Rating` is energy 1-5, and My Tags are grouped under Status, Genre, year/origin, and Situation.

## ANLZ and stem flow

`Content` resolves analysis files relative to the selected database and falls back to the platform Rekordbox share directory. `Anlz` uses big-endian binary serialization. `StemSeparator` runs Demucs, writes stems beside the source track, copies tags, synchronizes selected ANLZ sections, then replaces cues and My Tags for existing stem rows.

This flow has filesystem and database side effects beyond the normal cue generator and must remain separate from AI import.

## External systems

- Rekordbox 6 database and share directory.
- Mixed in Key/Serato metadata in audio files.
- Beatport API and Soundcharts API in current local work.
- Demucs through a Python child process.
- AppVeyor, SonarCloud, Codecov, NuGet, and GitHub Releases in CI.

## Target architecture direction

The upstream workflow should own identity resolution, acquisition, audio analysis, curation, provenance, and progressive human validation. RekordBot should own deterministic validation of a versioned import document and the final transaction that maps it to Rekordbox. Details are in `Docs/agent/08_workflow_adaptation.md`.
