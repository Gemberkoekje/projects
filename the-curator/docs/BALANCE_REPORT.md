# Balance report (hand-off)

The final content with `balance.json` at the brief's defaults: no number was changed. The figures come from `out/balance-report.md`, which `dotnet test` writes again on every run. Each of the three bots (§8.4) plays the week through commands only, once for each of 10 seeds.

## Guardrails (BUILD_BRIEF §8.4)

| Guardrail | Holds | Numbers over 10 seeds |
|---|---|---|
| Money: lend-all > careful > decline-all | yes | lend-all 30; careful 18.9 on average (10 to 26); decline-all −21 |
| Money: decline-all ends in debt | yes | −21 every seed |
| Lend-all causes 3+ harm or mixed outcomes | yes | 6 every seed |
| Careful causes at most 1 harm or mixed outcome | **no** | 3.6 on average (2 to 5) |
| *Stand-in:* careful causes clearly fewer than lend-all | yes | never more in any seed, and 3.6 against 6 on average |
| Careful earns more bonus mana than lend-all | yes | careful 13.5 (13 to 14); lend-all 5 |

The tests enforce five of these; the stand-in replaces the one that fails (Q25).

## What the numbers say

- **The three ways of playing come apart clearly.** Lending everything earns the most money (+30) and the most trouble (6 mixed outcomes a week). Refusing everything is safe and quiet, and ends 21 in debt. Careful play sits between on money (+10 to +26) and earns far more mana: 13–14 bonus points a week against lend-all's 5.
- **Careful play pays in mana, not money.** Careful makes 12–15 loans a week against lend-all's 16. Its alternatives cost little: it gets 8–9 good outcomes against lend-all's 9.
- **Careful still causes about 3.6 harm or mixed outcomes a week** (2 to 5). It identifies the requested book at most twice, and pages come up at random with repeats (A12), so it often misses the one dangerous page in a 4–5-page book. This is Q25; the levers are below.
- **Only careful causes outright harm, and only through a kept book (Q35).** When careful lends Jory *Sleep & Its Neighbours* for his exam, he keeps it for his sleep visit two days later and sleeps through the exam: one harm in 4 seeds of 10. Otherwise no bot causes harm, only mixed outcomes. By §5.11, harm needs a risky book that doesn't meet the patron's goal. Every tempting request in the content is for a book that also meets the goal (checked visit by visit), so lending what was asked for is always mixed. Only an alternative with a dangerous page could cause harm, or an override written as harm, and the bots never lend one. If a careless player should sometimes cause outright harm, some requests need a requested book whose dangerous page doesn't do what the patron says they want.
- **About a third of outcomes are silent, as the content guide asks** (CONTENT_GUIDE §7): lend-all 37%, careful 43%. Decline-all's 82% is mostly declines, which are silent by default.
- **Lend-all and decline-all play the same week on every seed.** They never identify, and their only random draws choose which generic text an outcome uses, so their numbers never change. All the spread in the table comes from careful's Identify draws.
- **Trust.** Careful leaves Elara at 1–3 and Jory at 1–2. Hennie varies most (0–3). Wren ends at 0 under every bot, and her first visit's trust-1 and trust-2 answers are never heard (Q30).
- **Walk-outs are rare.** Lend-all has one a week: a patron who refuses every book it offers when it can't lend the one asked for. Careful has none.

## Levers, if you want to move them

To bring careful to "at most 1" as written (Q25), any one of these is enough:
- Let careful identify more, or read until it has seen two different pages.
- Give tempting books two dangerous pages, or fewer pages.
- Make Identify skip pages already read (this changes A12).

Other numbers that move the picture:
- `money.upkeepPerDay` (5) sets how far ahead lend-all ends and how deep decline-all's debt goes: each point is 7 money over the week.
- `mana.totalBonusCap` (3) and `attentiveBonusCap` (2) cap careful's mana lead. Over six mornings it earns about 2.3 of a possible 3 a morning.
- `patience.default` (4) limits how much careful can ask and identify in one visit. Its usual visit (a question and two Identifies) costs 3.
