# DatumPrikker — Implementation Plan

## Status Snapshot (updated again)

- ✅ **Infra + DB bootstrap** completed in code (PostgreSQL resource in AppHost, EF Core Npgsql wiring in ApiService, domain entities + DbContext + constraints).
- ✅ **Core API v1 endpoints** implemented in ApiService (`create/list/get/respond/results`) with share token generation, computed open/closed checks, and response upsert semantics.
- ✅ **Dedupe behavior** implemented with unique index `(PollOptionId, RespondentKey)` and last-write-wins updates.
- ✅ **BFF auth baseline (Web)** implemented:
  - cookie auth + Google + Microsoft provider wiring,
  - login/logout endpoints,
  - Blazor authorization state integration,
  - server-to-server poll client forwarding owner/respondent identity headers.
- ✅ **UI core workflows implemented (Web)**:
  - CreatePoll form with slot add/remove + API create,
  - RespondPoll form with Yes/No/Maybe + reason submission,
  - PollResults owner view with ranked totals and respondent details,
  - Dashboard links into create/respond/results flows.
- ✅ **Anonymous respond rate limiting (API)** implemented:
  - fixed window policy: 30 requests per 5 minutes,
  - partition key: `IP + shareToken`,
  - authenticated respondents bypass limiter.
- ✅ **Weather sample cleanup (Web)** started and applied:
  - removed sample weather client registration,
  - removed Weather and Counter sample pages.
- ✅ **DB migrations** cutover completed (startup uses `MigrateAsync`; initial migration applied).
- ✅ **Redis-backed cache strategy** completed (output cache on anonymous poll detail endpoint with 30s TTL, tag-based eviction on new response submission).
- ✅ **UI polish** completed (clipboard copy button on dashboard, specific validation/error messages for closed poll, rate limit, and empty responses).
- ✅ **Targeted tests** completed (dedupe/last-write-wins, owner-only results authz, closed-poll conflict, rate-limit behavior).
- ✅ **Final cleanup** completed (no weather/template leftovers remain, docs updated).

---

## Overview

A "Doodle-style" scheduling poll app built on the existing .NET 10 Aspire solution.

- **DatumPrikker.Web** — Blazor Server frontend (interactive UI + BFF)
- **DatumPrikker.ApiService** — internal REST API (business logic + persistence)
- **DatumPrikker.AppHost** — Aspire orchestrator (PostgreSQL + Redis)
- **DatumPrikker.Tests** — unit / integration tests

---

## 1. Data Model (ApiService)

### Entities

```
Poll
├─ Id                   : Guid
├─ Title                : string
├─ Description          : string
├─ OwnerIdentityId      : string          (provider-qualified owner id, e.g. "google:123" / "microsoft:456")
├─ ShareToken           : string          (short URL-safe token, e.g. 8-char base62)
├─ ClosesAtUtc          : DateTimeOffset? (null = no deadline)
├─ ClosedAtUtc          : DateTimeOffset? (null = not manually closed)
├─ CreatedAtUtc         : DateTimeOffset
├─ UpdatedAtUtc         : DateTimeOffset
└─ Options              : List<PollOption>

PollOption
├─ Id                   : Guid
├─ PollId               : Guid
├─ Date                 : DateOnly
├─ StartTime            : TimeOnly
├─ EndTime              : TimeOnly
├─ CreatedAtUtc         : DateTimeOffset
├─ UpdatedAtUtc         : DateTimeOffset
└─ Responses            : List<Response>

Response
├─ Id                   : Guid
├─ PollOptionId         : Guid
├─ RespondentKey        : string          (dedupe key)
├─ RespondentName       : string          (display name)
├─ Availability         : Availability
├─ Reason               : string
├─ CreatedAtUtc         : DateTimeOffset
└─ UpdatedAtUtc         : DateTimeOffset
```

### Enums

```
Availability { None = 0, Yes = 1, No = 2, Maybe = 3 }
```

### Open/closed state rule (computed, no stored status)

