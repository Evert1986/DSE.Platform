using System.Reflection;
using DSE.Agent;
using DSE.Agent.State;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);

// Force the local API to bind when running as a Windows Service
builder.WebHost.UseUrls("http://localhost:5070");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DSE Agent";
});

builder.Services.AddSingleton<AgentRuntimeState>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/status", (AgentRuntimeState state) =>
{
    var version = Assembly.GetExecutingAssembly()
        .GetName()
        .Version?
        .ToString() ?? "Unknown";

    var now = DateTime.Now;
    var uptime = now - state.StartedAt;

    return Results.Json(new
    {
        service = "DSE Agent",
        status = "Running",
        machineName = Environment.MachineName,
        version = version,
        startedAt = state.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        lastHeartbeat = state.LastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss"),
        uptimeSeconds = (int)uptime.TotalSeconds
    });
});

app.Run();