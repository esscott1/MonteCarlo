using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MonteCarloSimulation.Core;
using MonteCarloSimulation.StrategyLab;

// Strategy Lab: does the app's Tax-optimized withdrawal order (with its Roth conversion policy) let a household
// spend the most, or does another order or conversion policy do better? Runs every combination on the same seeded
// market paths for many generated households and writes CSVs plus summary.json.
//
//   dotnet run -c Release --project MonteCarloSimulation.StrategyLab -- [--set main|funding|schedules] [--scenarios 1000]
//       [--paths 200] [--seed 2026] [--single-year 1000] [--out MonteCarloSimulation.StrategyLab/output] [--quick] [--publish]
//       [--validate]
//
// --set main (default) runs every order x conversion policy; --set funding compares where conversion tax comes from;
// --set schedules runs Roth conversion lines that change from year to year (ConversionSchedules). --validate reruns
// the same households on the next --paths markets (path i is new Random(paths + i)), in its own folder: once both the
// schedules run and its --validate run are complete, either run writes the schedule study (ScheduleStudy) to
// schedules.json in the validation folder.
// Completed scenarios are appended to results.csv as they finish; rerunning with the same settings (and the same
// engine - see EngineFingerprint) resumes and redoes only the fast parts. --publish (main set) writes summary.json to
// the web app, where the Model Info page reads it, including the funding comparison and the schedule study when those
// runs exist.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

int scenarioCount = 1000, paths = 200, seed = 2026, singleYearCount = 1000;
string outDir = Path.Combine("MonteCarloSimulation.StrategyLab", "output");
bool publish = false, validate = false;
var set = Candidates.Main;
for (int i = 0; i < args.Length; i++)
{
    string Next() => args[++i];
    switch (args[i])
    {
        case "--set": set = Candidates.SetNamed(Next()); break;
        case "--scenarios": scenarioCount = int.Parse(Next()); break;
        case "--paths": paths = int.Parse(Next()); break;
        case "--seed": seed = int.Parse(Next()); break;
        case "--single-year": singleYearCount = int.Parse(Next()); break;
        case "--out": outDir = Next(); break;
        case "--quick": scenarioCount = 50; break;
        case "--publish": publish = true; break;
        case "--validate": validate = true; break;
        default: Console.Error.WriteLine($"Unknown argument {args[i]}"); return 1;
    }
}
if (publish && (set != Candidates.Main || validate))
{
    Console.Error.WriteLine("--publish needs the main set on its own markets (the funding comparison is folded into it).");
    return 1;
}

// The validation markets follow the fit run's: paths..2*paths-1
int pathOffset = validate ? paths : 0;
string RunDir(CandidateSet s, int offset = 0) =>
    Path.Combine(outDir, $"seed{seed}-n{scenarioCount}-p{paths}-{s.Name}-{EngineFingerprint.For(s)}" + (offset > 0 ? $"-o{offset}" : ""));

var runDir = RunDir(set, pathOffset);
Directory.CreateDirectory(runDir);
var stopwatch = Stopwatch.StartNew();
var combinations = set.All;
Console.WriteLine($"Strategy Lab ({set.Name}): {scenarioCount} scenarios x {combinations.Count} combinations x {paths} paths " +
    $"(from path {pathOffset}), seed {seed} -> {runDir}");

var scenarios = ScenarioGenerator.Generate(scenarioCount, seed);
LabCsv.WriteScenarios(Path.Combine(runDir, "scenarios.csv"), scenarios);

// Resume: skip scenarios whose rows are already complete
var resultsPath = Path.Combine(runDir, "results.csv");
var existing = LabCsv.ReadResults(resultsPath);
var done = existing.GroupBy(r => r.ScenarioId).Where(g => g.Count() == combinations.Count).Select(g => g.Key).ToHashSet();
var rows = existing.Where(r => done.Contains(r.ScenarioId)).ToList();
LabCsv.WriteResults(resultsPath, rows); // drop any partial scenario
if (done.Count > 0) Console.WriteLine($"Resuming: {done.Count} scenarios already done.");

