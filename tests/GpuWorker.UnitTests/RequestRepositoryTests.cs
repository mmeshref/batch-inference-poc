using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GpuWorker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace GpuWorker.UnitTests;

public class RequestRepositoryTests
{
    [Fact]
    public async Task DequeueAsync_Should_Return_Queued_Request()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseSqlite(connection)
            .Options;

        var factory = new TestDbContextFactory(options);

        await using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();

            var batchId = Guid.NewGuid();
            var inputFileId = Guid.NewGuid();

            db.Files.Add(new FileEntity
            {
                Id = inputFileId,
                UserId = "user",
                Filename = "input.jsonl",
                StoragePath = "/tmp/input.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId,
                UserId = "user",
                InputFileId = inputFileId,
                Status = "queued",
                Endpoint = "test-endpoint",
                CompletionWindow = TimeSpan.FromHours(24),
                Priority = 1,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Requests.Add(new RequestEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                LineNumber = 1,
                InputPayload = "{}",
                Status = RequestStatuses.Queued,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().Build();
        var repo = new RequestRepository(factory, config, NullLogger<RequestRepository>.Instance);

        var result = await repo.DequeueAsync(GpuPools.Spot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.GpuPool.Should().Be(GpuPools.Spot);
        result.Status.Should().Be(RequestStatuses.Queued);
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
            return new BatchDbContext(_options);
        }

        public Task<BatchDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}

