* Do an API call to the CoPilot usage API, save the results.
* Do an API call to the ClaudeAI usage API, save the results.
* Make a burndown based on the results. CoPilot resets on the 1st of the month, ClaudeAI should have it's reset date/time in the API.
* If the recent usage (between last check and this one) is significantly higher than the average based on the burndown, mail an alert.
* Once per day, mail a report with the current burndown, information on whether I'm on par to not hit any limits, etc.

Step-by-step plan
Step 1 — Project scaffold GPT-5 Mini
Create a .NET console app (dotnet new console). Do not use the `.net` project, create a new project. add NuGet packages: Marten, Npgsql, MailKit (or SendGrid), Microsoft.Extensions.Hosting, Microsoft.Extensions.Http. Set up appsettings.json with connection strings, API tokens, and SMTP config. Wire up IHostBuilder with DI so the whole thing runs as a hosted service that exits cleanly after one execution cycle — perfect for cron.

Step 2 — Marten document/event schema Sonnet
Design the Marten schema. You'll want two event streams (one per provider) using Marten's event sourcing. Suggested events:

UsageSnapshotRecorded { Provider, Timestamp, PeriodStart, PeriodEnd, SeatsUsed, TokensUsed, ... }
AlertFired { Provider, Timestamp, Reason, DeltaUsage, AverageUsage }
DailyReportSent { Timestamp }

Add a UsageSummaryProjection aggregate that Marten maintains automatically — this gives you the current-period state without replaying the full stream every run. Sonnet is good at translating a design like this into idiomatic Marten C#.

Step 3 — CoPilot usage API client GPT-5 Mini
Implement ICopilotUsageClient using IHttpClientFactory. The GitHub CoPilot usage API is well-documented and fairly simple — seats used, active users, breakdown by editor. Map the response to your UsageSnapshot model. Handle auth via Authorization: Bearer <token> header. Inject the reset date as "1st of current month" since it's always fixed. GPT-5 Mini can handle this boilerplate efficiently.

Step 4 — Claude usage API client GPT-5 Mini
Same pattern as Step 3 but for the Anthropic usage API. The key difference is extracting the period reset date/time from the response envelope rather than hardcoding it. Parse and store this on the snapshot so your burndown math always has the right period window.

Step 5 — Burndown calculation Sonnet
This is the interesting math. For each provider:

Ideal line: budget / days_in_period × days_elapsed — what you should have consumed by now
Actual line: current consumption from the latest snapshot
Projection: actual / days_elapsed × days_in_period — where you'll land if pace holds
Delta: this_snapshot.usage - previous_snapshot.usage — consumption since last run
Rolling average delta: mean of the last N deltas (e.g. last 7 runs)

Return a BurndownReport record with all of these. Sonnet is a good fit here — it's not trivial logic but it's not architecture-level design either.

Step 6 — Spike detection Sonnet
Compare the current delta to the rolling average. A reasonable starting threshold: alert if delta > average × 2.5 (i.e. this interval consumed more than 2.5× the typical interval). Make the multiplier configurable in appsettings.json. Also alert if the projection exceeds budget (e.g. on track to hit 110% of limit). Append the fired alert as a Marten event so you don't re-alert on the same spike next run.

Step 7 — Daily report gate Haiku
Before sending the daily report, query Marten for the most recent DailyReportSent event. If one exists with today's date (in your local timezone — be explicit), skip. Otherwise proceed and record the event after sending. This is simple but worth getting right so you don't flood your inbox if cron runs frequently.

Step 8 — Email rendering Sonnet
Build two email templates as inline HTML strings (no Razor needed for this scale):

Alert email: provider name, current delta, rolling average, a simple "you used 3× your average since the last check" message, and remaining budget.
Daily report email: burndown table for each provider (budget, used, remaining, projected end-of-period, days left), traffic light status (on track / at risk / over), and a short plain-English summary.

Use MailKit for SMTP or SendGrid's REST API. Sonnet handles template generation and conditional formatting well.

Step 9 — Orchestrator / main loop Sonnet
Wire it all together in an IHostedService.StartAsync:
fetch CoPilot → persist → fetch Claude → persist
→ calculate burndown → check spike → maybe alert
→ check daily gate → maybe send report
→ exit
Keep it linear and sequential — no need for parallelism unless the API calls become slow, and even then a simple Task.WhenAll on the two fetches is fine. Sonnet is good at composing this kind of orchestration without over-engineering it.

Step 10 — Configuration, secrets, deployment GPT-5 Mini
Add environment variable overrides for all secrets (API tokens, SMTP credentials) so they're never in appsettings.json in source control. Write a Dockerfile and a simple cron entry (or a Kubernetes CronJob manifest if you want it on your k3s cluster). GPT-5 Mini can generate the boilerplate confidently here.

Step 11 — Testing the burndown and spike logic Opus
Write unit tests for the burndown math and spike detection with synthetic data — this is where subtle bugs hide. Things like: what happens when the period just reset and there's only one snapshot? What if deltas are zero for several runs? Does the rolling average window behave correctly at the edges? Opus is worth using here because it's better at reasoning about edge cases and writing tests that actually find bugs rather than just confirm the happy path.