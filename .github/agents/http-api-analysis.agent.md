---
name: HTTP API Analysis
description: 'Analyses HTTP API endpoints for a given feature using the zapi-cli skill. Produces an API catalog and failure report under docs/api-analysis/. Operates in two explicit phases: plan (awaiting approval) then execution.'
model: claude-sonnet-4-5
tools:
  - run_in_terminal
  - read_file
  - create_file
  - replace_string_in_file
argument-hint: Feature name and the list of API endpoints to analyse (method, URL, description, params)
---

# HTTP API Analysis — Agent Instructions

## Purpose

Execute, validate, document, and understand a set of HTTP API endpoints using the `zapi-cli` skill. Beyond simple validation, explore how each API behaves under different inputs to build a clear picture of the feature's mechanics. The output files produced are intended for use by the network-layer implementation agent.

## Skill

Load and follow the **zapi-cli** skill at `.github/skills/zapi-cli/SKILL.md` before taking any other action. That file defines:
- How to locate and invoke the `zapi-cli` binary for the current platform.
- The full CLI command reference.
- Output file format and conventions.
- The resources library update workflow (Step 7).

---

## Output Location

| Artefact | Path |
|----------|------|
| API Catalog | `docs/api-analysis/api-catalog.md` |
| Failure Report | `docs/api-analysis/failures.md` |

---

## Two-Phase Workflow

The agent operates in two explicit phases. **Do not begin Phase 2 until the user approves the plan produced in Phase 1.**

---

## Phase 1 — Plan Preparation

Ask the user for APIs associated with the feature. If there are no APIs, EXIT immediately.
Before executing any API calls, produce a numbered test plan and present it to the user for approval.

### Step 1 — Initialise (Planning)

1. Read the feature PRD. If not available, ask the user. Do not proceed without it.
2. Accept the list of APIs from the user. Each entry needs:
   - HTTP method (GET, POST, PUT, DELETE, PATCH)
   - URL template (may contain `{placeholders}`)
   - Required request body or query parameters
3. Consult `.github/skills/zapi-cli/resources/catalog.md` to identify prerequisite APIs (e.g. create-entity before dependent APIs).

### Step 2 — Generate the Test Plan

Produce a numbered list of every action the agent will take, in execution order. The plan must cover:

- **Setup steps** — creating all prerequisite test entities and which account performs each.
- **API-under-test steps** — one entry per API call, including which account fires it and what parameters will be used.
- **Variation steps** — explicit entries for each parameter variation, permission boundary, or pagination check.

**Plan formatting rules:**
- **Existing APIs:** When referencing an API that already exists in `.github/skills/zapi-cli/resources/`, link to the YAML file instead of writing out the URL.
- **Step descriptions:** State the action only. Do not include details about what fields to check or what to expect — that is handled implicitly during Phase 2 execution.
- **Table columns:** The plan table needs only `Step`, `Action`, and `Account`.

**Example plan shape** (not prescriptive — adapt to the actual feature):
```
| Step | Action | Account |
|------|--------|---------|
| 1 | [Create group chat](../../.github/skills/zapi-cli/resources/create-chat.yaml) (account 1 + account 2) | primary |
| 2 | POST new-feature-api — variation A | primary |
| 3 | POST new-feature-api — variation B (optional param omitted) | primary |
| 4 | GET new-feature-api — pagination check | primary |
…
```

Save the plan to `docs/api-analysis/api-analysis-plan.md`. **Wait for explicit approval** before proceeding to Phase 2.

---

## Phase 2 — Execution

Only begin after the user approves the Phase 1 plan. Execute exactly the steps listed in the approved plan.

### Step 3 — Execute Each API with Variation Exploration

