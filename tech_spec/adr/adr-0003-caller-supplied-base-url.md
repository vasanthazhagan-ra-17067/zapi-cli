---
title: "ADR-0003: Caller-Supplied Base URL Per API Invocation"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "api-client", "ux"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli is a **general-purpose Zoho API invoker** — not a product-specific CLI (it is not just a Zoho Desk CLI or a Zoho Cliq CLI). The same binary must be able to call APIs across Zoho Desk, Zoho Cliq, Zoho CRM, and any other Zoho product.

Each Zoho product has a distinct API base URL that varies by product and by datacenter region:

| Product | US | EU |
|---------|----|----|
| Zoho Cliq | `https://cliq.zoho.com/api/v2` | `https://cliq.zoho.eu/api/v2` |
| Zoho Desk | `https://desk.zoho.com/api/v1` | `https://desk.zoho.eu/api/v1` |
| Zoho CRM | `https://crm.zoho.com/crm/v5` | `https://crm.zoho.eu/crm/v5` |

Design options for how the CLI resolves the base URL:

1. **Derive from account domain** — use `AccountEntry.Domain` (e.g. `zoho.com`) plus a hardcoded per-product subdomain map.
2. **Require `--base-url` flag on every `api call`** — the caller supplies the full root URL.
3. **Named product aliases in a registry** — `--product desk` resolves to a stored base URL.
4. **Hybrid** — require `--base-url` in v1, add a registry/alias layer in v2.

The primary consumer is **AI agents**. Agents have precise, contextual knowledge of which API endpoint they are targeting — they are not end-users who need URL shorthand. Agents can supply `--base-url` reliably without abstraction layers.

A derived-URL approach would require zapi-cli to maintain an ever-growing, potentially stale map of product subdomains per datacenter. This map would need updating for every new Zoho product or newly supported datacenter — creating ongoing maintenance burden and a potential source of silent misdirection.

## Decision

**`--base-url` is a required flag on every `api call` invocation.** The final request URL is assembled as `<base-url><path>`.

The `AccountEntry.Domain` field (e.g. `"zoho.com"`) is used **only** for OAuth token endpoint resolution. It has no role in API URL construction.

```
--base-url "https://desk.zoho.com/api/v1"  +  --path "/tickets"
→ https://desk.zoho.com/api/v1/tickets

--base-url "https://cliq.zoho.eu/api/v2"   +  --path "/channels"
→ https://cliq.zoho.eu/api/v2/channels
```

An API Registry (`api registry add/list/show/remove`) is planned for v2 to allow named shorthand entries that carry a `baseUrl` field — enabling `--id desk-list-tickets` as a convenience form. This does not change the v1 `api call` contract.

## Consequences

### Positive

- **POS-001**: Zero inference — the caller has complete, explicit control over the target URL with no possibility of URL derivation bugs routing requests to the wrong product or datacenter.
- **POS-002**: No product-to-subdomain mapping table to maintain — new Zoho products and new datacenters are automatically supported as soon as their base URLs are known.
- **POS-003**: The `ApiTraceEntry` records `base_url` faithfully, making multi-product trace sessions unambiguous and trivially filterable by product.
- **POS-004**: AI agents are excellent consumers of this pattern — they can compose the correct `--base-url` from their context and the API spec, without needing a registry lookup.
- **POS-005**: The v2 API Registry builds naturally on top of v1 — it is purely additive and does not require changing the core `api call` contract.

### Negative

- **NEG-001**: More verbose to type for interactive human users — `--base-url "https://desk.zoho.com/api/v1"` on every call is tedious compared to `--product desk`.
- **NEG-002**: Human users unfamiliar with Zoho product URLs face a discoverability gap — they must look up the correct base URL before calling.
- **NEG-003**: Until the API Registry is implemented, there is no shorthand or autocomplete for common product URLs, increasing the chance of typos in manual use.
- **NEG-004**: The `ApiRegistry` in v2 adds a second surface area for URL management; without careful defaults, users may face cognitive overhead choosing between `api call --base-url` and `api call --id`.

## Alternatives Considered

##### Derive base URL from AccountEntry.Domain + product flag

- **ALT-001**: **Description**: Account stores `domain = "zoho.com"`; a `--product desk` flag triggers lookup of `desk.zoho.com` from a hardcoded table.
- **ALT-002**: **Rejection Reason**: Requires maintaining a product-to-subdomain table that grows with every new Zoho product and datacenter. Silently breaks if Zoho changes a subdomain. The agent consumer does not benefit from this abstraction.

##### Default base URL per account

- **ALT-003**: **Description**: Store a default `base_url` on `AccountEntry`; omit `--base-url` when the default is set.
- **ALT-004**: **Rejection Reason**: Most developers and agents need to call multiple products from the same account. A single default base URL on the account would not serve the multi-product use case. Introduces state that can silently misdirect calls.

##### Product-aware subcommands (`zapi-cli desk tickets list`)

- **ALT-005**: **Description**: Bake product knowledge into the CLI command structure; each Zoho product gets its own command group.
- **ALT-006**: **Rejection Reason**: Turns the project into an ever-expanding product-specific CLI. Contradicts the core design goal of being a general-purpose invoker. Requires deep knowledge of every Zoho product API in the CLI codebase.

## Implementation Notes

- **IMP-001**: `ApiRequest.BaseUrl` is a required property; `ApiClient.CallAsync` assembles the final URL as `new Uri(new Uri(request.BaseUrl), request.Path)` and validates the host against the allowlist (see ADR-0006) before sending.
- **IMP-002**: `ApiTraceEntry.BaseUrl` records the raw `--base-url` value; `ApiTraceEntry.Url` records the fully assembled URL — both fields are present to allow trace filtering by product base URL in v2.
- **IMP-003**: API Registry entries (`registry.json`) each carry a `base_url` field so that a future `api call --id <registry-id>` can resolve to the correct base URL without changing `ApiClient` internals.
- **IMP-004**: Document the required `--base-url` pattern prominently in the CLI `--help` output and README — include concrete per-product examples for Desk, Cliq, and CRM.

## References

- **REF-001**: [tech_spec.md — Section 4: Group `api`](../tech_spec.md#group-api)
- **REF-002**: [tech_spec.md — Section 9: API Client](../tech_spec.md#9-api-client)
- **REF-003**: [tech_spec.md — Section 10: Trace Sessions](../tech_spec.md#10-trace-sessions)
- **REF-004**: [ADR-0006: Host Allowlist as Compile-Time Security Control](./adr-0006-host-allowlist-compile-time-control.md)
