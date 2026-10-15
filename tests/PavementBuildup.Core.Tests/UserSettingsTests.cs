using PavementBuildup.Core;
using Xunit;

namespace PavementBuildup.Core.Tests;

[Collection("usage log")]
public class UserSettingsTests
{
    [Fact]
    public void Round_trips_and_clamps()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        UserSettings.Override = path;
        UserSettings.Reset();
        try
        {
            var s = UserSettings.Load();
            s.Name = "J. Smith"; s.DefaultScale = 20; s.TextSize = 400; s.CountUsage = false;
            s.Save();
            UserSettings.Reset();
            var back = UserSettings.Current;
            Assert.Equal("J. Smith", back.Name);
            Assert.Equal(20, back.DefaultScale);
            Assert.Equal(150, back.TextSize);
            Assert.False(back.CountUsage);
        }
        finally
        {
            UserSettings.Override = null;
            UserSettings.Reset();
            File.Delete(path);
        }
    }
}
