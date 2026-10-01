namespace ServiceLib.Tests.Handler;

/// <summary>
/// A subscription may hand back an array of complete Xray configs instead of
/// share links. Importing those verbatim yields EConfigType.Custom: opaque files
/// with no address and no per-node test. These tests pin the conversion of the
/// proxy outbound into a real, testable profile.
/// </summary>
public class ProxyOutboundFmtTests
{
    private const string FullConfigVless = """
    {
      "remarks": "1. VLESS - Domain",
      "inbounds": [ { "port": 10808, "protocol": "socks" } ],
      "outbounds": [
        {
          "protocol": "vless",
          "tag": "proxy",
          "settings": { "vnext": [ {
            "address": "example.com",
            "port": 443,
            "users": [ { "id": "c688a2d6-d843-4484-99a5-48ffc4ccce58", "encryption": "none" } ]
          } ] },
          "streamSettings": {
            "network": "ws",
            "security": "tls",
            "wsSettings": { "path": "/ray", "headers": { "Host": "cdn.example.com" } },
            "tlsSettings": { "serverName": "cdn.example.com" }
          }
        },
        { "protocol": "freedom", "tag": "direct", "settings": {} },
        { "protocol": "blackhole", "tag": "block", "settings": {} }
      ]
    }
    """;

    [Test]
    public async Task ConvertsProxyOutboundToTestableProfile()
    {
        var list = ProxyOutboundFmt.Resolve(FullConfigVless, "sub");
        await list.Count.Should().BeEqualTo(1);

        var p = list[0];
        // The whole point: a normal profile with an address, not a Custom blob.
        await p.ConfigType.Should().BeEqualTo(EConfigType.VLESS);
        await p.Address.Should().BeEqualTo("example.com");
        await p.Port.Should().BeEqualTo(443);
        await p.Password.Should().BeEqualTo("c688a2d6-d843-4484-99a5-48ffc4ccce58");
        await p.Network.Should().BeEqualTo("ws");
        await p.StreamSecurity.Should().BeEqualTo("tls");
        await p.Sni.Should().BeEqualTo("cdn.example.com");
        await p.GetTransportExtra().Path.Should().BeEqualTo("/ray");
        await p.GetTransportExtra().Host.Should().BeEqualTo("cdn.example.com");
    }

