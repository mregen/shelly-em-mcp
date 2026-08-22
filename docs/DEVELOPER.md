# Developer guide

Notes for building, publishing, and understanding the internals of `shelly-em-mcp`. If you just
want to run the tool, see the main [README](../README.md) instead - nothing here is needed for
that.

## Architecture

- Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`). Two transports, chosen at startup by branching on `args` before the host is built: stdio (default, `Host.CreateApplicationBuilder` + `.WithStdioServerTransport()`) or HTTP (`--http`, `WebApplication.CreateBuilder` + stateless Streamable HTTP via `MapMcp`) - the SDK doesn't support registering both on one builder, so `Program.cs` picks one.
- Also packable as a .NET global tool (`PackAsTool`).
- No OAuth/token flow - Shelly's local RPC API needs no account, just a device on the LAN. Devices are configured as a plain list (`Shelly:Devices`, `src/ShellyEmMcp/Devices/ShellyDeviceOptions.cs`), read once at startup (`Devices/ServiceCollectionExtensions.cs`), fails fast with a clear error if the list is empty.
- `Devices/ShellyRpcClient.cs` is the only thing that talks to a device - a plain GET to `http://{host}/rpc/{Method}?{params}` per the [Shelly Gen2 local RPC API](https://shelly-api-docs.shelly.cloud/gen2/), returning the raw JSON response. Each `Tools/*.cs` class parses that raw JSON itself (via `System.Text.Json.JsonDocument`/`TryGetProperty`, not `JsonSerializer.Deserialize<T>`) into a small `sealed record` DTO that becomes the tool's JSON return value - the parsing methods are `internal static` and unit-tested directly against hand-written fixture JSON (see `tests/ShellyEmMcp.Tests/EnergyToolsTests.cs`, `DeviceToolsTests.cs`), no live device needed for those tests.
- Target: multi-targets `net8.0;net10.0` (net8.0 is the still-widely-installed LTS through ~Nov 2026; net10.0 is the current LTS through ~Nov 2028) - single project for now, no RID-specific builds.

Deliberately not built yet (see the README's Status table for the reasoning behind each):
switch/relay control, Gen1 device support, the `monophase` device profile (EM1/EM1Data instead
of combined EM/EMData), Shelly Cloud API support.

## Running from source

The [README](../README.md) covers installing the published tool from nuget.org - this is for
working against a clone of this repo instead (e.g. to test unreleased changes).

### Prerequisites

- .NET 8 or .NET 10 SDK (the project multi-targets both - either builds and runs it)
- A Shelly Gen2/Gen3 EM-class device on your local network, reachable at a fixed IP

### 1. Configure your device(s)

Edit `src/ShellyEmMcp/appsettings.Development.json` with your device's real name/IP - it's not a
secret (a LAN IP), so unlike an OAuth-based sibling project there's no user-secrets step here:

```json
{
  "Shelly": {
    "Devices": [
      { "Name": "Hausanschluss", "Host": "192.168.1.100" }
    ]
  }
}
```

### 2. Run the server

The server supports two transports, chosen at startup - **stdio by default**, or HTTP via a flag:

```bash
cd src/ShellyEmMcp

dotnet run --no-launch-profile                                          # stdio - for MCP clients that spawn the process directly
dotnet run --no-launch-profile -- --http --urls http://localhost:5250   # HTTP - a long-running server on a port
```

In HTTP mode the MCP endpoint is at `<url>/mcp` (Streamable HTTP), e.g. `http://localhost:5250/mcp`.

**Never point a real MCP client at plain `dotnet run` for stdio** - its own "Building..." banner
pollutes stdout before the app starts, which corrupts the JSON-RPC channel (a real bug hit in the
sibling `fatsecret-mcp`/`fitbit-mcp` projects). Use `--no-launch-profile` as shown above, the
built DLL, or an installed tool instead.

### 3. Point an MCP client at it

```bash
claude mcp add shelly-em-mcp -- dotnet run --project src/ShellyEmMcp --no-launch-profile
```

Or HTTP, with the server already running from step 2:

```bash
claude mcp add --transport http shelly-em-local-dev http://localhost:5250/mcp
```

Then restart or reconnect your Claude Code session - new MCP registrations aren't picked up
mid-session.

### Packing and installing a local build as a tool

To test the tool as it would actually be installed, without waiting on a NuGet.org publish:

```bash
dotnet pack src/ShellyEmMcp/ShellyEmMcp.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg ShellyEmMcp
```

This installs the same `shelly-em-mcp` command described in the README, from your local build.

## Publishing to NuGet.org

`.github/workflows/build.yml` packs the tool (`.nupkg` + `.snupkg`) on every push to `main` and
uploads it as a workflow artifact - so a build is always inspectable. Publishing to nuget.org is
a **separate, manual-only job** that never runs on a normal push/PR: it only exists on
`workflow_dispatch` (Actions tab → this workflow → **Run workflow**), gated behind a `publish`
checkbox input that **defaults to false**, and it `needs: build` so it can't run unless the
build+test job already succeeded.

Publishing uses nuget.org's [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC) instead of a stored API key - no long-lived secret in this repo at all. One-time setup,
on nuget.org (**username menu → Trusted Publishing → Add policy**):

| Field | Value |
|---|---|
| Repository Owner | `mregen` |
| Repository | `shelly-em-mcp` |
| Workflow File | `build.yml` |
| Environment | `nuget-publish` |

The `publish` job passes `${{ github.repository_owner }}` as `NuGet/login`'s `user:` input,
rather than a hardcoded name - it relies on your nuget.org profile name matching your GitHub
username, so a fork of this repo publishes under *its own* owner's identity by default, without
editing the workflow. If your nuget.org username ever differs from your GitHub username, override
`user:` with a literal value (or a repository variable) instead.

After a successful push, the same job also creates a GitHub release (`gh release create`,
tagged `v<version>`) with the `.nupkg`/`.snupkg` attached and notes auto-generated from merged
PRs/commits since the last tag. The version is the one NBGV computed during `Pack`, extracted
from the packed filename (`ShellyEmMcp.<version>.nupkg`) and passed between jobs via a job
`output`.
