using System.Text.Json.Nodes;

namespace ServiceLib.Handler.Fmt;

/// <summary>
/// Turns the proxy outbound of a full Xray config into a normal profile.
/// </summary>
/// <remarks>
/// Some subscriptions return an array of complete Xray configs, each carrying its
/// own inbounds, routing, DNS and four outbounds. Importing those verbatim stores
/// them as an opaque custom file: one blob per entry, no address, no server
/// country and no per-node test. Since exactly one outbound is the real proxy,
/// that config is equivalent to a single VLESS/Trojan line, so we convert the proxy
/// outbound into a first-class profile instead. Everything else in the file
/// (dns, routing, the freedom/blackhole/dns outbounds) belongs to the provider's
/// own client setup and is not part of the node's identity.
/// </remarks>
public static class ProxyOutboundFmt
{
    /// <summary>Outbounds that describe the client's own plumbing, not a node.</summary>
    private static readonly HashSet<string> NonNodeProtocols =
        ["freedom", "blackhole", "dns", "loopback"];

    /// <summary>Protocols this converter knows how to express as a normal profile.</summary>
    private static readonly Dictionary<string, EConfigType> Supported = new()
    {
        ["vless"] = EConfigType.VLESS,
        ["vmess"] = EConfigType.VMess,
        ["trojan"] = EConfigType.Trojan,
        ["shadowsocks"] = EConfigType.Shadowsocks,
    };

    /// <summary>
    /// Extracts testable profiles from a payload of full Xray configs.
    /// </summary>
    /// <returns>
    /// One profile per proxy outbound, or an empty list when the payload holds
    /// nothing this converter understands (the caller then falls back to the
    /// existing Custom import).
    /// </returns>
    public static List<ProfileItem> Resolve(string strData, string? subRemarks)
    {
        var jsonNode = JsonUtils.ParseJson(strData);
        return ResolveCommon(jsonNode, subRemarks);
    }

    private static List<ProfileItem> ResolveCommon(JsonNode? jsonNode, string? subRemarks)
    {
        var result = new List<ProfileItem>();
        if (jsonNode is JsonArray array)
        {
            foreach (var item in array)
            {
                result.AddRange(ResolveCommon(item, subRemarks));
            }
            return result;
        }

        if (jsonNode is not JsonObject obj)
        {
            return result;
        }

        // A bare outbound (no inbounds) is already handled by ResolveOutbound.
        if (obj["outbounds"] is not JsonArray outbounds)
        {
            return result;
        }

        foreach (var node in outbounds)
        {
            if (node is not JsonObject outboundObj)
            {
                continue;
            }

            var protocol = outboundObj["protocol"]?.ToString();
            if (protocol is null || NonNodeProtocols.Contains(protocol))
            {
                continue;
            }
            if (!Supported.TryGetValue(protocol, out var configType))
            {
                continue;
            }
            if (TryBuild(outboundObj, configType, obj["remarks"]?.ToString() ?? subRemarks, out var profile))
            {
                result.Add(profile);
            }
        }

        return result;
    }

    private static bool TryBuild(JsonObject outbound, EConfigType configType, string? fallbackRemarks, out ProfileItem profile)
    {
        profile = new ProfileItem();

        // Servers live under vnext (vmess/vless) or servers (trojan/ss).
        var servers = outbound["settings"]?["vnext"] ?? outbound["settings"]?["servers"];
        if (servers is not JsonArray serverArray || serverArray.Count == 0)
        {
            return false;
        }
        if (serverArray[0] is not JsonObject server)
        {
            return false;
        }

        var address = server["address"]?.ToString();
        if (address.IsNullOrEmpty())
        {
            return false;
        }
        if (server["port"]?.ToInt() is not int port or <= 0 or >= 65536)
        {
            return false;
        }

        var users = server["users"] ?? server["user"];
        if (users is not JsonArray userArray || userArray.Count == 0 || userArray[0] is not JsonObject user)
        {
            return false;
        }

        var id = user["id"]?.ToString() ?? string.Empty;
        var password = user["password"]?.ToString() ?? string.Empty;

        profile.ConfigType = configType;
        profile.Address = address.TrimEx();
        profile.Port = port;
        profile.Remarks = SanitiseRemarks(outbound["tag"]?.ToString(), fallbackRemarks);
        profile.Network = string.Empty;

        switch (configType)
        {
            case EConfigType.VLESS:
                if (id.IsNullOrEmpty())
                {
                    return false;
                }
                profile.Password = id;
                var flow = user["flow"]?.ToString();
                if (flow.IsNotEmpty())
                {
                    profile.SetProtocolExtra(new ProtocolExtraItem { Flow = flow });
                }
                break;

            case EConfigType.VMess:
                if (id.IsNullOrEmpty())
                {
                    return false;
                }
                profile.Password = id;
                break;

            case EConfigType.Trojan:
                if (password.IsNullOrEmpty())
                {
                    return false;
                }
                profile.Password = password;
                break;

            case EConfigType.Shadowsocks:
                var method = user["method"]?.ToString();
                if (password.IsNullOrEmpty() || method.IsNullOrEmpty())
                {
                    return false;
                }
                profile.Password = password;
                profile.SetProtocolExtra(new ProtocolExtraItem { SsMethod = method });
                break;
        }

        ApplyStream(outbound["streamSettings"] as JsonObject, profile);
        return true;
    }

