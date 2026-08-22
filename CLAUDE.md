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
