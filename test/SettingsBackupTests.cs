namespace AudioFlip.Tests;

using System.Text.Json;

public sealed class SettingsBackupTests
{
    private static AudioSwitcherState State() => new()
    {
        DarkTheme = false,
        RunInBackground = false,
        Commands =
        [
            new("게임 | 헤드셋", AudioDeviceClass.Render, new(KeyModifiers.Control, Key.A),
                [DeviceReference.Encode("old-playback", "USB Headset"), DeviceReference.Encode("offline", "Speakers")]),
            new("마이크", AudioDeviceClass.Capture, new(KeyModifiers.Control, Key.A), [DeviceReference.Encode("old-input", "USB Headset")]),
        ],
    };

    [Fact]
    public void ExportAndImportPreserveCommandsHotkeysOrderPreferencesAndUnicode()
    {
        var original = State();
        var backup = SettingsBackup.Parse(new SettingsBackup { Settings = original }.ToJson());
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(backup.Settings));
    }

    [Fact]
    public void ReinstalledWindowsMapsBothDeviceClassesByNameAndRetainsMissingSelections()
    {
        var plan = new SettingsImportPlan(new() { Settings = State() },
            [new("new-output", "USB Headset")], [new("new-input", "USB Headset")]);
        var restored = plan.BuildState();
        Assert.Equal(DeviceReference.Encode("new-output", "USB Headset"), restored.Commands[0].Devices[0]);
        Assert.Equal(DeviceReference.Encode("offline", "Speakers"), restored.Commands[0].Devices[1]);
        Assert.Equal(DeviceReference.Encode("new-input", "USB Headset"), restored.Commands[1].Devices[0]);
        Assert.Equal("old-playback", DeviceReference.GetSavedId(plan.Backup.Settings.Commands[0].Devices[0]));
    }

    [Fact]
    public void DuplicateNamesNeedManualMappingAndCanBeExplicitlyRemoved()
    {
        var plan = new SettingsImportPlan(new() { Settings = State() },
            [new("left", "USB Headset"), new("right", "USB Headset")], []);
        var mapping = plan.Devices[0];
        Assert.Null(mapping.SelectedOption.Device);
        mapping.SelectedOption = mapping.Options.Single(x => x.Device?.Id == "right");
        plan.Devices[1].SelectedOption = plan.Devices[1].Options.Single(x => x.Omit);
        Assert.Equal(new[] { DeviceReference.Encode("right", "USB Headset") }, plan.BuildState().Commands[0].Devices);
    }

    [Fact]
    public void ExactFullIdSurvivesRenamingAndWinsOverTheOldName()
    {
        var plan = new SettingsImportPlan(new() { Settings = State() },
            [new("old-playback", "Renamed headset"), new("different", "USB Headset")], []);
        Assert.Equal("old-playback", plan.Devices[0].SelectedOption.Device!.Id);
    }

    [Fact]
    public void ImportDoesNotUseLegacyHashAsACrossComputerIdentity()
    {
        var state = State();
        state.Commands[0] = state.Commands[0] with { Devices = [$"ref|Headset|{"A0".GetDjb2HashCode()}"] };
        var plan = new SettingsImportPlan(new() { Settings = state }, [new("A1", "Monitor")], []);
        Assert.Null(plan.Devices[0].SelectedOption.Device);
        var reference = plan.BuildState().Commands[0].Devices[0];
        Assert.Equal("ref|Headset|0", reference);
        Assert.False(DeviceReference.TryResolveId(reference, [new("A1", "Monitor")], out _));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Format\":\"AudioFlip settings\",\"FormatVersion\":999,\"Settings\":{}}")]
    [InlineData("{\"Format\":\"AudioFlip settings\",\"FormatVersion\":1,\"Settings\":null}")]
    public void InvalidOrFutureBackupsAreRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => SettingsBackup.Parse(json));

    [Theory]
    [InlineData("name")]
    [InlineData("hotkey")]
    [InlineData("deviceClass")]
    [InlineData("emptyClass")]
    public void InvalidCommandsAreRejectedBeforeReplacingSettings(string invalid)
    {
        var state = State();
        if (invalid == "name")
            state.Commands.Add(state.Commands[0] with { Name = state.Commands[0].Name.ToUpperInvariant(), Hotkey = Hotkey.Empty });
        if (invalid == "hotkey")
            state.Commands[0] = state.Commands[0] with { Hotkey = new() { Key = Key.A } };
        if (invalid == "deviceClass")
            state.Commands[0] = state.Commands[0] with { DeviceClass = (AudioDeviceClass)999 };
        if (invalid == "emptyClass")
            state.Commands.RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => new SettingsBackup { Settings = state }.ToJson());
    }

    [Fact]
    public async Task FailedSaveKeepsPreviousStateAndItsHotkeyRegistration()
    {
        var services = new TestServices(new Command("Original", AudioDeviceClass.Render, new(KeyModifiers.Control, Key.Z)));
        await services.Switcher.LoadAsync();
        var registered = services.Hotkeys.Registered.Single();
        services.Storage.FailSave = true;
        await Assert.ThrowsAsync<IOException>(() => services.Switcher.ImportSettingsAsync(State()));
        Assert.Equal("Original", services.Switcher.Commands[0].Name);
        Assert.Equal(registered, services.Hotkeys.Registered.Single());
        Assert.NotNull(services.Storage.ImportBackup);
    }

    [Fact]
    public async Task FailedRestorePointAbortsBeforeChangingHotkeys()
    {
        var services = new TestServices(new Command("Original", AudioDeviceClass.Render, new(KeyModifiers.Control, Key.Z)));
        await services.Switcher.LoadAsync();
        var registrations = services.Hotkeys.RegistrationCalls;
        services.Storage.FailImportBackup = true;
        await Assert.ThrowsAsync<IOException>(() => services.Switcher.ImportSettingsAsync(State()));
        Assert.Equal("Original", services.Switcher.Commands[0].Name);
        Assert.Equal(registrations, services.Hotkeys.RegistrationCalls);
    }

    [Fact]
    public async Task ImportKeepsOsStartupPermissionAndReportsConflictingHotkeys()
    {
        var services = new TestServices();
        await services.Switcher.LoadAsync();
        var imported = State();
        imported.RunAtStartup = false;
        services.Hotkeys.Rejected.Add(imported.Commands[0].Hotkey);
        var errors = await services.Switcher.ImportSettingsAsync(imported);
        Assert.Single(errors);
        Assert.Equal(2, services.Switcher.Commands.Count);
        Assert.True(services.Switcher.RunAtStartup);
        Assert.False(services.Switcher.DarkTheme);
        Assert.False(services.Switcher.RunInBackground);
        Assert.Empty(services.Audio.Changes);
        imported.Commands.Clear();
        Assert.Equal(2, services.Switcher.Commands.Count);
    }
}
