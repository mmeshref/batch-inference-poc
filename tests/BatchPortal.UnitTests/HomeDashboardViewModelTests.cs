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
        var now = new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc);
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
            .Select(b => b.CreatedAt.UtcDateTime)
            .ToList();
        Assert.Equal(expected, sorted);

        var breachedItem = dashboard.RecentBatches.First(b => b.IsSlaBreached);
        Assert.True(breachedItem.IsSlaBreached);
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

