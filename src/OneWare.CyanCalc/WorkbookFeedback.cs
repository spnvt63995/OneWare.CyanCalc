using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyanCalc.Controls;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Services;

namespace OneWare.CyanCalc;

/// <summary>One replaceable notice owned by a workbook, including while it is detached.</summary>
internal sealed class WorkbookFeedback
{
    private readonly WorkbookView _workbook;
    private readonly Func<string> _name;
    private readonly ILogger _logger;
    private readonly TextBlock _title = new() { Classes = { "semibold" } };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Name = "WorkbookFeedbackMessage" };
    private readonly Border _notice = new() { Name = "WorkbookFeedback", Classes = { "callout" }, IsVisible = false };
    private readonly DispatcherTimer _expiry = new() { Interval = TimeSpan.FromSeconds(8) };
    private IDisposable? _translation;
    private long _generation;
    private string? _lastFailure;
    private bool _isFailure;

    public WorkbookFeedback(WorkbookView workbook, Func<string> name, ILogger logger)
    {
        _workbook = workbook; _name = name; _logger = logger;
        var dismiss = new Button { Content = "Dismiss", Name = "DismissWorkbookFeedback", Classes = { "small" },
            VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(dismiss, "Dismiss workbook message");
        dismiss.Click += (_, _) =>
        {
            var restoreFocus = dismiss.IsKeyboardFocusWithin;
            Clear();
            if (restoreFocus) workbook.FocusCell();
        };
        var content = new StackPanel { Children = { _title, _message } };
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        grid.Children.Add(new ScrollViewer { Content = content, MaxHeight = 120,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(dismiss, 1); grid.Children.Add(dismiss);
        _notice.Child = grid;
        workbook.AddHostFeedback(_notice);
        _expiry.Tick += (_, _) =>
        {
            // Keep a notice readable while the keyboard is interacting with it.
            if (!_notice.IsKeyboardFocusWithin) Clear();
        };
        workbook.Error += (_, message) =>
        {
            // Report(LocalizedText) emits Error first and then LocalizedError.
            // Defer the literal fallback so one report produces one notice/log.
            var generation = ++_generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == _generation)
                    Show("Command not applied", message, "warning");
            });
        };
        workbook.LocalizedError += (_, message) =>
        {
            ++_generation;
            var key = WorkbookView.GetFeedbackResourceKey(message);
            var failure = key is "native.copy_failed" or "native.link_needs_window";
            var information = key is "native.read_only" or "native.finish_edit_link" or "native.hyperlink_formula";
            Show(failure ? "Action failed" : information ? "Workbook information" : "Check your input",
                message.Value, failure ? "error" : information ? null : "warning", message);
            if (failure) LogFailure(_name() + ": " + message.Value);
        };
        workbook.DocumentChanged += (_, _) => { if (!_isFailure) Clear(); };
    }

    private void Show(string title, string message, string? status, LocalizedText? translated = null)
    {
        _expiry.Stop();
        _translation?.Dispose();
        _translation = null;
        _title.Text = _name() + " — " + title;
        _message.Text = message;
        if (translated is not null) _translation = translated.BindTo(_message, TextBlock.TextProperty);
        _notice.Classes.Set("warning", status == "warning");
        _notice.Classes.Set("error", status == "error");
        _isFailure = status == "error";
        if (!_isFailure) _lastFailure = null;
        _notice.IsVisible = true;
        if (!_isFailure) _expiry.Start();
    }

    public void FileFailure(string operation, string path, Exception error)
    {
        ++_generation;
        Show(operation + " failed", "See Output for details.", "error");
        LogFailure(Path.GetFileName(path) + " — " + operation + ": " + error.Message, error);
    }

    private void LogFailure(string message, Exception? error = null)
    {
        if (_lastFailure == message) return;
        _lastFailure = message;
        _logger.Error("CyanCalc — " + message, error);
    }

    public void Clear()
    {
        ++_generation;
        _expiry.Stop();
        _translation?.Dispose(); _translation = null;
        _notice.IsVisible = false;
        _title.Text = _message.Text = null;
        _lastFailure = null;
        _isFailure = false;
    }
}
