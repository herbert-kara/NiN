using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using v2rayN.Converters;

namespace v2rayN.Base;

// Retains MyDGTextColumn identity for existing sorting and column persistence.
internal class MyDGCountryColumn : MyDGTextColumn
{
    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        // Wrap so the two flag slots keep their 20px width no matter how long the
        // remark text is; a horizontal StackPanel would push the second flag out
        // of the cell instead of trimming the text.
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var exit = new Image { Width = 20, Height = 14, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Exit (measured)" };
        exit.SetBinding(Image.SourceProperty, new Binding("ExitCountryCode") { Converter = new CountryFlagConverter() });
        panel.Children.Add(exit);
        var endpoint = new Image { Width = 20, Height = 14, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Endpoint / CDN (IP estimate)" };
        endpoint.SetBinding(Image.SourceProperty, new Binding("EndpointCountryCode") { Converter = new CountryFlagConverter() });
        panel.Children.Add(endpoint);
        var text = base.GenerateElement(cell, dataItem);
        if (text is TextBlock tb)
        {
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.VerticalAlignment = VerticalAlignment.Center;
        }
        panel.Children.Add(text);
        return panel;
    }
}
