# Copilot Instructions

## General Guidelines
- Under almost any circumstances, do not use nullables (?) - they are generally not needed. Only use them in very specific circumstances where there's no other way to differentiate between an entity and no entity, or consider using `Result<T>`. Do NOT add `#nullable enable` as a workaround for CS8632 warnings — remove the `?` annotations instead.
- All enums should have an empty value.
- Implicit usings are disabled in this workspace (via project build props). This is intentional. All System.* and SDK usings must be explicitly declared in `GlobalUsings.cs` or as local usings.
- For this project, nullable `DateTimeOffset?` fields are explicitly allowed when they are the idiomatic representation of optional timestamps (e.g., `ClosesAtUtc`/`ClosedAtUtc`), even though general repo guidance discourages nullable usage.

## Project Guidelines
- In Qowaiv.Validation.Abstractions, use non-generic `Result` (with `Result.OK` for success and `Result.WithMessages(...)` for failure) when no value needs to be returned, instead of `Result<bool>`.
- When using Qowaiv Result, always check `IsValid` before accessing `Result.Value`. Accessing `Value` on an invalid result throws an exception.

## Architecture Considerations
- Ensure architecture plans explicitly cover the following aspects:
  - Response deduplication behavior
  - Blazor Server BFF vs. JWT authentication decision
  - Handling of authenticated respondent names
  - Deliberate omissions and edge-case controls in version 1, including closing, rate limiting, Redis purpose, and pagination.