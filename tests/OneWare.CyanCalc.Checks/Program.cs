using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyanCalc;
using CyanCalc.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneWare.CyanCalc.Projects;
using OneWare.CyanCalc.ViewModels;
using OneWare.CyanCalc.Views;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

internal static partial class Program
{
    private static int _passed;
    private static readonly ILogger Logger = NullLogger.Instance;
    private static readonly List<string> OutputLines = [];
    private static int _outputShows;
    [STAThread]
    static int Main(string[] args)
    {
        AppBuilder.Configure<CheckApp>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
        var directory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            ?? Path.Combine(Path.GetTempPath(), "cyancalc-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Console.WriteLine("Check data: " + directory);
        try
        {
            var task = args.Contains("--input-states") ? CheckWorkbookInputStates()
                : args.Contains("--feedback") ? CheckWorkbookFeedback(directory)
                : args.Contains("--file-actions") ? CheckWorkbookProjects(directory) : Run(directory);
            while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
            task.GetAwaiter().GetResult();
            Console.WriteLine($"PASS {_passed} checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
        _passed++; Console.WriteLine("PASS " + message);
    }

    static void Pump(Window window)
    {
        for (var i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump(window);
    }

    static async Task Run(string directory)
    {
        await CheckWorkbookInputStates();
        await CheckWorkbookFeedback(Path.Combine(directory, "feedback"));
        await CheckDeferredDocumentRehosting(Path.Combine(directory, "deferred-docking"));
        await CheckDocumentRehosting(Path.Combine(directory, "docking"));
        var bookA = Path.Combine(directory, "Budget.ic");
        var bookB = Path.Combine(directory, "Forecast.ic");
        using (var model = new UserModel())
        {
            model.SetUserInput(0, 1, 1, "10");
            File.WriteAllBytes(bookA, model.ToBytes());
            model.SetUserInput(0, 1, 1, "20");
            File.WriteAllBytes(bookB, model.ToBytes());
        }
        var projectPath = Path.Combine(directory, "Sample.wbproj");
        File.WriteAllText(projectPath, "{\"workbook\":\"Budget.ic\",\"include\":[\"**/*\"],\"exclude\":[]}");
        var manager = new WorkbookProjectManager();
        var root = (WorkbookProject)(await manager.LoadProjectAsync(projectPath))!;
        Check(root.StartupWorkbookPaths.SequenceEqual([bookA]), "legacy wbproj resolves its startup workbook");
        var invalid = new WorkbookProject(projectPath);
        invalid.Properties.SetString("workbook", "../outside.ic");
        try { _ = invalid.StartupWorkbookPaths; throw new Exception("outside path accepted"); }
        catch (InvalidDataException) { Check(true, "wbproj rejects a workbook outside its root"); }

        var dock = Service<IMainDockService>.Create();
        var projects = Service<IProjectExplorerService>.Create();
        var windows = Service<IWindowService>.Create();
        var icons = Service<IFileIconService>.Create();
        Service<IMainDockService>.For(dock).Values["OpenFiles"] = new Dictionary<string, IExtendedDocument>();
        var projectState = Service<IProjectExplorerService>.For(projects);
        projectState.Values["ActiveProject"] = root;
        projectState.Methods["GetRootFromFile"] = _ => root;
        UseHostFeedback(icons, dock, windows);
        var a = new WorkbookDocumentViewModel(bookA, icons, projects, dock, windows, Logger);
        var b = new WorkbookDocumentViewModel(bookB, icons, projects, dock, windows, Logger);
        a.InitializeContent(); b.InitializeContent();
        Check(!a.LoadingFailed && !b.LoadingFailed, "two independent workbooks load");
        Service<IMainDockService>.For(dock).Values["CurrentDocument"] = a;
        using var shellBindings = new ShellBindings(dock, projects);

        var contribution = new WorkbookToolbarView(dock, projects);
        var left = new StackPanel { Classes = { "toolbar" }, Height = 28, VerticalAlignment = VerticalAlignment.Center };
        var shellUndo = new Button { Classes = { "icon" }, Content = "↶" };
        var shellRedo = new Button { Classes = { "icon" }, Content = "↷" };
        shellUndo.Bind(Button.CommandProperty, new Binding("CurrentDocument.Undo") { Source = shellBindings });
        shellRedo.Bind(Button.CommandProperty, new Binding("CurrentDocument.Redo") { Source = shellBindings });
        left.Children.Add(shellUndo);
        left.Children.Add(shellRedo);
        var projectSelector = new ComboBox { Width = 155, ItemsSource = new[] { root }, DisplayMemberBinding = new Binding("Header") };
        projectSelector.Bind(ComboBox.SelectedItemProperty, new Binding("ActiveProject") { Source = shellBindings, Mode = BindingMode.TwoWay });
        left.Children.Add(projectSelector);
        left.Children.Add(contribution);
        var right = new StackPanel { Name = "RightButtons", Classes = { "toolbar" }, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 6; i++) right.Children.Add(new Button { Classes = { "icon" }, Width = 26, Height = 26, Content = "○" });
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left); DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        var tray = new DockPanel { Name = "IconTray", LastChildFill = false, Margin = new Thickness(4), Children = { left, right } };
        var body = new Border { Child = a.Workbook };
        Grid.SetRow(body, 1);
        var layout = new Grid { RowDefinitions = new("Auto,*"), Children = { tray, body } };
        var window = new Window { Width = 1200, Height = 700, Content = layout };
        window.Show(); Pump(window);
        Check(contribution.IsVisible && left.Height == 40, "project contribution raises only its host row to 40");
        Check(!a.Workbook.GetVisualDescendants().OfType<Control>().Any(x => x.Name == "WorkbookToolbar"), "toolbar has a single external parent");
        var toolbar = contribution.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "WorkbookToolbar");
        var buttons = toolbar.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("workbook-flat")).ToArray();
        var undo = buttons.First(x => AutomationProperties.GetName(x) == "Undo");
        var redo = buttons.First(x => AutomationProperties.GetName(x) == "Redo");
        var bold = toolbar.GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x) == "Bold");
        var actions = toolbar.GetVisualDescendants().OfType<StackPanel>().Single(x => x.Name == "WorkbookToolbarActions");
        Check(!undo.IsVisible && !redo.IsVisible && actions.Children.First(c => c.IsVisible) is Button,
            "hosted toolbar omits duplicate history buttons and their divider");
        Check(ReferenceEquals(shellUndo.Command, a.Undo) && ReferenceEquals(shellRedo.Command, a.Redo) &&
            !shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled,
            "shell history buttons bind to the active document and reflect its empty history");
        Check(bold.Bounds.Height == 28, "OneWare ancestor styles do not shrink workbook buttons to 24");
        Check(bold.GetVisualDescendants().OfType<Control>().Any(x => x.GetType().Name == "WorkbookIcon" && x.Bounds.Width == 16), "workbook icon remains 16");
        var measurements = new List<object>();
        foreach (var width in new[] { 1200, 1000, 800, 600 })
        {
            window.Width = width; Pump(window);
            var origin = right.TranslatePoint(default, window)!.Value;
            Check(origin.X + right.Bounds.Width <= window.ClientSize.Width + .5 && right.Bounds.Width >= 176,
                $"right shell actions remain visible at width {width}");
            var scroll = toolbar.GetVisualDescendants().OfType<ScrollViewer>().First();
            Check(scroll.Viewport.Width > 30 && scroll.Extent.Width > scroll.Viewport.Width, $"toolbar retains bounded overflow at width {width}");
            measurements.Add(new { width, rowHeight = tray.Bounds.Height, contributionWidth = contribution.Bounds.Width,
                rightWidth = right.Bounds.Width, rightEdge = origin.X + right.Bounds.Width, viewport = scroll.Viewport.Width, extent = scroll.Extent.Width });
        }

        bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(window);
        Check(a.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B, "external Bold modifies active workbook A");
        Check(!b.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B, "external command leaves workbook B unchanged");
        Check(shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled, "shell history availability updates after formatting");
        Click(window, shellUndo);
        Check(!a.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B && !shellUndo.IsEffectivelyEnabled && shellRedo.IsEffectivelyEnabled,
            "shell Undo click restores formatting and enables Redo");
        Click(window, shellRedo);
        Check(a.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B && shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled,
            "shell Redo click restores formatting and enables Undo");
        a.IsReadOnly = true; Pump(window);
        Check(!shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled, "read-only workbook disables shell history actions");
        a.IsReadOnly = false; Pump(window);
        Check(shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled, "writable workbook restores shell history availability");

        window.RequestedThemeVariant = ThemeVariant.Light; Pump(window);
        var light = ((ISolidColorBrush)toolbar.Background!).Color;
        window.RequestedThemeVariant = ThemeVariant.Dark; Pump(window);
        Check(((ISolidColorBrush)toolbar.Background!).Color != light && toolbar.Bounds.Height == 40,
            "external toolbar follows the host theme without changing its height");
        var fontSize = toolbar.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "FontSizeInput");
        fontSize.Focus(); fontSize.Text = "42";

        body.Child = b.Workbook;
        Service<IMainDockService>.For(dock).Set("CurrentDocument", b); Pump(window);
        Check(a.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.Sz == 12,
            "document switch cancels the departing toolbar's font draft");
        Check(a.Workbook.GetLogicalDescendants().OfType<Control>().Any(x => x.Name == "WorkbookToolbar"), "switching documents restores A's inline toolbar");
        Check(!b.Workbook.GetVisualDescendants().OfType<Control>().Any(x => x.Name == "WorkbookToolbar"), "switching documents mounts B's toolbar");
        Check(!shellUndo.IsEffectivelyEnabled && !shellRedo.IsEffectivelyEnabled, "shell history switches to document B's empty history");
        var bBold = contribution.GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x) == "Bold");
        bBold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(b.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B, "external command follows document B");
        Click(window, shellUndo);
        Check(!b.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B && a.Workbook.Snapshot!.Cell.ExtendedStyle.Style.Font.B,
            "shell Undo follows active document B without changing A");
        Click(window, shellRedo);
        Check(await b.SaveAsync(), "save writes the active document");
        using (var saved = UserModel.FromICalc(bookB)) Check(saved.GetExtendedCellStyle(0, 1, 1).Style.Font.B, "saved IC retains formatting");
        for (var i = 0; i < 6; i++)
        {
            var target = i % 2 == 0 ? a : b;
            body.Child = target.Workbook;
            Service<IMainDockService>.For(dock).Set("CurrentDocument", target); Pump(window);
        }
        Check(contribution.IsVisible && contribution.GetVisualDescendants().OfType<Border>().Count(x => x.Name == "WorkbookToolbar") == 1,
            "repeated document switches keep exactly one toolbar");
        projectState.Set("ActiveProject", new WorkbookProject(projectPath)); Pump(window);
        Check(!contribution.IsVisible, "unregistered duplicate project does not claim the existing workbook toolbar");
        projectSelector.SelectedItem = root; Pump(window);
        Check(ReferenceEquals(projects.ActiveProject, root) && contribution.IsVisible,
            "selecting the existing workbook project restores its shell toolbar");
        projectState.Set("ActiveProject", null); Pump(window);
        Check(!contribution.IsVisible && left.Height == 28, "leaving workbook project restores the original shell height");
        Check(b.Workbook.GetVisualDescendants().OfType<Control>().Any(x => x.Name == "WorkbookToolbar"), "leaving project restores inline toolbar");
        Check(b.Workbook.GetVisualDescendants().OfType<Button>()
            .Where(x => AutomationProperties.GetName(x) is "Undo" or "Redo").Count(x => x.IsVisible) == 2,
            "inline toolbar restores its history buttons when released by the shell");
        Check(ReferenceEquals(tray.Children[0], left) && ReferenceEquals(tray.Children[1], right), "leaving project restores shell child order");
        await CheckShellMenus(directory, window, body, a, b, dock, windows);
        await a.SaveAsync();
        window.Close();
        a.OnClose(); b.OnClose();
        await CheckWorkbookProjects(Path.Combine(directory, "projects"));
        File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
    }
    static void UseHostFeedback(IFileIconService icons, IMainDockService dock, IWindowService windows)
    {
        OutputLines.Clear();
        _outputShows = 0;
        var output = Service<IOutputService>.Create();
        Service<IOutputService>.For(output).Methods["WriteLine"] = args => { OutputLines.Add((string)args![0]!); return null; };
        Service<IMainDockService>.For(dock).Methods["Show"] = args =>
        {
            if (ReferenceEquals(args![0], output) && (DockShowLocation)args[1]! == DockShowLocation.Bottom) _outputShows++;
            return null;
        };
        ContainerLocator.SetContainer(new ServiceCollection().AddSingleton(icons).AddSingleton(dock)
            .AddSingleton(windows).AddSingleton(output).BuildServiceProvider());
        OneWare.Essentials.Services.LoggerExtensions.LayoutLoaded = true;
    }
}