var todo = scenarios.Where(s => !done.Contains(s.Id)).ToList();
var gate = new object();
int finished = 0;
Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, scenario =>
{
    var scenarioRows = ScenarioEvaluator.Evaluate(scenario, combinations, paths, pathOffset);
    lock (gate)
    {
        LabCsv.AppendResults(resultsPath, scenarioRows);
        rows.AddRange(scenarioRows);
        finished++;
        if (finished % 10 == 0 || finished == todo.Count)
        {
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            double eta = elapsed / finished * (todo.Count - finished);
            Console.WriteLine($"  {finished}/{todo.Count} scenarios  {elapsed / 60:0.0} min elapsed, ~{eta / 60:0.0} min left");
        }
    }
});

// The main page's defaults under each investment scenario
Console.WriteLine("Main page defaults under each investment scenario...");
var pageDefault = InvestmentScenarios.All
    .AsParallel().AsOrdered()
    .Select(inv => { var s = ScenarioGenerator.PageDefault(inv); return (Scenario: s, Rows: ScenarioEvaluator.Evaluate(s, combinations, paths)); })
    .ToList();
LabCsv.WriteResults(Path.Combine(runDir, "page-default.csv"), pageDefault.SelectMany(p => p.Rows).ToList());

Console.WriteLine($"Single-year check: {singleYearCount} situations...");
var singleYear = SingleYearCheck.Run(singleYearCount, seed);
LabCsv.WriteSingleYear(Path.Combine(runDir, "single-year.csv"), singleYear);

var caseStudy = CaseStudy.Build(set, scenarios, rows, paths);

var config = new RunConfig(scenarioCount, paths, seed, singleYearCount, DateTime.UtcNow, stopwatch.Elapsed.TotalSeconds);
var summary = Analysis.Summarize(set, scenarios, rows, singleYear, pageDefault, caseStudy, config);

// The funding comparison, from its own run with the same settings, rides along in the main summary, and so does
// the Optimal page's defaults before and after the app chose its own accounts
if (set == Candidates.Main)
{
    Console.WriteLine("Optimal page defaults, before and after...");
    summary = summary with { OptimalDefaults = OptimalDefaults.Compare() };

    var fundingRows = LabCsv.ReadResults(Path.Combine(RunDir(Candidates.Funding), "results.csv"));
    if (fundingRows.Count == scenarioCount * Candidates.Funding.All.Count)
    {
        var funding = Analysis.Summarize(Candidates.Funding, scenarios, fundingRows, singleYear, [], null, config);
        summary = summary with
        {
            Funding = new FundingComparison(funding.Baseline, funding.ScenariosCompared, funding.VsBaseline, funding.Subsets, funding.Slices)
        };
    }
    else
    {
        Console.WriteLine("No complete funding run with these settings; the summary has no funding comparison (run --set funding first).");
    }

    // The schedule study, from its two runs with the same settings: why the app keeps one conversion line per household
    var scheduleFit = LabCsv.ReadResults(Path.Combine(RunDir(Candidates.Schedules), "results.csv"));
    var scheduleValidation = LabCsv.ReadResults(Path.Combine(RunDir(Candidates.Schedules, paths), "results.csv"));
    if (scheduleFit.Count == scenarioCount * Candidates.Schedules.All.Count && scheduleValidation.Count == scheduleFit.Count)
    {
        Console.WriteLine("Schedule study...");
        summary = summary with
        {
            ScheduleStudy = ScheduleStudy.Run(Candidates.Schedules, scenarios, scheduleFit, scheduleValidation, seed, tradeoffPaths: paths)
        };
    }
    else
    {
        Console.WriteLine("No complete schedule study with these settings; the summary has none (run --set schedules, then --set schedules --validate).");
    }
}

var json = JsonSerializer.Serialize(summary, SummaryJson.Options);
File.WriteAllText(Path.Combine(runDir, "summary.json"), json);
if (publish)
{
    // The Model Info page's data, committed with the web app
    Directory.CreateDirectory(Path.GetDirectoryName(SummaryJson.PublishedPath)!);
    File.WriteAllText(SummaryJson.PublishedPath, json);
    Console.WriteLine($"Published to {SummaryJson.PublishedPath}");
}

Console.WriteLine();
Console.WriteLine($"Done in {stopwatch.Elapsed.TotalMinutes:0.0} min. {summary.ScenariosCompared} scenarios compared ({summary.ScenariosExcluded} excluded).");
Console.WriteLine($"{"Combination",-12} {"mean",8} {"median",8} {"p5",8} {"p95",8} {"win",6} {"loss",6}  mean 95% CI");
foreach (var c in summary.VsBaseline.OrderByDescending(c => c.MeanSpendDiff))
    Console.WriteLine($"{c.Code,-12} {c.MeanSpendDiff,8:P2} {c.MedianSpendDiff,8:P2} {c.P5SpendDiff,8:P2} {c.P95SpendDiff,8:P2} {c.WinRate,6:P0} {c.LossRate,6:P0}  {c.MeanCi[0]:P2}..{c.MeanCi[1]:P2}");
