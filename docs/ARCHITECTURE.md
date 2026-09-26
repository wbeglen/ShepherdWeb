# ShepherdWeb Architecture — MVP

**Status:** Proposed, revision 3. Awaiting review. No implementation has started, and none will until this document is approved.
**Scope:** The MVP only. This document says what the MVP is allowed to build. It does not describe a final design.
**Audience:** The Good Shepherd owner/operator, and an experienced .NET engineer learning modern Blazor. Sections marked *Blazor note* explain decisions that are specific to Blazor.

### Revision history

| Rev | Change |
|---|---|
| 1 | Initial proposal. |
| 2 | Revised for the owner's lifecycle clarification (§3) and answers to B1–B4 and N1–N9 (§14). The main effects: a new lifecycle model (Draft → Locked → execution); a **conflict found between that lifecycle and ShepherdAPI's current lock enforcement** (F1, new blocking question B5); clone/lineage removed (TBD); `ScoreRule.Active` ignored; weight totals removed; prompt-drift handling simplified; run facts written once when an execution finishes; score-rule correction left as a TBD boundary. |
| 3 | Revised for the owner's execution-workflow clarification: **Configure → Review → Lock → Start → HANDS OFF → Ends → Evaluate**. While an execution runs, the UI assumes no routine human activity. Artifact capture and scoring move to after the execution ends. Intervention handling is reduced to what the existing schema already records (`InterventionCount`, `OutcomeNotes`), plus an exceptional path for preserving any additional delivered prompt. The intervention-specific artifact types from revision 2 are removed, and intervention representation is TBD. Manual prompt copying is treated as start-up plumbing. |
| 3.1 | Recorded owner decisions N10 (new executions get `Draft`) and B5 (R1 approved; the reading of "editable" confirmed). No open questions remain. |

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
2. **Conduct** executions: start them from locked configuration, stay out of the way while they run, record when and how they ended, then support evaluation (artifacts, scores, evidence).
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
| `ShepherdAPI/src/**`, `ShepherdAPI/tests/**` | commit `3881ed6` **plus uncommitted working-tree changes** (ScoreRule add/delete-only lifecycle with `RowState`). Found during slice 0: the committed `openapi.json` predates those changes, so ShepherdWeb's pinned contract was captured from the running working tree (see `contracts/SOURCE.md`). |

Several findings come from the service code rather than the contract, because what the API actually enforces matters more here than what it describes.

---

## 3. Experiment Lifecycle (as clarified by the owner)

This is the central invariant that the rest of the design depends on. It applies to the whole experiment version, not to prompts, iterations, or score rules individually.

```
   Draft ───────────────► Locked ───────────────────────────► Complete / Retired
   configure, review,     configuration immutable               (status changes; what
   correct, refine        executions may now run                 they freeze is TBD)
   NO executions          after the first execution starts:
                          only ScoreObservation and
                          ScoreObservationEvidence stay editable
```

**[Owner decision]**

1. **Configuration takes as long as the operator needs.** A version does not become executable just because it was created. In `Draft`, the version and its configuration (descriptive fields, iterations and prompts, parameters, score rules) can be created, reviewed, corrected, and refined.
2. **Nothing executes against a version until its configuration is complete and the version is `Locked`.**
3. **Once the first execution begins, the version's configuration is immutable.** Because executions require a locked version, configuration is in fact immutable from the moment of locking.
4. **After execution begins, only `ScoreObservation` and `ScoreObservationEvidence` remain editable.**
5. **An execution is hands-off.** It works like a horse race: all human preparation happens before the gates open. Once an execution starts, the operator waits for the agent to finish while the system records what happens. Human activity resumes only after the execution ends, with scoring, evidence, review, and analysis.
6. **Intervention is exceptional.** It is not part of the normal workflow, but it can happen (as in the recent Codex execution problem). When it does, **the fact that it happened must be preserved**, because it may matter when the execution is interpreted later. How interventions are represented in detail is **TBD**.
7. **Manual steps needed to start an agent are plumbing.** Copying and pasting the prompt is not evidence that the operator normally takes part in an execution.
8. The version statuses that matter are `Draft`, `Locked`, `Retired`, and `Complete`.

