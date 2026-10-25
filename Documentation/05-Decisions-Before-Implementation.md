# Decisions Before Implementation

> Historical readiness snapshot from before implementation. Its investigation evidence and recorded approvals remain useful, but its authorization/readiness statements are superseded by the completed implementation and handoff recorded in the [implementation plan](ImplementationPlan.md). Fresh-session manual acceptance remains user-owned and pending.

## Immediate Focus

D017/D018 accepted .NET 10, Spectre.Console 0.57, xUnit v3, one console project, one test project, and built-in HTTP/JSON. At the time of this review, no further architecture or package decision was required to start the foundation. The user confirmed SDK availability; no environment/package checks were requested. Detailed design and presentation could evolve during implementation.

## Remaining Dependencies

- **Filtering resolution:** D005 has been resolved during implementation: exact UserName and typed Gender equality, case-sensitive contains matching for FirstName and LastName. Provider compatibility for LastName contains remains a manual read-acceptance item.
- **Update-scope resolution (D020):** The user reports that manual Gender PATCH checks all returned 500 and explicitly removes Gender from updates. Complete the five-field editor and leave final manual acceptance to Step 5. Historical residual test-session state is unchanged.
- **During approved implementation:** Choose compatible package pins/runner plumbing, class boundaries, test fixtures, prompts, rendering, and straightforward defaults not covered by explicit constraints. These routine decisions do not require another up-front approval round. Revisit the user only for a material scope change or contradictory evidence.

## Review Decisions

1. **Approve the target's suitability.** Reads and five selected field-update paths have thin evidence, including unique key renaming. Following manual Gender 500 results, the user explicitly removes Gender from the PATCH surface under D020. Temporary session data is accepted under D001. Final acceptance of the integrated five-field workflow remains user-owned.
2. **Complete filtering and listing semantics.** D004 accepts UserName/FirstName/LastName/Gender filters; D005 accepts exact UserName and typed Gender equality plus case-sensitive contains matching on FirstName/LastName; D006 accepts selectable AND/OR. D013 accepts the full list plus optional `$top`, `$skip`, `$orderby`. D014/D015 accept one of UserName/FirstName/LastName/Gender/Age with ascending/descending, not multi-key ordering or a hidden tie-breaker. No default ordering is selected; provider compatibility remains subject to manual read acceptance.
3. **Preserve the read and edit contract.** D020 supersedes D007's edit set with UserName, FirstName, LastName, MiddleName, and Age. Gender remains visible/filterable/sortable but is never sent in PATCH. D009 keeps full details minus root context; D016 permits LastName/MiddleName/Age null clearing. Send only intended changes and verify read-back before reporting success.
4. **Keep accepted conflict scope explicit.** D008 accepts no concurrent-edit prevention guarantee because it is not currently justified. Do not add locking, pre-save rereads, or conflict machinery for that purpose. D003's accepted uncertain-outcome policy remains unchanged; API concurrency capabilities need no further probe for the current scope.
5. **Preserve the accepted test boundary and stack.** D011/D012 require behavior and read-only live availability checks in the default suite, with distinguishable failures. D017/D018 fix the stack, two-project shape, and built-in HTTP/JSON baseline. Runner plumbing, fixtures, and presentation can be refined during approved scaffolding/implementation. No live mutation suite is authorized.

## Review Gate

The initial assessment was read-only. The 2026-10-22 name investigation used eight probes plus verified restoration. On 2026-10-23, 12 six-field probes and two cleanup PATCHes restored the key but left FirstName changed after the combined cleanup returned 500. The user declined further writes; no additional retry/cleanup was sent. At that historical review point, only documentation had changed and no application implementation existed. The [decision record](decisions.md) preserves approvals, and the [API contract](02-Verified-API-Contract.md) records exact evidence, limits, and residual session state.
