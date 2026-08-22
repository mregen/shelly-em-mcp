// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ShellyEmMcp.Devices;
using static ShellyEmMcp.Devices.ShellyJson;

namespace ShellyEmMcp.Tools;

[McpServerToolType]
public class DeviceTools(IReadOnlyList<ShellyDeviceOptions> devices, ShellyRpcClient rpc)
{
    [McpServerTool(Name = "list_devices")]
    [Description("List all configured Shelly devices with reachability, model, generation, firmware, app, and " +
        "local-auth status.")]
    public async Task<string> ListDevices(CancellationToken cancellationToken = default)
    {
        var results = new List<DeviceInfo>();
        foreach (var device in devices)
        {
            results.Add(await DescribeAsync(device, cancellationToken));
        }

        return JsonSerializer.Serialize(results);
    }

    [McpServerTool(Name = "get_status")]
    [Description("Get system status (uptime, cloud connectivity) for one configured Shelly device, or all " +
        "configured devices if none is specified.")]
    public async Task<string> GetStatus(
        [Description("Configured device name; omit to get status for all devices")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (device is not null)
        {
            var target = ShellyDeviceLookup.Find(devices, device);
            if (target is null)
            {
                return $"No configured Shelly device named '{device}'.";
            }

            return JsonSerializer.Serialize(await GetStatusAsync(target, cancellationToken));
        }

        var results = new List<DeviceStatus>();
        foreach (var d in devices)
        {
            results.Add(await GetStatusAsync(d, cancellationToken));
        }

        return JsonSerializer.Serialize(results);
    }

    private async Task<DeviceInfo> DescribeAsync(ShellyDeviceOptions device, CancellationToken cancellationToken)
    {
        try
        {
            var rawJson = await rpc.CallAsync(device.Host, "Shelly.GetDeviceInfo", cancellationToken: cancellationToken);
            return ParseDeviceInfo(device, rawJson);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DeviceInfo(device.Name, device.Host, Reachable: false, Model: null, Generation: null, Firmware: null, App: null, AuthEnabled: null, Error: ex.Message);
        }
    }

    /// <summary>
    /// Parses a Shelly.GetDeviceInfo response (https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/Shelly)
    /// - model/gen/fw_id/app for identification, auth_en to surface whether local RPC calls need digest auth
    /// this client doesn't implement (see ShellyRpcClient's doc comment).
    /// </summary>
    internal static DeviceInfo ParseDeviceInfo(ShellyDeviceOptions device, string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        return new DeviceInfo(
            device.Name,
            device.Host,
            Reachable: true,
            Model: GetString(root, "model"),
            Generation: GetInt(root, "gen"),
            Firmware: GetString(root, "fw_id"),
            App: GetString(root, "app"),
            AuthEnabled: GetBool(root, "auth_en"),
            Error: null);
    }

    private async Task<DeviceStatus> GetStatusAsync(ShellyDeviceOptions device, CancellationToken cancellationToken)
    {
        try
        {
            var rawJson = await rpc.CallAsync(device.Host, "Shelly.GetStatus", cancellationToken: cancellationToken);
            return ParseDeviceStatus(device, rawJson);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DeviceStatus(device.Name, device.Host, Online: false, UptimeSeconds: null, CloudConnected: null, Error: ex.Message);
        }
    }

    /// <summary>
    /// Parses a Shelly.GetStatus response - sys.uptime for system health, cloud.connected for connectivity, per
    /// the Gen2 System and Cloud component docs. The `sys` object has no `temperature` field on Gen2/Gen3
    /// devices (confirmed against the System component's status property list) and the Shelly Pro 3EM exposes
    /// no Temperature component either (confirmed against its device page's component list) - so unlike some
    /// other Shelly devices, there is no device temperature to report here for this project's target hardware.
    /// </summary>
    internal static DeviceStatus ParseDeviceStatus(ShellyDeviceOptions device, string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var uptimeSeconds = GetObject(root, "sys") is { } sys ? GetInt(sys, "uptime") : null;
        var cloudConnected = GetObject(root, "cloud") is { } cloud ? GetBool(cloud, "connected") : null;

        return new DeviceStatus(device.Name, device.Host, Online: true, uptimeSeconds, cloudConnected, Error: null);
    }
}

public sealed record DeviceInfo(string Name, string Host, bool Reachable, string? Model, int? Generation, string? Firmware, string? App, bool? AuthEnabled, string? Error);

public sealed record DeviceStatus(string Name, string Host, bool Online, int? UptimeSeconds, bool? CloudConnected, string? Error);
