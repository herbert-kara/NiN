using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ServiceLib.Services;

/// <summary>
/// Resolves the country of a server address (hostname or IP) by geolocating its
/// public IP via https://ipwho.is. Never touches private/reserved addresses,
/// never falls back to the local machine's country, and keeps a bounded cache
/// (24h successes, 5min failures). Outbound HTTP is serialized with a minimum
/// 1.1s gap so we stay friendly to the public endpoint.
/// </summary>
public sealed class ServerCountryService
{
    public static ServerCountryService Instance { get; } = new();

    private const string EndpointBase = "https://ipwho.is/";
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(1100);
    private const int MaxCacheEntries = 512;

    private readonly Func<string?, CancellationToken, Task<IPAddress[]>> _resolveDns;
    private readonly Func<string, CancellationToken, Task<string>> _sendHttp;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _httpGate = new(1, 1);
    private long _lastRequestTicks;

    public ServerCountryService(Func<string, CancellationToken, Task<string>>? sendHttp = null, Func<string?, CancellationToken, Task<IPAddress[]>>? resolveDns = null)
    {
        _sendHttp = sendHttp ?? DefaultSendAsync;
        _resolveDns = resolveDns ?? DefaultResolveDnsAsync;
    }

    public async Task<string?> ResolveAsync(string? address, CancellationToken cancellationToken = default, bool forceRefresh = false)
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
        // Match the reputation service: a manual refresh must re-query, not replay.
        if (forceRefresh) ClearCache(cacheKey);
        var cached = GetCache(cacheKey);
        if (cached != null)
        {
            return cached.Code;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Dedupe concurrent lookups of the same target.
        var task = _inFlight.GetOrAdd(cacheKey, _ => new Lazy<Task<string?>>(() => ResolveSlowAsync(cacheKey, ip))).Value;
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
            return cached?.Code;
        }
    }

    private async Task<string?> ResolveSlowAsync(string address, IPAddress? ip)
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

            var body = await SendWithGateAsync($"{EndpointBase}{ip}", CancellationToken.None);
            var code = ParseCountryCode(body);
            PutCache(address, code);
            return code;
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
            var elapsed = _lastRequestTicks == 0 ? RequestGap : Stopwatch.GetElapsedTime(_lastRequestTicks);
            if (elapsed < RequestGap) await Task.Delay(RequestGap - elapsed, cancellationToken);
            _lastRequestTicks = Stopwatch.GetTimestamp();
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

    /// <summary>Drops a cached country so the next lookup hits the provider again.</summary>
    public void ClearCache(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private void PutCache(string key, string? code)
    {
        lock (_gate)
        {
            _cache[key] = new Entry(code, DateTimeOffset.UtcNow + (code == null ? FailureTtl : SuccessTtl));
            while (_cache.Count > MaxCacheEntries)
            {
                var oldest = _cache.OrderBy(kvp => kvp.Value.ExpiresAt).First().Key;
                _cache.TryRemove(oldest, out _);
            }
        }
    }

    private static string? ParseCountryCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            var json = JsonNode.Parse(body);
            if (json?["success"]?.GetValue<bool>() != true)
            {
                return null;
            }
            return ProfileCountry.Normalize(json["country_code"]?.GetValue<string>());
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

    private sealed record Entry(string? Code, DateTimeOffset ExpiresAt);
}
