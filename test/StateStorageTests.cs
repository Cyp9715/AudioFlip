namespace AudioFlip.Tests;

using System.Text.Json;

public sealed class StateStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "AudioFlip.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacySettingsAreImportedWithoutChangingTheirContent()
    {
        var path = Path.Combine(directory, "settings.json");
        var original = new AudioSwitcherState
        {
            RunAtStartupMinimized = false,
            Commands = [new("Playback", AudioDeviceClass.Render, new(KeyModifiers.Control, Key.A), [DeviceReference.Encode("A", "Headset")])],
        };
        var legacy = JsonSerializer.Serialize(original);
        var storage = new JsonFileStateStorage(path, () => legacy);
        Assert.Equal(legacy, JsonSerializer.Serialize(storage.Load()));
        Assert.Equal(legacy, File.ReadAllText(path));
        Assert.Equal(legacy, JsonSerializer.Serialize(new JsonFileStateStorage(path, () => throw new Exception()).Load()));
    }

    [Fact]
    public void SettingsLargerThanEightKilobytesRoundTrip()
    {
        var path = Path.Combine(directory, "settings.json");
        var state = new AudioSwitcherState
        {
            Commands = Enumerable.Range(0, 200)
                .Select(index => new Command($"Command {index}", AudioDeviceClass.Render, devices: [DeviceReference.Encode($"ID-{index}", "Device")]))
                .ToList(),
        };
        var storage = new JsonFileStateStorage(path);
        storage.Save(state);
        Assert.True(new FileInfo(path).Length > 8192);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(storage.Load()));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Commands\":null}")]
    [InlineData("{\"Commands\":[null]}")]
    public void InvalidCurrentSettingsRecoverThePreviousCompleteSave(string corrupt)
    {
        var path = Path.Combine(directory, "settings.json");
        var storage = new JsonFileStateStorage(path);
        storage.Save(new() { DarkTheme = false });
        storage.Save(new() { DarkTheme = true });
        File.WriteAllText(path, corrupt);
        Assert.False(storage.Load().DarkTheme);
        Assert.Equal(corrupt, File.ReadAllText(path));
    }

    [Fact]
    public void MissingPrimarySettingsRecoverTheBackup()
    {
        var path = Path.Combine(directory, "settings.json");
        var storage = new JsonFileStateStorage(path);
        storage.Save(new() { DarkTheme = false });
        storage.Save(new() { DarkTheme = true });
        File.Delete(path);
        Assert.False(storage.Load().DarkTheme);
    }

    [Fact]
    public void InvalidSettingsWithoutABackupAreNeverSilentlyOverwritten()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{");
        Assert.Throws<AudioSwitcherException>(() => new JsonFileStateStorage(path).Load());
        Assert.Equal("{", File.ReadAllText(path));
    }

    [Fact]
    public void SaveCleansUpTemporaryFilesAndKeepsThePreviousVersion()
    {
        var path = Path.Combine(directory, "settings.json");
        var storage = new JsonFileStateStorage(path);
        storage.Save(new() { DarkTheme = false });
        storage.Save(new() { DarkTheme = true });
        Assert.True(storage.Load().DarkTheme);
        Assert.False(JsonSerializer.Deserialize<AudioSwitcherState>(File.ReadAllText(path + ".bak"))!.DarkTheme);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
