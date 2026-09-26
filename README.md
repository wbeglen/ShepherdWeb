# ShepherdWeb

The human-facing web client for the Good Shepherd experimental platform: a Blazor Web App
(.NET 10, Interactive Server) over ShepherdAPI. The architecture, and the reasons behind it, are
in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Run

ShepherdWeb needs ShepherdAPI running. From the ShepherdAPI repository:

```
dotnet run --project src/ShepherdAPI.Api --launch-profile http
```

Then, from this repository:

```
dotnet run --project src/ShepherdWeb
```

Open https://localhost:7240. The Setup page shows whether ShepherdAPI is reachable and whether the
reference rows ShepherdWeb relies on exist. ShepherdWeb listens on localhost only: it has no
authentication and must not be exposed on a network until it does (ARCHITECTURE.md AD-14).

## Test

```
dotnet test
```

## Configuration

`src/ShepherdWeb/appsettings.json`:

| Section | Purpose |
|---|---|
| `ShepherdApi` | ShepherdAPI base URL and timeout. |
| `Conventions` | Names of the status and artifact-type rows ShepherdWeb looks up (AD-13). |

## ShepherdAPI contract

The typed API client is generated at build time from the pinned snapshot
`contracts/shepherdapi.openapi.json`. Its source and the steps for updating it are in
[contracts/SOURCE.md](contracts/SOURCE.md).
