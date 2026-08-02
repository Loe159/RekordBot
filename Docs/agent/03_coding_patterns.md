# Coding patterns

## Namespaces and layout

- Production types use the `CueGen` namespace; binary types use `CueGen.Analysis`.
- The library is mostly flat, with only the ANLZ parser in a dedicated folder.
- Public classes use PascalCase. Rekordbox column names retain database casing or snake_case, with naming warnings suppressed in `CommonTable.cs` and `Content.cs`.

Representative files: `CueGen/Generator.cs`, `CueGen/CommonTable.cs`, `CueGen/Analysis/Anlz.cs`.

## Configuration and CLI

- `Config` is a mutable property bag with inline defaults.
- `Program` maps each `Mono.Options` option directly to a `Config` property.
- Optional comma-separated values are parsed after option processing.

Representative files: `CueGen/Config.cs`, `CueGen.Console/Program.cs`.

For new options, preserve the option/config pairing but validate a complete configuration before opening the database.

## Database mapping

- SQLite models use `[Table]`, `[PrimaryKey]`, `[NotNull]`, and `[SQLite.Ignore]` attributes.
- Common Rekordbox synchronization fields live in `CommonTable`.
- Relationships are assembled by `Generator.GetContents()` rather than ORM navigation.
- Writes use `SQLiteConnection.RunInTransaction`, `Insert`, `Update`, `Delete`, or parameterized `Execute`.

Representative files: `CueGen/Content.cs`, `CueGen/Cue.cs`, `CueGen/MyTag.cs`, `CueGen/Generator.cs`.

Reuse these mappings. Introduce a repository/service boundary for new import operations instead of spreading more SQL through `Generator`.

## Serialization

- Newtonsoft.Json serializes `contentCue.Cues`, fixtures, and external API responses.
- BinarySerializer maps big-endian ANLZ sections through ordered fields and subtype attributes.
- Date serialization uses an explicit Rekordbox-compatible timestamp format in `ContentCue.SetCues()` and the SQLite connection.

Representative files: `CueGen/ContentCue.cs`, `CueGen/Analysis/AnlzSection.cs`, `CueGen/Generator.cs`.

## Logging and error handling

- Classes obtain an NLog logger with `LogManager.GetCurrentClassLogger()`.
- Per-track operations generally catch exceptions, log context, set an error flag, and continue.
- The CLI catches parsing errors separately and has a final fatal exception handler.
- Some helpers return `false` or `null` on failure rather than throwing.

Representative files: `CueGen.Console/Program.cs`, `CueGen/Generator.cs`, `CueGen/StemSeparator.cs`.

The return contract is inconsistent: `Generator.Generate()` returns the error flag, while `Program.Generate()` treats `false` as failure. New code must use an unambiguous result type or `true == success` consistently.

## Synchronous and asynchronous work

- Main generation is synchronous.
- Beatport forces asynchronous HTTP calls into blocking waits.
- Soundcharts exposes async methods, but the current metadata update code that would await them is commented out.
- Demucs is a blocking child process with redirected output.

Representative files: `CueGen/BeatportClient.cs`, `CueGen/SoundchartsClient.cs`, `CueGen/StemSeparator.cs`.

Do not mix network calls into a database transaction. The target import path should not need network access.

## Cue generation

- Candidate generation and persistence are separate stages inside `CreateCuesForContent()`.
- Candidates are snapped with the ANLZ beat grid and filtered by minimum bar distance.
- Phrase names/order have static defaults but can be overridden through config.
- Generated rows are recognized by a fixed UUID prefix for later replacement/removal.

Representative files: `CueGen/Generator.cs`, `CueGen/CuePoint.cs`, `CueGen/Analysis/PhraseEntry.cs`.

The target workflow should reuse snapping and row creation calculations, but replace sequential slot assignment with explicit validated slots.

## Test style

- NUnit `[SetUp]` copies a seed database to a per-test database.
- Tests mutate through public configuration and `Generator.Generate()`.
- Golden JSON snapshots normalize timestamps and generated IDs.
- Binary format tests assert specific ANLZ section/beat/phrase values.

Representative files: `CueGen.Test/Tests.cs`, `CueGen.Test/json/`, `CueGen.Test/share/`.

New import tests should use the same isolated database pattern and add explicit assertions for rollback, status exclusivity, A/C/E slots, rating, mood color, and idempotency.

## Observed inconsistencies

- French and English comments are mixed.
- Several source files required by tracked code are untracked.
- `BeatportTests.cs` targets a different client API.
- `DryRun` is checked in many database paths but not in all file/stem paths.
- Configuration contains sensitive defaults. Never copy them into new code, logs, fixtures, or documentation.
