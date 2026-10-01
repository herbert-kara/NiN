namespace ServiceLib.Handler;

public static class ConnectionHandler
{
    private static readonly string _tag = "ConnectionHandler";

    /// <summary>
    /// Runs ping and IP checks.
    /// </summary>
    public static async Task<AvailabilityCheckResult> RunAvailabilityCheck()
    {
        var time = await GetRealPingTimeInfo();
        var ip = time > 0 ? await GetIPInfo() : Global.None;

        return new AvailabilityCheckResult(time, ip);
    }

    /// <summary>
    /// Gets IP information using the default local proxy.
    /// </summary>
    private static async Task<string?> GetIPInfo()
    {
        var webProxy = await GetWebProxy();

        var ipInfo = await GetIPInfo(webProxy);
        return ipInfo?.ToString() ?? Global.None;
    }

    /// <summary>
    /// Measures real ping time using configured test URL.
    /// </summary>
    private static async Task<int> GetRealPingTimeInfo()
    {
        var responseTime = -1;
        try
        {
            var webProxy = await GetWebProxy();

            for (var i = 0; i < 2; i++)
            {
                responseTime = await GetRealPingTime(webProxy);
                if (responseTime > 0)
                {
                    break;
                }
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return -1;
        }
        return responseTime;
    }

    /// <summary>
    /// Creates local SOCKS proxy instance.
    /// </summary>
    private static async Task<WebProxy?> GetWebProxy()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        return new WebProxy($"socks5://{Global.Loopback}:{port}");
    }

    /// <summary>
        /// Measures response time by sending HTTP requests through proxy.
        /// Takes <see cref="RealPingSamples"/> attempts so jitter and loss can be
        /// derived, and reports the median rather than the fastest sample: the
        /// minimum flatters a link whose other attempts time out.
        /// </summary>
        public static async Task<int> GetRealPingTime(IWebProxy? webProxy, CancellationToken cancellationToken = default)
            => (await GetRealPingQuality(webProxy, RealPingSamples, cancellationToken)).Median;

        /// <summary>Attempts per real-ping run; enough to see jitter without stalling a bulk scan.</summary>
        public const int RealPingSamples = 5;

        /// <summary>
        /// Runs the in-tunnel samples and reduces them to latency, jitter and loss.
        /// </summary>
        public static async Task<PingQuality> GetRealPingQuality(IWebProxy? webProxy, int samples, CancellationToken cancellationToken = default)
        {
            var url = AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl;
            var timings = new List<int>(Math.Max(samples, 1));
            try
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(Global.LocalFetch);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                var linkedToken = linkedCts.Token;
                using var client = new HttpClient(new SocketsHttpHandler()
                {
                    Proxy = webProxy,
                    UseProxy = webProxy != null,
                    ConnectTimeout = Global.LocalFetch,
                });

                var attempts = Math.Max(samples, 1);
                for (var i = 0; i < attempts; i++)
                {
                    var timer = Stopwatch.StartNew();
                    try
                    {
                        await client.GetAsync(url, linkedToken).ConfigureAwait(false);
                        timer.Stop();
                        timings.Add((int)timer.Elapsed.TotalMilliseconds);
                    }
                    catch
                    {
                        // A failed attempt is data, not an error: it is what the loss
                        // percentage is counting. Only an outright cancellation stops
                        // the run.
                        if (linkedToken.IsCancellationRequested && cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        timings.Add(-1);
                    }
                    await Task.Delay(100, linkedToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Ignore
            }
            return PingQuality.FromSamples(timings);
        }

    /// <summary>
    /// Gets IP and country information through specified proxy.
    /// Tries the configured URL first, then the built-in fallbacks: a single
    /// blocked endpoint used to leave the whole column "none" and the exit-country
    /// flag blank even though the delay test had succeeded.
    /// </summary>
    public static async Task<IpInfoResult?> GetIPInfo(IWebProxy? webProxy, CancellationToken cancellationToken = default)
    {
        var configured = AppManager.Instance.Config.SpeedTestItem.IPAPIUrl;
        var candidates = new List<string>();
        if (!configured.IsNullOrEmpty()) candidates.Add(configured);
        foreach (var fallback in Global.IPAPIUrls)
            if (!fallback.IsNullOrEmpty() && !candidates.Contains(fallback)) candidates.Add(fallback);

        foreach (var url in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TryGetIpInfo(url, webProxy, cancellationToken);
            if (result != null) return result;
        }
        return null;
    }

    private static async Task<IpInfoResult?> TryGetIpInfo(string url, IWebProxy? webProxy, CancellationToken cancellationToken)
    {
        try
        {
            var downloadHandle = new DownloadService();
            var result = await downloadHandle.TryDownloadString(url, webProxy, "", cancellationToken);
            if (result == null)
            {
                return null;
            }

            var ipInfo = JsonUtils.Deserialize<IPAPIInfo>(result);
            if (ipInfo == null)
            {
                return null;
            }

            var ip = ipInfo.ip ?? ipInfo.clientIp ?? ipInfo.ip_addr ?? ipInfo.query;
            var country = ipInfo.country_code ?? ipInfo.country ?? ipInfo.countryCode ?? ipInfo.location?.country_code;

            // A 200 with no country is still a failure for our purpose: the exit
            // flag would stay blank, so try the next endpoint instead.
            if (country.IsNullOrEmpty() || country == "unknown") return null;
            if (ip.IsNullOrEmpty()) ip = country;

            return new IpInfoResult(country, ip);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
