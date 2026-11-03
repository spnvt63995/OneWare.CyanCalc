using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CyanCalc;
using OneWare.CyanCalc;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Commands;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

internal static partial class Program
{
    static async Task CheckShellMenus(string directory, Window window, Border body,
        WorkbookDocumentViewModel a, WorkbookDocumentViewModel b, IMainDockService dock, IWindowService windows)
    {
        var menus = new Dictionary<string, MenuItemModel[]>();
        Service<IWindowService>.For(windows).Methods["RegisterMenuItem"] = args =>
        {
            menus.Add((string)args![0]!, (MenuItemModel[])args[1]!);
            return null;
        };
        var shell = new WorkbookShellMenus(dock, windows);
        shell.Register(); shell.Register();
        Check(menus.Count == 3 && menus.Values.Sum(items => items.Length) == 8,
            "shell registers the Workbook menu, five panels and two exports once");
        var panels = menus["MainWindow_MainMenu/Workbook"];
        var xlsx = (IAsyncRelayCommand)menus["MainWindow_MainMenu/File/Export"][0].Command!;
        var png = (IAsyncRelayCommand)menus["MainWindow_MainMenu/File/Export"][1].Command!;
        var palette = new MenuItemApplicationCommand(panels[0], "Workbook > ");
        var changed = 0;
        panels[0].Command!.CanExecuteChanged += (_, _) => changed++;
        Service<IMainDockService>.For(dock).Set("CurrentDocument", Service<IExtendedDocument>.Create());
        Check(changed > 0 && !palette.CanExecute(window) && !xlsx.CanExecute(null) && !png.CanExecute(null),
            "non-workbook documents disable menu and palette commands with a state notification");
        Service<IMainDockService>.For(dock).Set("CurrentDocument", b);
        body.Child = b.Workbook; Pump(window);
        Check(palette.CanExecute(window) && palette.Execute(window), "host palette adapter opens a workbook panel");
        var drawer = b.Workbook.GetVisualDescendants().OfType<Border>().Single(x => x.Classes.Contains("workbook-drawer"));
        Control? lastPanel = null;
        foreach (var item in panels)
        {
            item.Command!.Execute(null); Pump(window);
            Check(drawer.IsVisible && drawer.Child is not null && !ReferenceEquals(lastPanel, drawer.Child),
                $"shell opens the existing {item.Header} panel");
            lastPanel = drawer.Child;
        }
        Check(!a.Workbook.GetVisualDescendants().OfType<Border>().Single(x => x.Classes.Contains("workbook-drawer")).IsVisible,
            "panel commands leave the inactive workbook untouched");
        b.IsReadOnly = true;
        Check(panels.All(item => item.Command!.CanExecute(null)), "read-only documents can inspect existing panels");
        b.IsReadOnly = false;

        var picker = Service<IStorageProvider>.Create();
        var pickerState = Service<IStorageProvider>.For(picker);
        var factory = Service<IStorageProviderFactory>.Create();
        Service<IStorageProviderFactory>.For(factory).Methods["CreateProvider"] = _ => picker;
        using var storageScope = AvaloniaLocator.EnterScope();
        AvaloniaLocator.CurrentMutable.Bind<IStorageProviderFactory>().ToConstant(factory);
        var requests = new List<FilePickerSaveOptions>();
        Func<Task<IStorageFile?>> choose = () => Task.FromResult<IStorageFile?>(null);
        pickerState.Methods["SaveFilePickerAsync"] = args =>
        {
            requests.Add((FilePickerSaveOptions)args![0]!);
            return choose();
        };
        var errors = new List<string>();
        var outputBefore = OutputLines.Count;
        Service<IWindowService>.For(windows).Methods["ShowNotification"] = args => { errors.Add((string)args![1]!); return null; };
        b.Workbook.FormulaInput.Focus();
        b.Workbook.FormulaInput.Text = "25";
        Check(b.Workbook.State.Editing is not null, "formula draft is active before a shell panel command");
        panels[0].Command!.Execute(null); Pump(window);
        Check(b.Workbook.State.Editing is null && b.Workbook.Session!.GetCellContent(0, 1, 1) == "25",
            "opening a panel commits the existing cell draft through workbook behavior");
        var originalPath = b.FullPath;
        var originalFile = File.ReadAllBytes(originalPath);
        var dirty = b.IsDirty;
        var copy = Path.Combine(directory, "Export.xlsx");
        choose = () => Task.FromResult<IStorageFile?>(LocalStorageFile(copy));
        await xlsx.ExecuteAsync(null);
        Check(File.Exists(copy) && requests[^1].DefaultExtension == "xlsx" && requests[^1].ShowOverwritePrompt == true,
            "XLSX export uses the owner save picker and writes a copy");
        using (var exported = UserModel.FromXlsx(copy))
            Check(exported.GetCellContent(0, 1, 1) == "25", "exported XLSX contains the active workbook's committed draft");
        Check(b.FullPath == originalPath && b.IsDirty == dirty && originalFile.SequenceEqual(File.ReadAllBytes(originalPath)),
            "export preserves document identity, dirty state and source file");
        choose = () => Task.FromResult<IStorageFile?>(null);
        var before = Directory.GetFiles(directory).Length;
        await xlsx.ExecuteAsync(null);
        Check(Directory.GetFiles(directory).Length == before && b.IsDirty == dirty && !b.IsExporting,
            "cancelled export writes nothing and releases its busy state");

        var image = Path.Combine(directory, "Selection.png");
        choose = () => Task.FromResult<IStorageFile?>(LocalStorageFile(image));
        await png.ExecuteAsync(null);
        using (var bitmap = new Bitmap(image))
            Check(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0 && requests[^1].DefaultExtension == "png",
                "selection PNG export writes a decodable image through the same host picker");
        var pngButton = b.Workbook.GetVisualDescendants().OfType<Button>()
            .Single(x => Avalonia.Automation.AutomationProperties.GetName(x) == "Export selected area as PNG");
        choose = () => Task.FromResult<IStorageFile?>(null);
        var requestsBefore = requests.Count;
        pngButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (b.ExportPng.ExecutionTask is { } toolbarExport) await toolbarExport;
        Check(requests.Count == requestsBefore + 1, "existing PNG toolbar button uses the host export action");

        var pending = new TaskCompletionSource<IStorageFile?>();
        choose = () => pending.Task;
        var exportTask = xlsx.ExecuteAsync(null);
        Check(b.IsExporting && !png.CanExecute(null) && !b.ExportXlsx.CanExecute(null),
            "an open picker prevents overlapping exports from its document");
        Service<IMainDockService>.For(dock).Set("CurrentDocument", a);
        body.Child = a.Workbook; Pump(window);
        Check(await b.SaveAsync() && b.OnClose(), "the originating document can close while its export picker is pending");
        var switchedCopy = Path.Combine(directory, "Captured.xlsx");
        pending.SetResult(LocalStorageFile(switchedCopy));
        await exportTask;
        using (var exported = UserModel.FromXlsx(switchedCopy))
            Check(exported.GetCellContent(0, 1, 1) == "25", "switching and closing documents during a picker preserves the original export snapshot");
        Check(errors.Count == 0 && OutputLines.Count == outputBefore, "panel and successful export operations report no errors");
        choose = () => Task.FromResult<IStorageFile?>(LocalStorageFile(Path.Combine(directory, "missing", "Failed.xlsx")));
        await xlsx.ExecuteAsync(null);
        Check(errors.Count == 0 && OutputLines.Count == outputBefore + 1 && _outputShows > 0 &&
              !a.IsExporting && xlsx.CanExecute(null) && a.LoadError is null,
            "export write failure opens host Output without a toast, preserves load state and restores command availability");
        Service<IMainDockService>.For(dock).Set("CurrentDocument", null);
        Check(!xlsx.CanExecute(null) && panels.All(item => !item.Command!.CanExecute(null)),
            "closing the current document disables workbook menu actions");
    }

    static IStorageFile LocalStorageFile(string path)
    {
        var file = Service<IStorageFile>.Create();
        Service<IStorageFile>.For(file).Values["Path"] = new Uri(path);
        Service<IStorageFile>.For(file).Values["Name"] = Path.GetFileName(path);
        return file;
    }
}
