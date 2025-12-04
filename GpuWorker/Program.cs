using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using Prometheus;
using GpuWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string not configured.");

builder.Services.AddDbContextFactory<BatchDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IRequestRepository, RequestRepository>();
builder.Services.AddSingleton<IWorkerHealthMonitor, WorkerHealthMonitor>();

builder.Services.AddHostedService<HealthEndpointHostedService>();

builder.Services.AddHostedService<GpuWorkerService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BatchDbContext>>();
    var healthMonitor = scope.ServiceProvider.GetRequiredService<IWorkerHealthMonitor>();
    await using var db = await factory.CreateDbContextAsync();
    db.Database.EnsureCreated();
    healthMonitor.MarkReady();
}

var metricServer = new KestrelMetricServer(port: 8080);
metricServer.Start();

host.Run();
