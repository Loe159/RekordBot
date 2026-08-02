# Module map

## `CueGen.Console`

Purpose: parse command-line options, configure logging, locate and back up `master.db`, and invoke the generator.

- Main files: `CueGen.Console/Program.cs`, `CueGen.Console/ProgressBar.cs`.
- Dependencies: `CueGen`, `Mono.Options`, NLog.
- External interactions: environment variables, optional `.env`, filesystem backup, terminal output.
- Public boundary: executable arguments documented in `README.md`.
- Add similar CLI options in `Program.cs` and matching state in `CueGen/Config.cs`.

## Generator and configuration

Purpose: orchestrate all tracks and apply database/file operations.

- Main files: `CueGen/Generator.cs`, `CueGen/Config.cs`.
- Dependencies: database models, `TagFile`, ANLZ models, external clients, and `StemSeparator`.
- Public API: `Generator.Generate()`, read methods such as `GetContents()`, and `Progress<Status>`.
- Database interactions: reads and writes Rekordbox tables through `SQLiteConnection`.
- Extension point: add a validated import service beside `Generator`; do not expand the existing monolithic loop with unvalidated JSON handling.

## Rekordbox database model

Purpose: map Rekordbox SQLite rows to C# objects.

- Common fields: `CueGen/CommonTable.cs`.
- Track and relations: `Content.cs`, `Artist.cs`, `Genre.cs`, `Key.cs`.
- Cues: `Cue.cs`, `ContentCue.cs`, `CuePoint.cs`.
- My Tags: `MyTag.cs`, `SongMyTag.cs`.
- Tables used: `djmdContent`, `djmdArtist`, `djmdGenre`, `djmdKey`, `djmdCue`, `contentCue`, `djmdMyTag`, and `djmdSongMyTag`.
- Extension point: add repository methods around these existing mappings. Do not create duplicate table DTOs.

## Media tag reader

Purpose: read Mixed in Key and Serato data embedded in MP3/FLAC files.

- Main files: `TagFile.cs`, `MIKBase.cs`, `CuePointsAttachment.cs`, `EnergyAttachment.cs`, `KeyAttachment.cs`, `SeratoMarkers.cs`, `SeratoCue.cs`.
- External interaction: opens each audio file with TagLibSharp.
- Public API: `new TagFile(path)` exposes energy and candidate cue data.
- Reuse when legacy Mixed in Key input remains supported. It is not a substitute for the new workflow JSON contract.

## Rekordbox ANLZ parser

Purpose: deserialize and serialize beat grids, phrases, cues, paths, VBR data, and waveforms.

- Main files: `CueGen/Analysis/Anlz.cs`, `AnlzSection.cs`, `AnlzMagic.cs`.
- Beat/phrase files: `BeatGridSection.cs`, `AnlzBeat.cs`, `PhraseSection.cs`, `PhraseEntry.cs`.
- Cue files: `CueSection.cs`, `CueExtendedSection.cs`, `AnlzCue.cs`, `AnlzExtendedCue.cs`.
- Other sections: `PathSection.cs`, `VbrSection.cs`, `WaveformSections.cs`, `UnknownSection.cs`.
- Filesystem interaction: reads and can overwrite `.DAT` and `.EXT` files under the Rekordbox `share` directory through `Content.cs` and `StemSeparator.cs`.
- Extension point: reuse beat and phrase readers for validation; isolate all writes behind an explicit opt-in service.

## Stem separation

Purpose: run Demucs, copy audio metadata, synchronize analysis data, and copy Rekordbox rows to stems.

- Main file: `CueGen/StemSeparator.cs`.
- Dependencies: external Python/Demucs process, TagLibSharp, ANLZ parser, SQLite models.
- Side effects: creates/deletes/moves audio files, writes ANLZ files, deletes/recreates cue and My Tag rows.
- Status: experimental and high risk. It is not part of the target AI curation import path.

## Metadata clients

Purpose: query Beatport and Soundcharts.

- Files: `CueGen/BeatportClient.cs`, `SoundchartsClient.cs`, `SoundchartsModels.cs`, `Genre.cs`.
- External interactions: synchronous Beatport HTTP authentication/search and asynchronous Soundcharts requests.
- Status: these files are untracked at audit time while tracked generator code references them. They must be committed or removed before the repository can be reproduced.
- Target boundary: recording resolution belongs to the upstream workflow; RekordBot should consume resolved data rather than perform mandatory lookups.

## Tests and fixtures

Purpose: verify database reads/writes, cue generation, ANLZ parsing, filters, loops, dry-run behavior, and Energy My Tags.

- Main test: `CueGen.Test/Tests.cs`.
- Untracked stale test: `CueGen.Test/BeatportTests.cs` targets an API not present in the current `BeatportClient`.
- Fixtures: `CueGen.Test/test.db`, `content/`, `share/`, and `json/`.
- Pattern: copy the seed database per test, run the generator, normalize volatile IDs/dates, and compare golden JSON.
