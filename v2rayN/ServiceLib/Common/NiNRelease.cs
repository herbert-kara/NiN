namespace ServiceLib.Common;

public static class NiNRelease
{
    // A published NiN release is exactly v<major>.<minor>.<patch>-nin.<revision>.
    //
    // The historical sync candidate format appended a unix timestamp and a commit
    // sha (v7.25.2-nin.1789904268.ed090c08). SemanticVersion compares that 1789904268
    // numerically, so it outranked every real revision and the updater kept
    // reinstalling that old tree — the running app silently lost the flag columns,
    // the refresh button and every later fix. Only a plain small-integer revision
    // is a real release now; anything else is a leftover candidate and is ignored.
    private static readonly System.Text.RegularExpressions.Regex ReleaseTag =
        new(@"^v(?<base>\d+\.\d+\.\d+)-nin\.(?<rev>\d{1,6})$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    public static string? SelectTag(List<GitHubRelease>? releases, bool preRelease = false) => releases?
        .Where(r => !r.Draft
                 && (preRelease || !r.Prerelease)
                 && r.TagName != null
                 && ReleaseTag.IsMatch(r.TagName)
                 && r.Assets?.Any(a => a.Name == "NiN-windows-64.zip") == true
                 && r.Assets?.Any(a => a.Name == "NiN-windows-64-desktop.zip") == true)
        .Where(r => new SemanticVersion(r.TagName).ToStandardVersionString("v") == r.TagName)
        .OrderByDescending(r => new SemanticVersion(r.TagName))
        .Select(r => r.TagName).FirstOrDefault();
}
