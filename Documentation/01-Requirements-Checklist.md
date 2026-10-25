# Requirements Checklist

Initial assessment: 2026-10-22; field verification updated 2026-10-23. Implementation handoff updated 2026-10-24. Automated coverage is implemented; the user confirmed fresh-session manual acceptance passed on 2026-10-25.

## Functional Requirements And Acceptance Criteria

- [ ] **List people.** Retrieve the People collection with one GET and provide optional user-specified `$top`, `$skip`, and `$orderby` under D013. Do not add page parameters or continuation-link requests; no such API behavior is documented or was observed in the user's tests. Full retrieval has no client-imposed size limit or offset; intentional limits remain combinable with agreed field filters. Display the current entity key for selection; empty results are normal.
- [ ] **Order results.** D014/D015 accept one optional sort field at a time, ascending or descending, from `UserName`, `FirstName`, `LastName`, `Gender`, and `Age`. MiddleName and multi-key ordering are excluded. Do not silently append a tie-break key. Only UserName ordering has live evidence; no default order is chosen here.
- [ ] **Filter by one or more fields.** Support exact `UserName` and typed `Gender` equality, case-sensitive substring matching on `FirstName` and `LastName`, and user-selectable AND/OR. Demonstrate one-field and multi-field matches and a valid no-match result. A separate full-text search feature is not required. Provider behavior for LastName substring matching remains unverified.
- [ ] **Display a selected person.** Retrieve the selected string key and display the complete successful entity response body except top-level `@odata.context`. Preserve every other returned property, annotation, null, array, and nested value; do not limit the display to editable fields. Distinguish a missing person from a request failure.
- [ ] **Update details.** D020 accepts `UserName`, `FirstName`, `LastName`, `MiddleName`, and `Age`, excluding Gender after the user's manual checks returned 500. D016 accepts explicit null clearing of LastName/MiddleName/Age, distinct from unchanged input. Send only intended changes, report uncertainty accurately, and verify requested values by read-back in the same session before reporting success.
- [ ] **Test key functionality and availability.** The default suite covers application behavior and read-only live service availability under D011/D012, with distinguishable results. Local controlled responses alone are insufficient availability evidence. An unavailable/unusable service fails its availability check and the overall run, without turning passing local tests into behavior failures. Live availability does not prove update support.
- [ ] **Keep engineering proportionate.** Console interaction and HTTP behavior can be exercised separately without a live public service. Avoid a generic query engine, plugin architecture, or extra project layers unless an agreed requirement needs them.

The field lists, selectable combination rule, full-response details content, and interactive console workflow are approved. D005's match operations and case behavior were resolved during implementation; see the [decision record](decisions.md). Menus, prompts, list columns, and formatting are refined during implementation under D010, not treated as additional approval gates.

## Technical Constraints

- C# console application targeting .NET 10 and consuming an OData v4 service. D017 accepts Spectre.Console 0.57 and xUnit v3; D018 accepts one console project, one test project, and built-in HttpClient/System.Text.Json. Implementation uses SDK 10.0.401; see the exact dependency pins and runner below.
- The supplied URL is a service directory, not an API root. The application acquires its session from `https://services.odata.org/TripPinRESTierService/`; scope and evidence are in the [API contract](02-Verified-API-Contract.md).
- Use the metadata's `UserName` string key, not a numeric ID or a display name.
- Keep the acquired session-scoped service root consistent across reads, filters, and updates.
- Treat observed HTTP/JSON behavior and service-specific documentation as evidence; generic OData support is not proof that a feature works here.
- Do not turn an HTTP failure or an ineffective query into a misleading empty result or success message.

## Focused Test Acceptance

