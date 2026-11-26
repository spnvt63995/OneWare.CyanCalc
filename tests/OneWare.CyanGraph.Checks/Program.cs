using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using OneWare.CyanGraph;
using OneWare.CyanGraph.ViewModels;
using OneWare.CyanGraph.Views;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

internal static class Program
{
    static int checks;

    [STAThread]
    static void Main()
    {
        AppBuilder.Configure<CheckApp>().UseHeadless(new()).SetupWithoutStarting();
        var task = RunAsync();
        using var finished = new CancellationTokenSource();
        _ = task.ContinueWith(_ => finished.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(finished.Token);
        task.GetAwaiter().GetResult();
        Console.WriteLine($"CYANGRAPH_ONEWARE_PASS {checks} checks");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    static async Task RunAsync()
    {
        var paths = Service<IPaths>.Create();
        Service<IPaths>.For(paths).Values["AppDataDirectory"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dock = Service<IMainDockService>.Create();
        var open = new Dictionary<string, IExtendedDocument>();
        var shown = new List<CyanGraphDocumentViewModel>();
        var api = Service<IMainDockService>.For(dock);
        api.Values["OpenFiles"] = open;
        api.Methods["SearchView"] = _ => shown.ToArray();
        api.Methods["Show"] = args =>
        {
            Check(Equals(args![1], DockShowLocation.Document), "menu targets the document dock");
            var document = (CyanGraphDocumentViewModel)args[0]!;
            if (!shown.Contains(document)) shown.Add(document);
            return null;
        };
        api.Methods["CloseDockable"] = args =>
        {
            var document = (CyanGraphDocumentViewModel)args![0]!;
            if (document.OnClose()) shown.Remove(document);
            return null;
        };
        MenuItemModel? menu = null;
        var windows = Service<IWindowService>.Create();
        Service<IWindowService>.For(windows).Methods["RegisterMenuItem"] = args =>
        {
            Check(Equals(args![0], "MainWindow_MainMenu/View"), "command registered in the View menu");
            menu = ((MenuItemModel[])args[1]!).Single();
            return null;
        };
        Action? shutdown = null;
        var app = Service<IApplicationStateService>.Create();
        Service<IApplicationStateService>.For(app).Methods["RegisterShutdownAction"] = args =>
        {
            shutdown = (Action)args![0]!;
            return null;
        };
        var services = new ServiceCollection().AddSingleton(paths).AddSingleton(dock).AddSingleton(windows)
            .AddSingleton(app).AddSingleton(Service<IFileIconService>.Create()).AddSingleton(Service<IProjectExplorerService>.Create());
        var module = new CyanGraphModule();
        module.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        ContainerLocator.SetContainer(provider);
        module.Initialize(provider);
        _ = provider.GetRequiredService<CyanGraphDocumentViewModel>();
        Check(open.Count == 0, "serializer probe does not register a document");
        menu!.Command!.Execute(null);
        var original = shown.Single();
        menu.Command.Execute(null);
        Check(shown.Single() == original && open.Values.Single() == original, "second invocation activates the same document");
        Check(original.IsReadOnly && !original.IsDirty && await original.SaveAsync(), "host Save keeps the graph out of workbook writes");

        var first = new CyanGraphDocumentView { DataContext = original };
        var window = new Window { Width = 1200, Height = 720, Content = first };
        window.Show();
        await LayoutAsync(window);
        var editor = original.Editor.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "Cypher query");
        editor.Text = "RETURN 202";
        var replacement = new CyanGraphDocumentView { DataContext = original };
        window.Content = replacement;
        await LayoutAsync(window);
        Check(first.Content == null && replacement.Content == original.Editor && editor.Text == "RETURN 202",
            "new dock presenter reuses the same editor and draft");

        Check(!original.OnClose() && original.IsClosing, "first close waits for asynchronous cleanup");
        Check(!original.OnClose(), "repeated close does not bypass cleanup");
        await LayoutAsync(window);
        Check(!shown.Contains(original) && open.Count == 0 && !original.Editor.IsEnabled, "cleanup unregisters only the closed graph document");
        menu.Command.Execute(null);
        var reopened = shown.Single();
        Check(!ReferenceEquals(reopened.Editor, original.Editor), "reopening creates an independent editor");
        replacement.DataContext = reopened;
        await LayoutAsync(window);
        var fresh = reopened.Editor.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "Cypher query");
        Check(string.IsNullOrEmpty(fresh.Text), "the reopened document starts without a draft");
        shutdown!();
        Check(reopened.IsClosing && open.Count == 0, "host shutdown drains and unregisters an open document");
        shutdown();
        menu.Command.Execute(null);
        Check(open.Count == 0, "late menu and shutdown callbacks are idempotent");
        window.Close();
    }

    static async Task LayoutAsync(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
    }
}

public sealed class CheckApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());
}
