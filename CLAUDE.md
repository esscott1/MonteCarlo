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
```

There is no test project and no automated test suite — verification has been manual: smoke-running the console app with piped stdin input, and exercising the web app live in a browser.

**Windows gotcha:** if a previous `dotnet run` of `MonteCarloSimulation1` or `MonteCarloSimulation.Web` wasn't cleanly exited, its `.exe` stays locked and the next `dotnet build`/`dotnet run` fails with `MSB3021`/`MSB3027`. Kill the stray process (`Get-Process MonteCarloSimulation1,MonteCarloSimulation.Web -ErrorAction SilentlyContinue | Stop-Process -Force`) before rebuilding.

Source files are UTF-8 with BOM and CRLF line endings — preserve this when editing. `git add` will warn `LF will be replaced by CRLF`; that's expected and harmless.

## Architecture

Three projects in `MonteCarlo.sln`:

- **`MonteCarloSimulation.Core`** — the entire simulation engine, with zero I/O. `MonteCarloEngine.Run(SimulationParameters)` is the single source of truth for all financial math; both front ends call it and only format its output differently. Never duplicate simulation logic into either front end — if a calculation needs to change, it changes here once. Also holds `SimulationParameters`/`SimulationResult`/`SimulationRunOutput` (plain data) and `InvestmentScenarios` (the 4 built-in scenario presets, shared so neither front end re-hardcodes the same numbers).
- **`MonteCarloSimulation1`** — the console app. A thin I/O loop: prompt for input (`SimulationPrompt`), call `MonteCarloEngine.Run`, print results (`SimulationReporter`).
- **`MonteCarloSimulation.Web`** — an ASP.NET Core minimal API (`GET /api/scenarios`, `POST /api/run`) serving a static `wwwroot/` page (plain HTML + vanilla JS, no framework, no build step). The frontend never touches the input form when rendering results — results and inputs are separate DOM subtrees, which is what keeps inputs visible after a run.

### The simulation model (`MonteCarloEngine.Run`)

The user supplies the starting `taxable` and `nontaxable` balances directly (`InitialTaxableBalance`/`InitialNontaxableBalance` on `SimulationParameters`) — there is no hardcoded split. Every simulated year, both buckets grow by the same randomly-drawn rate (Box-Muller transform over the scenario's mean/stddev), then a withdrawal is taken pro-rata from both. Tax is modeled only on the taxable side, using the graduated 2026 single-filer federal bracket table (`FederalTaxBrackets.Single2026`): the first `AnnualStandardDeduction` dollars of that year's taxable-side withdrawal are exempt from tax, and only the amount above that is grossed up through `MonteCarloEngine`'s private `GrossUpTaxableWithdrawal` helper, which walks the bracket table and inverts it exactly (each bracket is linear, so no iteration is needed) rather than applying one flat rate; withdrawing from `nontaxable` is never grossed up regardless of the deduction. Bracket thresholds compound by the same inflation rate as the withdrawal and standard deduction (mirroring how the IRS itself inflation-adjusts brackets annually), so `bracketInflationFactor` tracks the same cumulative growth `AnnualStandardDeduction` does, starting from year 0 (no deferred start, unlike Social Security).

Two cash-flow features layer on top of that:
- **NewMoney** (a one-time inheritance) is credited directly into `nontaxable` in its arrival year — tax-free on arrival, and since `nontaxable` withdrawals are never grossed up, it stays tax-free on every withdrawal after that too.
- **Social Security** is credited directly into `taxable` at the same point in the year NewMoney is (after that year's withdrawal, before balances are tracked) — modeling it as taxable income: it isn't itself taxed on arrival, but since `taxable` withdrawals are grossed up, every dollar of it is taxed once it's later spent. It starts at `SocialSecurityYearsUntilStart` and compounds by the same inflation rate as the withdrawal thereafter. Because the deposit happens after that year's withdrawal is computed, it never reduces the withdrawal target for the year it arrives in — it only starts increasing the pro-rata share drawn from `taxable` (and therefore reducing reliance on `nontaxable`) from the following year onward.

A run "fails" the instant `taxable`, `nontaxable`, or their combined total goes negative — a sub-account can trigger failure even while the combined balance is still positive.

### `SimulationResult`'s per-run lists are index-aligned

`SimulationResult` holds several `List<T>` fields (`EndingBalances`, `AverageAnnualReturns`, `AverageTaxRates`, `FailureYears`, `HighestReturnYears`/`Values`, `LowestReturnYears`/`Values`, `LowestBalanceYears`/`Values`) that all get exactly one entry appended per iteration, in the same order — `EndingBalances[i]` and `FailureYears[i]` describe the same run. When adding a new per-run stat, append to it at the same point in `MonteCarloEngine.Run` (right after the year loop, alongside the existing appends) so it stays aligned; don't index it differently from the others. `YearsOutOfMoney` and `FailedScenarioAverages` are the exception — they only have entries for *failed* runs, in occurrence order, not aligned to run index.

### Intentional quirks — not bugs

- `SuccessMoneyRemaining` accumulates one entry *per year* of every successful run, not one ending balance per run.
- The console's detailed "last run balances" report and the web's last-successful-run detail table only populate when **zero** iterations failed (`OutOfMoneyCount == 0`) — otherwise only the failure trace and per-run summary print.

These were flagged during development and deliberately kept as-is; don't "fix" them without being asked.

### Non-determinism

`Random` is unseeded (`new Random()`), so runs aren't reproducible and there's no golden-value test to write. The verification approach used throughout this repo's history is structural/cross-referential instead: e.g., confirming a run's reported highest/lowest return year matches the max/min line in that same run's own printed trace, or confirming a value that should compound (like NewMoney) actually persists into the following year rather than reverting.
