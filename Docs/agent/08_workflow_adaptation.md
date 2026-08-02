# AI workflow adaptation

## Reference workflow

RekordBot is the only implementation repository in scope. The repository at
`https://github.com/Loe159/rekordbox-ai-worflow/tree/main`, inspected on
2026-08-02 at commit `53298e3b22e5ba5e42076a6e6679c03e93908b12`, is a functional reference only.
Do not modify it as part of RekordBot work.

Its README describes the intended new workflow, but its implementation still enforces the previous rules:

The target repository's four domain tests pass, including the test that rejects more than three My Tags. This confirms that the mismatch is in the checked-in contract, not only in this audit's interpretation.

| Concern | README target | Current workflow files | Current RekordBot |
|---|---|---|---|
| Progress | one of To Do, Mood, Energy, Tags, Hot Cues; READY has none | no `status` field in schemas/domain | no status support |
| Mood | one dominant mood stored as track color | taxonomy/color logic still uses Orange/Green/Red preparation state | optional track color stores MIK energy |
| Energy | Rekordbox rating 1-5 | export schema has rating 1-5 | MIK energy 1-8 stored as My Tag/color; `Content.Rating` is not written |
| Genres | multiple allowed taxonomy values | schema, validator, tests, and agent require one genre | Beatport genre/subgenre can create multiple My Tags, without taxonomy validation |
| My Tags | Status, Genre, year/origin, Situation; no global limit | old mood/texture/function model, maximum three | only Energy and Genre roots are created |
| Hot Cues | fixed A-H roles/colors; A/C/E required | old validator does enforce A/C/E and names | heuristic sequential slots; no A/C/E role validation |
| READY | no status plus all required controls validated | old export derives READY from preparation colors | no READY gate |
| Playlists | derive from status, mood, rating, and grouped tags | old derivation uses color, one genre, and function tags | no playlist mappings or writes |

Do not implement against schema version `1.0` and later reinterpret it. Define and
test RekordBot's versioned import contract locally before adding database writes.
The external repository can supply examples and intent, but it is not a runtime or
build dependency.

## Recommended ownership boundary

Upstream AI workflow owns:

- MusicBrainz/ISRC identity resolution and ambiguity handling.
- Legal acquisition adapter and file verification.
- Audio analysis and candidate beatgrid/phrases/cues.
- AI suggestions, provenance, confidence, and progressive human validation.
- Derivation of the desired metadata and playlist memberships.

RekordBot owns:

- Parsing and validating a versioned import document without network access.
- Resolving one existing Rekordbox content row deterministically.
- Backing up and transactionally applying mood color, rating, My Tags, cues, and supported playlists.
- Preserving unrelated user data and reporting a machine-readable result.

Beatport, Soundcharts, and Demucs must not run implicitly during import.

## Contract version 2 requirements

The workflow repository should publish a new schema before RekordBot implementation. At minimum it needs:

- `schema_version: "2.0"`.
- Track identity and verified path/ISRC.
- `status`: null or exactly one of `To Do`, `Mood`, `Energy`, `Tags`, `Hot Cues`.
- `mood.color` and `mood.label`, using one canonical mapping.
- `energy`: integer 1-5.
- `my_tags.genres`: unique allowed list, no forced primary genre.
- `my_tags.year_origin`: unique allowed list.
- `my_tags.situations`: unique allowed list.
- `hot_cues`: explicit slot A-H, canonical name/color, position, phrase-start evidence, and optional loop length.
- Beatgrid and Quantize validation flags.
- Optional memory cues with canonical names.
- Provenance, confidence, human validation, warnings, and desired playlists.

The displayed My Tag category for `year_origin` should map to the exact Rekordbox label `Ann\u00e9e`. Keep contract field names ASCII.

The contract must also decide whether RekordBot accepts partial progressive updates or only final READY imports. The README implies partial updates; if so, validation requirements must depend on `status`, and A/C/E/beatgrid become mandatory only when removing the final status.

## Proposed RekordBot components

Add these as focused modules after the contract is frozen:

1. Import contracts using Newtonsoft.Json, already referenced by `CueGen.csproj`.
2. A pure schema/domain validator with no database or filesystem access.
3. A taxonomy/color mapping loaded from a versioned file, not scattered numeric constants.
4. A `RekordboxRepository` that reuses the existing SQLite models and owns ID/USN allocation.
5. A `TrackResolver` that matches normalized absolute path first and verifies ISRC/title/artist; ambiguity must stop the import.
6. A generic My Tag upsert that supports all four roots and makes Status mutually exclusive.
7. Explicit cue upsert accepting slot A-H; reuse time/frame conversion and `ContentCue.SetCues()`.
8. A transaction-scoped import service with preflight, backup verification, dry-run diff, commit, and result report.
9. A CLI mode such as `--import <path>` that does not execute legacy generator/network/stem stages.
10. Playlist support only after the relevant Rekordbox tables/contracts are modeled and tested. Until then, fail clearly or document manual intelligent-playlist setup; do not silently claim playlist success.

