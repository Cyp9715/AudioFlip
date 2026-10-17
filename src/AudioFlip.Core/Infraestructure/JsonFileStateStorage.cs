namespace AudioFlip;

using System.Text.Json;

public sealed class JsonFileStateStorage : IStateStorage
{
    private readonly string path;
    private readonly Func<string?>? readLegacySettings;

    public JsonFileStateStorage(string path, Func<string?>? readLegacySettings = null)
    {
        this.path = Path.GetFullPath(path);
        this.readLegacySettings = readLegacySettings;
    }

    public AudioSwitcherState Load()
    {
        if (File.Exists(path))
        {
            try
            {
                return Read(path);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                if (File.Exists(path + ".bak"))
                    return Read(path + ".bak");
                throw new AudioSwitcherException($"Saved settings at '{path}' are invalid. Restore a backup before restarting AudioFlip.");
            }
        }

        if (File.Exists(path + ".bak"))
            return Read(path + ".bak");

        var legacy = readLegacySettings?.Invoke();
        if (string.IsNullOrWhiteSpace(legacy))
            return new();

        var state = Deserialize(legacy);
        Save(state);
        return state;
    }

    public void Save(AudioSwitcherState state)
    {
        Validate(state);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, path + ".bak");
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static AudioSwitcherState Read(string path)
    {
        // A CLI process can read the last complete snapshot while the UI
        // atomically replaces it with the next one.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return Deserialize(reader.ReadToEnd());
    }

    private static AudioSwitcherState Deserialize(string json)
    {
        var state = JsonSerializer.Deserialize<AudioSwitcherState>(json);
        Validate(state);
        return state!;
    }

    private static void Validate(AudioSwitcherState? state)
    {
        if (state?.Commands == null || state.Commands.Any(command =>
            command == null || command.Name == null || command.Hotkey == null
            || command.Devices == null || command.Devices.Any(device => device == null)))
        {
            throw new InvalidDataException("Saved settings contain an invalid command.");
        }
    }
}
