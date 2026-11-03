using CyanCalc;
using System.Globalization;

if (args.Length != 1) throw new ArgumentException("Pass an output directory for the fictional sample workbooks.");
var directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
foreach (var relative in new[] { "Budget.ic", "Roadmap.ic", "Reports/Forecast.ic" })
    if (File.Exists(Path.Combine(directory, relative))) throw new IOException($"Refusing to overwrite {relative}.");

using (var model = new UserModel())
{
    model.SetName("Quarterly project budget");
    model.RenameSheet(0, "Overview");
    void Put(int row, int column, string text) => model.SetUserInput(0, row, column, text);
    void Style(int row, int column, int width, int height, string key, string value) =>
        model.UpdateRangeStyle(new CellArea(0, row, column, width, height), key, value);
    model.SetColumnsWidth(0, 1, 1, 190);
    model.SetColumnsWidth(0, 2, 4, 105);
    model.SetColumnsWidth(0, 5, 5, 100);
    model.SetRowsHeight(0, 1, 18, 27);
    model.SetRowsHeight(0, 1, 1, 44);
    Put(1, 1, "Quarterly project plan");
    model.MergeCells(0, 1, 1, 1, 5);
    Style(1, 1, 5, 1, "font.size", "20");
    Style(1, 1, 5, 1, "font.b", "true");
    Style(1, 1, 5, 1, "font.color", "#009688");
    Put(2, 1, "FICTIONAL SAMPLE  /  USD  /  Q1");
    model.MergeCells(0, 2, 1, 2, 5);
    Style(2, 1, 5, 1, "font.size", "10");
    var headings = new[] { "Workstream", "Budget", "Actual", "Remaining", "Status" };
    for (var c = 0; c < headings.Length; c++) Put(4, c + 1, headings[c]);
    Style(4, 1, 5, 1, "fill.color", "#007D73");
    Style(4, 1, 5, 1, "font.color", "#FFFFFF");
    Style(4, 1, 5, 1, "font.b", "true");
    var items = new (string Name, int Budget, int Actual)[]
    {
        ("Product design", 12000, 9800), ("Prototype", 18000, 14250),
        ("Components", 9500, 7100), ("Validation", 8000, 5200),
        ("Documentation", 3500, 1800), ("Contingency", 5000, 0)
    };
    for (var i = 0; i < items.Length; i++)
    {
        var row = i + 5; var item = items[i];
        Put(row, 1, item.Name); Put(row, 2, item.Budget.ToString(CultureInfo.InvariantCulture));
        Put(row, 3, item.Actual.ToString(CultureInfo.InvariantCulture));
        Put(row, 4, $"=B{row}-C{row}");
        Put(row, 5, $"=IF(D{row}>=0,\"On track\",\"Review\")");
    }
    Put(12, 1, "TOTAL");
    foreach (var (column, letter) in new[] { (2, "B"), (3, "C"), (4, "D") })
        Put(12, column, $"=SUM({letter}5:{letter}10)");
    Style(12, 1, 5, 1, "font.b", "true");
    Style(12, 1, 5, 1, "fill.color", "#D8F0EC");
    Style(12, 1, 5, 1, "font.color", "#143D37");
    Style(5, 2, 3, 8, "num_fmt", "#,##0");
    Put(15, 1, "Budget used"); Put(15, 2, "=C12/B12");
    Style(15, 2, 1, 1, "num_fmt", "0.0%");
    Put(17, 1, "Edit a budget or actual value to recalculate the plan.");
    model.MergeCells(0, 17, 1, 17, 5);
    Style(17, 1, 5, 1, "font.size", "10");
    model.NewSheet(); model.RenameSheet(1, "Assumptions");
    model.SetUserInput(1, 1, 1, "This sample contains fictional data only.");
    model.SetUserInput(1, 3, 1, "Currency"); model.SetUserInput(1, 3, 2, "USD");
    model.SetUserInput(1, 4, 1, "Period"); model.SetUserInput(1, 4, 2, "Q1");
    model.SetColumnsWidth(1, 1, 1, 320);
    model.SetSelectedSheet(0); model.SetSelectedCell(12, 4); model.Evaluate();
    if (Convert.ToDouble(model.GetCellValue(0, 12, 2)) != 56000 || Convert.ToDouble(model.GetCellValue(0, 12, 4)) != 17850)
        throw new InvalidOperationException("The sample budget totals did not calculate as expected.");
    model.SaveToICalc(Path.Combine(directory, "Budget.ic"));
}
foreach (var (relative, title) in new[] { ("Roadmap.ic", "Project milestones"), ("Reports/Forecast.ic", "Quarterly forecast") })
{
    using var model = new UserModel();
    model.SetName(title); model.SetUserInput(0, 1, 1, title);
    model.SetUserInput(0, 3, 1, "Fictional sample workbook");
    model.SetColumnsWidth(0, 1, 1, 300);
    model.UpdateRangeStyle(new CellArea(0, 1, 1, 1, 1), "font.b", "true");
    model.UpdateRangeStyle(new CellArea(0, 1, 1, 1, 1), "font.size", "18");
    var path = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    model.SaveToICalc(path);
}
Console.WriteLine("Created Budget.ic, Roadmap.ic and Reports/Forecast.ic with verified fictional budget totals.");
