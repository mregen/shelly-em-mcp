// SPDX-License-Identifier: MIT

using ShellyEmMcp.Devices;
using ShellyEmMcp.Tools;

namespace ShellyEmMcp.Tests;

[TestFixture]
public class DeviceToolsTests
{
    private static readonly ShellyDeviceOptions TestDevice = new("Hausanschluss", "192.168.1.100");

    [Test]
    public void ParseDeviceInfo_ReadsModelGenerationFirmwareAppAndAuth()
    {
        const string rawJson = """
        {
          "id": "shellypro3em-abc123", "model": "SPEM-003CEBEU", "gen": 2, "fw_id": "20250101-000000/1.0.0-g1234567",
          "ver": "1.0.0", "app": "Pro3EM", "auth_en": false, "auth_domain": null
        }
        """;

        var info = DeviceTools.ParseDeviceInfo(TestDevice, rawJson);

        Assert.That(info.Name, Is.EqualTo("Hausanschluss"));
        Assert.That(info.Reachable, Is.True);
        Assert.That(info.Model, Is.EqualTo("SPEM-003CEBEU"));
        Assert.That(info.Generation, Is.EqualTo(2));
        Assert.That(info.Firmware, Is.EqualTo("20250101-000000/1.0.0-g1234567"));
        Assert.That(info.App, Is.EqualTo("Pro3EM"));
        Assert.That(info.AuthEnabled, Is.False);
    }

    [Test]
    public void ParseDeviceStatus_ReadsUptimeAndCloudConnectivity()
    {
        const string rawJson = """
        {
          "sys": { "mac": "AABBCCDDEEFF", "uptime": 123456 },
          "cloud": { "connected": true }
        }
        """;

        var status = DeviceTools.ParseDeviceStatus(TestDevice, rawJson);

        Assert.That(status.Online, Is.True);
        Assert.That(status.UptimeSeconds, Is.EqualTo(123456));
        Assert.That(status.CloudConnected, Is.True);
    }

    [Test]
    public void ParseDeviceStatus_MissingSections_ReturnNulls()
    {
        var status = DeviceTools.ParseDeviceStatus(TestDevice, "{}");

        Assert.That(status.Online, Is.True);
        Assert.That(status.UptimeSeconds, Is.Null);
        Assert.That(status.CloudConnected, Is.Null);
    }
}
