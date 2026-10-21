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
    /// Feeds one probe result into the watchdog.
    /// </summary>
    /// <param name="tunnelOk">Whether the tunnel answered.</param>
    /// <returns>True when the config should be reloaded now.</returns>
    public bool Tick(bool tunnelOk)
    {
        if (tunnelOk)
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
