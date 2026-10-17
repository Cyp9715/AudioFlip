namespace AudioFlip.Tests;

public sealed class HotkeyTests
{
    private static readonly Hotkey First = new(KeyModifiers.Control, Key.A);
    private static readonly Hotkey Second = new(KeyModifiers.Menu, Key.Z);

    [Fact]
    public async Task PlaybackAndRecordingShareOneNativeRegistration()
    {
        var services = new TestServices(new Command("Shared", AudioDeviceClass.Render, First), new("Shared", AudioDeviceClass.Capture, First));
        await services.Switcher.LoadAsync();
        Assert.Single(services.Hotkeys.Registered);
        Assert.All(services.Hotkeys.Registered.Keys, id => Assert.InRange(id, 1, 0xBFFF));
    }

    [Fact]
    public async Task ClearingAHotkeyUnregistersIt()
    {
        var command = new Command("Playback", AudioDeviceClass.Render, First);
        var services = new TestServices(command);
        await services.Switcher.LoadAsync();
        services.Switcher.ChangeCommandHotkey(command, Hotkey.Empty);
        Assert.Empty(services.Hotkeys.Registered);
    }

    [Fact]
    public async Task DeletingOneOfTwoSharedCommandsKeepsTheShortcut()
    {
        var services = new TestServices(new Command("Shared", AudioDeviceClass.Render, First),
            new("Shared", AudioDeviceClass.Capture, First), new("Other", AudioDeviceClass.Render));
        await services.Switcher.LoadAsync();
        services.Switcher.DeleteCommand("Shared", AudioDeviceClass.Render);
        Assert.Equal(First, Assert.Single(services.Hotkeys.Registered).Value);
    }

    [Fact]
    public async Task RegistrationFailureRestoresThePreviousCommandAndShortcut()
    {
        var command = new Command("Playback", AudioDeviceClass.Render, First);
        var services = new TestServices(command);
        await services.Switcher.LoadAsync();
        services.Hotkeys.Rejected.Add(Second);
        Assert.Throws<AudioSwitcherException>(() => services.Switcher.ChangeCommandHotkey(command, Second));
        Assert.Equal(First, services.Switcher.Commands.Single(x => x.Name == "Playback").Hotkey);
        Assert.Equal(First, Assert.Single(services.Hotkeys.Registered).Value);
        Assert.Equal(0, services.Storage.Saves);
    }

    [Fact]
    public async Task LoadIsIdempotentAcrossPages()
    {
        var services = new TestServices(new Command("Playback", AudioDeviceClass.Render, First));
        await services.Switcher.LoadAsync();
        await services.Switcher.LoadAsync();
        Assert.Equal(1, services.Storage.Loads);
        Assert.Equal(1, services.Hotkeys.RegistrationCalls);
    }

    [Fact]
    public async Task NamesRemainIndependentAcrossDeviceClasses()
    {
        var services = new TestServices();
        await services.Switcher.LoadAsync();
        services.Switcher.AddCommand("Shared", AudioDeviceClass.Render);
        services.Switcher.AddCommand("Shared", AudioDeviceClass.Capture);
        services.Switcher.RenameCommand("Shared", "SHARED", AudioDeviceClass.Render);
        Assert.Throws<AudioSwitcherException>(() => services.Switcher.AddCommand("shared", AudioDeviceClass.Render));
        Assert.Equal(2, services.Switcher.Commands.Count(x => x.Name.Equals("shared", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task UnavailableStartupShortcutDoesNotPreventUnrelatedEdits()
    {
        var services = new TestServices(new Command("Busy", AudioDeviceClass.Render, First));
        services.Hotkeys.Rejected.Add(First);
        await services.Switcher.LoadAsync();
        services.Switcher.RenameCommand("Busy", "Renamed", AudioDeviceClass.Render);
        services.Switcher.AddCommand("New", AudioDeviceClass.Capture, Second);
        Assert.Contains(services.Switcher.Commands, x => x.Name == "Renamed");
        Assert.Equal(Second, Assert.Single(services.Hotkeys.Registered).Value);
        Assert.Single(services.Notifications.Messages);
    }
}
