using System.Globalization;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests.Drill;

/// <summary>
/// What both providers' zone tests assert. Problems are collected rather than thrown so one run names every
/// persona, action and click trail that leaked or miscounted, instead of stopping at the first.
/// </summary>
internal static class DrillScopeChecks
{
    private static string Key(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static List<string> Ids(IEnumerable<Dictionary<string, object?>> rows) =>
        rows.Select(r => Key(r["Id"])).OrderBy(i => int.Parse(i, CultureInfo.InvariantCulture)).ToList();

    private static List<string> Ids(IEnumerable<EnrolmentRow> rows) =>
        rows.Select(r => r.Id.ToString(CultureInfo.InvariantCulture)).OrderBy(i => int.Parse(i, CultureInfo.InvariantCulture)).ToList();

    private static void CheckRows(
        List<string> problems, string where, Persona persona, IReadOnlyList<EnrolmentRow> seed, List<EnrolmentRow> expected,
        IEnumerable<Dictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        var byId = seed.ToDictionary(r => r.Id.ToString(CultureInfo.InvariantCulture));
        foreach (var row in list)
        {
            var id = Key(row["Id"]);
            if (!byId.TryGetValue(id, out var source) || !persona.Sees(source))
                problems.Add($"{where}: row Id {id} is outside the zone");
            if (row.TryGetValue("ProgramId", out var program) && persona.Programs is { } allowed
                && !allowed.Contains(Convert.ToInt32(program, CultureInfo.InvariantCulture)))
                problems.Add($"{where}: ProgramId {program} is outside the zone");
        }

        var actual = Ids(list);
        var wanted = Ids(expected);
        if (!actual.SequenceEqual(wanted))
            problems.Add($"{where}: expected ids [{string.Join(",", wanted)}] but got [{string.Join(",", actual)}]");
    }

    private static void CheckGroups(
        List<string> problems, string where, Dictionary<string, long> expected, IEnumerable<Dictionary<string, object?>> rows,
        string keyColumn, string countColumn)
    {
        // A saved query may group finer than the chart's axis (site and status), so one axis value can span several rows.
        var actual = rows.GroupBy(r => Key(r[keyColumn]))
            .ToDictionary(g => g.Key, g => g.Sum(r => Convert.ToInt64(r[countColumn], CultureInfo.InvariantCulture)));
        if (actual.Count != expected.Count || expected.Any(e => !actual.TryGetValue(e.Key, out var n) || n != e.Value))
            problems.Add($"{where}: expected {string.Join(",", expected.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"))} " +
                         $"but got {string.Join(",", actual.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"))}");
    }

    private static Dictionary<string, long> GroupCounts(IEnumerable<EnrolmentRow> rows, Func<EnrolmentRow, object> key) =>
        rows.GroupBy(r => Key(key(r))).ToDictionary(g => g.Key, g => (long)g.Count());

    /// <summary>Every action the user has, under every click trail including a forged one, for one persona.</summary>
    public static async Task<int> RunMatrixAsync(DrillHarness h, IReadOnlyList<EnrolmentRow> seed)
    {
        var persona = h.Persona;
        var problems = new List<string>();
        var checks = 0;

        foreach (var c in StepCase.All)
        {
            var expected = seed.Where(persona.Sees).Where(c.Matches).ToList();
            var tampered = c.Name.StartsWith("tampered", StringComparison.Ordinal);
            var label = $"{persona.Name} / {c.Name}";

            // Rows on a chart that has no ProgramId drill column: a forged ProgramId step is refused outright.
            if (tampered)
            {
                await Assert.ThrowsAsync<CatalogValidationException>(() => h.DrillAsync(h.Plain, DrillAction.Rows, c.Steps));
                await Assert.ThrowsAsync<CatalogValidationException>(() => h.DrillAsync(h.Plain, DrillAction.Breakdown, c.Steps, "Site"));
                checks += 2;
            }
            else
            {
                var plain = await h.DrillAsync(h.Plain, DrillAction.Rows, c.Steps);
                CheckRows(problems, $"{label} / Rows", persona, seed, expected, plain.Rows);
                checks++;
            }

            var withDrill = await h.DrillAsync(h.WithDrillColumns, DrillAction.Rows, c.Steps);
            CheckRows(problems, $"{label} / Rows with drill column ProgramId", persona, seed, expected, withDrill.Rows);
            if (!withDrill.Columns.Any(col => col.Name == "ProgramId")) problems.Add($"{label}: ProgramId column missing");
            checks++;

            var bySite = await h.DrillAsync(h.WithDrillColumns, DrillAction.Breakdown, c.Steps, "Site");
            CheckGroups(problems, $"{label} / Breakdown by Site", GroupCounts(expected, r => r.Site), bySite.Rows, "Site", "Enrolments");
            checks++;

            if (tampered)
            {
                // The breakdown column is the very column the forged step filters: refused, never merged.
                await Assert.ThrowsAsync<CatalogValidationException>(() => h.DrillAsync(h.WithDrillColumns, DrillAction.Breakdown, c.Steps, "ProgramId"));
            }
            else
            {
                var byProgram = await h.DrillAsync(h.WithDrillColumns, DrillAction.Breakdown, c.Steps, "ProgramId");
                CheckGroups(problems, $"{label} / Breakdown by ProgramId", GroupCounts(expected, r => r.ProgramId), byProgram.Rows, "ProgramId", "Enrolments");
                foreach (var row in byProgram.Rows)
                    if (persona.Programs is { } allowed && !allowed.Contains(Convert.ToInt32(row["ProgramId"], CultureInfo.InvariantCulture)))
                        problems.Add($"{label} / Breakdown by ProgramId: group {row["ProgramId"]} is outside the zone");
            }
            checks++;

            var filters = c.Steps.Select(s => (s.Column, s.Value)).ToArray();
            var crossFiltered = await h.RenderCrossFilteredAsync(filters);
            CheckGroups(problems, $"{label} / Cross-filtered tile", GroupCounts(expected, r => r.Site), crossFiltered.Rows, "Site", "Enrolments");
            if (filters.Length > 0 && crossFiltered.CrossFilter?.Applied.Count != filters.Length)
                problems.Add($"{label} / Cross-filtered tile: filter was not applied");
            checks++;

            var (header, csv) = await h.ExportCsvAsync(h.WithDrillColumns, c.Steps);
            var programIndex = Array.IndexOf(header, "ProgramId");
            var idIndex = Array.IndexOf(header, "Id");
            var csvRows = csv.Select(r => new Dictionary<string, object?> { ["Id"] = r[idIndex], ["ProgramId"] = r[programIndex] });
            CheckRows(problems, $"{label} / Drill export", persona, seed, expected, csvRows);
            checks++;

            if (persona.Programs is [10] && tampered)
            {
                if (withDrill.Rows.Count != 0 || crossFiltered.Rows.Count != 0 || csv.Count != 0)
                    problems.Add($"{label}: a forged ProgramId=20 must reach zero rows for a program 10 user");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        return checks;
    }

    /// <summary>A forged value (quotes, a boolean-ish tail) is bound as data: it matches nothing and cannot widen the zone.</summary>
    public static async Task ForgedTextValueMatchesNothingAsync(DrillHarness h)
    {
        var steps = new[] { new DrillStep("Status", "Active' OR '1'='1") };

        var result = await h.DrillAsync(h.WithDrillColumns, DrillAction.Rows, steps);

        Assert.Empty(result.Rows);
    }

    /// <summary>NULL is its own group on a chart, and clicking it must return exactly the rows with no value: not the rows holding
    /// an empty string, and not (through SQL's NULL comparison) none at all.</summary>
    public static async Task NullStepIsExactAsync(DrillHarness h, IReadOnlyList<EnrolmentRow> seed)
    {
        var (query, viz) = h.AddGroupedChart("Status", ColumnDataType.Text);
        var chart = await h.RenderAsync(query, viz);
        var nullGroup = Assert.Single(chart.Rows, r => r["Status"] is null);
        var expected = seed.Where(h.Persona.Sees).Where(r => r.Status is null).ToList();

        var drilled = await h.DrillAsync(viz, DrillAction.Rows, [new DrillStep("Status", DrillHarness.ValueFromChartJson(nullGroup, "Status"))], query: query);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, Convert.ToInt64(nullGroup["N"], CultureInfo.InvariantCulture));
        Assert.Equal(Ids(expected), Ids(drilled.Rows));
        Assert.All(drilled.Rows, r => Assert.Null(r["Status"]));
    }

    /// <summary>The cell of every chart row, copied from the JSON the endpoint would send, narrows the rows to exactly the number
    /// the chart said it had.</summary>
    public static async Task ChartValueRoundTripsAsync(DrillHarness h, string column, ColumnDataType type, int minimumGroups)
    {
        var (query, viz) = h.AddGroupedChart(column, type);
        var chart = await h.RenderAsync(query, viz);
        var problems = new List<string>();

        foreach (var row in chart.Rows)
        {
            var value = DrillHarness.ValueFromChartJson(row, column);
            var wanted = Convert.ToInt64(row["N"], CultureInfo.InvariantCulture);

            var drilled = await h.DrillAsync(viz, DrillAction.Rows, [new DrillStep(column, value)], query: query);

            if (drilled.RowCount != wanted)
                problems.Add($"{column} = {value ?? "null"}: the chart row counts {wanted} but the drill returned {drilled.RowCount}");
        }

        Assert.True(chart.Rows.Count >= minimumGroups, $"only {chart.Rows.Count} groups to test");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
