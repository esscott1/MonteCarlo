# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Monte Carlo retirement-portfolio simulator: given an investment scenario, a starting balance, and an annual withdrawal, it runs many randomized simulations of portfolio performance over a number of years and reports survival rate and balance outcomes. Available as both a console app and a web GUI, both built on .NET 9 and sharing one simulation engine.

## Commands

```bash
# Build everything
dotnet build MonteCarlo.sln

# Build a single project
dotnet build MonteCarloSimulation.Core/MonteCarloSimulation.Core.csproj

# Run the console app (interactive prompts)
dotnet run --project MonteCarloSimulation1

# Run the web app, then open http://localhost:5091
dotnet run --project MonteCarloSimulation.Web

# Run the automated tests
dotnet test MonteCarlo.sln
```

`MonteCarloSimulation.Core.Tests` covers `MonteCarloEngine`'s run-outcome behavior (all-succeed, all-fail, and two statistically-approximate pass-rate scenarios) — it deliberately does not assert on exact dollar values, since `Random` is unseeded (see "Non-determinism" below). Beyond that, verification has been manual: smoke-running the console app with piped stdin input, and exercising the web app live in a browser.

**Windows gotcha:** if a previous `dotnet run` of `MonteCarloSimulation1` or `MonteCarloSimulation.Web` wasn't cleanly exited, its `.exe` stays locked and the next `dotnet build`/`dotnet run` fails with `MSB3021`/`MSB3027`. Kill the stray process (`Get-Process MonteCarloSimulation1,MonteCarloSimulation.Web -ErrorAction SilentlyContinue | Stop-Process -Force`) before rebuilding.

## Git branch hygiene

Once a remote branch has been merged into `master` and deleted on the remote (e.g. after its PR merges), its local counterpart is stale and should go too. When doing branch-related work (checking status, listing branches, wrapping up a PR), run `git fetch --prune` and delete any local branch whose remote-tracking branch is now gone (`git branch -vv` shows these as `: gone]`) with `git branch -d <branch>`. Confirm with the user before deleting anything that isn't obviously merged.

Source files are UTF-8 with BOM and CRLF line endings — preserve this when editing. `git add` will warn `LF will be replaced by CRLF`; that's expected and harmless.

## Architecture

Three projects in `MonteCarlo.sln`:

- **`MonteCarloSimulation.Core`** — the entire simulation engine, with zero I/O. `MonteCarloEngine.Run(SimulationParameters)` is the single source of truth for all financial math; both front ends call it and only format its output differently. Never duplicate simulation logic into either front end — if a calculation needs to change, it changes here once. Also holds `SimulationParameters`/`SimulationResult`/`SimulationRunOutput` (plain data) and `InvestmentScenarios` (the 4 built-in scenario presets, shared so neither front end re-hardcodes the same numbers).
- **`MonteCarloSimulation1`** — the console app. A thin I/O loop: prompt for input (`SimulationPrompt`), call `MonteCarloEngine.Run`, print results (`SimulationReporter`).
- **`MonteCarloSimulation.Web`** — an ASP.NET Core minimal API (`GET /api/scenarios`, `POST /api/run`) serving a static `wwwroot/` page (plain HTML + vanilla JS, no framework, no build step). The frontend never touches the input form when rendering results — results and inputs are separate DOM subtrees, which is what keeps inputs visible after a run.

### The simulation model (`MonteCarloEngine.Run`)

