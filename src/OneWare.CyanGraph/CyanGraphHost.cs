using Avalonia.Threading;
using OneWare.CyanGraph.ViewModels;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Services;

namespace OneWare.CyanGraph;

public sealed class CyanGraphHost(IPaths paths, IMainDockService dock, IFileIconService icons,
    IProjectExplorerService projects, IWindowService windows)
{
    readonly HashSet<CyanGraphDocumentViewModel> documents = [];
    bool shuttingDown;
    internal IMainDockService Dock => dock;
    internal IFileIconService Icons => icons;
    internal IProjectExplorerService Projects => projects;
    internal IWindowService Windows => windows;
    internal string DocumentPath { get; } = Path.Combine(paths.AppDataDirectory, "CyanGraph", "CyanGraph");
    internal string QuestionFile { get; } = Path.Combine(paths.AppDataDirectory, "CyanGraph", "questions.json");
    internal void Track(CyanGraphDocumentViewModel document) => documents.Add(document);
    internal void Forget(CyanGraphDocumentViewModel document) => documents.Remove(document);

    public void Open()
    {
        if (shuttingDown) return;
        var document = dock.SearchView<CyanGraphDocumentViewModel>().FirstOrDefault()
            ?? documents.FirstOrDefault() ?? new CyanGraphDocumentViewModel(this);
        if (document.IsClosing) return;
        document.InitializeContent();
        dock.Show(document, DockShowLocation.Document);
    }

    public void Shutdown()
    {
        shuttingDown = true;
        var closing = Task.WhenAll(documents.ToArray().Select(document => document.DrainAsync()));
        // OneWare runs final shutdown actions synchronously after the veto phase.
        if (!closing.IsCompleted && Dispatcher.UIThread.CheckAccess())
        {
            using var done = new CancellationTokenSource();
            _ = closing.ContinueWith(_ => done.Cancel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Dispatcher.UIThread.MainLoop(done.Token);
        }
        closing.GetAwaiter().GetResult();
    }
}
