# Verified API Contract

**Current update scope (2026-10-24):** The user reports that all manual Gender PATCH checks returned 500, with no supported representation established by documentation. They explicitly removed Gender from the PATCH surface under D020. Updates now accept only UserName, FirstName, MiddleName, LastName, and Age, singly or combined as intended by the user. Gender remains readable/filterable/sortable. The historical six-field probes below are retained as evidence, not the current edit requirement. No new live mutation probes were made during Step 4.

Initially assessed on 2026-10-22; six-field update verification added on 2026-10-23. **Live** means observed; **Metadata** means declared by the live schema; **Documented** means not independently tested. The first investigation used eight PATCH probes plus successful restoration. The later investigation used 12 probes plus two cleanup PATCHes and left one documented residual change after the user declined further writes. No people were created/deleted and no service reset was invoked. Those investigations preceded the application implementation; the completed implementation and its remaining acceptance scope are recorded in the [implementation plan](ImplementationPlan.md) and [requirements checklist](01-Requirements-Checklist.md).

## Service And Session

- **Live:** `https://www.odata.org/odata-services/` is documentation. The investigated service root is `https://services.odata.org/TripPinRESTierService/`.
- **Live:** GET of that root redirected to `https://services.odata.org/TripPinRESTierService/(S(<key>))/`. Use the resulting URL as `sessionRoot`, including its trailing slash. Acquire a fresh key rather than copying an example.
- **Live:** The service document advertises `People`, `Airlines`, `Airports`, and singleton `Me`; responses include `OData-Version: 4.0`.
- **Live:** Reads and the bounded person PATCH requests succeeded or returned application validation/resource errors without supplied credentials. No authentication challenge was observed on these paths; this does not establish access rules for every endpoint.
- **Documented:** Retain the session key in every request to avoid other users affecting results. It identifies a data session; it is not evidence of an authenticated identity or durable private storage.

## Entity And Properties

**Metadata:** `People` is an entity set of `Trippin.Person`. Its sole key is `UserName` (`Edm.String`, non-nullable). Read one entity with `GET {sessionRoot}People('russellwhyte')`. A unique-key rename and reverse rename were observed on 2026-10-23; collision behavior and related-reference effects remain unverified.

| Property          | Metadata contract                                                         |
| ----------------- | ------------------------------------------------------------------------- |
| `UserName`        | Non-nullable string; entity key.                                          |
| `FirstName`       | Non-nullable string; no maximum length declared.                          |
| `LastName`        | Nullable string; maximum length 26.                                       |
| `MiddleName`      | Nullable string.                                                          |
| `Gender`          | Non-nullable `Trippin.PersonGender`: `Male`, `Female`, `Unknown`.         |
| `Age`             | Nullable `Edm.Int64`; do not assume a 32-bit integer or a required value. |
| `Emails`          | Collection of strings.                                                    |
| `AddressInfo`     | Collection of `Trippin.Location`.                                         |
| `HomeAddress`     | Nullable `Trippin.Location`.                                              |
| `FavoriteFeature` | Non-nullable `Trippin.Feature`: `Feature1` through `Feature4`.            |
| `Features`        | Collection of `Trippin.Feature`, explicitly non-nullable.                 |

`Location` contains nullable `Address` and `City`; `City` contains nullable `Name`, `CountryRegion`, and `Region`. `EventLocation` derives from `Location` and adds `BuildingInfo`.

Navigation properties are `Friends`, `BestFriend`, and `Trips`; they are not ordinary scalar edit fields and were absent from the unexpanded response. Derived people exist: live results included `#Trippin.Manager` with `Budget`/`BossOffice`, and `#Trippin.Employee` with `Cost`. Displaying or editing derived-only fields is not yet a requirement.

Nullable declarations are schema constraints, not proof of all runtime behavior. Name nullability, surname length, selected enum requests, and nullable Int64 values were probed below. Empty strings, full numeric bounds, collection elements, and nested updates remain untested.

## Response Format

