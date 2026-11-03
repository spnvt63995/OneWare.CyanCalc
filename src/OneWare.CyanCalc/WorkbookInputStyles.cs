using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OneWare.CyanCalc;

public enum WorkbookErrorSource { None, Name, Reference, ConditionalFormula }

// Adapts existing workbook validation output to host visuals; it does not validate input.
public sealed class WorkbookInputStyles : AvaloniaObject
{
    private static readonly Uri StylesUri = new("avares://OneWare.CyanCalc/Resources/WorkbookInputStyles.axaml");
    private static readonly ConditionalWeakTable<TextBox, FieldSubscription> Fields = new();
    public static readonly AttachedProperty<WorkbookErrorSource> ErrorSourceProperty =
        AvaloniaProperty.RegisterAttached<WorkbookInputStyles, TextBox, WorkbookErrorSource>("ErrorSource");

    static WorkbookInputStyles()
    {
        ErrorSourceProperty.Changed.AddClassHandler<TextBox>((field, _) =>
        {
            var source = GetErrorSource(field);
            if (source == WorkbookErrorSource.None)
            {
                if (Fields.TryGetValue(field, out var previous)) previous.Dispose();
                Fields.Remove(field);
            }
            else Fields.GetValue(field, input => new FieldSubscription(input)).SetSource(source);
        });
    }

    public static WorkbookErrorSource GetErrorSource(TextBox field) => field.GetValue(ErrorSourceProperty);
    public static void SetErrorSource(TextBox field, WorkbookErrorSource value) => field.SetValue(ErrorSourceProperty, value);

    public static void Register(Application application)
    {
        if (!application.Styles.OfType<StyleInclude>().Any(style => style.Source == StylesUri))
            application.Styles.Add(new StyleInclude(StylesUri) { Source = StylesUri });
    }

    private sealed class FieldSubscription : IDisposable
    {
        private readonly TextBox _field;
        private WorkbookErrorSource _source;
        private TextBlock? _error;
        private int _generation;

        internal FieldSubscription(TextBox field)
        {
            _field = field;
            field.AttachedToVisualTree += Attached;
            field.DetachedFromVisualTree += Detached;
        }

        internal void SetSource(WorkbookErrorSource source)
        {
            _source = source;
            QueueConnect();
        }

        private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => QueueConnect();
        private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Disconnect();

        private void QueueConnect()
        {
            Disconnect();
            if (!_field.IsAttachedToVisualTree()) return;
            var generation = _generation;
            // Drawer fields can attach before the following error label is constructed.
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation || !_field.IsAttachedToVisualTree()) return;
                var drawer = _field.GetVisualAncestors().OfType<Border>()
                    .FirstOrDefault(control => control.Classes.Contains("workbook-drawer"));
                if (drawer is null) return;
                if (_source == WorkbookErrorSource.ConditionalFormula)
                {
                    // The shared Formula editor places its validation label directly
                    // after ConditionalValue. Other rule types have no such sibling.
                    if (_field.Parent is Panel parent)
                        _error = parent.Children.ElementAtOrDefault(parent.Children.IndexOf(_field) + 1) as TextBlock;
                }
                else
                {
                    var name = _source == WorkbookErrorSource.Name ? "NamedRangeNameError" : "NamedRangeFormulaError";
                    _error = drawer.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(control => control.Name == name);
                }
                if (_error is not null) _error.PropertyChanged += ErrorChanged;
                Update();
            }, DispatcherPriority.Loaded);
        }

        private void ErrorChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == TextBlock.TextProperty || args.Property == Visual.IsVisibleProperty) Update();
        }

        private void Update() => _field.Classes.Set("invalid",
            _error is { IsVisible: true } && !string.IsNullOrWhiteSpace(_error.Text));

        private void Disconnect()
        {
            _generation++;
            if (_error is not null) _error.PropertyChanged -= ErrorChanged;
            _error = null;
            _field.Classes.Remove("invalid");
        }

        public void Dispose()
        {
            Disconnect();
            _field.AttachedToVisualTree -= Attached;
            _field.DetachedFromVisualTree -= Detached;
        }
    }
}
