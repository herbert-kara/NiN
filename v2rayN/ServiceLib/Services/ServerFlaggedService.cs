using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace ServiceLib.Services;

/// <summary>Verdict of an anti-fraud / IP-reputation check for one configuration.</summary>
public enum EFlagStatus
{
    /// <summary>No verdict yet, or the check could not run (offline, blocked, unknown).</summary>
    Unknown = 0,

    /// <summary>The public IP looks like a normal connection: not a proxy, VPN or hosting range.</summary>
    Clean = 1,

    /// <summary>The public IP is listed as proxy/VPN/hosting by the reputation service.</summary>
    Flagged = 2,
}

/// <summary>Result of a single reputation check.</summary>
public sealed record FlagVerdict(EFlagStatus Status, int Risk, string? Type, string? CountryCode);

/// <summary>
/// Checks whether a configuration's public IP is flagged as proxy/VPN/hosting by
/// https://proxycheck.io (HTTPS, no API key). Mirrors <see cref="ServerCountryService"/>:
/// private/reserved addresses are refused without any network call, results are cached
/// (24h verdicts, 5min failures), duplicate concurrent lookups are deduped, and outbound
/// requests are serialized with a 1.1s gap to stay friendly to the public endpoint.
/// Only the public IP is ever sent — never the name, credentials or config content.
/// </summary>
public sealed class ServerFlaggedService
{
    public static ServerFlaggedService Instance { get; } = new();

    private const string EndpointBase = "https://proxycheck.io/v2/";
    private const string Query = "?vpn=1&asn=1&risk=1";

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    // proxycheck.io's free plan allows 1000 queries/minute, so a 1100 ms gap --
    // one request at a time -- throttled us to 54/minute and made a 190-config
    // subscription take three and a half minutes to decorate. Keep the courtesy
    // gap but let requests overlap, which is what the provider actually permits.
    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(20);
    private const int MaxCacheEntries = 512;

    /// <summary>proxycheck.io scores 0-100; above this we report Flagged even without an explicit proxy flag.</summary>
    private const int RiskFlagThreshold = 60;

    private readonly Func<string?, CancellationToken, Task<IPAddress[]>> _resolveDns;
    private readonly Func<string, CancellationToken, Task<string>> _sendHttp;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<FlagVerdict?>>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    // 50 in flight keeps us near 1000/minute at RequestGap, under the free limit.
    // ponytail: no adaptive throttle; if proxycheck tightens its quota the gate
    // is the one number to lower, and nothing else needs to move.
    private readonly SemaphoreSlim _httpGate = new(50, 50);
    private long _lastRequestTicks;

    public ServerFlaggedService(Func<string, CancellationToken, Task<string>>? sendHttp = null, Func<string?, CancellationToken, Task<IPAddress[]>>? resolveDns = null)
    {
        _sendHttp = sendHttp ?? DefaultSendAsync;
        _resolveDns = resolveDns ?? DefaultResolveDnsAsync;
    }

    public async Task<FlagVerdict?> ResolveAsync(string? address, CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(address) || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        address = address.Trim();
        var ip = ParsePublicIp(address);
        if (ip == null && Utils.IsIpAddress(address))
        {
            // Parses as an IP but not a public one — refuse without any network call.
            return null;
        }

        if (ip == null && !LooksLikeHostname(address))
        {
            return null;
        }

        var cacheKey = ip?.ToString() ?? address;
        // A manual refresh must actually re-query the provider, not replay a
        // verdict that is up to a day old, so the cache entry is dropped first.
        if (forceRefresh) ClearCache(cacheKey);
        var cached = GetCache(cacheKey);
        if (cached != null)
        {
            return cached.Verdict;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Dedupe concurrent lookups of the same target.
        var task = _inFlight.GetOrAdd(cacheKey, _ => new Lazy<Task<FlagVerdict?>>(() => ResolveSlowAsync(cacheKey, ip))).Value;
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return cached?.Verdict;
        }
    }

    private async Task<FlagVerdict?> ResolveSlowAsync(string address, IPAddress? ip)
    {
        try
        {
            if (ip == null)
            {
                IPAddress[] answers;
                try
                {
                    using var dnsTimeout = new CancellationTokenSource(HttpTimeout);
                    answers = await _resolveDns(address, dnsTimeout.Token).WaitAsync(dnsTimeout.Token);
                }
                catch
                {
                    PutCache(address, null);
                    return null;
                }
                ip = answers.FirstOrDefault(a => IsPublicIp(a));
                if (ip == null)
                {
                    PutCache(address, null);
                    return null;
                }
            }

            var body = await SendWithGateAsync($"{EndpointBase}{ip}{Query}", CancellationToken.None);
            var verdict = ParseVerdict(body, ip.ToString());
            PutCache(address, verdict);
            return verdict;
        }
        catch
        {
            PutCache(address, null);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(address, out _);
        }
    }

