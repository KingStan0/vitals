using Vitals.Cli;
using Vitals.Config;
using Vitals.Setup;

namespace Vitals.Tests;

public sealed class SetupPromptTests
{
    [Theory]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("yes", true)]
    [InlineData(" YES ", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void Confirm_accepts_only_yes(string answer, bool expected)
    {
        var output = new StringWriter();
        var input = new StringReader(answer + Environment.NewLine);

        Assert.Equal(expected, SetupPrompt.Confirm(output, input));
    }

    [Fact]
    public void Confirm_treats_end_of_input_as_no()
    {
        Assert.False(SetupPrompt.Confirm(TextWriter.Null, new StringReader(string.Empty)));
    }

    [Fact]
    public void Prompt_is_shown_only_for_an_elevated_interactive_dashboard()
    {
        var config = new AppConfig();
        var cli = CliOptions.Parse(Array.Empty<string>());

        Assert.True(SetupPrompt.ShouldPrompt(config, cli, installed: false, elevated: true));
        Assert.False(SetupPrompt.ShouldPrompt(config, cli, installed: true, elevated: true));
        Assert.False(SetupPrompt.ShouldPrompt(config, cli, installed: false, elevated: false));
    }

    [Fact]
    public void Prompt_respects_driver_free_render_once_and_config()
    {
        var disabled = new AppConfig { PromptForPawnIo = false };
        var noDriver = CliOptions.Parse(new[] { "--no-driver" });
        var renderOnce = CliOptions.Parse(new[] { "--render-once" });
        var cli = CliOptions.Parse(Array.Empty<string>());

        Assert.False(SetupPrompt.ShouldPrompt(disabled, cli, installed: false, elevated: true));
        Assert.False(SetupPrompt.ShouldPrompt(new AppConfig(), noDriver, installed: false, elevated: true));
        Assert.False(SetupPrompt.ShouldPrompt(new AppConfig(), renderOnce, installed: false, elevated: true));
    }
}