| Area                  | Minimum useful evidence                                                                                                                                                                                                                                                                                                                      |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Query construction    | Approved single/multi-field filters plus optional `$top`, `$skip`, `$orderby`; omission preserves complete-list capability and supplied limits/offsets are honored. D014/D015 allow one of five sort fields with a direction, not a hidden second key. Escape apostrophes before URI encoding; query-like filter input remains literal data. |
| Read handling         | Collection envelope versus single object; empty results; nullable fields; derived/unknown properties; missing-key HTTP 404. A details fixture proves only root `@odata.context` is omitted while other annotations, arrays, and nested values survive.                                                                                       |
| Update request        | Correct key and session root; PATCH contains only intended changes to the five fields; untouched versus cleared values remain distinct; an empty HTTP 204 response is not deserialized as a person. Verify read-back values; a verified key rename changes subsequent lookup/update URLs. Gender is never an edit choice or PATCH property.  |
| Failures              | HTTP 400, 422, 500, malformed responses, and transport failures do not report success. A failed write is not assumed to leave state unchanged: the live 27-character surname probe returned 422 but changed the person. A lost response is not presented as a confirmed unchanged state.                                                     |
| Live availability     | Included in the default suite under accepted D012. A bounded read-only check reaches the live service and receives a usable People response; unavailable/unusable responses fail this check rather than skip it. Do not assert a fixed count or named mutable person. This is not a live update suite.                                       |
| Collection retrieval  | One GET returns the collection response used by the application. Controlled fixtures cover full and explicitly limited queries; optional `$top`/`$skip` are sent in that request. No client continuation/page parameter is used.                                                                                                             |
| End-to-end acceptance | User-owned read/edit/PATCH/read-back acceptance in a fresh session for the five editable fields; separately verify rejected input. Historical combined cleanup left a documented first-name change. Do not reuse that session as a clean fixture or claim automated live update acceptance. Gender writes are excluded under D020.           |

Use controlled responses for repeatable application tests and live requests for required availability coverage; keep their results distinguishable. Avoid fixed public-service record counts. Test console decisions and client behavior, not HTTP-library internals. Manual update acceptance remains separate and is not replaced by an availability pass.

Review regression coverage includes timeouts during body download after headers arrive on service-document, collection, and person GETs; rejection of person responses without a string `UserName`; and LastName substring query construction, preserving case and escaping literals for single-field and combined AND/OR filters. Controlled query tests do not establish live LastName filter support.

## Read Workflow UX

Accepted in D019 during the 2026-10-24 review; this describes the intended workflow:

- Main menu: Fetch people, View selected person (only after selection), Exit.
- Fetch: All people or Configure query, with Back. All people clears query restrictions.
- Configuration: edit one existing setting at a time; remove filters/limits/ordering explicitly. Apply performs a fetch; Cancel abandons draft edits. Blank filter values are literal empty strings; blank numeric entry cancels that numeric edit.
- Results: show the cached table and Select person, Change query, Main menu. Empty results still offer Change query/Main menu. Invalid numbers retry locally; blank person-number input cancels selection.
- Details: show complete JSON minus root context; Refresh explicitly reads again, while Back to results/Main menu and reopening details reuse cached data.
- Failures: loading status and explicit recoverable read errors without automatic retry. Failed fetch/refresh preserves the last successful cache with a warning; a person 404 clears stale details for that key. Query settings applied to a failed fetch remain available for manual refinement.
- Session startup: explicit user-confirmed retry or exit after a failed acquisition. All subsequent operations share the acquired session.

**Accepted implementation tradeoff (2026-10-25):** Failed fetches retain the last successful results and show a warning. If the failed fetch is started from the main Fetch menu, the application does not navigate back to the cached results, so they are not immediately accessible there. A failed details refresh retains the last successful details and displays a warning for that failed attempt; reopening or continuing to view details does not persist an unrefreshed/stale indicator. The user accepts these presentation limitations as a non-blocking tradeoff; they do not indicate a data or update correctness failure. Other D019 behavior is covered by the acceptance evidence below.

## Run And Test Handoff

Run these commands from the repository root with a .NET 10 SDK:

```powershell
dotnet restore PeopleApp.slnx
dotnet build PeopleApp.slnx --configuration Release --no-restore
dotnet run --project src\PeopleApp\PeopleApp.csproj --configuration Release --no-build --no-restore
dotnet test --solution PeopleApp.slnx --configuration Release --no-build --no-restore
```

The default test command includes GET-only live availability. For controlled tests only, after building:

```powershell
dotnet test --project tests\PeopleApp.Tests\PeopleApp.Tests.csproj --configuration Release --no-build --no-restore --filter-class PeopleApp.Tests.PeopleServiceSessionTests --filter-class PeopleApp.Tests.PersonUpdateTests --filter-class PeopleApp.Tests.PeopleConsoleApplicationTests --filter-class PeopleApp.Tests.LoggingAndCancellationTests
```

