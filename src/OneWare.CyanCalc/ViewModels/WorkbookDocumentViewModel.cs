using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using global::CyanCalc;
using global::CyanCalc.Controls;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

namespace OneWare.CyanCalc.ViewModels;

public sealed class WorkbookDocumentViewModel : ExtendedDocument
{
    private readonly IMainDockService _dock;
    private readonly ILogger _logger;
    private readonly WorkbookFeedback _feedback;
    private UserModel? _model;
    public WorkbookView Workbook { get; } = new();
    public string? LoadError { get; private set; }
    public bool HasWorkbook => _model is not null && !IsLoading && !LoadingFailed;
    public bool IsExporting { get; private set; }
    public IAsyncRelayCommand ExportXlsx { get; }
    public IAsyncRelayCommand ExportPng { get; }

    public WorkbookDocumentViewModel(string fullPath, IFileIconService icons, IProjectExplorerService projects,
        IMainDockService mainDockService, IWindowService windows, ILogger logger)
        : base(fullPath, icons, projects, mainDockService, windows)
    {
        // A parameter named dock would collide with Dock's persisted numeric Dock property.
        _dock = mainDockService;
        _logger = logger;
        _feedback = new WorkbookFeedback(Workbook, () => Path.GetFileName(FullPath), logger);
        // The document presenter chooses the toolbar's window before its first layout.
        Workbook.SetInlineToolbarVisible(false);
        ExportXlsx = new AsyncRelayCommand(() => ExportAsync(false), () => HasWorkbook && !IsExporting);
        ExportPng = new AsyncRelayCommand(() => ExportAsync(true), () => HasWorkbook && !IsExporting);
        Workbook.ExportPngRequested += (_, _) =>
        {
            if (ExportPng.CanExecute(null)) ExportPng.Execute(null);
        };
        Undo = new RelayCommand(Workbook.Undo, () => !IsReadOnly && Workbook.CanEdit && _model?.CanUndo() == true);
        Redo = new RelayCommand(Workbook.Redo, () => !IsReadOnly && Workbook.CanEdit && _model?.CanRedo() == true);
        Workbook.DocumentChanged += (_, _) => IsDirty = true;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsReadOnly))
            {
                Workbook.CanEdit = !IsReadOnly;
                ModelChanged(this, EventArgs.Empty);
            }
        };
    }

    protected override void UpdateCurrentFile(string? oldPath)
    {
        if (_model is not null && string.Equals(oldPath, FullPath, StringComparison.Ordinal)) return;
        Reset();
        IsLoading = true;
        try
        {
            _model = Path.GetExtension(FullPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? UserModel.FromXlsx(FullPath) : UserModel.FromICalc(FullPath);
            Workbook.Session = _model;
            _model.Changed += ModelChanged;
            LoadingFailed = false;
            LoadError = null;
            IsDirty = false;
            ModelChanged(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            LoadError = error.Message; LoadingFailed = true;
            _logger.Error("CyanCalc — " + Path.GetFileName(FullPath) + " — Open: " + error.Message, error);
        }
        finally { IsLoading = false; NotifyExportCommands(); }
    }

    private void ModelChanged(object? sender, EventArgs e)
    {
        Undo?.NotifyCanExecuteChanged();
        Redo?.NotifyCanExecuteChanged();
    }

    public override Task<bool> SaveAsync()
    {
        if (_model is null || IsReadOnly || !Workbook.CommitPendingEdit()) return Task.FromResult(false);
        var temporary = FullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = Path.GetExtension(FullPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? _model.ToXlsx() : _model.ToBytes();
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, FullPath, true);
            LastSaveTime = File.GetLastWriteTime(FullPath);
            IsDirty = false;
            _feedback.Clear();
            return Task.FromResult(true);
        }
        catch (Exception error) { _feedback.FileFailure("Save", FullPath, error); return Task.FromResult(false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task ExportAsync(bool png)
    {
        if (!HasWorkbook || IsExporting || _model is not { } model) return;
        var owner = _dock.GetWindowOwner(this) ?? TopLevel.GetTopLevel(Workbook);
        if (owner is null) return;
        var source = FullPath;
        var operation = png ? "Export PNG" : "Export XLSX";
        IsExporting = true;
        NotifyExportCommands();
        try
        {
            if (!Workbook.CommitPendingEdit()) return;
            // Capture this document before the picker can change the active tab or close it.
            byte[] bytes;
            if (png)
            {
                using var image = new MemoryStream();
                Workbook.ExportSelectionToPng(image);
                bytes = image.ToArray();
            }
            else bytes = model.ToXlsx();
            var extension = png ? "png" : "xlsx";
            using var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = png ? "Export selection as PNG" : "Export workbook as XLSX",
                SuggestedFileName = Path.GetFileNameWithoutExtension(source) + (png ? "-selection" : ""),
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(png ? "PNG image" : "Excel workbook") { Patterns = ["*." + extension] }]
            });
            if (file is null) return;
            if (file.TryGetLocalPath() is { } destination)
            {
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes);
                    File.Move(temporary, destination, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
            }
            if (ReferenceEquals(_model, model)) _feedback.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_model, model)) _feedback.FileFailure(operation, source, error);
            else _logger.Error("CyanCalc — " + Path.GetFileName(source) + " — " + operation + ": " + error.Message, error);
        }
        finally { IsExporting = false; NotifyExportCommands(); }
    }

    private void NotifyExportCommands()
    {
        OnPropertyChanged(nameof(HasWorkbook));
        OnPropertyChanged(nameof(IsExporting));
        ExportXlsx.NotifyCanExecuteChanged();
        ExportPng.NotifyCanExecuteChanged();
    }

    protected override void Reset()
    {
        _feedback.Clear();
        Workbook.Session = null;
        if (_model is not null) { _model.Changed -= ModelChanged; _model.Dispose(); _model = null; }
        ModelChanged(this, EventArgs.Empty);
        NotifyExportCommands();
    }

}
