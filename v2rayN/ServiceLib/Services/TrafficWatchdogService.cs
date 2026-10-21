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

        var tunnelOk = await ProbeAsync(token);
        if (!_watchdog.Tick(tunnelOk))
        {
            return;
        }

        Logging.SaveLog($"TrafficWatchdog: {ResUI.TipTunnelStalled}");
        NoticeManager.Instance.SendMessageEx(ResUI.TipTunnelStalled);

        // The same path the toolbar's Reload button uses.
        StatusBarViewModel.Instance.ReloadRequested.Publish();
    }

    /// <summary>
    /// Sends one request through the local proxy. A tunnel that cannot carry it is
    /// not able to carry the user's traffic either.
    /// </summary>
    private static async Task<bool> ProbeAsync(CancellationToken token)
    {
        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var webProxy = new WebProxy($"socks5://{Global.Loopback}:{port}");
            // One sample, not the five a delay measurement takes: five samples with a
            // five second timeout each would take longer than the probe interval.
            var quality = await ConnectionHandler.GetRealPingQuality(webProxy, 1, token).ConfigureAwait(false);
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
