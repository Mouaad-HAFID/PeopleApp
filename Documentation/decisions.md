# Decision Record

Record significant decisions only. Statuses: Proposed, Accepted, Superseded, Deferred. Acceptance requires explicit user agreement; recommendations in the assessment are not approvals. When evidence changes, retain the original decision and rationale, mark it Superseded, and link its replacement.

## D001: Required Data Lifetime

- **Status:** Accepted
- **Date:** 2026-10-22
- **Question:** Must edits survive application restarts reliably, or is a temporary demonstration sufficient?
- **Decision:** Option A: temporary sample-session data is sufficient. No retention guarantee across application or service restarts is required for the current scope.
- **Rationale:** The user explicitly selected A and emphasized avoiding premature optimization or complexity. The stated requirements require updates, not durable record keeping. Do not add a database, saved session state, or recovery machinery solely for cross-restart retention; revisit only if a confirmed requirement emerges.
- **Verified evidence from the assessment:** The TripPin root redirected to a session-keyed URL. Official documentation says to retain the key on every request to avoid interference from other users. These observations establish session routing, not durability.
- **Unknown:** Session expiry, retention across service restarts, and persistence guarantees. D002 subsequently verified bounded name updates, not durability.
- **Acceptance criterion retained:** An update must still be observable by a subsequent read in the same valid session. Choosing temporary data does not remove the requirement to perform real API updates.

| Option                                                         | Trade-off                                                                                                                                                                                        |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| A. Temporary demonstration; no guaranteed retention after exit | Selected. Smallest scope. Edits must still be observable by a subsequent read in the same valid session once writes are verified. Data may later reset or expire.                                |
| B. Best-effort continuation across app restarts                | Reusing the session URL may allow continued access while it remains valid, but adds session-state handling and cannot guarantee retention. Requires verification before promising this behavior. |
| C. Reliable retention across restarts is required              | Makes durability an explicit acceptance criterion. Investigate provider guarantees or reconsider the service before implementation; saving a session URL alone is insufficient.                  |

**Acceptance evidence:** On 2026-10-22, the user stated, "We will go with A," and clarified that persistence across service restarts is not a confirmed requirement and premature optimization or complexity should be avoided. This does not approve a service selection, remote write probe, or implementation.

**Evidence:** [API contract: service sessions and update limitations](02-Verified-API-Contract.md); [unresolved retention evidence](03-Unresolved-Questions-And-Assumptions.md); [prioritized suitability risk](04-Prioritized-Technical-Risks.md).

## D002: Verify Person Updates Before Implementation

- **Status:** Accepted
- **Date:** 2026-10-22
- **Question:** May a reversible name-update test be performed against a fresh TripPin RESTier sample session now?
- **Decision:** Perform bounded API investigation, not implementation or final service selection. The user's latest authorization extends the original one-name proposal to validation failures and critical name-update boundaries.
- **Rationale and evidence:** Updating a person is required. At proposal time, reads worked and name PATCH was documented but untested. The user subsequently reported Postman results and explicitly requested bounded independent validation before implementation depends on this path.
- **Authorized/executed bounds:** Fresh session; original person retained; eight PATCH probes covering invalid property/type, missing resource, success, name nullability, and surname lengths 26/27; GET comparisons after every probe; one restoration and full original-response comparison. No creation/deletion, reset, automatic retry, or unrelated field edit. Restoration verified.
- **Limits:** A successful result verifies this narrow update path only, not all editable fields, concurrency protection, or durable storage. Restoration is an intended check, not a guarantee if the service fails.

| Option                            | Trade-off                                                                                                                                                     |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. Authorize the bounded test now | Selected, with the subsequently authorized boundary checks. Resolves required-capability evidence early; temporarily changes sample data in a fresh session.  |
| B. Defer the write test           | Keeps investigation read-only for now; successful updates remain an unresolved acceptance gate. Documentation alone is not evidence of a working live update. |

**Acceptance evidence:** On 2026-10-22, after reporting manual Postman results, the user instructed, "Perform a bounded test to validate these findings and point out other critical integration boundaries".

**Outcome:** Reported 400/500/204 cases reproduced. Additional finding: an overlong surname returned 422 **and persisted the invalid value**. Restoration returned 204 and GET matched the complete original response. See the [live update evidence and remaining limits](02-Verified-API-Contract.md). This does not establish that all failed writes change data or approve implementation.

## D003: Failed-Update Handling