The user supplies the starting `taxable`, `roth`, and `brokerage` balances directly — there is no hardcoded split. `InitialTaxableBalance` on `SimulationParameters` seeds `taxable` directly; `roth` and `brokerage` are each seeded from a Basis/Unrealized Gain pair (`InitialRothBasis`/`InitialRothUnrealizedGain` and `InitialBrokerageBasis`/`InitialBrokerageUnrealizedGain` respectively — each pair's sum is that bucket's true starting balance, tracked separately from year zero so the tax and age-gating math below is accurate). Every simulated year, all three buckets grow by the same randomly-drawn rate (Box-Muller transform over the scenario's mean/stddev), then a withdrawal is taken pro-rata from whichever buckets are currently *eligible* (see the age-gating paragraph below). Tax is modeled on the taxable and brokerage sides only:
- `taxable` uses the graduated 2026 single-filer federal bracket table (`FederalTaxBrackets.Single2026`): the first `AnnualStandardDeduction` dollars of that year's taxable-side withdrawal are exempt from tax, and only the amount above that is grossed up through `MonteCarloEngine`'s private `GrossUpTaxableWithdrawal` helper, which walks the bracket table and inverts it exactly (each bracket is linear, so no iteration is needed) rather than applying one flat rate.
- `brokerage` uses a flat 20% long-term capital gains rate, applied only to the *embedded gain* portion of that year's withdrawal, via `GrossUpLtcgWithdrawal`. This requires tracking cost basis: a parallel `brokerageBasis` scalar runs alongside the `brokerage` balance, grown only by contributions (never by investment growth). Each year, `gainFraction = (brokerage - brokerageBasis) / brokerage` (clamped to `[0, 1]`) determines what fraction of that year's Brokerage withdrawal is taxable gain; the rest is a tax-free return of principal. `brokerageBasis` shrinks proportionally with the balance on withdrawal (`basis *= balanceAfter / balanceBefore`) to preserve the average-cost ratio for the following year. This is average-cost tracking, not per-lot/FIFO — there's a single running basis, not individual purchase records.
- `roth` withdrawals are never grossed up or taxed, regardless of the deduction.

Bracket thresholds compound by the same inflation rate as the withdrawal and standard deduction (mirroring how the IRS itself inflation-adjusts brackets annually), so `bracketInflationFactor` tracks the same cumulative growth `AnnualStandardDeduction` does, starting from year 0 (no deferred start, unlike Social Security).

The per-year `yearTaxRate` (surfaced as `AverageTaxRates`/`LastTaxRates`) is the blended effective rate across all three buckets — `totalTax / totalGrossWithdrawal`, where `totalTax` now includes both Taxable's bracket tax and Brokerage's LTCG tax (Roth always contributes 0, same as the old `nontaxable` bucket did). Each year's tax also breaks out into `OrdinaryTaxAmount` (Taxable's bracket tax, in dollars) and `CapitalGainsTaxAmount` (Brokerage's LTCG tax, in dollars) on `RunYearDetail`/`SimulationRunOutput`, surfaced separately in the web UI's per-year "Taxes" column. `GetOrdinaryBracketRoom` reports how many more gross Taxable-withdrawal dollars could be taken this year before crossing into the next ordinary bracket up (`AmountUntilNextBracket`) and what that next bracket's rate is (`NextBracketRate`, `null` once already in the top bracket) — it reuses `GrossUpTaxableWithdrawal`'s accounting directly (the amount above the standard deduction maps 1:1 onto the inflation-scaled bracket thresholds) rather than re-deriving it.

**Age-gated withdrawals (the 59½ rule).** `Birthdate` (a `DateOnly`) determines `ageAtStartYears = (today's day-number - Birthdate's day-number) / 365.25`; for simulated year index `run`, `ageInYear = ageAtStartYears + run` and `ageEligible = ageInYear >= 59.5`. While `!ageEligible`: `taxable`'s pro-rata proportion is forced to 0 (no withdrawal from Tax Deferred at all), and `roth`'s *eligible* amount is `rothBasis` rather than the full `roth` balance — mirroring `brokerageBasis`, `rothBasis` is a parallel scalar seeded from `InitialRothBasis`, never grown by returns, and shrunk proportionally on withdrawal (`rothBasis *= balanceAfter / balanceBefore`), but (unlike Brokerage) never taxed regardless of eligibility — the age gate changes *how much* can be withdrawn from Roth, never *how* it's taxed. `brokerage` is never age-restricted. Once `ageEligible` is true, the eligible total equals the full `taxable + brokerage + roth` and every formula below reduces to exactly the unrestricted behavior. If the *eligible* total across all buckets can't cover a year's desired withdrawal — even while locked funds keep the full portfolio positive — the withdrawal is capped at what's eligible and the year fails, using the exact same failure machinery as a negative balance (see below).

**Roth conversions** (opt-in via `SimulationParameters.EnableRothConversions`; C# default `false`, web checkbox default on). After each year's regular withdrawal, Tax Deferred money is converted to Roth to fill the remaining cheap brackets: the conversion brings total ordinary income (`grossTaxableWithdrawal + conversion`) up to the inflation-scaled start of the first bracket at 22% or higher (`Bracket22Floor`, read from the table, not hard-coded); years already at 22%+ convert nothing. The conversion's tax (`ComputeOrdinaryTax` on income with vs. without the conversion — a forward helper, the inverse of `GrossUpTaxableWithdrawal`) is paid by selling Brokerage through `GrossUpLtcgWithdrawal`, so that sale incurs its own LTCG. If Brokerage can't cover the tax, the conversion is scaled down proportionally — safe because tax on extra income is convex with f(0)=0, so f(k·x) ≤ k·f(x). Converted dollars are added to both `roth` and `rothBasis`, so they're accessible anytime under the 59½ gate (the IRS 5-year seasoning rule isn't modeled); conversions themselves ignore the age gate since they aren't withdrawals. The year's reported `OrdinaryTaxAmount`, `CapitalGainsTaxAmount`, Brokerage withdrawal, blended tax rate, and bracket/room-to-next-bracket all include the conversion and its funding sale; `RothConversionAmount`/`RothConversionTax` break it out separately. `GetOrdinaryBracketRoom` assigns income sitting exactly on a threshold to the lower bracket, so a year that fills to the 22% line reads "12% bracket, $0 until 22%".

Two cash-flow features layer on top of that:
- **NewMoney** (a one-time inheritance) is credited directly into `brokerage` in its arrival year, increasing both the balance *and* the basis by the same amount — it's a cash contribution, not a gain, so it stays tax-free on arrival and isn't taxed later beyond its own subsequent growth.
- **Social Security** is credited directly into `taxable` at the same point in the year NewMoney is (after that year's withdrawal, before balances are tracked) — modeling it as taxable income: it isn't itself taxed on arrival, but since `taxable` withdrawals are grossed up, every dollar of it is taxed once it's later spent. It starts at `SocialSecurityYearsUntilStart` and compounds by the same inflation rate as the withdrawal thereafter. Because the deposit happens after that year's withdrawal is computed, it never reduces the withdrawal target for the year it arrives in — it only starts increasing the pro-rata share drawn from `taxable` (and therefore reducing reliance on `brokerage`/`roth`) from the following year onward.

A run "fails" the instant `taxable`, `brokerage`, `roth`, or their combined total goes negative, *or* the eligible (currently-unlocked) total falls short of that year's desired withdrawal — a sub-account, or the age gate itself, can trigger failure even while the combined balance is still positive.

### `SimulationResult`'s per-run lists are index-aligned

`SimulationResult` holds several `List<T>` fields (`EndingBalances`, `AverageAnnualReturns`, `AverageTaxRates`, `FailureYears`, `HighestReturnYears`/`Values`, `LowestReturnYears`/`Values`, `LowestBalanceYears`/`Values`) that all get exactly one entry appended per iteration, in the same order — `EndingBalances[i]` and `FailureYears[i]` describe the same run. When adding a new per-run stat, append to it at the same point in `MonteCarloEngine.Run` (right after the year loop, alongside the existing appends) so it stays aligned; don't index it differently from the others. `YearsOutOfMoney` and `FailedScenarioAverages` are the exception — they only have entries for *failed* runs, in occurrence order, not aligned to run index.

### Intentional quirks — not bugs

- `SuccessMoneyRemaining` accumulates one entry *per year* of every successful run, not one ending balance per run.
- The console's detailed "last run balances" report and the web's last-successful-run detail table only populate when **zero** iterations failed (`OutOfMoneyCount == 0`) — otherwise only the failure trace and per-run summary print.
- `gainFraction` (the fraction of a Brokerage withdrawal treated as taxable gain) is clamped to `[0, 1]`; an embedded loss in Brokerage (basis exceeding balance) never produces a tax rebate on withdrawal in this model.
- A run can fail in a year where its *total* balance (including age-locked Taxable and Roth-gains) is still comfortably positive — the age gate can make most of the portfolio inaccessible even while it exists on paper. This is the intended "early-retirement liquidity trap" behavior, not a display bug.

These were flagged during development and deliberately kept as-is; don't "fix" them without being asked.

### Non-determinism

`Random` is unseeded (`new Random()`), so runs aren't reproducible and there's no golden-value test to write. The verification approach used throughout this repo's history is structural/cross-referential instead: e.g., confirming a run's reported highest/lowest return year matches the max/min line in that same run's own printed trace, or confirming a value that should compound (like NewMoney) actually persists into the following year rather than reverting.
