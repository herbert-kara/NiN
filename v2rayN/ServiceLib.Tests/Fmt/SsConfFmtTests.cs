namespace ServiceLib.Tests.Fmt;

/// <summary>
/// Outline dynamic access keys (ssconf://) carry no cipher data: the link has to
/// be fetched and its payload (an ss:// link or an outline-sdk YAML document)
/// turned into an ordinary Shadowsocks profile. These cases pin both halves.
/// </summary>
public class SsConfFmtTests
{
    private const string SsConfUrl = "ssconf://dynamic-access-keys.s3.us-east-2.amazonaws.com/KEY-1#D3627185";
    private const string KeyYaml = """
        transport:
          $type: tcpudp
          tcp:
            $type: shadowsocks
            endpoint: c3.example.net:443
            cipher: chacha20-ietf-poly1305
            secret: hunter2hunter2
            prefix: 'POST%20http://example.com/'
        """;

    [Test]
    public async Task IsSsConf_ShouldOnlyMatchTheSsConfScheme()
    {
        await SsConfFmt.IsSsConf(SsConfUrl).Should().BeTrue();
        await SsConfFmt.IsSsConf("  " + SsConfUrl + "  ").Should().BeTrue();
        await SsConfFmt.IsSsConf("ss://YWVzLTEyOC1nY206cGFzcw==@example.com:8388").Should().BeFalse();
        await SsConfFmt.IsSsConf("https://example.com/x").Should().BeFalse();
        await SsConfFmt.IsSsConf(null).Should().BeFalse();
    }

    [Test]
    public async Task ToHttpUrl_ShouldDropTheFragmentAndKeepTheQuery()
    {
        await SsConfFmt.ToHttpUrl(SsConfUrl)
            .Should().BeEqualTo("https://dynamic-access-keys.s3.us-east-2.amazonaws.com/KEY-1");
        await SsConfFmt.ToHttpUrl("ssconf://host/p?a=1#name")
            .Should().BeEqualTo("https://host/p?a=1");
    }

    [Test]
    public async Task GetKeyName_ShouldDecodeTheFragment()
    {
        await SsConfFmt.GetKeyName(SsConfUrl).Should().BeEqualTo("D3627185");
        await SsConfFmt.GetKeyName("ssconf://host/p#my%20key").Should().BeEqualTo("my key");
        await SsConfFmt.GetKeyName("ssconf://host/p").Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task ResolveOutlineKey_ShouldMapTheTcpTransport()
    {
        var item = SsConfFmt.ResolveOutlineKey(KeyYaml, out _);

        await item.Should().NotBeNull();
        await item!.ConfigType.Should().BeEqualTo(EConfigType.Shadowsocks);
        await item.Address.Should().BeEqualTo("c3.example.net");
        await item.Port.Should().BeEqualTo(443);
        await item.Password.Should().BeEqualTo("hunter2hunter2");
        await item.GetProtocolExtra().SsMethod.Should().BeEqualTo("chacha20-ietf-poly1305");
    }

    [Test]
    public async Task ResolveOutlineKey_ShouldReadAnIpv6Endpoint()
    {
        var yaml = KeyYaml.Replace("c3.example.net:443", "[2001:db8::1]:8443");

        var item = SsConfFmt.ResolveOutlineKey(yaml, out _);

        await item!.Address.Should().BeEqualTo("2001:db8::1");
        await item.Port.Should().BeEqualTo(8443);
    }

    /// <summary>The tcp transport can be nested anywhere in the document.</summary>
    [Test]
    public async Task ResolveOutlineKey_ShouldFindTheTransportInASequence()
    {
        var yaml = """
            name: whatever
            transports:
              - $type: websocket
                url: wss://example.com/ws
              - $type: tcpudp
                tcp:
                  $type: shadowsocks
                  endpoint: host.example.org:9000
                  cipher: aes-256-gcm
                  secret: pw
            """;

        var item = SsConfFmt.ResolveOutlineKey(yaml, out _);

        await item!.Address.Should().BeEqualTo("host.example.org");
        await item.Port.Should().BeEqualTo(9000);
        await item.GetProtocolExtra().SsMethod.Should().BeEqualTo("aes-256-gcm");
        await item.Password.Should().BeEqualTo("pw");
    }

    [Test]
    public async Task ResolveOutlineKey_ShouldReportAnUnsupportedTransport()
    {
        var yaml = """
            transport:
              $type: websocket
              url: wss://example.com/ws
            """;

        var item = SsConfFmt.ResolveOutlineKey(yaml, out var msg);

        await item.Should().BeNull();
        await msg.IsNotEmpty().Should().BeTrue();
    }

    [Test]
    public async Task ResolveOutlineKey_ShouldRejectIncompleteOrBrokenKeys()
    {
        await SsConfFmt.ResolveOutlineKey(KeyYaml.Replace("secret: hunter2hunter2", "secret:"), out _)
            .Should().BeNull();
        await SsConfFmt.ResolveOutlineKey(KeyYaml.Replace("c3.example.net:443", "c3.example.net"), out _)
            .Should().BeNull();
        await SsConfFmt.ResolveOutlineKey("not: [valid", out _).Should().BeNull();
        await SsConfFmt.ResolveOutlineKey(string.Empty, out _).Should().BeNull();
    }
}