```
 Configure → Review → Lock │ Start Execution → HANDS OFF → Execution Ends │ Evaluate
 ───── human-paced ─────── │ ─── system records; human waits ────────────  │ ── human-paced ──
                           │  (exceptional: intervention, fact preserved)  │ artifacts, scores,
                           │                                               │ evidence, review
```

**[Owner decision, B5 — confirmed] How "editable" in rule 4 is read.** Rule 4 governs *correcting existing records*. *Recording* what happens while an execution runs is a different activity, and it has to remain possible, or no execution could be recorded at all:

| Activity after locking | Allowed? | Basis |
|---|---|---|
| Changing configuration (version descriptive fields, Iteration, Parameter, ScoreRule) | No | Rules 3–4 |
| Changing version status (Locked → Complete / Retired) | Yes | ShepherdAPI §5.3: status is exempt from the lock |
| Creating an execution (the gates open) | Yes, and only now | Rule 2 |
| Registering the delivered prompt at start, and any additional prompt delivered during an exceptional intervention | Yes. Artifacts are insert-only, so this records rather than edits | Brief §4.3 |
| Writing an execution's run facts (`EndUTC`, `InterventionCount`, `OutcomeNotes`) | Yes, **once**, when the execution ends (§8.2). Frozen afterwards. | ShepherdAPI §5.1 |
| After the execution ends: registering evaluation artifacts; creating and correcting ScoreObservation and ScoreObservationEvidence | Yes | Rules 4–5 |
| Anything else | No | Rule 4 |

**[TBD]** What `Complete` and `Retired` freeze, if anything. Examples: whether scoring may continue after `Complete`, or whether a `Retired` version may still start executions. Until evidence says otherwise, ShepherdWeb offers **Start execution** only when the version is locked *and* its status is `Locked` (a UI convenience, not a domain rule), and it offers scoring wherever the API allows it.

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
| **F1** | **ShepherdAPI's lock enforcement conflicts with the clarified lifecycle (§3), in both directions.** (a) The API **rejects** creating executions, artifacts, observations, and evidence links once `LockedUTC` is set (`RecordingService` lock guards; API architecture §5.3 treats locking as "the experimental record is final"). (b) The API **allows** executions on unlocked (`Draft`) versions. | Under the clarified lifecycle, **no execution can currently be recorded correctly.** Against a locked version the API refuses. Against a draft version it accepts something the invariant forbids. This is the most important finding in revision 2. It blocks the execution and scoring slices until resolved. See **B5** and **R1**. |
| F2 | ScoreRule has a unique constraint on `(ExperimentVersionID, PhaseID, ScoreCategoryID)` that **includes soft-deleted rows**, and there is no update endpoint. | A retired rule cannot be re-created for the same phase and category in the same version. Under the clarified lifecycle this matters only in `Draft`, where no observations can reference any rule. **[TBD, owner decision B4]** See AD-7 and §7.4. |
| F3 | `CreateScoreObservation` does not check that the score is within [Min, Max], that the rule belongs to the execution's version, that the rule's phase matches the execution's phase, or that the rule is active (`RowState='A'`). | ShepherdWeb restricts what can be picked and entered, but only as a convenience. The API should enforce this (R2, brief §7.3). |
| F4 | Timestamps are `DateTime`. Values read through Dapper have `Kind=Unspecified`, so the JSON has **no `Z` suffix**. Incoming values with a non-UTC offset are converted to the *server's local time* before being stored. | ShepherdWeb always sends UTC-kind values and treats every received timestamp as UTC (AD-10). R3 fixes this for all clients. |
| F5 | The OpenAPI document has no `operationId`s. | Generated client method names are awkward (AD-2, R4). |
| F6 | No CORS configuration. | Irrelevant with server-side calls (AD-1). |
| F7 | Reference data (Agent, Phase, ScoreCategory, Status, Rater, ArtifactType) is global, unversioned, freely editable, and not governed by any lock. | **[Owner decision, N4]** A permanent warning on the edit pages is enough for the MVP (AD-7). |
| F8 | Lists are scoped by foreign key only. There is no "all executions for a version" query, and `GET /score-rules` returns only non-retired rules. | Inspection makes one call per iteration. That is acceptable at MVP volume (AD-4, R5 if it proves slow). |
| F9 | `Execution.StatusID` is set at creation and **cannot be changed through the API**. There is no endpoint for it, and API §5.1 says it has no architectural meaning. | ShepherdWeb sets it once from a configured default (AD-13). **[TBD]** Whether execution status needs to change after creation. |
| F10 | ShepherdAPI targets .NET 8, whose support ends 10 November 2026. | **[Owner decision, N9]** Upgrade to .NET 10 as soon as practical (R6). |

