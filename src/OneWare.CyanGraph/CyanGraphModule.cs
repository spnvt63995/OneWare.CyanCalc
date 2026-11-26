using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.CyanGraph.ViewModels;
using OneWare.CyanGraph.Views;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;

namespace OneWare.CyanGraph;

public sealed class CyanGraphModule : OneWareModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<CyanGraphHost>();
        services.AddTransient<CyanGraphDocumentViewModel>();
        services.AddTransient<CyanGraphDocumentView>();
    }

    public override void Initialize(IServiceProvider services)
    {
        var host = services.Resolve<CyanGraphHost>();
        services.Resolve<IWindowService>().RegisterMenuItem("MainWindow_MainMenu/View",
            new MenuItemModel("CyanGraph") { Header = "CyanGraph", Command = new RelayCommand(host.Open) });
        services.Resolve<IApplicationStateService>().RegisterShutdownAction(host.Shutdown);
    }
}