- **Live:** With `Accept: application/json;odata.metadata=minimal`, responses were JSON with `Content-Type: application/json; odata.metadata=minimal`.
- **Live:** A people collection is an object containing `@odata.context` and a `value` array. A single-person response is a direct object, not a `value` array.
- **Live:** Enums were strings; `Age`, `MiddleName`, and `HomeAddress` included null values. Emails and addresses were arrays. Additional properties and `@odata.type` occurred for derived entities.
- **Live:** The default collection had 20 people and no `@odata.nextLink`. That is a snapshot, not a guaranteed count or completeness rule for future datasets. In the 2026-10-24 implementation review, the user confirmed that the API does not document paging and their tests did not observe it. The application therefore uses one collection GET and does not inspect `@odata.nextLink` or issue continuation requests.
- **Live:** Errors used an `error` object with `code` and `message`. A missing key returned HTTP 404; an unknown filter property returned HTTP 400. Do not rely on these exact English messages.

## Filtering: Live Probes

The following are the 2026-10-22 read probes, before the later field-update investigation. They are decoded query expressions on `{sessionRoot}People`. Encode query values for transport; double an apostrophe inside an OData string literal before URI encoding.

| Query                                                                         | Observed result                                                                                                          |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `$filter=FirstName eq 'Scott'`                                                | HTTP 200; `scottketchum`.                                                                                                |
| `$filter=FirstName eq 'Scott' and LastName eq 'Ketchum'`                      | HTTP 200; `scottketchum`.                                                                                                |
| `$filter=FirstName eq 'Scott' or LastName eq 'Whyte'`                         | HTTP 200; Scott and Russell.                                                                                             |
| `$filter=contains(FirstName,'cott')`                                          | HTTP 200; Scott.                                                                                                         |
| `$filter=startswith(FirstName,'Sco')`                                         | HTTP 200; Scott.                                                                                                         |
| `$filter=endswith(LastName,'tchum')`                                          | HTTP 200; Scott.                                                                                                         |
| `$filter=FirstName eq 'scott'` and, separately, `contains(FirstName,'scott')` | HTTP 200; neither matched. These probes were case-sensitive.                                                             |
| `$filter=contains(tolower(FirstName),'scott')`                                | HTTP 200; Scott. Locale-wide case behavior is unverified.                                                                |
| `$filter=LastName eq 'O''Brien'`                                              | HTTP 200; empty result. Escaped syntax accepted; no apostrophe-bearing fixture was available to verify a positive match. |
| `$filter=Gender eq Trippin.PersonGender'Female'`                              | HTTP 200; 10 matches.                                                                                                    |
| `$filter=Age eq null` / `$filter=Age gt 30`                                   | HTTP 200; 20 / zero matches. All observed ages were null, so positive numeric comparisons remain untested.               |
| `$search=Scott` and an unrelated nonmatching search token                     | HTTP 200; both returned all 20 people. Full-text search was ineffective; do not use it to satisfy filtering.             |
| `$filter=Emails/any(email:email eq 'Scott@example.com')`                      | HTTP 500; `Value cannot be null. Parameter name: source`.                                                                |
| `$filter=AddressInfo/any(address:address/City/Name eq 'Boise')`               | Same HTTP 500 failure.                                                                                                   |

The two collection predicates succeeded when prefixed with `UserName eq 'scottketchum' and` and `UserName eq 'russellwhyte' and`, respectively. Thus lambda syntax is not universally unsupported, but unrestricted collection filtering is unreliable. No returned person's email/address collection was null; the exception's underlying cause is **not established**.

## Listing And Concurrency