    /// <summary>Copies transport and TLS settings onto the profile.</summary>
    private static void ApplyStream(JsonObject? stream, ProfileItem profile)
    {
        if (stream is null)
        {
            return;
        }

        var network = stream["network"]?.ToString();
        if (network.IsNotEmpty())
        {
            profile.Network = network;
        }

        var transport = new TransportExtraItem();

        // Free-form transport options (raw header type) live outside ws/grpc;
        // without them those nodes connect but behave oddly.
        var rawHeaderType = stream["rawHeader"]?["header"]?["type"]?.ToString()
            ?? stream["tcpSettings"]?["header"]?["type"]?.ToString();
        if (rawHeaderType.IsNotEmpty())
        {
            transport.RawHeaderType = rawHeaderType;
        }

        var security = stream["security"]?.ToString();
        if (security.IsNotEmpty() && security != "none")
        {
            profile.StreamSecurity = security;
        }

        var tls = stream["tlsSettings"] as JsonObject;
        var reality = stream["realitySettings"] as JsonObject;
        var serverName = tls?["serverName"]?.ToString();
        if (serverName.IsNotEmpty())
        {
            profile.Sni = serverName;
        }
        if (tls?["allowInsecure"]?.ToBool() == true)
        {
            profile.AllowInsecure = Global.StringTrue;
        }

        if (reality is not null)
        {
            // REALITY needs its public key, or the profile fails validation.
            var publicKey = reality["publicKey"]?.ToString();
            if (publicKey.IsNotEmpty())
            {
                profile.PublicKey = publicKey;
                profile.Sni = reality["serverName"]?.ToString() ?? profile.Sni;
            }
            profile.ShortId = reality["shortId"]?.ToString() ?? string.Empty;
            profile.SpiderX = reality["spiderX"]?.ToString() ?? string.Empty;
            var fingerprint = reality["fingerprint"]?.ToString();
            if (fingerprint.IsNotEmpty())
            {
                profile.Fingerprint = fingerprint;
            }
        }

        var ws = stream["wsSettings"] as JsonObject;
        if (ws is not null)
        {
            transport.Host = ws["headers"]?["Host"]?.ToString() ?? serverName ?? string.Empty;
            transport.Path = ws["path"]?.ToString() ?? string.Empty;
        }
        else if (serverName.IsNotEmpty())
        {
            // A non-ws transport still needs the host carried for TLS/SNI.
            transport.Host = serverName;
        }

        var grpc = stream["grpcSettings"] as JsonObject;
        if (grpc is not null)
        {
            transport.GrpcAuthority = grpc["authority"]?.ToString() ?? string.Empty;
            transport.GrpcServiceName = grpc["serviceName"]?.ToString() ?? string.Empty;
            transport.GrpcMode = grpc["multiMode"]?.ToBool() == true ? "multi" : "gun";
        }

        profile.SetTransportExtra(transport);
    }

    /// <summary>
    /// Prefers the human remark from the enclosing config, since a provider
    /// usually names the node there ("1. VLESS - Domain : 443") while the
    /// outbound tag is just "proxy".
    /// </summary>
    private static string SanitiseRemarks(string? tag, string? fallback)
    {
        var remarks = fallback.IsNotEmpty() ? fallback : tag;
        return remarks.IsNullOrEmpty() ? "imported" : remarks.TrimEx();
    }
}