namespace ServiceLib.Models.Entities;

[Serializable]
public class ProfileExItem
{
    [PrimaryKey]
    public string IndexId { get; set; }

    public int Delay { get; set; }
    public decimal Speed { get; set; }
    public int Sort { get; set; }
    public string? Message { get; set; }
    public string? IpInfo { get; set; }

    /// <summary>In-tunnel spread of the last real-ping run, in ms. -1 when unknown.</summary>
    public int Jitter { get; set; } = -1;

    /// <summary>Fraction of real-ping attempts that failed, 0..1.</summary>
    public double PacketLoss { get; set; }

    /// <summary>Composite 0..100 quality from latency, jitter and loss.</summary>
    public int QualityScore { get; set; }
}