What already works well for a client: RFC 7807 ProblemDetails errors, a distinct 409 "Record Locked", full-replacement PUTs with explicit mutable-field sets, a real OpenAPI contract, and a configuration aggregate (`ExperimentDesignService`) that **already** freezes everything at lock exactly as §3 requires.

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

**Why a pinned snapshot instead of fetching the live Swagger document:** builds are reproducible and do not need ShepherdAPI running. Contract changes show up as reviewable diffs in ShepherdWeb. Updating is a deliberate step: copy the file, rebuild, fix whatever no longer compiles. This matters in revision 2, because the lifecycle alignment (R1) will change API behavior. The contract shape may not change, but ShepherdWeb should record which API commit it was verified against.

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
5. **N+1 reads are acceptable** at MVP volume. **[Recommendation R5]** `GET /executions?experimentVersionId=` only if inspection proves slow.

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
| 409 **Record Locked** (title) | Explains that the configuration is locked or the execution has finished, and reloads the page so editing controls disappear. |
| 409 Conflict | A specific message where the cause is known (for example, "a parameter with this name already exists in this version"). Otherwise the API's `detail`. |
| 5xx | Generic error showing the API's `traceId`, for finding the matching entry in the ShepherdAPI logs. |
| Connection refused / timeout | "ShepherdAPI is unreachable at {BaseUrl}", plus a layout-level API-status indicator (a `GET /api/statuses` check on first load). |

Every failed call is also logged through `ILogger`, with method, path, status, and `traceId`.

---

### AD-7 — The UI reflects the lifecycle but never decides it

ShepherdWeb **reads** the lifecycle state and changes what it offers, so the operator rarely runs into a 409. ShepherdAPI remains authoritative, and a 409 is still handled (AD-6). **[Decision]**

| State | UI behavior |
|---|---|
| Version in `Draft` (no `LockedUTC`) | All configuration is editable. **Start execution is not offered.** A **Review configuration** view and the **Lock** action are available. |
| Version locked (`LockedUTC` set) | All configuration is read-only, with a banner showing the lock time. **Start execution** is offered while the status is `Locked` (§3, TBD for other statuses). Status can still be changed to `Complete` or `Retired`. |
| Version **not locked but already has executions** (found in slice 1: data recorded before the lifecycle was clarified, e.g. "Pit Stop Agent Evaluation", 15 executions) | Treated as **configuration frozen**, exactly like a locked version, because §3 rule 3 makes configuration immutable once execution has begun. A banner explains this and **Review and lock** stays available so the operator can make it permanent. UI guard only; once R1 is in place ShepherdAPI prevents new cases. |
| Status name and `LockedUTC` disagree (e.g. status `Locked`, no `LockedUTC`) | A warning explaining the mismatch. Lock state is always taken from `LockedUTC`. The owner chose this handling at the start of slice 1. |
| Execution in progress (no `EndUTC`) | **Hands-off view.** It shows what is running (iteration, agent, phase, attempt, start time) and the delivered prompt with **Copy prompt** (start-up plumbing). The primary action is **Execution ended**. The only other action is a deliberately secondary **Deliver additional prompt (intervention)…** for the exceptional case (AD-8). No artifact capture, scoring, or editing is offered. |
| Execution ended (`EndUTC` set) | **Evaluation view.** Run facts are read-only. Artifact capture, scoring, and evidence linking are offered here, wherever the API allows. |
| Fields that identify a record (execution: iteration, agent, phase, attempt, start; parameter name; every artifact field) | Editable only on the create form, read-only everywhere else. |
| Retired ScoreRule (`RowState='D'`) | Not shown in configuration lists. |