public sealed class CheckApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new SimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://OneWare.Essentials/")) { Source = new Uri("avares://OneWare.Essentials/Styles/OneWareTheme.axaml") });
        OneWare.CyanCalc.WorkbookInputStyles.Register(this);
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://OneWare.CyanCalc/"))
            { Source = new Uri("avares://OneWare.CyanCalc/Resources/WorkbookIcons.axaml") });
    }
}

// Reflection bindings require concrete properties; DispatchProxy only exposes interface methods.
public sealed class ShellBindings : INotifyPropertyChanged, IDisposable
{
    private readonly IMainDockService _dock;
    private readonly IProjectExplorerService _projects;
    public ShellBindings(IMainDockService dock, IProjectExplorerService projects)
    {
        _dock = dock; _projects = projects;
        _dock.PropertyChanged += Changed;
        _projects.PropertyChanged += Changed;
    }
    public IExtendedDocument? CurrentDocument => _dock.CurrentDocument;
    public IProjectRoot? ActiveProject { get => _projects.ActiveProject; set => _projects.ActiveProject = value; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(object? sender, PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
    public void Dispose()
    {
        _dock.PropertyChanged -= Changed;
        _projects.PropertyChanged -= Changed;
    }
}

public class Service<T> : DispatchProxy where T : class
{
    public readonly Dictionary<string, object?> Values = [];
    public readonly Dictionary<string, Func<object?[]?, object?>> Methods = [];
    private PropertyChangedEventHandler? _changed;
    public static T Create() => DispatchProxy.Create<T, Service<T>>();
    public static Service<T> For(T service) => (Service<T>)(object)service;
    public void Set(string name, object? value) { Values[name] = value; _changed?.Invoke(this, new(name)); }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        var name = method!.Name;
        if (name == "add_PropertyChanged") { _changed += (PropertyChangedEventHandler)arguments![0]!; return null; }
        if (name == "remove_PropertyChanged") { _changed -= (PropertyChangedEventHandler)arguments![0]!; return null; }
        if (name.StartsWith("get_")) return Values.GetValueOrDefault(name[4..]);
        if (name.StartsWith("set_")) { Set(name[4..], arguments![0]); return null; }
        if (Methods.TryGetValue(name, out var invoke)) return invoke(arguments);
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