- **Status:** Accepted
- **Date:** 2026-10-22
- **Decision, accepted narrowed scope:** Validate only established constraints on the ultimately approved editable fields. For the tested names, these are schema-declared string types, non-null `FirstName`, nullable `LastName`, and the declared surname maximum of 26. Preserve the distinction between omitted edits and explicit clearing. Do not invent nonempty/whitespace rules, trim or restrict names by assumption, or add arbitrary limits. Leave unestablished business rules to the server.
- **Uncertain outcomes:** Treat a failed write as potentially state-changing; report the failure without claiming rollback or unchanged server data. Do not automatically retry or attempt rollback. A normal user-requested read can inspect current state; no additional automatic refresh, recovery workflow, or validation framework is assumed. Defer presentation details until the console workflow is agreed.
- **Rationale:** D002 captured a 27-ASCII-character surname PATCH returning 422, followed by GET showing all 27 characters instead of the preceding 26. The metadata explicitly declares `MaxLength="26"`; local validation can prevent that tested invalid request. This does not establish other business rules, Unicode counting behavior, or a need for transaction/reconciliation machinery.
- **Review refinement (2026-10-22):** The user agreed with the principles but explicitly requested evidence review and instructed that D003 remain Proposed. Earlier wording conflated relying on server validation with assuming rollback; these are separate concerns. Server validation remains appropriate for unknown rules, while failure must not be represented as proof of unchanged state.
- **Evidence:** [Exact request, before/after values, constraint scope, and evidence limits](02-Verified-API-Contract.md). Captured structured results support one immediate read-visible mutation; raw HTTP transcripts/full response bodies were not retained. Test cleanup restored the original record and is not an application rollback requirement. No new requests were made for this review.

**Acceptance evidence:** On 2026-10-22, after reviewing the exact request, before/after state, established constraints, and evidence limits, the user answered, "Yes. We can move along to the next point" to acceptance of this narrowed scope. The final editable-field set remains undecided; no application behavior has been implemented.

## D004: Filterable Fields

- **Status:** Accepted
- **Date:** 2026-10-22
- **Question:** Which person properties must be available as filter criteria?
- **Decision:** The filterable fields are `UserName`, `FirstName`, `LastName`, and `Gender`. Other properties are outside the current filter scope.
- **Rationale:** The user added `Gender` to the original three-string proposal to exercise different types of filtering logic. This provides string and enum handling without requiring unreliable collection predicates or a general query engine.
- **Evidence:** First/last-name filters and multi-field combinations worked; scoped collection probes demonstrated `UserName eq` predicates. `Gender eq Trippin.PersonGender'Female'` also worked. Metadata declares `Male`, `Female`, and `Unknown`; only the Female filter was independently probed. Unrestricted email/address collection filters returned 500. See the [API evidence](02-Verified-API-Contract.md).

| Option                                 | Trade-off                                                                                                                                                                          |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. `UserName`, `FirstName`, `LastName` | Original recommendation; the user chose to extend it with Gender.                                                                                                                  |
| B. A different or larger scalar set    | Selected as the three strings plus `Gender`, to cover string and enum predicates.                                                                                                  |
| C. Include emails or addresses         | Broader lookup capabilities, but observed unrestricted predicates fail. Requires focused investigation before promising support; do not silently substitute client-side filtering. |

**Scope:** This decision selects fields only. Match modes, case rules, AND/OR behavior, list/detail presentation, and editable fields remain separate questions. No additional API probes or implementation are authorized by this field selection.

**Acceptance evidence:** On 2026-10-22, the user stated, "We'll go with UserName, FirstName, LastName and Gender. This way we can cover different types of filtering logic".

## D005: Filter Match Operations

- **Status:** Accepted
- **Date:** 2026-10-22
- **Question:** Should the accepted filter fields use exact equality only, or should names also support partial matches?
- **Accepted resolution (2026-10-23):** Use exact equality for `UserName` and typed enum equality for `Gender`; use substring `contains` matching for `FirstName` and `LastName`. String matching is case-sensitive. Do not add a per-field operator selector.
- **Original recommendation:** One fixed equality operation per field: string equality for `UserName`, `FirstName`, and `LastName`; typed enum equality for `Gender`. The user selected substring matching for the two name fields instead.
- **Rationale:** Exact matching is the smallest contract satisfying the filtering requirement and is supported by observed predicates. It requires a complete field value; partial matching would be more forgiving for discovering names but is not yet a stated need.
- **Evidence:** Exact string and enum predicates worked, as did `contains(FirstName,'cott')`, `startswith(FirstName,'Sco')`, and `endswith(LastName,'tchum')`. Partial-name matching is a realistic alternative, not a universal capability claim; `contains` on `LastName` was not separately tested. See the [API evidence](02-Verified-API-Contract.md).
- **Acceptance evidence:** On 2026-10-23, the user selected exact `UserName`/`Gender` matching, substring `contains` for `FirstName`/`LastName`, and case-sensitive string matching. Provider behavior for `contains(LastName,...)` remains unverified; no live query was issued during implementation.

| Option                                                                | Trade-off                                                                                                                                         |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. Exact equality for all four fields                                 | Original recommendation; not selected. Gender uses an enum literal, not a quoted string or substring predicate.                                   |
| B. Exact username and gender; substring matching for first/last names | Selected. Easier name discovery without an operator menu; broader result sets. Verify the chosen LastName predicate before claiming live support. |
| C. User-selectable string operators                                   | More flexible, but adds interaction and test cases. Include only for a concrete requirement, not type-coverage alone.                             |

**Scope:** `UserName` uses exact equality, `Gender` uses typed enum equality, and `FirstName`/`LastName` use case-sensitive substring matching. The user selects AND/OR when multiple criteria are supplied. No per-field operator selector, raw OData expression, or full-text search is included.

**Deferral history:** On 2026-10-22, the user stated, "Deferred for later". No option was accepted at that time. The later acceptance resolves that deferral.

## D006: Combining Filter Criteria