- **Live:** `$top=2&$skip=0&$orderby=UserName&$count=true` returned `angelhuffman`, `clydeguess`, and `@odata.count: 20`; `$top=2&$skip=2&$orderby=UserName` returned `elainestewart`, `genevievereeves`. Neither response had a continuation link.
- **Implementation scope (2026-10-24):** One People collection GET with optional `$top`/`$skip`; no page query parameter or continuation-link handling. The user reports that paging is undocumented and was not observed in their tests.
- **Unverified:** Whether the provider silently limits any collection response and whether results are stable during writes. `$top`/`$skip` are the supported controls used by the client; no client paging behavior is assumed.
- **Metadata:** `People` has no optimistic-concurrency annotation. `Airlines` explicitly declares one on `Name`.
- **Live:** The people collection and selected-person response had no observed ETag header or `@odata.etag`. The documentation's airline `If-Match` example is not proof of person conflict detection. Stale-write rejection for people is unverified.

## Updates: Bounded Live Verification

On 2026-10-22, the user reported Postman observations of HTTP 400 for invalid property/type, 500 for a missing person, and 204 for success, and authorized bounded independent verification. All four were reproduced.

**Method:** Acquire a fresh keyed session, GET `People('russellwhyte')`, PATCH with `Content-Type: application/json` and `Accept: application/json;odata.metadata=minimal`, then GET to compare every returned property with the preceding state. Requests used the keyed URL directly, without credentials, conditional headers, automatic retries, or redirects on PATCH. A random missing key returned GET 404 before and after its PATCH.

| PATCH body / target                                      | HTTP | Read-back evidence                                                                                                                             |
| -------------------------------------------------------- | ---- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `{"NoSuchProperty":"AssessmentProbe"}`                   | 400  | Existing person unchanged.                                                                                                                     |
| `{"FirstName":42}`                                       | 400  | Existing person unchanged; error identified conversion to `Edm.String`.                                                                        |
| `{"FirstName":"AssessmentProbe"}` on the missing key     | 500  | Error message `Resource Not Found`; GET of that key remained 404. Existing selected person unchanged.                                          |
| `{"FirstName":"AssessmentProbe"}` on the existing person | 204  | First name changed; all omitted properties unchanged.                                                                                          |
| `{"FirstName":null}`                                     | 400  | Existing person unchanged; error identified non-nullable `FirstName`.                                                                          |
| `{"LastName":null}`                                      | 204  | Surname cleared; other properties unchanged.                                                                                                   |
| `{"LastName":"<26 ASCII L characters>"}`                 | 204  | All 26 characters stored; other properties unchanged.                                                                                          |
| `{"LastName":"<27 ASCII L characters>"}`                 | 422  | **All 27 characters were stored despite the error.** Error message was `Validaion failed for one or more objects.` Other properties unchanged. |

The length rows are descriptive, not literal request bodies: their string values were 26 and 27 repetitions of ASCII `L`. The probes ran sequentially; rejected-state comparisons used the immediately preceding read, not only the initial fixture.

### Exact 422 Evidence

Reviewed against retained request/result objects on 2026-10-22, without further HTTP requests. The exact application-level request was:

```http
PATCH https://services.odata.org/TripPinRESTierService/(S(i3ktkftgwt0sok5iiznryb0p))/People('russellwhyte')
Content-Type: application/json
Accept: application/json;odata.metadata=minimal

{"LastName":"LLLLLLLLLLLLLLLLLLLLLLLLLLL"}
```

This is the captured method, URI, explicitly supplied headers, and exact JSON body, not a raw wire transcript. No credentials or conditional headers were supplied. The session URL identifies the completed test, not a stable fixture to reuse.

| Stage                                                                          | HTTP result | `FirstName`           | Exact `LastName`              | Length         |
| ------------------------------------------------------------------------------ | ----------- | --------------------- | ----------------------------- | -------------- |
| GET immediately before this PATCH, following the successful 26-character probe | 200         | `AssessmentProbe`     | `LLLLLLLLLLLLLLLLLLLLLLLLLL`  | 26             |
| PATCH above                                                                    | 422         | Not a person response | Not a person response         | Not applicable |
| GET immediately after this PATCH                                               | 200         | `AssessmentProbe`     | `LLLLLLLLLLLLLLLLLLLLLLLLLLL` | 27             |
| GET after explicit test cleanup                                                | 200         | `Russell`             | `Whyte`                       | 5              |