A poll is considered open when:

- `ClosedAtUtc is null`, and
- (`ClosesAtUtc is null` OR `ClosesAtUtc > now`)

This avoids redundant state between separate status fields.

### Constraints and deduplication

- Unique index on `(PollOptionId, RespondentKey)`.
- `POST /respond` behavior: **last-write-wins** (upsert existing respondent rows for the same option).
- `RespondentKey` rules:
  - Authenticated: derived server-side from claims (`{provider}:{subject}`), never trusted from request body.
  - Anonymous: derived from normalized name in the request (`anon:{normalizedName}`) for v1.
    - Tradeoff note: two different anonymous people with the same name on the same option will collide and overwrite each other in v1.

### Storage

- Add **PostgreSQL** via `Aspire.Hosting.PostgreSQL` + EF Core.
- Single `DatumPrikkerDbContext` in ApiService with migrations.

---

## 2. Authentication & Access Model

## Decision: Blazor Server BFF pattern (not JWT minting)

Blazor Server does not issue JWTs by default. For v1, use a BFF-style flow:

- Browser authenticates to **Web** using cookie auth + external OAuth providers.
- Browser never calls ApiService directly.
- **Web** calls **ApiService** server-to-server over internal Aspire networking.
- Owner identity is forwarded by Web using trusted internal headers (or internal contract), validated as internal traffic.
- Production hardening requirement: ApiService must accept identity headers only from trusted Web origin (private networking + shared secret or mTLS/service-to-service auth).

### Web (Blazor)

- `Microsoft.AspNetCore.Authentication.Google` + `Microsoft.AspNetCore.Authentication.MicrosoftAccount`.
- Cookie auth with external OAuth redirect flow.
- `AuthenticationStateProvider` exposes claims to Blazor components.
- Login page with buttons: "Sign in with Google" / "Sign in with Microsoft".

### ApiService

- Internal API (not public internet endpoint).
- Anonymous public poll read/respond actions are still exposed through Web routes.

### Public poll behavior for logged-in users

- If logged in and visiting `/poll/{shareToken}`, respondent name is pre-filled from claims.
- For authenticated submissions, server persists claim-derived name and identity key.

---

## 3. API Endpoints (ApiService)

| Method | Route | Auth Context | Purpose |
|--------|-------|--------------|---------|
| POST   | `/api/polls` | Owner required | Create poll with options |
| GET    | `/api/polls` | Owner required | List owner polls (+ response counts) |
| GET    | `/api/polls/{shareToken}` | Anonymous allowed | Get poll details for voting view |
| GET    | `/api/polls/{shareToken}/results` | Owner required | Get ranked results |
| POST   | `/api/polls/{shareToken}/respond` | Anonymous or logged-in | Upsert responses for all options |

Results visibility decision for v1: **owner-only**.

### Respond DTO (explicit batch shape)

`POST /api/polls/{shareToken}/respond` accepts:

- `respondentName` (required for anonymous, ignored for authenticated)
- `responses: List<ResponseInput>` where each item is:
  - `pollOptionId: Guid`
  - `availability: Availability`
  - `reason: string`

### Deliberate v1 omissions

- No poll editing (`PUT`) and no poll deletion (`DELETE`) in v1.
- No pagination on `GET /api/polls` in v1 (add TODO marker in API contract).

---

## 4. Blazor Pages (Web)

| Route | Component | Auth | Description |
|-------|-----------|------|-------------|
| `/` | `Home.razor` | No | Landing page with login CTA |
| `/login` | `Login.razor` | No | Google + Microsoft sign-in buttons |
| `/dashboard` | `Dashboard.razor` | Yes | Owner polls, response counts, links to create/respond/results |
| `/polls/create` | `CreatePoll.razor` | Yes | Form: title, description, date/time slots, optional close date |
| `/poll/{ShareToken}` | `RespondPoll.razor` | No | Vote page: Yes/No/Maybe per slot + optional reason |
| `/poll/{ShareToken}/results` | `PollResults.razor` | Yes (owner) | Ranked options with totals + respondent details |

