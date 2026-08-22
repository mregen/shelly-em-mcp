// SPDX-License-Identifier: MIT

using ShellyEmMcp.Devices;

var useHttp = args.Contains("--http");
var remainingArgs = args.Where(a => a != "--http").ToArray();

if (useHttp)
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
