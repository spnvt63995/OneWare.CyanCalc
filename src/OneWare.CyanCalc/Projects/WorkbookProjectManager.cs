using System.Text.Json;
using global::CyanCalc;
using OneWare.Essentials.Models;

namespace OneWare.CyanCalc.Projects;

public sealed class WorkbookProjectManager : IProjectManager
{
    public string Extension => WorkbookProject.FileExtension;
    public async Task<IProjectRoot?> LoadProjectAsync(string path)
    {
        var root = new WorkbookProject(Path.GetFullPath(path));
        if (!await root.LoadAsync()) throw new InvalidDataException("The workbook project could not be read.");
        root.ApplyDefaults();
        await root.InitializeAsync();
        return root;
    }

    public async Task ReloadProjectAsync(IProjectRoot project)
    {
        if (project is not WorkbookProject root) return;
        if (!await root.LoadAsync()) throw new InvalidDataException("The workbook project could not be read.");
        root.ApplyDefaults();
        await root.InitializeAsync();
    }

    public async Task<bool> SaveProjectAsync(IProjectRoot root)
    {
        if (root is not WorkbookProject workbook) return false;
        await WriteProjectAsync(workbook, true);
        return true;
    }

    public async Task<WorkbookProject> CreateProjectAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A workbook project must use the .wbproj extension.");
        var root = new WorkbookProject(path);
        root.ApplyDefaults();
        await WriteProjectAsync(root, false);
        await root.InitializeAsync();
        return root;
    }

    public async Task<string> CreateWorkbookAsync(WorkbookProject project, string relativePath)
    {
        var path = project.ResolveWorkbookPath(relativePath);
        if (!Path.GetExtension(path).Equals(".ic", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("New workbooks use the .ic format. Use Export to create an XLSX copy.");
        var included = project.IsPathIncluded(relativePath);
        var previousInclude = project.Properties["include"]?.DeepClone();
        if (!included)
        {
            project.IncludePath(relativePath);
            if (!project.IsPathIncluded(relativePath))
            {
                project.Properties["include"] = previousInclude;
                throw new InvalidDataException("This workbook location is excluded from the project.");
            }
        }
        var created = false;
        try
        {
            using var model = new UserModel(Path.GetFileNameWithoutExtension(path));
            await WriteFileAsync(path, model.ToBytes(), false);
            created = true;
            if (!included) await WriteProjectAsync(project, true);
        }
        catch
        {
            if (!included) project.Properties["include"] = previousInclude;
            if (created) File.Delete(path);
            throw;
        }
        await project.InitializeAsync();
        return path;
    }

    private static async Task WriteProjectAsync(WorkbookProject project, bool overwrite)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project.Properties.AsObject(), new JsonSerializerOptions { WriteIndented = true });
        await WriteFileAsync(project.ProjectFilePath, bytes, overwrite);
        project.LastSaveTime = File.GetLastWriteTime(project.ProjectFilePath);
    }

    private static async Task WriteFileAsync(string path, byte[] bytes, bool overwrite)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
