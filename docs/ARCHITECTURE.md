# ShepherdWeb Architecture — MVP

**Status:** Revision 4.2. Revision 3 was approved and slices 0–1 are implemented; revision 4 records the owner's lifecycle decisions for the execution and scoring slice (§14, Q1–Q6).
**Scope:** The MVP only. This document says what the MVP is allowed to build. It does not describe a final design.
**Audience:** The Good Shepherd owner/operator, and an experienced .NET engineer learning modern Blazor. Sections marked *Blazor note* explain decisions that are specific to Blazor.

### Revision history

| Rev | Change |
|---|---|
| 1 | Initial proposal. |
| 2 | Revised for the owner's lifecycle clarification (§3) and answers to B1–B4 and N1–N9 (§14). The main effects: a new lifecycle model (Draft → Locked → execution); a **conflict found between that lifecycle and ShepherdAPI's current lock enforcement** (F1, new blocking question B5); clone/lineage removed (TBD); `ScoreRule.Active` ignored; weight totals removed; prompt-drift handling simplified; run facts written once when an execution finishes; score-rule correction left as a TBD boundary. |
| 3 | Revised for the owner's execution-workflow clarification: **Configure → Review → Lock → Start → HANDS OFF → Ends → Evaluate**. While an execution runs, the UI assumes no routine human activity. Artifact capture and scoring move to after the execution ends. Intervention handling is reduced to what the existing schema already records (`InterventionCount`, `OutcomeNotes`), plus an exceptional path for preserving any additional delivered prompt. The intervention-specific artifact types from revision 2 are removed, and intervention representation is TBD. Manual prompt copying is treated as start-up plumbing. |
| 3.1 | Recorded owner decisions N10 (new executions get `Draft`) and B5 (R1 approved; the reading of "editable" confirmed). No open questions remain. |
| 4.1 | **Execution-specific start prompt** (owner decision). The iteration's prompt remains the default, but before Start the operator may edit the prompt for one execution. The registered `DeliveredPrompt` artifact stays the authoritative record; no prompt is added to `Execution` and the API contract and database are unchanged. An edited prompt has distinct provenance (`urn:goodshepherd:execution:{ExecutionID}:prompt`); retry after a partial failure registers the exact saved text, with no editing; the consistency check no longer warns for an intentional override. AD-8 and §8.2 revised; assumption A3 (§12) narrowed. Interventions are unchanged. |
| 4.2 | **Start-prompt evidence integrity** (owner-approved review of `1a808b2`). Start-prompt identity comes from explicit provenance (the role contract in AD-8), never from timestamp order; artifact identity includes provenance, so identical bytes with a different role are different evidence; a delivered prompt is displayed or copied only after its stored bytes are verified against the recorded SHA-256; the pending start prompt carries its execution record's identity and the provenance decided at Start, and a retry reproduces exactly that; **if the exact Start prompt cannot be established, no substitute is offered** (the execution stays incomplete and a new attempt is required, owner decision); a registration failure keeps the operator on the Start page with an in-memory retry; mismatch messages state observed facts only. Class C dependencies tracked as A1, A6, A7, A8 (§11); Class D limitations documented (§12). |
| 4.3 | **Compatibility with ShepherdAPI V10** (`b0125d4`, Issues #1–#10; owner decisions D1–D5, §14). The pinned contract is refreshed; R8 now applies only to the clients whose creates are still documented as `200`; errors are mapped by ShepherdAPI's stable `code` (AD-6). ShepherdAPI now enforces **A1** (`design-frozen`) and **A3**; A5 is still UI-only. Score rules are **deleted** physically and free their phase and category (Issue #7), replacing Retire (B4 resolved). **Lock runs finalization**: ShepherdAPI refuses it unless every execution has ended and been scored and every phase's rule weights total 100, snapshots every score, and no longer sets the status; ShepherdWeb disables Lock while executions are open and then sets `Locked` in a second call (D1). Weight totals stay uncalculated in ShepherdWeb (N3 kept, D3), but ShepherdAPI requires 100 per phase to start an execution and to lock. |
| 4 | **Lifecycle revised to ShepherdAPI's existing lock semantics** (owner decisions Q1–Q6, §14). Executions run on a version that is still `Draft`; configuration becomes immutable when the **first execution exists**; the **Lock action is the final immutability boundary** for everything, including scores and evidence. **R1 is withdrawn**; the execution and scoring slice proceeds against the existing API. Server-side enforcement the API still lacks is recorded as A1, A3 and A5 (§11); A4 is an optional efficiency improvement; A2 is deferred. Former slices 2 and 3 are merged. Lock moves onto the version Overview. Also records what implementing slice 1 found (201/204 contract gap, overload ambiguity, versions with executions but no lock). |

### How to read this document

Each significant statement is labeled:

| Label | Meaning |
|---|---|
| **[Decision]** | An architectural decision made here under the authority in §8.1 of the brief. It can be reviewed, but it is not a question. |
| **[Owner decision]** | Settled by the owner's answers (§14). |
| **[Assumption]** | Something I treat as true without confirmation. If it is wrong, the design changes in the way described. |
| **[Recommendation]** | A proposal that needs someone else's approval, usually a ShepherdAPI change. It is not part of the design until approved. |
| **[Deferred]** | Deliberately left out of the MVP, with the reason. |
| **[TBD]** | *To Be Discovered.* Deliberately left open until implementation or experimental evidence makes a decision necessary. The design keeps a clean boundary around it and makes no irreversible assumption about it. |
| **[API finding]** | Something I found in ShepherdAPI that affects this design. |

Open questions are collected in §14.

---

## 1. Purpose

ShepherdWeb is the web client an operator uses to work with the Good Shepherd experiment platform. Its job is to cut down the typing, retyping, and copying needed to:

1. **Configure** experiments: experiment versions, iterations and their prompts, parameters, score rules, and the global reference data those depend on.
2. **Conduct** executions: start them from complete configuration, stay out of the way while they run, record when and how they ended, then support evaluation (artifacts, scores, evidence).
3. **Inspect** experiment data, in bounded amounts, to investigate and verify.

It does this without weakening experimental integrity. Convenience must never hide or change what was actually used.

ShepherdWeb does not own domain data. ShepherdAPI owns the domain, and ShepherdWeb is a client of it.

## 2. Inputs Reviewed

| Input | Version |
|---|---|
| Owner's architecture brief (Sections 0–9) | — |
| Owner's clarifications (lifecycle, B1–B4, N1–N9) | Received after revision 1 |
| `ShepherdAPI/docs/ARCHITECTURE.md` | Alpha, approved |
| `ShepherdAPI/database/Create_Experiments_Database_And_Tables-6.sql` | DDL v6, including `ExperimentVersion.LockedUTC` |
| `ShepherdAPI/contracts/openapi.json` | OpenAPI 3.0.4 |
| `ShepherdAPI/src/**`, `ShepherdAPI/tests/**` | commit `dce802f` (ScoreRule add/delete-only lifecycle with `RowState`). Slices 0–1 were built against the same code while it was still uncommitted. ShepherdAPI's committed `contracts/openapi.json` was not regenerated in that commit, so ShepherdWeb's pinned contract was captured from the running API (see `contracts/SOURCE.md`). **Revision 4.3:** re-pinned to commit `b0125d4` (database V10, Issues #1–#10), copied from ShepherdAPI's committed contract, which a snapshot test now keeps current. |

Several findings come from the service code rather than the contract, because what the API actually enforces matters more here than what it describes.

---

## 3. Experiment Lifecycle (as clarified by the owner)

This is the central invariant that the rest of the design depends on. It applies to the whole experiment version, not to prompts, iterations, or score rules individually. **Revision 4** adopts ShepherdAPI's existing lock semantics (owner decision Q1).

```
   Draft, no executions ──first execution──► Draft, executing ──Lock──► Locked (final)
   configure, review,                         configuration frozen        LockedUTC set
   correct, refine                            executions recorded         nothing editable
                                              scoring and evidence        except status
                                              editable                    (e.g. → Retired)
```

**[Owner decision]**

1. **Configuration takes as long as the operator needs.** A version does not become executable just because it was created. Before its first execution, the version and its configuration (descriptive fields, iterations and prompts, parameters, score rules) can be created, reviewed, corrected, and refined.
2. **Executions run on a version that is still `Draft`.** The operator starts the first execution when the configuration is complete. **New executions are blocked for `Locked` or `Retired` versions** (Q3). `Complete` is not an immutability boundary; `LockedUTC` remains authoritative.
3. **Once the first execution exists, all experiment-defining configuration is immutable** (Q1, Q6): version descriptive fields, iterations and prompts, parameters, and score rules.
4. **After execution begins, executions are recorded and evaluated until the version is locked.** Executions, artifacts and run facts are recorded; ScoreObservations and ScoreObservationEvidence are created and corrected. **The Lock action is the final immutability boundary**: it sets `LockedUTC` and the `Locked` status, after which nothing can change except the version's status.
5. **An execution is hands-off.** It works like a horse race: all human preparation happens before the gates open. Once an execution starts, the operator waits for the agent to finish while the system records what happens. Human activity resumes only after the execution ends, with scoring, evidence, review, and analysis.
6. **Intervention is exceptional.** It is not part of the normal workflow, but it can happen (as in the recent Codex execution problem). When it does, **the fact that it happened must be preserved**, because it may matter when the execution is interpreted later. How interventions are represented in detail is **TBD**.
7. **Manual steps needed to start an agent are plumbing.** Copying and pasting the prompt is not evidence that the operator normally takes part in an execution.
8. The version statuses that matter are `Draft`, `Locked`, `Retired`, and `Complete`. The version stays `Draft` while executions and scoring are underway (Q3).
9. **`EndUTC` is the authoritative indicator that an execution has ended** (Q2). `Execution.StatusID` does not govern editability. Ending an execution freezes its run facts but **does not freeze scoring**; scoring is offered only after an execution has ended (Q5).

```
 Configure → Review │ Start Execution → HANDS OFF → Execution Ends │ Evaluate … │ Lock (final)
 ── human-paced ─── │ ─── system records; human waits ────────────  │ ── human ── │
                    │  (exceptional: intervention, fact preserved)  │ artifacts,  │ nothing more
                    │                                               │ scores,     │ can change
                    │                                               │ evidence    │
```

**What is allowed in each state:**

| Activity | Draft, no executions | Draft, executing | Locked | Basis |
|---|---|---|---|---|
| Change configuration (version details, Iteration, Parameter, ScoreRule) | Yes | **No** | No | Rules 1, 3 |
| Start an execution | Yes, once configuration is complete | Yes | **No** (also no for `Retired`) | Rule 2 |
| Register delivered prompts (at start, and during an exceptional intervention) | — | Yes | No | Brief §4.3 |
| Write run facts (`EndUTC`, `InterventionCount`, `OutcomeNotes`) | — | Yes, **once**, when the execution ends; frozen afterwards | No | Rule 9; ShepherdAPI §5.1 |
| Register evaluation artifacts; create and correct ScoreObservation and ScoreObservationEvidence | — | Yes, for ended executions | **No** | Rules 4, 9 |
| Change version status | Yes | Yes | Yes (`Locked` → `Retired`/`Complete`) | ShepherdAPI §5.3 |

**Enforcement.** ShepherdAPI enforces the Lock boundary, the `EndUTC` run-fact freeze and, since revision 4.3 (V10), the first-execution configuration freeze (rule 3, `design-frozen`, **A1**). It does **not** enforce the `Retired` start block (rule 2) or scoring only after an execution ends (rule 9). ShepherdWeb applies those two in the UI; rule 9 is recorded as server-side enforcement work **A5** (§11). Since V10, locking is ShepherdAPI's **finalization**, which has preconditions (AD-7).

---

## 4. System Context

```
 ┌──────────────────────────┐   HTTPS/WSS (Blazor circuit, localhost)
 │ Operator's browser       │◄───────────────────────────────┐
 │ (DOM + blazor.web.js)    │                                │
 └──────────────────────────┘                                │
                                                              │
 ┌────────────────────────────────────────────────────────────┴───┐
 │ ShepherdWeb (ASP.NET Core, Blazor Web App, Interactive Server) │
 │                                                                │
 │  Razor components ── generated ShepherdAPI client ── HttpClient├──HTTP/JSON──► ShepherdAPI ──► Experiments DB
 │         │                                                      │              (sole domain       (SQL Server)
 │         └── Artifact capture (hash + write-once file copy) ────┼──► Artifact store: local disk
 └────────────────────────────────────────────────────────────────┘      (the location in StoredURI)
```

- **[Decision]** Every Good Shepherd domain read and write goes through ShepherdAPI. ShepherdWeb has no database connection string and no reference to `Microsoft.Data.SqlClient`.
- **[Owner decision, B1/B3]** Artifact bytes, including delivered prompts, live on the **local machine**, at the location `Artifact.StoredURI` names. ShepherdWeb writes them there (AD-9). The artifact store is not a database and holds no domain records. It holds the bytes whose identity (SHA-256) and provenance ShepherdAPI records.

---

## 5. ShepherdAPI Findings That Shape This Design

| # | Finding | Effect on ShepherdWeb |
|---|---|---|
| **F1** | ShepherdAPI treats `LockedUTC` as "the experimental record is final" (API architecture §5.3): it allows executions, artifacts, observations and evidence while a version is unlocked and rejects all of them after locking. It does **not** freeze configuration when the first execution is created. | **Resolved in revision 4:** the owner adopted these lock semantics (Q1), so the execution and scoring slice proceeds against the existing API and **R1 is withdrawn**. The remaining gap, the first-execution configuration freeze, is enforced in the UI and recorded as **A1** (§11). **Revision 4.3:** ShepherdAPI V10 enforces A1 (`design-frozen`). |
| F2 | ScoreRule has a unique constraint on `(ExperimentVersionID, PhaseID, ScoreCategoryID)` that **includes soft-deleted rows**, and there is no update endpoint. | **Resolved in revision 4.3 (ShepherdAPI Issue #7):** `RowState` is gone; a rule is deleted physically before the first execution, which frees its phase and category, and `PUT /api/score-rules/{id}` exists (not used by ShepherdWeb yet). See AD-7 and §8.1. |
| F3 | `CreateScoreObservation` does not check that the score is within [Min, Max], that the rule belongs to the execution's version, that the rule's phase matches the execution's phase, or that the rule is active (`RowState='A'`). | ShepherdWeb restricts what can be picked and entered, but only as a convenience. The API should enforce this (**A3**, brief §7.3). **Revision 4.3:** ShepherdAPI V10 enforces it (`400 out-of-range-score`, `409 version-phase-mismatch`). |
| F4 | Timestamps are `DateTime`. Values read through Dapper have `Kind=Unspecified`, so the JSON has **no `Z` suffix**. Incoming values with a non-UTC offset are converted to the *server's local time* before being stored. | ShepherdWeb always sends UTC-kind values and treats every received timestamp as UTC (AD-10). R3 fixes this for all clients. |
| F5 | The OpenAPI document has no `operationId`s. | Generated client method names are awkward (AD-2, R4). |
| F6 | No CORS configuration. | Irrelevant with server-side calls (AD-1). |
| F7 | Reference data (Agent, Phase, ScoreCategory, Status, Rater, ArtifactType) is global, unversioned, freely editable, and not governed by any lock. | **[Owner decision, N4]** A permanent warning on the edit pages is enough for the MVP (AD-7). |
| F8 | Lists are scoped by foreign key only. There is no "all executions for a version" query. (`GET /score-rules` returned only non-retired rules until revision 4.3; it now returns every rule.) | Inspection makes one call per iteration. That is acceptable at MVP volume (AD-4; A4 would remove the extra calls). |
| F9 | `Execution.StatusID` is set at creation and **cannot be changed through the API**. There is no endpoint for it, and API §5.1 says it has no architectural meaning. | ShepherdWeb sets it once from a configured default (AD-13). `EndUTC`, not the status, decides whether an execution has ended (Q2). **[Deferred, A2]** A way to change execution status, pending clarification of its future purpose. |
| F10 | ShepherdAPI targets .NET 8, whose support ends 10 November 2026. | **[Owner decision, N9]** Upgrade to .NET 10 as soon as practical (R6). |

What already works well for a client: RFC 7807 ProblemDetails errors with a stable `code` on every error (revision 4.3; before V10, a distinct 409 "Record Locked" title), full-replacement PUTs with explicit mutable-field sets, a real OpenAPI contract, and lock enforcement (`LockedUTC` freezes everything a version governs) that already matches §3's final boundary.

---

## 6. Architectural Decisions

### AD-1 — Blazor Web App on .NET 10, global Interactive Server render mode, prerendering off

**[Decision; B2 confirmed by the owner]** ShepherdWeb is a single ASP.NET Core **Blazor Web App** targeting **.NET 10 (the current LTS)**. It uses the **Interactive Server** render mode globally (set once on `<Routes>` and `<HeadOutlet>` in `App.razor`) with **prerendering turned off**.

**Why:**

- **Single user on localhost.** Interactive Server's main costs are a server connection per browser tab and sensitivity to latency. Neither matters here.
- **API calls stay on the server.** The browser talks only to ShepherdWeb. ShepherdAPI needs no CORS change, is not exposed to the browser, and adding authentication later is the standard server-side pattern: a cookie session in ShepherdWeb, with tokens forwarded to the API by a `DelegatingHandler` (brief §2.8).
- **Server-side file access.** Artifact capture hashes files and writes them once to the local artifact store (AD-9). That is simple .NET code on the server and cannot be done from WebAssembly.
- **One process, one project, normal .NET debugging.** For a reference implementation, this avoids the client/server project split and the "does this code run in the browser?" question that WebAssembly and Auto bring.

**Why prerendering is off:** When prerendering is on, a page first renders statically on the server and then renders *again* once the interactive circuit connects. Without extra work, `OnInitializedAsync` runs twice and every API call on the page happens twice. .NET 10's `[PersistentState]` attribute can pass the first result along, but an internal tool gains nothing from prerendering (no SEO, and first paint on localhost is already instant). Turning it off removes a class of subtle bugs: `@rendermode="new InteractiveServerRenderMode(prerender: false)"`.

**Alternatives considered:**

| Alternative | Why not |
|---|---|
| Interactive WebAssembly | Requires CORS on ShepherdAPI (an API change made only to suit the client). Exposes the API to the browser. Future auth tokens would live in the browser. No server-side hashing or artifact writing, so a separate backend would be needed anyway. |
| Interactive Auto | Two runtimes, so components have to work in both. Complexity with no requirement behind it. |
| Static SSR with enhanced forms only | Handles basic create/edit screens, but is poor for the core workflows: live prompt review and hashing, grid-style scoring, clipboard copy, dependent dropdowns. |
| Choosing a render mode per page | Powerful, but every page then has to decide its mode. One global mode is simpler for an internal tool. |

*Blazor note — the circuit and brief §3.2 (resolved, B2).* Interactive Server keeps a **circuit** per browser tab: component state held in server memory, with UI events and DOM diffs sent over a SignalR connection. That connection is how Blazor Server renders. The owner confirmed that §3.2 prohibits introducing application-level real-time infrastructure without a demonstrated requirement, not infrastructure intrinsic to the chosen render mode. Any application-level use of SignalR (hubs, server push) would still need its own justification.

*Blazor note — what happens when the circuit is lost.* If the connection drops (laptop sleep, server restart) and the circuit is evicted, in-memory component state is gone. Committed data is safe because it lives in ShepherdAPI. Every page can rebuild itself from its URL plus API data (AD-4). Long, unsaved text (prompts, notes) is protected by drafts in local storage (AD-11). .NET 10 also offers circuit state persistence. [Deferred] I am not adopting it unless the simpler draft mechanism proves insufficient.

---

### AD-2 — A generated typed API client from a pinned contract snapshot

**[Decision]** ShepherdWeb calls ShepherdAPI through a **typed client generated at build time from a snapshot of `openapi.json` committed in the ShepherdWeb repository** (`contracts/shepherdapi.openapi.json`, with the source commit recorded next to it). Model classes are not written by hand. That matches the API architecture's own rule (§2).

**Generator: NSwag**, through the `OpenApiReference` MSBuild item (`NSwag.ApiDescription.Client`), with these settings: one client and one interface per OpenAPI tag (`ExperimentVersionsClient` / `IExperimentVersionsClient`, mapping onto the API controllers); System.Text.Json; **data annotations generated** from the schema's `maxLength` and `required` values (AD-5); and the base URL taken from `HttpClient.BaseAddress`.

**Why a pinned snapshot instead of fetching the live Swagger document:** builds are reproducible and do not need ShepherdAPI running. Contract changes show up as reviewable diffs in ShepherdWeb. Updating is a deliberate step: copy the file, rebuild, fix whatever no longer compiles. Behavior changes that leave the contract shape unchanged (such as the server-side enforcement items in §11) are why ShepherdWeb records which API commit it was verified against.

**Why NSwag rather than Kiota:** Kiota produces a fluent request builder and makes every model property nullable. It needs its own runtime packages and error-mapping setup. NSwag produces plain classes and flat async methods that feel familiar to .NET developers. It throws a typed `ApiException<ProblemDetails>` that is easy to map in one place (AD-6), and it carries DataAnnotations through to the forms. The weakness is naming without `operationId`s (F5). That is cosmetic and fixed by **R4**.

**Alternative considered:** hand-written `HttpClient` calls with hand-copied DTOs. It is feasible for about 40 endpoints, but it contradicts the API's contract-first decision and drifts silently.

**[Decision]** Components and the few workflow services (AD-4) use the generated client **directly**. There is no repository or "ApiService" layer re-wrapping every endpoint.

---

### AD-3 — Solution structure: one application project, one test project, organized by feature

**[Decision]**

```
ShepherdWeb/
  contracts/
    shepherdapi.openapi.json        pinned contract snapshot (+ SOURCE.md: API commit, date)
  docs/
    ARCHITECTURE.md                 this document
  src/ShepherdWeb/                  the Blazor Web App (single deployable)
    Components/
      App.razor, Routes.razor       root + global render mode
      Layout/                       MainLayout, NavMenu, ErrorBoundary placement, API status
      Shared/                       small reusable UI pieces (AD-12)
    Features/
      ExperimentVersions/           list, detail, create/edit, configuration review, lock, status
      Iterations/                   list/edit, prompt editor
      Parameters/
      ScoreRules/
      ReferenceData/                one small page per lookup
      Executions/                   prepare/start, hands-off in-progress view, execution-ended form
      Artifacts/                    capture (upload+hash), list, integrity verify
      Scoring/                      observation grid, evidence links
      Inspection/                   version overview, execution detail
    Infrastructure/
      Api/                          generated client registration, ProblemDetails mapping
      ArtifactStore/                write-once store, hashing, file-serving endpoint
      Time/                         UTC helpers
    Program.cs
  tests/ShepherdWeb.Tests/          xUnit + bUnit
```

**Why group by feature instead of by technical layer:** a Blazor feature is usually a page, one or two child components, and possibly one workflow service. Keeping them together makes each vertical slice (§10) a single folder to review.

**Why only one application project:** there is one deployable, no shared client assembly (no WebAssembly), and no reuse outside this app. Separate "Core" or "Domain" projects would suggest a domain layer that must not exist here (brief §2.2).

---

### AD-4 — Component and state model: pages own their state; there is no state-management library

**[Decision]**

1. **A routable page is the unit of state.** It loads what it needs in `OnInitializedAsync` / `OnParametersSetAsync`, keeps it in fields, and passes data down as parameters. After a successful write it **reloads from the API response**, so the screen always shows what the API actually stored.
2. **The URL identifies what is on screen.** Routes include IDs (`/versions/{VersionId:long}`, `/executions/{ExecutionId:long}`), and filters are query-string parameters (`[SupplyParameterFromQuery]`). A refresh, a lost circuit, or a bookmark returns to the same view.
3. **Reference data is cached per circuit.** A scoped `ReferenceData` service loads the six lookup lists once and provides by-ID name lookups for display. It has an explicit `RefreshAsync()`, called after reference-data edits.
4. **Workflow services exist only where one user action needs several API calls in a fixed order.** The MVP has exactly two: `StartExecutionWorkflow` (create the execution, then write and register the delivered prompt) and `ArtifactCaptureWorkflow` (stream, hash, write, register). They hold client-side sequencing, not domain rules, and are unit-testable without the UI.
5. **N+1 reads are acceptable** at MVP volume. **[Optional, A4]** `GET /executions?experimentVersionId=` and `GET /score-observations?experimentVersionId=` would make the version-level tabs one call each.

**Alternative considered:** Fluxor or other Redux-style stores. They solve frequently changing state shared across pages. ShepherdWeb's state is "the API resource I'm looking at", and a global store would create a second, possibly stale, authority.

*Blazor note — DI lifetimes in Interactive Server.* **Scoped means per circuit (roughly one browser tab), not per HTTP request.** **Singletons are shared by every circuit** and must never hold user-specific state. Keeping to this now is what makes adding authentication later safe. Generated clients are registered as typed clients through `IHttpClientFactory`.

*Blazor note — errors.* An unhandled exception in an Interactive Server component **ends the circuit**. The layout wraps page content in `<ErrorBoundary>` so a failure shows an inline panel instead. Expected API failures are handled explicitly (AD-6) and never reach the boundary.

---

### AD-5 — Forms and validation: `EditForm`, generated annotations, and API errors mapped into the form

**[Decision]**

- Forms use the built-in `EditForm`, `DataAnnotationsValidator`, and `Input*` components.
- **Simple create and edit forms bind directly to the generated request types.** NSwag carries over `[Required]` and `[StringLength]`, so length limits are checked as the operator types, with no duplicated rules.
- **A form-specific edit model is used only when the form differs from the contract** (for example, timestamps entered in a UI-friendly format, AD-10). Each has a single explicit `ToRequest()`.
- **Server errors are mapped into the form.** A 400 `ValidationProblemDetails` is added to the form's `EditContext` through a `ValidationMessageStore`, keyed by field name (case-insensitive). Unmatched errors go to the validation summary. Brief §7.3 holds: client checks give fast feedback, and the API is the final authority.
- Submit buttons are disabled while a request is in flight, which prevents double submission of insert-only resources (Execution, Artifact).

*Blazor note:* `EditForm` keeps an `EditContext` that records changed fields and validation messages. Server errors go into the same context, so the UI has one validation pipeline whichever side reported the error.

---

### AD-6 — Handling API failures in one place

**[Decision]** A single `ApiErrorMapper` converts NSwag's `ApiException` / `ApiException<ProblemDetails>` into a small `ApiProblem` result that every page displays the same way:

| API response | ShepherdWeb behavior |
|---|---|
| 400 Validation Failed | Field errors in the form (AD-5). |
| 404 | "No longer exists", with a link to the parent. |
| 409 with `code` `version-locked`, `design-frozen` or `execution-run-facts-frozen` (revision 4.3; before V10 the title "Record Locked", still accepted when a body has no `code`) | Explains that the version is locked, its configuration is frozen by its first execution, or the execution has finished, and reloads the page so editing controls disappear. |
| 409 otherwise | A specific message where the cause is known, chosen by `code` (for example `duplicate-observation`). Otherwise ShepherdAPI's own title and `detail`, which name the cause (for example "Finalization Failed: Execution Not Ended" with the execution IDs, or "Invalid Rule Configuration" with the phase's weights). `retryable-conflict` is marked retryable. |
| 5xx | Generic error showing the API's `traceId`, for finding the matching entry in the ShepherdAPI logs. |
| Connection refused / timeout | "ShepherdAPI is unreachable at {BaseUrl}", plus a layout-level API-status indicator (a `GET /api/statuses` check on first load). |

Every failed call is also logged through `ILogger`, with method, path, status, and `traceId`.

---

### AD-7 — The UI reflects the lifecycle but never decides it

ShepherdWeb **reads** the lifecycle state and changes what it offers, so the operator rarely runs into a 409. ShepherdAPI remains authoritative, and a 409 is still handled (AD-6). **[Decision]**

| State | UI behavior |
|---|---|
| Version `Draft`, no executions | All configuration is editable. **Start execution** is offered once the version has at least one iteration. **Lock** is on the Overview (with confirmation); a read-only **Review configuration** page is linked from it. |
| Version `Draft` with executions | **Configuration frozen** (§3 rule 3): configuration tabs are read-only and a banner explains why. Executions can be started; ended executions can be evaluated. A **Score observations** tab appears once any execution has ended. **Lock** stays available on the Overview, disabled while any execution is in progress. ShepherdAPI also enforces the freeze (`design-frozen`, A1, revision 4.3). |
| Version locked (`LockedUTC` set) | **Final.** Everything is read-only, with a banner showing the lock time: configuration, executions, artifacts, scores and evidence. Start execution is not offered. Status can still change (for example to `Retired`). |
| Version status `Retired` (not locked) | Start execution is not offered (Q3). Everything else follows the rows above. |
| Status name and `LockedUTC` disagree (e.g. status `Locked`, no `LockedUTC`) | A warning explaining the mismatch. Lock state is always taken from `LockedUTC`. The owner chose this handling at the start of slice 1. Start execution is not offered while the status says `Locked` (Q3). |
| Execution in progress (no `EndUTC`) | **Hands-off view.** It shows what is running (iteration, agent, phase, attempt, start time) and the delivered prompt with **Copy prompt** (start-up plumbing). The primary action is **Execution ended**. The only other action is a deliberately secondary **Deliver additional prompt (intervention)…** for the exceptional case (AD-8). No artifact capture, scoring, or editing is offered. |
| Execution ended (`EndUTC` set) | **Evaluation view.** Run facts are read-only. Artifact capture, scoring, and evidence linking are offered here until the version is locked. |
| Fields that identify a record (execution: iteration, agent, phase, attempt, start; parameter name; every artifact field) | Editable only on the create form, read-only everywhere else. |
| Deleted ScoreRule | Gone: ShepherdAPI deletes rules physically (Issue #7, revision 4.3). Before then, a retired rule (`RowState='D'`) was hidden from configuration lists. |

**Status selection:** for an unlocked version the status control never offers `Locked` (that is reached only through the Lock action: ShepherdAPI sets `LockedUTC` through finalization, and ShepherdWeb then sets the status in a second call, owner decision D1, revision 4.3); for a locked version it never offers `Draft`. If that second call fails, the Overview says the version is locked and the status still needs setting, and the lock is never retried; the status/lock mismatch warning also appears until the status is changed. A current status that is not offerable (an existing mismatch) is still shown as selected, so the control never silently misreports it. That keeps `StatusID` and `LockedUTC` from disagreeing. It is a UI guard, not a domain rule. **[TBD]** Whether ShepherdAPI should also enforce it.

**Configuration review:** the Overview links to a **Review configuration** view: a single read-only page showing the version, every iteration with its full prompt text, the parameters, and the score rules grouped by phase. It exists so the operator can check the whole configuration in one place, especially before the first execution, which freezes it. The **Lock** action itself is on the Overview (revision 4). **Lock preconditions (revision 4.3, owner decision D2):** ShepherdAPI V10 decides them. Its finalization refuses to lock unless the version has executions, every execution has ended and has an observation, every execution's phase has rules and every phase with rules has an observation, and each phase's rule weights total 100.00; it then snapshots every score. ShepherdWeb disables Lock while any execution is in progress and otherwise shows ShepherdAPI's title and detail when it refuses. A readiness checklist before confirming is deferred.

**Irreversible actions require an explicit confirmation that says what will happen:**

- **Lock version:** offered only when no execution is in progress. Shows the version, counts of iterations and executions, that ShepherdAPI first checks the version is complete and changes nothing if not, that every score is copied into a permanent snapshot, that the status is then set to `Locked`, and the text "Everything becomes permanent: configuration, executions, artifacts, scores and evidence. No further executions can be started. This cannot be undone through ShepherdWeb or ShepherdAPI."
- **Start the first execution:** also states that starting freezes the version's configuration.
- **Execution ended:** "End time, intervention count, and outcome notes become permanent."
- **Register artifact:** a review step shows hash, size, and type. Artifacts cannot be changed or deleted.
- **Delete score rule (before the first execution only; revision 4.3, replacing Retire):** "The rule is removed from this version. Its phase and category become free, so a new rule can be added for them." A mistaken rule is corrected by deleting it and adding it again (B4 resolved by ShepherdAPI Issue #7). Editing a rule in place through `PUT /api/score-rules/{id}` is deferred (owner decision D4).

**Reference-data edits (F7, owner decision N4):** the edit pages for Phase, ScoreCategory, Status, ArtifactType, and Rater permanently show "Changes apply retroactively to every experiment version, including locked ones."

---

### AD-8 — Experimental integrity: every delivered prompt is registered as an artifact before it is released

This addresses brief §4.3.

**Principle — register before release.** ShepherdWeb **never makes prompt text available for delivery (copying to the clipboard) until that exact text has been written to the artifact store and registered with ShepherdAPI as an `Artifact` of the execution it is delivered to.** What the operator copies comes from the registered text, not from an editable field.

**[Decision] The "Start execution" workflow:**

1. **Before the gates open** (no API writes yet): on a version that is not locked and not `Retired`, the operator selects an iteration, agent, and phase. ShepherdWeb fills in defaults (§8.2).
2. A **review panel** shows the exact prompt text, its UTF-8 byte length, and its SHA-256, next to the execution fields. The text is pre-filled from the iteration and can be edited for this execution only (see "The delivered prompt defaults to `Iteration.Prompt`…" below). This is the operator's last chance to check everything, because nothing about the execution can be changed after Start.
3. **Start** (the gates open) runs `POST /api/executions` (StartUTC = now, UTC). ShepherdWeb then writes the prompt bytes once to the artifact store (AD-9) and runs `POST /api/artifacts` with ArtifactType `DeliveredPrompt`, `MimeType = text/plain; charset=utf-8`, and `OriginalURI = urn:goodshepherd:iteration:{IterationID}:prompt`, which records where the text came from (for a prompt edited for this execution, `urn:goodshepherd:execution:{ExecutionID}:prompt`).
4. Only after registration succeeds does **Copy prompt** become available, directly under the Start button. The operator copies it and pastes it into the agent. That is the plumbing needed to start a manually operated agent (§3 rule 7), and it takes seconds, so `StartUTC` is the moment Start was clicked. Copying uses browser JS interop (`navigator.clipboard.writeText`); localhost is a secure context, so the clipboard API is allowed.
   *Why two clicks, not one:* browsers only allow clipboard writes shortly after a user gesture. Start involves two server-side API calls and a file write, which can outlast that window. A separate Copy click always works. **[Deferred]** If agents are later started programmatically (brief §3.7), this step disappears, and register-before-release still applies unchanged.
5. If step 3 partly fails (execution created, artifact not registered), the Start page **stays put**, states "Execution #N was created. Its start prompt was not registered" with the error, and offers **Retry registration** using the exact text and provenance still held in memory (revision 4.2). If the browser could not save the pending prompt either, it also says the text is held only on that page. Copying stays blocked until registration succeeds. A later retry from the execution page uses the saved pending prompt (below). Retries are idempotent: ShepherdWeb reuses an existing artifact only if it has the same type, the same SHA-256 **and the same `OriginalURI`**.

**Byte-exactness:** UTF-8 without BOM. Line endings are kept exactly as stored, with no normalization. The hash covers exactly the bytes written. *Found in slice 1:* some existing prompts use `\r\n` line endings, and a browser textarea turns line endings into `\n` as soon as the text is typed in. Editing such a prompt in Draft therefore changes its line endings (the editor's live hash shows it). This is harmless to integrity, because it can only happen before locking and what is stored is what will be delivered, but it is why ShepherdWeb never re-saves a prompt field the operator didn't edit.

**The delivered prompt defaults to `Iteration.Prompt`, and may be edited for one execution — [Owner decision, revision 4.1].** An iteration's prompt is the default, common prompt for the executions of that iteration. Because configuration freezes when the first execution exists (§3 rule 3), `Iteration.Prompt` cannot change once any execution has used it. ShepherdWeb enforces this in the UI today; **A1** would make ShepherdAPI enforce it. Executions are independently scorable units, and nothing assumes equal numbers of them per agent, iteration or phase.

*Execution-specific prompt.* Before Start, the review panel (step 2) is an editor, pre-filled with the iteration's prompt. The operator may edit it **for this execution only**; the iteration is never written. Whatever the editor holds when Start is clicked is the prompt that is registered, and from that moment it is immutable, historical evidence. The rules:

- **The artifact is the record.** No prompt text is added to `Execution` (the API contract and database are unchanged). The registered `DeliveredPrompt` artifact, with its hash and timestamps, is the authoritative statement of what was delivered.
- **Provenance says which it is.** Unedited, the artifact is exactly as before: `OriginalURI = urn:goodshepherd:iteration:{IterationID}:prompt`, `OriginalName = iteration-{IterationID}-prompt.txt`. Edited, it is `OriginalURI = urn:goodshepherd:execution:{ExecutionID}:prompt`, `OriginalName = execution-{ExecutionID}-prompt.txt`. A prompt whose text is identical to the iteration's is the iteration's prompt, however the operator got there (exact, ordinal comparison).
- **Byte-exactness.** An untouched prompt is taken from the iteration itself, never from the textarea, so its line endings are preserved. An edited prompt is whatever the textarea holds, which can normalize `\r\n` to `\n` (see above). A blank prompt cannot be started.
- **Changing the iteration** on the Start page discards an edit and shows the new iteration's prompt, with a visible message saying so. Nothing has been recorded yet.
- **Retry reproduces exactly what was decided at Start (revision 4.2).** Immediately after the execution is created, and before registration is attempted, a *pending start prompt* is kept as a browser draft (`execution:{id}:start-prompt`, AD-11). It records the execution ID, iteration ID and `StartUTC` of the execution record, the `OriginalURI`/`OriginalName` decided at Start, the SHA-256, and the exact text. The execution page uses it only if all three identity fields match the loaded execution and the text still hashes to the saved SHA-256; it then **shows and registers exactly that text with the saved provenance**, never re-deciding it, and offers **no editing**. The draft is deleted once registration succeeds. It is a safety net, not a gate: if browser storage is unavailable, Start still proceeds.
- **No substitute when the Start prompt cannot be established (owner decision).** If no usable pending start prompt exists (none saved, another browser, a format that does not identify its execution record, a different record with the same ID after a database restore, or text that no longer matches its hash), the page states that observed fact, registers **nothing**, offers no substitute or reconstruction, and keeps Copy blocked. The incomplete execution is preserved as it is; to proceed, the operator starts a new attempt (and may record what happened in the incomplete execution's outcome notes).
- **Interventions are unchanged.** Additional prompts delivered during an exceptional intervention keep their own mechanism and provenance (`…:additional-prompt`).

The per-execution artifact is required either way, because brief §4.3 makes every delivered prompt an artifact in its own right. It also gives each execution a self-contained, hash-verified record.

**Interventions: exceptional, preserved, not yet modeled (§3 rules 5–6).** The design records only what current evidence requires and **introduces no intervention taxonomy, types, or workflow**:

- **The fact that an intervention happened** is preserved with what the schema already has: `InterventionCount` and `OutcomeNotes`, both written once when the execution ends (§8.2). `InterventionCount` defaults to **0**, since hands-off is the norm. When it is above 0, the ended-execution confirmation asks the operator to describe what happened in `OutcomeNotes`. This is a prompt, not a requirement.
- **The only exceptional-path mechanism** exists to meet brief §4.3: if an intervention delivers any further prompt text to the agent, that text is a delivered prompt. **Deliver additional prompt (intervention)…** on the in-progress page registers it as another `DeliveredPrompt` artifact (register before release, the same code as at start) before it can be copied. Its `ReceivedUTC` records when it happened. Nothing else is captured during the run.
- ShepherdWeb does not link a delivered-prompt artifact to a particular intervention, does not calculate `InterventionCount` from artifacts, and does not classify interventions. **[TBD]** Every one of those depends on how interventions actually look in practice. The inspection views (§8.4) show `InterventionCount`, `OutcomeNotes`, and the execution's delivered prompts in time order. That gives the evidence needed to decide later.

**Role contract (revision 4.2).** A delivered prompt's evidentiary role is read **only** from its exact `OriginalURI` (ordinal comparison), defined in one place (`PromptProvenance`):

| Role | `OriginalURI` |
|---|---|
| Start prompt, unedited | `urn:goodshepherd:iteration:{Execution.IterationID}:prompt` |
| Start prompt, edited for this execution | `urn:goodshepherd:execution:{ExecutionID}:prompt` |
| Intervention prompt | `urn:goodshepherd:execution:{ExecutionID}:additional-prompt` |
| Anything else | No recognised role: listed as such, never treated as the start prompt |

Timestamps order lists; they never decide a role. With no start-role artifact the start prompt is "not registered"; with more than one, the page states how many and releases none. **Deliver additional prompt** is unavailable until a start prompt is registered. Identical bytes registered under different roles are different evidence.

**Verified release (revision 4.2).** A delivered prompt is displayed or copied only after its stored file is read once and those bytes are hashed and matched against the artifact's recorded SHA-256. Otherwise the page states the observed fact (stored file missing, not in this store, or not matching the recorded SHA-256) and releases nothing. The `/artifact-files` view link is labelled "not verified" and does not claim verification.

**Consistency check during inspection:** for the single start prompt whose provenance names this execution's iteration, the page compares its SHA-256 with the hash of the current `Iteration.Prompt`. If they differ it states only the observed fact: "The registered start prompt (SHA-256 …) differs from this iteration's current prompt (SHA-256 …)". **No cause is inferred** (revision 4.2). A start prompt whose provenance is the execution's own intentionally differs; the page states "Provenance: edited for this execution before Start".

**What changed from revision 1:** the "prompt drift" feature (side-by-side comparison, warnings in the prompt editor) is not needed while configuration is frozen from the first execution, so all that remains is the one-line consistency check above. Until **A1** is implemented the freeze is UI-only, and the consistency check is what would reveal a prompt changed through another client. Owner answer N8 ("just the prompt") is superseded by Q6: the freeze covers all configuration.

---

### AD-9 — Artifact capture and the artifact store

**[Owner decision B1/B3; Decision on mechanics]** Artifact bytes live on the local machine. ShepherdWeb captures them itself, so the operator never types a SHA-256, byte length, or MIME type (brief §7.1):

1. The operator selects a file with `<InputFile>`, picks an ArtifactType (the last one used is pre-selected), and optionally edits the name.
2. On the server, ShepherdWeb **streams** the upload to a temporary file inside the store and computes SHA-256 and the byte count as it goes (`IncrementalHash`). `ReceivedUTC` is the time the upload started.
3. It moves the file to its final, **write-once** location: `{ArtifactStore:RootPath}/executions/{ExecutionID}/{SHA256}{ext}`. **An existing file is never overwritten.** If the target already exists, its hash is checked and the file is reused. `StoredUTC` is the time the move completed. `StoredURI` is the resulting `file:///` URI.
4. It shows a review step (AD-7) and runs `POST /api/artifacts`.

Delivered prompts go through the same store code (steps 2–4) with the text's UTF-8 bytes as input.

**When:** apart from delivered prompts, artifacts are captured **after the execution ends**, as part of evaluation (§3 rule 5). The in-progress view does not offer upload.

**Inspection support:** a minimal ASP.NET Core endpoint (`GET /artifact-files/{artifactId}`) streams a stored artifact back for viewing. It resolves `StoredURI` through the API and **rejects any path outside the store root** to prevent path traversal. **Verify integrity** re-hashes the file and compares it with the recorded SHA-256.

**Size limit (owner decision N7):** `ArtifactStore:MaxUploadBytes` defaults to 50 MB.

*Blazor note:* `InputFile` in Interactive Server sends the file over the circuit in chunks. `OpenReadStream(maxAllowedSize)` has a default limit of **500 KB**, which is intentionally conservative, so ShepherdWeb passes the configured limit.

**[Deferred]** Registering large files *in place* by path (hashing where they are, with `StoredURI = OriginalURI`). The local-machine decision makes this feasible later. Build it if uploads prove inconvenient.

---

### AD-10 — Handling time: UTC in, UTC out, labeled everywhere

**[Decision]**
- **Outgoing:** every timestamp sent to ShepherdAPI is a `DateTime` with `Kind=Utc`. Because of F4, a local-kind value would be stored as *server-local* time. A small `Utc` helper is the only place conversions happen, and it has unit tests.
- **Incoming:** every received timestamp is treated as UTC (`DateTime.SpecifyKind(..., Utc)`).
- **Display:** UTC with an explicit `UTC` label (`2026-09-25 14:03:12 UTC`), with local time shown secondarily using the machine's time zone. The operator and server share a machine (B1).
- **Entry:** defaults to "now" through **Now** buttons (start, end, observed). Manual entry is labeled as UTC.
- **[Recommendation R3]** ShepherdAPI should normalize timestamps to UTC so every client is safe.

---

### AD-11 — Client-local state: preferences and drafts only, in browser storage

| Need | Mechanism |
|---|---|
| **Preferences**: last-used rater, agent, artifact type, and "current version" | Browser storage via the built-in `ProtectedLocalStorage`. No new dependency. |
| **Recoverable drafts**: unsaved prompt text (during configuration) and outcome notes (when the execution ends). Observation notes are short and saved row by row, so they have no drafts **[Deferred]** until evidence shows they are lost in practice | `ProtectedLocalStorage`, keyed by resource, saved shortly after typing stops, cleared on a successful save. A page with a draft shows "Unsaved draft from {time} — restore / discard". A draft is **never** submitted automatically. |

**Why:** it protects long text against a dropped circuit (AD-1) and cannot be confused with domain data (brief §3.4).

*Blazor note:* browser storage is reached through JS interop, which is **only available after the first interactive render**, so reads happen in `OnAfterRenderAsync(firstRender: true)`. `ProtectedLocalStorage` encrypts with ASP.NET Core Data Protection, whose keys persist in the Windows user profile by default.

---

### AD-12 — UI approach: Bootstrap from the template, built-in components, QuickGrid, no component library

**[Decision]**
- Use the **Bootstrap CSS** that comes with the Blazor template, plus a small site stylesheet. There is no theming system (brief §5.2).
- Use the built-in `Input*` components, `NavLink`, `<ErrorBoundary>`, and **QuickGrid** (`Microsoft.AspNetCore.Components.QuickGrid`, first-party) for sortable and filterable lists.
- Write a few shared components, each used in several places: `ConfirmDialog`, `LockBanner`, `UtcTimestamp`, `Sha256Display`, `ProblemDisplay`, `DraftRestoreBar`, and `LookupSelect`.

**Prompt editor:** a large monospace `<textarea>` with live character, line, and byte counts and the live SHA-256, plus **"start from another iteration's prompt"** (copy text from any iteration of any version into the editor). That is brief §7.1 reuse without new persistence or lineage. **[Deferred]** A structured section composer and file-based templates, until real prompts show a repeated structure worth helping with (brief §7.2).

**Alternatives considered:** MudBlazor, Radzen, Fluent UI Blazor. Capable, but each brings a large component and theming surface and its own upgrade cycle. None of the MVP screens needs more than forms, tables, and dialogs.

---

### AD-13 — Configuration

**[Decision]** Strongly typed options (`IOptions<T>` with `ValidateDataAnnotations().ValidateOnStart()`), so a misconfigured app fails at startup:

| Key | Purpose |
|---|---|
| `ShepherdApi:BaseUrl` (`http://localhost:5231`), `ShepherdApi:TimeoutSeconds` (30) | API location: ShepherdAPI's default `http` launch profile. Server-to-server over loopback needs no TLS. |
| `ArtifactStore:RootPath`, `ArtifactStore:MaxUploadBytes` (default 50 MB) | AD-9. |
| `Conventions:DraftStatusName` (`Draft`), `Conventions:LockedStatusName` (`Locked`) | The default for new versions, and the status ShepherdWeb sets right after a successful Lock (owner decisions N6, D1). Lock no longer requires the `Locked` status to exist; without it, the status is left unchanged. |
| `Conventions:DefaultExecutionStatusName` (`Draft`) | `StatusID` for new executions (owner decision N10). |
| `Conventions:DeliveredPromptArtifactTypeName` (`DeliveredPrompt`) | The one artifact type ShepherdWeb assigns by itself (AD-8). All other artifact types are chosen by the operator. |

Names are looked up against reference data when first used. If a configured name does not exist, the action shows an actionable message ("Create ArtifactType 'DeliveredPrompt' in Reference Data"). These are ShepherdWeb conventions. ShepherdAPI still hard-codes no status names.

---

### AD-14 — Hosting and security before authentication exists

**[Decision]**
- ShepherdWeb runs on the operator's workstation, next to ShepherdAPI. **Kestrel listens on localhost only.**
- There is no authentication (brief §2.7), and ShepherdWeb can lock versions and write evidence, so listening on localhost is the only access control. **Exposing ShepherdWeb on a network must wait until authentication is added.**
- **Keeping future authentication possible (brief §2.8):** all API access goes through `IHttpClientFactory`, so token forwarding is a single `DelegatingHandler`. No user-specific state lives in singletons. The "current rater" is a preference that can later come from the signed-in user. `AuthorizeRouteView` and `[Authorize]` can be added without restructuring.
- **[Deferred]** Production deployment, containers, and TLS beyond the development certificate.

---

### AD-15 — Testing

**[Decision]**
- **Unit tests (xUnit):** UTC conversions; ProblemDetails-to-`EditContext` mapping; default derivation (next attempt number, next sequence); workflow services against fake client interfaces, including register-before-release ordering and retries, write-once store behavior, and the single run-facts write when an execution ends.
- **Component tests (bUnit):** lifecycle-aware rendering (Draft vs. Locked; in-progress hands-off view vs. ended evaluation view), confirmation dialogs, status-control guards, draft restore, and score min/max limits.
- **Manual end-to-end** against a local ShepherdAPI with a test database, using a written checklist per slice. **[Deferred]** Automated browser tests until the screens settle.

This follows the owner's working method: **Build → Test → Observe → Discover → Refine → Repeat.** The checklists and observed operator behavior are the evidence that will resolve the TBD items (§12).

---

## 7. Screens and Navigation

```
Top nav:  Experiments | Executions | Inspect | Reference Data          [API: ● connected]

/experiments                         Versions grouped by Name (UI grouping only; there is no
                                     "Experiment" entity: brief §5.3). Status, lock state,
                                     EffectiveDateUTC.
/versions/new
/versions/{id}                       Tabs: Overview (with Lock) | Iterations | Parameters |
                                     Score Rules | Executions | Score observations (appears once
                                     any execution has ended). In-progress executions are listed
                                     first, so one left running is easy to find.
/versions/{id}/review                Read-only configuration review
/versions/{id}/iterations/{iid}      Iteration + prompt editor (read-only once frozen)
/versions/{id}/executions/new        Prepare + Start (AD-8); not for Locked or Retired versions
/executions/{id}                     In progress: hands-off view (copy prompt, "Execution ended",
                                     secondary "Deliver additional prompt (intervention)…").
                                     Ended: evaluation view (artifacts, link to scoring)
/executions/{id}/scoring             Observation grid + evidence links (ended executions only)
/inspect/versions/{id}               Execution matrix (iteration × agent × phase)
/reference/{agents|phases|statuses|score-categories|raters|artifact-types}
```

---

## 8. Capabilities

### 8.1 Configuration (Draft)

- **Experiment versions.** Create: Name (choose an existing name or type a new one), VersionLabel, EffectiveDateUTC (default now), Description. Status is set to `Draft`. Edit: descriptive fields only, before the first execution. **Lock** (final) is on the Overview (AD-7). Status changes go through a separate control.
- **Iterations.** The next `Sequence` is filled in as max + 1. There is a prompt editor (AD-12). Delete is available before the first execution. **[Deferred]** Drag-and-drop reordering.
- **Parameters.** An inline editable table. `Name` cannot be changed after creation; renaming means delete and re-create, before the first execution.
- **Score rules.** A table grouped by phase (in phase order) showing category, weight, min/max, HigherIsBetter (from the category), and notes. **`ScoreRule.Active` is not shown or used anywhere (owner decision N2).** No weight totals are shown or calculated (owner decision N3, kept by D3), although ShepherdAPI requires each phase's weights to total 100.00 before an execution can start (`invalid-rule-configuration`) and before Lock; its message names the phase and totals. Create: phase and category chosen from lists, with pairs that already exist disabled, and min < max checked on the client. Delete (before the first execution): AD-7; it frees the pair (revision 4.3). Edit in place is deferred (D4).
- **Reference data.** Six small, explicit list-and-edit pages sharing `ConfirmDialog` and layout markup, with the retroactivity warning (AD-7). They are **not** a metadata-driven generic CRUD framework (brief §3.6).
- **Not included (owner decision N5, TBD):** cloning a version and recording lineage between versions. A new version is created empty. The only reuse is copying prompt text between iterations (AD-12). Revisit when repeated manual setup shows what is actually needed.

### 8.2 Conducting an execution (version not locked or retired): prepare, start, hands off, end

**Before the gates open.** Everything is chosen and reviewed here, and no API writes happen until Start:

| Field | Where the value comes from (brief §7.1) |
|---|---|
| Iteration | The context the operator navigated from |
| Agent | Selection from active agents; defaults to the last used |
| Phase | Selection in phase order; defaults to the last used for this iteration, otherwise the first |
| AttemptNumber | Derived: max(existing attempts for iteration + agent + phase) + 1. Editable before creation only |
| ModelUsed / InterfaceUsed | Defaults to `Agent.DefaultModel` / `Agent.DefaultInterface`. Set at creation |
| StatusID | `Draft` (owner decision N10). Not shown on the form |
| StartUTC | Now, when **Start** is clicked |
| Prompt | Pre-filled from the chosen iteration's prompt. Editable for this execution only; changing the iteration resets it (with a visible message). Not part of the execution record: it is registered as the delivered-prompt artifact at Start (AD-8) |

**Gates open.** **Start** creates the execution and registers the delivered prompt. **Copy prompt** is the start-up plumbing (AD-8).

**Hands off.** The in-progress page stays quiet. It asks for no input, has no timers or polling, and shows no evaluation controls. The operator waits for the agent. The only secondary action, **Deliver additional prompt (intervention)…**, exists for the exceptional case (AD-8).

**Execution ends.** **Execution ended** opens a short form, and submitting it writes the run facts in a single `PUT run-facts`:
- `EndUTC`: defaults to now. It can be edited (for example, to the moment the agent actually finished, if the operator noticed later) but must be ≥ `StartUTC`.
- `InterventionCount`: defaults to **0**.
- `OutcomeNotes`: free text, saved as a local draft while typing. The form asks for a description when the intervention count is above 0.
- `ModelUsed` / `InterfaceUsed`: re-sent unchanged from creation, because the contract replaces the full set.

This is followed by the AD-7 confirmation. The page then switches to the **evaluation view**.

**Evaluate.** Human activity resumes: capture artifacts (AD-9), score (§8.3), link evidence, and review.

**[Decision, brief §3.7]** ShepherdWeb never starts, drives, or monitors an agent. Handing the prompt over manually is the MVP boundary, and it is plumbing, not participation.

### 8.3 Scoring (evaluation, after the execution ends)

- Scoring is offered only for ended executions (`EndUTC` set) on versions that are not locked (owner decision Q5). ShepherdAPI enforces the lock; the ended-execution rule is UI-only until **A5**.
- The scoring page for an execution shows the **rater** (defaults to the last used) and a grid with **one row for each score rule whose phase matches the execution's phase** (every rule since revision 4.3; `Active` is ignored, as ShepherdAPI ignores it). Each row shows category, HigherIsBetter, weight, allowed range, a score input limited to [Min, Max] with step 0.01 (`decimal(5,2)`), and notes.
- Existing observations for the selected rater are loaded. Saving a row creates (POST) or corrects (`PUT content`) an observation. Each row has its own save status, so a failure affects only that row.
- **Evidence:** each observation has a checklist of the execution's artifacts with optional notes, which adds or removes `ScoreObservationEvidence` links. Only the same execution's artifacts are offered, which matches the API's check.
- **[Owner decision N3]** No weighted totals or composite scores are calculated.
- **Version-level view:** once any execution of a version has ended, the version page shows a **Score observations** tab listing every observation across its executions (iteration, agent, phase, attempt, rule, rater, score, evidence count), filterable, with links to each execution's scoring page. It reads one list per execution (**A4** would make it one call).

### 8.4 Inspection (bounded)

- **Version overview:** a matrix with iterations as rows and agent/phase as columns. Each cell lists attempts with start and end times (in-progress attempts marked), a visible **intervention marker** when `InterventionCount` > 0, and counts of artifacts and observations. Always scoped to one version.
- **Execution detail:** everything about one execution: iteration and prompt; the delivered-prompt artifacts in time order, with the consistency check on the first (AD-8); `InterventionCount` and `OutcomeNotes` shown prominently when interventions occurred; all artifacts with **Verify integrity**, observations with rule, category, rater, and evidence, and the legacy `ArtifactURI` / `EvidenceURI` fields shown read-only and labeled "legacy" (API §4.3).
- **Jump to ID:** enter an ExecutionID or ArtifactID to go straight to it.
- **[Deferred]** Exports, cross-version comparison, and charts.

---

## 9. Handling the Lifecycle Conflict (F1)

**[Owner decision, Q1 — revision 4]** The conflict found in revision 2 is resolved by adopting ShepherdAPI's existing lock semantics (§3). **R1 is withdrawn** (it had been approved as B5 in revision 3). The execution and scoring slice proceeds against the existing API.

- ShepherdAPI already enforces the final boundary (`LockedUTC`) and the run-fact freeze (`EndUTC`).
- ShepherdWeb enforces, in the UI only, the rules the API does not yet have: no new executions for `Locked`/`Retired` versions, and scoring only after an execution ends (**A5**, §11). The first-execution configuration freeze was in this list until ShepherdAPI V10 enforced it (**A1**, revision 4.3).
- Existing data such as "Pit Stop Agent Evaluation" is shown with its configuration frozen; the operator can lock it from the Overview once every execution has ended. (Revision 4 recorded it as status `Locked`, no `LockedUTC`, 15 executions. On 2026-10-23 it is status `Draft`, no `LockedUTC`, 16 executions, with Execution 9 still open.)

---

## 10. Implementation Sequence

Each slice produces something usable and is reviewed before the next begins.

| Slice | Content | Depends on |
|---|---|---|
| **0. Skeleton** | Blazor Web App (.NET 10, global Interactive Server, prerender off); pinned contract and NSwag client; options validation; layout, navigation, API status; `ApiErrorMapper`; `ErrorBoundary`; `ReferenceData` cache; UTC helpers; test project | — |
| **1. Configuration** | Version list, detail, create, edit; iterations with prompt editor and drafts; parameters; score rules; reference-data pages; configuration review; lock; status changes | — (score-rule correction was "retire with warning"; since revision 4.3, delete before the first execution) |
| **2. Execution and scoring** (revision 4 merges former slices 2 and 3) | Artifact store; prepare and start (register before release, copy prompt); hands-off in-progress view with the exceptional intervention path; "execution ended" form; evaluation-view artifact upload and registration; per-execution scoring grid with evidence links; version **Executions** and **Score observations** tabs; Lock on the Overview | — (existing API) |
| **3. Inspection** | Version matrix; execution detail; consistency and integrity checks; jump to ID | — |

No slice waits on ShepherdAPI changes. The server-side enforcement items (A1, A3, A5) and R6 (the .NET 10 upgrade) can proceed separately. **Revision 4.3:** A1 and A3 are implemented in ShepherdAPI V10; ShepherdWeb was brought up to that API on branch `v10-api-compatibility`.

---

## 11. Recommended ShepherdAPI Changes (for review; none are assumed)

| # | Change | Why (requirement) | Kind | Priority |
|---|---|---|---|---|
| ~~R1~~ | ~~Align lock enforcement with the revision 3 lifecycle.~~ **Withdrawn in revision 4** (owner decision Q1): ShepherdAPI's existing lock semantics were adopted instead. | — | — | Withdrawn |
| **A1** | **(Revision 4.2: ShepherdWeb must not remain the sole enforcement boundary for this invariant.)** **Freeze experiment-defining configuration once the first execution exists:** reject changes to version descriptive fields, iterations (create, update, delete), parameters (create, update, delete) and score rules (create, retire) when any execution exists for the version. Optionally also reject starting executions for `Retired` versions. | §3 rules 2–3 (Q1, Q3, Q6). Today only ShepherdWeb enforces it; any other client could change configuration that executions already used | Service rule | **Implemented in ShepherdAPI V10** (`design-frozen`; revision 4.3). The optional `Retired` start block is not implemented |
| **A3** (was R2) | Validate score observations: score within [Min, Max]; rule belongs to the execution's version; rule phase = execution phase; rule not retired | Validation authority belongs to the API (brief §7.3, F3); scoring begins in slice 2 | Service rule | **Implemented in ShepherdAPI V10** (revision 4.3; rules are no longer retired) |
| R3 | Normalize timestamps to UTC (UTC kind on read, conversion on write), or use `DateTimeOffset` | Correct UTC handling for every client (F4) | Service/serialization | Medium |
| R4 | Explicit OpenAPI `operationId`s | Readable generated clients (F5). Found in slice 1: without them NSwag emits overloads such as `IterationsGetAsync(long id)` and `IterationsGetAsync(long? experimentVersionId)`, so a list call with a plain `long` silently binds to the single-item overload. ShepherdWeb uses named arguments to avoid this. | Contract metadata | Low → Medium |
| **A4** (was R5) | `GET /executions?experimentVersionId=` and `GET /score-observations?experimentVersionId=` | The version Executions and Score observations tabs make one call per iteration and per execution (F8) | Query endpoints | **Optional efficiency improvement** |
| R6 | Upgrade ShepherdAPI to .NET 10 LTS | .NET 8 support ends 10 November 2026 (owner decision N9) | Platform | **As soon as practical** |
| **A5** (was R7) | Reject ScoreObservation creation for an execution whose `EndUTC` is not set | Scoring follows the end of the execution (§3 rule 9, Q5). ShepherdWeb only guards this in the UI | Service rule | **Identified server-side enforcement work.** Not implemented in this pass |
| **A2** | A way to change `Execution.StatusID` after creation | Only needed if execution status is to mean something (F9) | Endpoint or contract change | **Deferred** pending clarification of the future purpose of `Execution.StatusID` (Q2) |
| **A6** (revision 4.2) | An explicit evidentiary role for artifacts (a role field, or distinct artifact types per role) instead of the `OriginalURI` convention | Start-prompt identity must come from explicit provenance/role; today only ShepherdWeb defines and reads the convention (AD-8 role contract), and another client could write any URI | Contract/DDL change | **Tracked dependency.** Not implemented in ShepherdWeb |
| **A7** (revision 4.2) | Server-side uniqueness or idempotency for an execution's start prompt (for example, at most one start-role artifact per execution) | ShepherdWeb's duplicate check (read, then write) is not atomic; two concurrent retries could register two start prompts | Service rule (with A6) | **Tracked dependency.** ShepherdWeb shows more than one as an observed fact and releases none |
| **A8** (revision 4.2) | A server-recorded receipt time for artifacts | `ReceivedUTC`/`StoredUTC` are times observed by ShepherdWeb; ShepherdAPI records no row write time (API architecture §5.3) | Contract/DDL change | **Tracked dependency**, only if server-observed time is ever required |
| R8 | Declare the status codes ShepherdAPI actually returns: `201 Created` for every create (`CreatedAtAction`) and `204 No Content` for every DELETE (`[ProducesResponseType(...)]`) | Found in slice 1 (the 201 case only in live testing): the OpenAPI document declares `200` for every operation, so generated clients report every successful create and delete as an error **after the change was made**, which invites a retry and a duplicate. ShepherdWeb works around it with NSwag's `ProcessResponse` hook (`GeneratedClientCustomization.cs`); any other generated client would hit the same trap | Contract metadata | **Mostly done in ShepherdAPI V10** (revision 4.3): declared for artifacts, executions, iterations, parameters, score observations and evidence, and score rules. Still `200` for the creates on agents, artifact types, experiment versions, phases, raters, score categories and statuses, which ShepherdWeb still works around per client |

**Removed or changed since revision 1** (revision 4: R1 withdrawn; R2, R5 and R7 renumbered A3, A4 and A5 to match the owner's decisions; A1 and A2 added):
- **Old R1** (reject prompt edits once executions exist) is covered by **A1**, the first-execution configuration freeze.
- **Old R2** (filtered unique index for ScoreRule) became **TBD (B4)**, and was **resolved by ShepherdAPI Issue #7** (revision 4.3): physical deletion and an update endpoint before the first execution.
- **Old R4** (reference-data retroactivity policy) is **settled: a warning is enough** (N4).
- **Old R8** (server-side clone and lineage) is **TBD (N5)**.

---

## 12. Assumptions, TBD Boundaries, and Deferred Items

### Assumptions
- **A1** A single operator on the machine that runs ShepherdWeb, ShepherdAPI, and the artifact store (confirmed by B1).
- **A2** The operator delivers prompts by copy and paste into the agent's interface.
- **A3** The delivered prompt is `Iteration.Prompt` unless the operator edited it for that one execution before Start (AD-8, revision 4.1); either way, the registered artifact is the record. `Iteration.Prompt` itself cannot change once an execution exists, which follows from the first-execution configuration freeze. (Assumption numbers here are unrelated to the API work items A1–A5 in §11.)
- **A4** Data volume per version is tens of iterations and hundreds of executions, so N+1 reads are acceptable.
- **A5** The operator creates the conventional reference rows (`Draft`, `Locked`, `Complete`, `Retired` statuses; the `DeliveredPrompt` artifact type) on the reference-data pages. ShepherdWeb does not seed data.
- ~~**A6**~~ How §3 rule 4 is read. No longer an assumption: confirmed by the owner (B5).

### TBD boundaries (To Be Discovered)

Each item has a deliberate boundary in the design, and nothing assumes the answer.

| TBD | Current boundary in ShepherdWeb | Evidence that would resolve it |
|---|---|---|
| ~~**B4** Score-rule correction~~ | **Resolved in revision 4.3** (ShepherdAPI Issue #7): delete and add again before the first execution; edit in place deferred (D4) | — |
| **N5** Version cloning / lineage | New versions start empty; prompt text can be copied between iterations | Repeated manual setup effort across versions |
| What `Complete` means | Nothing: it is not an immutability boundary (Q3). `LockedUTC` is authoritative; `Retired` blocks new executions | How the operator actually closes out an experiment |
| ~~Lock preconditions~~ | **Resolved in revision 4.3:** ShepherdAPI's finalization decides them (AD-7); ShepherdWeb disables Lock while executions are open and shows ShepherdAPI's reason otherwise (D2) | — |
| Execution status after creation (F9, A2) | Always `Draft` (N10); `EndUTC` alone decides whether an execution has ended (Q2) | A defined future purpose for `Execution.StatusID` |
| Intervention representation | `InterventionCount` + `OutcomeNotes` when the execution ends; any additional delivered prompt registered as a `DeliveredPrompt` artifact; no types, links, or classification | Real interventions (such as the recent Codex case): what had to be preserved to interpret them, and what the count and notes failed to capture |

### Deferred (with the reason)
- **Agent invocation, automated prompt delivery, and progress monitoring** (brief §3.7). Register before release works the same whether a human or a program delivers the prompt.
- **Authentication and authorization** (brief §2.7). The way to add them later is kept open (AD-14).
- **Circuit state persistence**, a **structured prompt composer or templates**, **prompt diff views**, **registering artifacts in place**, **iteration reordering**, **exports**, **automated browser tests**.
- **Optimistic concurrency.** The DDL has no rowversion columns. With one operator, the only risk is two browser tabs, where the last write wins. Accepted and documented.

### Accepted Alpha limitations (revision 4.2, owner-approved)
- **Role by URI convention.** The start-prompt and intervention roles are carried by exact `OriginalURI` formats (AD-8 role contract), defined and parsed in one place. This is explicit provenance, but it is a ShepherdWeb convention, not an API-enforced field (A6).
- **Non-atomic duplicate check.** Under the single-operator assumption (A1 above), a concurrent double registration is not prevented. If it happens, the execution page states that more than one start prompt is registered and releases none; it never picks one (A7).
- **ShepherdWeb-observed artifact times.** `ReceivedUTC` and `StoredUTC` are recorded from ShepherdWeb's clock on the operator's machine, not by ShepherdAPI (A8).
- **Browser drafts after a database restore.** Pending start prompts left in a browser from before a restore are recognised as not belonging to the restored records (identity mismatch) and are never used; they are simply ignored.

---

## 13. Dependencies

| Dependency | Source | Why it is worth the cost |
|---|---|---|
| ASP.NET Core / Blazor (.NET 10) | Microsoft | The platform. |
| `Microsoft.AspNetCore.Components.QuickGrid` | Microsoft | Sortable and filterable tables without a component library. |
| `NSwag.ApiDescription.Client` (build-time) | NSwag (MIT) | Generates the typed client and models from the contract, as the API architecture requires. |
| bUnit (tests only) | bUnit (MIT) | The standard Blazor component-testing library. |

Deliberately **not** included: UI component libraries, state-management libraries, Kiota, SQLite or any local database, SignalR hubs or messaging, and diff libraries.

---

## 14. Questions

### Resolved

| # | Question | Owner decision |
|---|---|---|
| B1 | Artifact storage location | Local machine. |
| B2 | Interactive Server and §3.2 | Allowed. The framework-managed connection is not application-level real-time infrastructure. |
| B3 | Delivered prompt storage | Wherever `Artifact.StoredURI` specifies. |
| B4 | Score-rule correction | **TBD** until revision 4.3; resolved by ShepherdAPI Issue #7 (delete before the first execution). See D4. |
| N1 | Interventions | Exceptional; execution is hands-off. The fact must be preserved; representation is TBD (AD-8, revision 3). |
| N2 | `ScoreRule.Active` | Ignored entirely. |
| N3 | Weight totals / composite score | No. |
| N4 | Reference-data retroactivity | A warning is enough. |
| N5 | Lineage / cloning | **TBD.** Nothing built. |
| N6 | Status names | `Draft`, `Locked`, `Retired`, `Complete`. |
| N7 | 50 MB artifact limit | Yes. |
| N8 | Scope of old R1 | Just the prompt. Now covered by the lifecycle (AD-8). |
| N9 | ShepherdAPI platform | Upgrade to .NET 10 as soon as practical (R6). |
| B5 | Align ShepherdAPI's lock enforcement with the lifecycle (R1) | Approved in revision 3; **superseded by Q1 in revision 4** (R1 withdrawn). |
| N10 | Status for new executions | `Draft`. Because the API cannot change `Execution.StatusID` after creation (F9), every execution keeps `Draft` status. Execution state (in progress or ended) comes from `EndUTC`, never from the status. |
| Q1 | Lifecycle for the execution and scoring slice | Adopt ShepherdAPI's lock semantics: configuration immutable when the first execution exists; Lock is the final immutability boundary. **R1 withdrawn**; §3 rules 2 and 4 revised. |
| Q2 | What governs execution editability | `EndUTC` is authoritative. `StatusID` does not govern editability. A2 not implemented (deferred). |
| Q3 | Version status during executions | Stays `Draft`. Lock sets `Locked` (since revision 4.3, ShepherdWeb sets it right after the lock, D1). New executions blocked for `Locked` or `Retired` versions. `Complete` is not an immutability boundary; `LockedUTC` is authoritative. |
| Q4 | Artifacts in this slice | Yes: artifact upload and delivered-prompt artifacts, with the configurable local artifact store. |
| Q5 | When scoring is offered | Only after an execution has ended. |
| Q6 | Scope of the first-execution freeze | All experiment-defining configuration, not ScoreRules alone. |
| D1 | Status after Lock (revision 4.3) | ShepherdAPI's lock no longer sets the status. After a successful finalization, ShepherdWeb sets `Locked` in a separate call; `LockedUTC` stays authoritative. If that call fails, ShepherdWeb reports that finalization succeeded and the status still needs setting, and never retries the lock. |
| D2 | Lock readiness (revision 4.3) | Disable Lock while any execution is in progress; otherwise show ShepherdAPI's finalization failure. A readiness checklist is deferred. |
| D3 | Weight totals (revision 4.3) | N3 kept: ShepherdWeb shows no totals. ShepherdAPI's `invalid-rule-configuration` and `finalize-phase-weight-invalid` messages, which name the phase and totals, are shown as they are. |
| D4 | Score-rule correction (revision 4.3) | Delete replaces Retire. Editing in place (`PUT /api/score-rules/{id}`) is deferred. |
| D5 | Rehearsal (revision 4.3) | Rehearse the Pit Stop lock on an isolated copy of the database and API before doing it on the real `Experiments` database. |

### Blocking

None open. The execution and scoring slice proceeds against the existing API; A1, A3 and A5 are recorded server-side enforcement work, not dependencies. (A1 and A3 are implemented in ShepherdAPI V10, revision 4.3.)

### Non-blocking

None open.
