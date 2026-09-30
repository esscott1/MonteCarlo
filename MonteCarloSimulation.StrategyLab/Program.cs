using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MonteCarloSimulation.Core;
using MonteCarloSimulation.StrategyLab;

// Strategy Lab: does the app's Tax-optimized withdrawal order (with its Roth conversion policy) let a household
// spend the most, or does another order or conversion policy do better? Runs every combination on the same seeded
// market paths for many generated households and writes CSVs plus summary.json.
//
//   dotnet run -c Release --project MonteCarloSimulation.StrategyLab -- [--set main|funding] [--scenarios 1000]
//       [--paths 200] [--seed 2026] [--single-year 1000] [--out MonteCarloSimulation.StrategyLab/output] [--quick] [--publish]
//
// --set main (default) runs every order x conversion policy; --set funding compares where conversion tax comes from.
// Completed scenarios are appended to results.csv as they finish; rerunning with the same settings (and the same
// engine - see EngineFingerprint) resumes and redoes only the fast parts. --publish (main set) writes summary.json to
// the web app, where the Model Info page reads it, including the funding comparison if that run exists.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

int scenarioCount = 1000, paths = 200, seed = 2026, singleYearCount = 1000;
string outDir = Path.Combine("MonteCarloSimulation.StrategyLab", "output");
bool publish = false;
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
        default: Console.Error.WriteLine($"Unknown argument {args[i]}"); return 1;
    }
}
if (publish && set != Candidates.Main)
{
    Console.Error.WriteLine("--publish needs the main set (the funding comparison is folded into it).");
    return 1;
}

string RunDir(CandidateSet s) => Path.Combine(outDir, $"seed{seed}-n{scenarioCount}-p{paths}-{s.Name}-{EngineFingerprint.For(s)}");

var runDir = RunDir(set);
Directory.CreateDirectory(runDir);
var stopwatch = Stopwatch.StartNew();
var combinations = set.All;
Console.WriteLine($"Strategy Lab ({set.Name}): {scenarioCount} scenarios x {combinations.Count} combinations x {paths} paths, seed {seed} -> {runDir}");

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
    var scenarioRows = ScenarioEvaluator.Evaluate(scenario, combinations, paths);
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
return 0;