- **Status:** Accepted
- **Date:** 2026-10-22
- **Question:** When multiple criteria are supplied, must all criteria match (AND), or is any matching criterion sufficient (OR)?
- **Decision:** Let the user select AND (all supplied criteria must match) or OR (any supplied criterion may match).
- **Rationale:** The user explicitly wants the richer feature set and the ability to refine filters. AND narrows by all criteria; OR broadens to alternatives. The additional mode and its tests now have a stated user need. The original recommendation was fixed AND; it was not accepted.
- **Evidence:** FirstName/LastName predicates combined with both AND and OR worked live. Gender equality also worked independently; mixed name-and-gender combinations were not separately probed. See the [API evidence](02-Verified-API-Contract.md). This decision does not depend on resolving D005's exact-versus-partial matching choice.

| Option                    | Trade-off                                                                                                                       |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| A. AND                    | Original recommendation, not selected as a fixed rule. Every supplied criterion must match; adding criteria narrows results.    |
| B. OR                     | Any supplied criterion can match; useful for alternative matches, but may return many people that do not match a supplied name. |
| C. User-selectable AND/OR | Selected: supports both narrowing and alternative-match workflows, with corresponding console behavior and tests.               |

**Scope:** Selection between combining supplied criteria with AND or with OR. This does not by itself approve mixed per-criterion operators, nested groups, raw expressions, or saved filters. D005 remains Deferred. Case sensitivity, omitted/empty criteria, the initial mode, and console presentation remain undecided. No implementation or remote writes are authorized.

**Acceptance evidence:** On 2026-10-22, the user selected the user-selectable option, stating, "user selectable allows for a richer feature set and allows user to refine their filters as they like".

## D007: Editable Person Fields

- **Status:** Superseded by D020 for the editable field set; historical verification retained below
- **Date:** 2026-10-22
- **Question:** Which person properties must the application allow the user to update?
- **Original decision (superseded by D020):** The requested editable set was `UserName`, `FirstName`, `LastName`, `MiddleName`, `Gender`, and `Age`. This was product scope, not proof the service supports every requested edit. D020 removes Gender after the user's manual results.
- **Rationale:** The user explicitly selected six scalar fields, extending the original names-only recommendation to the key, another nullable string, an enum, and a nullable Int64. Verify the concrete API behavior rather than silently keeping the original read-only-key recommendation or assuming metadata proves editability.
- **Evidence at selection:** First/last-name updates and restoration were verified. Gender filtering and enum metadata were verified, but Gender PATCH was not yet tested. Key renaming and numeric updates were not yet tested. See the [API evidence](02-Verified-API-Contract.md).

| Option                          | Trade-off                                                                                                                         |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| A. FirstName and LastName only  | Original recommendation; not selected. Covers updates with the strongest initial evidence and a small input surface.              |
| B. Add Gender                   | Extends editing to an enum as well as strings. Focused write verification would be needed before claiming live support.           |
| C. Another explicitly named set | Selected as the six fields above. Each field's contract must be established; metadata presence alone is not proof of editability. |

**Acceptance evidence:** On 2026-10-23, the user selected `UserName`, `FirstName`, `LastName`, `MiddleName`, `Gender`, and `Age` and instructed, "Run thin update verification around each field".

**Authorized verification:** Fresh session; at most 12 probe PATCHes plus two cleanup PATCHes. Test one valid update per field, selected null/type/enum/Int64 boundaries, and a unique key change last. Retain original values and before/after responses; check both key URLs and verify final restoration. No create/delete/reset, broad validation sweep, automatic retries, or application implementation. Test cleanup is not an application rollback feature.

**Verification outcome (2026-10-23):** Twelve probes and two cleanup PATCHes completed. FirstName, LastName, MiddleName, nullable Int64 Age, and unique UserName rename worked with read-back evidence. The key rename was reversed successfully. A declared Gender member returned 500 unchanged; Gender null returned 400. The combined cleanup including original Gender also returned 500, leaving only FirstName as `FieldProbeFirst` instead of `Russell`. Full restoration is not claimed. See the [six-field results and exact residual session](02-Verified-API-Contract.md).

**Further-write decision (2026-10-23):** When asked to authorize one extra FirstName-only cleanup after reaching the announced limit, the user chose "No further writes; document the remaining change". No additional writes were sent. This instruction controls the current investigation; it does not supersede the six requested editable fields.

**Manual follow-up (2026-10-23):** The user stated, "I will perform the remaining checks manually. We can move on to the next decision". Await the reported results before changing the evidence record. Gender behavior, remaining boundaries, and the residual first-name change have not thereby been resolved or reverified. No further automated API investigation is authorized by this handoff.

**Scope:** Detailed console interaction, clearing controls, and final input behavior remain open. D003 still governs established-constraint validation and uncertain write outcomes. If verification contradicts an edit requirement, report the mismatch for review rather than silently accepting a workaround or changing the requested set.

