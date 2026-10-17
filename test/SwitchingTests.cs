namespace AudioFlip.Tests;

public sealed class SwitchingTests
{
    [Fact]
    public async Task AlreadyDefaultSingleDeviceStillUpdatesCommunicationRole()
    {
        var services = new TestServices();
        services.Audio.Devices = [new("A", "Headset")];
        services.Audio.Defaults[AudioDeviceRoleType.Default] = "A";
        await services.Switcher.ToggleAsync(AudioDeviceClass.Render, ["A"], services.Notifications);
        Assert.Equal("A", services.Audio.Defaults[AudioDeviceRoleType.Communications]);
        Assert.Contains("Headset", Assert.Single(services.Notifications.Messages));
    }

    [Fact]
    public async Task MissingAndDisabledDevicesAreSkippedInCycleOrder()
    {
        var services = new TestServices();
        services.Audio.Devices = [new("A", "First"), new("B", "Disabled"), new("C", "Last")];
        services.Audio.Defaults[AudioDeviceRoleType.Default] = "A";
        services.Audio.States["B"] = AudioDeviceState.Disabled;
        await services.Switcher.ToggleAsync(AudioDeviceClass.Render, ["A", "missing", "B", "C"], services.Notifications);
        Assert.Equal("C", services.Audio.Defaults[AudioDeviceRoleType.Default]);
        Assert.DoesNotContain(services.Audio.Changes, x => x.Id == "missing" || x.Id == "B");
    }

    [Fact]
    public async Task CommunicationFailureDoesNotReportFullSuccess()
    {
        var services = new TestServices();
        services.Audio.Devices = [new("A", "Headset")];
        services.Audio.FailCommunicationChange = true;
        await services.Switcher.ToggleAsync(AudioDeviceClass.Render, ["A"], services.Notifications);
        Assert.Contains("could not be changed", Assert.Single(services.Notifications.Messages));
    }

    [Fact]
    public async Task CommandExecutionAwaitsGroupedNotifications()
    {
        var services = new TestServices();
        services.Audio.Devices = [new("A", "Headset")];
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.Notifications.OnShow = () => { notified.SetResult(); return release.Task; };
        var task = services.Switcher.ExecuteCommandAsync(new("Command", AudioDeviceClass.Render, devices: ["A"]));
        try
        {
            await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(task.IsCompleted);
        }
        finally
        {
            release.SetResult();
            await task;
        }
    }

    [Fact]
    public async Task RapidCommandsCannotRaceTheCurrentDevice()
    {
        var services = new TestServices();
        services.Audio.Devices = [new("A", "First"), new("B", "Second")];
        services.Audio.Defaults[AudioDeviceRoleType.Default] = "A";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.Audio.BeforeEnumeration = () => { entered.TrySetResult(); return release.Task; };
        var command = new Command("Cycle", AudioDeviceClass.Render, devices: ["A", "B"]);
        var first = services.Switcher.ExecuteCommandAsync(command);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = services.Switcher.ExecuteCommandAsync(command);
        try
        {
            Assert.Equal(1, services.Audio.Enumerations);
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(first, second);
        }
        Assert.Equal("A", services.Audio.Defaults[AudioDeviceRoleType.Default]);
    }
}
