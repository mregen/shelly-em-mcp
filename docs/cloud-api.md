# Shelly Cloud API support (design sketch, not implemented)

The current tool set (`list_devices`, `get_status`, `get_power`, `get_energy_live`,
`get_energy_totals`) only talks to devices directly on the local network via
[Shelly's Gen2 local RPC API](https://shelly-api-docs.shelly.cloud/gen2/). That's all that's
needed today, and all that's been verified against a real device.

Shelly also offers a [Cloud Control API](https://shelly-api-docs.shelly.cloud/cloud-control-api/)
that would unlock two things the local API can't do:

- **Reach devices when you're not on the same network** (e.g. asking about home energy usage
  while away).
- **Historical energy data** - the local API only exposes live measurements
  (`EM.GetStatus`) and lifetime counters (`EMData.GetStatus`); day/week/month rollups and hourly
  profiles require either the Cloud API's `v2/statistics/power-consumption/*` endpoints or local
  `EMData.GetRecords`/`GetData` pagination (also not implemented - see the README's Status table).

## Sketch, if/when this gets built

- A second client alongside `Devices/ShellyRpcClient.cs` - e.g. `ShellyCloudClient` - wrapping
  `https://{server}/...` endpoints, authenticated with a per-account "Authorization cloud key"
  (from the Shelly app: **User Settings → Authorization cloud key**), not OAuth - so still no
  interactive `auth login` flow needed, just a config value (`Shelly:Cloud:AuthKey`,
  `Shelly:Cloud:Server`).
- Existing tools (`get_power`, `get_status`) would need a way to resolve a device either locally
  or via Cloud, similar to the reference Python plugin this project's tool taxonomy was modeled
  on ([`game4automation/shelly`](https://github.com/game4automation/shelly)) - it tries local
  first when configured, falls back to Cloud.
- New tools for historical data: something like `get_energy_history(device, period)` and
  `get_daily_consumption(days)`, mapped onto the Cloud API's statistics endpoints.

None of this is scheduled - it's here so a future session (or contributor) doesn't have to
re-derive the reasoning about why Cloud support was deferred, and has a starting shape if/when
it's picked up.