## D008: Concurrent-Edit Expectations

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Must the application prevent overwriting a newer edit to the same field made after the person was read?
- **Decision:** Option A: do not require or promise concurrent-edit prevention at this stage. Accept the possibility of a later successful write overwriting another edit to the same field, without adding client-side locking, automatic rereads, or conflict-resolution machinery.
- **Rationale:** The user explicitly stated that conflict prevention adds complexity and is not a justified requirement at this stage. A pre-save reread or confirmation prompt cannot close the race between checking and writing. Sending only intended fields limits the affected surface but does not prevent same-field overwrites. Revisit only if a confirmed requirement emerges.
- **Verified evidence:** People has no optimistic-concurrency annotation in the inspected metadata, and no ETag was observed in the person read or tested PATCH responses. See the [API evidence](02-Verified-API-Contract.md).
- **Unknown:** Whether any supported atomic precondition mechanism exists for People; no stale/conditional-write probe was performed. The absence of observed tokens is not proof that every conditional mechanism is unsupported. Pending manual findings may change the capability evidence, not automatically the product requirement.

| Option                                     | Trade-off                                                                                                                                                                                    |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. No concurrent-edit prevention guarantee | Selected. Keeps scope proportionate and states the limitation honestly. Another edit to the same field may be overwritten.                                                                   |
| B. Require atomic stale-write rejection    | Protects against lost updates, but requires verified server-side precondition support. If unavailable, revisit API suitability; a client-only reread/prompt is not an equivalent substitute. |

**Scope:** Conflict-prevention requirement only, not service selection or an API capability finding. D003's handling of uncertain write outcomes still applies. D005 remains Deferred; D007's field scope is unchanged. No implementation or API requests are authorized by this decision; concurrency probing is not a current acceptance gate.

**Acceptance evidence:** On 2026-10-23, the user selected A, stating, "conflict prevention adds extra complexity and is not a justified requirement at this stage".

## D009: Person-Details Content

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Which properties must appear when the user opens a selected person's details?
- **Decision:** Display the complete successful Get Entity By Id response body, excluding only its top-level `@odata.context` property. Do not restrict details to the editable fields.
- **Rationale:** The user explicitly requested the full returned entity rather than a hand-selected projection. Preserve all other returned properties and values, including arrays, nested objects, nulls, derived-only properties, and other annotations such as `@odata.type` or `@odata.etag` if present. Displaying a property does not make it editable.
- **Evidence:** The live unexpanded person responses contain all six properties, as well as other structural data such as Emails, AddressInfo, HomeAddress, and feature values. Navigation properties such as Friends and Trips were absent from unexpanded responses. See the [API evidence](02-Verified-API-Contract.md).

| Option                                          | Trade-off                                                                                                                                 |
| ----------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| A. The six selected editable properties         | Original recommendation; not selected. Would omit other returned properties.                                                              |
| B. Add selected read-only structural properties | The user chose a broader rule: retain the entire entity response except top-level context, not a fixed additional-field list.             |
| C. Also include related people or trips         | A broader browsing feature requiring related-data requests or expansion and more navigation. Not implied by viewing one person's details. |

**Acceptance evidence:** On 2026-10-23, the user requested "the full Get Entity By Id response minus the @odata.context field".

**Scope:** Render what the normal single-entity GET actually returns, not HTTP headers and not a reconstructed six-field edit model. Do not invent absent properties or add `$expand`/relationship requests solely to populate the display. List columns, exact layout, and formatting remain separate. D007's edit scope is unchanged; D005 remains Deferred. No implementation or API requests are authorized.

## D010: Console Interaction Mode

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Should the primary console workflow be interactive, command-line arguments, or both?
- **Decision:** Option A: one interactive workflow for listing, filtering, selecting, viewing, and editing people within the same application run. A separate argument-driven/scriptable interface is not required.
- **Rationale:** It naturally retains the same scoped service root and selected current key while the user refines filters or edits a person, without saved-session machinery. Command arguments are useful for scripting but a sequence of separate processes would need an explicit session-continuity policy if they must share the same temporary dataset.
- **Evidence:** A C# console application and the listed operations are required; the API uses session-keyed URLs. The user explicitly selected the interactive option. Scripting support has not been requested.

| Option                    | Trade-off                                                                                                                         |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| A. Interactive workflow   | Selected. Supports browse/filter/select/edit in one run with existing session context.                                            |
| B. Command-line arguments | Convenient for scripts and repeatable invocations; separate invocations do not automatically share the temporary service session. |
| C. Both                   | Serves manual and scripted use, but adds another interaction surface and tests. Include only if both workflows are needed.        |

**Acceptance evidence:** On 2026-10-23, the user selected A and stated, "further details on presentation will be fleshed out as the implementation goes".

**Presentation deferral:** Menus, prompts, list columns, layout, and formatting can be refined during implementation, within accepted behavior such as D009's complete entity display. Do not turn these reversible presentation choices into additional pre-implementation approval gates. This does not resolve D005's Deferred matching behavior or other unagreed functional semantics.

**Scope:** Primary interaction mode only, not a menu framework, project architecture, or runtime choice. D001's no-retention guarantee and all accepted API boundaries remain unchanged. No implementation or API requests are authorized.

