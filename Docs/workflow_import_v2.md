# Workflow import 2.0 - phase 2

This import mode applies metadata and preparation status to one existing Rekordbox
content row. It does not run Beatport, Soundcharts, Demucs, legacy cue generation,
or ANLZ writes.

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
  }
}
```

The path must resolve to exactly one existing `djmdContent` row. Every supplied
identity verifier must match. The import synchronizes only the `Status`, `Genre`,
`Annee` (stored as the Unicode Rekordbox label), and `Situation` associations for
that track. Other My Tag categories and all global tag definitions are preserved.

Phase 2 accepts progressive statuses only. A null status is rejected until phase 3
can validate the READY invariants for beatgrid, Quantize, and hot cues A/C/E.
