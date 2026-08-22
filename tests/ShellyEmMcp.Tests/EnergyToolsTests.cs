// SPDX-License-Identifier: MIT

using ShellyEmMcp.Tools;

namespace ShellyEmMcp.Tests;

[TestFixture]
public class EnergyToolsTests
{
    [Test]
    public void ParseEnergyLive_ReadsPerPhaseAndTotalFields()
    {
        const string rawJson = """
        {
          "id": 0,
          "a_voltage": 231.2, "a_current": 2.1, "a_act_power": 480.5, "a_aprt_power": 482.0, "a_pf": 0.98, "a_freq": 50.0, "a_errors": [], "a_flags": [],
          "b_voltage": 230.8, "b_current": 1.4, "b_act_power": 320.1, "b_aprt_power": 321.0, "b_pf": 0.97, "b_freq": 50.0, "b_errors": [], "b_flags": ["overpower"],
          "c_voltage": 232.0, "c_current": 0.9, "c_act_power": 205.3, "c_aprt_power": 206.0, "c_pf": 0.96, "c_freq": 50.0, "c_errors": [], "c_flags": [],
          "n_current": 0.6,
          "total_current": 4.4, "total_act_power": 1005.9, "total_aprt_power": 1030.2,
          "errors": ["phase_sequence"]
        }
        """;

        var reading = EnergyTools.ParseEnergyLive(rawJson);

        Assert.That(reading.PhaseA.VoltageVolts, Is.EqualTo(231.2));
        Assert.That(reading.PhaseA.CurrentAmps, Is.EqualTo(2.1));
        Assert.That(reading.PhaseA.ActivePowerWatts, Is.EqualTo(480.5));
        Assert.That(reading.PhaseA.ApparentPowerVoltAmps, Is.EqualTo(482.0));
        Assert.That(reading.PhaseA.PowerFactor, Is.EqualTo(0.98));
        Assert.That(reading.PhaseA.FrequencyHz, Is.EqualTo(50.0));
        Assert.That(reading.PhaseA.Flags, Is.Empty);
        Assert.That(reading.PhaseB.Flags, Is.EqualTo(new[] { "overpower" }));
        Assert.That(reading.NeutralCurrentAmps, Is.EqualTo(0.6));
        Assert.That(reading.TotalActivePowerWatts, Is.EqualTo(1005.9));
        Assert.That(reading.TotalApparentPowerVoltAmps, Is.EqualTo(1030.2));
        Assert.That(reading.TotalCurrentAmps, Is.EqualTo(4.4));
        Assert.That(reading.ComponentErrors, Is.EqualTo(new[] { "phase_sequence" }));
    }

    [Test]
    public void ParseEnergyLive_MissingFields_ReturnNullsAndEmptyArrays()
    {
        var reading = EnergyTools.ParseEnergyLive("{}");

        Assert.That(reading.PhaseA.VoltageVolts, Is.Null);
        Assert.That(reading.PhaseA.Errors, Is.Empty);
        Assert.That(reading.PhaseA.Flags, Is.Empty);
        Assert.That(reading.TotalActivePowerWatts, Is.Null);
        Assert.That(reading.ComponentErrors, Is.Empty);
    }

    [Test]
    public void ParseEnergyTotals_ReadsPerPhaseAndAggregateCounters()
    {
        const string rawJson = """
        {
          "id": 0,
          "a_total_act_energy": 152300.5, "a_total_act_ret_energy": 0,
          "b_total_act_energy": 98120.2, "b_total_act_ret_energy": 12.5,
          "c_total_act_energy": 61044.9, "c_total_act_ret_energy": 0,
          "total_act": 311465.6, "total_act_ret": 12.5
        }
        """;

        var totals = EnergyTools.ParseEnergyTotals(rawJson);

        Assert.That(totals.PhaseA, Is.EqualTo(new EnergyPhaseTotals(152300.5, 0)));
        Assert.That(totals.PhaseB, Is.EqualTo(new EnergyPhaseTotals(98120.2, 12.5)));
        Assert.That(totals.TotalActiveEnergyWh, Is.EqualTo(311465.6));
        Assert.That(totals.TotalActiveReturnedEnergyWh, Is.EqualTo(12.5));
    }

