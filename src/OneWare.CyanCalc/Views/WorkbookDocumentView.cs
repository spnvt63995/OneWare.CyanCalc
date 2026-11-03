using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneWare.CyanCalc.ViewModels;

namespace OneWare.CyanCalc.Views;

public sealed class WorkbookDocumentView : UserControl
{
    private static readonly ConditionalWeakTable<WorkbookDocumentViewModel, WorkbookDocumentView> Hosts = new();
    private WorkbookDocumentViewModel? _document;
    private bool _placementResolved;

    public WorkbookDocumentView()
    {
        DataContextChanged += (_, _) =>
        {
            ReleaseDocument();
            if (this.IsAttachedToVisualTree()) AttachDocument();
        };
        AttachedToVisualTree += (_, _) => AttachDocument();
        DetachedFromVisualTree += (_, _) =>
        {
            // An ancestor (for example a closing window) can detach without
            // changing this view's own parent. Wait until that traversal returns.
            var document = _document;
            Dispatcher.UIThread.Post(() =>
            {
                if (!this.IsAttachedToVisualTree() && ReferenceEquals(_document, document)) ReleaseDocument();
            }, DispatcherPriority.Send);
        };
        LayoutUpdated += (_, _) =>
        {
            if (_placementResolved || _document is null || !ReferenceEquals(Content, _document.Workbook)) return;
            _placementResolved = true;
            var window = TopLevel.GetTopLevel(this);
            var toolbar = WorkbookToolbarView.ForWindow(window);
            _document.Workbook.SetInlineToolbarVisible(toolbar?.CanHost(_document, window) != true);
            toolbar?.DocumentMounted(_document);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // Dock detaches data-templated views before clearing their logical parents.
        // DetachedFromVisualTree fires BEFORE descendants finish detaching. Clearing
        // Content there refreshes themed children while their root is unreachable.
        // Release only after the complete subtree has detached, and before Dock can
        // reuse this presenter in another window.
        base.OnPropertyChanged(change);
        if (change.Property == VisualParentProperty && change.NewValue is null) ReleaseDocument();
    }

    private void AttachDocument()
    {
        if (_document is not null || DataContext is not WorkbookDocumentViewModel document) return;
        // Dock can create the destination view while its previous presenter is still attached.
        if (Hosts.TryGetValue(document, out var previous)) previous.ReleaseDocument();
        Hosts.Add(document, this);
        _document = document;
        _placementResolved = false;
        document.Workbook.SetInlineToolbarVisible(false);
        // Leave the workbook detached until the old window's queued layout has drained.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_document, document) || !this.IsAttachedToVisualTree() ||
                !Hosts.TryGetValue(document, out var host) || !ReferenceEquals(host, this)) return;
            Content = document.LoadingFailed
                ? new TextBlock { Text = document.LoadError ?? "Unable to open workbook." } : document.Workbook;
        }, DispatcherPriority.Loaded);
    }

    private void ReleaseDocument()
    {
        if (_document is not { } document) return;
        _document = null;
        if (Hosts.TryGetValue(document, out var host) && ReferenceEquals(host, this)) Hosts.Remove(document);
        document.Workbook.SetInlineToolbarVisible(false);
        document.Workbook.ReleaseHostedToolbar();
        WorkbookToolbarView.ForWindow(TopLevel.GetTopLevel(this))?.RefreshDocument();
        Content = null;
        // Content changes normally reach the visual tree during layout; a transfer needs them now.
        Presenter?.UpdateChild();
    }
}
