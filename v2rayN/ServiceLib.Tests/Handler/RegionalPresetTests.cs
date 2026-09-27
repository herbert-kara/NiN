using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Handler;

public class RegionalPresetTests
{
    [Test]
    [Arguments(EPresetType.Default)]
    [Arguments(EPresetType.China)]
    [Arguments(EPresetType.Russia)]
    [Arguments(EPresetType.Iran)]
    public async Task ApplyRegionalPreset_ShouldKeepTheDnsSettings(EPresetType type)
    {
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        SQLiteHelper.Instance.CreateTable<RoutingItem>();
        var config = CoreConfigTestFactory.CreateConfig();
        var simpleDns = config.SimpleDNSItem;
        var simpleDnsBefore = JsonUtils.Serialize(simpleDns);
        var dnsItemsBefore = JsonUtils.Serialize(await AppManager.Instance.DNSItems());

        await ConfigHandler.ApplyRegionalPreset(config, type);

        await ReferenceEquals(config.SimpleDNSItem, simpleDns).Should().BeTrue();
        await JsonUtils.Serialize(config.SimpleDNSItem).Should().BeEqualTo(simpleDnsBefore);
        await JsonUtils.Serialize(await AppManager.Instance.DNSItems()).Should().BeEqualTo(dnsItemsBefore);
    }
}
