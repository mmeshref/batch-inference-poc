using BatchPortal.Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class BadgeStylesTests
{
    [Theory]
    [InlineData("Queued", "Queued")]
    [InlineData("completed", "Completed")]
    public void ForStatus_ShouldReturnLabel(string status, string expectedLabel)
    {
        var config = BadgeStyles.ForStatus(status);

        Assert.Contains("badge", config.CssClass);
        Assert.Equal(expectedLabel, config.Label);
    }

    [Theory]
    [InlineData("spot", "⚡")]
    [InlineData("dedicated", "🔒")]
    public void ForGpuPool_ShouldSetIcon(string pool, string expectedIcon)
    {
        var config = BadgeStyles.ForGpuPool(pool);

        Assert.Equal(expectedIcon, config.Icon);
        Assert.Contains("badge", config.CssClass);
    }
}