Console.WriteLine($"Regret: mean {summary.Regret.Mean:P2}, median {summary.Regret.Median:P2}, >1% in {summary.Regret.ShareOver1Pct:P0}, >3% in {summary.Regret.ShareOver3Pct:P0}");
var sy = summary.SingleYear;
Console.WriteLine($"Single year: {sy.Checked} checked, {sy.WithinFiveDollars} within $5 of the minimum, {sy.MaterialGaps} gaps (median ${sy.MedianMaterialGap:N0}), Roth-before-others {sy.RothBeforeOthers}");

// The schedule study, once both the fit run and the validation run are complete
if (set == Candidates.Schedules)
{
    var fitRows = validate ? LabCsv.ReadResults(Path.Combine(RunDir(set), "results.csv")) : rows;
    var validationRows = validate ? rows : LabCsv.ReadResults(Path.Combine(RunDir(set, paths), "results.csv"));
    int expected = scenarioCount * combinations.Count;
    if (fitRows.Count != expected || validationRows.Count != expected)
    {
        Console.WriteLine($"Schedule study: waiting for the {(validate ? "fit run (--set schedules)" : "validation run (--set schedules --validate)")}.");
        return 0;
    }

    var study = ScheduleStudy.Run(set, scenarios, fitRows, validationRows, seed, tradeoffPaths: paths);
    var studyPath = Path.Combine(RunDir(set, paths), "schedules.json");
    File.WriteAllText(studyPath, JsonSerializer.Serialize(study, SummaryJson.Options));
    Console.WriteLine();
    Console.WriteLine($"Schedule study -> {studyPath}");
    Console.WriteLine($"Rules chosen on {study.FitHouseholds} households (fit markets), judged on {study.HoldoutHouseholds} others: " +
                      $"picked on the fit markets, measured on the validation markets. A schedule is the in-sample best for " +
                      $"{study.ShareWhereAScheduleIsBest:P0} of fit households.");
    Console.WriteLine($"{"Rule",-36} {"in-sample",10} {"held out",9} {"95% CI",17} {"win",5} {"loss",5}  worst household type");
    foreach (var r in study.Rules)
        Console.WriteLine($"{r.Name,-36} {r.InSampleMeanGain,10:P2} {r.MeanGain,9:P2} {$"{r.MeanGainCi[0]:P2}..{r.MeanGainCi[1]:P2}",17} " +
                          $"{r.WinShare,5:P0} {r.LossShare,5:P0}  {r.WorstSlice} {r.WorstSliceMeanGain:P2}{(r.Adopted ? "  ADOPT" : "")}");
    foreach (var r in study.Rules) Console.WriteLine($"  {r.Name}: {r.Description}");
    Console.WriteLine("Schedules that add the most beside today's pick (in sample):");
    foreach (var u in study.TopSchedules.Take(10))
        Console.WriteLine($"  {u.Code,-14} +{u.MeanAddedGain:P2} on average, best for {u.ShareBest:P1}  {u.Name}");
    Console.WriteLine($"Converting heavily early, at {set.Baseline.Code}'s 82.5% spend (after-tax money left: median % difference, share of households higher):");
    Console.WriteLine($"  {"",-14} {"82.5% spend",11} {"strong markets (p90)",22} {"weaker markets (p25)",22}");
    foreach (var o in study.TradeOffs)
        Console.WriteLine($"  {o.Code,-14} {o.MeanSpendDiff,11:P2} " +
                          $"{$"{o.StrongMarketWealthMedianDiff:P1} ({o.StrongMarketWealthHigherShare:P0})",22} " +
                          $"{$"{o.WeakMarketWealthMedianDiff:P1} ({o.WeakMarketWealthHigherShare:P0})",22}");
    Console.WriteLine(study.Rules.Any(r => r.Adopted)
        ? "Verdict: at least one rule meets the adoption bar (see ADOPT)."
        : $"Verdict: no rule meets the adoption bar (held-out mean >= {study.AdoptMeanGain:P1} with its 95% interval above 0, " +
          $"no household type below {study.AdoptWorstSliceMean:P0}).");
}
return 0;
