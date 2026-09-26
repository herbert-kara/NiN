using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ServiceLib.Tests.Services;

public class ServerFlaggedServiceTests
{
    // ---- direct address inputs (no DNS) -----------------------------------

    [Test]
    [Arguments("10.0.0.1")]
    [Arguments("192.168.1.1")]
    [Arguments("127.0.0.1")]
    [Arguments("169.254.1.1")]
    [Arguments("192.0.2.55")]
    [Arguments("224.0.0.5")]
    [Arguments("240.0.0.1")]
    [Arguments("100.64.0.1")]
    [Arguments("198.18.0.1")]
    [Arguments("0.0.0.0")]
    [Arguments("255.255.255.255")]
    [Arguments("::1")]
    [Arguments("fe80::1")]
    [Arguments("fc00::1")]
    [Arguments("2001:db8::1")]
    [Arguments("ff02::1")]
    [Arguments("::")]
    [Arguments("::ffff:192.168.1.1")]
    [Arguments("192.0.0.1")]
    [Arguments("64:ff9b::a00:1")]
    public async Task ResolveAsync_ShouldNotQueryHttpForNonPublicDirectIps(string address)
    {
        var http = new FakeHttp();
        var service = CreateService(http: http);
        var result = await service.ResolveAsync(address);
        await result.Should().BeNull();
        await http.CallCount.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ResolveAsync_NullOrEmptyOrWhitespace_ReturnsNullWithoutHttp()
    {
        var http = new FakeHttp();
        var service = CreateService(http: http);
        await (await service.ResolveAsync(null)).Should().BeNull();
        await (await service.ResolveAsync("")).Should().BeNull();
        await (await service.ResolveAsync("   ")).Should().BeNull();
        await http.CallCount.Should().BeEqualTo(0);
    }

    // ---- verdict mapping ---------------------------------------------------

    [Test]
    public async Task ResolveAsync_ProxyYes_IsFlagged()
    {
        var http = new FakeHttp(("8.8.8.8", """{"status":"ok","8.8.8.8":{"risk":66,"proxy":"yes","type":"VPN","isocode":"US"}}"""));
        var service = CreateService(http: http);
        var verdict = await service.ResolveAsync("8.8.8.8");
        await verdict.Should().NotBeNull();
        await verdict!.Status.Should().BeEqualTo(EFlagStatus.Flagged);
        await verdict.Risk.Should().BeEqualTo(66);
        await verdict.Type.Should().BeEqualTo("VPN");
        await verdict.CountryCode.Should().BeEqualTo("US");
    }

    [Test]
    public async Task ResolveAsync_ProxyNoAndLowRisk_IsClean()
    {
        var http = new FakeHttp(("1.1.1.1", """{"status":"ok","1.1.1.1":{"risk":0,"proxy":"no","type":"Business","isocode":"AU"}}"""));
        var service = CreateService(http: http);
        var verdict = await service.ResolveAsync("1.1.1.1");
        await verdict!.Status.Should().BeEqualTo(EFlagStatus.Clean);
        await verdict.Risk.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ResolveAsync_HighRiskEvenWithoutProxyFlag_IsFlagged()
    {
        // proxy says "no" but the risk score alone crosses the threshold.
        var http = new FakeHttp(("9.9.9.9", """{"status":"ok","9.9.9.9":{"risk":75,"proxy":"no","type":"Residential"}}"""));
        var service = CreateService(http: http);
        var verdict = await service.ResolveAsync("9.9.9.9");
        await verdict!.Status.Should().BeEqualTo(EFlagStatus.Flagged);
    }

    [Test]
    public async Task ResolveAsync_TypeVpnWithoutProxyYes_IsFlagged()
    {
        var http = new FakeHttp(("4.4.4.4", """{"status":"ok","4.4.4.4":{"risk":10,"proxy":"no","type":"VPN"}}"""));
        var service = CreateService(http: http);
        await (await service.ResolveAsync("4.4.4.4"))!.Status.Should().BeEqualTo(EFlagStatus.Flagged);
    }

    [Test]
    public async Task ResolveAsync_EndpointDenialOrBadBody_IsNotAVerdict()
    {
        var http = new FakeHttp(
            ("5.5.5.5", """{"status":"denied","message":"quota"}"""),
            ("6.6.6.6", """{"status":"ok","1.1.1.1":{"risk":0,"proxy":"no"}}"""), // key mismatch: never guess
            ("7.7.7.7", "not-json"),
            ("2.2.2.2", ""));
        var service = CreateService(http: http);
        await (await service.ResolveAsync("5.5.5.5")).Should().BeNull();
        await (await service.ResolveAsync("6.6.6.6")).Should().BeNull();
        await (await service.ResolveAsync("7.7.7.7")).Should().BeNull();
        await (await service.ResolveAsync("2.2.2.2")).Should().BeNull();
    }

    [Test]
    public async Task ResolveAsync_CountryCodeIsNormalized()
    {
        var http = new FakeHttp(("1.0.0.2", """{"status":"ok","1.0.0.2":{"risk":0,"proxy":"no","isocode":"UK"}}"""));
        var service = CreateService(http: http);
        await (await service.ResolveAsync("1.0.0.2"))!.CountryCode.Should().BeEqualTo("GB");
    }

    [Test]
    public async Task ResolveAsync_RiskIsClampedTo0And100()
    {
        var http = new FakeHttp(
            ("1.2.3.4", """{"status":"ok","1.2.3.4":{"risk":-5,"proxy":"no"}}"""),
            ("1.2.3.5", """{"status":"ok","1.2.3.5":{"risk":250,"proxy":"no"}}"""));
        var service = CreateService(http: http);
        await (await service.ResolveAsync("1.2.3.4"))!.Risk.Should().BeEqualTo(0);
        await (await service.ResolveAsync("1.2.3.5"))!.Risk.Should().BeEqualTo(100);
    }

    // ---- hostnames ---------------------------------------------------------

    [Test]
    public async Task ResolveAsync_Hostname_ResolvesDnsPicksPublicAnswerAndQueries()
    {
        var dns = new FakeDns("example.com", ["10.0.0.1", "8.8.8.8", "192.168.1.1"]);
        var http = new FakeHttp(("8.8.8.8", """{"status":"ok","8.8.8.8":{"risk":0,"proxy":"no"}}"""));
        var service = CreateService(http: http, dns: dns);
        var verdict = await service.ResolveAsync("example.com");
        await verdict!.Status.Should().BeEqualTo(EFlagStatus.Clean);
        await http.LastPath.Should().Contain("/8.8.8.8");
    }

    [Test]
    public async Task ResolveAsync_HostnameOnlyPrivateAnswers_ReturnsNullWithoutHttp()
    {
        var dns = new FakeDns("internal.corp", ["10.0.0.1", "192.168.1.1"]);
        var http = new FakeHttp();
        var service = CreateService(http: http, dns: dns);
        await (await service.ResolveAsync("internal.corp")).Should().BeNull();
        await http.CallCount.Should().BeEqualTo(0);
    }

    // ---- caching, dedupe, rate limiting ------------------------------------

    [Test]
    public async Task ResolveAsync_SuccessResultIsCached_NoSecondHttpCall()
    {
        var http = new FakeHttp(("8.8.8.8", """{"status":"ok","8.8.8.8":{"risk":0,"proxy":"no"}}"""));
        var service = CreateService(http: http);
        await (await service.ResolveAsync("8.8.8.8"))!.Status.Should().BeEqualTo(EFlagStatus.Clean);
        await (await service.ResolveAsync("8.8.8.8"))!.Status.Should().BeEqualTo(EFlagStatus.Clean);
        await http.CallCount.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ResolveAsync_FailureIsCachedWithinWindow_NoImmediateRetry()
    {
        var http = new FakeHttp();
        var service = CreateService(http: http);
        await (await service.ResolveAsync("3.3.3.3")).Should().BeNull();
        await (await service.ResolveAsync("3.3.3.3")).Should().BeNull();
        await http.CallCount.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ResolveAsync_ConcurrentDuplicateRequests_DedupeToSingleHttpCall()
    {
        var http = new FakeHttp(("4.4.4.4", """{"status":"ok","4.4.4.4":{"risk":0,"proxy":"no"}}"""));
        var service = CreateService(http: http);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.ResolveAsync("4.4.4.4")));
        foreach (var result in results) await result!.Status.Should().BeEqualTo(EFlagStatus.Clean);
        await http.CallCount.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ResolveAsync_SequentialRequests_AreSpacedAtLeast1100ms()
    {
        var http = new FakeHttp(
            ("8.8.8.8", """{"status":"ok","8.8.8.8":{"risk":0,"proxy":"no"}}"""),
            ("1.1.1.1", """{"status":"ok","1.1.1.1":{"risk":0,"proxy":"no"}}"""),
            ("4.4.4.4", """{"status":"ok","4.4.4.4":{"risk":0,"proxy":"no"}}"""));
        var service = CreateService(http: http);
        var stopwatch = Stopwatch.StartNew();
        await service.ResolveAsync("8.8.8.8");
        await service.ResolveAsync("1.1.1.1");
        await service.ResolveAsync("4.4.4.4");
        stopwatch.Stop();
        await (stopwatch.ElapsedMilliseconds >= 2200).Should().BeTrue();
    }

    [Test]
    public async Task ResolveAsync_PreCancelled_ReturnsNullWithoutHttp()
    {
        var http = new FakeHttp();
        var service = CreateService(http: http);
        await (await service.ResolveAsync("8.8.8.8", new CancellationToken(true))).Should().BeNull();
        await http.CallCount.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ResolveAsync_HttpThrows_ReturnsNullWithoutCrash()
    {
        var http = new FakeHttp { ThrowOnCall = true };
        var service = CreateService(http: http);
        await (await service.ResolveAsync("8.8.8.8")).Should().BeNull();
    }

    // ---- the coloured flag assets exist and match the verdict --------------

    [Test]
    [Arguments(EFlagStatus.Clean, "nin_flag_clean.png")]
    [Arguments(EFlagStatus.Flagged, "nin_flag_flagged.png")]
    [Arguments(EFlagStatus.Unknown, "nin_flag_unknown.png")]
    public void StatusFlag_IsEmbeddedAndOpenable(EFlagStatus status, string fileName)
    {
        var names = typeof(ProfileCountry).Assembly.GetManifestResourceNames();
        await (names.Contains($"ServiceLib.Resources.Flags.{fileName}")).Should().BeTrue();
        using var stream = ProfileCountry.OpenStatusFlag(status);
        await (stream != null).Should().BeTrue();
    }

    [Test]
    public void StatusFlag_ColoursAreDistinctPerState()
    {
        // The three flags must not look alike; compare raw bytes of the embedded assets.
        var blobs = new[]
        {
            EFlagStatus.Clean,
            EFlagStatus.Flagged,
            EFlagStatus.Unknown,
        }.Select(s =>
        {
            using var stream = ProfileCountry.OpenStatusFlag(s)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Convert.ToBase64String(ms.ToArray());
        }).ToList();
        await blobs.Distinct().Count().Should().BeEqualTo(3);
    }

    [Test]
    public void FlagCode_UnknownIsTheDefault()
    {
        await ProfileCountry.FlagCode(EFlagStatus.Unknown).Should().BeEqualTo("nin_flag_unknown");
        await ProfileCountry.FlagCode(EFlagStatus.Clean).Should().BeEqualTo("nin_flag_clean");
        await ProfileCountry.FlagCode(EFlagStatus.Flagged).Should().BeEqualTo("nin_flag_flagged");
    }

    // ---- model plumbing ----------------------------------------------------

    [Test]
    public async Task ProfileItemModel_DefaultsToUnknownAndTracksCode()
    {
        var item = new ProfileItemModel { Remarks = "x" };
        await item.FlagStatus.Should().BeEqualTo(EFlagStatus.Unknown);
        await item.FlagStatusCode.Should().BeEqualTo("nin_flag_unknown");
        item.FlagStatus = EFlagStatus.Flagged;
        await item.FlagStatusCode.Should().BeEqualTo("nin_flag_flagged");
        await item.FlagStatusText.Should().Contain("FLAGGED");
        item.FlagStatus = EFlagStatus.Clean;
        await item.FlagStatusCode.Should().BeEqualTo("nin_flag_clean");
        await item.FlagStatusText.Should().Contain("Clean");
    }

    // ---- live lookup, opt-in -----------------------------------------------

    [Test]
    public async Task LiveLookupWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("NIMN_LIVE_FLAG_TEST") != "1") return;
        var verdict = await ServerFlaggedService.Instance.ResolveAsync("8.8.8.8");
        await (verdict != null).Should().BeTrue();
        Console.WriteLine($"LIVE 8.8.8.8 -> {verdict!.Status} risk={verdict.Risk} type={verdict.Type}");
    }

    // ---- helpers -----------------------------------------------------------

    private static ServerFlaggedService CreateService(FakeHttp? http = null, FakeDns? dns = null)
    {
        http ??= new FakeHttp();
        dns ??= new FakeDns();
        return new ServerFlaggedService(http.SendAsync, dns.GetHostAddressesAsync);
    }

    private sealed class FakeHttp
    {
        private readonly Queue<string> _bodies;
        private readonly List<string> _paths = [];

        public FakeHttp(params (string Path, string Body)[] responses)
        {
            _bodies = new Queue<string>(responses.Select(r => r.Body));
        }

        public int CallCount { get; private set; }
        public string? LastPath => _paths.Count > 0 ? _paths[^1] : null;
        public bool ThrowOnCall { get; set; }

        public async Task<string> SendAsync(string url, CancellationToken cancellationToken)
        {
            CallCount++;
            _paths.Add(new Uri(url).AbsolutePath);
            await Task.Delay(5, cancellationToken);
            if (ThrowOnCall) throw new HttpRequestException("simulated failure");
            return _bodies.TryDequeue(out var body) ? body : throw new HttpRequestException("no response configured");
        }
    }

    private sealed class FakeDns
    {
        private readonly string? _host;
        private readonly string[]? _answers;

        public FakeDns() { }

        public FakeDns(string host, string[] answers)
        {
            _host = host;
            _answers = answers;
        }

        public int CallCount { get; private set; }
        public bool ThrowOnCall { get; set; }

        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowOnCall || _host != host || _answers == null)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }
            return Task.FromResult(_answers.Select(IPAddress.Parse).ToArray());
        }
    }
}
