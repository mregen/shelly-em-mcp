// SPDX-License-Identifier: MIT

using System.Text;

namespace ShellyEmMcp.Devices;

/// <summary>
/// Thin wrapper around the Shelly Gen2/Gen3 local JSON-RPC HTTP API
/// (https://shelly-api-docs.shelly.cloud/gen2/). Devices accept RPC calls as a plain GET to
/// /rpc/{Method}?{param=value&amp;...}, returning the RPC result object directly (no JSON-RPC
/// envelope) - confirmed against the Gen2 Shelly/EM/EMData component docs used by the current tool
/// set. Digest auth (if the device has a local admin password set) is not implemented here - not
/// needed for an unprotected LAN device, and untestable without one to verify against.
/// </summary>
public sealed class ShellyRpcClient(HttpClient httpClient)
{
    public async Task<string> CallAsync(
        string host,
        string method,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder($"http://{host}/rpc/{method}");
        if (parameters is { Count: > 0 })
        {
            url.Append('?');
            url.Append(string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")));
        }

        var response = await httpClient.GetAsync(url.ToString(), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Shelly device {host} request '{method}' failed ({(int)response.StatusCode}): {body}");
        }

        return body;
    }
}
