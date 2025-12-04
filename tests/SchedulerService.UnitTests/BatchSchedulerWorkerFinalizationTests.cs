using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchedulerService;
using Shared;
using Xunit;

namespace SchedulerService.UnitTests;

/// <summary>
/// Tests to ensure that batches with all deduplicated requests get finalized immediately
/// instead of staying stuck in "Running" status.
/// </summary>
public class BatchSchedulerWorkerFinalizationTests
{
    private static IDbContextFactory<BatchDbContext> CreateTestFactory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseSqlite(connection)
            .Options;

        return new TestDbContextFactory(options);
    }

    [Fact]
    public async Task ProcessSingleBatchAsync_Should_FinalizeBatch_WhenAllRequestsAreDeduplicated()
    {
        // Arrange
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:BasePath", Path.GetTempPath() },
                { "Scheduling:SlaEscalationThresholdHours", "2" },
                { "Deduplication:Enabled", "true" },
                { "Deduplication:PerUserScope", "false" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BatchDbContext>>(factory);
        services.AddScoped<IDeduplicationService, DeduplicationService>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILogger<DeduplicationService>>(NullLogger<DeduplicationService>.Instance);
        var serviceProvider = services.BuildServiceProvider();

        var logger = NullLogger<BatchSchedulerWorker>.Instance;
        var scheduler = new BatchSchedulerWorker(serviceProvider, logger, config);

        var batchId = Guid.NewGuid();
        var inputFileId = Guid.NewGuid();
        var userId = "test-user";
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Create input file with JSONL content
            var inputFilePath = Path.Combine(tempDir, "input.jsonl");
            var inputLines = new[] { "{\"text\":\"hello\"}", "{\"text\":\"world\"}" };
            await File.WriteAllLinesAsync(inputFilePath, inputLines);

            // Create a completed request in the database (to simulate deduplication match)
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Database.EnsureCreated();

                // Create file entity
                db.Files.Add(new FileEntity
                {
                    Id = inputFileId,
                    UserId = userId,
                    Filename = "input.jsonl",
                    StoragePath = inputFilePath,
                    Purpose = "batch",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                // Create a batch with status "queued"
                db.Batches.Add(new BatchEntity
                {
                    Id = batchId,
                    UserId = userId,
                    InputFileId = inputFileId,
                    Status = "queued",
                    Endpoint = "test-endpoint",
                    CompletionWindow = TimeSpan.FromHours(24),
                    Priority = 1,
                    GpuPool = GpuPools.Spot,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                // Create a completed request that will be used for deduplication
                var deduplicationService = serviceProvider.GetRequiredService<IDeduplicationService>();
                var inputHash1 = deduplicationService.ComputeInputHash(inputLines[0]);
                var inputHash2 = deduplicationService.ComputeInputHash(inputLines[1]);

                var originalRequestId1 = Guid.NewGuid();
                var originalRequestId2 = Guid.NewGuid();
                var originalBatchId1 = Guid.NewGuid();
                var originalBatchId2 = Guid.NewGuid();
                var originalFileId1 = Guid.NewGuid();
                var originalFileId2 = Guid.NewGuid();

                // Add original completed requests that will be matched during deduplication
                db.Files.Add(new FileEntity
                {
                    Id = originalFileId1,
                    UserId = userId,
                    Filename = "original1.jsonl",
                    StoragePath = "/tmp/original1.jsonl",
                    Purpose = "batch",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Files.Add(new FileEntity
                {
                    Id = originalFileId2,
                    UserId = userId,
                    Filename = "original2.jsonl",
                    StoragePath = "/tmp/original2.jsonl",
                    Purpose = "batch",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Batches.Add(new BatchEntity
                {
                    Id = originalBatchId1,
                    UserId = userId,
                    InputFileId = originalFileId1,
                    Status = "completed",
                    Endpoint = "test-endpoint",
                    CompletionWindow = TimeSpan.FromHours(24),
                    Priority = 1,
                    GpuPool = GpuPools.Spot,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Batches.Add(new BatchEntity
                {
                    Id = originalBatchId2,
                    UserId = userId,
                    InputFileId = originalFileId2,
                    Status = "completed",
                    Endpoint = "test-endpoint",
                    CompletionWindow = TimeSpan.FromHours(24),
                    Priority = 1,
                    GpuPool = GpuPools.Spot,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Requests.Add(new RequestEntity
                {
                    Id = originalRequestId1,
                    BatchId = originalBatchId1,
                    LineNumber = 1,
                    InputPayload = inputLines[0],
                    InputHash = inputHash1,
                    OutputPayload = "{\"result\":\"hello-processed\"}",
                    Status = RequestStatuses.Completed,
                    GpuPool = GpuPools.Spot,
                    IsDeduplicated = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow
                });

                db.Requests.Add(new RequestEntity
                {
                    Id = originalRequestId2,
                    BatchId = originalBatchId2,
                    LineNumber = 1,
                    InputPayload = inputLines[1],
                    InputHash = inputHash2,
                    OutputPayload = "{\"result\":\"world-processed\"}",
                    Status = RequestStatuses.Completed,
                    GpuPool = GpuPools.Spot,
                    IsDeduplicated = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync();
            }

            // Act: Process the batch
            await using (var db = await factory.CreateDbContextAsync())
            {
                var batch = await db.Batches
                    .Include(b => b.Requests)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                batch.Should().NotBeNull();
                batch!.Status.Should().Be("queued");

                await scheduler.ProcessSingleBatchAsync(db, batch, CancellationToken.None);
            }

            // Assert: Batch should be finalized
            await using (var db = await factory.CreateDbContextAsync())
            {
                var batch = await db.Batches
                    .Include(b => b.Requests)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                batch.Should().NotBeNull();
                batch!.Status.Should().Be("completed", "batch should be finalized when all requests are deduplicated");
                batch.OutputFileId.Should().NotBeNull("batch should have an output file created");
                batch.CompletedAt.Should().NotBeNull("batch should have a completion timestamp");

                // Verify all requests are marked as deduplicated and completed
                var requests = batch.Requests.ToList();
                requests.Should().HaveCount(2, "should have 2 requests");
                requests.Should().AllSatisfy(r =>
                {
                    r.Status.Should().Be(RequestStatuses.Completed);
                    r.IsDeduplicated.Should().BeTrue();
                    r.OriginalRequestId.Should().NotBeNull();
                    r.CompletedAt.Should().NotBeNull();
                });

                // Verify output file exists and contains the deduplicated outputs
                var outputFile = await db.Files.FirstOrDefaultAsync(f => f.Id == batch.OutputFileId);
                outputFile.Should().NotBeNull();
                File.Exists(outputFile!.StoragePath).Should().BeTrue("output file should exist on disk");

                var outputLines = await File.ReadAllLinesAsync(outputFile.StoragePath);
                outputLines.Should().HaveCount(2);
                outputLines[0].Should().Contain("hello-processed");
                outputLines[1].Should().Contain("world-processed");
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessSingleBatchAsync_Should_NotFinalizeBatch_WhenSomeRequestsAreStillQueued()
    {
        // Arrange
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:BasePath", Path.GetTempPath() },
                { "Scheduling:SlaEscalationThresholdHours", "2" },
                { "Deduplication:Enabled", "true" },
                { "Deduplication:PerUserScope", "false" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BatchDbContext>>(factory);
        services.AddScoped<IDeduplicationService, DeduplicationService>();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILogger<DeduplicationService>>(NullLogger<DeduplicationService>.Instance);
        var serviceProvider = services.BuildServiceProvider();

        var logger = NullLogger<BatchSchedulerWorker>.Instance;
        var scheduler = new BatchSchedulerWorker(serviceProvider, logger, config);

        var batchId = Guid.NewGuid();
        var inputFileId = Guid.NewGuid();
        var userId = "test-user";
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Create input file with JSONL content - one will be deduplicated, one will be new
            var inputFilePath = Path.Combine(tempDir, "input.jsonl");
            var inputLines = new[] { "{\"text\":\"hello\"}", "{\"text\":\"unique-new-request\"}" };
            await File.WriteAllLinesAsync(inputFilePath, inputLines);

            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Database.EnsureCreated();

                db.Files.Add(new FileEntity
                {
                    Id = inputFileId,
                    UserId = userId,
                    Filename = "input.jsonl",
                    StoragePath = inputFilePath,
                    Purpose = "batch",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Batches.Add(new BatchEntity
                {
                    Id = batchId,
                    UserId = userId,
                    InputFileId = inputFileId,
                    Status = "queued",
                    Endpoint = "test-endpoint",
                    CompletionWindow = TimeSpan.FromHours(24),
                    Priority = 1,
                    GpuPool = GpuPools.Spot,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                // Create one completed request for deduplication (only first line will match)
                var deduplicationService = serviceProvider.GetRequiredService<IDeduplicationService>();
                var inputHash1 = deduplicationService.ComputeInputHash(inputLines[0]);
                var originalBatchId = Guid.NewGuid();
                var originalFileId = Guid.NewGuid();

                db.Files.Add(new FileEntity
                {
                    Id = originalFileId,
                    UserId = userId,
                    Filename = "original.jsonl",
                    StoragePath = "/tmp/original.jsonl",
                    Purpose = "batch",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Batches.Add(new BatchEntity
                {
                    Id = originalBatchId,
                    UserId = userId,
                    InputFileId = originalFileId,
                    Status = "completed",
                    Endpoint = "test-endpoint",
                    CompletionWindow = TimeSpan.FromHours(24),
                    Priority = 1,
                    GpuPool = GpuPools.Spot,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                db.Requests.Add(new RequestEntity
                {
                    Id = Guid.NewGuid(),
                    BatchId = originalBatchId,
                    LineNumber = 1,
                    InputPayload = inputLines[0],
                    InputHash = inputHash1,
                    OutputPayload = "{\"result\":\"hello-processed\"}",
                    Status = RequestStatuses.Completed,
                    GpuPool = GpuPools.Spot,
                    IsDeduplicated = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync();
            }

            // Act: Process the batch
            await using (var db = await factory.CreateDbContextAsync())
            {
                var batch = await db.Batches
                    .Include(b => b.Requests)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                batch.Should().NotBeNull();
                await scheduler.ProcessSingleBatchAsync(db, batch!, CancellationToken.None);
            }

            // Assert: Batch should NOT be finalized (one request is still queued)
            await using (var db = await factory.CreateDbContextAsync())
            {
                var batch = await db.Batches
                    .Include(b => b.Requests)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                batch.Should().NotBeNull();
                batch!.Status.Should().Be("running", "batch should remain running when some requests are still queued");
                batch.OutputFileId.Should().BeNull("batch should not have an output file yet");
                batch.CompletedAt.Should().BeNull("batch should not be completed yet");

                // Verify one request is deduplicated and one is queued
                var requests = batch.Requests.ToList();
                requests.Should().HaveCount(2);
                requests.Should().Contain(r => r.IsDeduplicated && r.Status == RequestStatuses.Completed);
                requests.Should().Contain(r => !r.IsDeduplicated && r.Status == RequestStatuses.Queued);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<BatchDbContext>
    {
        private readonly DbContextOptions<BatchDbContext> _options;

        public TestDbContextFactory(DbContextOptions<BatchDbContext> options)
        {
            _options = options;
        }

        public BatchDbContext CreateDbContext()
        {
            var dbContext = new BatchDbContext(_options);
            dbContext.Database.EnsureCreated();
            return dbContext;
        }

        public Task<BatchDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}

