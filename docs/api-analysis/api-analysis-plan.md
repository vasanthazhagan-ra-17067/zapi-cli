# API Analysis Plan — Add User Status

**Feature:** User Status  
**API Under Analysis:** Add a new status (`POST /api/v2/statuses`)  
**Required OAuth Scope:** `ZohoCliq.Profile.CREATE` (read verification also needs `ZohoCliq.Profile.READ`), cleanup needs `ZohoCliq.Profile.DELETE`  
**Account:** vasanthseeker  
**Prerequisites:** None — operates solely on the authenticated user's profile.

---

| Step | Action | Account |
|------|--------|---------|
| 1 | Start trace session `api-analysis-<timestamp>` | vasanthseeker |
| 2 | Scope check — verify `ZohoCliq.Profile.CREATE`, `ZohoCliq.Profile.READ`, `ZohoCliq.Profile.DELETE` are present; add any missing via `scope add` and re-auth | vasanthseeker |
| 3 | `POST /api/v2/statuses` — baseline: `code=busy`, `message="In a Meeting"` | vasanthseeker |
| 4 | `POST /api/v2/statuses` — variation: `code=available`, `message="Ready to collaborate"` | vasanthseeker |
| 5 | `POST /api/v2/statuses` — variation: `code=invisible`, `message="Focusing"` | vasanthseeker |
| 6 | `POST /api/v2/statuses` — duplicate code: `code=busy`, `message="Second busy status"` (test whether multiple statuses with same code are allowed) | vasanthseeker |
| 7 | [`GET /api/v2/statuses`](../../.github/skills/zapi-cli/resources/get-custom-statuses.yaml) — verify all created statuses appear in the list | vasanthseeker |
| 8 | `DELETE /api/v2/statuses/{id}` — clean up each status created in steps 3–6 (4 calls) | vasanthseeker |
| 9 | Close trace session | vasanthseeker |
