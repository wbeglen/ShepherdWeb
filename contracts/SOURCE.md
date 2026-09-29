# ShepherdAPI contract snapshot

`shepherdapi.openapi.json` is the pinned ShepherdAPI contract that ShepherdWeb's typed client is generated from at build time (docs/ARCHITECTURE.md AD-2).

| Field | Value |
|---|---|
| Source | `GET http://localhost:5231/swagger/v1/swagger.json` from a locally running ShepherdAPI (Development) |
| ShepherdAPI commit | `dce802f8c71ed20caf41327f0d34b28f9ba43c6d` (2026-09-28, "Implement ScoreRule RowState lifecycle") |
| Verified | 2026-09-29: the running API's document is identical to this snapshot |
| First captured | 2026-09-25, from `3881ed6` plus the then-uncommitted ScoreRule changes that `dce802f` later committed |

ShepherdAPI's own committed `contracts/openapi.json` was not regenerated in `dce802f` and still describes the older ScoreRule API (with `PUT /api/score-rules/{id}`). Use the running API, not that file, when recapturing.

## Known contract gaps (worked around in ShepherdWeb)

- Every operation is documented as `200`, but creates return `201 Created` and deletes `204 No Content` (recommendation R8). See `src/ShepherdWeb/Infrastructure/Api/GeneratedClientCustomization.cs`.
- No `operationId`s, so list and single-item calls share a method name (recommendation R4). List calls use named arguments.
- `Artifact.SHA256` is camel-cased to `shA256`, so the generated property is `ShA256`.

## Updating

1. Run ShepherdAPI locally (`dotnet run --project src/ShepherdAPI.Api --launch-profile http`).
2. Save `http://localhost:5231/swagger/v1/swagger.json` over `shepherdapi.openapi.json`.
3. Update the table above with the ShepherdAPI commit and date.
4. Rebuild ShepherdWeb and fix anything the regenerated client breaks.