For availability only, use the same project command with `--filter-trait Category=LiveAvailability` instead of the class filters. These are native .NET 10 MTP arguments, not VSTest `--filter` expressions. A controlled-only pass is not a default-suite pass.

On environments with an inaccessible inherited feed, restore using `dotnet restore PeopleApp.slnx --source https://api.nuget.org/v3/index.json`, then use the `--no-restore` commands above. Existing inherited source configuration can emit NU1507 (multiple unmapped sources with central package management) and NU1900 (unavailable vulnerability audit source). A failed audit is not evidence that dependencies are vulnerability-free. User-wide NuGet configuration is not changed.

| Component                                    | Selected version / role                                         |
| -------------------------------------------- | --------------------------------------------------------------- |
| Target framework / SDK used for verification | `net10.0` / 10.0.401; SDK is not pinned in `global.json`        |
| Spectre.Console                              | 0.57.2, sole production package                                 |
| Spectre.Console.Testing                      | 0.57.2, scripted test console only                              |
| xunit.v3                                     | 4.0.2, executable test project using Microsoft.Testing.Platform |
| HTTP / JSON                                  | Built-in HttpClient and System.Text.Json                        |

Package versions are centralized in [Directory.Packages.props](../Directory.Packages.props). [global.json](../global.json) selects MTP; [PeopleApp.slnx](../PeopleApp.slnx) retains the two-project solution. No VSTest adapter or Microsoft.NET.Test.Sdk is used. Release verification avoids interfering with an already-running Debug application; stop your own application before rebuilding that same configuration if its executable is locked.

## Selective Update UX

From selected details, choose Edit person. The editor displays current and draft values for UserName, FirstName, LastName, MiddleName, and Age. Choose Replace value, Keep unchanged (remove the draft property), or Clear to null for the three nullable fields. Empty/whitespace names are literal strings; blank Age input cancels that entry. Save previews the draft and requires confirmation; Back retains edits and Cancel discards them. No-change Save sends nothing.

A verified read-back updates the selected key/details and cached row. List filtering/order may be stale until fetched again. Failed writes clear cached details, flag cached results, and report possible server changes without retry/rollback. An acknowledged PATCH followed by failed or mismatched read-back is not presented as verified success. For unverified renames, the original selection remains and the requested new key is shown; use Fetch people to select the actual current key.

Mutation tests use controlled handlers only. No live PATCH requests were made during Steps 4 or 5. Integrated scripted regressions exercise AND and OR queries with top/skip/ordering, selection, all-five-field editing with optional clearing, verified rename, explicit refresh, cached navigation, unrestricted refetch, and exit. They assert the exact requests and payload; they do not establish provider query or mutation support.

## Cancellation And Operation Logs

Press Ctrl+C once to stop gracefully at any prompt or in-flight request (exit code 130, no error message, no retry); a second Ctrl+C forces termination. Cancelling during Save warns that the server may already have applied the change. Request timeouts remain distinct, recoverable errors.

Each run writes a JSON-lines operation log to `%LOCALAPPDATA%\PeopleApp\logs` and prints its path at startup. Entries contain only operation, HTTP method, status, duration, outcome, failure stage, and exception type: no URLs, session keys, query values, person data, or bodies. See D021 in the [decision record](decisions.md).

## Fresh-Session Manual Acceptance (User-Owned)

**Status: confirmed complete by the user on 2026-10-25.** The user reports performing the fresh-session manual checks below and confirms they passed. This is user-confirmed acceptance; detailed request/response transcripts were not supplied. The D019 cache-warning behavior is an explicitly accepted tradeoff, not an outstanding acceptance blocker.

**Automated baseline (2026-10-24):** Release build passed (zero errors; four inherited NU1507/NU1900 warnings). Default MTP suite passed 97/97: 96 controlled tests and one GET-only live availability check, with zero failures/skips. The earlier Step 4 DNS failure remains recorded in the [implementation plan](ImplementationPlan.md); this later availability pass is not manual functional acceptance.

**Latest automated baseline (2026-10-25):** Default Release suite passed 110/110, including the GET-only live availability check. Restore reported inherited NU1507 and NU1900 warnings for package-source mapping and vulnerability-audit feed access.

Record the session/start time, actions and query settings, original selected key and complete before JSON, intended changes, observed messages/statuses, after JSON, and cleanup outcome. If capturing HTTP requests yourself, retain the session root and actual request/response evidence; otherwise label evidence as application observations rather than raw HTTP transcripts.

