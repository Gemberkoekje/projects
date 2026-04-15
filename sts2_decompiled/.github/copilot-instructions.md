# Copilot Instructions

## General Guidelines
- Under almost any circumstances, do not use nullables (?) - they are generally not needed. Only use them in very specific circumstances where there's no other way to differentiate between an entity and no entity, or consider using `Result<T>`. Do NOT add `#nullable enable` as a workaround for CS8632 warnings — remove the `?` annotations instead.
- All enums should have an empty value.
- Implicit usings are disabled in this workspace (via project build props). This is intentional. All System.* and SDK usings must be explicitly declared in `GlobalUsings.cs` or as local usings.

## Project Guidelines
- For this STS2 analysis project, user prefers practical recommendations for API-usable LLMs and wants low-cost model routing: use Haiku-class models for large mechanical annotation batches, and stronger models (Sonnet-class) for archetype discovery/synergy reasoning. User has personal Copilot Pro+ and Claude Pro subscriptions.
- In Qowaiv.Validation.Abstractions, use non-generic `Result` (with `Result.OK` for success and `Result.WithMessages(...)` for failure) when no value needs to be returned, instead of `Result<bool>`.
- When using Qowaiv Result, always check `IsValid` before accessing `Result.Value`. Accessing `Value` on an invalid result throws an exception.
- For the STS2 analysis pipeline, compute entity strength ratings after pairwise synergy edges to ensure ratings reflect confirmed synergy relevance rather than pre-edge heuristics.