    [Test]
    public void ParseEnergyConfig_ReadsCtTypeReverseAndAlarmThresholds()
    {
        const string rawJson = """
        {
          "id": 0, "name": null, "phase_selector": "a", "monitor_phase_sequence": true,
          "reverse": { "b": true },
          "ct_type": "120A",
          "alarms": {
            "a": { "voltage": [200, 250], "current": [1.1, 100], "power": [null, null] },
            "b": { "voltage": [null, null], "current": [10, 11000], "power": [null, null] }
          }
        }
        """;

        var config = EnergyTools.ParseEnergyConfig(rawJson);

        Assert.That(config.CtType, Is.EqualTo("120A"));
        Assert.That(config.PhaseSelector, Is.EqualTo("a"));
        Assert.That(config.MonitorPhaseSequence, Is.True);
        Assert.That(config.ReverseA, Is.False);
        Assert.That(config.ReverseB, Is.True);
        Assert.That(config.ReverseC, Is.False);
        Assert.That(config.AlarmsA, Is.EqualTo(new EnergyPhaseAlarms(200, 250, 1.1, 100, null, null)));
        Assert.That(config.AlarmsB!.CurrentUnder, Is.EqualTo(10));
        Assert.That(config.AlarmsC, Is.Null);
    }

    [Test]
    public void ParseEnergyConfig_NoAlarmsOrReverse_ReturnsNullsAndFalseFlags()
    {
        var config = EnergyTools.ParseEnergyConfig("""{ "id": 0 }""");

        Assert.That(config.ReverseA, Is.False);
        Assert.That(config.AlarmsA, Is.Null);
    }

    [Test]
    public void ParseEnergyHistory_SumsPerIntervalEnergyAcrossRecords()
    {
        const string rawJson = """
        {
          "keys": ["a_total_act_energy", "a_total_act_ret_energy", "b_total_act_energy", "b_total_act_ret_energy", "c_total_act_energy", "c_total_act_ret_energy"],
          "data": [
            { "ts": 1000, "period": 60, "values": [[10.0, 0, 8.0, 0, 5.0, 0], [12.0, 1.5, 9.0, 0, 5.5, 0]] }
          ]
        }
        """;

        var summary = EnergyTools.ParseEnergyHistory(rawJson, requestedHours: 24);

        Assert.That(summary.RecordCount, Is.EqualTo(2));
        Assert.That(summary.Truncated, Is.False);
        Assert.That(summary.PhaseAEnergyWh, Is.EqualTo(22.0));
        Assert.That(summary.PhaseAReturnedEnergyWh, Is.EqualTo(1.5));
        Assert.That(summary.TotalEnergyWh, Is.EqualTo(22.0 + 17.0 + 10.5));
        Assert.That(summary.TotalReturnedEnergyWh, Is.EqualTo(1.5));
    }

    [Test]
    public void ParseEnergyHistory_NextRecordTsPresent_MarksTruncated()
    {
        const string rawJson = """
        {
          "keys": ["a_total_act_energy"],
          "data": [{ "ts": 1000, "period": 60, "values": [[1.0]] }],
          "next_record_ts": 1060
        }
        """;

        var summary = EnergyTools.ParseEnergyHistory(rawJson, requestedHours: 24);

        Assert.That(summary.Truncated, Is.True);
    }

    [Test]
    public void ParseEnergyHistory_NoData_ReturnsZeroesNotTruncated()
    {
        var summary = EnergyTools.ParseEnergyHistory("""{ "keys": [], "data": [] }""", requestedHours: 1);

        Assert.That(summary.RecordCount, Is.EqualTo(0));
        Assert.That(summary.Truncated, Is.False);
        Assert.That(summary.TotalEnergyWh, Is.EqualTo(0));
    }
}