## D011: Automated Test Dependencies

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** What must the automated tests cover locally and against the live service?
- **Decision:** Cover both application behavior and live service availability. Local controlled-response tests alone, with only manual live checks, are not the complete requested test scope.
- **Rationale:** The user explicitly requires availability coverage as well as application behavior. Controlled responses can consistently cover query construction, selective updates, complete details display, empty 204 handling, and failed/uncertain outcomes; live checks establish whether the real API can be reached and used from the test environment. Keep these outcomes distinguishable rather than presenting every network failure as an application regression.
- **Evidence and limits:** The provider is a mutable sample with observed nonstandard failures, including error-with-mutation. An availability pass does not prove Gender updates or every operation works; a failure does not by itself prove a global provider outage rather than a network/environment problem. Pending manual update findings remain separate evidence. No test framework, mocking package, monitoring system, or new project layering is implied.

| Option                                                    | Trade-off                                                                                                                                                    |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| A. Local automated tests plus manual live acceptance only | Original recommendation; not sufficient for the user's required automated availability coverage.                                                             |
| B. Application tests plus live automated coverage         | Selected for live availability coverage; D012 subsequently includes read-only live checks in the default suite. No automated live-write suite is authorized. |
| C. Live-dependent routine tests only                      | Not required. Makes application failures dependent on provider availability/data and makes controlled error scenarios harder to reproduce.                   |

**Acceptance evidence:** On 2026-10-23, the user stated, "Tests should cover both application behaviour and live service availability".

**Scope:** Required coverage only. Availability can be checked without PATCH/POST/DELETE, and this decision adds no permission for remote writes or resuming the manual update investigation. Execution policy is D012; framework, fixture layout, and exact cases can be chosen during implementation. No application/test implementation or API requests are authorized now. D005 remains Deferred and manual findings remain pending.

## D012: Live Availability Test Execution

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Should live availability checks run by explicit selection or in the default test run?
- **Decision:** Include application-behavior tests and read-only live availability checks in the default test suite. Keep them separately identifiable in results. An unavailable/unusable service fails its availability check and the overall run; do not silently skip it or reinterpret the local behavior results as failures.
- **Rationale:** The user explicitly requires the default suite to include live checks. This gives a broader single-run result while knowingly making overall success dependent on the test environment's network and the sample service. The original explicit-selection recommendation was not selected.
- **Availability boundary:** Use a bounded GET path that acquires the scoped root and reads People successfully with valid OData JSON. Do not depend on a fixed record count or a named mutable person. This is availability/basic-read evidence, not full functional acceptance, a health-monitoring system, or an update test. Exact endpoints/timeouts can be finalized during implementation.

| Option                                          | Trade-off                                                                                                                                                                        |
| ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. Explicitly selected live availability checks | Original recommendation; not selected as the default policy.                                                                                                                     |
| B. Live checks included in the default run      | Selected. One run exercises both required areas; a network/provider problem can fail the overall run even when local behavior tests pass. Keep failures separately identifiable. |

**Acceptance evidence:** On 2026-10-23, the user stated, "Include them as part of the default test suite".

**Scope:** Default execution policy only. Local tests may still be independently selectable, but an offline-only run does not satisfy the full default suite's availability coverage. No live mutations, implementation, scheduled monitoring, or present API requests are authorized. Manual update checks remain with the user.

## D013: Complete Listing And Query Options

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Must listing support the complete dataset, explicit query options, or both?
- **Decision:** Retrieve the People collection with one GET and provide optional user-specified `$top`, `$skip`, and `$orderby`. These are OData query options, distinct from D004/D006's field predicates and their AND/OR combination. Do not add page query parameters or continuation-link requests.
- **Complete-list behavior:** Do not impose an implicit top limit or skip offset. The collection GET is the list retrieval; the client does not follow `@odata.nextLink`. No fixed 20-person dataset is assumed. Optional ordering does not itself limit the list.
- **Explicit query behavior:** Apply supplied `$top`/`$skip` to the requested result slice and supplied `$orderby` to ordering in the collection request. Allow these options alongside the agreed field filters; no raw arbitrary `$filter` language is implied.
- **Rationale:** The user explicitly wants both complete retrieval and control over result size, offset, and ordering, rather than choosing only one retrieval mode. Exact interactive controls remain implementation-time presentation. Offset queries do not provide a stable snapshot during concurrent service changes.
- **Evidence and uncertainty:** Historical live probes returned 20 people without `@odata.nextLink`; `$top=2&$skip=0&$orderby=UserName&$count=true` and a second query with `$skip=2` worked. In the 2026-10-24 implementation review, the user confirmed that the API does not document paging and their tests did not observe it; the agreed client behavior is one collection GET with optional `$top`/`$skip`, not continuation handling. Other sort fields and all combinations of query options with field predicates remain independently unverified. See the [API evidence](02-Verified-API-Contract.md).

| Option                                       | Trade-off                                                                                                                                 |
| -------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| A. Complete-list retrieval only              | Original recommendation; insufficient for the requested explicit top/skip/orderby controls.                                               |
| B. User-requested page loading only          | Does not by itself express the requested complete-list capability or ordering controls. No particular next/previous-page UI was selected. |
| C. Complete list plus optional query options | Selected. Supports full retrieval and explicitly bounded/ordered queries without an implicit result cap.                                  |

**Acceptance evidence:** On 2026-10-23, the user requested "the ability to get the complete user list as well as the possibility to specify query filter for top and skip and orderby".

