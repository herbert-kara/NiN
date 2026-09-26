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
        // A Grid with fixed flag columns, not a StackPanel/WrapPanel: both of those
        // let the remark text claim the whole cell width and squeeze the second
        // flag out of view. Here the text goes in its own star column and the two
        // flag slots keep their 20px no matter how long the text is.
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); // exit flag
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); // server flag
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text

        var exit = new Image { Width = 20, Height = 14, ToolTip = "Exit (measured)" };
        exit.SetBinding(Image.SourceProperty, new Binding("ExitCountryCode") { Converter = new CountryFlagConverter() });
        Grid.SetColumn(exit, 0);
        grid.Children.Add(exit);

        var endpoint = new Image { Width = 20, Height = 14, ToolTip = "Endpoint / CDN (IP estimate)" };
        endpoint.SetBinding(Image.SourceProperty, new Binding("EndpointCountryCode") { Converter = new CountryFlagConverter() });
        Grid.SetColumn(endpoint, 1);
        grid.Children.Add(endpoint);

        var text = base.GenerateElement(cell, dataItem);
        if (text is TextBlock tb)
        {
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        return grid;
    }
}
