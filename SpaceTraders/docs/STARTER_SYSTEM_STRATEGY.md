# SpaceTraders — Starter System Strategy

> **Goal:** Get your jump gate to 100% construction as fast as possible. Everything else is secondary.
> Experienced players complete this in **18–24 hours** after reset — that's your benchmark.

---

## TL;DR Priority Order

1. Grow your starter markets by buying fab mats in bulk and supplying raw-material precursors
2. Mine to supplement income and stimulate market growth
3. **Hoard advanced circuits** — do not sell them until the gate is complete
4. Buy fab mats in maximum quantities when prices are reasonable (~2 500 cr/unit)
5. Get through that gate as fast as possible — everything interesting is on the other side

---

## Phase 1 — Market Growth (Immediate Priority)

The core feedback loop in your starter system:

```
buy goods → markets grow → prices recover → repeat at better volumes
```

Fab mats are your primary construction currency.

### Key rules

| Rule | Detail |
|---|---|
| Target price | ~2 500 credits per unit is a reasonable entry point |
| Buy in bulk | Purchase the maximum quantity you can afford in one transaction — each purchase increments the price, so spreading buys across many small orders wastes money |
| Volume ceiling | Fab mats cap at **60 units** trade volume in starter systems — that is your ceiling per buy cycle |
| Wait for recovery | Do not spam-buy at inflated prices; wait for prices to recover between purchase cycles |

### Advanced circuits — special handling

- **Do not sell advanced circuits on the market** until your gate is 100% complete (assuming your starter system has a buyer for them)
- Every unit sold to the market is a unit not contributing to construction
- Treat your advanced circuit stockpile as ring-fenced construction budget

---

## Phase 2 — Supplement with Mining

Trading alone may not generate credits fast enough. Mining raw materials and supplying them to the market serves two purposes:

1. **Income** — direct credit generation
2. **Market stimulation** — supplying precursors grows market volume, which lowers your future fab mat purchase prices

Mining is not strictly required but it helps close the credit gap when market prices are recovering.

### Supply chain priming — prime the full transitive closure

When stimulating supply to produce jump gate construction materials (e.g. `FAB_MAT`, `ADVANCED_CIRCUIT`), do **not** limit your attention to the direct inputs of those top-level materials. The entire transitive closure of the supply chain benefits from being kept healthy:

- `FAB_MAT` requires upstream goods (e.g. `IRON`, `ALUMINUM`), which in turn depend on their own raw-material imports.
- `ADVANCED_CIRCUIT` requires `MICROPROCESSORS` or `ELECTRONICS`, which require `SILICON_CRYSTALS`, `COPPER` etc.
- A bottleneck anywhere in the chain will eventually restrict the goods at the top.

**Supply level target:** you do not need to push every node to `ABUNDANT`. The goal is to keep every market in the chain **above `LIMITED`**. The relevant supply levels in ascending order are:

```
SCARCE → LIMITED → MODERATE → HIGH → ABUNDANT
```

A good in `LIMITED` or `SCARCE` is being consumed faster than it is produced; supplying it will unblock the downstream chain. Once a market is at `MODERATE` or above it can sustain itself for a few cycles without intervention.

**Practical priming checklist:**

1. Start with the jump gate construction requirements and look up which goods each required material imports.
2. Follow each import one level deeper — what does *that* market import? Repeat until you reach raw ores / gases with no production inputs.
3. For every node in that tree that is currently at `LIMITED` or `SCARCE`, assign a mining or trading ship to supply it.
4. Prioritise the deepest bottlenecks first: a `SCARCE` raw material will cascade shortages up the entire chain.
5. Once a market recovers to `MODERATE` or above, deprioritise it and redirect effort to the next bottleneck.

### Recommended mining approach

1. Identify the nearest asteroid cluster with `MINERAL_DEPOSITS` or `COMMON_METAL_DEPOSITS`
2. Mine until cargo is full
3. Sell at a marketplace that imports ores or accepts exchange goods
4. Prioritise selling goods that are also market precursors for jump gate materials — the deeper in the chain the better

---

## Phase 3 — Construction Budget Discipline

### Relax the 30% budget cap

Limiting your construction routine to 30 % of credits per hour is too conservative. The price mechanics punish slow, small purchases:

- You want to dump **large amounts** when the price is right, not trickle-buy across many cycles
- Consider removing the hard percentage cap and instead letting your agent buy aggressively whenever the per-unit price is at or below your target threshold (~2 500 cr)
- Keep a minimum cash reserve sufficient to cover fleet operating costs (fuel, refuelling stops), but beyond that reserve, deploy credits into construction materials

### Decision framework for each buy cycle

```
if unit_price <= 2_500 AND available_credits > operating_reserve:
    buy max(affordable_units, 60)   // never exceed the 60-unit volume cap
else:
    wait for price recovery
```

---

## Phase 4 — After the Gate: Expansion

Once construction completes and the gate activates:

### Immediate next steps

1. **Scout the gate network** — send your command ship or a scout through the gate to chart connected systems
2. **Identify expansion targets** — look for systems with active marketplaces, shipyards, or resource-rich asteroid belts
3. **Start thinking about market probes** — the top leaderboard agents (2 000+ ships) are mostly probe fleets doing market coverage at scale

### Probe fleet scaling notes

- Probes do not all move simultaneously — API rate limits are **per-call**, not per-fleet
- A large stationary probe fleet provides continuous market data at near-zero operating cost
- Deploy probes to new system marketplaces as early as possible to build the price-data picture needed for profitable cross-system trading routes

---

## Quick-Reference Checklist

Use this as a run-time checklist each reset:

- [ ] Check jump gate construction requirements on arrival
- [ ] Identify fab mat supplier marketplaces in the starter system
- [ ] Set target buy price at ≤ 2 500 cr/unit for fab mats
- [ ] Ring-fence advanced circuit stockpile — no market sales until gate is done
- [ ] Start a mining loop to supplement income and stimulate markets
- [ ] Map the full transitive supply chain for each construction material (follow imports recursively to raw inputs)
- [ ] Identify every node in that chain at `LIMITED` or `SCARCE` supply and prioritise supplying those first
- [ ] Re-check supply levels each cycle; deprioritise recovered markets and redirect effort to remaining bottlenecks
- [ ] Remove or relax the 30 % hourly budget cap on construction purchases
- [ ] Buy fab mats in single max-quantity transactions (up to 60 units) when price is right
- [ ] Wait for price recovery between buy cycles
- [ ] Deliver all construction materials; reach 100 % gate completion
- [ ] Scout gate connections immediately after activation
- [ ] Begin probe-fleet deployment in connected systems