    private async Task<string> SendWithGateAsync(string url, CancellationToken cancellationToken)
    {
        await _httpGate.WaitAsync(cancellationToken);
        try
        {
            // Small gap between starts: enough to stay well clear of the provider's
            // rate limit while letting the 50-slot gate do the real throttling.
            var elapsed = _lastRequestTicks == 0 ? RequestGap : Stopwatch.GetElapsedTime(_lastRequestTicks);
            if (elapsed < RequestGap) await Task.Delay(RequestGap - elapsed, cancellationToken);
            Interlocked.Exchange(ref _lastRequestTicks, Stopwatch.GetTimestamp());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HttpTimeout);
            return await _sendHttp(url, timeout.Token).WaitAsync(timeout.Token);
        }
        finally { _httpGate.Release(); }
    }

    private Entry? GetCache(string key)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTimeOffset.UtcNow < entry.ExpiresAt)
                {
                    return entry;
                }
                _cache.TryRemove(key, out _);
            }
        }
        return null;
    }

    /// <summary>Drops a cached verdict so the next lookup hits the provider again.</summary>
    public void ClearCache(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private void PutCache(string key, FlagVerdict? verdict)
    {
        lock (_gate)
        {
            _cache[key] = new Entry(verdict, DateTimeOffset.UtcNow + (verdict == null ? FailureTtl : SuccessTtl));
            while (_cache.Count > MaxCacheEntries)
            {
                var oldest = _cache.OrderBy(kvp => kvp.Value.ExpiresAt).First().Key;
                _cache.TryRemove(oldest, out _);
            }
        }
    }

    /// <summary>
    /// proxycheck.io returns {"status":"ok","1.2.3.4":{...,"risk":N,"proxy":"yes|no","type":"...","isocode":"US"}}.
    /// A non-ok status (rate limit, denied quota, bad input) is not a verdict.
    /// </summary>
    internal static FlagVerdict? ParseVerdict(string? body, string? queriedIp = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            var json = JsonNode.Parse(body);
            if (json == null || json["status"]?.GetValue<string>() != "ok")
            {
                return null;
            }

            // The payload is keyed by the IP that was queried; match it exactly, never guess a key.
            JsonNode? entry = null;
            if (queriedIp != null && json[queriedIp] is JsonNode matched)
            {
                entry = matched;
            }
            if (entry == null)
            {
                return null;
            }

            var proxy = entry["proxy"]?.GetValue<string>();
            var type = entry["type"]?.GetValue<string>();
            var risk = entry["risk"]?.GetValue<int>() ?? 0;
            if (risk < 0) risk = 0;
            if (risk > 100) risk = 100;

            var isProxy = string.Equals(proxy, "yes", StringComparison.OrdinalIgnoreCase);
            var isVpn = string.Equals(type, "VPN", StringComparison.OrdinalIgnoreCase);
            var status = isProxy || isVpn || risk >= RiskFlagThreshold
                ? EFlagStatus.Flagged
                : EFlagStatus.Clean;

            var country = entry["isocode"]?.GetValue<string>();
            return new FlagVerdict(status, risk, string.IsNullOrWhiteSpace(type) ? null : type, ProfileCountry.Normalize(country));
        }
        catch
        {
            return null;
        }
    }

    private static IPAddress? ParsePublicIp(string address)
    {
        if (!Utils.IsIpAddress(address))
        {
            return null;
        }
        var parsed = IPAddress.Parse(address);
        return IsPublicIp(parsed) ? parsed : null;
    }

    private static bool IsPublicIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        return !IPAddress.IsLoopback(address)
            && !IsPrivate(address)
            && !IsLinkLocal(address)
            && !IsMulticast(address)
            && !IsReserved(address)
            && !IsDocumentation(address);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // CGNAT
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // benchmarking
                || (b[0] == 0)
                || b[0] >= 240; // reserved + broadcast
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast;
        }
        return true;
    }

    private static bool IsLinkLocal(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetwork
            ? address.GetAddressBytes()[0] == 169 && address.GetAddressBytes()[1] == 254
            : address.IsIPv6LinkLocal;

    private static bool IsMulticast(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetwork
            ? (address.GetAddressBytes()[0] & 0xF0) == 0xE0
            : address.IsIPv6Multicast;

    private static bool IsReserved(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0 || b[0] >= 240
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }
        var bytes = address.GetAddressBytes();
        // Only global unicast; exclude special-purpose 2001::/23 and 2002::/16.
        return (bytes[0] & 0xe0) != 0x20
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] < 2)
            || (bytes[0] == 0x20 && bytes[1] == 0x02);
    }

    private static bool IsDocumentation(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return (b[0] == 192 && b[1] == 0 && b[2] == 2)
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        return address.ToString().StartsWith("2001:db8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHostname(string address)
        => Uri.CheckHostName(address) == UriHostNameType.Dns
            && !address.All(c => char.IsAsciiDigit(c) || c == '.');

    private static async Task<IPAddress[]> DefaultResolveDnsAsync(string? host, CancellationToken cancellationToken)
    {
        if (host == null)
        {
            return [];
        }
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static async Task<string> DefaultSendAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(url, cancellationToken);
    }

    private sealed record Entry(FlagVerdict? Verdict, DateTimeOffset ExpiresAt);
}
