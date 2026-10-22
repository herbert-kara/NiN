namespace ServiceLib.Tests.Services;

public class TrafficWatchdogTests
{
    /// <summary>
    /// A tunnel that answers must never trigger a reload, however often it is probed.
    /// </summary>
    [Test]
    public async Task AWorkingTunnelNeverTriggersAReload()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 3);

        for (var i = 0; i < 20; i++)
        {
            await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();
        }
    }

    /// <summary>
    /// The failure the user hit: the tunnel was working, then it stopped answering and
    /// stayed that way. That is a wedged tunnel and the config has to be reloaded.
    /// </summary>
    [Test]
    public async Task ATunnelThatStopsAnsweringTriggersAReload()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 3);

        // It was working.
        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();

        // It stopped. Not yet -- the failure has to repeat.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();

        // The third failure in a row is a wedged tunnel.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();
    }

    /// <summary>
    /// The bug the user hit next: a download saturated the link, the probe timed out,
    /// and the config was reloaded mid-download. Bytes arriving are proof the tunnel
    /// works, so a failed probe while traffic flows must never count as a stall.
    /// </summary>
    [Test]
    public async Task AProbeFailureDuringADownloadNeverTriggersAReload()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 3);

        // The tunnel worked before the download started.
        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();

        // The link is saturated: every probe times out, but data is arriving.
        for (var i = 0; i < 20; i++)
        {
            await watchdog.Tick(tunnelOk: false, trafficFlowing: true).Should().BeFalse();
        }
    }

    /// <summary>
    /// Traffic must also clear a streak that was already building, so a link that
    /// recovers mid-countdown is not reloaded for failures that are already stale.
    /// </summary>
    [Test]
    public async Task TrafficClearsAnExistingFailureStreak()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 3);

        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();

        // Data starts flowing: the two failures are stale, not a countdown.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: true).Should().BeFalse();

        // A real stall now has to build up from zero.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();
    }

    /// <summary>
    /// One reload is enough: the reload restarts the core, and the next failure must
    /// not queue another one on top of it.
    /// </summary>
    [Test]
    public async Task AReloadIsNotRepeatedForTheSameStall()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 2);

        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();

        // Still down, but the reload is already in flight.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
    }

    /// <summary>
    /// A tunnel that answers again is healthy: the failure streak is over, and a later
    /// stall has to build up from scratch.
    /// </summary>
    [Test]
    public async Task ATunnelThatRecoversClearsTheStreak()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 3);

        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();

        // It answers again: the streak is over.
        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();

        // A new stall has to build up again from zero.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();
    }

    /// <summary>
    /// A tunnel that never answered cannot be fixed by reloading the config -- the
    /// server is simply down. Reloading in a loop would only restart the core forever.
    /// </summary>
    [Test]
    public async Task ATunnelThatNeverAnsweredNeverTriggersAReload()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 2);

        for (var i = 0; i < 20; i++)
        {
            await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        }
    }

    /// <summary>
    /// After a reload the tunnel is starting up, so it fails for a moment. That must
    /// not re-arm the watchdog into another reload: the core has to answer once first.
    /// </summary>
    [Test]
    public async Task AFailedTunnelNeedsToAnswerOnceBeforeItCanFailAgain()
    {
        var watchdog = new TrafficWatchdog(failuresBeforeReload: 2);

        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();

        // The reload is in flight and the core is coming up: failures are ignored.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();

        // It came up: the watchdog is armed again.
        await watchdog.Tick(tunnelOk: true, trafficFlowing: false).Should().BeFalse();

        // And a fresh stall is caught again.
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeFalse();
        await watchdog.Tick(tunnelOk: false, trafficFlowing: false).Should().BeTrue();
    }
}
