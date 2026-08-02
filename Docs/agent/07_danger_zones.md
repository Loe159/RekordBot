# Danger zones

## Credentials and tracked local configuration

Paths: `CueGen/Config.cs`, `CueGen/Generator.cs`, `.env`, `appveyor.yml`, and `CueGen/key.snk`.

Why sensitive: source defaults include plaintext service credentials, `.env` is tracked/staged, CI contains encrypted tokens, and the signing key may be sensitive.

What can break: account compromise, secret leakage in commits/logs/packages, and unauthorized API use.

Before editing: do not print or copy values. Rotate exposed credentials, remove plaintext defaults, stop tracking `.env`, add a deny rule, and use environment/secret-store injection. Confirm whether `key.snk` is intended to be public.

Validation: secret scan the Git history and working tree with approved tooling; verify the app starts with missing optional credentials and that logs contain no secret values.

## Live Rekordbox database

Paths: `CueGen/Generator.cs`, database models under `CueGen/`, and `CueGen.Console/Program.cs`.

Why sensitive: the application opens SQLCipher `master.db` read/write and directly changes cue, content, color, and My Tag tables.

What can break: library corruption, lost cues/tags, inconsistent local USNs, Rekordbox sync problems, or concurrent writes while Rekordbox is open.

Before editing: use a copied fixture database, verify Rekordbox is closed, retain automatic backup, and define transaction/rollback scope.

Validation: run unit/integration tests against a copy, compare row counts and foreign keys, reopen the copy in an isolated Rekordbox test environment, and verify idempotency.

## ANLZ binary files

Paths: `CueGen/Analysis/`, `CueGen/Content.cs`, `CueGen/StemSeparator.cs`, `Docs/Anlz.md`.

Why sensitive: formats are partially reverse engineered, big-endian, and contain unknown sections/bytes.

What can break: beatgrids, waveforms, phrases, cues, and player compatibility.

Before editing: preserve unknown data, lengths, order, and endianness. Do not overwrite the only copy of a `.DAT` or `.EXT` file.

Validation: deserialize/serialize round trips on fixtures, byte-level comparison where expected, existing `BeatGridTest`/`PhraseTest`, and testing in an isolated Rekordbox library.

## Stem processing

Path: `CueGen/StemSeparator.cs`.

Why sensitive: invokes an external process; deletes/replaces stem audio; overwrites analysis files; deletes/recreates cues, content-cue rows, and My Tags.

What can break: media files, stem analysis, metadata, cue ownership, and database consistency. The configured output directory is created, but stems are written beside source audio. FLAC stem path detection also compares an extension without its dot.

Before editing: isolate it from normal import, add explicit dry-run/preflight, use recoverable file replacement, validate resolved paths, and back up both database and analysis files.

Validation: dedicated temporary-directory tests for MP3 and FLAC, failure injection around Demucs/file moves/transactions, and rollback verification.

## Dry-run and exit status

Paths: `CueGen/Generator.cs`, `CueGen/StemSeparator.cs`, `CueGen.Console/Program.cs`.

Why sensitive: dry-run guards many database calls but does not prevent all stem/file writes. `Generator.Generate()` returns an error flag, while the caller interprets a false return as failure.

What can break: a supposed dry run can mutate files/data, and automation can receive an inverted success code.

Before editing: define a single result contract and central mutation boundary.

Validation: snapshot filesystem/database before and after dry-run; assert exit 0 on success and non-zero on failure.

## Forced Beatport access

Paths: `CueGen/Generator.cs`, `CueGen/BeatportClient.cs`.

Why sensitive: the track loop enables Beatport regardless of configuration and constructs the client with embedded credentials. Network/auth/search failures can block ordinary cue generation.

What can break: offline execution, rate limits, privacy, deterministic tests, and all processing when authentication fails.

Before editing: remove the forced condition and move metadata resolution upstream or behind explicit configuration.

Validation: full generation must work offline with metadata updates disabled; mocked tests cover success, no result, ambiguity, and API failure.

## Generated cue ownership and slot assignment

Path: `CueGen/Generator.cs`.

Why sensitive: generated cues are deleted by UUID prefix, while overwrite mode can delete all cues. Hot slots are assigned sequentially and skip one numeric kind without modeling A-H roles.

What can break: user-created cues or the workflow-required A/C/E mapping.

Before editing: distinguish managed/user cues, accept explicit slots, reject collisions, and make overwrite policy explicit.

Validation: preserve unrelated cues; upsert A/C/E idempotently; reject duplicate/out-of-range slots; verify names and colors.

## Working tree and reproducibility

Paths: repository root, untracked C# files, staged `.env`, `MusicTests/`, and IDE/build artifacts.

Why sensitive: local source is required by tracked code but not committed, and unrelated user work is present.

What can break: clean-clone compilation and accidental loss of current work.

Before editing: inspect `git status --short`, never reset/clean, decide which source belongs in version control, and keep secret/media changes out of commits.

Validation: build and test a clean clone after the source set is reconciled.