## Field mapping

| Contract value | Rekordbox destination | Existing reuse |
|---|---|---|
| `energy` 1-5 | `djmdContent.Rating` | `Content.Rating` |
| `mood.color` | `djmdContent.ColorID` | centralize `ColorTable.Colors` mapping |
| grouped tags | `djmdMyTag` + `djmdSongMyTag` | existing models and USN/timestamp conventions |
| status | exactly one child under Status, or none for READY | genericized My Tag upsert |
| explicit hot cue | `djmdCue` with explicit `Kind`/slot | `CreateCue` time/frame fields and color table |
| memory cue | `djmdCue` with memory kind | existing `Cue` model |
| complete cue list | `contentCue.Cues` and cue count | `ContentCue.SetCues()` |
| playlist membership | currently unsupported | requires new schema evidence/model |

## Implementation sequence

### Phase 0 - make RekordBot reproducible

1. Rotate/remove hardcoded credentials and stop tracking `.env`.
2. Commit or remove the untracked C# files required by tracked code.
3. Reconcile stale Beatport tests.
4. Install a supported .NET SDK and get a clean-clone build/test baseline.
5. Record the local contract `2.0` requirements and keep the external workflow as reference-only input.

Exit criteria: RekordBot builds and tests from a clean clone, optional integrations
remain disabled without credentials, and the current source tree contains no
plaintext secrets. Previously exposed credentials must be rotated separately; Git
history cleanup requires explicit approval.

### Phase 1 - make mutation safe

1. Fix the generator success/error return contract.
2. Remove forced Beatport execution.
3. Make dry-run cover every database, audio, and ANLZ write.
4. Add an explicit Rekordbox-closed/preflight check where feasible.
5. Verify backup creation before any mutation.

Exit criteria: an offline dry-run changes no database or filesystem byte and returns reliable exit codes.

### Phase 2 - import metadata and status

1. Add v2 DTO parsing and pure validation.
2. Resolve one existing content row.
3. Write mood color to `ColorID` and energy to `Rating`.
4. Upsert Status, Genre, year/origin, and Situation tags.
5. Remove legacy energy-color and Energy-MyTag behavior from the import path.

Exit criteria: repeated imports are idempotent, status is exclusive, multiple genres are preserved, and unrelated tags remain untouched.

### Phase 3 - import cues and READY gate

1. Upsert explicit hot slots A-H and canonical names/colors.
2. Preserve unrelated/manual cues by policy.
3. Require verified beatgrid, Quantize, phrase starts, and A/C/E before status becomes null.
4. Update both `djmdCue` and `contentCue` consistently.

Exit criteria: incomplete tracks retain the correct next status; READY tracks have no status and pass every required invariant.

### Phase 4 - playlists and end-to-end proof

1. Discover and model playlist tables using an isolated database.
2. Implement idempotent folders/playlists/memberships or choose documented intelligent-playlist setup.
3. Test one track from each main musical family and every status transition.
4. Reopen the isolated library in Rekordbox and verify UI behavior.

Exit criteria: preparation playlists are mutually exclusive, classification playlists can overlap, and rollback restores the pre-import state.

## Minimum acceptance tests

- Reject schema 1.0 and unknown fields/tags/colors/statuses.
- Accept multiple genres and more than three total grouped tags.
- Map energy 1 and 5 to Rating without changing mood color.
- Replace one Status child atomically and remove it only for valid READY data.
- Reject READY when beatgrid, Quantize, A, C, or E is missing/unverified.
- Place A/C/E in their exact slots with canonical name/color and first-phrase-beat evidence.
- Preserve manual cues and unrelated My Tags.
- Import twice with no duplicate rows.
- Roll back the complete track on any write failure.
- Produce a useful dry-run diff and mutate nothing.
- Operate offline with external integrations disabled.

## Open decisions

- Partial status updates versus final-only import.
- Exact mood-to-Rekordbox `ColorID` mapping, verified on the target Rekordbox version.
- Whether playlist memberships are written directly or represented by user-created intelligent playlists.
- Track resolution when the file is not yet present in `djmdContent`.
- Managed-cue ownership policy when a user already occupies A, C, or E.
- Unicode normalization and exact display labels for year/origin tags.
