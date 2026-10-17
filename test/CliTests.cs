namespace AudioFlip.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("-recording", "Mic")]
    [InlineData("Mic", "-recording")]
    public void RecordingFlagWorksInEitherPosition(string first, string second)
    {
        CLI.ParseArguments([first, second], out var devices, out var type);
        Assert.Equal(AudioDeviceClass.Capture, type);
        Assert.Equal(new[] { "Mic" }, devices);
    }

    [Fact]
    public void ContradictoryDeviceClassesAreRejected()
    {
        Assert.Throws<CLIException>(() => CLI.ParseArguments(["-recording", "Mic", "-playback"], out _, out _));
    }

    [Fact]
    public void WhitespaceIsNotPartOfUnquotedArguments()
    {
        Assert.Equal(new[] { "Mic", "-recording" }, CLI.ParseArguments("  \tMic   -recording  "));
    }

    [Theory]
    [InlineData("USB Microphone")]
    [InlineData("마이크 | USB")]
    [InlineData("Microphone \"Studio\" \\ Interface")]
    [InlineData("Microphone\\")]
    public void GeneratedCommandsRoundTripNames(string name)
    {
        var device = new AudioDevice("A", name);
        var args = CLI.ParseArguments(CLI.BuildCommandArgs(AudioDeviceClass.Capture, [device]));
        CLI.ParseArguments(args, out var devices, out var type);
        Assert.Equal(AudioDeviceClass.Capture, type);
        Assert.Equal("A", CLI.GetDeviceId(Assert.Single(devices), [device]));
    }

    [Fact]
    public void UnterminatedQuotesAreRejected()
    {
        Assert.Throws<CLIException>(() => CLI.ParseArguments("\"Microphone"));
    }

    [Fact]
    public void OversizedHashIsRejected()
    {
        Assert.Throws<CLIException>(() => CLI.DecodeName("Microphone / 4294967296"));
    }
}
