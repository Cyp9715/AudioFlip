namespace AudioFlip.Tests;

public sealed class AudioPageTests
{
    [Fact]
    public async Task RematchedSelectionAndDisconnectedDevicesSurviveHotkeyEdits()
    {
        var missing = DeviceReference.Encode("C", "Disconnected");
        var services = new TestServices(new Command("Playback", AudioDeviceClass.Render, devices: [DeviceReference.Encode("A", "Headset"), missing]));
        using var page = services.CreatePage();
        var selected = new List<object>();
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false, selected);
        page.Devices.Add(new() { Id = "B", FullName = "Headset", IsEnabled = true });
        await page.LoadCommandAsync(page.SelectedCommand);
        Assert.Equal("B", Assert.IsType<AudioDeviceViewModel>(Assert.Single(selected)).Id);
        page.SetHotkey(new(KeyModifiers.Control, Key.A));
        page.SaveSelectedCommand();
        var saved = services.Switcher.Commands.Single(x => x.Name == "Playback");
        Assert.Contains(missing, saved.Devices);
        Assert.Contains(DeviceReference.Encode("B", "Headset"), saved.Devices);
    }

    [Fact]
    public async Task AutomaticRemovalDoesNotOverwriteTheSavedSelection()
    {
        var reference = DeviceReference.Encode("A", "Headset");
        var services = new TestServices(new Command("Playback", AudioDeviceClass.Render, devices: [reference]));
        using var page = services.CreatePage();
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false);
        page.Devices.Add(new() { Id = "A", FullName = "Headset", IsEnabled = true });
        page.FilteredDevices.CollectionChanged += (_, _) => page.DeviceSelectionChanged();
        page.Devices.Clear();
        Assert.Equal(new[] { reference }, services.Switcher.Commands.Single(x => x.Name == "Playback").Devices);
        Assert.Equal(0, services.Storage.Saves);
    }

    [Fact]
    public async Task WhitespaceInRenameDoesNotDisconnectTheViewModelFromItsCommand()
    {
        var services = new TestServices(new Command("Playback", AudioDeviceClass.Render));
        using var page = services.CreatePage();
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false);
        services.IO.NextInput = "  Speakers  ";
        await page.RenameAsync();
        Assert.Equal("Speakers", page.SelectedCommand.Name);
        page.SetHotkey(new(KeyModifiers.Control, Key.A));
        Assert.Equal(new(KeyModifiers.Control, Key.A), services.Switcher.Commands.Single(x => x.Name == "Speakers").Hotkey);
    }

    [Fact]
    public async Task RepeatedPageLoadsDoNotDuplicateCommands()
    {
        var services = new TestServices();
        using var page = services.CreatePage();
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false);
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false);
        Assert.Single(page.Commands);
        Assert.True(page.CanExecute());
    }

    [Fact]
    public async Task RecordingDefaultEventsDoNotClearPlaybackDefaults()
    {
        var services = new TestServices();
        var events = new AudioEvents();
        using var page = services.CreatePage(events);
        await page.InitializeAsync(AudioDeviceClass.Render, watch: false);
        var device = new AudioDeviceViewModel { Id = "A", FullName = "Headset", IsDefault = true };
        page.Devices.Add(device);
        events.Raise(new DefaultAudioDeviceChanged(AudioDeviceRoleType.Default, "Mic", AudioDeviceClass.Capture));
        Assert.True(device.IsDefault);
    }
}
