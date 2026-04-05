# Failure Report — Designations Feature

> Generated during analysis session `api-analysis-1774261860`
> All failures below share the same root cause: the `vasanthseeker` account is not an organisation
> admin. All write operations against the Designations API require org-admin role at the Zoho
> application layer, regardless of OAuth scope. Scope `ZohoCliq.Designations.ALL` is necessary
> but not sufficient for these operations.

---

## Create Designation

**Method:** `POST`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations`
**Account Used:** `vasanthseeker`

### Request Sent

**Body:**
```json
{"name": "Analysis-Engineer-Test"}
```

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** The account does not hold org-admin rights; Zoho rejects all designation creation regardless of valid OAuth scope.

**Suggested Fix / Investigation:** Re-run with an account that has org-admin or super-admin role in the Zoho organisation. The scope `ZohoCliq.Designations.ALL` must be present AND the user must be an org-admin.

---

## Create Designation — Duplicate Name

**Method:** `POST`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations`
**Account Used:** `vasanthseeker`

### Request Sent

**Body:**
```json
{"name": "CEO"}
```

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** Same 403 as above — duplicate-name validation (expected error code: `designation_already_exist`) could not be reached because the permission check fires first.

**Suggested Fix / Investigation:** Requires org-admin account. Once permission is resolved, expect `designation_already_exist` error code when posting a name that already exists.

---

## Update Designation

**Method:** `PUT`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations/5129211000000789007`
**Account Used:** `vasanthseeker`

### Request Sent

**Body:**
```json
{"name": "Chief-Executive-Officer-Test"}
```

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** Designation rename requires org-admin role; same root cause as create.

**Suggested Fix / Investigation:** Requires org-admin account. Body is expected to accept `{"name": "<new_name>"}`.

---

## Add Designation Members

**Method:** `POST`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations/5129211000000789007/members`
**Account Used:** `vasanthseeker`

### Request Sent

**Body:**
```json
{"user_ids": ["809568848"]}
```

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** Adding members to a designation requires org-admin role.

**Suggested Fix / Investigation:** Requires org-admin account. Body accepts `{"user_ids": ["<ZUID>", ...]}` (array of user ZUIDs as strings).

---

## Remove Designation Members

**Method:** `DELETE`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations/5129211000000789007/members`
**Account Used:** `vasanthseeker`

### Request Sent

**Body:**
```json
{"user_ids": ["809568848"]}
```

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** Removing members from a designation requires org-admin role.

**Suggested Fix / Investigation:** Requires org-admin account. DELETE to the members sub-resource with `{"user_ids": [...]}` in the body.

---

## Delete Designation

**Method:** `DELETE`
**URL Attempted:** `https://cliq.zoho.com/api/v2/designations/5129211000000789007`
**Account Used:** `vasanthseeker`

### Failure Detail

**CLI Exit Code:** `1`
**Error Code:** `API_ERROR`
**Error Message:** `API returned 403.`
**HTTP Status:** `403`
**Response Body:**
```json
{"code": "operation.not.allowed", "message": "Uh-Oh! You are not authorized to do this operation."}
```

**Failure Reason:** Deleting a designation requires org-admin role.

**Suggested Fix / Investigation:** Requires org-admin account. No request body is needed — the designation LUID is sufficient in the path.
