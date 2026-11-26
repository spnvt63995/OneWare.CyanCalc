using CommunityToolkit.Mvvm.Input;
using global::CyanGraph;
using global::CyanGraph.Data;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.ViewModels;
using System.Runtime.Serialization;

namespace OneWare.CyanGraph.ViewModels;

public sealed class CyanGraphDocumentViewModel : ExtendedDocument
{
    readonly CyanGraphHost host;
    Task? cleanup;
    bool drained;

    [IgnoreDataMember] public CyanGraphView Editor { get; }
    [IgnoreDataMember] public bool IsClosing { get; private set; }

    public CyanGraphDocumentViewModel(CyanGraphHost host)
        : base(host.DocumentPath, host.Icons, host.Projects, host.Dock, host.Windows)
    {
        this.host = host;
        Id = "CyanGraph.Workspace"; Title = "CyanGraph";
        IsReadOnly = true; IsLoading = false;
        Undo = new RelayCommand(() => { }, () => false);
        Redo = new RelayCommand(() => { }, () => false);
        Editor = new CyanGraphView(new QueryWorkspace(), host.QuestionFile);
    }

    public override void InitializeContent()
    {
        if (IsClosing) return;
        // The layout serializer probes DI registrations. Only mounted documents own a session.
        FullPath = host.DocumentPath; Id = "CyanGraph.Workspace"; Title = "CyanGraph";
        host.Dock.OpenFiles.TryAdd(FullPath.ToPathKey(), this);
        host.Track(this);
    }

    protected override void UpdateCurrentFile(string? oldPath) { }

    public override bool OnClose()
    {
        if (drained) return true;
        if (!IsClosing) _ = CloseAfterDrainAsync();
        return false;
    }

    async Task CloseAfterDrainAsync()
    {
        await DrainAsync();
        host.Dock.CloseDockable(this);
    }

    internal Task DrainAsync() => cleanup ??= DrainCoreAsync();

    async Task DrainCoreAsync()
    {
        IsClosing = true;
        await Editor.CloseAsync();
        if (host.Dock.OpenFiles.TryGetValue(FullPath.ToPathKey(), out var active) && ReferenceEquals(active, this))
        {
            host.Dock.OpenFiles.Remove(FullPath.ToPathKey());
            host.Dock.UnregisterOpenFile(FullPath);
        }
        drained = true;
        host.Forget(this);
    }
}
