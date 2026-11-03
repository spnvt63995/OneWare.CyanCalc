using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CyanCalc.Controls;

// This partial class is compiled only into the OneWare plugin's UI assembly.
public sealed partial class WorkbookView
{
    private ToolbarAttachment? _externalToolbar;
    private Grid? _toolbarHome;
    private bool _inlineToolbarVisible = true;
    private bool _observingToolbarVisibility;
    private long _toolbarGeneration;

    public bool CommitPendingEdit() => CommitEdit();
    public bool IsToolbarHosted => _externalToolbar is not null;

    public void SetInlineToolbarVisible(bool visible)
    {
        _inlineToolbarVisible = visible;
        if (_toolbarRoot is not { } toolbar) return;
        if (!_observingToolbarVisibility)
        {
            _observingToolbarVisibility = true;
            // Workbook refreshes update IsVisible from CanEdit. Keep the host's
            // placement decision in effect while loading or moving the document.
            toolbar.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && toolbar.IsVisible &&
                    _externalToolbar is null && !_inlineToolbarVisible) toolbar.IsVisible = false;
            };
        }
        if (_externalToolbar is null) toolbar.IsVisible = visible && CanEdit;
    }

    public void ReleaseHostedToolbar() => _externalToolbar?.Dispose();

    public IDisposable AttachToolbar(Decorator host, bool useShellHistory = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_externalToolbar is not null || host.Child is not null || _toolbarRoot is not Border toolbar)
            throw new InvalidOperationException("The workbook toolbar already has a host.");
        var home = _toolbarHome ??= toolbar.Parent as Grid
            ?? throw new InvalidOperationException("The workbook toolbar has no home.");
        if (toolbar.Parent is not null && !ReferenceEquals(toolbar.Parent, home))
            throw new InvalidOperationException("The workbook toolbar already has a host.");
        if (TopLevel.GetTopLevel(this) is not { } window || TopLevel.GetTopLevel(host) != window)
            throw new InvalidOperationException("The toolbar and workbook must belong to the same window.");

        CancelExternalToolbarInput();
        if (!host.Classes.Contains("cyancalc-toolbar-host"))
        {
            WorkbookChrome.Attach(host);
            host.Classes.Add("cyancalc-toolbar-host");
        }
        host.SetValue(TextElement.FontFamilyProperty, FontFamily);
        // An ancestor StackPanel.toolbar applies Height=24 to Button descendants.
        // Local toolbar dimensions preserve the workbook's intended 28/16 sizing.
        foreach (var button in toolbar.GetVisualDescendants().OfType<Button>())
            if (button != _toolbarBack && button != _toolbarForward) button.Height = 28;
        if (ReferenceEquals(toolbar.Parent, home)) home.Children.Remove(toolbar);
        host.Child = toolbar;
        _toolbarGeneration++;
        _externalToolbar = new ToolbarAttachment(this, host, home, toolbar, useShellHistory);
        toolbar.IsVisible = CanEdit;
        return _externalToolbar;
    }

    private void CancelExternalToolbarInput()
    {
        _skipFontSizeBlur = _fontSizeInput?.IsFocused == true;
        RestoreFontSize();
        CloseToolbarPopups();
        _toolbarDialog?.Close();
    }

    private sealed class ToolbarAttachment(WorkbookView owner, Decorator host, Grid home, Border toolbar, bool useShellHistory) : IDisposable
    {
        private readonly (Control Control, bool WasVisible)[] _historyControls = useShellHistory ? HideHistoryControls(owner) : [];
        private bool _disposed;

        private static (Control, bool)[] HideHistoryControls(WorkbookView workbook)
        {
            var controls = new List<Control>();
            if (workbook._undoButton is { } undo) controls.Add(undo);
            if (workbook._redoButton is { } redo)
            {
                controls.Add(redo);
                if (redo.Parent is Panel actions)
                {
                    var dividerIndex = actions.Children.IndexOf(redo) + 1;
                    if (dividerIndex < actions.Children.Count &&
                        actions.Children[dividerIndex] is Border { Width: 1, Height: 16 } divider)
                        controls.Add(divider);
                }
            }
            var visibility = controls.Select(control => (control, control.IsVisible)).ToArray();
            foreach (var control in controls) control.IsVisible = false;
            return visibility;
        }

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            _disposed = true;
            owner.CancelExternalToolbarInput();
            if (ReferenceEquals(host.Child, toolbar)) host.Child = null;
            owner._externalToolbar = null;
            var generation = ++owner._toolbarGeneration;
            toolbar.IsVisible = owner._inlineToolbarVisible && owner.CanEdit;
            foreach (var (control, wasVisible) in _historyControls) control.IsVisible = wasVisible;
            // A document may be halfway through detaching during tab replacement.
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == owner._toolbarGeneration && owner._externalToolbar is null && toolbar.Parent is null)
                    home.Children.Insert(0, toolbar);
            }, DispatcherPriority.Loaded);
        }
    }
}
