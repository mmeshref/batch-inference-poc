using ApiGateway.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace ApiGateway.UnitTests;

public class BatchServiceTests
{
    private static BatchDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<BatchDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    [Fact]
    public async Task CreateBatchAsync_Should_Persist_Batch()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Filename = "input.jsonl",
            StoragePath = "/tmp/input.jsonl",
            Purpose = "batch",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var service = new BatchService(db, NullLogger<BatchService>.Instance);

        var batch = await service.CreateBatchAsync(
            userId: "user-1",
            inputFileId: file.Id,
            gpuPool: "spot",
            userName: "demo",
            completionWindow: TimeSpan.FromHours(24),
            priority: 1,
            cancellationToken: CancellationToken.None);

        var saved = await db.Batches.FindAsync(batch.Id);
        saved.Should().NotBeNull();
        saved!.InputFileId.Should().Be(file.Id);
        saved.UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task CreateBatchAsync_Should_Throw_When_File_Not_Found()
    {
        await using var db = CreateDb(Guid.NewGuid().ToString());
        var service = new BatchService(db, NullLogger<BatchService>.Instance);

        Func<Task> act = () => service.CreateBatchAsync(
            userId: "user-1",
            inputFileId: Guid.NewGuid(),
            gpuPool: "spot",
            userName: "demo",
            completionWindow: TimeSpan.FromHours(24),
            priority: 1,
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

