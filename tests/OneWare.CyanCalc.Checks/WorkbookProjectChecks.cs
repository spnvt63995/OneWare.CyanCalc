using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CyanCalc;
using Microsoft.Extensions.DependencyInjection;
using OneWare.CyanCalc.Projects;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.Enums;
using OneWare.Essentials.ViewModels;

internal static partial class Program
{
    static async Task CheckWorkbookProjects(string directory)
    {
        Directory.CreateDirectory(directory);
        var icons = Service<IFileIconService>.Create();
        ContainerLocator.SetContainer(new ServiceCollection().AddSingleton(icons).BuildServiceProvider());
        var manager = new WorkbookProjectManager();
        var projectFile = Path.Combine(directory, "Collection.wbproj");
        var root = await manager.CreateProjectAsync(projectFile);
        Check(root.StartupWorkbookPaths.Count == 0 && root.WorkbookPaths.Count == 0,
            "an empty workbook project loads without a mandatory entry file");
        var manifest = File.ReadAllBytes(projectFile);
        await Reject<IOException>(() => manager.CreateProjectAsync(projectFile), "creating a project never overwrites an existing manifest");
        Check(manifest.SequenceEqual(File.ReadAllBytes(projectFile)), "existing project bytes survive a name collision");
        var a = await manager.CreateWorkbookAsync(root, "Budget.ic");
        Directory.CreateDirectory(Path.Combine(directory, "Reports"));
        var b = await manager.CreateWorkbookAsync(root, "Reports/Forecast.ic");
        using (var model = UserModel.FromICalc(a))
        {
            Check(model.GetWorksheetsProperties().Length == 1, "new workbook contains a valid engine worksheet");
            model.SetUserInput(0, 1, 1, "10"); model.NewSheet(); model.SetUserInput(1, 1, 1, "30");
            File.WriteAllBytes(a, model.ToBytes());
        }
        var xlsx = Path.Combine(directory, "Exchange.xlsx");
        using (var model = new UserModel("Exchange")) File.WriteAllBytes(xlsx, model.ToXlsx());
        File.WriteAllText(Path.Combine(directory, "Notes.txt"), "Project notes");
        await root.InitializeAsync();
        Check(root.WorkbookPaths.Order().SequenceEqual(new[] { a, b, xlsx }.Order()),
            "project discovers root and nested IC/XLSX workbooks while excluding unrelated files");
        var bytes = File.ReadAllBytes(a);
        await Reject<IOException>(() => manager.CreateWorkbookAsync(root, "Budget.ic"), "new workbook refuses to overwrite existing content");
        Check(bytes.SequenceEqual(File.ReadAllBytes(a)), "existing workbook bytes survive a name collision");
        foreach (var path in new[] { "../Outside.ic", "..\\Outside.ic", "/Outside.ic", "C:\\Outside.ic", "Notes.txt" })
            await Reject<InvalidDataException>(() => manager.CreateWorkbookAsync(root, path), "project rejects invalid workbook path: " + path);
        Check(!root.ContainsWorkbook(Path.Combine(directory + "-other", "Budget.ic")),
            "a similarly named neighboring folder is not part of the project");

        root.Properties["startupWorkbooks"] = new JsonArray("Missing.ic", "Budget.ic", "Reports/Forecast.ic", "Budget.ic");
        root.Properties.SetString("customSetting", "preserved");
        await manager.SaveProjectAsync(root);
        root = (WorkbookProject)(await manager.LoadProjectAsync(projectFile))!;
        Check(root.StartupWorkbookPaths.Count == 3 && root.Properties.GetString("customSetting") == "preserved",
            "multiple startup paths round-trip, deduplicate and preserve unrelated project properties");
        Check(root.WorkbookPaths.Count == 3, "a missing startup workbook does not prevent loading the project collection");
        var invalid = new WorkbookProject(projectFile);
        invalid.Properties["startupWorkbooks"] = new JsonArray(42);
        await Reject<InvalidDataException>(() => { invalid.ApplyDefaults(); return Task.CompletedTask; },
            "malformed startup entries are rejected instead of silently ignored");
        var narrowFile = Path.Combine(directory, "Narrow.wbproj");
        File.WriteAllText(narrowFile, "{\"include\":[\"Budget.ic\"],\"exclude\":[\"Hidden*\"]}");
        var narrow = (WorkbookProject)(await manager.LoadProjectAsync(narrowFile))!;
        await manager.CreateWorkbookAsync(narrow, "Added.ic");
        var reloaded = (WorkbookProject)(await manager.LoadProjectAsync(narrowFile))!;
        Check(reloaded.IsPathIncluded("Added.ic"), "creating a workbook extends and saves an explicit project include list");
        await Reject<InvalidDataException>(() => manager.CreateWorkbookAsync(narrow, "Hidden.ic"),
            "creating a workbook respects explicit project exclusions");
        Check(!File.Exists(Path.Combine(directory, "Hidden.ic")), "excluded creation leaves no stray workbook");
        var blockedFile = Path.Combine(directory, "Blocked.wbproj");
        Directory.CreateDirectory(blockedFile);
        var blocked = new WorkbookProject(blockedFile);
        blocked.Properties["include"] = new JsonArray("Budget.ic");
        await Reject<IOException>(() => manager.CreateWorkbookAsync(blocked, "RolledBack.ic"),
            "project metadata write failure is reported");
        Check(!File.Exists(Path.Combine(directory, "RolledBack.ic")) && !blocked.IsPathIncluded("RolledBack.ic"),
            "failed metadata commit rolls back the newly created workbook and include change");

        var dock = Service<IMainDockService>.Create();
        var projects = Service<IProjectExplorerService>.Create();
        var windows = Service<IWindowService>.Create();
        var dockState = Service<IMainDockService>.For(dock);
        var projectState = Service<IProjectExplorerService>.For(projects);
        var windowState = Service<IWindowService>.For(windows);
        UseHostFeedback(icons, dock, windows);
        var open = new Dictionary<string, IExtendedDocument>();
        var opened = new List<string>();
        var documentHost = new Border();
        var allRoots = new ObservableCollection<IProjectRoot> { root };
        dockState.Values["OpenFiles"] = open;
        projectState.Values["Projects"] = allRoots;
        projectState.Values["ActiveProject"] = root;
        projectState.Methods["AddProject"] = args => { allRoots.Add((IProjectRoot)args![0]!); return null; };
        projectState.Methods["GetRootFromFile"] = args => allRoots.OfType<WorkbookProject>().FirstOrDefault(p => p.ContainsWorkbook((string)args![0]!));
        dockState.Methods["OpenFileAsync"] = args =>
        {
            var path = (string)args![0]!; opened.Add(path);
            if (!open.TryGetValue(path, out var document))
            {
                var workbook = new WorkbookDocumentViewModel(path, icons, projects, dock, windows, Logger);
                workbook.InitializeContent(); document = workbook;
            }
            documentHost.Child = ((WorkbookDocumentViewModel)document).Workbook;
            dockState.Set("CurrentDocument", document);
            return Task.FromResult<IExtendedDocument?>(document);
        };
        var errors = new List<string>();
        windowState.Methods["ShowNotification"] = args => { errors.Add((string)args![1]!); return null; };
        var dialogs = new List<(string Message, object? Owner)>();
        windowState.Methods["ShowMessageAsync"] = args =>
        { dialogs.Add(((string)args![1]!, args[3])); return Task.CompletedTask; };
        Action<IReadOnlyList<IProjectExplorerNode>, IList<MenuItemModel>>? context = null;
        projectState.Methods["RegisterConstructContextMenu"] = args =>
        { context = (Action<IReadOnlyList<IProjectExplorerNode>, IList<MenuItemModel>>)args![0]!; return null; };
        var picker = Service<IStorageProvider>.Create();
        var factory = Service<IStorageProviderFactory>.Create();
        Service<IStorageProviderFactory>.For(factory).Methods["CreateProvider"] = _ => picker;
        using var storageScope = AvaloniaLocator.EnterScope();
        AvaloniaLocator.CurrentMutable.Bind<IStorageProviderFactory>().ToConstant(factory);
        var window = new Window { Width = 1000, Height = 700, Content = documentHost }; window.Show();
        dockState.Methods["GetWindowOwner"] = _ => window;
        var actions = new WorkbookProjectActions(manager, projects, dock, windows, Logger);
        actions.Register();
        await actions.OpenStartupWorkbooksAsync(root);
        Check(opened.SequenceEqual(new[] { a, b }) && errors.Count == 0 && OutputLines.Count == 1 && _outputShows == 1,
            "project activation opens remaining workbooks and reports a missing entry through host Output without a toast");
        Check(((WorkbookDocumentViewModel)open[a]).Workbook.Session!.GetWorksheetsProperties().Length == 2 &&
              ((WorkbookDocumentViewModel)open[a]).Workbook.Session!.GetCellContent(1, 1, 1) == "30",
            "a workbook retains its internal sheets when opened as a project document");
        var current = (WorkbookDocumentViewModel)open[b];
        Pump(window);
        current.Workbook.Session!.SetUserInput(0, 1, 1, "unsaved");
        Pump(window);
        await actions.OpenStartupWorkbooksAsync(root);
        Check(opened.Count == 2 && ReferenceEquals(dock.CurrentDocument, current) && current.IsDirty,
            "reactivating a project preserves its selected document and unsaved content");
        await actions.OpenAllWorkbooks.ExecuteAsync(null);
        Check(open.Count == root.WorkbookPaths.Count && ReferenceEquals(open[b], current),
            "open-all routes project workbooks through the host and reuses open document sessions");
        var enteredName = "NewBook";
        windowState.Methods["ShowInputAsync"] = _ => Task.FromResult<string?>(enteredName);
        await actions.NewWorkbook.ExecuteAsync(null);
        Check(File.Exists(Path.Combine(directory, "NewBook.ic")) && dock.CurrentDocument?.FullPath == Path.Combine(directory, "NewBook.ic"),
            "New workbook creates a valid IC document in the active project and opens its tab");
        var originalNewBook = File.ReadAllBytes(Path.Combine(directory, "NewBook.ic"));
        var outputBeforeDuplicate = OutputLines.Count;
        await actions.NewWorkbook.ExecuteAsync(null);
        await actions.NewWorkbook.ExecuteAsync(null);
        Check(File.Exists(Path.Combine(directory, "NewBook1.ic")) &&
              dock.CurrentDocument?.FullPath == Path.Combine(directory, "NewBook2.ic") &&
              originalNewBook.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "NewBook.ic"))) &&
              OutputLines.Count == outputBeforeDuplicate && errors.Count == 0,
            "repeated workbook creation uses Explorer's numeric suffixes, preserves existing data and opens the new file without an error");
        enteredName = "../Outside.ic";
        var filesBeforeInvalid = Directory.GetFiles(directory).Length;
        await actions.NewWorkbook.ExecuteAsync(null);
        Check(Directory.GetFiles(directory).Length == filesBeforeInvalid && dialogs.Count == 1 &&
              ReferenceEquals(dialogs[0].Owner, window) && OutputLines.Count == outputBeforeDuplicate + 1 && errors.Count == 0,
            "rejected workbook name uses host Output and an owned error dialog without writing a file or showing a toast");
        var beforeCancel = Directory.GetFiles(directory).Length;
        enteredName = "";
        await actions.NewWorkbook.ExecuteAsync(null);
        Check(Directory.GetFiles(directory).Length == beforeCancel, "cancelled workbook creation changes no files");

        var newFolder = Path.Combine(directory, "NewCollection"); Directory.CreateDirectory(newFolder);
        var newProjectPath = Path.Combine(newFolder, "NewCollection.wbproj");
        var selectedFolder = Service<IStorageFolder>.Create();
        Service<IStorageFolder>.For(selectedFolder).Values["Path"] = new Uri(newFolder);
        Service<IStorageProvider>.For(picker).Methods["OpenFolderPickerAsync"] = _ =>
            Task.FromResult<IReadOnlyList<IStorageFolder>>([selectedFolder]);
        enteredName = "NewCollection.wbproj";
        await actions.NewProject.ExecuteAsync(null);
        Check(projects.ActiveProject is WorkbookProject created && created.ProjectFilePath == newProjectPath && allRoots.Contains(created),
            "New project uses a folder picker and owned name input, then activates the project");
        var originalProject = File.ReadAllBytes(newProjectPath);
        var projectsBeforeDuplicate = allRoots.Count;
        var outputBeforeProject = OutputLines.Count;
        var collisionDecision = MessageBoxStatus.No;
        var collisionOwner = (object?)null;
        windowState.Methods["ShowYesNoAsync"] = args =>
        { collisionOwner = args![3]; return Task.FromResult(collisionDecision); };
        await actions.NewProject.ExecuteAsync(null);
        Check(allRoots.Count == projectsBeforeDuplicate && originalProject.SequenceEqual(File.ReadAllBytes(newProjectPath)) &&
              OutputLines.Count == outputBeforeProject && errors.Count == 0 && ReferenceEquals(collisionOwner, window),
            "declining an owned project collision prompt preserves the project without an error");
        collisionDecision = MessageBoxStatus.Yes;
        var existingRoot = projects.ActiveProject;
        await actions.NewProject.ExecuteAsync(null);
        Check(ReferenceEquals(projects.ActiveProject, existingRoot) && allRoots.Count == projectsBeforeDuplicate &&
              originalProject.SequenceEqual(File.ReadAllBytes(newProjectPath)) && OutputLines.Count == outputBeforeProject,
            "accepting a project collision reuses the existing project without overwriting it");
        var importCalls = 0;
        projectState.Methods["ImportAsync"] = args =>
        {
            Check(ReferenceEquals(args![0], projects.ActiveProject) && (bool)args[1]! && (bool)args[2]! &&
                  ((string[])args[3]!).SequenceEqual(new[] { a, xlsx }),
                "multi-workbook import delegates copy and inclusion handling to OneWare");
            importCalls++; return Task.CompletedTask;
        };
        Service<IStorageProvider>.For(picker).Methods["OpenFilePickerAsync"] = args =>
        {
            Check(((FilePickerOpenOptions)args![0]!).AllowMultiple, "import picker accepts multiple workbook files");
            return Task.FromResult<IReadOnlyList<IStorageFile>>([LocalStorageFile(a), LocalStorageFile(xlsx)]);
        };
        await actions.ImportWorkbooks.ExecuteAsync(null);
        Check(importCalls == 1, "import invokes the host once with the selected workbooks");

        await root.InitializeAsync();
        var folder = (IProjectFolder)root.GetLoadedEntry("Reports")!;
        var contextItems = new List<MenuItemModel>(); context!([folder], contextItems);
        enteredName = "FolderBook.ic";
        var addWorkbook = (IAsyncRelayCommand)contextItems.Single(item => item.PartId == "Add").Items!.Single().Command!;
        await addWorkbook.ExecuteAsync(null);
        Check(File.Exists(Path.Combine(directory, "Reports", "FolderBook.ic")) && ReferenceEquals(projects.ActiveProject, root),
            "Explorer Add Workbook targets the selected project folder and activates that project");
        await addWorkbook.ExecuteAsync(null);
        Check(dock.CurrentDocument?.FullPath == Path.Combine(directory, "Reports", "FolderBook1.ic") &&
              !File.Exists(Path.Combine(directory, "FolderBook1.ic")),
            "Explorer duplicate workbook creation stays inside the selected nested folder");

        var originalPath = current.FullPath;
        var outputBeforeSave = OutputLines.Count;
        current.FullPath = Path.Combine(directory, "MissingFolder", "Save.ic");
        try
        {
            Check(!await current.SaveAsync() && current.IsDirty && current.LoadError is null &&
                  OutputLines.Count == outputBeforeSave + 1 && errors.Count == 0,
                "failed save uses host Output, keeps unsaved edits and does not turn into a load error or toast");
        }
        finally { current.FullPath = originalPath; }
        Service<IStorageProvider>.For(picker).Methods["SaveFilePickerAsync"] = _ =>
            Task.FromResult<IStorageFile?>(LocalStorageFile(Path.Combine(directory, "MissingFolder", "Export.xlsx")));
        var outputBeforeExport = OutputLines.Count;
        await current.ExportXlsx.ExecuteAsync(null);
        Check(OutputLines.Count == outputBeforeExport + 1 && errors.Count == 0 && !current.IsExporting &&
              current.ExportXlsx.CanExecute(null) && current.IsDirty && current.FullPath == originalPath,
            "failed export uses host Output, releases its busy state and preserves the source document");

        dockState.Set("CurrentDocument", null);
        var originalStartup = root.Properties["startupWorkbooks"]?.DeepClone();
        root.Properties["startupWorkbooks"] = new JsonArray("MissingOne.ic", "MissingTwo.ic");
        var outputBeforeBatch = OutputLines.Count;
        await actions.OpenStartupWorkbooksAsync(root);
        Check(OutputLines.Count == outputBeforeBatch + 1 && OutputLines[^1].Contains("2 of 2") &&
              OutputLines[^1].Contains("MissingOne.ic") && OutputLines[^1].Contains("MissingTwo.ic"),
            "multiple missing startup files produce one project summary with each failed path");
        root.Properties["startupWorkbooks"] = originalStartup;
        opened.Clear();
        var pending = new TaskCompletionSource<IExtendedDocument?>();
        dockState.Methods["OpenFileAsync"] = args => { opened.Add((string)args![0]!); return pending.Task; };
        var activation = actions.OpenStartupWorkbooksAsync(root);
        projectState.Set("ActiveProject", null);
        pending.SetResult(null);
        await activation;
        Check(opened.SequenceEqual(new[] { a }) && !actions.NewWorkbook.CanExecute(null) && !actions.ImportWorkbooks.CanExecute(null),
            "switching projects cancels remaining startup opens and updates project command availability");
        foreach (var document in open.Values.OfType<WorkbookDocumentViewModel>().ToArray())
        {
            await document.SaveAsync(); document.OnClose();
        }
        window.Close();
    }

    static async Task Reject<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, message); return; }
        throw new Exception(message + ": expected " + typeof(T).Name);
    }
}
