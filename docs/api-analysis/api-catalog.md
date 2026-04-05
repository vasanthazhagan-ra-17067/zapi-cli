# API Catalog — Designations Feature

> Generated during analysis session `api-analysis-1774261860`
> Account used: `vasanthseeker` (US DC)
> Note: All write operations (POST, PUT, DELETE) returned HTTP 403; the `vasanthseeker` account does
> not hold org-admin rights. Only GET endpoints were reachable.

---

## List Designations

**Method:** `GET`
**URL Template:** `https://cliq.zoho.com/api/v2/designations`
**Account Used:** `vasanthseeker`

### Request

**Headers:**
```json
{
  "Authorization": "Zoho-oauthtoken <token>"
}
```

### Response

**Status:** `200`

**Body:** ([full response](responses/list-designations.json))
```json
{
  "url": "/api/v2/designations",
  "data": [
    {
      "name": "CEO",
      "id": "5129211000000789007"
    }
  ]
}
```

### Notes

- The `data` array contains designation objects with two fields each: `name` (string) and `id` (string LUID).
- The `url` field in the response echoes the request path (without the base domain).
- No `next_token` or pagination cursor was present in the response, even when `?limit=1` was supplied — the API does not appear to support cursor-based pagination for this endpoint in the tested environment.
- When there are zero results (e.g., non-matching `search` query), the response is `{"url": "...", "data": []}` — a 200 with an empty array instead of a 404.
- The `--query` flag on the `zapi-cli` CLI has a Spectre.Console parsing bug with `List<string>` types; query parameters must be embedded directly in the URL string.

### Feature Behaviour Summary

> Synthesised from baseline list, search, and pagination variation calls.

- **What the API does:** Returns all designations in the organisation, optionally filtered by name.
- **Key parameters and their effect:**
  - `search` — Case-insensitive substring/prefix filter on designation name. A non-matching search returns `{"data":[]}` (200); partial prefix match (e.g., `CE` finds `CEO`) works correctly. An unknown or empty search term that matches nothing returns an empty array.
  - `limit` — Accepted as a query parameter but did not observe a `next_token` in the response with the single-designation test dataset; pagination cursor behaviour could not be fully exercised.
- **Pagination / continuation:** No `next_token` observed in any response variant. Pagination may only activate when the result set exceeds the `limit` with a larger dataset.
- **Empty / edge-state behaviour:** Returns `{"url": "...", "data": []}` with HTTP 200 — no 404 or error for empty state.
- **Permission differences:** GET is available to any authenticated user with `ZohoCliq.Designations.ALL` scope (or a READ sub-scope if one exists). Write operations require org-admin role (see Failures).
- **Unexpected or noteworthy findings:** The `limit` parameter is accepted without error but did not produce a paginatable response with a single-item dataset; the field may be honoured only with a richer dataset.

---

## Get Designation

**Method:** `GET`
**URL Template:** `https://cliq.zoho.com/api/v2/designations/{designationId}`
**Account Used:** `vasanthseeker`

### Request

**Headers:**
```json
{
  "Authorization": "Zoho-oauthtoken <token>"
}
```

### Response

**Status:** `200`

**Body:** ([full response](responses/get-designation.json))
```json
{
  "url": "/api/v2/designations/5129211000000789007",
  "data": {
    "name": "CEO",
    "id": "5129211000000789007"
  }
}
```

### Notes

- Single-object response: `data` is a plain object, not an array (unlike the list endpoint).
- `id` is a string LUID; pass it back as-is in path parameters.
- On an invalid/non-existent ID the API returns **HTTP 400** (not 404) with:
  ```json
  {"message": "Designation does not exist", "code": "designation_not_exist"}
  ```

### Feature Behaviour Summary

- **What the API does:** Fetches the name and ID of a single designation by its LUID.
- **Key parameters and their effect:** `{designationId}` in path — must be a valid string LUID.
- **Pagination / continuation:** Not applicable (single-entity endpoint).
- **Empty / edge-state behaviour:** Non-existent ID returns HTTP 400 with error code `designation_not_exist` — the API uses 400 rather than 404 for this case.
- **Permission differences:** Read is available to any authenticated user; write is restricted to org-admin.
- **Unexpected or noteworthy findings:** 400 (not 404) for non-existent resource is an unusual HTTP semantics choice — the implementation layer must handle this as a "not found" case.

---

## List Designation Members

**Method:** `GET`
**URL Template:** `https://cliq.zoho.com/api/v2/designations/{designationId}/members`
**Account Used:** `vasanthseeker`

### Request

**Headers:**
```json
{
  "Authorization": "Zoho-oauthtoken <token>"
}
```

### Response

**Status:** `200`

**Body:** ([full response](responses/list-designation-members.json))
```json
{
  "url": "/api/v2/designations/5129211000000789007/members",
  "data": []
}
```

### Notes

- `data` is an array — empty when no members are assigned to the designation.
- The CEO designation had no members in the test environment; the member object schema (fields like `name`, `email`, `user_id`) could not be observed directly.
- Add and remove member operations both returned 403; schema of individual member objects was inferred from the departments equivalent endpoint.

### Feature Behaviour Summary

- **What the API does:** Returns the list of users (members) assigned to a given designation.
- **Key parameters and their effect:** `{designationId}` in path — the LUID of the designation whose members to retrieve.
- **Pagination / continuation:** No pagination cursor observed; likely similar to list-designations.
- **Empty / edge-state behaviour:** Returns `{"data": []}` (200) when no members are assigned.
- **Permission differences:** Read is available to any authenticated user; write (add/remove members) requires org-admin role.
- **Unexpected or noteworthy findings:** Member object schema was not observable due to 403 on add-member; assumed to contain at minimum `user_id`/`name` fields consistent with other Cliq member-list endpoints.
