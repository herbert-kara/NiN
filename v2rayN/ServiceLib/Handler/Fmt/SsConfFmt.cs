using YamlDotNet.RepresentationModel;

namespace ServiceLib.Handler.Fmt;

/// <summary>
/// Outline "dynamic access key" import (ssconf://host/path#name).
/// The link itself carries no cipher data: fetching it returns either an ss://
/// share link or an outline-sdk YAML document, so the key has to be resolved
/// over the network before it can become a profile.
/// </summary>
public class SsConfFmt
{
    private static readonly string _tag = "SsConfFmt";

    public static bool IsSsConf(string? str)
    {
        return str is not null && str.TrimEx().StartsWith(Global.SsConfProtocol, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ssconf://host/path#name is fetched over https; the fragment is only the
    /// key's display name and must not be sent to the server.
    /// </summary>
    public static string ToHttpUrl(string ssconfUrl)
    {
        var rest = ssconfUrl.TrimEx()[Global.SsConfProtocol.Length..];
        var hash = rest.IndexOf('#');
        if (hash >= 0)
        {
            rest = rest[..hash];
        }
        return Global.HttpsProtocol + rest;
    }

    public static string GetKeyName(string ssconfUrl)
    {
        var hash = ssconfUrl.IndexOf('#');
        return hash < 0 ? string.Empty : Utils.UrlDecode(ssconfUrl[(hash + 1)..]);
    }

    /// <summary>
    /// Resolves the key over https. An async method cannot carry an out parameter,
    /// so the message rides along with the item.
    /// </summary>
    public static async Task<(ProfileItem? Item, string Msg)> ResolveAsync(string ssconfUrl)
    {
        var msg = ResUI.ConfigurationFormatIncorrect;

        var body = await HttpClientHelper.Instance.TryGetAsync(ToHttpUrl(ssconfUrl));
        if (body.IsNullOrEmpty())
        {
            return (null, ResUI.FailedReadConfiguration);
        }

        var text = body.TrimEx();
        ProfileItem? item;
        if (text.StartsWith(Global.ProtocolShares[EConfigType.Shadowsocks], StringComparison.OrdinalIgnoreCase))
        {
            item = ShadowsocksFmt.Resolve(text, out msg);
        }
        else
        {
            item = ResolveOutlineKey(text, out msg);
        }
        if (item is null)
        {
            return (null, msg);
        }

        var name = GetKeyName(ssconfUrl);
        if (name.IsNotEmpty())
        {
            item.Remarks = name;
        }
        else if (item.Remarks.IsNullOrEmpty())
        {
            item.Remarks = "outline";
        }
        return (item, msg);
    }

    /// <summary>
    /// outline-sdk access key, e.g.
    /// transport: { $type: tcpudp, tcp: { $type: shadowsocks, endpoint: host:443,
    /// cipher: chacha20-ietf-poly1305, secret: ..., prefix: "POST%20abc" } }
    /// The optional `prefix` is camouflage that the server tolerates on its own
    /// (verified against a live key), so it is not modelled here.
    /// </summary>
    public static ProfileItem? ResolveOutlineKey(string yaml, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        var node = FindShadowsocksNode(yaml);
        if (node is null)
        {
            msg = ResUI.NonvmessOrssProtocol;
            return null;
        }

        var endpoint = node.GetValueOrDefault("endpoint");
        var cipher = node.GetValueOrDefault("cipher");
        var secret = node.GetValueOrDefault("secret");
        if (endpoint.IsNullOrEmpty() || cipher.IsNullOrEmpty() || secret.IsNullOrEmpty())
        {
            return null;
        }
        if (!TrySplitEndpoint(endpoint, out var host, out var port))
        {
            return null;
        }

        var item = new ProfileItem
        {
            ConfigType = EConfigType.Shadowsocks,
            Address = host,
            Port = port,
            Password = secret,
        };
        item.SetProtocolExtra(item.GetProtocolExtra() with { SsMethod = cipher });

        var name = node.GetValueOrDefault("name");
        if (name.IsNotEmpty())
        {
            item.Remarks = name;
        }
        return item;
    }

    private static bool TrySplitEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var idx = endpoint.LastIndexOf(':');
        if (idx <= 0 || idx == endpoint.Length - 1)
        {
            return false;
        }
        port = endpoint[(idx + 1)..].ToInt();
        host = endpoint[..idx].Trim('[', ']');
        return host.IsNotEmpty() && port is > 0 and <= 65535;
    }

    private static Dictionary<string, string>? FindShadowsocksNode(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            foreach (var document in stream.Documents)
            {
                if (FindShadowsocksNode(document.RootNode) is { } found)
                {
                    return found;
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed key is a user error, not a crash: report it as such.
            Logging.SaveLog(_tag, ex);
        }
        return null;
    }

    private static Dictionary<string, string>? FindShadowsocksNode(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                {
                    var children = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in mapping.Children)
                    {
                        children[pair.Key.ToString()] = pair.Value is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty;
                    }
                    if (children.TryGetValue("$type", out var type) && type == "shadowsocks")
                    {
                        return children;
                    }
                    foreach (var pair in mapping.Children)
                    {
                        if (FindShadowsocksNode(pair.Value) is { } found)
                        {
                            return found;
                        }
                    }
                    break;
                }
            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                {
                    if (FindShadowsocksNode(child) is { } found)
                    {
                        return found;
                    }
                }
                break;
        }
        return null;
    }
}