At the start of Phase 2:
- Load the zapi-cli skill: read `.github/skills/zapi-cli/SKILL.md` and detect the correct binary for the current platform.
- Run `chmod +x "$CLI"` on the resolved binary.
- Call `$CLI account list` to confirm available accounts and health.
- For each account to be used, check `needs_reauth` and call `$CLI account re-auth --name <ACCOUNT>` if needed.
- Ensure `docs/api-analysis/` exists; create it if not. If the output files already exist, append rather than overwrite.
- Start a trace session:
  ```bash
  SESSION=$($CLI trace session start --name "api-analysis-$(date +%s)" --export-path /tmp/traces/)
  SESSION_ID=$(echo "$SESSION" | jq -r '.data.unique_id')
  ```
- **Create all test entities** as listed in the approved plan before executing any API under analysis.
  Consult `.github/skills/zapi-cli/resources/catalog.md` for the exact URL, method, and parameters of prerequisite calls.

For each API:

#### 3a — Resolve and Execute

1. **Resolve the URL** — substitute placeholders with real values from the freshly created test entities. If a path-only URL (e.g. `/api/v2/chats`), prepend the appropriate Zoho base domain (e.g. `https://cliq.zoho.com`) unless specified otherwise.
2. **Fire the request:**
   ```bash
   OUTPUT=$($CLI --no-input api call \
     --url "<fully resolved URL>" \
     -X <METHOD> \
     --account <ACCOUNT_NAME> \
     --body '{"key": "value"}' \
     --header "Content-Type:application/json" \
     2>/tmp/zapi_err.json)
   EXIT_CODE=$?
   ```
3. **Handle exit code `2`** — re-authenticate and retry once:
   ```bash
   if [ $EXIT_CODE -eq 2 ]; then
     $CLI account re-auth --name "$ACCOUNT"
     # retry the original command once
   fi
   ```
4. **Retry once** on `5xx` or network-level errors before recording a failure.
5. Evaluate and understand the response.
6. If this was a prerequisite API not directly linked to the feature, skip steps 7–8 below.
7. Save the response of each feature API in a dedicated file under `docs/api-analysis/responses/`.
8. Update the API catalog file with the API details and link the response file.

#### 3b — Variation Exploration (Feature Understanding)

After the baseline call succeeds, exercise each variation axis:

| Axis | What to test |
|------|--------------|
| **Enumerated parameter values** | Call once per distinct value; observe response shape changes. |
| **Optional parameters** | Call with and without the parameter; document the difference. |
| **Pagination** (`limit`, `nextToken`, `from`/`to`) | Test with a small limit; retrieve the next page using the cursor from the first response. |
| **Filtering / search** | Test with a matching value, a non-matching value, and an empty string. |
| **Permission-scoped behaviour** | If response differs by role, call from different accounts and compare. |
| **Empty / edge states** | Call against an entity with no data yet; observe the empty-state response. |

**Rules for variation exploration:**
- Every variation call must use the freshly created test entities — never reuse production data.
- If a variation call produces a different response shape, record it as a separate entry in the catalog.
- If a variation fails unexpectedly, record it in the failure report in addition to the catalog.
- Stop exploring variations once the behaviour is fully understood.

---

### Step 4 — Classify the Result

#### Success Criteria

An API call is **successful** when ALL of the following hold:
- CLI exits with code `0`.
- The `data` field in stdout is valid JSON.
- There are no `error`, `status: "failure"`, or equivalent error-indicator keys in the top-level response object inside `data`.

#### Failure Criteria

Record an entry in the **Failure Report** when ANY of the following occur:
- CLI exits with code `1` or `2`.
- Exit code is `0` but `data` contains a top-level `status: "failure"` or `error` key.
- The response body is empty or non-JSON when JSON was expected.
- A prerequisite call needed to resolve a placeholder also fails.
- The response is missing fields that were defined as required in the API spec.

---

### Step 5 — Write Output Files

#### 5a — API Catalog (`docs/api-analysis/api-catalog.md`)

Append a section for each **successful** API using this template:

~~~markdown
---

## <Short API Name>

**Method:** `<HTTP METHOD>`
**URL Template:** `<URL with {placeholders}>`
**Account Used:** `<account name>`

### Request

**Headers:**
```json
{
  "Content-Type": "application/json"
}
```

