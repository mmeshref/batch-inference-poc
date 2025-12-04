using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// Tests to validate that the database schema matches the entity model.
/// These tests catch schema mismatches early, preventing runtime errors like "column does not exist".
/// </summary>
public class DatabaseSchemaTests
{
    [Fact]
    public async Task EnsureCreated_Should_CreateAllRequiredColumns_ForRequestEntity()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new BatchDbContext(options);
        db.Database.EnsureCreated();

        // Query SQLite's sqlite_master table to get table schema
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(requests);";
        await using var reader = await command.ExecuteReaderAsync();
        
        var columnNames = new List<string>();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(1); // Column name is at index 1
            columnNames.Add(columnName);
        }

        // Verify all required columns exist
        columnNames.Should().Contain("Id", "RequestEntity.Id should exist");
        columnNames.Should().Contain("BatchId", "RequestEntity.BatchId should exist");
        columnNames.Should().Contain("LineNumber", "RequestEntity.LineNumber should exist");
        columnNames.Should().Contain("InputPayload", "RequestEntity.InputPayload should exist");
        columnNames.Should().Contain("OutputPayload", "RequestEntity.OutputPayload should exist");
        columnNames.Should().Contain("Status", "RequestEntity.Status should exist");
        columnNames.Should().Contain("GpuPool", "RequestEntity.GpuPool should exist");
        columnNames.Should().Contain("AssignedWorker", "RequestEntity.AssignedWorker should exist");
        columnNames.Should().Contain("CreatedAt", "RequestEntity.CreatedAt should exist");
        columnNames.Should().Contain("StartedAt", "RequestEntity.StartedAt should exist");
        columnNames.Should().Contain("CompletedAt", "RequestEntity.CompletedAt should exist");
        columnNames.Should().Contain("ErrorMessage", "RequestEntity.ErrorMessage should exist");
        
        // Deduplication columns (added in recent update) - these are critical for deduplication feature
        columnNames.Should().Contain("InputHash", "RequestEntity.InputHash should exist for deduplication");
        columnNames.Should().Contain("OriginalRequestId", "RequestEntity.OriginalRequestId should exist for deduplication");
        columnNames.Should().Contain("IsDeduplicated", "RequestEntity.IsDeduplicated should exist for deduplication");
    }

    [Fact]
    public async Task EnsureCreated_Should_AllowQueryingByInputHash()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new BatchDbContext(options);
        db.Database.EnsureCreated();

        // Create required entities for foreign key constraints
        var batchId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        db.Files.Add(new FileEntity
        {
            Id = fileId,
            UserId = "user1",
            Filename = "test.jsonl",
            StoragePath = "/tmp/test.jsonl",
            Purpose = "batch",
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.Batches.Add(new BatchEntity
        {
            Id = batchId,
            UserId = "user1",
            InputFileId = fileId,
            Status = "running",
            Endpoint = "test",
            CompletionWindow = TimeSpan.FromHours(24),
            Priority = 1,
            GpuPool = "spot",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Test that we can create and query by InputHash
        var testRequest = new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            LineNumber = 1,
            InputPayload = "{}",
            Status = "Queued",
            GpuPool = "spot",
            InputHash = "test-hash",
            IsDeduplicated = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Requests.Add(testRequest);
        await db.SaveChangesAsync();

        // Query by InputHash to verify the column exists and is queryable
        var found = await db.Requests
            .Where(r => r.InputHash == "test-hash")
            .FirstOrDefaultAsync();

        found.Should().NotBeNull("Should be able to query by InputHash");
        found!.InputHash.Should().Be("test-hash");
        
        // Verify the index exists (SQLite creates it automatically for foreign keys and unique constraints)
        // The actual index name may vary, but the important thing is that queries work
    }

    [Fact]
    public async Task RequestEntity_Should_SupportDeduplicationFields()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new BatchDbContext(options);
        db.Database.EnsureCreated();

        var batchId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var originalRequestId = Guid.NewGuid();
        var deduplicatedRequestId = Guid.NewGuid();

        // Create required file entity
        db.Files.Add(new FileEntity
        {
            Id = fileId,
            UserId = "user1",
            Filename = "input.jsonl",
            StoragePath = "/tmp/input.jsonl",
            Purpose = "batch",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Create a batch
        db.Batches.Add(new BatchEntity
        {
            Id = batchId,
            UserId = "user1",
            InputFileId = fileId,
            Status = "running",
            Endpoint = "test",
            CompletionWindow = TimeSpan.FromHours(24),
            Priority = 1,
            GpuPool = "spot",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        // Create original request
        db.Requests.Add(new RequestEntity
        {
            Id = originalRequestId,
            BatchId = batchId,
            LineNumber = 1,
            InputPayload = "{\"text\":\"hello\"}",
            InputHash = "hash123",
            OutputPayload = "{\"result\":\"processed\"}",
            Status = "Completed",
            GpuPool = "spot",
            IsDeduplicated = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        // Create deduplicated request
        db.Requests.Add(new RequestEntity
        {
            Id = deduplicatedRequestId,
            BatchId = batchId,
            LineNumber = 2,
            InputPayload = "{\"text\":\"hello\"}",
            InputHash = "hash123",
            OutputPayload = "{\"result\":\"processed\"}",
            Status = "Completed",
            GpuPool = "spot",
            IsDeduplicated = true,
            OriginalRequestId = originalRequestId,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        // Verify we can query deduplicated requests
        var deduplicated = await db.Requests
            .Where(r => r.IsDeduplicated == true)
            .FirstOrDefaultAsync();

        deduplicated.Should().NotBeNull();
        deduplicated!.OriginalRequestId.Should().Be(originalRequestId);
        deduplicated.InputHash.Should().Be("hash123");

        // Verify we can find duplicates by hash
        var duplicates = await db.Requests
            .Where(r => r.InputHash == "hash123")
            .ToListAsync();

        duplicates.Should().HaveCount(2);
        duplicates.Should().Contain(r => r.IsDeduplicated == true);
        duplicates.Should().Contain(r => r.IsDeduplicated == false);
    }
}

