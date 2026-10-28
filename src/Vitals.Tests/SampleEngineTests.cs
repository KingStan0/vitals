using Vitals.Config;
using Vitals.Sampling;

namespace Vitals.Tests;

public sealed class SampleEngineTests
{
    [Fact]
    public void No_driver_mode_reports_disabled_without_an_error()
    {
        using var engine = new SampleEngine(new AppConfig { NoDriver = true });

        Assert.False(engine.Status.LhmAvailable);
        Assert.Null(engine.Status.LhmError);
        Assert.Contains(engine.Status.Notes, note => note.Contains("--no-driver", StringComparison.Ordinal));
    }
}
