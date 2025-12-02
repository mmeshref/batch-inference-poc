using ApiGateway.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;

namespace ApiGateway.UnitTests;

public class FileServiceTests
{
    private static BatchDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<BatchDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    [Fact]
    public async Task SaveFileAsync_Should_Persist_FileEntity()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var storagePath = Path.Combine(Path.GetTempPath(), "file-service-tests", Guid.NewGuid().ToString());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = storagePath
            })
            .Build();

        var service = new FileService(db, config, NullLogger<FileService>.Instance);
        await using var ms = new MemoryStream(new byte[] { 1, 2, 3 });

        var file = await service.SaveFileAsync(
            userId: "user-1",
            originalFilename: "test.jsonl",
            content: ms,
            purpose: "batch",
            cancellationToken: CancellationToken.None);

        var saved = await db.Files.FindAsync(file.Id);
        saved.Should().NotBeNull();
        saved!.Filename.Should().Be("test.jsonl");
        saved.UserId.Should().Be("user-1");
        File.Exists(saved.StoragePath).Should().BeTrue();
    }
}