The 422 response was 580 bytes, with `Content-Type: application/json; charset=utf-8`, empty `error.code`, and captured `error.message: "Validaion failed for one or more objects."` The spelling is as returned. The comparison recorded `LastName` as the only changed property; no other returned property changed across the failing request.

**Evidence limits:** Exact request bodies, parsed status/header/error information, selected GET values, and full-property comparison results were retained. Raw HTTP exports and complete before/after response bodies were not retained. This is one observed sequence, not an independent second reproduction or proof of the server's internal transaction behavior. It establishes immediate read-visible mutation despite 422, not durability or that every failed write mutates. Cleanup was a bounded investigation step, not a proposed application rollback feature.

### Established Constraint Scope

Retained metadata declares `FirstName` as `Edm.String` with `Nullable="false"`, and `LastName` as `Edm.String` with `MaxLength="26"`.

| Constraint or behavior                      | Evidence and limit                                                                                                                                                                                                                                                                                         |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Names have string types                     | Metadata declares both strings. Numeric `FirstName: 42` returned 400; not every invalid JSON shape was probed.                                                                                                                                                                                             |
| `FirstName` cannot be null                  | Metadata and explicit-null PATCH returning 400 with unchanged read-back agree. This does not establish that empty or whitespace-only strings are forbidden.                                                                                                                                                |
| `LastName` can be null                      | Explicit-null PATCH returned 204 and GET showed null. Clearing is distinct from omitting the property.                                                                                                                                                                                                     |
| Declared surname maximum is 26              | Metadata has `MaxLength="26"`. The 26-ASCII-character request succeeded; 27 returned 422 but still became read-visible. The constraint is established; atomic enforcement is not. Non-ASCII counting behavior was not tested, so no ASCII-only rule or specific Unicode counting algorithm is established. |
| A missing edit property preserves its value | Single-property PATCH probes left other returned properties unchanged. Do not translate an omitted edit into an explicit null.                                                                                                                                                                             |

No nonempty-name, whitespace, trimming, character-set, capitalization, email-format, or age-range rule was established by this investigation. No maximum length is declared for `FirstName` in the inspected metadata. Do not invent such rules or infer a generic validation framework is needed. For approved fields, local validation can use explicit schema constraints; unknown business rules remain the server's responsibility, without assuming rejection leaves data unchanged.

**Restoration:** One final PATCH restored original `FirstName: Russell` and `LastName: Whyte` and returned 204. Subsequent GET returned 200 and matched every property in the original person response. There were eight probe PATCH requests plus one restoration, with no automatic retries. Probing stopped on detecting the error-with-mutation anomaly; no further boundary writes were attempted. An earlier preparation-scope error failed locally before any HTTP PATCH and is excluded from these counts.

### Critical Integration Boundaries

- **Failed writes can change state.** The 422 surname request was followed by a GET showing the new value. The underlying server implementation cause is unknown; this is one observed case, not proof that all failures mutate. Do not assume a non-2xx response is a rollback, retry writes blindly, or present unchanged local data as authoritative after an error. Accepted D003 limits local validation to established constraints and leaves unknown rules to the server. A normal user-requested read can inspect current state; no automatic refresh/recovery workflow is required.
- **Success has no representation.** Every observed 204 response had zero body bytes and no Content-Type or ETag. Do not attempt to deserialize a person from it; use GET if an authoritative updated representation is needed.
- **Missing-resource status is method-specific.** GET returned 404 while PATCH returned 500 for the same absent key. A generic 500 is not sufficient to label a person missing: collection filters also produced 500 for unrelated reasons. A follow-up GET can inform the diagnosis but is not an atomic existence guarantee.
- **Omission and clearing differ.** A one-field PATCH preserved omitted properties. Explicit `LastName: null` cleared that field; `FirstName: null` was rejected. Blank strings and empty collections were not tested and must not be treated as equivalent to null.
- **Validation is not just 400.** Parse/type/null failures returned 400, while the overlong surname returned 422. Error bodies had an `error` object and an empty `code`; the 422 Content-Type was `application/json; charset=utf-8`, versus `application/json; odata.metadata=minimal` on tested 400/500 responses. Handle JSON without requiring one exact Content-Type string or a populated service error code. Exact error messages are not a stable contract.
- **No observed conflict token.** Baseline GET and all PATCH responses had no ETag. No conditional or stale-write probe was made. A reread cannot supply atomic conflict prevention.
- **Session routing still matters.** Acquire the keyed root before PATCH and retain it throughout. Session expiry, reset scope, durability, and redirect preservation of a PATCH body remain unverified; cross-restart retention is outside the accepted current scope.

