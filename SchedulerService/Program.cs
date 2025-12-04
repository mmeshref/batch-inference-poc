using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared;
using SchedulerService;
using Prometheus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<BatchSchedulerWorker>();

// small web server for /metrics
builder.Services.AddSingleton<IStartupFilter>(_ => new MetricsStartupFilter());

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string not configured.");

builder.Services.AddDbContext<BatchDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDbContextFactory<BatchDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDeduplicationService, DeduplicationService>();

builder.Services.AddHostedService<BatchSchedulerWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
    db.Database.EnsureCreated();
}

// start metrics server
var metricServer = new KestrelMetricServer(port: 8080);
metricServer.Start();

host.Run();