**Scope:** A single collection GET for list retrieval and the three named query options. D014/D015 accept five sortable fields and one field at a time with ascending/descending. No page parameter, continuation request, secondary key, or default order is implied; exact input controls remain implementation details. Test full versus intentionally limited retrieval with controlled responses.

## D014: Sortable Fields

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Which properties should be available for `$orderby`?
- **Decision:** The sortable fields are `UserName`, `FirstName`, `LastName`, `Gender`, and `Age`, using the API's canonical property names.
- **Rationale:** The user selected these five fields as sufficient. They cover identity, names, enum, and numeric ordering without sorting every metadata property. Sorting scope is independent of filtering and editing: Age sorting does not add Age to D004's filter set.
- **Exclusion:** MiddleName is not in the sortable set; it remains editable under D007 and visible under D009's full-response rule.
- **Evidence and limits:** Only `$orderby=UserName` has live evidence. The other fields' types are declared by metadata, but their ordering/null behavior was not tested. Acceptance establishes requested behavior, not provider support; remaining checks stay manual unless separately authorized.

| Option                                | Trade-off                                                                                                           |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| A. All six selected scalar fields     | Original recommendation; not selected. The user excluded MiddleName from sorting.                                   |
| B. UserName only                      | Strongest existing live evidence and a unique ordering key, but no name/gender/age sorting.                         |
| C. Another explicitly named field set | Selected as UserName, FirstName, LastName, Gender, and Age; ordering beyond UserName still needs provider evidence. |

**Acceptance evidence:** On 2026-10-23, the user stated, "Username, Firstname, Lastname, gender and age are enough". These labels refer to the canonical properties listed above, not new or renamed API fields.

**Scope:** Sortable properties only. D015 accepts one field at a time with ascending/descending; no default ordering or additional tie-break key is implied. Exact presentation can be refined during implementation. No API requests or application implementation are authorized.

## D015: Ordering Expression Scope

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Should a query select one sort field or allow a prioritized list of sort fields?
- **Decision:** One optional field from D014 at a time, with an ascending/descending choice. Prioritized multiple-field ordering is outside the selected scope.
- **Rationale:** The user selected the one-field option, keeping ordering input and tests smaller. Do not silently add a secondary UserName key to resolve ties, because that would introduce multi-field ordering. This choice is separate from deferred exact/partial filter matching.
- **Evidence and limits:** Only one-field `$orderby=UserName` was tested live. Explicit descending and other sort fields remain unverified; acceptance establishes requested behavior, not additional provider evidence. Nonunique fields can tie, and even unique ordering does not provide a snapshot across concurrent mutations.

| Option                                              | Trade-off                                                                                                                                                                                     |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. One field, ascending or descending               | Selected. A single nonunique field can leave ties; no automatic tie-breaker is accepted here.                                                                                                 |
| B. Prioritized fields, each ascending or descending | Supports expressions such as `LastName asc,FirstName asc`; more flexible but adds ordering and direction input/test cases. Restricted to D014's five fields, not arbitrary OData expressions. |

**Acceptance evidence:** On 2026-10-23, in response to the one-field ascending/descending versus prioritized-multiple-fields options, the user stated, "We'll do one field at a time".

**Scope:** Order-expression breadth/directions only. No default ordering is selected here; presentation remains implementation-time detail. D005 remains Deferred, D007's editable set is unchanged, and manual findings remain pending. No implementation or API requests are authorized.

## D016: Clearing Optional Values

- **Status:** Accepted
- **Date:** 2026-10-23
- **Question:** Should users be able to clear LastName, MiddleName, and Age to null, in addition to replacing their values?
- **Decision:** Option A: allow explicit clear-to-null for LastName, MiddleName, and Age, distinct from leaving a field unchanged. Send only intended changes. Do not infer that an empty or whitespace-only name means null, or silently trim it.
- **Rationale:** This exposes an already verified update capability without inventing business rules or expanding the editable-field set. A replacement-only editor is smaller but cannot intentionally remove an existing optional value.
- **Evidence:** LastName null clearing was verified on 2026-10-22; MiddleName and Age null clearing were verified on 2026-10-23. Metadata agrees they are nullable. FirstName/Gender null requests returned 400; UserName is non-nullable in metadata, but null-key PATCH was not probed. See the [API evidence](02-Verified-API-Contract.md).

| Option                                                  | Trade-off                                                                                                                                                |
| ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A. Allow explicit clearing of the three nullable fields | Selected. Supports replacing, retaining, or removing optional values; no particular prompt design is implied.                                            |
| B. Replacement only                                     | Fewer edit actions, but users cannot clear an existing optional value through the application. Existing null values may still be displayed and retained. |

**Scope:** Functional edit semantics only. Prompt wording, keyboard tokens, and controls remain implementation-time presentation under D010. D003's established-constraint validation still applies; no untested blank-string rule is introduced. D005 remains Deferred and manual findings remain pending. No API requests or implementation are authorized.

**Acceptance evidence:** On 2026-10-23, the user selected A and redirected the review toward overall project structure, framework, and packages before further implementation details. No scaffolding or implementation was requested.

