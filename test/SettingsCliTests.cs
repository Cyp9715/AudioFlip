namespace AudioFlip.Tests;

using System.Text.Json;

public sealed class SettingsCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "AudioFlip.Tests", Guid.NewGuid().ToString("N"));

    private static AudioSwitcherState State() => new()
    {
        DarkTheme = false,
        Commands =
        [
            new("Output", AudioDeviceClass.Render, new(KeyModifiers.Control, Key.A), [DeviceReference.Encode("old-output", "Headset")]),
            new("Input", AudioDeviceClass.Capture, devices: [DeviceReference.Encode("old-input", "Microphone")]),
        ],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedCommandsSupportUnicodeAndSpacesInPaths(bool import)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "설정 backup.json");
        var storage = new InMemoryStateStorage();
        storage.Save(State());
        if (import)
            File.WriteAllText(path, new SettingsBackup { Settings = State() }.ToJson());
        var cli = new CLI(new TestAudioManager(), storage, new TestNotifications());
        var command = CLI.BuildSettingsCommand(path, import);
        await cli.RunAsync(command["AudioFlip ".Length..]);
        Assert.NotNull(cli.Output);
        Assert.Equal(2, import ? storage.Load().Commands.Count : SettingsBackup.Parse(File.ReadAllText(path)).Settings.Commands.Count);
    }

    [Fact]
    public async Task ExportCreatesDefaultsOnFirstRunWithoutMutatingStoredState()
    {
        var storage = new InMemoryStateStorage();
        var cli = new CLI(new TestAudioManager(), storage, new TestNotifications());
        var path = Path.Combine(directory, "first.json");
        await cli.RunAsync(["-export", path]);
        Assert.Equal(2, SettingsBackup.Parse(File.ReadAllText(path)).Settings.Commands.Count);
        Assert.Empty(storage.Load().Commands);
    }

    [Fact]
    public async Task ImportRemapsDevicesByClassAndPreservesStartupPermissionAndBackup()
    {
        Directory.CreateDirectory(directory);
        var storage = new InMemoryStateStorage();
        storage.Save(State() with { RunAtStartup = false, DarkTheme = true });
        var audio = new TestAudioManager();
        audio.DevicesByClass[AudioDeviceClass.Render] = [new("new-output", "Headset")];
        audio.DevicesByClass[AudioDeviceClass.Capture] = [new("new-input", "Microphone")];
        var path = Path.Combine(directory, "import.json");
        File.WriteAllText(path, new SettingsBackup { Settings = State() }.ToJson());
        await new CLI(audio, storage, new TestNotifications()).RunAsync([path, "-import"]);
        var restored = storage.Load();
        Assert.False(restored.DarkTheme);
        Assert.False(restored.RunAtStartup);
        Assert.True(storage.ImportBackup!.DarkTheme);
        Assert.Equal("new-output", DeviceReference.GetSavedId(restored.Commands[0].Devices[0]));
        Assert.Equal("new-input", DeviceReference.GetSavedId(restored.Commands[1].Devices[0]));
        Assert.Empty(audio.Changes);
    }

    [Fact]
    public async Task ImportUsesRunningInstanceInsteadOfWritingBehindItsCache()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "import.json");
        File.WriteAllText(path, new SettingsBackup { Settings = State() }.ToJson());
        var storage = new TestStorage();
        AudioSwitcherState? received = null;
        var cli = new CLI(new TestAudioManager(), storage, new TestNotifications(), state =>
        {
            received = state;
            return Task.FromResult<IReadOnlyList<string>>(["Hotkey conflict"]);
        });
        await cli.RunAsync(["-import", path]);
        Assert.NotNull(received);
        Assert.Equal(0, storage.Saves);
        Assert.Null(storage.ImportBackup);
        Assert.Contains("Hotkey conflict", cli.Output);
    }

    [Theory]
    [InlineData("-export")]
    [InlineData("-import -export")]
    [InlineData("-import -playback")]
    [InlineData("-import a.json b.json")]
    public async Task InvalidSettingsArgumentsDoNotModifyStorage(string args)
    {
        var storage = new TestStorage();
        await Assert.ThrowsAsync<CLIException>(() => new CLI(new TestAudioManager(), storage, new TestNotifications()).RunAsync(args));
        Assert.Equal(0, storage.Saves);
        Assert.Null(storage.ImportBackup);
    }

    [Fact]
    public async Task InvalidFileDoesNotModifySettings()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "invalid.json");
        File.WriteAllText(path, "{");
        var storage = new TestStorage();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CLI(new TestAudioManager(), storage, new TestNotifications()).RunAsync([path, "-import"]));
        Assert.Equal(0, storage.Saves);
        Assert.Null(storage.ImportBackup);
    }

    [Fact]
    public async Task RepeatedExportAtomicallyReplacesTheDestination()
    {
        var path = Path.Combine(directory, "backup.json");
        var storage = new InMemoryStateStorage();
        var cli = new CLI(new TestAudioManager(), storage, new TestNotifications());
        storage.Save(State());
        await cli.RunAsync([path, "-export"]);
        storage.Save(State() with { DarkTheme = true });
        await cli.RunAsync([path, "-export"]);
        Assert.True(SettingsBackup.Parse(File.ReadAllText(path)).Settings.DarkTheme);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task PipeImportUpdatesHotkeysAndSurvivesARejectedRequest()
    {
        var services = new TestServices();
        await services.Switcher.LoadAsync();
        var pipeName = "AudioFlip.Tests." + Guid.NewGuid().ToString("N");
        using var server = new SettingsImportPipe(pipeName, services.Switcher.ImportSettingsAsync);
        services.Storage.FailSave = true;
        await Assert.ThrowsAsync<AudioSwitcherException>(() => SettingsImportPipe.SendAsync(pipeName, State()));
        Assert.DoesNotContain(services.Switcher.Commands, x => x.Name == "Output");
        services.Storage.FailSave = false;
        Assert.Empty(await SettingsImportPipe.SendAsync(pipeName, State()));
        Assert.Contains(services.Switcher.Commands, x => x.Name == "Output");
        Assert.Contains(State().Commands[0].Hotkey, services.Hotkeys.Registered.Values);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
