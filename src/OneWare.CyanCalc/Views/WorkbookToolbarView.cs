using System.ComponentModel;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using OneWare.CyanCalc.Projects;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

namespace OneWare.CyanCalc.Views;

// The window owns the slot; a document owns its toolbar and editor state.
public sealed class WorkbookToolbarView : UserControl
{
    private static readonly ConditionalWeakTable<TopLevel, WorkbookToolbarView> Hosts = new();
    private readonly IMainDockService _dock;
    private readonly IProjectExplorerService _projects;
    private readonly bool _floating;
    private readonly Border _host = new();
    private TopLevel? _window;
    private INotifyPropertyChanged? _root;
    private IExtendedDocument? _selected;
    private WorkbookDocumentViewModel? _document;
    private IDisposable? _attachment, _undoBinding, _redoBinding;
    private StackPanel? _shellRow;
    private DockPanel? _tray;
    private Control[]? _originalOrder;
    private double _previousRowHeight;
    private bool _attached, _pending, _waiting;
    private long _lifetime;

    public WorkbookToolbarView(IMainDockService dock, IProjectExplorerService projects, bool floating = false)
    {
        _dock = dock; _projects = projects; _floating = floating;
        Name = floating ? "CyanCalcWindowToolbar" : "CyanCalcProjectToolbar";
        Height = 40; Width = floating ? double.NaN : 0; ClipToBounds = true; IsVisible = false;
        if (floating) Margin = new Thickness(4);
        Content = _host;
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        // Layout only measures the existing slot or completes a pending mount.
        // It does not select a document or release/reacquire the shell geometry.
        LayoutUpdated += (_, _) =>
        {
            MeasureSlot();
            if (_waiting && _selected is WorkbookDocumentViewModel document &&
                ReferenceEquals(TopLevel.GetTopLevel(document.Workbook), _window)) RefreshDocument();
        };
    }

    internal static WorkbookToolbarView? ForWindow(TopLevel? window) =>
        window is not null && Hosts.TryGetValue(window, out var host) ? host : null;

    private void Attach()
    {
        if (_attached) return;
        Release();
        _attached = true;
        _lifetime++;
        _window = TopLevel.GetTopLevel(this);
        if (_window is null) return;
        Hosts.Remove(_window);
        Hosts.Add(_window, this);
        _window.DataContextChanged += WindowContextChanged;
        _dock.PropertyChanged += Changed;
        _projects.PropertyChanged += Changed;
        ObserveRoot();
        RefreshDocument();
    }

    private void Detach()
    {
        _attached = false;
        _pending = false;
        var lifetime = ++_lifetime;
        _dock.PropertyChanged -= Changed;
        _projects.PropertyChanged -= Changed;
        if (_root is not null) _root.PropertyChanged -= RootChanged;
        _root = null;
        if (_window is { } window)
        {
            window.DataContextChanged -= WindowContextChanged;
            if (ReferenceEquals(ForWindow(window), this)) Hosts.Remove(window);
        }
        // The subtree must finish detaching before changing any themed descendants.
        Dispatcher.UIThread.Post(() =>
        {
            if (_attached || lifetime != _lifetime) return;
            Release(); _selected = null; _window = null;
        }, DispatcherPriority.Send);
    }

    private void WindowContextChanged(object? sender, EventArgs e) { ObserveRoot(); RefreshDocument(); }

    private void ObserveRoot()
    {
        if (_root is not null) _root.PropertyChanged -= RootChanged;
        _root = (_floating ? _window?.DataContext as IRootDock : _dock.Layout) as INotifyPropertyChanged;
        if (_root is not null) _root.PropertyChanged += RootChanged;
    }