## D017: Runtime And Framework Baseline

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decision:** Target .NET 10, use Spectre.Console 0.57 for the interactive console, and xUnit v3 for tests.
- **Rationale:** The user explicitly selected this baseline and confirmed .NET 10 is available. Spectre.Console supports the accepted interactive workflow; xUnit v3 is the selected test framework for both application-behavior and read-only live availability coverage.
- **Evidence:** User-reported environment availability and explicit package/framework choices, not independent installation, release, or compatibility checks. The user instructed that those checks need not be performed; none were performed for this decision.
- **Version scope:** Preserve the selected .NET major, Spectre.Console 0.57 version line, and xUnit major. Concrete package pins and the compatible test runner/adapter setup can be chosen during approved scaffolding; no specific patch version or additional runner package has been accepted here.

**Acceptance evidence:** On 2026-10-23, the user stated, ".NET 10 is available, we'll go with Spectre V0.57 and xUnit V3". Spectre is recorded as Spectre.Console for the interactive console, not an added command-line interface.

**Scope:** Runtime and the two selected framework/package lines only. No installation, package restore, environment verification, API request, or application/test scaffold is authorized. D005 remains Deferred and manual API findings remain pending.

## D018: Initial Project Structure

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decision:** One .NET 10 console application project and one xUnit v3 test project in a single solution. Keep console interaction and OData/HTTP integration in separate folders within the application, not separate class-library layers.
- **Rationale:** The requirements identify one interactive client and tests, not independently deployed/reused business or infrastructure libraries. Two projects provide executable/test separation while leaving internal responsibilities testable without a Domain/Application/Infrastructure project hierarchy.

Accepted initial layout, not files created:

```text
PeopleApp.slnx
src/
	PeopleApp/
		PeopleApp.csproj
		Program.cs
		ConsoleUi/
		Api/
tests/
	PeopleApp.Tests/
		PeopleApp.Tests.csproj
Documentation/
```

- **Dependency direction:** The test project references the console project. The console owns composition and the interactive workflow; API code owns requests, session routing, and response handling. Exact classes/interfaces and file placement can be refined during implementation.
- **Package baseline:** Use accepted Spectre.Console 0.57 and xUnit v3. Start with built-in HttpClient and System.Text.Json for HTTP and JSON; no extra HTTP/JSON package is necessary. Select the test-runner plumbing during scaffolding. Do not add an OData generator, generic host/DI framework, mocking framework, resilience package, or additional production projects without a concrete need.
- **Test organization:** Keep application-behavior tests and read-only live availability tests identifiable within the same test project; both run by default under D011/D012. Controlled responses test application-owned behavior, while manual update acceptance remains separate. Folder names do not imply separate runners or projects.
- **Trade-off:** A separate reusable client library could be extracted if another consumer or a meaningful reuse boundary appears. Adding it now creates another public assembly boundary without an established consumer; keeping two projects does not justify mixing UI and HTTP responsibilities.

**Scope:** Accepted initial project/dependency shape, not a finalized class design. Exact classes, test-runner setup, compatible package pins, and presentation can be refined during approved scaffolding/implementation. No directories, projects, package installations, or application implementation have been created. Approval of the foundation and a readiness question are not instructions to begin implementation.

**Acceptance evidence:** On 2026-10-23, after the two-project structure and built-in HTTP/JSON baseline were presented, the user stated, "It's good enough to start with", and asked whether any further decision was needed before implementation.

**Readiness assessment:** No additional architecture/framework/package decision was required before a minimal scaffold. D005's filter matching and case behavior were subsequently accepted. D020 resolves the Gender scope dependency by explicitly removing it from editing; final integrated acceptance remains Step 5.

## D019: Menu-Driven Read Workflow

- **Status:** Accepted
- **Date:** 2026-10-24
- **Decision:** Use a numbered Spectre.Console menu workflow. Main menu offers Fetch people, View selected person when a key is selected, and Exit. Fetch offers All people, Configure query, and Back.
- **Query editing:** The user selected a compact configuration menu. Show current values; allow individual filters, AND/OR, top/skip, and one sort field/direction to be changed or removed. Apply explicitly fetches results; Cancel discards the draft and preserves prior settings/results. All people deliberately uses an unrestricted query. Empty string filters remain literal values, not implicit removal or null.
- **Navigation:** Results offer Select person (when nonempty), Change query, and Main menu. Invalid inputs retry locally without another GET. Empty results remain navigable. Details retain the full JSON display minus root `@odata.context` and offer Refresh, Back to results, and Main menu. Returning to results or reopening selected details uses cached data; only explicit Fetch/Apply/Refresh requests perform reads, retaining the same session.
- **Read failures:** Show a loading indicator and explicit HTTP, transport, timeout, or malformed-response feedback. Do not automatically retry or replace successful cached results on failure. Retain applied query settings for manual refinement after a failed fetch; clearly identify cached results as belonging to the preceding successful query. Failed refresh retains explicitly identified old details; a confirmed 404 clears those details but retains the selected key for an explicit refresh.
- **Startup:** Session acquisition shows loading/error feedback and offers a user-confirmed retry or exit. Once acquired, the session is not automatically replaced after read failures.
- **Verification:** Scripted interaction tests use Spectre.Console.Testing 0.57.2, centrally pinned and referenced only by the test project. Cover navigation, cached reads, local validation, query retention/removal/cancellation, empty results, refresh, 404, and recoverable failures.
- **Scope:** UX refinement of the implemented read/query workflow only. No person updates, new query semantics, automatic retries, or implementation-plan advancement are included.
- **Accepted implementation tradeoff (2026-10-25):** Cache identification is not fully persistent. A failed fetch warns that prior results remain cached; from the main Fetch menu, that failure does not return the user to those cached results. A failed detail refresh warns once and retains the last successful details, but later detail views do not continue to identify them as unrefreshed. The user explicitly accepts these presentation limitations as a tradeoff, noting that presentation is not the main point of this implementation. They do not block acceptance; no code change is implied.