**Initial investigation limits:** The 2026-10-22 results verified name operations only. The following section adds key, middle-name, enum, and numeric evidence. Whitespace rules, non-ASCII length handling, nested/collection replacement, `If-Match`, and transport-loss behavior remain untested. PUT was neither needed nor tested.

## Six-Field Update Verification: 2026-10-23

The user selected `UserName`, `FirstName`, `LastName`, `MiddleName`, `Gender`, and `Age` and authorized thin verification. A fresh metadata read confirmed their types/nullability and `LastName` maximum length 26. Requests used JSON, no supplied credentials, no automatic retries, and no redirects on PATCH.

**Session:** `https://services.odata.org/TripPinRESTierService/(S(ebkrfedq2cu5gbi51lsj4kd1))/`. The selected person was `russellwhyte`; the proposed key `fieldprobe_ad3d48c656324c06987944bd0405ce91` was confirmed absent by GET 404 before writing. Each probe changed one field, followed by GET and comparison of every returned property against the preceding state. Key checks read both URLs. This identifies a completed sample session, not a durable fixture.

| Exact PATCH body                                             | HTTP | Verified read-back                                                                        |
| ------------------------------------------------------------ | ---- | ----------------------------------------------------------------------------------------- |
| `{"FirstName":"FieldProbeFirst"}`                            | 204  | First name changed; all other properties preserved.                                       |
| `{"LastName":"FieldProbeLast"}`                              | 204  | Surname changed; all other properties preserved.                                          |
| `{"MiddleName":"FieldProbeMiddle"}`                          | 204  | Middle name changed from null.                                                            |
| `{"MiddleName":null}`                                        | 204  | Middle name cleared.                                                                      |
| `{"Gender":"Female"}`                                        | 500  | Gender remained `Male`; all properties unchanged.                                         |
| `{"Gender":"NotAGender"}`                                    | 500  | All properties unchanged; same `Gender` error as the declared member.                     |
| `{"Gender":null}`                                            | 400  | All properties unchanged; error identifies non-nullable `Trippin.PersonGender`.           |
| `{"Age":35}`                                                 | 204  | Exact integer stored.                                                                     |
| `{"Age":2147483648}`                                         | 204  | Exact value above Int32 maximum stored.                                                   |
| `{"Age":"not-a-number"}`                                     | 400  | Previous numeric value preserved.                                                         |
| `{"Age":null}`                                               | 204  | Age cleared.                                                                              |
| `{"UserName":"fieldprobe_ad3d48c656324c06987944bd0405ce91"}` | 204  | Key changed; original URL returned 404, proposed URL 200; all other properties preserved. |

All successful probe responses had empty bodies and no ETag. Rejected requests in this run left the person unchanged; this does not undo the earlier 422 mutation evidence or establish general failure atomicity.

### Gender Integration Gap

PATCH of the existing person with the exact body `{"Gender":"Female"}` returned:

```json
{ "error": { "code": "", "message": "Gender" } }
```

Status was 500, and a subsequent GET showed `Gender: Male` unchanged. `Female` is a declared member, so do not describe this as ordinary invalid input. An undeclared member returned the same 500; explicit null returned 400. The internal failure cause and whether another correct representation could work are not established. No fallback representation or workaround was attempted. Gender remained requested at the time of this probe; D020 later explicitly removed it from updates following the user's manual 500 results.

