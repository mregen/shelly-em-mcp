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

## Status update, 2026-08-22 (continued - live verification against the real Pro 3EM)

Registered as an MCP server (`claude mcp add shelly-em-mcp -s local -e Shelly__Devices__0__Name=Hausanschluss -e Shelly__Devices__0__Host=192.168.1.11 -- ~/.dotnet/tools/shelly-em-mcp.exe`) and exercised against the real device for the first time.

**`get_energy_history` redesigned after live testing revealed a real problem**, not just a
docs-inferred one: it originally summed raw per-minute records from `EMData.GetData`. Live on the
Pro 3EM, that endpoint chunks *very* aggressively - a request for just the last 1 hour (60 records
expected) returned only 6 records before `next_record_ts` appeared. A 24-hour request would have
needed roughly 240 sequential RPC calls to fully page through, which the original implementation
didn't even attempt (it took the first, heavily-truncated page and reported wrong totals silently
correct-looking but covering only ~6 minutes instead of 24 hours).

Fixed by switching to `EMData.GetNetEnergies` instead, which aggregates on the device side into
period buckets (300/900/1800/3600s) rather than returning raw per-minute rows. Verified live: a
`period=900` (15-minute) request for the last 24 hours returned all 96 buckets in a single
response, `next_record_ts` absent. The tool now reports **net** energy per phase (consumed minus
returned) rather than separate consumed/returned totals - a real trade-off, but the practical
choice given the chunking behavior, and for a load-only meter (no solar) net equals consumption
anyway. Bucket width scales with the requested window (5 min for ≤6h, 15 min for ≤24h, 1h beyond
that) and `hours` is capped at 168 (7 days) rather than the original 1440 (60 days), since even
bucketed requests could still chunk for very wide windows and nothing this large has been tested.

Also confirmed live: `list_devices`/`Shelly.GetDeviceInfo` fields (model `SPEM-003CEBEU`, gen 2,
app `Pro3EM`, `auth_en: false`, `profile: "triphase"` - matches the assumed default profile).

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

## Status update, 2026-08-22 (continued - standalone `--dashboard` mode)

Added a third `Program.cs` mode, `--dashboard`, alongside stdio and `--http` - a plain browser
dashboard (live power widget + today's history chart), not an MCP server. Your idea: leave a
browser tab open to see at a glance whether there's PV surplus before starting something like the
dishwasher.

**Key design point, and why it stayed simple:** the dashboard's `/api/power` and
`/api/history/today` minimal-API endpoints don't reimplement any RPC or parsing logic - they
inject and call `EnergyTools.GetPower()`/`GetEnergyHistory()` directly (the same classes MCP's
`WithToolsFromAssembly()` exposes as tools), now also registered directly in DI via
`AddShellyClients()`. Two lines of new registration bought reuse of everything already built and
verified this session.

**Per your answers:** the live widget shows only the current net-power number (green/red by
sign), no per-appliance wattage threshold - and updates are plain browser polling (`fetch` every
~4s for power, ~60s for the chart), no SSE/WebSocket. Both were explicit simplicity choices, not
oversights - see the plan file's "What's explicitly out of scope for v1" if this needs revisiting.

**One real unknown resolved during verification:** `Microsoft.NET.Sdk.Web`'s `wwwroot` folder does
**not** get copied to `bin/{Debug,Release}/net10.0/` by a plain `dotnet build` - only by
`dotnet publish` (confirmed by running `dotnet publish` directly and inspecting the output; a
`dotnet run` from source still serves it correctly via ASP.NET Core's dev-time static web assets
pipeline, so local testing wasn't affected). Since `dotnet pack` for a `PackAsTool` project packs
the *publish* output, this confirms `wwwroot/index.html` reaches the installed global tool
correctly without any extra `.csproj` changes.

**A real bug caught by testing the installed tool, not just `dotnet run`:** the first version
returned `GET /` as a 404 when launched as the actual installed global tool (`shelly-em-mcp
--dashboard`) from an unrelated working directory, despite working fine via `dotnet run` from the
project folder and despite `wwwroot` being correctly present next to the installed exe.
Root cause: `WebApplication.CreateBuilder(args)` defaults `ContentRootPath` to
`Directory.GetCurrentDirectory()`, not the assembly's own directory - fine when the CWD happens to
be the project folder (`dotnet run`), wrong for a global tool invoked from wherever the user
happens to be. Fixed by passing `ContentRootPath = AppContext.BaseDirectory` explicitly via
`WebApplicationOptions`. Verified live end to end *as the installed tool*, launched from an
unrelated directory (`~`, not the repo): `GET /` returns the page (200, correct byte count),
`GET /api/power` returns live wattage matching a manual `EM.GetStatus` call, `GET
/api/history/today` returns 200 with today's bucketed history so far.

Could not get a browser screenshot of the rendered page in this session (the browser automation
tool couldn't reach this machine's `localhost`/`127.0.0.1` - a sandboxing limitation of that tool,
not the dashboard) - the JS was still double-checked carefully against the actual JSON field
casing the endpoints return (mixed: the literal-cased anonymous object in `GetPower()`'s
all-devices branch vs. PascalCase on the `PowerReading`/`EnergyHistorySummary` records nested
inside it - easy to get wrong by assuming one casing convention throughout). Worth an actual
visual check next time a session has real browser access to this machine.
