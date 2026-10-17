namespace AudioFlip;

public sealed class SettingsImportPlan
{
    public SettingsImportPlan(SettingsBackup backup, IReadOnlyCollection<AudioDevice> playback, IReadOnlyCollection<AudioDevice> recording)
    {
        SettingsBackup.Validate(backup.Settings);
        Backup = backup with { Settings = SettingsBackup.Copy(backup.Settings) };
        Devices = Backup.Settings.Commands
            .SelectMany(command => command.Devices.Select(reference => (command.DeviceClass, Reference: reference)))
            .Distinct()
            .Select(item => new ImportDeviceMapping(
                item.DeviceClass,
                item.Reference,
                item.DeviceClass == AudioDeviceClass.Render ? playback : recording))
            .ToArray();
    }

    public SettingsBackup Backup { get; }
    public IReadOnlyList<ImportDeviceMapping> Devices { get; }

    public AudioSwitcherState BuildState()
    {
        var selections = Devices.ToDictionary(x => (x.DeviceClass, x.Reference));
        return Backup.Settings with
        {
            Commands = Backup.Settings.Commands.Select(command => command with
            {
                Devices = command.Devices
                    .Select(reference => selections[(command.DeviceClass, reference)].GetReference())
                    .Where(reference => reference != null).Cast<string>().Distinct().ToArray(),
            }).ToList(),
        };
    }
}

public sealed class ImportDeviceMapping
{
    public ImportDeviceMapping(AudioDeviceClass deviceClass, string reference, IReadOnlyCollection<AudioDevice> devices)
    {
        DeviceClass = deviceClass;
        Reference = reference;
        SavedName = DeviceReference.GetSavedName(reference) ?? reference;
        var choices = new List<ImportDeviceChoice>
        {
            new("Keep saved selection", null),
            new("Remove from imported selections", null, Omit: true),
        };
        foreach (var device in devices.DistinctBy(x => x.Id))
        {
            var duplicateName = devices.Count(x => x.Name == device.Name) > 1;
            choices.Add(new(duplicateName ? $"{device.Name} — {device.Id}" : device.Name, device));
        }

        Options = choices;
        var savedId = DeviceReference.GetSavedId(reference);
        var exact = choices.FirstOrDefault(x => x.Device != null && x.Device.Id == savedId);
        var named = choices.Where(x => x.Device?.Name == SavedName).ToArray();
        SelectedOption = exact ?? (named.Length == 1 ? named[0] : choices[0]);
        MatchDescription = exact != null ? "Matched by ID"
            : named.Length == 1 ? "Matched by name — review device"
            : named.Length > 1 ? "Duplicate names — choose device"
            : "Unavailable — keep or replace";
    }

    public AudioDeviceClass DeviceClass { get; }
    public string Reference { get; }
    public string SavedName { get; }
    public string Category => DeviceClass == AudioDeviceClass.Render ? "Playback" : "Recording";
    public string MatchDescription { get; }
    public IReadOnlyList<ImportDeviceChoice> Options { get; }
    public ImportDeviceChoice SelectedOption { get; set; }

    public string? GetReference()
    {
        if (!Options.Contains(SelectedOption))
            throw new InvalidDataException("Choose a device from the import preview.");
        return SelectedOption.Omit ? null
            : SelectedOption.Device is { } device ? DeviceReference.Encode(device.Id, device.Name)
            : DeviceReference.ForImport(Reference);
    }
}

public sealed record ImportDeviceChoice(string Label, AudioDevice? Device, bool Omit = false);
