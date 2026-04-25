# SpaceTraders – Plan

> ⚠️ This file is superseded. The canonical implementation plan lives in [`docs/plan/`](docs/plan/).
>
> Start here: **[docs/plan/00-overview.md](docs/plan/00-overview.md)**

---

# SpaceTraders API integration plan (archived)

## Goal
Add code in `SpaceTraders.Infrastructure.SpaceTradersAPI` so the solution can communicate with the SpaceTraders v2 API defined at:
- `https://api.spacetraders.io/v2/documentation/json`

The integration should provide a clean, reusable client for both public and authenticated endpoints, with room to expand as more game features are implemented.

## Current workspace state
- `SpaceTraders.Infrastructure.SpaceTradersAPI` now contains a first-pass typed client implementation rather than only placeholder code.
- `SpaceTraders.Application` is still empty, so there are no existing abstractions yet.
- The API definition is OpenAPI 3.0.1 with base URL:
  - `https://api.spacetraders.io/v2`
- Authentication uses bearer tokens:
  - `AgentToken` for most authenticated gameplay endpoints
  - `AccountToken` for account-level registration endpoints

## Progress update
Completed in this pass:
- added `SpaceTradersApiOptions`
- added `ISpaceTradersApiClient`
- added `SpaceTradersApiClient` using `HttpClient`
- updated DI registration to support typed client and options configuration
- implemented public endpoints for status, factions, agents, systems, and waypoints
- implemented authenticated endpoints for register, current agent, ships, ship by symbol, and contracts
- added initial DTOs for common wrappers, status, accounts, agents, factions, systems, fleet, and contracts
- added `SpaceTradersApiException` for HTTP and deserialization failures
- validated that the solution builds successfully

Remaining work:
- verify runtime connectivity against live public endpoints
- verify authenticated calls with configured bearer tokens
- harden/expand DTOs as consuming features require more fields
- add automated tests once a test project exists

## Proposed design

### 1. Replace placeholder code with a typed API client package structure
Create a small SDK-like client inside `SpaceTraders.Infrastructure.SpaceTradersAPI` with:
- `SpaceTradersApiOptions`
  - `BaseUrl`
  - `AgentToken`
  - `AccountToken`
- `ISpaceTradersApiClient` for the main entry point
- `SpaceTradersApiClient` implementation using `HttpClient`
- optional feature group clients if the file count starts growing:
  - `SystemsClient`
  - `AgentsClient`
  - `FactionsClient`
  - `FleetClient`
  - `ContractsClient`

Status:
- completed with a single typed client for the first iteration

A single typed client is enough for the first iteration; split by feature only if it becomes too large.

### 2. Add dependency injection registration
Expose an extension method such as:
- `AddSpaceTradersApi(...)`

This should:
- register options
- register a typed `HttpClient`
- set the API base address
- configure default headers like `Accept: application/json`
- register the concrete client and any helpers

Status:
- completed

This keeps consumption simple from `SpaceTraders.API` or other projects later.

### 3. Implement authentication handling
Add a lightweight auth mechanism that can:
- send no token for public endpoints
- send `AgentToken` for `/my/*` endpoints and optional authenticated public requests
- support `AccountToken` for `/register`

Recommended approach:
- keep token selection explicit in client methods for now
- centralize request creation in one helper method so auth behavior is not duplicated

Status:
- completed

## API surface for first implementation
Start with a minimal but useful subset that proves connectivity and covers the main API styles.

### Public endpoints
Implement methods for:
- `GET /` -> server status
- `GET /factions`
- `GET /factions/{factionSymbol}`
- `GET /agents`
- `GET /agents/{agentSymbol}`
- `GET /systems`
- `GET /systems/{systemSymbol}`
- `GET /systems/{systemSymbol}/waypoints`
- `GET /systems/{systemSymbol}/waypoints/{waypointSymbol}`

Status:
- completed

### Authenticated endpoints
Implement methods for:
- `POST /register`
- `GET /my/agent`
- `GET /my/ships`
- `GET /my/ships/{shipSymbol}`
- `GET /my/contracts`

Status:
- completed

