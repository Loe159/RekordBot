# Workflow import 2.0 - phases 2 and 3

This import mode applies metadata and preparation status to one existing Rekordbox
content row, and can synchronize explicit Hot Cues. It does not run Beatport,
Soundcharts, Demucs, legacy cue generation, or ANLZ writes.

Use a dry run against a copied database first:

```powershell
CueGen.Console.exe --import workflow.json --database copied-master.db --dryrun
```

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
  "hot_cues": []
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
prepared.

Use `status: null` only to request READY. READY requires complete mood, energy,
grouped tags with at least one genre, `beatgrid_verified: true`,
`quantize_verified: true`, and canonical Hot Cues A, C, and E. Every supplied Hot
Cue must have `phrase_start_verified: true`. If any invariant fails, validation
stops before opening a transaction and the existing `Hot Cues` status remains.

## Canonical Hot Cues

| Slot | Name | Color | Rekordbox kind |
|---|---|---|---:|
| A | `IN-32` | Green | 1 |
| B | `BUILD-16` | Yellow | 2 |
| C | `DROP 1` | Red | 3 |
| D | `BREAK` | Blue | 5 |
| E | `OUT-32` | Orange | 6 |
| F | `PEAK / DROP 2` | Purple | 7 |
| G | `VOCAL / HOOK` | Pink | 8 |
| H | `LOOP` | Cyan | 9 |

H requires `loop_beats` equal to 8 or 16. Other slots do not accept a loop.
Positions are non-negative milliseconds and must not exceed the track duration.

When `hot_cues` is present, RekordBot synchronizes only rows carrying its managed
UUID prefix. Manual cues and manual memory cues are preserved. If a manual cue
already occupies a requested slot, the import fails before mutation instead of
overwriting it. After a cue change, `djmdCue`, the complete JSON cue list in
`contentCue.Cues`, and `contentCue.rb_cue_count` are updated in the same
transaction.
