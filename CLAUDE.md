# shelly-em-mcp - project journal

Running, dated log of decisions and state, for continuity across Claude Code sessions. See
[`README.md`](README.md) for user-facing docs and [`docs/DEVELOPER.md`](docs/DEVELOPER.md) for
build/run internals.

## Status update, 2026-08-22

Initial scaffold, built by mirroring the two sibling MCP projects in this workspace,
`fatsecret-mcp` and `fitbit-mcp` (solution layout, DI/bootstrap pattern, tool-attribute style,
testing approach, packaging/publish pipeline - copied close to verbatim where it made sense).

**Scope decisions, and why:**

- **Local-only (Shelly Gen2/Gen3 RPC), no Shelly Cloud** - the only device available to test
  against is a Shelly Pro 3EM on the local network; Cloud API support needs an account/auth key
  that hasn't been set up, and the user explicitly called it out as a *future* option, not part
  of this build. See [`docs/cloud-api.md`](docs/cloud-api.md) for the design sketch to pick up
  later.
- **No switch/relay control tool** - the Pro 3EM is measurement-only (no relay), so a
  `switch_control` tool would be entirely untested. Deferred until a relay-capable device (e.g.
  Plus 1PM) is available to verify against.
- **No `auth` CLI subcommand, no user-secrets** - unlike `fatsecret-mcp` (OAuth1) and
  `fitbit-mcp` (OAuth2/PKCE), local Shelly access needs no account or token exchange - just a
  device name + LAN IP, which isn't a secret. Config for that reason lives directly in
  `appsettings.json`/`appsettings.Development.json`, bound as a typed `List<ShellyDeviceOptions>`
  rather than the flat `IConfiguration` indexer reads the sibling projects use for OAuth
  key/secret pairs (a list doesn't fit that pattern cleanly).
- **RPC field names** (`EM.GetStatus`, `EMData.GetStatus`, `Shelly.GetStatus`,
  `Shelly.GetDeviceInfo`) were confirmed against the live Shelly Gen2 API docs
  (`shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/{EM,EMData}`), not guessed - see the
  doc-comment citations in `src/ShellyEmMcp/Devices/ShellyRpcClient.cs` and
  `src/ShellyEmMcp/Tools/*.cs`. Not yet exercised against the real Pro 3EM in this session - see
  Next steps.
- A Python reference plugin, [`game4automation/shelly`](https://github.com/game4automation/shelly)
  (a DeskAgent plugin, different host entirely), was useful for cross-checking the tool taxonomy
  (what questions people actually want answered about a home energy meter) and confirming field
  names independently, but not copied for code style - this project follows the sibling .NET
  projects' conventions throughout.

**Built this session:** full solution scaffold (`ShellyEmMcp.slnx`, `Directory.Build.props`,
`Directory.Packages.props`, CI workflow, `.editorconfig`/`LICENSE` copied verbatim from
`fitbit-mcp`), `src/ShellyEmMcp/` (`Program.cs`, `Devices/` - `ShellyDeviceOptions`,
`ShellyRpcClient`, `ShellyDeviceLookup`, `ShellyJson`, `ServiceCollectionExtensions`, `Tools/` -
`DeviceTools` with `list_devices`/`get_status`, `EnergyTools` with
`get_power`/`get_energy_live`/`get_energy_totals`), and `tests/ShellyEmMcp.Tests/` (RPC client
URL-building/error tests, parsing-logic tests for both Tools classes against hand-written Gen2
JSON fixtures - 9 tests, all passing, no live device needed). `dotnet build` and `dotnet test`
both verified clean in this session.

## Status update, 2026-08-22 (continued - doc audit against the official Shelly API docs MCP server)

Checked the implementation against `https://shelly-api-docs.mcp.shelly.link/mcp` (an official
Shelly-hosted MCP server serving their live API docs - queried directly over HTTP JSON-RPC in
this session, since this Claude Code session couldn't reconnect mid-session after `claude mcp
add`ing it). Findings:

**Bug fixed:** `DeviceTools.ParseDeviceStatus` was reading `sys.temperature.tC` from
`Shelly.GetStatus`. Confirmed against the `Sys` component's status property list that `sys` has
**no `temperature` field on Gen2/Gen3 devices** - it was copied from the `game4automation/shelly`
Python reference plugin, which apparently got the same thing wrong (or was written against a
different device/generation). The Shelly Pro 3EM's own device page also lists no `Temperature`
component, so there is no temperature reading at all to surface for this project's target
hardware. `TemperatureC` was removed from `DeviceStatus` rather than left in as permanently-null
dead functionality.

**Gaps filled, all confirmed against `EM.mdx`/`EMData.mdx`/`Shelly.mdx`:**

- `get_energy_live` (`EM.GetStatus`) was only reading `{a,b,c}_voltage/current/act_power/pf` and
  `total_act_power/total_current`. Added apparent power (`aprt_power`), frequency (`freq`),
  neutral current (`n_current`), per-phase `errors`/`flags` arrays, aggregate `total_aprt_power`,
  and component-level `errors` (`phase_sequence`, `ct_type_not_set`, `power_meter_failure`) -
  genuinely useful diagnostics for a 3-phase installer, e.g. `phase_sequence` catches wiring the
  phases in the wrong order.
- New tool `get_energy_config` (`EM.GetConfig`) - CT type, phase-reversal flags, and the
  under/over alarm thresholds for voltage/current/power per phase. Nothing else surfaced this
  read-only diagnostic view before.
- New tool `get_energy_history` (`EMData.GetData`) - the Pro 3EM stores ~60 days of 1-minute
  interval data **on-device**, which the original plan incorrectly assumed needed Shelly Cloud to
  access at all (deferred as "requires either Shelly Cloud or local data-block pagination"). It
  turns out `EMData.GetData` with a `ts`/`end_ts` window is a single RPC call, not real pagination
  work - implemented as: sum each interval record's `{a,b,c}_total_act_energy` /
  `_total_act_ret_energy` values across the window. **Caveat, not yet verified live:** the docs'
  example shows these fields alongside min/max/avg power/voltage stats for the same 60-second
  interval, which reads as "this interval's delta" rather than "running total as of this
  interval" - that interpretation is inferred, not confirmed against a real response. If it turns
  out to be a running total instead, the fix is a one-line change (take the last record's value
  minus the first's, instead of summing) - see `EnergyTools.ParseEnergyHistory`'s doc comment.
- `list_devices` (`Shelly.GetDeviceInfo`) now also surfaces `app` (e.g. `"Pro3EM"`) and `auth_en`
  - the latter matters because if a device has local admin auth enabled, every RPC call from this
    project's `ShellyRpcClient` (no digest auth support) will fail; surfacing `auth_en: true`
    turns an opaque connection failure into an explainable one.

**Also learned, not yet acted on:**

- The Pro 3EM has two device profiles: default `triphase` (single combined `EM`/`EMData` at
  `em:0`/`emdata:0` - what this project assumes throughout) and `monophase` (three independent
  `EM1`/`EM1Data` instances per channel instead). If a device is ever configured in `monophase`
  mode, every tool in this project would get an empty/error response from `EM.GetStatus` id 0.
  Not handled - flagged in the README's Status table as a known limitation.
- `EMData` also has `ResetCounters`, `DeleteAllData`, `GetNetEnergies`, and a CSV download
  endpoint (`http://<ip>/emdata/0/data.csv?ts=..&end_ts=..`, an alternative to `GetData` that
  wasn't used here for consistency with the RPC-only client). None of these seemed valuable
  enough to add speculatively; revisit if a real need comes up.

## Next steps

- **Verify against the real Pro 3EM.** Nothing in this session touched the actual device (no
  network reachability from the environment this was built in) - `appsettings.Development.json`
  currently has a placeholder IP (`192.168.1.100`) that needs updating to the real one, then a
  manual pass through `list_devices`, `get_status`, `get_power`, `get_energy_live`,
  `get_energy_totals` per the plan's verification steps, to confirm the Gen2 field-name
  assumptions hold for the actual firmware version in use.
- No GitHub repo exists yet for this project - deliberately out of scope until credentials are
  sorted out (per explicit instruction). `git init`/remote/first-push is unstarted.
- Once verified live, revisit the Status table in `README.md` (currently marks live features
  "verified" preemptively based on doc-confirmed field names + passing unit tests, not an actual
  device call - should be corrected either way once tested).