This gives enough coverage to:
- verify connectivity
- register an agent
- authenticate
- inspect the player state
- query public world data

## Models and serialization

### 4. Create DTOs for the implemented endpoints
Create response/request models for only the subset above, not the entire OpenAPI file initially.

Model groups to add first:
- common wrappers
  - `ApiResponse<T>`
  - `PagedResponse<T>` or wrapper with `Data` and `Meta`
  - `Meta`
- auth/account models
  - `RegisterRequest`
  - `RegisterResponseData`
- world models
  - `Faction`
  - `PublicAgent`
  - `System`
  - `Waypoint`
- player models
  - `Agent`
  - `Ship`
  - `Contract`
- status models
  - server status response objects

Status:
- completed for the initial subset, with intentionally partial DTO coverage for larger schemas

### 5. Use `System.Text.Json`
Configure serializer settings for API compatibility:
- case-insensitive property matching if needed
- string enum support with `JsonStringEnumConverter`
- nullable-safe DTOs

Prefer records or simple sealed classes, following the style that best fits the project once implementation starts.

Status:
- completed using `System.Text.Json` with shared serializer settings

## Error handling

### 6. Add a consistent exception model
The API exposes error codes separately and may return structured error responses.

Add:
- `SpaceTradersApiException`
- optional error DTO if the API returns structured error payloads

The client should:
- throw a domain-specific exception for non-success responses
- include status code, endpoint, and response body when available
- avoid leaking raw `HttpRequestException` details alone

Status:
- completed for the exception type and request pipeline behavior

## Implementation steps

### Phase 1: Foundation
- remove `Class1.cs`
- add options, DI registration, client interface, client implementation
- add request helper methods for GET and POST
- add JSON serializer configuration

Status:
- completed

### Phase 2: Public API support
- implement status, factions, agents, and systems methods
- add DTOs for public responses
- verify base connectivity against `https://api.spacetraders.io/v2`

Status:
- implementation completed
- runtime connectivity verification still pending

### Phase 3: Authenticated API support
- implement registration and authenticated agent/fleet/contracts methods
- add token-aware request handling
- add DTOs for registration and `/my/*` responses

Status:
- implementation completed
- runtime auth verification still pending

### Phase 4: Validation
- add a minimal usage path from the consuming project later
- confirm public endpoint calls work without auth
- confirm authenticated endpoint calls send bearer tokens correctly
- validate deserialization for representative responses

Status:
- build validation completed
- runtime and automated test validation still pending

## Suggested folder layout
Inside `SpaceTraders.Infrastructure.SpaceTradersAPI`:

- `Configuration/`
  - `SpaceTradersApiOptions.cs`
- `DependencyInjection/`
  - `ServiceCollectionExtensions.cs`
- `Clients/`
  - `ISpaceTradersApiClient.cs`
  - `SpaceTradersApiClient.cs`
- `Models/Common/`
- `Models/Agents/`
- `Models/Factions/`
- `Models/Systems/`
- `Models/Fleet/`
- `Models/Contracts/`
- `Models/Accounts/`
- `Exceptions/`
  - `SpaceTradersApiException.cs`

## Notes from the API definition
- Base server URL: `https://api.spacetraders.io/v2`
- Public and authenticated access both exist for some endpoints
- `/register` requires `AccountToken`
- most `/my/*` endpoints require `AgentToken`
- paginated endpoints commonly use `page` and `limit`
- response bodies usually follow a `{ "data": ... }` or `{ "data": ..., "meta": ... }` shape

## Scope guardrails
To keep the first implementation manageable:
- do not implement every endpoint from the OpenAPI definition yet
- do not add websocket support in the first pass
- do not introduce external SDK/codegen tooling unless manual DTO creation becomes too costly
- do not mix gameplay logic into the infrastructure client

## Definition of done
The plan is complete when `SpaceTraders.Infrastructure.SpaceTradersAPI` contains:
- a typed `HttpClient`-based client
- DI registration
- DTOs for the initial endpoint subset
- public endpoint support
- bearer-token authenticated endpoint support
- structured error handling

And a consuming project can call the library to:
- fetch server status
- list factions or systems
- register or authenticate an agent
- retrieve the current agent and ships
