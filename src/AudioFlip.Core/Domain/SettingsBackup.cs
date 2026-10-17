namespace AudioFlip;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record SettingsBackup
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumFileSize = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Format { get; init; } = "AudioFlip settings";
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public string AppVersion { get; init; } = AudioSwitcherState.Version;
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    [JsonRequired]
    public AudioSwitcherState Settings { get; init; } = null!;

    public string ToJson()
    {
        Validate(Settings);
        var json = JsonSerializer.Serialize(this, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumFileSize)
            throw new InvalidDataException("The settings backup exceeds the 16 MB size limit.");
        return json;
    }

    public static SettingsBackup Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumFileSize)
            throw new InvalidDataException("The settings backup exceeds the 16 MB size limit.");
        try
        {
            // Check the envelope before deserializing: missing fields must not
            // silently acquire defaults and look like a valid backup.
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(Format), out var format)
                || format.GetString() != "AudioFlip settings"
                || !root.TryGetProperty(nameof(FormatVersion), out var version)
                || !version.TryGetInt32(out var number) || number != CurrentFormatVersion)
            {
                throw new InvalidDataException("This is not a supported AudioFlip settings backup.");
            }

            var backup = JsonSerializer.Deserialize<SettingsBackup>(json)
                ?? throw new InvalidDataException("The settings backup is empty.");
            Validate(backup.Settings);
            return backup;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("The settings backup is incomplete or invalid.", e);
        }
    }

    public static AudioSwitcherState Copy(AudioSwitcherState state) => state with
    {
        Commands = state.Commands.Select(command => command with { Devices = command.Devices.ToArray() }).ToList(),
    };

    public static void Validate(AudioSwitcherState? state)
    {
        if (state?.Commands == null || state.Commands.Count > 10000)
            throw new InvalidDataException("The settings backup contains an invalid command list.");
        foreach (var command in state.Commands)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name) || command.Name != command.Name.Trim()
                || command.DeviceClass is not (AudioDeviceClass.Render or AudioDeviceClass.Capture)
                || command.Action is not (0 or CommandType.Set)
                || command.Hotkey == null || !Hotkey.Validate(command.Hotkey.Modifiers, command.Hotkey.Key)
                || command.Devices == null || command.Devices.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("The settings backup contains an invalid command or hotkey.");
            }
        }

        foreach (var type in new[] { AudioDeviceClass.Render, AudioDeviceClass.Capture })
        {
            var commands = state.Commands.Where(x => x.DeviceClass == type).ToArray();
            if (commands.Length == 0
                || commands.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != commands.Length
                || commands.Where(x => x.Hotkey != Hotkey.Empty).GroupBy(x => x.Hotkey).Any(x => x.Count() > 1))
            {
                throw new InvalidDataException("Each device type needs at least one command, unique command names, and unique hotkeys.");
            }
        }
    }
}
