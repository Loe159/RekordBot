# Workflow import 2.0 - phases 2 to 4

This import mode applies metadata and preparation status to one existing Rekordbox
content row, and can synchronize explicit Hot Cues. It does not run Beatport,
Soundcharts, Demucs, legacy cue generation, or ANLZ writes.

Use a dry run against a copied database first:

```powershell
CueGen.Console.exe --import workflow.json --database copied-master.db --dryrun
```

To calculate the workflow Hot Cues and Memory Cues directly from Rekordbox phrase
and beatgrid analysis, use the explicit batch mode and an explicit track glob:

```powershell
CueGen.Console.exe --workflow-hotcues --glob "C:\Music\Set\*.mp3" --database copied-master.db --dryrun
```

The JSON report lists every selected track, proposed cue, rule evidence, warning,
status, and database diff. Review it, close Rekordbox, then repeat without
`--dryrun` to write. The command reads ANLZ files but never modifies them, and it
does not run network metadata lookup or stem separation.

Whenever an import or workflow Hot Cue run changes metadata, tags, cues, or
managed playlist membership, RekordBot atomically assigns the My Tag
`Status/À vérifier`. The dry-run diff reports this status before any write. An
identical second run produces an empty diff and does not rewrite the status. The
existing marker remains until a person reviews and replaces or removes it.

### Argument-free executable with environment variables

The packaged Windows executable can enable this mode without command-line
arguments. Define both required variables once:

```powershell
[Environment]::SetEnvironmentVariable(
  "REKORDBOT_DATABASE_PATH",
  "D:\PIONEER\Master\master.db",
  "User")
[Environment]::SetEnvironmentVariable(
  "REKORDBOT_FILE_GLOB",
  "D:/Musique2/v2/test-rekordbot/*.flac",
  "User")
```

Open a new terminal, close Rekordbox, and run:

```powershell
.\RekordBot.exe
```

When both path variables exist, RekordBot automatically selects the deterministic
workflow Hot Cue and Memory Cue mode. Command-line options still override
environment values.
Optional variables are:

- `REKORDBOT_DRY_RUN`: `true`, `false`, `1`, or `0`.
- `REKORDBOT_TAXONOMY_PATH`: optional workflow 2.0 taxonomy JSON path.

Set `REKORDBOT_DRY_RUN=true` for the first launch, review the report, then change
it to `false` for writing. Real writes still require Rekordbox to be closed and
always create a verified database backup.

Remove `--dryrun` only after reviewing the JSON result. The normal CLI safety
checks require Rekordbox to be closed and create a verified database backup before
opening a write transaction.

The runtime contract is documented by `workflow_import_v2.schema.json`. Allowed
statuses, mood/color mappings, genres, year/origin patterns, and situations come
from the embedded `workflow_taxonomy_v2.json`. Supply a reviewed replacement with
`--taxonomy path-to-taxonomy.json` when the library uses a different allowed tag
list.

Example:

```json
{
  "schema_version": "2.0",
  "track": {
    "path": "C:\\Music\\Artist - Track.mp3",
    "isrc": "FRABC2600001",
    "title": "Track",
    "artist": "Artist"
  },
  "status": "Hot Cues",
  "mood": {"color": "Red", "label": "\u00c9nergie"},
  "energy": 5,
  "my_tags": {
    "genres": ["House", "Techno"],
    "year_origin": ["2024", "90FR"],
    "situations": ["Main Floor", "Peak Time"]
  },
  "beatgrid_verified": false,
  "quantize_verified": false,
  "hot_cues": [],
  "desired_playlists": [
    "Preparation/Hot Cues",
    "Mood/\u00c9nergie",
    "Energy/5",
    "Genre/House",
    "Genre/Techno",
    "Ann\u00e9e/2024",
    "Ann\u00e9e/90FR",
    "Situation/Main Floor",
    "Situation/Peak Time"
  ]
}
```

The path must resolve to exactly one existing `djmdContent` row. Every supplied
identity verifier must match. The metadata import synchronizes only the `Status`,
`Genre`, `Annee` (stored as the Unicode Rekordbox label), and `Situation`
associations for that track. Other My Tag categories and all global tag
definitions are preserved.

`status` is the next preparation step. Progressive documents may omit
`beatgrid_verified`, `quantize_verified`, and `hot_cues`. A document with
`status: "Hot Cues"` may carry a partial cue set while the track is still being
prepared. `À vérifier` is an output-only RekordBot marker, not an accepted value
of the workflow 2.0 import contract. A request that only changes the workflow
step does not add the review marker.

Use `status: null` only to request READY. READY requires complete mood, energy,
grouped tags with at least one genre, `beatgrid_verified: true`,
`quantize_verified: true`, and canonical Hot Cues A, B, C, D, E, and H. A, D,
E, and H must have `phrase_start_verified: true`. B must have
`vocal_section_verified: true`; C is an exact 32-beat offset before D. If any
invariant fails, validation
stops before opening a transaction and the existing `Hot Cues` status remains.

## Canonical Hot Cues

| Slot | Name | Color | Rekordbox kind |
|---|---|---|---:|
| A | `INTRO` | Yellow | 1 |
| B | `VOCAL` | Pink | 2 |
| C | `DROP -32` | Green | 3 |
| D | `DROP 1` | Red | 5 |
| E | `BREAKDOWN` | Purple | 6 |
| F | `PEAK / DROP 2` | Purple | 7 |
| G | `VOCAL / HOOK` | Pink | 8 |
| H | `LOOP` | Orange | 9 |

The deterministic generator applies these rules:

- A is the first beat of the first phrase.
- B is the first beat of the first audible vocal section. RekordBot reads the
  detailed waveform of the analyzed `_vocal` stem, treats a beat as vocal when
  its mean height is at least 2.0, and requires four consecutive vocal beats.
  Shorter appearances are ignored.
- D is the first Chorus immediately preceded by Up, or the first Chorus as fallback.
- C is exactly 32 beats before D and is omitted when D has fewer than 32
  preceding beats. Its position is independent of B, and its contract evidence
  is `drop_offset_beats: 32`.
- E is the first Down after D, or the first Bridge after D as fallback.
- H starts on the first beat of the last Outro and loops 16 beats, or 8 when 16
  does not fit. F and G are not assigned automatically.

The `_vocal` stem must already exist as an analyzed Rekordbox content row. The
batch reads its preserved `PWV3` envelope only; it never runs Demucs or mutates
stem audio or ANLZ files. Missing or unreadable vocal analysis leaves B absent,
adds a warning, and keeps the track in `Hot Cues`. Rows whose file name ends in
`_vocal` or `_instrumental` are excluded from batch selection even when the glob
matches them.

H requires `loop_beats` equal to 8 or 16. Other slots do not accept a loop.
Positions are non-negative milliseconds and must not exceed the track duration.

The batch command writes every safely generated partial set, but READY is granted
only when mood, energy, tags, readable beatgrid, Quantize, and all six required
slots are complete. Otherwise it keeps the earliest incomplete status in this
order: `Mood`, `Energy`, `Tags`, `Hot Cues`. `DisableQuantize == 0` is the only
database value currently treated as verified; other values produce the
`quantize_unverified` warning and prevent READY.

When `hot_cues` is present, RekordBot synchronizes only rows carrying its managed
UUID prefix. Manual cues and manual memory cues are preserved. If a manual cue
already occupies a requested slot, the import fails before mutation instead of
overwriting it. After a cue change, `djmdCue`, the complete JSON cue list in
`contentCue.Cues`, and `contentCue.rb_cue_count` are updated in the same
transaction.

## Generated Memory Cues

The deterministic batch generator also synchronizes up to 10 Memory Cues per
track. It preserves unrelated manual Memory Cues and applies these rules:

- `VOCAL MANUEL` is created on the first beat when absent. Move it manually to
  the desired vocal or pressure-rise point; later runs preserve its position.
- Consecutive phrases of the same type form one block. Internal cues count
  backward from the block end every 32 beats and never land on a block boundary.
- Names combine the phrase abbreviation and distance: `IN`, `VE`, `BR`, `CH`,
  `BU`, `BD`, or `OUT`, followed by `-32`, `-64`, and so on.
- `FIN` is the cleanest four-beat, downbeat-to-downbeat loop found within the
  final 128 beats. RekordBot compares the detailed ANLZ waveform envelope around
  both boundaries, rejects loops whose mean waveform height is below the audible
  threshold, and omits `FIN` with a warning when no candidate passes both tests.
- `VOCAL MANUEL`, an eligible `FIN`, and all manual cues consume capacity first. Remaining
  candidates prioritize every `-32`, then every `-64`, and so on; equal
  distances use chronological order.
- A manual cue exactly on an automatic target covers that target, so no duplicate
  is created.

Rekordbox orders Memory Cues chronologically. Automatic cues before
`VOCAL MANUEL` are intentionally retained, so `VOCAL MANUEL` is not guaranteed
to be selected automatically when the track loads.

## Managed playlists

`desired_playlists` is optional so phase 2/3 documents remain compatible. Once a
workflow starts managing playlists for a track, include it in every later import
for that track. RekordBot treats the list as the complete desired membership set.

Paths are relative to a managed top-level `RekordBot` folder and have exactly two
segments. The required plan is derived from the fields in the same document:

- `Preparation/<status>` or `Preparation/READY`: exactly one membership.
- `Mood/<label>` when mood is present.
- `Energy/<rating>` when energy is present.
- `Genre/<tag>`, `Ann\u00e9e/<tag>`, and `Situation/<tag>` for every grouped tag.

When `desired_playlists` is present, validation rejects missing, extra, duplicate,
or non-canonical paths. Preparation memberships are therefore mutually exclusive,
while all classification memberships can overlap.

RekordBot creates only normal playlists and folders carrying its managed UUID
prefix. A user-created item colliding with a requested `RekordBot` path stops the
import before the transaction. Other folders, playlists, memberships, and their
track order are preserved. Playlist definitions are retained when they become
empty; only the imported track's managed memberships are synchronized.

Playlist writes require the Rekordbox `djmdPlaylist` and `djmdSongPlaylist` tables.
If either table is unavailable or incompatible, the import fails explicitly and
does not fall back to claiming success. Mood, rating, My Tags, cues, playlists,
and aggregate cue JSON are committed in one transaction, so a playlist failure
rolls back the whole track.

## Isolated Rekordbox UI verification

The automated tests use a disposable copy of `test.db`; they do not open a live
Rekordbox library. Before release, complete this manual check in an isolated
Rekordbox profile:

1. Keep the production library closed and retain its verified backup.
2. Import the fixture tracks into the isolated profile, then close Rekordbox.
3. Run one dry-run and one real import against the isolated `master.db` copy.
4. Reopen only the isolated profile.
5. Verify one preparation membership per track, overlapping mood/energy/tag
   classifications, stable track order, and no change to user playlists.
6. Repeat the same import and verify that the UI shows no duplicate folder,
   playlist, or track membership.

Do not treat automated SQLite assertions as proof that a new Rekordbox version
renders or synchronizes the rows correctly. A schema/UI mismatch is a release
blocker, not permission to test against the production database.
