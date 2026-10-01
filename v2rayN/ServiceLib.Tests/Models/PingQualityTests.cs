namespace ServiceLib.Tests.Models;

public class PingQualityTests
{
    [Test]
    public async Task FromSamples_EmptyMeansNoResult()
    {
        var q = PingQuality.FromSamples([]);
        await q.Median.Should().BeEqualTo(-1);
        await q.Score.Should().BeEqualTo(0);
        await q.Loss.Should().BeEqualTo(1.0);
    }

    [Test]
    public async Task FromSamples_AllFailedMeansNoResult()
    {
        var q = PingQuality.FromSamples([-1, -1, -1, -1, -1]);
        await q.Median.Should().BeEqualTo(-1);
        await q.Score.Should().BeEqualTo(0);
        await q.Loss.Should().BeEqualTo(1.0);
    }

    [Test]
    public async Task FromSamples_IdenticalTimesHaveNoJitterAndNoLoss()
    {
        var q = PingQuality.FromSamples([120, 120, 120, 120, 120]);
        await q.Median.Should().BeEqualTo(120);
        await q.Jitter.Should().BeEqualTo(0);
        await q.Loss.Should().BeEqualTo(0.0);
        // Steady, but 120 ms is not at the top of the latency curve.
        await q.Score.Should().BeGreaterThan(80);
        await q.Score.Should().BeLessThan(100);
    }

    [Test]
    public async Task FromSamples_ImpossiblyFastAndSteadyScoresFull()
    {
        await PingQuality.FromSamples([10, 10, 10, 10, 10]).Score.Should().BeEqualTo(100);
    }

    [Test]
    public async Task FromSamples_MedianOfOddAndEvenCounts()
    {
        await PingQuality.FromSamples([10, 20, 30, 40, 50]).Median.Should().BeEqualTo(30);
        await PingQuality.FromSamples([10, 20, 30, 40]).Median.Should().BeEqualTo(25);
        await PingQuality.FromSamples([50]).Median.Should().BeEqualTo(50);
    }

    [Test]
    public async Task FromSamples_LossCountsFailedAttempts()
    {
        var q = PingQuality.FromSamples([100, -1, 100, 100, -1]);
        await q.Loss.Should().BeEqualTo(0.4);
        await q.Median.Should().BeEqualTo(100);
    }

    [Test]
    public async Task StableLinkOutranksErraticOneAtTheSameMedian()
    {
        // The whole point of the feature: a fast but swinging link is worse.
        var steady = PingQuality.FromSamples([80, 81, 80, 79, 80]);
        var erratic = PingQuality.FromSamples([20, 300, 25, 40, 90]);
        await steady.Jitter.Should().BeLessThan(erratic.Jitter);
        await (steady.Score > erratic.Score).Should().BeTrue();
    }

    [Test]
    public async Task LossIsPunishedHarderThanLatency()
    {
        // Same latency, but one drops a fifth of its requests.
        var clean = PingQuality.FromSamples([150, 150, 150, 150, 150]);
        var lossy = PingQuality.FromSamples([150, 150, 150, 150, -1]);
        await (lossy.Score < clean.Score).Should().BeTrue();
        await (clean.Score - lossy.Score).Should().BeGreaterThan(5);
    }

    [Test]
    public async Task FasterBeatsSlowerAtEqualStability()
    {
        var fast = PingQuality.FromSamples([60, 62, 60, 61, 60]);
        var slow = PingQuality.FromSamples([300, 302, 300, 301, 300]);
        await (fast.Score > slow.Score).Should().BeTrue();
    }

    [Test]
    public async Task ScoreAlwaysStaysInRange()
    {
        var samples = new List<int> { -1, -1, 1, 9999, 500 };
        for (var i = 0; i < 200; i++)
        {
            samples = new List<int>();
            for (var j = 0; j <= i % 7; j++) samples.Add(j % 3 == 0 ? 0 : (j * 37) % 5000);
            var q = PingQuality.FromSamples(samples);
            await q.Score.Should().BeInRange(0, 100);
            await q.Loss.Should().BeInRange(0.0, 1.0);
            await q.Jitter.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public async Task JitterIsNotInflatedByASingleOutlier()
    {
        // Interquartile spread, so one bad sample moves the number far less than the
        // full min/max range would.
        var withOutlier = PingQuality.FromSamples([100, 101, 99, 102, 900]);
        var rangeBased = 900 - 99;
        await withOutlier.Jitter.Should().BeLessThan(rangeBased / 2);
        // The median must ignore the outlier entirely.
        await withOutlier.Median.Should().BeEqualTo(101);
    }

    [Test]
    public async Task SingleSampleIsUsableAndHasZeroJitter()
    {
        var q = PingQuality.FromSamples([123]);
        await q.Median.Should().BeEqualTo(123);
        await q.Jitter.Should().BeEqualTo(0);
        await q.Loss.Should().BeEqualTo(0.0);
        await q.Score.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task VerySlowAndVeryJitteryLinksScoreNearZero()
    {
        var awful = PingQuality.FromSamples([-1, 3000, -1, 4000, 5000]);
        await awful.Score.Should().BeLessThan(10);
    }
}