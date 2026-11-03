using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.CyanCalc.Projects;
using OneWare.CyanCalc.ViewModels;
using OneWare.CyanCalc.Views;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;

namespace OneWare.CyanCalc;

public sealed class CyanCalcModule : OneWareModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<WorkbookProjectManager>();
        services.AddSingleton<WorkbookProjectActions>();
        services.AddSingleton<WorkbookShellMenus>();
        services.AddTransient<WorkbookDocumentViewModel>();
        services.AddTransient<WorkbookDocumentView>();
    }

    public override void Initialize(IServiceProvider services)
    {
        if (Application.Current is { } application) WorkbookInputStyles.Register(application);
        WorkbookFileIcons.Register(services.Resolve<IFileIconService>());
        var dock = services.Resolve<IMainDockService>();
        var projects = services.Resolve<IProjectExplorerService>();
        var windows = services.Resolve<IWindowService>();
        var manager = services.Resolve<WorkbookProjectManager>();
        services.Resolve<IProjectManagerService>().RegisterProjectManager(WorkbookProject.TypeId, manager);
        dock.RegisterDocumentView<WorkbookDocumentViewModel>(".ic", ".xlsx");
        windows.RegisterUiExtension("MainWindow_LeftToolBarExtension",
            new OneWareUiExtension(_ => new WorkbookToolbarView(dock, projects)));
        windows.RegisterMenuItem("MainWindow_MainMenu/File/Open", new MenuItemModel("CyanCalcWorkbookProject")
        {
            Header = "Workbook project…",
            Command = new AsyncRelayCommand(async () => await projects.LoadProjectFileDialogAsync(manager,
                new FilePickerFileType("Workbook project") { Patterns = ["*.wbproj"] }))
        });
        services.Resolve<WorkbookShellMenus>().Register();
        services.Resolve<WorkbookProjectActions>().Register();
    }
}
