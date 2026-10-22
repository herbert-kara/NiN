namespace ServiceLib.Services;

/// <summary>
/// Decides when a stalled tunnel should be recovered by reloading the config.
/// </summary>
/// <remarks>
/// The rule that matters: a tunnel is only broken if it USED to work. A server that
/// is down, or an app that was never connected, cannot be fixed by reloading the
/// config -- that would only restart the core over and over. So the watchdog stays
/// disarmed until the tunnel answers once, and only then does a run of failures
/// mean the tunnel wedged rather than the server being unreachable.
///
/// Traffic flowing is proof of life that outranks the probe. A download that
/// saturates the link makes the probe slow enough to time out, and a timeout used
/// to be read as a stall: the config was reloaded in the middle of the download.
/// </remarks>
public class TrafficWatchdog
{
    private readonly int _failuresBeforeReload;
    private int _consecutiveFailures;
    private bool _armed;
    private bool _reloadRequested;

    /// <param name="failuresBeforeReload">
    /// How many consecutive failed probes count as a wedged tunnel.
    /// </param>
    public TrafficWatchdog(int failuresBeforeReload = 3)
    {
        _failuresBeforeReload = Math.Max(1, failuresBeforeReload);
    }

    /// <summary>
    /// Feeds one sample into the watchdog.
    /// </summary>
    /// <param name="tunnelOk">Whether the probe through the tunnel answered.</param>
    /// <param name="trafficFlowing">
    /// Whether bytes are moving on the wire. True means the tunnel is carrying
    /// traffic right now, which is stronger evidence than a probe that timed out
    /// because the link was saturated.
    /// </param>
    /// <returns>True when the config should be reloaded now.</returns>
    public bool Tick(bool tunnelOk, bool trafficFlowing)
    {
        // A busy link is a working link. The probe is a small request, but on a
        // saturated connection even that can take longer than its timeout, and
        // reading that as a stall reloads the config mid-download.
        if (tunnelOk || trafficFlowing)
        {
            _consecutiveFailures = 0;
            _armed = true;
            _reloadRequested = false;
            return false;
        }

        // Never worked: the server is down or the app was never connected. Reloading
        // the config cannot fix that, so stay out of the way.
        if (!_armed)
        {
            return false;
        }

        // A reload is already in flight; the core is restarting and will fail until
        // it is back. Do not queue another one on top of it.
        if (_reloadRequested)
        {
            return false;
        }

        _consecutiveFailures++;
        if (_consecutiveFailures < _failuresBeforeReload)
        {
            return false;
        }

        _reloadRequested = true;
        return true;
    }
}
