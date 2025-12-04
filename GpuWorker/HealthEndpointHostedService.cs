using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GpuWorker;

public sealed class HealthEndpointHostedService : IHostedService
{
    private readonly IWorkerHealthMonitor _healthMonitor;
    private WebApplication? _app;

    public HealthEndpointHostedService(IWorkerHealthMonitor healthMonitor)
    {
        _healthMonitor = healthMonitor;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HealthEndpointHostedService).Assembly.FullName
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8081);
        });

        builder.Services.AddSingleton(_healthMonitor);

        var app = builder.Build();

        app.MapGet("/health/live", () => Results.Ok("live"));

        app.MapGet("/health/ready", (IWorkerHealthMonitor monitor) =>
            monitor.IsReady
                ? Results.Ok("ready")
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        _app = app;

        await app.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }
}

