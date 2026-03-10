using DSE.Agent.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DSE.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentRuntimeState _state;

    public Worker(ILogger<Worker> logger, AgentRuntimeState state)
    {
        _logger = logger;
        _state = state;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _state.StartedAt = DateTime.Now;
        _state.LastHeartbeat = DateTime.Now;

        _logger.LogInformation("DSE Agent is starting.");
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _state.LastHeartbeat = DateTime.Now;

            _logger.LogInformation("DSE Agent heartbeat at: {time}", DateTimeOffset.Now);

            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "heartbeat.log");

                await File.AppendAllTextAsync(
                    logPath,
                    $"Heartbeat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}",
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write heartbeat log.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DSE Agent is stopping.");
        await base.StopAsync(cancellationToken);
    }
}