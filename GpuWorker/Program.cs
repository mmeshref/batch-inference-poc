using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using Prometheus;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string not configured.");

builder.Services.AddDbContext<BatchDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddHostedService<GpuWorkerService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
    db.Database.EnsureCreated();
}

var metricServer = new KestrelMetricServer(port: 8080);
metricServer.Start();

host.Run();
