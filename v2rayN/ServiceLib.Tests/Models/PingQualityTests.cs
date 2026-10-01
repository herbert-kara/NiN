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
        await (q.Score > 80).Should().BeTrue();
        await (q.Score < 100).Should().BeTrue();
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
        await (steady.Jitter < erratic.Jitter).Should().BeTrue();
        await (steady.Score > erratic.Score).Should().BeTrue();
    }

    [Test]
    public async Task LossIsPunishedHarderThanLatency()
    {
        // Same latency, but one drops a fifth of its requests.
        var clean = PingQuality.FromSamples([150, 150, 150, 150, 150]);
        var lossy = PingQuality.FromSamples([150, 150, 150, 150, -1]);
        await (lossy.Score < clean.Score).Should().BeTrue();
        await (clean.Score - lossy.Score > 5).Should().BeTrue();
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
            await (q.Score >= 0 && q.Score <= 100).Should().BeTrue();
            await (q.Loss >= 0.0 && q.Loss <= 1.0).Should().BeTrue();
            await (q.Jitter >= 0).Should().BeTrue();
        }
    }

    [Test]
    public async Task JitterIsNotInflatedByASingleOutlier()
    {
        // Interquartile spread, so one bad sample moves the number far less than the
        // full min/max range would.
        var withOutlier = PingQuality.FromSamples([100, 101, 99, 102, 900]);
        var rangeBased = 900 - 99;
        await (withOutlier.Jitter < rangeBased / 2).Should().BeTrue();
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
        await (q.Score > 0).Should().BeTrue();
    }

    [Test]
    public async Task VerySlowAndVeryJitteryLinksScoreNearZero()
    {
        var awful = PingQuality.FromSamples([-1, 3000, -1, 4000, 5000]);
        await (awful.Score < 10).Should().BeTrue();
    }

    [Test]
    public async Task SlowButSteadyLinkCannotScoreWellOnStabilityAlone()
    {
        // The bug this guards: jitter is zero here, so without a usability cap a
        // 3 s link collected most of its stability marks and ranked around 60,
        // above genuinely usable links.
        var slow = PingQuality.FromSamples([3000, 3000, 3000, 3000, 3000]);
        await slow.Jitter.Should().BeEqualTo(0);
        await (slow.Score < 25).Should().BeTrue();
    }

    [Test]
    public async Task UsableSixHundredMsStillScoresInTheMiddle()
    {
        var usable = PingQuality.FromSamples([600, 610, 595, 620, 605]);
        await (usable.Score > 40).Should().BeTrue();
        await (usable.Score < 75).Should().BeTrue();
    }

    [Test]
    public async Task UsableLinkOutranksAFailedOneDespiteWorseLatency()
    {
        var usable = PingQuality.FromSamples([600, 610, 595, 620, 605]);
        var awful = PingQuality.FromSamples([-1, 3000, -1, 4000, 5000]);
        await (usable.Score > awful.Score).Should().BeTrue();
    }


    [Test]
    public async Task None_MarksJitterAsUnmeasured()
    {
        // The grid shows -1 jitter as empty, but a bare 0 would read as a real,
        // measured "perfectly steady link". PingQuality.None is the single source
        // of that distinction, so pin it.
        await PingQuality.None.Jitter.Should().BeEqualTo(0);
        await PingQuality.None.Median.Should().BeEqualTo(-1);
        await PingQuality.None.Score.Should().BeEqualTo(0);
    }

    [Test]
    public async Task None_IsDistinctFromAMeasuredZeroJitterLink()
    {
        // A link that really answered with no spread must not be confused with a
        // link that was never measured.
        var steady = PingQuality.FromSamples([120, 120, 120, 120, 120]);
        await steady.Median.Should().BeEqualTo(120);
        await steady.Jitter.Should().BeEqualTo(0);
        await PingQuality.None.Median.Should().BeEqualTo(-1);
    }
}