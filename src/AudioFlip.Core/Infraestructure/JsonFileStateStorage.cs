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
                    return RecoverBackup();
                throw new AudioSwitcherException($"Saved settings at '{path}' are invalid. Restore a backup before restarting AudioFlip.");
            }
        }

        if (File.Exists(path + ".bak"))
            return RecoverBackup();

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

        // Never rotate corrupt data over the last known-good backup, including
        // when a file was damaged after the application loaded it.
        var preservePrevious = false;
        if (File.Exists(path))
        {
            try
            {
                Read(path);
                preservePrevious = true;
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
            }
        }

        WriteAtomic(path, JsonSerializer.Serialize(state), preservePrevious ? path + ".bak" : null);
    }

    public void SaveImportBackup(AudioSwitcherState state)
    {
        var backup = new SettingsBackup { Settings = SettingsBackup.Copy(state) };
        var backupPath = Path.Combine(Path.GetDirectoryName(path)!, "settings.before-import.json");
        WriteAtomic(backupPath, backup.ToJson());
    }

    private AudioSwitcherState RecoverBackup()
    {
        var recovered = Read(path + ".bak");
        WriteAtomic(path, JsonSerializer.Serialize(recovered));
        return recovered;
    }

    private static void WriteAtomic(string destination, string json, string? backupPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
                File.Replace(temporaryPath, destination, backupPath);
            else
                File.Move(temporaryPath, destination);
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
