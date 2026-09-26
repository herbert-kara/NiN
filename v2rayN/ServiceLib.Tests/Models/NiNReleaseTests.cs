namespace ServiceLib.Tests.Models;

public class NiNReleaseTests
{
    private static GitHubRelease Release(string tag, bool draft = false) => new()
    {
        TagName = tag, Draft = draft,
        Assets = [new() { Name = "NiN-windows-64.zip" }, new() { Name = "NiN-windows-64-desktop.zip" }]
    };

    [Test]
    public async Task RejectsUpstreamAndIncompleteReleases()
    {
        var releases = new List<GitHubRelease>
        {
            Release("7.25.1-P99"),
            new() { TagName = "v7.25.1-nin.99", Assets = [] },
            Release("v7.25.1-nin.100", true),
            Release("garbage-nin.999"),
            Release("v7.25.1-nin.3")
        };
        await NiNRelease.SelectTag(releases).Should().BeEqualTo("v7.25.1-nin.3");
    }

    [Test]
    public async Task LegacyTimestampCandidateIsNeverSelected()
    {
        // The old sync format stamped a unix time into the revision, so
        // SemanticVersion read 1789904268 as the revision number and ranked it
        // above every real release. The updater then reinstalled that stale tree
        // and the running app lost the flag columns and the refresh button.
        // It must be ignored no matter how large the timestamp is.
        const string legacy = "v7.25.2-nin.1789904268.ed090c08";
        var releases = new List<GitHubRelease>
        {
            Release(legacy),
            Release("v7.25.2-nin.1"),
            Release("v7.25.1-nin.10")
        };
        await NiNRelease.SelectTag(releases).Should().BeEqualTo("v7.25.2-nin.1");
    }

    [Test]
    public async Task TimestampCandidateWinsEvenWhenItIsTheOnlyRelease()
    {
        await (NiNRelease.SelectTag([Release("v7.25.2-nin.1789904268.ed090c08")]) == null)
            .Should().BeTrue();
    }

    [Test]
    public async Task NewBaseVersionStillOutranksOlderBaseRegardlessOfRevision()
    {
        const string older = "v7.25.1-nin.10";
        await (new SemanticVersion("v7.26.0-nin.1") > new SemanticVersion(older)).Should().BeTrue();
        await NiNRelease.SelectTag([Release(older), Release("v7.26.0-nin.1")])
            .Should().BeEqualTo("v7.26.0-nin.1");
    }

    [Test]
    public async Task RespectsPrereleasePreferenceAndNumericRevisionOrder()
    {
        var preview = Release("v7.25.1-nin.11");
        preview.Prerelease = true;
        var releases = new List<GitHubRelease> { preview, Release("v7.25.1-nin.9"), Release("v7.25.1-nin.10") };
        await NiNRelease.SelectTag(releases).Should().BeEqualTo("v7.25.1-nin.10");
        await NiNRelease.SelectTag(releases, true).Should().BeEqualTo("v7.25.1-nin.11");
    }

    [Test]
    public async Task EmptyOrUnrelatedReleasesProduceNoUpdate()
    {
        await (NiNRelease.SelectTag(null) == null).Should().BeTrue();
        await (NiNRelease.SelectTag([Release("7.25.1-P26")]) == null).Should().BeTrue();
    }
}
