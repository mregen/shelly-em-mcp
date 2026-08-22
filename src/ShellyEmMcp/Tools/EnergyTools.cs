// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using ShellyEmMcp.Devices;
using static ShellyEmMcp.Devices.ShellyJson;

namespace ShellyEmMcp.Tools;

[McpServerToolType]
public class EnergyTools(IReadOnlyList<ShellyDeviceOptions> devices, ShellyRpcClient rpc)
{
    [McpServerTool(Name = "get_power")]
    [Description("Get current total active power (W) for one Shelly device, or all configured devices plus a " +
        "combined total if none is specified.")]
    public async Task<string> GetPower(
        [Description("Configured device name; omit to get power for all devices")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (device is not null)
        {
            var target = ShellyDeviceLookup.Find(devices, device);
            if (target is null)
            {
                return $"No configured Shelly device named '{device}'.";
            }

            return JsonSerializer.Serialize(await GetPowerAsync(target, cancellationToken));
        }

        var readings = new List<PowerReading>();
        foreach (var d in devices)
        {
            readings.Add(await GetPowerAsync(d, cancellationToken));
        }

        var totalWatts = readings.Where(r => r.Online).Sum(r => r.ActivePowerWatts ?? 0);
        return JsonSerializer.Serialize(new { devices = readings, totalActivePowerWatts = totalWatts });
    }

    [McpServerTool(Name = "get_energy_live")]
    [Description("Get real-time per-phase measurements (voltage, current, active/apparent power, power factor, " +
        "frequency, and any active error/alarm flags for phases A/B/C, plus neutral current and totals) for a " +
        "Shelly EM-class device (e.g. Pro 3EM).")]
    public async Task<string> GetEnergyLive(
        [Description("Configured device name")] string device,
        CancellationToken cancellationToken = default)
    {
        var target = ShellyDeviceLookup.Find(devices, device);
        if (target is null)
        {
            return $"No configured Shelly device named '{device}'.";
        }

        var rawJson = await rpc.CallAsync(target.Host, "EM.GetStatus", new Dictionary<string, string> { ["id"] = "0" }, cancellationToken);
        return JsonSerializer.Serialize(ParseEnergyLive(rawJson));
    }

    [McpServerTool(Name = "get_energy_totals")]
    [Description("Get cumulative energy counters (Wh consumed and returned, per phase and total) for a Shelly " +
        "EM-class device (e.g. Pro 3EM) since its counters were last reset.")]
    public async Task<string> GetEnergyTotals(
        [Description("Configured device name")] string device,
        CancellationToken cancellationToken = default)
    {
        var target = ShellyDeviceLookup.Find(devices, device);
        if (target is null)
        {
            return $"No configured Shelly device named '{device}'.";
        }

        var rawJson = await rpc.CallAsync(target.Host, "EMData.GetStatus", new Dictionary<string, string> { ["id"] = "0" }, cancellationToken);
        return JsonSerializer.Serialize(ParseEnergyTotals(rawJson));
    }

    [McpServerTool(Name = "get_energy_config")]
    [Description("Get the CT type, phase-reversal settings, and alarm thresholds (under/over voltage, current, " +
        "power per phase) configured on a Shelly EM-class device (e.g. Pro 3EM) - useful for explaining why an " +
        "alarm webhook fired, or checking whether the current transformer type is set.")]
    public async Task<string> GetEnergyConfig(
        [Description("Configured device name")] string device,
        CancellationToken cancellationToken = default)
    {
        var target = ShellyDeviceLookup.Find(devices, device);
        if (target is null)
        {
            return $"No configured Shelly device named '{device}'.";
        }

        var rawJson = await rpc.CallAsync(target.Host, "EM.GetConfig", new Dictionary<string, string> { ["id"] = "0" }, cancellationToken);
        return JsonSerializer.Serialize(ParseEnergyConfig(rawJson));
    }

    [McpServerTool(Name = "get_energy_history")]
    [Description("Get net energy per phase over a recent time window, broken into buckets, from the device's own " +
        "on-device history - Shelly Pro 3EM stores about 60 days locally, so this needs no Shelly Cloud account. " +
        "A negative bucket value means that phase exported more than it consumed during that bucket (e.g. solar " +
        "feed-in exceeding load); for a plain load-only meter, net energy equals energy consumed.")]
    public async Task<string> GetEnergyHistory(
        [Description("Configured device name")] string device,
        [Description("How many hours back from now to cover (default 24, max 168 = 7 days)")] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        var target = ShellyDeviceLookup.Find(devices, device);
        if (target is null)
        {
            return $"No configured Shelly device named '{device}'.";
        }

        hours = Math.Clamp(hours, 1, 168);

        // Bucket width chosen so a single EMData.GetNetEnergies call comfortably covers the requested window
        // without the device needing to chunk the response (empirically, raw per-minute EMData.GetData chunks
        // after only ~6 records even for a 1-hour window - see EnergyTools' class-level notes - so this tool
        // uses the device's own period-aggregation instead of summing raw per-minute records).
        var periodSeconds = hours switch
        {
            <= 6 => 300,
            <= 24 => 900,
            _ => 3600,
        };

        var endTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var startTs = ((endTs - (hours * 3600L)) / periodSeconds) * periodSeconds;

        var rawJson = await rpc.CallAsync(
            target.Host,
            "EMData.GetNetEnergies",
            new Dictionary<string, string>
            {
                ["id"] = "0",
                ["ts"] = startTs.ToString(CultureInfo.InvariantCulture),
                ["period"] = periodSeconds.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);

        return JsonSerializer.Serialize(ParseEnergyHistory(rawJson, hours, periodSeconds));
    }

    private async Task<PowerReading> GetPowerAsync(ShellyDeviceOptions device, CancellationToken cancellationToken)
    {
        try
        {
            var rawJson = await rpc.CallAsync(device.Host, "EM.GetStatus", new Dictionary<string, string> { ["id"] = "0" }, cancellationToken);
            using var doc = JsonDocument.Parse(rawJson);
            return new PowerReading(device.Name, device.Host, Online: true, GetDouble(doc.RootElement, "total_act_power"), Error: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PowerReading(device.Name, device.Host, Online: false, ActivePowerWatts: null, ex.Message);
        }
    }

    /// <summary>
    /// Parses an EM.GetStatus response (https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/EM) into
    /// per-phase measurements. Field names confirmed against the Gen2 EM component docs: per-phase
    /// {a,b,c}_voltage/current/act_power/aprt_power/pf/freq/errors/flags, neutral n_current, aggregate
    /// total_act_power/total_aprt_power/total_current, and component-level errors (e.g. `phase_sequence` -
    /// wiring order wrong on a 3-phase install, `ct_type_not_set`, `power_meter_failure`).
    /// </summary>
    internal static EnergyLiveReading ParseEnergyLive(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        return new EnergyLiveReading(
            ReadPhase(root, "a"),
            ReadPhase(root, "b"),
            ReadPhase(root, "c"),
            GetDouble(root, "n_current"),
            GetDouble(root, "total_current"),
            GetDouble(root, "total_act_power"),
            GetDouble(root, "total_aprt_power"),
            ReadStringArray(root, "errors"));

        static EnergyPhase ReadPhase(JsonElement root, string prefix) => new(
            GetDouble(root, $"{prefix}_voltage"),
            GetDouble(root, $"{prefix}_current"),
            GetDouble(root, $"{prefix}_act_power"),
            GetDouble(root, $"{prefix}_aprt_power"),
            GetDouble(root, $"{prefix}_pf"),
            GetDouble(root, $"{prefix}_freq"),
            ReadStringArray(root, $"{prefix}_errors"),
            ReadStringArray(root, $"{prefix}_flags"));
    }

    /// <summary>
    /// Parses an EMData.GetStatus response (https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/EMData)
    /// into cumulative energy counters. Field names confirmed against the Gen2 EMData component docs: per-phase
    /// {a,b,c}_total_act_energy/total_act_ret_energy, aggregate total_act/total_act_ret (all Wh).
    /// </summary>
    internal static EnergyTotalsReading ParseEnergyTotals(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        return new EnergyTotalsReading(
            ReadPhase(root, "a"),
            ReadPhase(root, "b"),
            ReadPhase(root, "c"),
            GetDouble(root, "total_act"),
            GetDouble(root, "total_act_ret"));

        static EnergyPhaseTotals ReadPhase(JsonElement root, string prefix) => new(
            GetDouble(root, $"{prefix}_total_act_energy"),
            GetDouble(root, $"{prefix}_total_act_ret_energy"));
    }

    /// <summary>
    /// Parses an EM.GetConfig response (https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/EM) -
    /// `reverse.{a,b,c}` are only present in the response when `true` (absent = not reversed), and each
    /// `alarms.{a,b,c}.{voltage,current,power}` is a `[under, over]` pair where either side may be `null` to
    /// disable that threshold, or the whole phase entry may be absent if no alarms are configured for it.
    /// </summary>
    internal static EnergyConfig ParseEnergyConfig(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        var reverse = GetObject(root, "reverse");
        var alarms = GetObject(root, "alarms");

        return new EnergyConfig(
            GetString(root, "name"),
            GetString(root, "ct_type"),
            GetString(root, "phase_selector"),
            GetBool(root, "monitor_phase_sequence"),
            ReverseFlag(reverse, "a"),
            ReverseFlag(reverse, "b"),
            ReverseFlag(reverse, "c"),
            ReadAlarms(alarms, "a"),
            ReadAlarms(alarms, "b"),
            ReadAlarms(alarms, "c"));

        static bool ReverseFlag(JsonElement? reverse, string phase) => reverse is { } r && (GetBool(r, phase) ?? false);

        static EnergyPhaseAlarms? ReadAlarms(JsonElement? alarms, string phase)
        {
            if (alarms is not { } a || GetObject(a, phase) is not { } p)
            {
                return null;
            }

            var (voltageUnder, voltageOver) = ReadThresholdPair(p, "voltage");
            var (currentUnder, currentOver) = ReadThresholdPair(p, "current");
            var (powerUnder, powerOver) = ReadThresholdPair(p, "power");
            return new EnergyPhaseAlarms(voltageUnder, voltageOver, currentUnder, currentOver, powerUnder, powerOver);
        }

        static (double? Under, double? Over) ReadThresholdPair(JsonElement phaseAlarms, string property)
        {
            if (!phaseAlarms.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return (null, null);
            }

            var items = value.EnumerateArray().ToArray();
            double? Read(int index) => items.Length > index && items[index].ValueKind == JsonValueKind.Number ? items[index].GetDouble() : null;
            return (Read(0), Read(1));
        }
    }

    /// <summary>
    /// Parses an EMData.GetNetEnergies response
    /// (https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/EMData) into one
    /// <see cref="EnergyHistoryBucket"/> per returned interval plus the summed totals across all of them.
    /// `{a,b,c}_net_act_energy` is each bucket's net Wh (consumed minus returned) for that period; a bucket's
    /// timestamp is `block.ts + index * block.period` (confirmed against the docs' `data[].ts`/`period` shape -
    /// `ts` is the first interval's start, not each record's). Confirmed live against a Shelly Pro 3EM: a
    /// `period=3600` (hourly) request for the last 24 hours returned all 24 buckets in a single response with no
    /// `next_record_ts`, unlike the raw per-minute `EMData.GetData` endpoint, which chunked after only ~6 records
    /// even for a 1-hour window on the same device - this is why history uses net-energy buckets instead of
    /// summing raw per-minute records. `next_record_ts` being present means the response was still chunked and
    /// doesn't cover the full window.
    /// </summary>
    internal static EnergyHistorySummary ParseEnergyHistory(string rawJson, int requestedHours, int bucketSeconds)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var keys = root.TryGetProperty("keys", out var keysEl) && keysEl.ValueKind == JsonValueKind.Array
            ? keysEl.EnumerateArray().Select(k => k.GetString() ?? string.Empty).ToList()
            : [];

        var aIndex = keys.IndexOf("a_net_act_energy");
        var bIndex = keys.IndexOf("b_net_act_energy");
        var cIndex = keys.IndexOf("c_net_act_energy");

        var buckets = new List<EnergyHistoryBucket>();
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in dataEl.EnumerateArray())
            {
                if (!block.TryGetProperty("ts", out var blockTsEl) || blockTsEl.ValueKind != JsonValueKind.Number
                    || !block.TryGetProperty("period", out var periodEl) || periodEl.ValueKind != JsonValueKind.Number
                    || !block.TryGetProperty("values", out var valuesEl) || valuesEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var blockTs = blockTsEl.GetInt64();
                var period = periodEl.GetInt64();
                var recordIndex = 0;

                foreach (var record in valuesEl.EnumerateArray())
                {
                    if (record.ValueKind == JsonValueKind.Array)
                    {
                        buckets.Add(new EnergyHistoryBucket(
                            blockTs + (recordIndex * period),
                            ReadIndexed(record, aIndex),
                            ReadIndexed(record, bIndex),
                            ReadIndexed(record, cIndex)));
                    }

                    recordIndex++;
                }
            }
        }

        var truncated = root.TryGetProperty("next_record_ts", out var nextTs) && nextTs.ValueKind == JsonValueKind.Number;

        return new EnergyHistorySummary(
            requestedHours,
            bucketSeconds,
            buckets.Count,
            truncated,
            buckets.Sum(b => b.PhaseANetEnergyWh),
            buckets.Sum(b => b.PhaseBNetEnergyWh),
            buckets.Sum(b => b.PhaseCNetEnergyWh),
            buckets);

        static double ReadIndexed(JsonElement record, int index) =>
            index >= 0 && record.GetArrayLength() > index && record[index].ValueKind == JsonValueKind.Number
                ? record[index].GetDouble()
                : 0;
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : [];
}

public sealed record PowerReading(string Name, string Host, bool Online, double? ActivePowerWatts, string? Error);

public sealed record EnergyPhase(
    double? VoltageVolts,
    double? CurrentAmps,
    double? ActivePowerWatts,
    double? ApparentPowerVoltAmps,
    double? PowerFactor,
    double? FrequencyHz,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Flags);

public sealed record EnergyLiveReading(
    EnergyPhase PhaseA,
    EnergyPhase PhaseB,
    EnergyPhase PhaseC,
    double? NeutralCurrentAmps,
    double? TotalCurrentAmps,
    double? TotalActivePowerWatts,
    double? TotalApparentPowerVoltAmps,
    IReadOnlyList<string> ComponentErrors);

public sealed record EnergyPhaseTotals(double? TotalActiveEnergyWh, double? TotalActiveReturnedEnergyWh);

public sealed record EnergyTotalsReading(EnergyPhaseTotals PhaseA, EnergyPhaseTotals PhaseB, EnergyPhaseTotals PhaseC, double? TotalActiveEnergyWh, double? TotalActiveReturnedEnergyWh);

public sealed record EnergyPhaseAlarms(double? VoltageUnder, double? VoltageOver, double? CurrentUnder, double? CurrentOver, double? PowerUnder, double? PowerOver);

public sealed record EnergyConfig(
    string? Name,
    string? CtType,
    string? PhaseSelector,
    bool? MonitorPhaseSequence,
    bool ReverseA,
    bool ReverseB,
    bool ReverseC,
    EnergyPhaseAlarms? AlarmsA,
    EnergyPhaseAlarms? AlarmsB,
    EnergyPhaseAlarms? AlarmsC);

public sealed record EnergyHistoryBucket(long UnixTimestamp, double PhaseANetEnergyWh, double PhaseBNetEnergyWh, double PhaseCNetEnergyWh);

public sealed record EnergyHistorySummary(
    int RequestedHours,
    int BucketSeconds,
    int BucketCount,
    bool Truncated,
    double PhaseANetEnergyWh,
    double PhaseBNetEnergyWh,
    double PhaseCNetEnergyWh,
    IReadOnlyList<EnergyHistoryBucket> Buckets)
{
    public double TotalNetEnergyWh => PhaseANetEnergyWh + PhaseBNetEnergyWh + PhaseCNetEnergyWh;
}
