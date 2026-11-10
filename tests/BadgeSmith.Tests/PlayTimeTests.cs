using System.Globalization;
using BadgeSmith.Desktop.Services;

namespace BadgeSmith.Tests;

public class PlayTimeTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(0.4, "24 min")]
    [InlineData(0.9, "54 min")]
    [InlineData(1, "1.0 h")]
    [InlineData(2.14, "2.1 h")]
    public void ShowsMinutesBelowOneHour(double hours, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, PlayTime.Format(hours));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
