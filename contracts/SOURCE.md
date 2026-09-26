# ShepherdAPI contract snapshot

`shepherdapi.openapi.json` is the pinned ShepherdAPI contract that ShepherdWeb's typed client is generated from at build time (docs/ARCHITECTURE.md AD-2).

| Field | Value |
|---|---|
| Source | `GET http://localhost:5231/swagger/v1/swagger.json` from a locally running ShepherdAPI (Development) |
| ShepherdAPI commit | `3881ed631e85c403eb1c96750a2ee785b9aaa124` (2026-09-23) **plus uncommitted working-tree changes** |
| Uncommitted changes reflected | ScoreRule add/delete-only lifecycle: `PUT /api/score-rules/{id}` and `UpdateScoreRuleRequest` removed; `ScoreRule.rowState` added |
| Captured | 2026-09-25 |

The committed `ShepherdAPI/contracts/openapi.json` at that commit still describes the older ScoreRule API. When the ScoreRule changes are committed in ShepherdAPI (and again when the approved R1 lifecycle change lands), recapture this file and update the table above.

## Updating

1. Run ShepherdAPI locally (`dotnet run --project src/ShepherdAPI.Api --launch-profile http`).
2. Save `http://localhost:5231/swagger/v1/swagger.json` over `shepherdapi.openapi.json`.
3. Update the table above with the ShepherdAPI commit and date.
4. Rebuild ShepherdWeb and fix anything the regenerated client breaks.
