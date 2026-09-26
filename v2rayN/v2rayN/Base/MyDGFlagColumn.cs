using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using v2rayN.Converters;

namespace v2rayN.Base;

// A standalone column so sorting and column-width persistence stay independent
// of the country column; the verdict is a single coloured flag plus a tooltip.
internal class MyDGFlagColumn : MyDGTextColumn
{
    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        // Only the coloured flag is drawn: the base text column would render the raw
        // enum name, which is noise next to the flag itself.
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var image = new Image { Width = 20, Height = 14 };
        // Verdict, risk score and detection type for this config.
        image.SetBinding(FrameworkElement.ToolTipProperty, new Binding("FlagStatusText"));
        image.SetBinding(Image.SourceProperty, new Binding("FlagStatus") { Converter = new FlagStatusConverter() });
        panel.Children.Add(image);
        return panel;
    }
}
