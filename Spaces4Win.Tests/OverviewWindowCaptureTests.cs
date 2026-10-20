using Spaces4Win.Overview;

namespace Spaces4Win.Tests;

public class OverviewWindowCaptureTests
{
    [Fact]
    public void Uniform_black_frame_is_rejected()
    {
        var samples = new uint[16];
        Assert.True(OverviewWindowCapture.IsUniformDark(samples));
    }

    [Fact]
    public void Dark_theme_with_contrast_is_kept()
    {
        var samples = new uint[16];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0x001E1E1E;
        }

        samples[0] = 0x00E8E8E8;
        samples[3] = 0x004A9EFF;
        Assert.False(OverviewWindowCapture.IsUniformDark(samples));
    }
}
