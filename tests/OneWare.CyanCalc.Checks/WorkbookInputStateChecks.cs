using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyanCalc;
using CyanCalc.Controls;

internal static partial class Program
{
    static Task CheckWorkbookInputStates()
    {
        using var model = new UserModel();
        model.SetUserInput(0, 1, 1, "10");
        var book = new WorkbookView { Session = model };
        var shellButton = new Button { Content = "Host action", Classes = { "primary" }, IsEnabled = false };
        var layout = new DockPanel();
        DockPanel.SetDock(shellButton, Avalonia.Controls.Dock.Top);
        layout.Children.Add(shellButton); layout.Children.Add(book);
        var main = new Window { Width = 1200, Height = 850, Content = layout, RequestedThemeVariant = ThemeVariant.Dark };
        var floating = new Window { Width = 1000, Height = 800, RequestedThemeVariant = ThemeVariant.Light };
        LinkEditorWindow? link = null;
        main.Show(); Pump(main);
        T Find<T>(Control root, string name) where T : Control =>
            root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
        Button Action(string key) => book.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == book.Text[key]);
        Color ColorOf(IBrush? brush) => (brush as ISolidColorBrush)?.Color
            ?? throw new Exception("Expected a solid state brush, got " + brush);
        Color Token(Control control, string key) => ColorOf(control.FindResource(control.ActualThemeVariant, key) as IBrush);
        Border InputBorder(TextBox field) => field.GetVisualDescendants().OfType<Border>()
            .First(border => border.Name is "border" or "PART_BorderElement");
        ContentPresenter Face(Button button) => button.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(presenter => presenter.Name == "PART_ContentPresenter");
        bool Red(TextBox field) => ColorOf(InputBorder(field).BorderBrush) == Token(field, "ErrorBrush");
        bool Neutral(Button button) => !button.IsEffectivelyEnabled &&
            ColorOf(Face(button).Background) == Token(button, "ThemeControlMidBrush") &&
            ColorOf(Face(button).Foreground) == Token(button, "ThemeForegroundLowBrush");
        void WaitForNameValidation(Window window)
        {
            using var elapsed = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            Dispatcher.UIThread.MainLoop(elapsed.Token);
            Pump(window);
        }
        try
        {
            book.ShowNames(); Pump(main);
            Click(main, Action("name_manager_dialog.new"));
            var name = Find<TextBox>(book, "NamedRangeName");
            var reference = Find<TextBox>(book, "NamedRangeFormula");
            var apply = Find<Button>(book, "NamedRangeApply");
            name.Text = "1Bad"; reference.Text = "Sheet1!$A$1"; WaitForNameValidation(main);
            Check(name.Classes.Contains("invalid") && Red(name) && !reference.Classes.Contains("invalid") && Neutral(apply),
                "defined-name error drives the rendered border and disabled button without marking its valid reference");
            name.Focus(); Pump(main);
            Check(Red(name), "focus does not replace an invalid field's error border");
            Check(ColorOf(shellButton.Background) == Token(shellButton, "ThemeAccentBrush"),
                "workbook disabled styling does not change an ordinary host primary button");
            main.RequestedThemeVariant = ThemeVariant.Light; Pump(main);
            Check(Red(name) && Neutral(apply), "name error and disabled button resolve light theme resources");

            layout.Children.Remove(book); floating.Content = book; floating.Show(); Pump(floating);
            name = Find<TextBox>(book, "NamedRangeName");
            reference = Find<TextBox>(book, "NamedRangeFormula");
            apply = Find<Button>(book, "NamedRangeApply");
            Check(name.Classes.Contains("invalid") && Red(name) && Neutral(apply),
                "existing validation reconnects after the workbook moves to another window");
            name.Text = "SampleRange"; reference.Text = "=("; WaitForNameValidation(floating);
            Check(!name.Classes.Contains("invalid") && reference.Classes.Contains("invalid") && Red(reference) && Neutral(apply),
                "reference validation marks its own field and clears the corrected name");
            reference.Text = "Sheet1!$A$1"; WaitForNameValidation(floating);
            Check(!reference.Classes.Contains("invalid") && !Red(reference) && apply.IsEffectivelyEnabled &&
                  ColorOf(Face(apply).Background) == Token(apply, "ThemeAccentBrush"),
                "valid defined-name input restores its normal border and active accent");
            var oldNameError = Find<TextBlock>(book, "NamedRangeNameError");
            Click(floating, apply);
            Check(model.GetDefinedNameList().Any(item => item.Name == "SampleRange"), "corrected defined name applies normally");
            oldNameError.Text = "Detached validation";
            Check(!name.Classes.Contains("invalid"), "a detached error label no longer updates its former input");

            book.ShowConditionalFormats(); Pump(floating);
            Click(floating, Action("conditional_formatting.new"));
            var type = Find<ComboBox>(book, "ConditionalRuleType");
            type.SelectedItem = "Formula"; Pump(floating);
            var formula = Find<TextBox>(book, "ConditionalValue");
            var conditionalApply = Find<Button>(book, "ConditionalApply");
            formula.Text = "A1>0"; Pump(floating);
            Check(formula.Classes.Contains("invalid") && Red(formula) && Neutral(conditionalApply),
                "conditional formula uses its existing validation label for the error border and disabled action");
            formula.Focus(); floating.RequestedThemeVariant = ThemeVariant.Dark; Pump(floating);
            Check(Red(formula) && Neutral(conditionalApply), "conditional error stays visible while focused after a theme change");
            type.SelectedItem = "Text"; Pump(floating);
            var textValue = Find<TextBox>(book, "ConditionalValue");
            Check(!textValue.Classes.Contains("invalid"), "another rule type does not mistake format labels for validation errors");
            type.SelectedItem = "Formula"; Pump(floating);
            formula = Find<TextBox>(book, "ConditionalValue");
            formula.Text = "=A1>0"; Pump(floating);
            Check(!formula.Classes.Contains("invalid") && !Red(formula) && conditionalApply.IsEffectivelyEnabled,
                "rebuilt conditional formula clears the error after a valid correction");
            Click(floating, conditionalApply);
            Check(model.GetConditionalFormattingList(0).GetArrayLength() == 1, "corrected conditional rule applies normally");

            link = new LinkEditorWindow(["Sheet1"], "Sheet1", null, "Example", book.Text)
                { RequestedThemeVariant = ThemeVariant.Dark };
            _ = link.ShowDialog(floating); Pump(link);
            var url = Find<TextBox>(link, "LinkUrl");
            var save = Find<Button>(link, "LinkSave");
            url.Text = "not a url"; Pump(link);
            Check(url.Classes.Contains("invalid") && Red(url) && Neutral(save),
                "link dialog retains its existing invalid state and renders a neutral disabled Save");
            url.Focus(); link.RequestedThemeVariant = ThemeVariant.Light; Pump(link);
            Check(Red(url) && Neutral(save), "owned link dialog error and disabled states work in light theme while focused");
            url.Text = "https://example.com"; Pump(link);
            Check(!url.Classes.Contains("invalid") && !Red(url) && save.IsEffectivelyEnabled &&
                  ColorOf(Face(save).Background) == Token(save, "ThemeAccentBrush"),
                "valid link restores its border and active Save without changing validation");
            Click(link, save);
            Check(!link.IsVisible, "valid link Save still completes the dialog");
        }
        finally
        {
            link?.Close(); floating.Close(); main.Close(); book.Session = null;
        }
        return Task.CompletedTask;
    }
}
