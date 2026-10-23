# ShepherdAPI contract snapshot

`shepherdapi.openapi.json` is the pinned ShepherdAPI contract that ShepherdWeb's typed client is generated from at build time (docs/ARCHITECTURE.md AD-2).

| Field | Value |
|---|---|
| Source | ShepherdAPI's committed `contracts/openapi.json`, copied unchanged |
| ShepherdAPI commit | `b0125d4d65ebca1c5a055d2bd16c14c945411095` (2026-10-23, branch `issue-7-scorerule-physical-delete`, "Make Version 10 the authoritative database baseline"; database V10, Issues #1–#10) |
| Verified | 2026-10-23: identical to ShepherdAPI's committed file at that commit, which ShepherdAPI's `OpenApiSnapshotTests` keeps identical to the running API's document |
| Previous snapshot | `dce802f` (2026-09-28), captured from the running API |

ShepherdAPI's committed `contracts/openapi.json` is guarded by a snapshot test (it fails when the file differs from the generated document), so it is the file to copy.

## Known contract gaps (worked around in ShepherdWeb)

- Creates on agents, artifact types, experiment versions, phases, raters, score categories and statuses are still documented as `200`, but return `201 Created` (recommendation R8). Every other create and delete declares its real `201`/`204`. See `src/ShepherdWeb/Infrastructure/Api/GeneratedClientCustomization.cs`, which applies the 201/204-to-200 mapping per client.
- No `operationId`s, so list and single-item calls share a method name (recommendation R4). List calls use named arguments.
- `Artifact.SHA256` is camel-cased to `shA256`, so the generated property is `ShA256`.

## Updating

1. Copy ShepherdAPI's `contracts/openapi.json` (at the commit you build against) over `shepherdapi.openapi.json`.
2. Update the table above with the ShepherdAPI commit and date.
3. Check which clients now declare `201`/`204` and update `GeneratedClientCustomization.cs`: a client that declares them must not use the R8 mapping.
4. Rebuild ShepherdWeb and fix anything the regenerated client breaks.
