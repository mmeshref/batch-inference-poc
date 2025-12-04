using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SchedulerService;
using Shared;
using Xunit;

namespace SchedulerService.UnitTests;

public class DeduplicationServiceTests
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
    public void ComputeInputHash_Should_ProduceSameHash_ForIdenticalInputs()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var input = "{\"text\":\"hello\"}";
        var hash1 = service.ComputeInputHash(input);
        var hash2 = service.ComputeInputHash(input);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
        hash1.Length.Should().Be(64); // SHA256 hex string length
    }

    [Fact]
    public void ComputeInputHash_Should_NormalizeJson_ForWhitespaceDifferences()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var input1 = "{\"text\":\"hello\"}";
        var input2 = "{\n  \"text\": \"hello\"\n}";
        var input3 = "{ \"text\" : \"hello\" }";

        var hash1 = service.ComputeInputHash(input1);
        var hash2 = service.ComputeInputHash(input2);
        var hash3 = service.ComputeInputHash(input3);

        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);
    }

    [Fact]
    public void ComputeInputHash_Should_ProduceDifferentHashes_ForDifferentInputs()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var hash1 = service.ComputeInputHash("{\"text\":\"hello\"}");
        var hash2 = service.ComputeInputHash("{\"text\":\"world\"}");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeInputHash_Should_HandleNonJsonInput()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var input = "plain text input";
        var hash = service.ComputeInputHash(input);

        hash.Should().NotBeNullOrEmpty();
        hash.Length.Should().Be(64);
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_ReturnNull_WhenDeduplicationDisabled()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Deduplication:Enabled", "false" }
            })
            .Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var result = await service.FindDuplicateAsync("test-hash", "user1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_ReturnNull_WhenNoDuplicateExists()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();
        }

        var result = await service.FindDuplicateAsync("nonexistent-hash", "user1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_FindDuplicate_WhenCompletedRequestExists()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var inputHash = "test-hash-123";
        var originalRequestId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var inputFileId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();

            db.Files.Add(new FileEntity
            {
                Id = inputFileId,
                UserId = "user1",
                Filename = "input.jsonl",
                StoragePath = "/tmp/input.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId,
                UserId = "user1",
                InputFileId = inputFileId,
                Status = "completed",
                Endpoint = "test-endpoint",
                CompletionWindow = TimeSpan.FromHours(24),
                Priority = 1,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Requests.Add(new RequestEntity
            {
                Id = originalRequestId,
                BatchId = batchId,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"processed\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var result = await service.FindDuplicateAsync(inputHash, "user1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(originalRequestId);
        result.InputHash.Should().Be(inputHash);
        result.OutputPayload.Should().Be("{\"result\":\"processed\"}");
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_NotFindDuplicate_WhenRequestNotCompleted()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var inputHash = "test-hash-123";
        var batchId = Guid.NewGuid();
        var inputFileId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();

            db.Files.Add(new FileEntity
            {
                Id = inputFileId,
                UserId = "user1",
                Filename = "input.jsonl",
                StoragePath = "/tmp/input.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId,
                UserId = "user1",
                InputFileId = inputFileId,
                Status = "running",
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
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = null,
                Status = RequestStatuses.Running,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var result = await service.FindDuplicateAsync(inputHash, "user1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_RespectPerUserScope_WhenEnabled()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Deduplication:Enabled", "true" },
                { "Deduplication:PerUserScope", "true" }
            })
            .Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var inputHash = "test-hash-123";
        var batchId1 = Guid.NewGuid();
        var batchId2 = Guid.NewGuid();
        var inputFileId1 = Guid.NewGuid();
        var inputFileId2 = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();

            // User1's completed request
            db.Files.Add(new FileEntity
            {
                Id = inputFileId1,
                UserId = "user1",
                Filename = "input1.jsonl",
                StoragePath = "/tmp/input1.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId1,
                UserId = "user1",
                InputFileId = inputFileId1,
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
                BatchId = batchId1,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"user1\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });

            // User2's completed request with same hash
            db.Files.Add(new FileEntity
            {
                Id = inputFileId2,
                UserId = "user2",
                Filename = "input2.jsonl",
                StoragePath = "/tmp/input2.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId2,
                UserId = "user2",
                InputFileId = inputFileId2,
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
                BatchId = batchId2,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"user2\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // User1 should find their own duplicate
        var result1 = await service.FindDuplicateAsync(inputHash, "user1", CancellationToken.None);
        result1.Should().NotBeNull();
        result1!.OutputPayload.Should().Be("{\"result\":\"user1\"}");

        // User2 should find their own duplicate
        var result2 = await service.FindDuplicateAsync(inputHash, "user2", CancellationToken.None);
        result2.Should().NotBeNull();
        result2!.OutputPayload.Should().Be("{\"result\":\"user2\"}");

        // User1 should NOT find User2's duplicate
        var result3 = await service.FindDuplicateAsync(inputHash, "user1", CancellationToken.None);
        result3.Should().NotBeNull();
        result3!.OutputPayload.Should().Be("{\"result\":\"user1\"}"); // Should be user1's, not user2's
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_FindAcrossUsers_WhenPerUserScopeDisabled()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Deduplication:Enabled", "true" },
                { "Deduplication:PerUserScope", "false" }
            })
            .Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var inputHash = "test-hash-123";
        var batchId = Guid.NewGuid();
        var inputFileId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();

            db.Files.Add(new FileEntity
            {
                Id = inputFileId,
                UserId = "user1",
                Filename = "input.jsonl",
                StoragePath = "/tmp/input.jsonl",
                Purpose = "batch",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId,
                UserId = "user1",
                InputFileId = inputFileId,
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
                BatchId = batchId,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"processed\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // User2 should find User1's duplicate when per-user scope is disabled
        var result = await service.FindDuplicateAsync(inputHash, "user2", CancellationToken.None);

        result.Should().NotBeNull();
        result!.OutputPayload.Should().Be("{\"result\":\"processed\"}");
    }

    [Fact]
    public async Task FindDuplicateAsync_Should_ReturnMostRecentDuplicate_WhenMultipleExist()
    {
        var factory = CreateTestFactory();
        var config = new ConfigurationBuilder().Build();
        var service = new DeduplicationService(factory, config, NullLogger<DeduplicationService>.Instance);

        var inputHash = "test-hash-123";
        var batchId1 = Guid.NewGuid();
        var batchId2 = Guid.NewGuid();
        var inputFileId1 = Guid.NewGuid();
        var inputFileId2 = Guid.NewGuid();
        var olderRequestId = Guid.NewGuid();
        var newerRequestId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Database.EnsureCreated();

            var baseTime = DateTimeOffset.UtcNow;

            // Older completed request
            db.Files.Add(new FileEntity
            {
                Id = inputFileId1,
                UserId = "user1",
                Filename = "input1.jsonl",
                StoragePath = "/tmp/input1.jsonl",
                Purpose = "batch",
                CreatedAt = baseTime
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId1,
                UserId = "user1",
                InputFileId = inputFileId1,
                Status = "completed",
                Endpoint = "test-endpoint",
                CompletionWindow = TimeSpan.FromHours(24),
                Priority = 1,
                GpuPool = GpuPools.Spot,
                CreatedAt = baseTime
            });

            db.Requests.Add(new RequestEntity
            {
                Id = olderRequestId,
                BatchId = batchId1,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"older\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = baseTime,
                CompletedAt = baseTime.AddHours(1)
            });

            // Newer completed request
            db.Files.Add(new FileEntity
            {
                Id = inputFileId2,
                UserId = "user1",
                Filename = "input2.jsonl",
                StoragePath = "/tmp/input2.jsonl",
                Purpose = "batch",
                CreatedAt = baseTime
            });

            db.Batches.Add(new BatchEntity
            {
                Id = batchId2,
                UserId = "user1",
                InputFileId = inputFileId2,
                Status = "completed",
                Endpoint = "test-endpoint",
                CompletionWindow = TimeSpan.FromHours(24),
                Priority = 1,
                GpuPool = GpuPools.Spot,
                CreatedAt = baseTime
            });

            db.Requests.Add(new RequestEntity
            {
                Id = newerRequestId,
                BatchId = batchId2,
                LineNumber = 1,
                InputPayload = "{\"text\":\"hello\"}",
                InputHash = inputHash,
                OutputPayload = "{\"result\":\"newer\"}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = baseTime,
                CompletedAt = baseTime.AddHours(2)
            });

            await db.SaveChangesAsync();
        }

        var result = await service.FindDuplicateAsync(inputHash, "user1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(newerRequestId); // Should return the most recent
        result.OutputPayload.Should().Be("{\"result\":\"newer\"}");
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

