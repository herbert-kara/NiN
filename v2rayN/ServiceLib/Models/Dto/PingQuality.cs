namespace ServiceLib.Models.Dto;

/// <summary>
/// Quality of one real-ping run: several in-tunnel samples reduced to numbers a
/// person can rank configs by.
/// </summary>
/// <remarks>
/// A single response time hides how a config actually behaves. A config that
/// answers 80 ms every time is better than one that averages 80 ms but swings
/// between 20 and 300, and a config that fails one request in five is not
/// "fast" at all. These four values are what the samples reduce to:
/// median (typical), jitter (stability), loss (reliability) and score (ranking).
/// </remarks>
/// <param name="Median">Middle sample time in ms; the representative latency.</param>
/// <param name="Jitter">Spread of the successful samples: p75 - p25, in ms.</param>
/// <param name="Loss">Fraction of samples that failed, 0..1.</param>
/// <param name="Score">0..100 composite; higher is better.</param>
public readonly record struct PingQuality(int Median, int Jitter, double Loss, int Score)
{
    /// <summary>Nothing to judge: no successful sample came back.</summary>
    public static readonly PingQuality None = new(-1, 0, 1, 0);

    /// <summary>
    /// Reduces per-sample times to the four reported values.
    /// </summary>
    /// <param name="samples">Elapsed ms per attempt; 0 or less means it failed.</param>
    public static PingQuality FromSamples(IReadOnlyList<int> samples)
    {
        var total = samples?.Count ?? 0;
        if (total == 0) return None;

        var ok = samples.Where(x => x > 0).OrderBy(x => x).ToList();
        var lost = total - ok.Count;
        if (ok.Count == 0) return new PingQuality(-1, 0, (double)lost / total, 0);

        // Median of the successful samples.
        var median = ok.Count % 2 == 1
            ? ok[ok.Count / 2]
            : (int)Math.Round((ok[(ok.Count / 2) - 1] + ok[ok.Count / 2]) / 2.0);

        // Interquartile spread, so one outlier cannot inflate the number.
        var jitter = Percentile(ok, 0.75) - Percentile(ok, 0.25);
        var loss = (double)lost / total;
        var score = ScoreOf(median, jitter, loss);
        return new PingQuality(median, jitter, loss, score);
    }

    /// <summary>
    /// Blends latency, stability and reliability into one rankable number.
    /// </summary>
    /// <remarks>
    /// Latency is scored on a 40..400 ms curve, saturating at zero past the top of
    /// that range so a very slow link is bad without dominating the scale. Jitter
    /// is penalised relative to latency, because 100 ms of jitter on a 120 ms base
    /// is far worse than the same jitter on 800 ms. Loss is the harshest term: a
    /// link dropping half its requests is unusable no matter how fast it looks.
    /// </remarks>
    private static int ScoreOf(int median, int jitter, double loss)
    {
        var latencyPart = 1.0 - Clamp((median - 40) / 360.0);
        var jitterPart = 1.0 - Clamp((double)jitter / Math.Max(60, median * 0.5));
        var lossPart = 1.0 - Clamp(loss / 0.5);

        var score = (latencyPart * 0.4) + (jitterPart * 0.3) + (lossPart * 0.3);
        return (int)Math.Round(Clamp(score) * 100);
    }

    private static int Percentile(IReadOnlyList<int> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Round((sorted.Count - 1) * p, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
}