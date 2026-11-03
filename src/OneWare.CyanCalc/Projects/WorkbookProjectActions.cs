using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.Helpers;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;

namespace OneWare.CyanCalc.Projects;

public sealed class WorkbookProjectActions
{
    private readonly WorkbookProjectManager _manager;
    private readonly IProjectExplorerService _projects;
    private readonly IMainDockService _dock;
    private readonly IWindowService _windows;
    private readonly ILogger _logger;
    private int _activationVersion;
    private bool _registered;
    public IAsyncRelayCommand NewProject { get; }
    public IAsyncRelayCommand NewWorkbook { get; }
    public IAsyncRelayCommand ImportWorkbooks { get; }
    public IAsyncRelayCommand OpenAllWorkbooks { get; }

    public WorkbookProjectActions(WorkbookProjectManager manager, IProjectExplorerService projects,
        IMainDockService dock, IWindowService windows, ILogger logger)
    {
        _manager = manager; _projects = projects; _dock = dock; _windows = windows;
        _logger = logger;
        NewProject = new AsyncRelayCommand(() => RunAsync(CreateProjectDialogAsync));
        NewWorkbook = new AsyncRelayCommand(() => RunAsync(() => CreateWorkbookDialogAsync(ActiveProject())), HasProject);
        ImportWorkbooks = new AsyncRelayCommand(() => RunAsync(() => ImportWorkbooksDialogAsync(ActiveProject())), HasProject);
        OpenAllWorkbooks = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            var project = ActiveProject();
            await OpenWorkbooksAsync(project, project.WorkbookPaths, _activationVersion);
        }), HasProject);
    }

    public void Register()
    {
        if (_registered) return;
        _registered = true;
        _windows.RegisterMenuItem("MainWindow_MainMenu/File/New", new MenuItemModel("CyanCalcWorkbookProject")
        {
            Header = "Workbook project…", Command = NewProject
        });
        _windows.RegisterMenuItem("MainWindow_MainMenu/Workbook",
            new MenuItemModel("CyanCalcNewWorkbook") { Header = "New workbook…", Command = NewWorkbook, Priority = -30 },
            new MenuItemModel("CyanCalcImportWorkbooks") { Header = "Import workbooks…", Command = ImportWorkbooks, Priority = -20 },
            new MenuItemModel("CyanCalcOpenWorkbooks") { Header = "Open all workbooks", Command = OpenAllWorkbooks, Priority = -10 });
        _projects.RegisterConstructContextMenu((selected, items) =>
        {
            if (selected is not [IProjectFolder { Root: WorkbookProject } folder]) return;
            var add = items.FirstOrDefault(item => item.PartId == "Add");
            if (add is null) { add = new MenuItemModel("Add") { Header = "Add" }; items.Add(add); }
            add.Items ??= [];
            add.Items.Add(new MenuItemModel("CyanCalcNewWorkbook")
            {
                Header = "Workbook…", Command = new AsyncRelayCommand(() => RunAsync(() => CreateWorkbookDialogAsync(folder)))
            });
        });
        _projects.PropertyChanged += ActiveProjectChanged;
        ActiveProjectChanged(_projects, new(nameof(_projects.ActiveProject)));
    }

    private bool HasProject() => _projects.ActiveProject is WorkbookProject;
    private WorkbookProject ActiveProject() => _projects.ActiveProject as WorkbookProject
        ?? throw new InvalidOperationException("Select a workbook project first.");

    private Window? TryOwner() => _dock.GetWindowOwner(_projects)
        ?? (_dock.CurrentDocument is { } document ? _dock.GetWindowOwner(document) : null);
    private Window Owner() => TryOwner()
        ?? throw new InvalidOperationException("The workbook project needs an open OneWare window.");

    private async Task CreateProjectDialogAsync()
    {
        var owner = Owner();
        // A native Save panel may ask to replace an existing file even when its
        // overwrite prompt is disabled. Creation never replaces a project file.
        var folder = await StorageProviderHelper.SelectFolderAsync(owner, "Workbook project folder", null);
        if (folder is null) return;
        var name = await _windows.ShowInputAsync("New workbook project", "Project file name (.wbproj)",
            MessageBoxIcon.Info, "Workbooks.wbproj", owner);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!name.IsValidFileName() || Path.GetFileName(name) != name || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            throw new InvalidDataException("Enter a project file name without a folder path.");
        if (Path.GetExtension(name).Length == 0) name += ".wbproj";
        var path = Path.Combine(folder, name);
        if (!Path.GetExtension(path).Equals(".wbproj", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A workbook project must use the .wbproj extension.");
        WorkbookProject? root;
        if (File.Exists(path)) root = await ConfirmOpenExistingProjectAsync(path, owner);
        else
        {
            try { root = await _manager.CreateProjectAsync(path); }
            catch (IOException) when (File.Exists(path)) { root = await ConfirmOpenExistingProjectAsync(path, owner); }
        }
        if (root is null) return;
        if (!_projects.Projects.Contains(root)) _projects.AddProject(root);
        root.IsExpanded = true;
        _projects.ActiveProject = root;
    }

    private async Task<WorkbookProject?> ConfirmOpenExistingProjectAsync(string path, Window owner)
    {
        if (!owner.IsVisible || await _windows.ShowYesNoAsync("Workbook project already exists",
                Path.GetFileName(path) + " already exists. Open the existing project instead?",
                MessageBoxIcon.Info, owner) != MessageBoxStatus.Yes) return null;
        return _projects.Projects.OfType<WorkbookProject>().FirstOrDefault(
            project => string.Equals(project.ProjectFilePath, path, StringComparison.Ordinal))
            ?? (WorkbookProject?)await _manager.LoadProjectAsync(path);
    }

    private async Task CreateWorkbookDialogAsync(IProjectFolder folder)
    {
        var project = (WorkbookProject)folder.Root;
        var name = await _windows.ShowInputAsync("New workbook", "Name for the new workbook (.ic)",
            MessageBoxIcon.Info, "Workbook.ic", Owner());
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!name.IsValidFileName() || Path.GetFileName(name) != name || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            throw new InvalidDataException("Enter a file name without a folder path.");
        if (Path.GetExtension(name).Length == 0) name += ".ic";
        // Use the same collision naming as Explorer's New File. The manager's
        // non-overwriting write still protects a file created after this check.
        var destination = project.ResolveWorkbookPath(Path.Combine(folder.RelativePath, name)).CheckNameFile();
        var path = await _manager.CreateWorkbookAsync(project, Path.GetRelativePath(project.RootFolderPath, destination));
        await _dock.OpenFileAsync(path);
        _projects.ActiveProject = project;
    }

    private async Task ImportWorkbooksDialogAsync(IProjectFolder folder)
    {
        var files = await Owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import workbooks", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Workbooks") { Patterns = ["*.ic", "*.xlsx"] }]
        });
        try
        {
            if (files.Count == 0) return;
            var paths = files.Select(file => file.TryGetLocalPath()
                ?? throw new InvalidDataException("Select local workbook files to import.")).ToArray();
            if (paths.Any(path => !WorkbookProject.IsWorkbook(path)))
                throw new InvalidDataException("Select .ic or .xlsx workbooks to import.");
            await _projects.ImportAsync(folder, true, true, paths);
            await ((WorkbookProject)folder.Root).InitializeAsync();
            folder.Root.IsExpanded = true;
        }
        finally { foreach (var file in files) file.Dispose(); }
    }

    private void ActiveProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(_projects.ActiveProject))) return;
        var version = ++_activationVersion;
        NewWorkbook.NotifyCanExecuteChanged(); ImportWorkbooks.NotifyCanExecuteChanged(); OpenAllWorkbooks.NotifyCanExecuteChanged();
        if (_projects.ActiveProject is WorkbookProject project)
            Dispatcher.UIThread.Post(() => _ = RunAsync(() => OpenStartupWorkbooksAsync(project, version), false));
    }

    public Task OpenStartupWorkbooksAsync(WorkbookProject project) => OpenStartupWorkbooksAsync(project, ++_activationVersion);

    private async Task OpenStartupWorkbooksAsync(WorkbookProject project, int version)
    {
        // A restored or already selected workbook keeps its tab and its unsaved editor state.
        if (_dock.CurrentDocument is WorkbookDocumentViewModel current && project.ContainsWorkbook(current.FullPath)) return;
        await OpenWorkbooksAsync(project, project.StartupWorkbookPaths, version);
    }

    private async Task OpenWorkbooksAsync(WorkbookProject project, IReadOnlyList<string> paths, int version)
    {
        var failures = new List<string>();
        var attempted = 0;
        foreach (var path in paths)
        {
            if (version != _activationVersion || !ReferenceEquals(_projects.ActiveProject, project)) break;
            attempted++;
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Workbook not found: " + Path.GetFileName(path), path);
                var opened = await _dock.OpenFileAsync(path);
                if (opened is WorkbookDocumentViewModel { LoadingFailed: true } failed)
                    failures.Add(Path.GetRelativePath(project.RootFolderPath, path) + ": " + failed.LoadError);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error)
            {
                _logger.LogError(error, "Could not open workbook {Path}", path);
                failures.Add(Path.GetRelativePath(project.RootFolderPath, path) + ": " + error.Message);
            }
        }
        if (failures.Count > 0)
            _logger.Error("CyanCalc — " + Path.GetFileName(project.ProjectFilePath) + ": Could not open " +
                failures.Count + " of " + attempted + " attempted workbooks." + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
    }

    private async Task RunAsync(Func<Task> action, bool showInputDialog = true)
    {
        var owner = TryOwner();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (InvalidDataException error)
        {
            // Like the FPGA creator, rejected input gets Output plus an owned dialog.
            _logger.Error(error.Message, showDialog: showInputDialog && owner is { IsVisible: true }, dialogOwner: owner);
        }
        catch (Exception error) { Report(error); }
    }

    private void Report(Exception error) => _logger.Error(error.Message, error);
}