### Cleanup And Remaining Change

The announced bound was 12 probe PATCHes plus at most two cleanup PATCHes. Both cleanup requests were sent:

1. PATCH at the new key URL with `{"UserName":"russellwhyte"}` returned 204. GET of the original URL returned 200; GET of the proposed URL returned 404. Key restoration is verified.
2. PATCH at the original URL with the exact body below returned 500 with the same `Gender` error:

```json
{
  "LastName": "Whyte",
  "Age": null,
  "Gender": "Male",
  "FirstName": "Russell",
  "MiddleName": null
}
```

The final GET matched every original returned property **except FirstName**:

| Property     | Original       | Last observed     |
| ------------ | -------------- | ----------------- |
| `UserName`   | `russellwhyte` | `russellwhyte`    |
| `FirstName`  | `Russell`      | `FieldProbeFirst` |
| `LastName`   | `Whyte`        | `Whyte`           |
| `MiddleName` | null           | null              |
| `Gender`     | `Male`         | `Male`            |
| `Age`        | null           | null              |

**Residual test state:** `People('russellwhyte')` in the session above was last observed with `FirstName: FieldProbeFirst`, not `Russell`. Full restoration was **not** achieved. After the limit was reached, the user explicitly chose "No further writes; document the remaining change" instead of authorizing a FirstName-only cleanup. No additional writes or retries were made. Session expiry may later change availability; no retention guarantee is claimed.

### Established Boundaries And Limits

- **Key changes affect routing.** Use the original key URL to request the rename; subsequent selection/update URLs must use the newly observed key. Unique rename and reverse rename worked. Duplicate keys, case-only renames, invalid/null keys, and effects on friends/trips were not tested; no create/delete workaround is approved.
- **Avoid whole-person update payloads.** Name-only writes worked, while both the changed and original Gender values produced 500 when included in the tested payloads. Send only intended changes; do not include unchanged Gender in a names-only update. Do not silently discard a requested Gender edit.
- **Nullable fields differ from no edit.** MiddleName and Age accepted explicit null. FirstName and Gender reject null. LastName null-clearing was already verified. Console clearing controls remain undecided.
- **Age is nullable Int64, not an established business range.** JSON numbers 35 and 2147483648 were stored exactly; null cleared the field. The nonnumeric string was rejected with an error mentioning Int64 and IEEE754Compatible. Numeric strings, IEEE754Compatible mode, full Int64 endpoints/overflow, fractions, and negative values were not tested. No realistic-age minimum or maximum is established.
- **No universal 400 classification.** A declared enum member returned 500, not a validation 400. Preserve the observed status and distinguish a failed request from a known invalid user value.
- **Evidence capture:** Exact payloads and full baseline, probe before/after, error, and final response bodies are retained in the terminal session object, not exported as raw HTTP transcripts or durable data files. The key probe's summary `RequestUri` was assigned after switching to the new read-back URL; the actual PATCH target was the original URL, established by the executed request sequence. The table and cleanup account use that sequence, not the mislabeled summary value.

This was a thin single-person investigation, not exhaustive validation or permission to implement. D007 records the selected scope; unresolved Gender behavior must be reviewed rather than silently removing the field.

## Sources And Reproduction

1. [Service directory and basic usage, including PATCH and airline ETags](https://www.odata.org/odata-services/).
2. [Session-key acquisition and per-request use](https://www.odata.org/odata-services/service-usages/request-key-tutorial/).
3. [Advanced usage and derived types](https://www.odata.org/odata-services/service-usages/trippin-advanced-usages/).
4. [Live service root](https://services.odata.org/TripPinRESTierService/) and [metadata entry point](https://services.odata.org/TripPinRESTierService/$metadata). For consistent reproduction, acquire a root once and request its `$metadata`, `People`, and the expressions above in the same session. Fixture counts may change.
