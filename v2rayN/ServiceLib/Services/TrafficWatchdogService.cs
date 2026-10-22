using ServiceLib.ViewModels;

namespace ServiceLib.Services;

/// <summary>
/// Watches the tunnel and asks for a config reload when it stops answering.
/// </summary>
/// <remarks>
/// The probe is a real request through the local proxy -- the same thing the user
/// does by hand when the connection hangs ("I run a real delay and it works"). It
/// is deliberately not the core's traffic counters: those only exist when the
/// statistics display is switched on, and they cannot tell "idle" apart from
/// "wedged" anyway.
///
/// The reload is requested through the same path as the toolbar's Reload button
/// (StatusBarViewModel.ReloadRequested), so a wedged tunnel is recovered exactly
/// the way a manual reload would recover it.
/// </remarks>
public class TrafficWatchdogService
{
    private readonly TrafficWatchdog _watchdog;
    private CancellationTokenSource? _cts;

    public TrafficWatchdogService(int failuresBeforeReload = 3)
    {
        _watchdog = new TrafficWatchdog(failuresBeforeReload);

        // The probe must not outlive the app: it would keep a proxy request running
        // while everything else is shutting down. The subscription lives as long as
        // the service, which is as long as the app, so it needs no disposal.
        AppEvents.AppExitRequested
            .AsObservable()
            .Subscribe(_ => Stop());
    }

    /// <summary>
    /// How long to wait between probes. Short enough to notice a hang quickly, long
    /// enough not to hammer the server.
    /// </summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Traffic above this in one interval means the link is busy, so the tunnel is
    /// carrying data and there is nothing to fix. A wedged tunnel moves no bytes at
    /// all, so the threshold only has to clear background chatter: 8 KB over 5
    /// seconds is 13 kbit/s, far below any real download and far above idle.
    /// </summary>
    private const long MinBytesPerTick = 8 * 1024;

    /// <summary>
    /// How long the liveness probe may take. Longer than the 5 s latency timeout:
    /// a saturated link slows a small request down but does not break it, and the
    /// point here is to tell "slow" from "dead", not to measure speed.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => RunAsync(cts.Token));
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("TrafficWatchdog", ex);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(ProbeInterval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            try
            {
                await TickAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("TrafficWatchdog", ex);
            }
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        // Only meaningful while a core is actually running: with no core there is
        // nothing to reload.
        if (!AppManager.Instance.IsRunningCore(ECoreType.Xray)
            && !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            return;
        }

        // Bytes moving on the wire are proof the tunnel works, and they outrank the
        // probe: a download that saturates the link makes a small request time out,
        // and that timeout used to be read as a stall -- so the config was reloaded
        // in the middle of the download. When traffic flows there is nothing to
        // probe and nothing to fix.
        if (BytesMovedSinceLastTick() >= MinBytesPerTick)
        {
            _watchdog.Tick(tunnelOk: true, trafficFlowing: true);
            return;
        }

        var tunnelOk = await ProbeAsync(token);
        if (!_watchdog.Tick(tunnelOk, trafficFlowing: false))
        {
            return;
        }

        Logging.SaveLog($"TrafficWatchdog: {ResUI.TipTunnelStalled}");
        NoticeManager.Instance.SendMessageEx(ResUI.TipTunnelStalled);

        // The same path the toolbar's Reload button uses.
        StatusBarViewModel.Instance.ReloadRequested.Publish();
    }

    /// <summary>
    /// Bytes received on every non-loopback interface since the previous call, so a
    /// saturated download reads as traffic while a wedged tunnel does not.
    /// </summary>
    /// <remarks>
    /// Received only: bytes the app sends into a dead tunnel keep leaving the machine
    /// until the socket buffer fills, so counting sends would call a wedged tunnel busy.
    /// The physical interface is counted rather than the core's own sockets because the
    /// core's byte counters only exist when the statistics display is switched on, and
    /// the user runs with it off.
    /// </remarks>
    private long _lastReceivedBytes;

    private long BytesMovedSinceLastTick()
    {
        try
        {
            long total = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up
                    || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                total += ni.GetIPStatistics().BytesReceived;
            }

            var delta = total - _lastReceivedBytes;
            _lastReceivedBytes = total;
            // Counters reset when an interface goes down and up again; a negative
            // delta is that reset, not traffic going backwards.
            return Math.Max(0, delta);
        }
        catch (Exception ex)
        {
            // A missing counter must not stop the watchdog from probing.
            Logging.SaveLog("TrafficWatchdog", ex);
            return 0;
        }
    }

    /// <summary>
    /// Sends one request through the local proxy. A tunnel that cannot carry it is
    /// not able to carry the user's traffic either.
    /// </summary>
    /// <remarks>
    /// The timeout is generous on purpose. This is a liveness check, not a speed
    /// test: a download that saturates the link can make a small request take far
    /// longer than the 5 s used for latency, and scoring that as dead reloaded the
    /// config in the middle of the download. A tunnel that is actually wedged never
    /// answers at all, so waiting longer costs nothing and the traffic check above
    /// covers the busy case.
    /// </remarks>
    private static async Task<bool> ProbeAsync(CancellationToken token)
    {
        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var webProxy = new WebProxy($"socks5://{Global.Loopback}:{port}");
            // One sample, not the five a delay measurement takes: five samples with a
            // timeout each would take longer than the probe interval.
            var quality = await ConnectionHandler.GetRealPingQuality(webProxy, 1, token, ProbeTimeout)
                .ConfigureAwait(false);
            return quality.Median > 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
