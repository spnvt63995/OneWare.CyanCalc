using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CyanCalc;
using Dock.Controls.DeferredContentControl;
using Microsoft.Extensions.DependencyInjection;
using OneWare.CyanCalc.Projects;
using OneWare.CyanCalc.ViewModels;
using OneWare.CyanCalc.Views;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

internal static partial class Program
{
    static async Task CheckDeferredDocumentRehosting(string directory)
    {
        Directory.CreateDirectory(directory);
        var icons = Service<IFileIconService>.Create();
        ContainerLocator.SetContainer(new ServiceCollection().AddSingleton(icons).BuildServiceProvider());
        var path = Path.Combine(directory, "Deferred.ic");
        using (var model = new UserModel()) { model.SetUserInput(0, 1, 1, "42"); File.WriteAllBytes(path, model.ToBytes()); }
        var dock = Service<IMainDockService>.Create();
        var projects = Service<IProjectExplorerService>.Create();
        var windows = Service<IWindowService>.Create();
        Service<IMainDockService>.For(dock).Values["OpenFiles"] = new Dictionary<string, IExtendedDocument>();
        var document = new WorkbookDocumentViewModel(path, icons, projects, dock, windows, Logger);
        document.InitializeContent();
        var template = new FuncDataTemplate<WorkbookDocumentViewModel>((value, _) => new WorkbookDocumentView { DataContext = value });
        var source = new DeferredContentControl { Content = document, ContentTemplate = template };
        var window = new Window { Width = 1000, Height = 700, RequestedThemeVariant = ThemeVariant.Dark, Content = source };
        window.Show(); Pump(window);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), window) && document.Workbook.ActualThemeVariant == ThemeVariant.Dark,
            "Dock deferred presenter loads the workbook under the window's inherited dark theme");
        // Unlike removing a control from a Border, Dock removes a data-templated
        // view from its visual parent before its logical theme parent is cleared.
        source.Content = null; Pump(window);
        Check(TopLevel.GetTopLevel(document.Workbook) is null,
            "deferred source removal completes without changing content during visual detachment");
        var destination = new DeferredContentControl { Content = document, ContentTemplate = template };
        var floating = new Window { Width = 900, Height = 650, RequestedThemeVariant = ThemeVariant.Light, Content = destination };
        floating.Show(); Pump(floating);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), floating) && document.Workbook.ActualThemeVariant == ThemeVariant.Light,
            "deferred floating presenter renders the existing workbook with its destination theme");
        source.Content = document; destination.Content = null; Pump(window); Pump(floating);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), window) && document.Workbook.Session!.GetCellContent(0, 1, 1) == "42",
            "deferred redocking restores the same workbook without a stale release clearing it");
        source.Content = null; Pump(window); floating.Close(); window.Close(); document.OnClose();
        await Task.CompletedTask;
    }

    static async Task CheckDocumentRehosting(string directory)
    {
        Directory.CreateDirectory(directory);
        var icons = Service<IFileIconService>.Create();
        ContainerLocator.SetContainer(new ServiceCollection().AddSingleton(icons).BuildServiceProvider());
        var path = Path.Combine(directory, "Floating.ic");
        using (var model = new UserModel()) { model.SetUserInput(0, 1, 1, "10"); File.WriteAllBytes(path, model.ToBytes()); }
        var project = await new WorkbookProjectManager().CreateProjectAsync(Path.Combine(directory, "Floating.wbproj"));
        var dock = Service<IMainDockService>.Create();
        var projects = Service<IProjectExplorerService>.Create();
        var windows = Service<IWindowService>.Create();
        Service<IMainDockService>.For(dock).Values["OpenFiles"] = new Dictionary<string, IExtendedDocument>();
        Service<IProjectExplorerService>.For(projects).Values["ActiveProject"] = project;
        Service<IProjectExplorerService>.For(projects).Methods["GetRootFromFile"] = _ => project;
        var document = new WorkbookDocumentViewModel(path, icons, projects, dock, windows, Logger);
        document.InitializeContent();
        var toolbar = document.Workbook.GetLogicalDescendants().OfType<Border>().Single(x => x.Name == "WorkbookToolbar");
        Check(!toolbar.IsVisible, "plugin workbook starts with its toolbar hidden until its host is known");
        Service<IMainDockService>.For(dock).Values["CurrentDocument"] = document;
        var contribution = new WorkbookToolbarView(dock, projects);
        var left = new StackPanel { Height = 28, Children = { contribution } };
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
        var tray = new DockPanel { Name = "IconTray", LastChildFill = false, Children = { left } };
        var firstView = new WorkbookDocumentView { DataContext = document };
        var body = new Border { Child = firstView };
        Grid.SetRow(body, 1);
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Width = 1000, Height = 700, Content = new Grid { RowDefinitions = new("Auto,*"), Children = { tray, body } } };
        var inlineStartupLayouts = 0;
        void ObserveStartup(object? sender, EventArgs e)
        {
            if (toolbar.IsVisible && toolbar.GetVisualAncestors().Contains(document.Workbook)) inlineStartupLayouts++;
        }
        window.LayoutUpdated += ObserveStartup;
        window.Show(); Pump(window);
        window.LayoutUpdated -= ObserveStartup;
        Check(inlineStartupLayouts == 0 && contribution.IsVisible && document.Workbook.IsToolbarHosted,
            "initial layout shows the toolbar in the shell without an intermediate inline toolbar");
        var session = document.Workbook.Session!;
        document.Workbook.FormulaInput.Focus(); document.Workbook.FormulaInput.Text = "123";
        Check(document.Workbook.State.Editing is not null, "a cell draft is active before the document is floated");
        var floatingView = new WorkbookDocumentView { DataContext = document };
        var floating = new Window { Width = 900, Height = 650, Content = floatingView };
        // Dock creates the destination presenter before the source presenter is removed.
        floating.Show(); Pump(floating); Pump(window);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), floating) && firstView.Content is null,
            "floating transfers the workbook out of the old presenter before the new presenter attaches");
        Check(!contribution.IsVisible && document.Workbook.GetVisualDescendants().OfType<Border>()
                .Count(x => x.Name == "WorkbookToolbar" && x.IsVisible) == 1,
            "floating window gets one inline toolbar and releases the shell toolbar");
        Check(document.Workbook.GetVisualDescendants().OfType<Button>()
                .Count(x => AutomationProperties.GetName(x) is "Undo" or "Redo" && x.IsVisible) == 2,
            "floating toolbar restores local history buttons");
        Check(document.Workbook.CommitPendingEdit() && session.GetCellContent(0, 1, 1) == "123" && document.IsDirty,
            "floating preserves entered text and the document's unsaved state");
        var undo = document.Workbook.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == "Undo");
        var redo = document.Workbook.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == "Redo");
        Click(floating, undo);
        Check(session.GetCellContent(0, 1, 1) == "10", "floating toolbar Undo uses the document's existing history");
        Click(floating, redo);
        Check(session.GetCellContent(0, 1, 1) == "123", "floating toolbar Redo restores the edited value");
        var nextView = new WorkbookDocumentView { DataContext = document };
        body.Child = nextView; Pump(window); Pump(floating);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), window) && floatingView.Content is null && contribution.IsVisible,
            "redocking transfers the same workbook back and restores the shell toolbar");
        floating.Content = null; floating.Close(); Pump(window);
        Check(ReferenceEquals(document.Workbook.Session, session) && ReferenceEquals(nextView.Content, document.Workbook),
            "late disposal of the old floating presenter does not remove or reset the current workbook");
        Check(document.Undo!.CanExecute(null) && document.IsDirty && session.GetCellContent(0, 1, 1) == "123",
            "redocking retains the workbook's history and unsaved state");

        for (var i = 0; i < 4; i++)
        {
            var abandoned = new WorkbookDocumentView { DataContext = document };
            var transient = new Window { Width = 850, Height = 600, Content = abandoned };
            transient.Show();
            // Redock before the queued destination mount and toolbar restoration have run.
            transient.Content = null;
            body.Child = new WorkbookDocumentView { DataContext = document };
            Pump(window); Pump(transient); transient.Close();
        }
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), window) && contribution.IsVisible &&
              contribution.GetVisualDescendants().OfType<Border>().Count(x => x.Name == "WorkbookToolbar") == 1,
            "rapid float/redock cancels stale presenter mounts and keeps one toolbar");

        var reused = (WorkbookDocumentView)body.Child!;
        body.Child = null;
        var reusedWindow = new Window { Width = 900, Height = 650, Content = reused };
        reusedWindow.Show(); Pump(reusedWindow); Pump(window);
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), reusedWindow) && !document.Workbook.IsToolbarHosted,
            "moving the existing presenter to a floating window also restores inline hosting");
        reusedWindow.Content = null; body.Child = reused; Pump(window); reusedWindow.Close();
        Check(ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), window) && document.Workbook.IsToolbarHosted,
            "moving the existing presenter back restores shell hosting");
        await document.SaveAsync(); window.Close(); document.OnClose();
    }
}
