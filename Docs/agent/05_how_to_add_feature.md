# How to add a feature

This file documents flows that already exist. The proposed AI workflow import is described separately in `Docs/agent/08_workflow_adaptation.md`.

## Add a CLI/configuration option

1. Add a typed property and safe default to `CueGen/Config.cs`.
2. Add a `Mono.Options` entry in `CueGen.Console/Program.cs`.
3. Parse any compound value after `options.Parse(args)` as existing colors, names, and order options do.
4. Validate incompatible or required options before `Program.Generate()` opens or backs up the database.
5. Add a help-text assertion or a behavior test.

## Add a generator operation

1. Read existing state through the mappings and helpers in `Generator`.
2. Put deterministic mapping logic in a separate private or service method.
3. Perform a preflight validation before mutation.
4. Respect `Config.DryRun` for every database and filesystem side effect.
5. Wrap related writes in one `RunInTransaction` call.
6. Log content ID and path without logging credentials or sensitive payloads.
7. Continue per track only when partial success is explicitly safe.
8. Add a seed-database test and idempotency test.

## Add a Rekordbox table mapping

1. Confirm the real table and columns against a test database schema.
2. Derive shared synchronization columns from `CommonTable` when applicable.
3. Add `[Table]`, `[PrimaryKey]`, and nullability attributes following existing models.
4. Add a focused read method before adding writes.
5. Test only with a copied fixture database.

## Add an ANLZ section

1. Add the FourCC value to `CueGen/Analysis/AnlzMagic.cs`.
2. Create an `AnlzSectionContent` subclass with explicit field order, size, encoding, and endianness metadata.
3. Register the subtype in `AnlzSection.cs`.
4. Preserve unknown bytes so reserialization is lossless.
5. Add a binary fixture test for deserialize and serialize behavior.

## Add an external metadata client operation

1. Keep authentication/configuration outside database transactions.
2. Return a typed response model.
3. Handle non-success status codes without logging credentials or full secret-bearing responses.
4. Map external data in a pure method before persistence.
5. Make the feature opt-in; never force a network call in the main track loop.
6. Add mocked client tests rather than live API tests.

## Add a test

1. Use `CueGen.Test/test.db` through the copy-per-test setup in `Tests.cs`.
2. Reuse existing audio/ANLZ fixtures when possible.
3. Normalize generated IDs and timestamps for golden output.
4. Add direct assertions for invariants and side effects; do not rely only on snapshots.
5. Ensure the test cannot access a live Rekordbox database or real external API.
