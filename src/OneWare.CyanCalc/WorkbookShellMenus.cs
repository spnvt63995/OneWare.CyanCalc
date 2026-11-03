using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::CyanCalc.Controls;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;

namespace OneWare.CyanCalc;

public sealed class WorkbookShellMenus(IMainDockService dock, IWindowService windows)
{
    private readonly List<IRelayCommand> _commands = [];
    private WorkbookDocumentViewModel? _current;
    private bool _registered;

    public void Register()
    {
        if (_registered) return;
        _registered = true;
        windows.RegisterMenuItem("MainWindow_MainMenu", new MenuItemModel("Workbook")
        {
            Header = "Workbook",
            Priority = 210
        });
        windows.RegisterMenuItem("MainWindow_MainMenu/Workbook",
            Panel("Names", "Name manager", workbook => workbook.ShowNames()),
            Panel("Styles", "Named styles", workbook => workbook.ShowNamedStyles()),
            Panel("ConditionalFormats", "Conditional formatting", workbook => workbook.ShowConditionalFormats()),
            Panel("Themes", "Workbook themes", workbook => workbook.ShowThemes()),
            Panel("Settings", "Regional settings", workbook => workbook.ShowSettings()));
        windows.RegisterMenuItem("MainWindow_MainMenu/File/Export",
            Export("Xlsx", "Workbook as XLSX…", document => document.ExportXlsx),
            Export("Png", "Selection as PNG…", document => document.ExportPng));
        dock.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(dock.CurrentDocument)) UpdateDocument();
        };
        UpdateDocument();
    }

    private MenuItemModel Panel(string id, string header, Action<WorkbookView> open)
    {
        var command = new RelayCommand(() =>
        {
            if (_current is { HasWorkbook: true } document) open(document.Workbook);
        }, () => _current?.HasWorkbook == true);
        _commands.Add(command);
        return new MenuItemModel("CyanCalc" + id) { Header = header, Command = command };
    }

    private MenuItemModel Export(string id, string header, Func<WorkbookDocumentViewModel, IAsyncRelayCommand> select)
    {
        var command = new AsyncRelayCommand(async () =>
        {
            if (_current is { } document && select(document).CanExecute(null))
                await select(document).ExecuteAsync(null);
        }, () => _current is { } document && select(document).CanExecute(null));
        _commands.Add(command);
        return new MenuItemModel("CyanCalcExport" + id) { Header = header, Command = command };
    }

    private void UpdateDocument()
    {
        if (_current is not null) _current.PropertyChanged -= DocumentChanged;
        _current = dock.CurrentDocument as WorkbookDocumentViewModel;
        if (_current is not null) _current.PropertyChanged += DocumentChanged;
        NotifyCommands();
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs e) => NotifyCommands();

    private void NotifyCommands()
    {
        foreach (var command in _commands) command.NotifyCanExecuteChanged();
    }
}
