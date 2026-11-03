using Avalonia.Controls;

namespace CyanCalc.Controls;

// Compiled only into the plugin UI assembly; the standalone UI is unchanged.
public sealed partial class WorkbookView
{
    public static string? GetFeedbackResourceKey(LocalizedText message) => message.ResourceKey;

    public void AddHostFeedback(Control feedback)
    {
        var root = (Grid)Content!;
        Grid.SetRow(feedback, root.RowDefinitions.Count);
        Grid.SetColumnSpan(feedback, root.ColumnDefinitions.Count);
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.Children.Add(feedback);
    }
}