**Status selection:** for an unlocked version the status control never offers `Locked` (that is reached only through the Lock action, which sets `LockedUTC` and the status together, API §5.3); for a locked version it never offers `Draft`. A current status that is not offerable (an existing mismatch) is still shown as selected, so the control never silently misreports it. That keeps `StatusID` and `LockedUTC` from disagreeing. It is a UI guard, not a domain rule. **[TBD]** Whether ShepherdAPI should also enforce it. It is not part of the approved R1.

**Configuration review before locking:** locking is deliberate and human-paced (§3), so the version page has a **Review configuration** view. It is a single read-only page showing the version, every iteration with its full prompt text, the parameters, and the score rules grouped by phase. It exists so the operator can check the whole configuration in one place before locking. **[TBD]** Whether locking should have preconditions (for example, "every phase has at least one score rule"). ShepherdWeb shows no completeness warnings until there is evidence of what counts as incomplete.

**Irreversible actions require an explicit confirmation that says what will happen:**

- **Lock version:** shows the version, counts of iterations, parameters, and score rules, and the text "Configuration becomes permanent. Executions become possible. This cannot be undone through ShepherdWeb or ShepherdAPI."
- **Execution ended:** "End time, intervention count, and outcome notes become permanent."
- **Register artifact:** a review step shows hash, size, and type. Artifacts cannot be changed or deleted.
- **Retire score rule (Draft only):** "This phase and category cannot be given a new rule in this version" (F2). **[TBD, B4]** How score-rule mistakes should be corrected. Until evidence shows what is needed, retiring is the only correction the UI offers, and it states the consequence plainly.

**Reference-data edits (F7, owner decision N4):** the edit pages for Phase, ScoreCategory, Status, ArtifactType, and Rater permanently show "Changes apply retroactively to every experiment version, including locked ones."

---

### AD-8 — Experimental integrity: every delivered prompt is registered as an artifact before it is released

This addresses brief §4.3.

**Principle — register before release.** ShepherdWeb **never makes prompt text available for delivery (copying to the clipboard) until that exact text has been written to the artifact store and registered with ShepherdAPI as an `Artifact` of the execution it is delivered to.** What the operator copies comes from the registered text, not from an editable field.

**[Decision] The "Start execution" workflow:**

1. **Before the gates open** (no API writes yet): on a locked version, the operator selects an iteration, agent, and phase. ShepherdWeb fills in defaults (§8.2).
2. A **review panel** shows the exact prompt text, its UTF-8 byte length, and its SHA-256, next to the execution fields. This is the operator's last chance to check everything, because nothing about the execution can be changed after Start.
3. **Start** (the gates open) runs `POST /api/executions` (StartUTC = now, UTC). ShepherdWeb then writes the prompt bytes once to the artifact store (AD-9) and runs `POST /api/artifacts` with ArtifactType `DeliveredPrompt`, `MimeType = text/plain; charset=utf-8`, and `OriginalURI = urn:goodshepherd:iteration:{IterationID}:prompt`, which records where the text came from.
4. Only after registration succeeds does **Copy prompt** become available, directly under the Start button. The operator copies it and pastes it into the agent. That is the plumbing needed to start a manually operated agent (§3 rule 7), and it takes seconds, so `StartUTC` is the moment Start was clicked. Copying uses browser JS interop (`navigator.clipboard.writeText`); localhost is a secure context, so the clipboard API is allowed.
   *Why two clicks, not one:* browsers only allow clipboard writes shortly after a user gesture. Start involves two server-side API calls and a file write, which can outlast that window. A separate Copy click always works. **[Deferred]** If agents are later started programmatically (brief §3.7), this step disappears, and register-before-release still applies unchanged.
5. If step 3 partly fails (execution created, artifact not registered), the execution page shows "**Delivered prompt not registered**" with a retry, and copying stays blocked. Retries are idempotent: ShepherdWeb first checks the execution's existing artifacts for the same type and SHA-256.

