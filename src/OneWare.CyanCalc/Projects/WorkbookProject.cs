using System.Text.Json.Nodes;
using OneWare.Essentials.Models;
using OneWare.ProjectSystem.Models;

namespace OneWare.CyanCalc.Projects;

public sealed class WorkbookProject : UniversalProjectRoot
{
    public const string TypeId = "CyanCalcWorkbookProject";
    public const string FileExtension = ".wbproj";
    public override string ProjectTypeId => TypeId;

    public WorkbookProject(string path) : base(path)
    {
        Icon = new IconModel(WorkbookFileIcons.ResourceKey);
    }

    public IReadOnlyList<string> StartupWorkbookPaths
    {
        get
        {
            if (Properties.ContainsKey("startupWorkbooks"))
            {
                if (Properties["startupWorkbooks"] is not JsonArray entries)
                    throw new InvalidDataException("'startupWorkbooks' must be an array of relative workbook paths.");
                return entries.Select(entry => entry is JsonValue value && value.TryGetValue<string>(out var path)
                        ? ResolveWorkbookPath(path) : throw new InvalidDataException("Each startup workbook needs a relative path."))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            // Earlier project files named one startup workbook; the project may now contain many.
            return Properties.ContainsKey("workbook") ? [ResolveWorkbookPath(Properties.GetString("workbook"))] : [];
        }
    }

    public IReadOnlyList<string> WorkbookPaths => GetFiles()
        .Where(path => IsWorkbook(path)).Select(ResolveWorkbookPath)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void ApplyDefaults()
    {
        // OneWare uses simple expressions: '*' also matches directory separators.
        if (!Properties.ContainsKey("include")) Properties.AddToStringArray("include", "*.ic", "*.xlsx");
        if (!Properties.ContainsKey("exclude")) Properties.AddToStringArray("exclude");
        _ = StartupWorkbookPaths;
    }

    public string ResolveWorkbookPath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.StartsWith('\\'))
            throw new InvalidDataException("A workbook needs a relative path inside the project.");
        var path = Path.GetFullPath(Path.Combine(RootFolderPath, relative.Replace('\\', Path.DirectorySeparatorChar)));
        if (!ContainsWorkbook(path))
            throw new InvalidDataException("The workbook must be an .ic or .xlsx file inside the project.");
        return path;
    }

    public bool ContainsWorkbook(string path)
    {
        var relative = Path.GetRelativePath(RootFolderPath, Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) &&
               !Path.IsPathRooted(relative) && IsWorkbook(path);
    }

    public static bool IsWorkbook(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ic" or ".xlsx";
}
