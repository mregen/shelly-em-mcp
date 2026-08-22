// SPDX-License-Identifier: MIT

using ShellyEmMcp.Tools;

namespace ShellyEmMcp.Devices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShellyClients(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var devices = config.GetSection("Shelly:Devices").Get<List<ShellyDeviceOptions>>() ?? [];
            if (devices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Shelly:Devices is not configured. Add at least one device (Name + Host) to appsettings.json.");
            }

            return (IReadOnlyList<ShellyDeviceOptions>)devices;
        });
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ShellyRpcClient));
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            return new ShellyRpcClient(httpClient);
        });

        // Also directly resolvable, not just discoverable via MCP's WithToolsFromAssembly() -
        // the --dashboard mode's minimal API endpoints call these same tool methods rather than
        // duplicating the RPC/parsing logic behind a second code path.
        services.AddSingleton<DeviceTools>();
        services.AddSingleton<EnergyTools>();

        return services;
    }
}
