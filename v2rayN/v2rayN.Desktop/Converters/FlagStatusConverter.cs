using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using ServiceLib.Common;
using ServiceLib.Services;

namespace v2rayN.Desktop.Converters;

/// <summary>Renders the anti-fraud verdict as a coloured flag: green clean, red flagged, grey unknown.</summary>
public sealed class FlagStatusConverter : IValueConverter
{
    private static readonly Dictionary<EFlagStatus, Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is EFlagStatus s ? s : EFlagStatus.Unknown;
        if (Cache.TryGetValue(status, out var cached)) return cached;
        using var stream = ProfileCountry.OpenStatusFlag(status);
        if (stream == null) return null;
        var image = new Bitmap(stream);
        Cache[status] = image;
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
