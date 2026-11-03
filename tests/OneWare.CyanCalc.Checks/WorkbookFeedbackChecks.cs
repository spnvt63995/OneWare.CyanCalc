using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyanCalc;
using CyanCalc.Controls;
using OneWare.CyanCalc.ViewModels;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;

internal static partial class Program
{
    static async Task CheckWorkbookFeedback(string directory)
    {
        Directory.CreateDirectory(directory);
        var aPath = Path.Combine(directory, "FeedbackA.ic");
        var bPath = Path.Combine(directory, "FeedbackB.ic");
        using (var model = new UserModel())
        {
            model.SetUserInput(0, 1, 1, "25");
            File.WriteAllBytes(aPath, model.ToBytes());
            File.WriteAllBytes(bPath, model.ToBytes());
        }
        var dock = Service<IMainDockService>.Create();
        var projects = Service<IProjectExplorerService>.Create();
        var windows = Service<IWindowService>.Create();
        var icons = Service<IFileIconService>.Create();
        Service<IMainDockService>.For(dock).Values["OpenFiles"] = new Dictionary<string, IExtendedDocument>();
        UseHostFeedback(icons, dock, windows);
        var toasts = 0;
        Service<IWindowService>.For(windows).Methods["ShowNotification"] = _ => { toasts++; return null; };
        var a = new WorkbookDocumentViewModel(aPath, icons, projects, dock, windows, Logger);
        var b = new WorkbookDocumentViewModel(bPath, icons, projects, dock, windows, Logger);
        a.InitializeContent(); b.InitializeContent();
        var main = new Window { Width = 1000, Height = 700, Content = a.Workbook, RequestedThemeVariant = ThemeVariant.Dark };
        var floating = new Window { Width = 900, Height = 600 };
        main.Show(); Pump(main);
        Border Notice(WorkbookView book) => book.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "WorkbookFeedback");
        TextBlock Message(WorkbookView book) => book.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "WorkbookFeedbackMessage");
        var localizedReport = typeof(WorkbookView).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "Report" && method.GetParameters()[0].ParameterType == typeof(LocalizedText));
        void Report(WorkbookView book, string key) =>
            localizedReport.Invoke(book, [book.Text.Key(key, ("error", "clipboard unavailable"))]);
        try
        {
            a.Workbook.FormulaInput.Focus();
            var focused = main.FocusManager!.GetFocusedElement();
            var navigate = typeof(WorkbookView).GetMethod("TryNavigateAddressOrName", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(!(bool)navigate.Invoke(a.Workbook, ["NoSuchWorkbookName"])!, "unknown reference is rejected");
            Pump(main);
            Check(Notice(a.Workbook).IsVisible && Notice(a.Workbook).Classes.Contains("warning") &&
                  ReferenceEquals(main.FocusManager.GetFocusedElement(), focused) && toasts == 0 && OutputLines.Count == 0,
                "reference guidance stays in its document without taking focus or opening global feedback");
            Report(a.Workbook, "native.read_only"); Pump(main);
            Check(Notice(a.Workbook).IsVisible && !Notice(a.Workbook).Classes.Contains("warning") &&
                  !Notice(a.Workbook).Classes.Contains("error") && Message(a.Workbook).Text == a.Workbook.Text["native.read_only"],
                "known edit limitations are information and the paired literal event does not replace them");

            Report(a.Workbook, "native.copy_failed"); Pump(main);
            Report(a.Workbook, "native.copy_failed"); Pump(main);
            Check(Notice(a.Workbook).Classes.Contains("error") && OutputLines.Count == 1 &&
                  OutputLines[0].Contains("FeedbackA.ic") && toasts == 0,
                "clipboard failure has one document error and one named Output entry even when repeated");

            var darkText = ((ISolidColorBrush)Message(a.Workbook).Foreground!).Color;
            main.RequestedThemeVariant = ThemeVariant.Light; Pump(main);
            Check(Notice(a.Workbook).Background is not null &&
                  ((ISolidColorBrush)Message(a.Workbook).Foreground!).Color != darkText,
                "document feedback uses host theme resources in light and dark");

            main.Content = b.Workbook; Pump(main);
            floating.Content = a.Workbook; floating.Show(); Pump(floating);
            Check(ReferenceEquals(TopLevel.GetTopLevel(Notice(a.Workbook)), floating) && !Notice(b.Workbook).IsVisible,
                "feedback follows its workbook to another window and does not appear in the other document");
            Report(b.Workbook, "native.finish_edit_link"); Pump(main);
            // RunJobs drains ready jobs but does not drive the platform's timers.
            using (var elapsed = new CancellationTokenSource(TimeSpan.FromSeconds(8.4)))
                Dispatcher.UIThread.MainLoop(elapsed.Token);
            Pump(main); Pump(floating);
            Check(!Notice(b.Workbook).IsVisible && Notice(a.Workbook).IsVisible && Notice(a.Workbook).Classes.Contains("error"),
                "information expires while an unresolved error remains in its own window");
            var dismiss = a.Workbook.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "DismissWorkbookFeedback");
            dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(floating);
            Check(!Notice(a.Workbook).IsVisible && OutputLines.Count == 1, "dismissing feedback leaves the recorded failure available");
            Report(a.Workbook, "native.copy_failed"); Pump(floating);
            Check(OutputLines.Count == 2, "a new failure after dismissal is recorded again");
            Check(await a.SaveAsync(), "saving after feedback succeeds");
            Check(!Notice(a.Workbook).IsVisible, "successful save clears an earlier failure");
            a.Workbook.Session!.SetUserInput(0, 1, 2, "=1/0"); Pump(floating);
            Check(!Notice(a.Workbook).IsVisible && OutputLines.Count == 2, "a formula error value remains in the sheet without an application alert");

            navigate.Invoke(b.Workbook, ["AnotherMissingName"]);
            b.OnClose(); Pump(main);
            Check(!Notice(b.Workbook).IsVisible, "a deferred message cannot return after its document closes");
        }
        finally
        {
            a.OnClose(); b.OnClose(); floating.Close(); main.Close();
        }
    }
}