    [Test]
    public async Task SkipsFreedomBlackholeAndDnsOutbounds()
    {
        var list = ProxyOutboundFmt.Resolve(FullConfigVless, "sub");
        await list.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ReadsTheHumanRemarkFromTheEnclosingConfig()
    {
        // The provider names the node on the config; the outbound tag is just "proxy".
        var list = ProxyOutboundFmt.Resolve(FullConfigVless, "sub");
        await list[0].Remarks.Should().BeEqualTo("1. VLESS - Domain");
    }

    [Test]
    public async Task HandlesAnArrayOfFullConfigs()
    {
        var payload = "[" + string.Join(",", Enumerable.Repeat(FullConfigVless, 3)) + "]";
        var list = ProxyOutboundFmt.Resolve(payload, "sub");
        await list.Count.Should().BeEqualTo(3);
    }

    [Test]
    public async Task ConvertsTrojanUsingThePasswordField()
    {
        const string json = """
        { "outbounds": [ {
            "protocol": "trojan", "tag": "proxy",
            "settings": { "servers": [ {
                "address": "t.example.com", "port": 8443,
                "users": [ { "password": "hunter2" } ] } ] },
            "streamSettings": { "network": "ws", "security": "tls" }
        } ] }
        """;
        var list = ProxyOutboundFmt.Resolve(json, "sub");
        await list.Count.Should().BeEqualTo(1);
        await list[0].ConfigType.Should().BeEqualTo(EConfigType.Trojan);
        await list[0].Password.Should().BeEqualTo("hunter2");
        await list[0].Port.Should().BeEqualTo(8443);
    }

    [Test]
    public async Task CopiesRealitySettingsIncludingTheRequiredPublicKey()
    {
        const string json = """
        { "outbounds": [ {
            "protocol": "vless", "tag": "proxy",
            "settings": { "vnext": [ {
                "address": "r.example.com", "port": 443,
                "users": [ { "id": "c688a2d6-d843-4484-99a5-48ffc4ccce58" } ] } ] },
            "streamSettings": {
                "network": "tcp", "security": "reality",
                "realitySettings": {
                    "serverName": "www.microsoft.com",
                    "publicKey": "PUBLICKEY",
                    "shortId": "abcd", "spiderX": "/", "fingerprint": "chrome"
                }
            }
        } ] }
        """;
        var list = ProxyOutboundFmt.Resolve(json, "sub");
        await list.Count.Should().BeEqualTo(1);
        await list[0].StreamSecurity.Should().BeEqualTo("reality");
        await list[0].PublicKey.Should().BeEqualTo("PUBLICKEY");
        await list[0].Sni.Should().BeEqualTo("www.microsoft.com");
        await list[0].ShortId.Should().BeEqualTo("abcd");
        await list[0].Fingerprint.Should().BeEqualTo("chrome");
        // Without the key the profile fails IsValid and can never connect.
        await list[0].IsValid().Should().BeTrue();
    }

    [Test]
    public async Task ProducesAProfileThatPassesValidation()
    {
        var list = ProxyOutboundFmt.Resolve(FullConfigVless, "sub");
        await list[0].IsValid().Should().BeTrue();
    }

    [Test]
    public async Task RejectsOutboundsWithNoServers()
    {
        const string json = """{ "outbounds": [ { "protocol": "vless", "tag": "proxy", "settings": {} } ] }""";
        await ProxyOutboundFmt.Resolve(json, "sub").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task RejectsInvalidPorts()
    {
        const string json = """
        { "outbounds": [ { "protocol": "vless", "tag": "proxy",
            "settings": { "vnext": [ { "address": "a.com", "port": 0,
            "users": [ { "id": "c688a2d6-d843-4484-99a5-48ffc4ccce58" } ] } ] } } ] }
        """;
        await ProxyOutboundFmt.Resolve(json, "sub").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ReturnsEmptyForAPlainShareLinkPayloadSoCallerFallsThrough()
    {
        // Must not claim payloads it cannot convert, or the existing import paths
        // would never run.
        await ProxyOutboundFmt.Resolve("vless://uuid@a.com:443?type=ws#x", "sub").Count.Should().BeEqualTo(0);
        await ProxyOutboundFmt.Resolve("vmess://eyJhIjoxfQ", "sub").Count.Should().BeEqualTo(0);
        await ProxyOutboundFmt.Resolve("", "sub").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task IgnoresUnsupportedProxyProtocols()
    {
        const string json = """
        { "outbounds": [ { "protocol": "hysteria2", "tag": "proxy",
            "settings": { "servers": [ { "address": "h.example.com", "port": 443,
            "password": "p" } ] } } ] }
        """;
        await ProxyOutboundFmt.Resolve(json, "sub").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ImportsSeveralNodesFromOneFile()
    {
        // One config per node, each a full config: the shape the real subscription
        // uses. Every proxy outbound becomes its own row.
        const string a = """
        { "remarks": "node-a", "outbounds": [
            { "protocol": "vless", "tag": "proxy",
              "settings": { "vnext": [ { "address": "a.com", "port": 443,
              "users": [ { "id": "c688a2d6-d843-4484-99a5-48ffc4ccce58" } ] } ] } } ] }
        """;
        const string b = """
        { "remarks": "node-b", "outbounds": [
            { "protocol": "trojan", "tag": "proxy",
              "settings": { "servers": [ { "address": "b.com", "port": 443,
              "users": [ { "password": "pw" } ] } ] } } ] }
        """;
        var list = ProxyOutboundFmt.Resolve($"[{a},{b}]", "sub");
        await list.Count.Should().BeEqualTo(2);
        await list.Any(x => x.Remarks == "node-a").Should().BeTrue();
        await list.Any(x => x.Remarks == "node-b").Should().BeTrue();
        foreach (var x in list)
        {
            await x.IsValid().Should().BeTrue();
        }
    }

    [Test]
    public async Task ArrayOfFullConfigs_YieldsEveryNode()
    {
        // A subscription that returns several complete Xray configs, each carrying
        // its own outbounds. Every entry must produce a profile, not just the
        // first one -- this mirrors what real subs actually return.
        string Config(int i) => $$"""
        {
          "remarks": "node {{i}}",
          "inbounds": [{ "port": 10808, "protocol": "socks", "settings": {} }],
          "outbounds": [
            {
              "tag": "proxy", "protocol": "vless",
              "settings": { "vnext": [{
                  "address": "a{{i}}.example.com", "port": 443,
                  "users": [{ "id": "uuid-{{i}}", "encryption": "none" }] }] },
              "streamSettings": {
                  "network": "ws", "security": "tls",
                  "wsSettings": { "path": "/p{{i}}", "headers": { "Host": "h{{i}}.example.com" } } }
            },
            {
              "tag": "t", "protocol": "trojan",
              "settings": { "servers": [{
                  "address": "t{{i}}.example.com", "port": 443,
                  "password": "pw-{{i}}" }] },
              "streamSettings": { "network": "grpc", "security": "tls" }
            },
            { "tag": "direct", "protocol": "freedom", "settings": {} },
            { "tag": "block", "protocol": "blackhole", "settings": {} },
            { "tag": "dns-out", "protocol": "dns", "settings": {} }
          ]
        }
        """;

        var json = $"[{Config(1)},{Config(2)},{Config(3)}]";
        var list = ProxyOutboundFmt.Resolve(json, "sub");

        await list.Count.Should().BeEqualTo(6);

        var vless = list.Where(x => x.ConfigType == EConfigType.VLESS).ToList();
        var trojan = list.Where(x => x.ConfigType == EConfigType.Trojan).ToList();
        await vless.Count.Should().BeEqualTo(3);
        await trojan.Count.Should().BeEqualTo(3);

        // Every node keeps its own address, credentials and transport.
        await vless.Select(x => x.Address).Distinct().Count().Should().BeEqualTo(3);
        await vless.Select(x => x.Id).Distinct().Count().Should().BeEqualTo(3);
        await trojan.Select(x => x.Address).Distinct().Count().Should().BeEqualTo(3);

        // Remarks come from the owning config, so each group is labelled.
        await vless.Select(x => x.Remarks).Distinct().Count().Should().BeEqualTo(3);

        // ws / grpc transport survives per node, not just the first one.
        await vless.Select(x => x.Network).Distinct().Count().Should().BeEqualTo(1);
        await vless[0].Network.Should().BeEqualTo("ws");
        await trojan.Select(x => x.Network).Distinct().Count().Should().BeEqualTo(1);
        await trojan[0].Network.Should().BeEqualTo("grpc");

        // Each node keeps its own ws path and host.
        await vless.Select(x => x.Path).Distinct().Count().Should().BeEqualTo(3);
        await vless[0].Path.Should().BeEqualTo("/p1");
        await vless[1].Path.Should().BeEqualTo("/p2");
    }
}