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
        // Three small horizontal flags: exit-test country, server-country, verdict badge.
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Exit test country flag
        var exitFlag = new Image { Width = 20, Height = 14, Margin = new Thickness(0,0,6,0) };
        exitFlag.SetBinding(Image.SourceProperty, new Binding("ExitCountryCode") { Converter = new CountryFlagConverter() });
        exitFlag.ToolTip = "Exit (measured)";
        panel.Children.Add(exitFlag);

        // Server country flag (the second one that was missing)
        var serverFlag = new Image { Width = 20, Height = 14, Margin = new Thickness(0,0,6,0) };
        serverFlag.SetBinding(Image.SourceProperty, new Binding("ServerCountryCode") { Converter = new CountryFlagConverter() });
        serverFlag.ToolTip = "Server location";
        panel.Children.Add(serverFlag);

        // Reputation verdict flag (green/red/grey badge)
        var badge = new Image { Width = 20, Height = 14 };
        badge.SetBinding(Image.SourceProperty, new Binding("FlagStatus") { Converter = new FlagStatusConverter() });
        badge.SetBinding(FrameworkElement.ToolTipProperty, new Binding("FlagStatusText"));
        panel.Children.Add(badge);

        return panel;
    }
}