    private void RootChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "FocusedDockable" or "ActiveDockable") RefreshDocument();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IMainDockService.CurrentDocument) or nameof(IProjectExplorerService.ActiveProject))
            RefreshDocument();
        else if (e.PropertyName == nameof(IMainDockService.Layout)) { ObserveRoot(); RefreshDocument(); }
    }

    internal void DocumentMounted(WorkbookDocumentViewModel document)
    {
        if (_selected is null || ReferenceEquals(document, _dock.CurrentDocument) || !BelongsHere(_selected))
            _selected = document;
        RefreshDocument();
    }

    internal void RefreshDocument()
    {
        if (!_attached || _pending) return;
        _pending = true;
        var lifetime = _lifetime;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_attached || lifetime != _lifetime) return;
            _pending = false;
            Update();
        }, DispatcherPriority.Loaded);
    }

    private bool BelongsHere(IExtendedDocument document)
    {
        if (_window is null) return false;
        var owner = document is WorkbookDocumentViewModel workbook ? TopLevel.GetTopLevel(workbook.Workbook) : null;
        owner ??= _dock.GetWindowOwner(document);
        return ReferenceEquals(owner, _window);
    }

    internal bool CanHost(WorkbookDocumentViewModel document, TopLevel? documentWindow = null)
    {
        if (!_attached || _window is null ||
            !ReferenceEquals(documentWindow ?? TopLevel.GetTopLevel(document.Workbook), _window)) return false;
        var project = _projects.GetRootFromFile(document.FullPath) as WorkbookProject;
        return project?.ContainsWorkbook(document.FullPath) == true &&
            (_floating || ReferenceEquals(_projects.ActiveProject, project)) &&
            (_floating || FindTray() is not null);
    }

    private DockPanel? FindTray() => this.GetVisualAncestors().OfType<DockPanel>().FirstOrDefault(p => p.Name == "IconTray" &&
        this.GetVisualAncestors().OfType<StackPanel>().Any(row => ReferenceEquals(row.Parent, p) && DockPanel.GetDock(row) == Avalonia.Controls.Dock.Left));

    private void Update()
    {
        var current = _dock.CurrentDocument;
        var focused = (_root as IRootDock)?.FocusedDockable as IExtendedDocument;
        if (current is not null && BelongsHere(current)) _selected = current;
        else if (focused is not null && BelongsHere(focused)) _selected = focused;
        else if (_selected is not null && !BelongsHere(_selected)) _selected = null;

        // Before the initial presenter mounts there is no visual owner yet.
        if (_selected is null && !_floating && current is WorkbookDocumentViewModel initial &&
            TopLevel.GetTopLevel(initial.Workbook) is null && _dock.GetWindowOwner(initial) is null) _selected = initial;
        var next = _selected as WorkbookDocumentViewModel;
        _waiting = next is { HasWorkbook: true } && TopLevel.GetTopLevel(next.Workbook) is null;
        if (_waiting) { IsEnabled = false; return; }
        if (next is null || !next.HasWorkbook || !CanHost(next))
        {
            Release();
            if (next is not null && ReferenceEquals(TopLevel.GetTopLevel(next.Workbook), _window))
                next.Workbook.SetInlineToolbarVisible(true);
            return;
        }

        next.Workbook.SetInlineToolbarVisible(false);
        IsVisible = true;
        IsEnabled = true;
        if (TopLevel.GetTopLevel(_host) != _window)
        {
            if (!_floating) Width = 1;
            _waiting = true;
            return;
        }
        AcquireSlot();
        if (!ReferenceEquals(next, _document) || !next.Workbook.IsToolbarHosted)
        {
            ReleaseDocument();
            _attachment = next.Workbook.AttachToolbar(_host, useShellHistory: !_floating);
            _document = next;
            BindShellHistory(next);
        }
        MeasureSlot();
    }

    private void AcquireSlot()
    {
        if (_floating || _shellRow is not null) return;
        _tray = FindTray();
        _shellRow = this.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault(row => ReferenceEquals(row.Parent, _tray));
        if (_shellRow is null || _tray is null) return;
        _previousRowHeight = _shellRow.Height;
        _shellRow.Height = 40;
        _originalOrder = _tray.Children.ToArray();
        var right = _tray.Children.Where(c => DockPanel.GetDock(c) == Avalonia.Controls.Dock.Right).ToArray();
        for (var i = 0; i < right.Length; i++) _tray.Children.Move(_tray.Children.IndexOf(right[i]), i);
    }

    private void BindShellHistory(WorkbookDocumentViewModel document)
    {
        if (_floating || _shellRow is null) return;
        // Override while this window owns the contribution, preserving the shell's
        // original binding underneath. Other windows cannot redirect these buttons.
        var buttons = _shellRow.Children.OfType<Button>().Take(2).ToArray();
        if (buttons.Length != 2) return;
        _undoBinding = buttons[0].Bind(Button.CommandProperty, Observable.Return(document.Undo), BindingPriority.Animation);
        _redoBinding = buttons[1].Bind(Button.CommandProperty, Observable.Return(document.Redo), BindingPriority.Animation);
    }

    private void MeasureSlot()
    {
        if (!_attached || _floating || _shellRow is null || _tray is null || this.TranslatePoint(default, _tray) is not { } origin) return;
        var rightWidth = _tray.Children.OfType<Control>()
            .Where(c => c.IsVisible && DockPanel.GetDock(c) == Avalonia.Controls.Dock.Right).Sum(c => c.DesiredSize.Width);
        var width = Math.Max(0, _tray.Bounds.Width - origin.X - rightWidth - 8);
        if (Math.Abs(Width - width) > .5) Width = width;
    }

    private void ReleaseDocument()
    {
        _undoBinding?.Dispose(); _undoBinding = null;
        _redoBinding?.Dispose(); _redoBinding = null;
        if (_document is not null) _document.Workbook.SetInlineToolbarVisible(!CanHost(_document));
        _attachment?.Dispose(); _attachment = null;
        _document = null;
    }

    private void Release()
    {
        ReleaseDocument();
        _waiting = false;
        IsVisible = false;
        if (!_floating) Width = 0;
        if (_shellRow is not null && _shellRow.Height == 40) _shellRow.Height = _previousRowHeight;
        _shellRow = null;
        if (_tray is not null && _originalOrder is not null)
            for (var i = 0; i < _originalOrder.Length; i++)
                if (_tray.Children.Contains(_originalOrder[i])) _tray.Children.Move(_tray.Children.IndexOf(_originalOrder[i]), i);
        _tray = null; _originalOrder = null;
    }
}