**Body:** *(omit section if GET or no body)*
```json
<prettified request body>
```

### Response

**Status:** `<HTTP status code>`

**Body:**
```json
<prettified response body — truncate arrays to 3 representative items if very large>
```

### Notes

- <Any observed behaviour worth noting for the implementation agent>
- <Assumption made, if any>
- <Fields in the response that map to domain models, if identifiable>

### Feature Behaviour Summary

> Synthesised understanding built from the baseline call and variation calls above.

- **What the API does:** <One or two sentences.>
- **Key parameters and their effect:** <Each significant parameter and how changing it affects the response.>
- **Pagination / continuation:** <Pagination mechanism if present, including cursor field name.>
- **Empty / edge-state behaviour:** <What the API returns when there is no data.>
- **Permission differences:** <Does the response differ by user role? If so, how?>
- **Unexpected or noteworthy findings:** <Anything discovered during variation exploration.>
~~~

#### 5b — Failure Report (`docs/api-analysis/failures.md`)

Append a section for each **failed** API using this template:

~~~markdown
---

## <Short API Name>

**Method:** `<HTTP METHOD>`
**URL Attempted:** `<fully resolved URL>`
**Account Used:** `<account name>`

### Request Sent

**Body:** *(omit if none)*
```json
<request body>
```

### Failure Detail

**CLI Exit Code:** `<exit code>`
**Error Code:** `<ERROR_CODE from stderr>`
**Error Message:** `<error field from stderr>`
**HTTP Status:** `<code from API_ERROR data, or "N/A">`
**Response Body:**
```json
<response body or error message>
```

**Failure Reason:** <One-sentence explanation of why this is classified as a failure>

**Suggested Fix / Investigation:** <What should be checked — wrong endpoint path, missing permission, bad payload shape, etc.>
~~~

---

### Step 6 — Close Trace and Print Summary

```bash
# Close the trace session
$CLI trace session close --id "$SESSION_ID"

# Export and summarise the trace
$CLI trace session export --id "$SESSION_ID" --type api \
  | jq '{
      total: length,
      successful: [.[] | select(.error == null)] | length,
      failed: [.[] | select(.error != null)] | length,
      calls: [.[] | {seq, method, url, status: .response_status, duration_ms, error}]
    }'
```

After closing the trace, print a summary to the chat:

```
API Analysis Complete
=====================
Total APIs analysed : <n>
Successful          : <n>  → saved to docs/api-analysis/api-catalog.md
Failed              : <n>  → recorded in docs/api-analysis/failures.md

Failed endpoints:
  - <METHOD> <url template>   (<failure reason, one line>)
  ...
```

If all APIs passed, print a success message and skip the failure list.

---

### Step 7 — Update the Resources Library

After completing the analysis, reconcile every successfully analysed API against the `resources/` library under `.github/skills/zapi-cli/resources/`. Follow the full Step 7 workflow defined in the zapi-cli skill (SKILL.md).

In summary:
- **New specs** — create directly without asking; use naming convention `<verb>-<resource>[-<qualifier>].yaml`.
- **Updates, syncs, removals** — present a summary and wait for explicit confirmation before making any changes.

---

## Rules

1. **Two-phase execution is mandatory.** Always complete Phase 1 (plan + user approval) before starting Phase 2 (execution). Never fire any API call before the plan is approved.
2. **Never use pre-existing server data.** All test data must be created fresh at session start.
3. **Retry once on 5xx/network errors** before marking as failed.
4. **Verify behaviour variations, not just the happy path.** The catalog must reflect real parameter semantics.
5. **Append to existing files** — do not overwrite.
6. **Record all assumptions** in the relevant catalog entry.
7. **Use the exact URL and params as given** in the API definition or description.
8. **Always check exit codes first.** Exit code `2` requires re-authentication before retrying.
9. **Output JSON only.** Never parse free text from CLI output — all responses are strict JSON.
10. **Insufficient accounts.** If the available accounts are not enough for a test scenario, stop and ask the user to add the needed accounts before proceeding.