## D020: Five-Field Selective Updates; Gender Read-Only

- **Status:** Accepted
- **Date:** 2026-10-24
- **Decision:** Supersede D007's six-field edit set with UserName, FirstName, MiddleName, LastName, and Age. The user reports that all manual Gender update checks returned 500 and documentation did not establish a supported write representation. Gender is explicitly removed from the PATCH surface, not silently omitted; its display/filter/sort behavior remains unchanged.
- **Payload:** Send whichever field or fields the user explicitly chooses. Keep unchanged, replacement, and explicit null distinct. LastName/MiddleName/Age can be cleared; UserName/FirstName cannot. No Gender property is accepted by the edit model or included in PATCH.
- **Validation:** Preserve empty and whitespace-only strings without trimming. Enforce surname length 26 using .NET string length; exact provider Unicode counting remains unverified. Use nullable Int64 Age without business-age limits; reject noninteger/out-of-range input.
- **Workflow:** Add Edit person after the existing details actions, preserving prior menu numbering. Show current and intended values. Save requires explicit confirmation; Back retains the draft and Cancel discards it. An empty draft sends nothing.
- **Outcomes:** Empty 204 is an acknowledgement, not a person body. GET the intended key after acknowledgement and compare all intended values before announcing verified success. Use a new key for subsequent selection/reads/writes only after read-back verifies it.
- **Uncertainty:** Failed PATCH or lost response may have changed data. An acknowledged PATCH with failed/mismatched read-back is separately reported as unverified. Do not automatically retry, roll back, or add recovery/conflict machinery. Clear cached details on uncertain writes and mark list data potentially stale; keep the original selected key for an unverified rename and show the requested key, allowing an explicit fresh fetch to discover current state.
- **Cache:** After verified updates, update the cached selected row/key from read-back, but mark results potentially stale until a fresh fetch reapplies filtering and ordering.
- **Evidence boundary:** User-reported manual findings are not raw HTTP transcripts and do not establish the failure cause or universal API impossibility. Historical probe and residual-session records are unchanged. Automated mutation tests use controlled handlers only; final manual acceptance of the five-field workflow remains Step 5.

## D021: Graceful Cancellation And Operation Logging

- **Status:** Accepted
- **Date:** 2026-10-24
- **Cancellation:** The first Ctrl+C cancels one `CancellationTokenSource` in `Program`; a second Ctrl+C uses default process termination. The token flows through session startup, the retry confirmation, every menu/text prompt (Spectre `PromptAsync`), and every HTTP request. User cancellation is distinguished from `HttpClient.Timeout` by the caller token: it stops the application with exit code 130, without an error message or automatic retry. Timeouts remain recoverable read errors or uncertain update failures.
- **PATCH cancellation:** Cancellation propagates rather than becoming a definite failure. The editor states that the server may already have applied the change and that nothing was retried or rolled back; cached details are cleared. A timeout or lost PATCH response is described as "not confirmed" and may have changed data.
- **Logging:** The user selected a minimal built-in JSON-lines logger with no new packages, writing one file per run under `%LOCALAPPDATA%\PeopleApp\logs` (path shown once at startup). Each HTTP exchange records timestamp, operation (`open_session`, `list_people`, `get_person`, `update_person`, `update_readback`), method, status when received, duration, outcome (`succeeded`, `not_found`, `failed`, `timed_out`, `cancelled`), failure stage (`send`, `read`, `status`, `response`, `verify`), and exception type name only.
- **Redaction:** Never log URLs, session keys, query values, person keys/data, request/response bodies, PATCH payloads, or exception messages. Logging failures disable logging rather than interrupting the workflow; no retention/rotation policy is added.
- **Verification:** Controlled tests cover field/stage classification, timeout versus cancellation, PATCH cancellation propagation and messaging, prompt cancellation, redaction, and the file format. Interactive Ctrl+C in a real terminal is a manual check.

## Review Discipline

Resolve one consequential question at a time. Leave reversible implementation details until implementation unless they affect scope or acceptance. Record new evidence when obtained; do not silently rewrite an accepted rationale.

**Current implementation note (2026-10-24):** D017/D018's framework and two-project foundation are implemented. D005 is resolved. D020 removes Gender from updates following the user's manual findings. After Step 4's review pause, the user authorized Step 5. Complete integrated controlled regression coverage, build/default-suite checks, and handoff documentation; fresh-session manual acceptance remains user-owned and no agent live writes are authorized.
