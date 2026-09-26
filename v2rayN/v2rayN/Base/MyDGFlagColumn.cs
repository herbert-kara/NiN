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
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var image = new Image { Width = 20, Height = 14, ToolTip = "Flagged check (anti-fraud)" };
        image.SetBinding(Image.SourceProperty, new Binding("FlagStatus") { Converter = new FlagStatusConverter() });
        panel.Children.Add(image);
        panel.Children.Add(base.GenerateElement(cell, dataItem));
        return panel;
    }
}
