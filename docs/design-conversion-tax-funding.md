# Design note: why conversion-tax funding is a single fixed rule

*Audience: developers reading this repo, and reviewers looking at how the modeling decisions were made. This is a design-decision record, not a user guide.*

## The goal and the three "where does the money come from" choices

The app's single objective is to **maximize the spending that survives 80–85% of simulated markets** for a household's inputs. Three modeling choices serve that goal, and the app makes all three itself (users don't pick them):

1. **Withdrawal order** — which accounts pay for each year's spending (`WithdrawalStrategy`).
2. **Roth-conversion target** — how far each year's conversion fills the brackets (`RothConversionTarget`).
3. **Conversion-tax funding** — where the tax on a conversion comes from (`ConversionTaxFunding`).

## The decision

The first two are chosen **per household, at runtime**: the engine runs the candidate pairs on that household's own seeded market paths and keeps the best (`AutomaticStrategy.Candidates` for the Scenario runner; `SpendingOptimizer.BestCandidate` for the Optimizer).

The third — **funding — is a single fixed rule**: `FromConversion`, where the tax is withheld from the converted amount (Tax Deferred falls by the full amount; Roth rises by the amount minus tax). It is **not** part of the per-household search.

## Why `FromConversion` works as a fixed default

**The mechanic.** Paying the tax out of the conversion is, dollar for dollar, the same as simply converting less. It needs no outside cash, so a conversion can never stall for lack of a funding source. For most households that makes it indistinguishable from the other rules — the plan comes out the same.

**The evidence.** The Strategy Lab's `--set funding` run compares each rule against `F0` (Brokerage-only — the app before it chose its own funding), on 1,000 households and the same markets, measured by the 82.5%-survival spend. Current published run (seed 2026, 1,000 scenarios, 200 paths, 2026-09-30):

| Funding rule (with Tax-optimized + 12% fill) | Mean | Median | Wins | Loses |
|---|---|---|---|---|
| `F0` Brokerage-only (baseline) | — | — | — | — |
| **`F1` FromConversion (the app's choice)** | **+3.34%** | **0.00%** | 11% | 6.2% |
| `F2` Brokerage-then-conversion | +3.32% | 0.00% | 11% | 6.8% |
| `F3` Bridge-aware | +3.32% | 0.00% | 11% | 6.4% |

(95% interval for `F1`: +2.4% to +4.4%. "Wins/Loses" count households changed by more than ±0.5%.)

The key reads: the **median change is 0.00%** — funding changes nothing for the typical household (~83% of them). It **helps about 11%** a great deal (roughly +30% for the ~12% it helps, almost all early retirees whose Brokerage would otherwise run dry and stall their conversions), and **modestly hurts about 6%** (around −1.9% on those). `FromConversion` is the best single fixed choice: the largest average upside, a median that doesn't hurt the typical household, and a small, rare downside.

## Why funding is *not* chosen per household (the trade-off)

It could be — but the payoff is small and uneven:

- Once the **order** and **conversion target** adapt per household, they absorb most of the household-specific variation; funding's *residual* effect is small for the large majority (hence the 0.00% median).
- The three non-baseline rules differ by only ~0.02% on average (`F1` +3.34% vs `F2`/`F3` +3.32%), so even a "smarter" funding choice buys almost nothing on average.
- Adding `F0`–`F3` to the candidate grid roughly **4×'s** the per-household search, which the Optimizer already keeps deliberately bounded for responsiveness.
- The downside of fixing it is bounded and rare.

So this is a deliberate **simplicity/robustness** trade-off, not a claim that one rule is optimal everywhere.

## Acknowledged limitation

`FromConversion` is **not** per-household optimal. By the numbers above, about **6% of households would do slightly better** under a different funding rule. The app accepts that small, uncommon shortfall rather than widen the search. The Model Info page shows this win/loss distribution to users directly.

## Roadmap note (a candidate "PRO" enhancement)

Making funding a **fourth per-household dimension** is a clean, bounded enhancement and a plausible flagged/paid feature:

- Add the `ConversionTaxFunding` rules (`F0`–`F3`) to `AutomaticStrategy.Candidates` and the Optimizer's `BestCandidate`, so each run also picks its best funding.
- Gate it behind a feature flag; rerun the Strategy Lab (`--set funding` plus a re-measure of the main grid) to confirm the gain and update Model Info.
- Expect ~4× candidates in the affected search, and the beneficiaries to be mainly that ~6% who currently come out slightly behind.

A paid **tier** (entitlement, payment, gating UI) is a separate product concern and is out of scope for this note — only the engine/search change is described here.

## Where this lives in the code and data

- **Rules:** `MonteCarloSimulation.Core/ConversionTaxFunding.cs`; applied in `RothConversion.Apply` (`MonteCarloSimulation.Core/RothConversion.cs`).
- **Per-household search (the two choices that *do* adapt):** `MonteCarloSimulation.Core/AutomaticStrategy.cs` (`Candidates`); `MonteCarloSimulation.Optimizer/SpendingOptimizer.cs` (`BestCandidate`).
- **Measurement:** the Strategy Lab's funding comparison (`MonteCarloSimulation.StrategyLab`, `--set funding`, `Analysis`), published to `MonteCarloSimulation.Web/wwwroot/model-info/strategy-lab.json`.
- **User-facing explanation:** the Model Info page's "Where conversion tax comes from" section (`MonteCarloSimulation.Web/wwwroot/model-info.js`).

The figures in this note are from the published run dated above and **refresh whenever the Strategy Lab is rerun** — the Model Info page / `strategy-lab.json` is the live source of truth; treat the numbers here as a dated snapshot.
