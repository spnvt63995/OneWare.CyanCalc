using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneWare.CyanGraph.ViewModels;

namespace OneWare.CyanGraph.Views;

public sealed class CyanGraphDocumentView : UserControl
{
    public CyanGraphDocumentView()
    {
        DataContextChanged += (_, _) => { Release(); Attach(); };
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!this.IsAttachedToVisualTree()) Release();
        }, DispatcherPriority.Send);
    }

    void Attach()
    {
        // Dock can replace its presenter when switching tabs or floating a document.
        Dispatcher.UIThread.Post(() =>
        {
            if (!this.IsAttachedToVisualTree() || DataContext is not CyanGraphDocumentViewModel document ||
                document.IsClosing || ReferenceEquals(Content, document.Editor)) return;
            if (document.Editor.Parent is CyanGraphDocumentView previous) previous.Release();
            Content = document.Editor;
            document.InitializeContent();
        }, DispatcherPriority.Loaded);
    }

    void Release()
    {
        Content = null;
        Presenter?.UpdateChild();
    }
}
