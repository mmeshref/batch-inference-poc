using System;
using System.Collections.Generic;
using System.Linq;
using BatchPortal.Models;
using BatchPortal.Pages;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class HomeDashboardViewModelTests
{
    [Fact]
    public void BuildDashboard_ComputesAggregationsAndLimitsRecentBatches()
    {
        var now = new DateTimeOffset(new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        var batches = new List<BatchEntity>
        {
            CreateBatch(RequestStatuses.Completed, now.AddHours(-1), completedHoursAfterCreate: 0.5), // completed in window
            CreateBatch(RequestStatuses.Completed, now.AddHours(-30), completedHoursAfterCreate: 1), // outside window
            CreateBatch(RequestStatuses.Failed, now.AddHours(-2), completedHoursAfterCreate: 0.5), // failed within window
            CreateBatch(RequestStatuses.Failed, now.AddHours(-50), completedHoursAfterCreate: 1), // failed outside window
            CreateBatch(RequestStatuses.Queued, now.AddHours(-3)),
            CreateBatch(RequestStatuses.Running, now.AddHours(-4)),
            CreateBatch(RequestStatuses.Completed, now.AddHours(-5), completedHoursAfterCreate: 0.5),
            CreateBatch(RequestStatuses.Completed, now.AddHours(-6), completedHoursAfterCreate: 30), // SLA breach
            CreateBatch(RequestStatuses.Completed, now.AddHours(-40), completedHoursAfterCreate: 1),
            CreateBatch(RequestStatuses.Completed, now.AddHours(-41), completedHoursAfterCreate: 1),
            CreateBatch(RequestStatuses.Completed, now.AddHours(-42), completedHoursAfterCreate: 1)
        };

        var dashboard = IndexModel.BuildDashboard(batches, now);

        Assert.Equal(batches.Count, dashboard.TotalBatches);
        Assert.Equal(3, dashboard.CompletedLast24h);
        Assert.Equal(1, dashboard.FailedLast24h);
        Assert.Equal(2, dashboard.InProgress); // Queued + Running

        Assert.Equal(10, dashboard.RecentBatches.Count);
        var sorted = dashboard.RecentBatches.Select(b => b.CreatedAt).ToList();
        var expected = batches
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .Select(b => b.CreatedAt)
            .ToList();
        Assert.Equal(expected, sorted);

        var breachedItem = dashboard.RecentBatches.First(b => b.IsSlaBreached);
        Assert.True(breachedItem.IsSlaBreached);
    }

    [Fact]
    public void BuildDashboard_HandlesLowercaseBatchStatuses()
    {
        // This test ensures that batch statuses stored as lowercase strings ("queued", "running", "completed", "failed")
        // are correctly counted, preventing the bug where case-sensitive comparisons failed.
        var now = new DateTimeOffset(new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        var batches = new List<BatchEntity>
        {
            CreateBatch("completed", now.AddHours(-1), completedHoursAfterCreate: 0.5), // lowercase "completed"
            CreateBatch("COMPLETED", now.AddHours(-2), completedHoursAfterCreate: 0.5), // uppercase (should still work)
            CreateBatch("failed", now.AddHours(-3), completedHoursAfterCreate: 0.5), // lowercase "failed"
            CreateBatch("queued", now.AddHours(-4)), // lowercase "queued"
            CreateBatch("running", now.AddHours(-5)), // lowercase "running"
            CreateBatch("cancelled", now.AddHours(-6), completedHoursAfterCreate: 0.5), // lowercase "cancelled"
        };

        var dashboard = IndexModel.BuildDashboard(batches, now);

        // Should count all completed batches (both lowercase and uppercase) within 24h window
        Assert.Equal(2, dashboard.CompletedLast24h);
        
        // Should count failed batches within 24h window
        Assert.Equal(1, dashboard.FailedLast24h);
        
        // Should count queued and running batches (case-insensitive)
        Assert.Equal(2, dashboard.InProgress);
        
        // Total should be all batches
        Assert.Equal(6, dashboard.TotalBatches);
    }

    [Fact]
    public void BuildDashboard_HandlesCaseInsensitiveStatusComparison()
    {
        // This test specifically verifies that case-insensitive comparison works correctly
        // for all batch status types, preventing regression of the case-sensitivity bug.
        var now = new DateTimeOffset(new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        
        // Test with various case combinations
        var batches = new List<BatchEntity>
        {
            CreateBatch("queued", now.AddHours(-1)),
            CreateBatch("QUEUED", now.AddHours(-2)),
            CreateBatch("Queued", now.AddHours(-3)),
            CreateBatch("running", now.AddHours(-4)),
            CreateBatch("RUNNING", now.AddHours(-5)),
            CreateBatch("Running", now.AddHours(-6)),
            CreateBatch("completed", now.AddHours(-7), completedHoursAfterCreate: 0.5),
            CreateBatch("COMPLETED", now.AddHours(-8), completedHoursAfterCreate: 0.5),
            CreateBatch("Completed", now.AddHours(-9), completedHoursAfterCreate: 0.5),
            CreateBatch("failed", now.AddHours(-10), completedHoursAfterCreate: 0.5),
            CreateBatch("FAILED", now.AddHours(-11), completedHoursAfterCreate: 0.5),
            CreateBatch("Failed", now.AddHours(-12), completedHoursAfterCreate: 0.5),
        };

        var dashboard = IndexModel.BuildDashboard(batches, now);

        // All queued batches (regardless of case) should be counted as in progress
        // All running batches (regardless of case) should be counted as in progress
        Assert.Equal(6, dashboard.InProgress); // 3 queued + 3 running
        
        // All completed batches (regardless of case) within 24h should be counted
        Assert.Equal(3, dashboard.CompletedLast24h);
        
        // All failed batches (regardless of case) within 24h should be counted
        Assert.Equal(3, dashboard.FailedLast24h);
    }

    private static BatchEntity CreateBatch(string status, DateTimeOffset createdAt, double? completedHoursAfterCreate = null)
    {
        return new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user",
            InputFileId = Guid.NewGuid(),
            Status = status,
            Endpoint = "endpoint",
            CompletionWindow = TimeSpan.FromHours(24),
            Priority = 1,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(5),
            CompletedAt = completedHoursAfterCreate.HasValue ? createdAt.AddHours(completedHoursAfterCreate.Value) : null,
            Requests = new List<RequestEntity>()
        };
    }
}