---

## 5. Results Ranking Logic

For each `PollOption`, compute:

1. **YesCount** — responses with `Availability.Yes`
2. **MaybeCount** — responses with `Availability.Maybe`
3. **NoCount** — responses with `Availability.No`

Sort descending by `YesCount`, then descending by `MaybeCount`.

Display row: date/time, Yes, Maybe, No, expandable respondents + reasons.

---

## 6. Share Link

Format: `https://<host>/poll/{ShareToken}`

- `ShareToken` generated server-side (8-char base62, collision-checked).
- Copy-to-clipboard button on dashboard.

---

## 7. Infrastructure Changes

### AppHost (`Program.cs`)

(`cache` is assumed to be the existing Redis resource already declared earlier in `Program.cs`.)

```
var postgres = builder.AddPostgres("postgres").AddDatabase("datumprikker");

var apiService = builder.AddProject<Projects.DatumPrikker_ApiService>("apiservice")
    .WithReference(postgres);

var web = builder.AddProject<Projects.DatumPrikker_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WithReference(apiService);
```

### Redis purpose (explicit)

- Use Redis output caching for anonymous high-read endpoints (`poll details` and `public results snapshots` if exposed).
- Keep short TTL and evict on new response submission.

### Rate limiting

- Add basic rate limiting for anonymous respond flow:
  - partition by IP + share token,
  - fixed window (example: 30 requests / 5 minutes),
  - return 429 on abuse.

### Packages to add

| Project | Package |
|---------|---------|
| AppHost | `Aspire.Hosting.PostgreSQL` |
| ApiService | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| Web | `Microsoft.AspNetCore.Authentication.Google` |
| Web | `Microsoft.AspNetCore.Authentication.MicrosoftAccount` |

---

## 8. Implementation Order

1. ✅ **Infra + DB** — add PostgreSQL, entities, constraints, timestamps, migrations.
2. ✅ **Core API** — create/list/get/respond/results endpoints with upsert behavior.
3. ✅ **BFF auth wiring** — Google/Microsoft login in Web and internal identity propagation scaffold to API.
4. ✅ **UI pages core flow** — CreatePoll, RespondPoll, PollResults implemented with API integration.
5. ✅ **Poll closing rules (API)** — computed open/closed logic enforced in respond endpoint.
6. ✅ **Rate limiting (API)** — anonymous respond throttling implemented.
7. ✅ **Caching strategy** — Redis-backed output cache on anonymous poll detail with tag eviction on respond.
8. ✅ **Tests** — integration tests (dedupe, authz, respond flow, closure, rate-limit).
9. ✅ **Cleanup + UI polish** — template remnants removed, clipboard copy, specific error messages.

Implementation TODO: `PollOption.UpdatedAtUtc` will only change once edit endpoints are introduced in a later version.

---

## 9. Continuation Plan (completed)

### 9.1 Migrations cutover ✅

- Replaced runtime `EnsureCreatedAsync` with startup migration flow (`Database.MigrateAsync`).
- Initial migration created and verified.

### 9.2 Redis cache strategy ✅

- Output cache policy applied to anonymous poll detail endpoint (30s TTL, `poll-details` tag).
- Owner-only results uncached by default in v1.
- Cache evicted by tag on successful respond upsert.

### 9.3 UI polish ✅

- Dashboard copy-link button with clipboard JS interop and "Copied!" feedback (auto-resets after 2s).
- RespondPoll shows specific messages for closed poll (409), rate limit (429), and empty-response validation.
- `PollClosedException` and `RateLimitedException` added to `PollsApiClient` for typed error handling.

### 9.4 Tests ✅

- Integration tests cover: dedupe/last-write-wins, owner-only results authorization, closed-poll conflict, anonymous rate-limit behavior.

### 9.5 Final cleanup ✅

- No weather/counter template artifacts remain.
- Plan updated to match implemented behavior.
