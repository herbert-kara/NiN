namespace ServiceLib.Models.Dto;

[Serializable]
public partial class ProfileItemModel : ReactiveObject
{
    // Reactive: the grid highlights this row, and the highlight has to follow the
    // active connection when it changes, not only when the list is rebuilt.
    [Reactive]
    public partial bool IsActive { get; set; }
    public string IndexId { get; set; }
    public EConfigType ConfigType { get; set; }
    private string _remarks;
    public string Remarks
    {
        get => _remarks;
        set
        {
            this.RaiseAndSetIfChanged(ref _remarks, value);
            this.RaisePropertyChanged(nameof(CountryCode));
        }
    }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Network { get; set; }
    public string StreamSecurity { get; set; }
    public string Subid { get; set; }
    public string SubRemarks { get; set; }
    public int Sort { get; set; }

    [Reactive]
    public partial int Delay { get; set; }

    public decimal Speed { get; set; }

    [Reactive]
    public partial string DelayVal { get; set; }

    [Reactive]
    public partial string SpeedVal { get; set; }

    // In-tunnel quality from the last real-ping run. A config that answers quickly
    // but swings wildly, or drops requests, is worse than a steadier one, and a
    // single delay number cannot show that.
    [Reactive]
    public partial int Jitter { get; set; }

    // The grid binds to the *Text properties, so those must raise change
    // notification themselves; a plain computed getter never re-renders.
    partial void OnJitterChanged(int value)
    {
        this.RaisePropertyChanged(nameof(JitterText));
        this.RaisePropertyChanged(nameof(QualityDetail));
    }

    // Shown as an em dash rather than a blank cell: an empty column reads as a
    // broken feature, while a dash reads as "not measured yet", which is true.
    // Only a real-ping run produces these two; a plain delay test cannot.
    private const string NotMeasured = "\u2014";

    /// <summary>Score as shown in the grid; dash until a quality run happens.</summary>
    public string QualityScoreText => QualityScore > 0 ? QualityScore.ToString() : NotMeasured;

    /// <summary>Jitter as shown in the grid; dash until a quality run happens.</summary>
    public string JitterText => Jitter >= 0 ? Jitter.ToString() : NotMeasured;

    [Reactive]
    public partial int QualityScore { get; set; }

    partial void OnQualityScoreChanged(int value)
    {
        this.RaisePropertyChanged(nameof(QualityScoreText));
        this.RaisePropertyChanged(nameof(QualityDetail));
    }

    /// <summary>Loss as a whole percentage for display; -1 when never measured.</summary>
    public int LossVal { get; set; } = -1;

    /// <summary>"80 ms / ±12 / 0% / 87" style summary for the column tooltip.</summary>
    public string QualityDetail
    {
        get
        {
            if (Delay <= 0) return string.Empty;
            var loss = LossVal < 0 ? "-" : $"{LossVal}%";
            return $"Median {Delay} ms, jitter {Jitter} ms, loss {loss}, score {QualityScore}/100";
        }
    }

    private string _ipInfo;
    public string IpInfo
    {
        get => _ipInfo;
        set
        {
            this.RaiseAndSetIfChanged(ref _ipInfo, value);
            this.RaisePropertyChanged(nameof(ExitCountryCode));
            this.RaisePropertyChanged(nameof(CountryCode));
        }
    }

    [Reactive]
    public partial string TodayUp { get; set; }

    private string? _serverCountryCode;
    public string? ServerCountryCode
    {
        get => _serverCountryCode;
        set
        {
            this.RaiseAndSetIfChanged(ref _serverCountryCode, value);
            this.RaisePropertyChanged(nameof(EndpointCountryCode));
            this.RaisePropertyChanged(nameof(CountryCode));
        }
    }

    // Independent observations: an endpoint/CDN IP estimate does not identify a hidden origin.
    public string? ExitCountryCode => ProfileCountry.Resolve(IpInfo, null);
    public string? EndpointCountryCode => ProfileCountry.Normalize(ServerCountryCode);

    // Anti-fraud / IP-reputation verdict for the config's public IP.
    // Unknown is a real value (not yet checked, or the check could not run) and is
    // rendered as a grey flag rather than being hidden.
    private EFlagStatus _flagStatus = EFlagStatus.Unknown;
    public EFlagStatus FlagStatus
    {
        get => _flagStatus;
        set
        {
            this.RaiseAndSetIfChanged(ref _flagStatus, value);
            this.RaisePropertyChanged(nameof(FlagStatusCode));
        }
    }

    private int _flagRisk;
    public int FlagRisk
    {
        get => _flagRisk;
        set => this.RaiseAndSetIfChanged(ref _flagRisk, value);
    }

    private string? _flagType;
    public string? FlagType
    {
        get => _flagType;
        set => this.RaiseAndSetIfChanged(ref _flagType, value);
    }

    /// <summary>Resource key of the coloured flag that represents <see cref="FlagStatus"/>.</summary>
    public string FlagStatusCode => ProfileCountry.FlagCode(FlagStatus)!;

    /// <summary>Human-readable tooltip for the flagged column: verdict, risk score and detection type.</summary>
    public string FlagStatusText => FlagStatus switch
    {
        EFlagStatus.Clean => $"Clean - not flagged as proxy/VPN/hosting (risk {FlagRisk}/100)",
        EFlagStatus.Flagged => $"FLAGGED - {(FlagType ?? "proxy/VPN/hosting")} (risk {FlagRisk}/100)",
        _ => "Unknown - not checked yet, or the check could not run",
    };

    // Legacy hint fallback only; location flags bind to the independent properties above.
    public string? CountryCode => ExitCountryCode
        ?? EndpointCountryCode
        ?? ProfileCountry.Resolve(null, Remarks);

    [Reactive]
    public partial string TodayDown { get; set; }

    [Reactive]
    public partial string TotalUp { get; set; }

    [Reactive]
    public partial string TotalDown { get; set; }

    public string GetSummary()
    {
        var summary = $"[{ConfigType}] {Remarks}";
        if (!ConfigType.IsComplexType())
        {
            summary += $"({Address}:{Port})";
        }

        return summary;
    }
}