**Byte-exactness:** UTF-8 without BOM. Line endings are kept exactly as stored, with no normalization. The hash covers exactly the bytes written. *Found in slice 1:* some existing prompts use `\r\n` line endings, and a browser textarea turns line endings into `\n` as soon as the text is typed in. Editing such a prompt in Draft therefore changes its line endings (the editor's live hash shows it). This is harmless to integrity, because it can only happen before locking and what is stored is what will be delivered, but it is why ShepherdWeb never re-saves a prompt field the operator didn't edit.

**The delivered prompt is `Iteration.Prompt` — [Decision].** Because configuration is frozen at lock and executions can only happen after lock (§3), `Iteration.Prompt` cannot change once any execution has used it, as long as the API enforces the lifecycle (R1). No substitution or composition happens when an execution runs. The per-execution artifact is still required, because brief §4.3 makes every delivered prompt an artifact in its own right. It also gives each execution a self-contained, hash-verified record.

**Interventions: exceptional, preserved, not yet modeled (§3 rules 5–6).** The design records only what current evidence requires and **introduces no intervention taxonomy, types, or workflow**:

- **The fact that an intervention happened** is preserved with what the schema already has: `InterventionCount` and `OutcomeNotes`, both written once when the execution ends (§8.2). `InterventionCount` defaults to **0**, since hands-off is the norm. When it is above 0, the ended-execution confirmation asks the operator to describe what happened in `OutcomeNotes`. This is a prompt, not a requirement.
- **The only exceptional-path mechanism** exists to meet brief §4.3: if an intervention delivers any further prompt text to the agent, that text is a delivered prompt. **Deliver additional prompt (intervention)…** on the in-progress page registers it as another `DeliveredPrompt` artifact (register before release, the same code as at start) before it can be copied. Its `ReceivedUTC` records when it happened. Nothing else is captured during the run.
- ShepherdWeb does not link a delivered-prompt artifact to a particular intervention, does not calculate `InterventionCount` from artifacts, and does not classify interventions. **[TBD]** Every one of those depends on how interventions actually look in practice. The inspection views (§8.4) show `InterventionCount`, `OutcomeNotes`, and the execution's delivered prompts in time order. That gives the evidence needed to decide later.

**Consistency check during inspection:** the execution detail page compares the *first* delivered-prompt artifact's SHA-256 with the hash of the current `Iteration.Prompt`. Under the lifecycle they must always match. A mismatch means the invariant was bypassed (for example, by direct database access, which API §5.3 says it does not detect). The check is one comparison, and it shows a visible warning if it ever fires.

**What changed from revision 1:** the "prompt drift" feature (side-by-side comparison, warnings in the prompt editor, and the old R1 "reject prompt edits once executions exist") is no longer needed. Locking before execution makes drift impossible through the API, so all that remains is the one-line consistency check above. Owner answer N8 ("just the prompt") is kept on record for the narrower rule, but R1's lifecycle alignment covers it.

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
| **Recoverable drafts**: unsaved prompt text (during configuration), outcome notes (when the execution ends), observation notes (during evaluation) | `ProtectedLocalStorage`, keyed by resource, saved shortly after typing stops, cleared on a successful save. A page with a draft shows "Unsaved draft from {time} — restore / discard". A draft is **never** submitted automatically. |

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
| `Conventions:DraftStatusName` (`Draft`), `Conventions:LockedStatusName` (`Locked`) | The default for new versions, and the status the Lock action sets (owner decision N6). |
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
/versions/{id}                       Tabs: Overview | Iterations | Parameters | Score Rules |
                                     Review | Executions (appears once locked; in-progress
                                     executions listed first, so one left running is easy to find)
/versions/{id}/iterations/{iid}      Iteration + prompt editor (read-only once locked)
/executions/start?iterationId=…      Prepare + Start (AD-8), locked versions only
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

- **Experiment versions.** Create: Name (choose an existing name or type a new one), VersionLabel, EffectiveDateUTC (default now), Description. Status is set to `Draft`. Edit: descriptive fields only, while in Draft. **Lock** follows the configuration review (AD-7). Status changes after locking go through a separate control.
- **Iterations.** The next `Sequence` is filled in as max + 1. There is a prompt editor (AD-12). Delete is available in Draft. **[Deferred]** Drag-and-drop reordering.
- **Parameters.** An inline editable table. `Name` cannot be changed after creation; renaming means delete and re-create, in Draft.
- **Score rules.** A table grouped by phase (in phase order) showing category, weight, min/max, HigherIsBetter (from the category), and notes. **`ScoreRule.Active` is not shown or used anywhere (owner decision N2).** No weight totals are shown or calculated (owner decision N3). Create: phase and category chosen from lists, with pairs that already exist disabled (F2), and min < max checked on the client. Retire: AD-7, with the F2 consequence stated. **[TBD, B4]** How score-rule mistakes should be corrected beyond that.
- **Reference data.** Six small, explicit list-and-edit pages sharing `ConfirmDialog` and layout markup, with the retroactivity warning (AD-7). They are **not** a metadata-driven generic CRUD framework (brief §3.6).
- **Not included (owner decision N5, TBD):** cloning a version and recording lineage between versions. A new version is created empty. The only reuse is copying prompt text between iterations (AD-12). Revisit when repeated manual setup shows what is actually needed.

### 8.2 Conducting an execution (Locked): prepare, start, hands off, end

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

- Scoring is offered only for ended executions (`EndUTC` set). That is a UI convenience following §3 rule 5. Whether the API should enforce it is R7.
- The scoring page for an execution shows the **rater** (defaults to the last used) and a grid with **one row for each non-retired score rule whose phase matches the execution's phase**. Each row shows category, HigherIsBetter, weight, allowed range, a score input limited to [Min, Max] with step 0.01 (`decimal(5,2)`), and notes.
- Existing observations for the selected rater are loaded. Saving a row creates (POST) or corrects (`PUT content`) an observation. Each row has its own save status, so a failure affects only that row.
- **Evidence:** each observation has a checklist of the execution's artifacts with optional notes, which adds or removes `ScoreObservationEvidence` links. Only the same execution's artifacts are offered, which matches the API's check.
- **[Owner decision N3]** No weighted totals or composite scores are calculated.

### 8.4 Inspection (bounded)

- **Version overview:** a matrix with iterations as rows and agent/phase as columns. Each cell lists attempts with start and end times (in-progress attempts marked), a visible **intervention marker** when `InterventionCount` > 0, and counts of artifacts and observations. Always scoped to one version.
- **Execution detail:** everything about one execution: iteration and prompt; the delivered-prompt artifacts in time order, with the consistency check on the first (AD-8); `InterventionCount` and `OutcomeNotes` shown prominently when interventions occurred; all artifacts with **Verify integrity**, observations with rule, category, rater, and evidence, and the legacy `ArtifactURI` / `EvidenceURI` fields shown read-only and labeled "legacy" (API §4.3).
- **Jump to ID:** enter an ExecutionID or ArtifactID to go straight to it.
- **[Deferred]** Exports, cross-version comparison, and charts.

---

## 9. Handling the Lifecycle Conflict (F1)

**[Owner decision, B5]** R1(a)–(d) is **approved** as the intended ShepherdAPI lifecycle. A version must be Locked before an execution can be created. Locking freezes experimental configuration. It does not prevent recording executions, artifacts, run facts, score observations, or evidence.

**[Decision]** ShepherdWeb is designed for this lifecycle (§3). It does not accommodate the API's current behavior.

- Slices 0 and 1 (skeleton, configuration, locking) work against the **current** API unchanged. The configuration aggregate already behaves as §3 requires.
- Slices 2 and 3 (execution, scoring) need R1 **implemented** in ShepherdAPI first. Until then, ShepherdWeb must not work around F1. Creating executions against Draft versions would record data that violates the invariant, and executions cannot be deleted.
- **Who changes ShepherdAPI:** R1 is carried out as a separate ShepherdAPI effort, following that project's own process (update its `docs/ARCHITECTURE.md` §5.3 first, then implement). This ShepherdWeb effort does not modify ShepherdAPI (brief §9.3). When R1 lands, ShepherdWeb refreshes its pinned contract snapshot and records the ShepherdAPI commit it was verified against (AD-2).

---

## 10. Implementation Sequence

Each slice produces something usable and is reviewed before the next begins.

| Slice | Content | Depends on |
|---|---|---|
| **0. Skeleton** | Blazor Web App (.NET 10, global Interactive Server, prerender off); pinned contract and NSwag client; options validation; layout, navigation, API status; `ApiErrorMapper`; `ErrorBoundary`; `ReferenceData` cache; UTC helpers; test project | — |
| **1. Configuration** | Version list, detail, create, edit; iterations with prompt editor and drafts; parameters; score rules; reference-data pages; configuration review; lock; status changes | — (score-rule correction stays at "retire with warning", B4 TBD) |
| **2. Execution** | Artifact store; prepare and start (register before release, copy prompt); hands-off in-progress view with the exceptional intervention path; "execution ended" form; evaluation-view artifact upload and registration | **R1 (approved) implemented in ShepherdAPI** |
| **3. Scoring** | Observation grid; evidence links | R1 implemented |
| **4. Inspection** | Version matrix; execution detail; consistency and integrity checks; jump to ID | — |

Slices 0 and 1 are not blocked by anything once this document is approved. The ShepherdAPI work for R1 (and R6, the .NET 10 upgrade) can proceed alongside them.

---

## 11. Recommended ShepherdAPI Changes (for review; none are assumed)

| # | Change | Why (requirement) | Kind | Priority |
|---|---|---|---|---|
| **R1** | **Align lock enforcement with the clarified lifecycle.** (a) `CreateExecution` requires the version to be locked. (b) Remove the lock guard from recording operations: execution creation, `run-facts` (the `EndUTC` freeze stays), artifact creation, observation create/update, and evidence add/remove. (c) Leave the configuration aggregate unchanged; it already freezes at lock. (d) Update ShepherdAPI `docs/ARCHITECTURE.md` §5.3 to the new meaning of `LockedUTC`: "configuration final, execution permitted". **[TBD]** What, if anything, later freezes recording (for example `Complete` or `Retired`) is left as an explicit open boundary, not decided here. | Lifecycle invariant (§3); F1 | Service rules + API architecture doc | **Approved (B5). Critical: implementation blocks slices 2–3** |
| R2 | Validate score observations: score within [Min, Max]; rule belongs to the execution's version; rule phase = execution phase; rule not retired | Validation authority belongs to the API (brief §7.3, F3) | Service rule | High |
| R3 | Normalize timestamps to UTC (UTC kind on read, conversion on write), or use `DateTimeOffset` | Correct UTC handling for every client (F4) | Service/serialization | Medium |
| R4 | Explicit OpenAPI `operationId`s | Readable generated clients (F5). Found in slice 1: without them NSwag emits overloads such as `IterationsGetAsync(long id)` and `IterationsGetAsync(long? experimentVersionId)`, so a list call with a plain `long` silently binds to the single-item overload. ShepherdWeb uses named arguments to avoid this. | Contract metadata | Low → Medium |
| R5 | `GET /executions?experimentVersionId=` | Fewer N+1 reads in inspection (F8) | Query endpoint | Low, only if needed |
| R6 | Upgrade ShepherdAPI to .NET 10 LTS | .NET 8 support ends 10 November 2026 (owner decision N9) | Platform | **As soon as practical** |
| R7 | Reject ScoreObservation creation for an execution whose `EndUTC` is not set | Evaluation follows the end of the execution (§3 rule 5). ShepherdWeb only guards this in the UI | Service rule | Low. For review; not needed for the MVP to work |
| R8 | Declare the status codes ShepherdAPI actually returns: `201 Created` for every create (`CreatedAtAction`) and `204 No Content` for every DELETE (`[ProducesResponseType(...)]`) | Found in slice 1 (the 201 case only in live testing): the OpenAPI document declares `200` for every operation, so generated clients report every successful create and delete as an error **after the change was made**, which invites a retry and a duplicate. ShepherdWeb works around it with NSwag's `ProcessResponse` hook (`GeneratedClientCustomization.cs`); any other generated client would hit the same trap | Contract metadata | Medium (worked around in ShepherdWeb) |

**Removed or changed since revision 1:**
- **Old R1** (reject prompt edits once executions exist) is covered by the new R1, because locking before execution makes it redundant.
- **Old R2** (filtered unique index for ScoreRule) is now **TBD (B4)**. Evidence to watch for: how often rules have to be corrected during Draft. Because no observations can exist in Draft, every option remains open, including relaxing the constraint, allowing updates in Draft, or physical deletion in Draft.
- **Old R4** (reference-data retroactivity policy) is **settled: a warning is enough** (N4).
- **Old R8** (server-side clone and lineage) is **TBD (N5)**.

---

## 12. Assumptions, TBD Boundaries, and Deferred Items

### Assumptions
- **A1** A single operator on the machine that runs ShepherdWeb, ShepherdAPI, and the artifact store (confirmed by B1).
- **A2** The operator delivers prompts by copy and paste into the agent's interface.
- **A3** The delivered prompt is exactly `Iteration.Prompt` (AD-8). This follows from the lifecycle.
- **A4** Data volume per version is tens of iterations and hundreds of executions, so N+1 reads are acceptable.
- **A5** The operator creates the conventional reference rows (`Draft`, `Locked`, `Complete`, `Retired` statuses; the `DeliveredPrompt` artifact type) on the reference-data pages. ShepherdWeb does not seed data.
- ~~**A6**~~ How §3 rule 4 is read. No longer an assumption: confirmed by the owner (B5).

### TBD boundaries (To Be Discovered)

Each item has a deliberate boundary in the design, and nothing assumes the answer.

| TBD | Current boundary in ShepherdWeb | Evidence that would resolve it |
|---|---|---|
| **B4** Score-rule correction | Retire with a stated consequence; nothing else | How often rules have to be corrected during Draft, and why |
| **N5** Version cloning / lineage | New versions start empty; prompt text can be copied between iterations | Repeated manual setup effort across versions |
| What `Complete` / `Retired` freeze | Start execution offered only while status is `Locked`; scoring wherever the API allows | How the operator actually closes out an experiment |
| Lock preconditions | None; the configuration review is informational | Versions locked with configuration that later proved incomplete |
| Execution status after creation (F9) | Always `Draft` (N10); UI state comes from `EndUTC` | Whether operators need execution status to change or mean something (that would need a new API endpoint) |
| Intervention representation | `InterventionCount` + `OutcomeNotes` when the execution ends; any additional delivered prompt registered as a `DeliveredPrompt` artifact; no types, links, or classification | Real interventions (such as the recent Codex case): what had to be preserved to interpret them, and what the count and notes failed to capture |

### Deferred (with the reason)
- **Agent invocation, automated prompt delivery, and progress monitoring** (brief §3.7). Register before release works the same whether a human or a program delivers the prompt.
- **Authentication and authorization** (brief §2.7). The way to add them later is kept open (AD-14).
- **Circuit state persistence**, a **structured prompt composer or templates**, **prompt diff views**, **registering artifacts in place**, **iteration reordering**, **exports**, **automated browser tests**.
- **Optimistic concurrency.** The DDL has no rowversion columns. With one operator, the only risk is two browser tabs, where the last write wins. Accepted and documented.

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
| B4 | Score-rule correction | **TBD.** Boundary in §12. |
| N1 | Interventions | Exceptional; execution is hands-off. The fact must be preserved; representation is TBD (AD-8, revision 3). |
| N2 | `ScoreRule.Active` | Ignored entirely. |
| N3 | Weight totals / composite score | No. |
| N4 | Reference-data retroactivity | A warning is enough. |
| N5 | Lineage / cloning | **TBD.** Nothing built. |
| N6 | Status names | `Draft`, `Locked`, `Retired`, `Complete`. |
| N7 | 50 MB artifact limit | Yes. |
| N8 | Scope of old R1 | Just the prompt. Now covered by the lifecycle (AD-8). |
| N9 | ShepherdAPI platform | Upgrade to .NET 10 as soon as practical (R6). |
| B5 | Align ShepherdAPI's lock enforcement with the lifecycle (R1) | **Approved.** R1(a)–(d) is the intended lifecycle: a version must be Locked before an execution can be created, and locking freezes configuration only. The reading of "editable" in §3 is confirmed. |
| N10 | Status for new executions | `Draft`. Because the API cannot change `Execution.StatusID` after creation (F9), every execution keeps `Draft` status. Execution state (in progress or ended) comes from `EndUTC`, never from the status. |

### Blocking

None open. **Remaining dependency, not a question:** slices 2 and 3 wait for the approved R1 to be implemented in ShepherdAPI (§9).

### Non-blocking

None open.