| Check                            | Procedure / expected observation                                                                                                                                                                                                                                                                                  | Result / evidence                      |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------- |
| Full list and navigation         | Fetch All people; select a returned row; navigate details/results/main. Back navigation reuses the cache and the run's session. Empty results and invalid selections stay navigable.                                                                                                                              | Confirmed passed by user (2026-10-25). |
| Slicing and ordering             | Configure top/skip, including zero and omitted values. Check each of UserName/FirstName/LastName/Gender/Age in both directions against the returned values; note nulls/ties without assuming a secondary sort key. All people subsequently removes restrictions.                                                  | Confirmed passed by user (2026-10-25). |
| Matching and combinations        | Exercise exact UserName, typed Gender, case-sensitive FirstName and LastName substrings, one-field and multi-field AND/OR queries, and a valid no-match query. Compare returned data with the criteria rather than treating HTTP 200 alone as proof.                                                              | Confirmed passed by user (2026-10-25). |
| Complete details and refresh     | Compare displayed JSON with a same-session GET when available: only root `@odata.context` is omitted; other annotations, unknown/derived values, arrays, nested objects, and nulls survive. Explicit Refresh reads again.                                                                                         | Confirmed passed by user (2026-10-25). |
| Five-field changes               | Capture original values. Exercise UserName/FirstName/LastName/MiddleName/Age individually and a chosen multi-field edit. Only intended fields are sent; verified success confirms requested values by read-back. Gender is not an edit choice.                                                                    | Confirmed passed by user (2026-10-25). |
| Optional clearing and validation | Clear LastName/MiddleName/Age explicitly; distinguish null from empty/whitespace names and Keep unchanged. Try a 27-character surname, fractional Age, and out-of-Int64 Age: local rejection sends nothing. Cancel and no-change Save also send nothing. Do not intentionally send known-invalid values remotely. | Confirmed passed by user (2026-10-25). |
| Rename routing                   | Choose a noncolliding new UserName, capture the original key, and verify the new key in read-back, explicit Refresh, subsequent edit routing, and a fresh list/selection. Collision and related-reference behavior remain unverified.                                                                             | Confirmed passed by user (2026-10-25). |
| Cleanup / uncertain outcomes     | Record whether restoration was attempted and verify every restored value/key by read-back. Do not infer restoration from a PATCH status. On any failure, record actual state or explicitly mark it unknown; do not retry/roll back automatically.                                                                 | Confirmed passed by user (2026-10-25). |

Historical user-reported Gender 500 findings are incorporated under D020; they are not acceptance evidence for Gender writes. The user confirmed the five-field application workflow passed manual acceptance on 2026-10-25.

### Remaining Limitations

- Public-service availability and DNS/network access can fail the default suite independently of controlled application tests.
- Session data is temporary; lifetime and cross-restart retention are not guaranteed. No local persistence is added.
- Gender writes are deliberately unsupported. The user confirmed manual query and ordering acceptance on 2026-10-25; this does not imply support for Gender updates or guarantees beyond the tested service behavior. The D019 cache-warning limitations are accepted as a tradeoff.
- A failed PATCH can have changed data; read-back verifies observed values, not causation or exclusive ownership. There are no automatic retries, rollback, conflict prevention, or ETag guarantees.
- Unique-key rename has historical evidence, but collisions and relationship effects are unverified. Cached list membership/order remains potentially stale after updates until refetched.
- Surname validation counts .NET UTF-16 string units; exact provider Unicode length semantics, empty/whitespace names, and full Int64 bounds are not established by live evidence.

## Scope Boundary

Creation, deletion, relationship editing, trips, graphical UI, authentication workflows, and production hosting are not imposed by this request. Client-side collection filtering and a full-text search engine are not required. User-selectable AND/OR is explicitly accepted under D006, not inferred from the original assessment.

Temporary sample-session data is accepted under D001 in the [decision record](decisions.md). Cross-restart retention is not a current requirement. D003 accepts established-constraint validation and honest uncertain-outcome reporting without automatic retry, rollback, or added recovery machinery. D020 supersedes D007 with five editable fields, excluding Gender; current API limits and historical residual state are recorded in the [API contract](02-Verified-API-Contract.md). No further agent live writes were authorized.
