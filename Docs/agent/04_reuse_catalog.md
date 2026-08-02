# Reuse catalog

| Existing item | Location | Reuse for | Do not duplicate |
|---|---|---|---|
| Rekordbox track aggregate loader | `CueGen/Generator.cs` (`GetContents`) | Resolve contents with artist, cues, content cues, and My Tags | New ad hoc joins over the same tables |
| Rekordbox table models | `CueGen/Content.cs`, `Cue.cs`, `ContentCue.cs`, `MyTag.cs`, `SongMyTag.cs`, `CommonTable.cs` | Any database import repository | Parallel import-only table models |
| Cue JSON serialization | `CueGen/ContentCue.cs` | Refresh the `contentCue` aggregate after cue changes | A second date/JSON format |
| ANLZ loader/cache | `CueGen/Content.cs` (`GetAnlz`, `GetBeats`) | Beatgrid and phrase validation | Independent binary readers |
| ANLZ format model | `CueGen/Analysis/` | Read beats, phrases, and existing cue sections | Reimplementing FourCC parsing |
| Media tag reader | `CueGen/TagFile.cs` | Legacy Mixed in Key/Serato fallback | New ID3/FLAC parsing |
| Cue time utilities | `CueGen/Generator.cs` (`TimeToFrame`, beat/bar conversions, `SnapToBar`) | Convert validated import positions and optional snapping | Slightly different timing math |
| Cue color tables | `CueGen/ColorTable.cs` and `Generator.ColorTableIndexes` | Centralize verified Rekordbox cue/track color mappings | Scattered numeric color IDs |
| Phrase defaults | `CueGen/Generator.cs` (`DefaultPhraseNames`, `DefaultPhraseOrder`) | Legacy candidate generation and phrase labels | Duplicate phrase maps |
| Generated cue marker | `CueGen/Generator.cs` (`UUIDPrefix`) | Identify rows managed by RekordBot | Deleting all user cues by default |
| Database fixture pattern | `CueGen.Test/Tests.cs`, `CueGen.Test/test.db` | Import integration and rollback tests | Tests against a live `master.db` |
| Golden snapshot normalization | `CueGen.Test/Tests.cs` (`ReplaceJson`, `AssertContent`) | Stable assertions over IDs/dates | Brittle raw snapshots |

## Reuse with refactoring first

- My Tag creation in `Generator.cs` contains useful field conventions (`Seq`, UUID, local USN, timestamps), but Energy/Genre-specific methods should be replaced by a generic category/tag repository before adding Status, year/origin, and Situation.
- `Generator.CreateCue()` contains correct row-shape and time/frame logic, but currently chooses the next slot implicitly. Extract a method accepting an explicit slot and validated color.
- `StemSeparator.CopyCuesToStem()` and `CopyMyTagsToStem()` demonstrate relation copying, but they delete target relations first and should not be used for normal workflow imports.
- `BeatportClient` and `SoundchartsClient` are local experimental integrations. The upstream workflow already owns metadata resolution, so do not make them dependencies of the importer.

## Missing reusable abstractions

The repository has no import DTO, schema validator, taxonomy loader, playlist model, transaction-scoped repository, or idempotent track import service. These should be added once, then reused by the CLI and tests.
