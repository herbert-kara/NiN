using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ServiceLib.Common;

/// <summary>Local country hints only; never geolocates a configuration via a third party.</summary>
public static class ProfileCountry
{
    private static readonly HashSet<string> Codes = typeof(ProfileCountry).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("ServiceLib.Resources.Flags.", StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
        .Select(n => n.Split('.')[3].ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Names = BuildNames();

    private static Dictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!Codes.Contains(region.TwoLetterISORegionName)) continue;
                names.TryAdd(region.EnglishName, region.TwoLetterISORegionName);
                names.TryAdd(region.NativeName, region.TwoLetterISORegionName);
            }
            catch (ArgumentException) { }
        }
        foreach (var (name, code) in new[] { ("USA", "US"), ("UK", "GB"), ("ایران", "IR"), ("آلمان", "DE"), ("آمریکا", "US"), ("انگلیس", "GB"), ("هلند", "NL"), ("فرانسه", "FR"), ("ترکیه", "TR"), ("روسیه", "RU"), ("کانادا", "CA"), ("فنلاند", "FI"), ("سوئد", "SE"), ("ژاپن", "JP"), ("سنگاپور", "SG") })
            names[name] = code;
        return names;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var code = value.ToUpperInvariant();
        if (code == "UK") code = "GB";
        return Codes.Contains(code) ? code : Names.GetValueOrDefault(value);
    }

    public static string? Resolve(string? ipInfo, string? remarks)
    {
        if (!string.IsNullOrWhiteSpace(ipInfo))
        {
            var match = Regex.Match(ipInfo, @"\(([^()]{1,80})\)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var tested = match.Success ? Normalize(match.Groups[1].Value) : null;
            if (tested != null) return tested;
        }
        if (string.IsNullOrWhiteSpace(remarks)) return null;
        var runes = remarks.EnumerateRunes().ToArray();
        for (var i = 0; i + 1 < runes.Length; i++)
        {
            if (runes[i].Value is >= 0x1F1E6 and <= 0x1F1FF && runes[i + 1].Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                var code = string.Concat((char)('A' + runes[i].Value - 0x1F1E6), (char)('A' + runes[i + 1].Value - 0x1F1E6));
                if (Codes.Contains(code)) return code;
            }
        }
        // Prefer longest full country names (e.g. South Korea before Korea).
        foreach (var name in Names.Keys.OrderByDescending(n => n.Length))
        {
            if (Regex.IsMatch(remarks, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                return Names[name];
        }
        // Codes in labels must be upper case to avoid words such as 'in'/'at'.
        foreach (Match match in Regex.Matches(remarks, @"(?<![\p{L}\p{N}])[A-Z]{2}(?![\p{L}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var code = Normalize(match.Value);
            if (code != null) return code;
        }
        return null;
    }

    public static Stream? OpenFlag(string? code)
    {
        code = Normalize(code);
        return code == null ? null : typeof(ProfileCountry).Assembly.GetManifestResourceStream($"ServiceLib.Resources.Flags.{code.ToLowerInvariant()}.png");
    }

    // Reputation verdict flags. These are NiN's own artwork, not ISO country codes, so
    // they bypass Normalize (which would reject the "nin_flag_*" names) and are opened
    // by exact resource name.
    public const string CleanFlagCode = "nin_flag_clean";
    public const string FlaggedFlagCode = "nin_flag_flagged";
    public const string UnknownFlagCode = "nin_flag_unknown";

    public static string? FlagCode(EFlagStatus status) => status switch
    {
        EFlagStatus.Clean => CleanFlagCode,
        EFlagStatus.Flagged => FlaggedFlagCode,
        _ => UnknownFlagCode,
    };

    public static Stream? OpenStatusFlag(EFlagStatus status)
        => typeof(ProfileCountry).Assembly.GetManifestResourceStream($"ServiceLib.Resources.Flags.{FlagCode(status)}.png");
}
