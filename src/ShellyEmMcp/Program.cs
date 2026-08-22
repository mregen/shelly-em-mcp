// SPDX-License-Identifier: MIT

using ShellyEmMcp.Devices;
using ShellyEmMcp.Tools;

var useHttp = args.Contains("--http");
var useDashboard = args.Contains("--dashboard");
var remainingArgs = args.Where(a => a != "--http" && a != "--dashboard").ToArray();

if (useDashboard)
{
    // ContentRootPath (and so WebRootPath/wwwroot resolution) otherwise defaults to the current
    // working directory - fine for `dotnet run` from the project folder, but wrong for a globally
    // installed tool invoked from an arbitrary directory, which is the normal way to run this.
    // Anchoring it to the assembly's own directory is what makes `shelly-em-mcp --dashboard` find
    // its wwwroot/index.html regardless of where it's launched from.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = remainingArgs,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Services.AddShellyClients();

    var app = builder.Build();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapGet("/api/power", async (EnergyTools energyTools, CancellationToken cancellationToken) =>
        Results.Text(await energyTools.GetPower(cancellationToken: cancellationToken), "application/json"));

    app.MapGet("/api/history/today", async (
        EnergyTools energyTools,
        IReadOnlyList<ShellyDeviceOptions> devices,
        string? device,
        CancellationToken cancellationToken) =>
    {
        var targetName = device ?? (devices.Count > 0 ? devices[0].Name : null);
        if (targetName is null)
        {
            return Results.Problem("No Shelly devices configured.", statusCode: 500);
        }

        // Hours since local midnight, rounded up so the just-started current hour's partial
        // bucket is still included - GetEnergyHistory's own bucket-width scaling (5/15/60 min by
        // window size) applies exactly as it does for the MCP tool.
        var hoursSinceMidnight = (int)Math.Ceiling(DateTime.Now.TimeOfDay.TotalHours);
        var hours = Math.Clamp(hoursSinceMidnight, 1, 24);
        var json = await energyTools.GetEnergyHistory(targetName, hours, cancellationToken);
        return Results.Text(json, "application/json");
    });

    app.Run();
}
else if (useHttp)
{
    var builder = WebApplication.CreateBuilder(remainingArgs);
    builder.Services.AddShellyClients();
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp("/mcp");
    app.Run();
}
else
{
    var builder = Host.CreateApplicationBuilder(remainingArgs);

    // Stdio is the JSON-RPC channel - any stray console log line on stdout would corrupt it.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddShellyClients();
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
