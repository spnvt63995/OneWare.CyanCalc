using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using OneWare.Essentials.Services;

namespace OneWare.CyanCalc;

internal static class WorkbookFileIcons
{
    internal const string ResourceKey = "CyanCalc.WorkbookFileIcon";

    internal static void Register(IFileIconService icons)
    {
        Application.Current!.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://OneWare.CyanCalc/"))
        {
            Source = new Uri("avares://OneWare.CyanCalc/Resources/WorkbookIcons.axaml")
        });
        icons.RegisterFileIcon(ResourceKey, ".ic", ".xlsx", ".wbproj");
    }
}
