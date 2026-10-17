namespace AudioFlip.Tests;

internal sealed class TestServices
{
    public TestServices(params Command[] commands)
    {
        Storage.State.Commands.AddRange(commands);
        Switcher = new(Audio, new StartupManager(), IO, Storage, Hotkeys, Notifications);
    }

    public TestAudioManager Audio { get; } = new();
    public TestStorage Storage { get; } = new();
    public TestHotkeyManager Hotkeys { get; } = new();
    public TestNotifications Notifications { get; } = new();
    public TestIO IO { get; } = new();
    public AudioSwitcher Switcher { get; }

    public AudioPageViewModel CreatePage(AudioEvents? events = null)
    {
        events ??= new();
        return new(Switcher, events, new AudioDeviceWatcher(events), Audio, Notifications,
            IO, new SyncDispatcher(), new Clipboard(), new App());
    }

    private sealed class StartupManager : IStartupTaskManager
    {
        public Task<IStartupTask> GetTaskAsync(string id) => Task.FromResult<IStartupTask>(new Startup());
    }

    private sealed class Startup : IStartupTask
    {
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
        public Task EnableAsync() => Task.CompletedTask;
        public Task DisableAsync() => Task.CompletedTask;
    }

    private sealed class Clipboard : IClipboard
    {
        public void SetTextContent(string text) { }
    }

    private sealed class App : IApp
    {
        public void UpdateTheme() { }
    }
}

internal sealed class TestStorage : IStateStorage
{
    public AudioSwitcherState State { get; } = new();
    public int Loads { get; private set; }
    public int Saves { get; private set; }
    public AudioSwitcherState Load() { ++Loads; return State; }
    public void Save(AudioSwitcherState state) { ++Saves; }
}

internal sealed class TestHotkeyManager : IHotkeyManager
{
    public Dictionary<int, Hotkey> Registered { get; } = new();
    public HashSet<Hotkey> Rejected { get; } = new();
    public int RegistrationCalls { get; private set; }
    public void RegisterHotkey(int id, Hotkey hotkey)
    {
        ++RegistrationCalls;
        if (Rejected.Contains(hotkey))
            throw new AudioSwitcherException("The requested hotkey is in use.");
        Registered.Add(id, hotkey);
    }
    public void UnregisterHotkey(int id) => Registered.Remove(id);
}

internal sealed class TestNotifications : INotificationService
{
    public List<string> Messages { get; } = new();
    public Func<Task>? OnShow { get; set; }
    public Task ShowNotificationAsync(string message)
    {
        Messages.Add(message);
        return OnShow?.Invoke() ?? Task.CompletedTask;
    }
}

internal sealed class TestIO : IO
{
    public string? NextInput { get; set; }
    public List<string> Messages { get; } = new();
    public Task ShowMessageAsync(string title, string message) { Messages.Add(message); return Task.CompletedTask; }
    public Task<string?> GetMessageAsync(string message, string defaultValue = "") => Task.FromResult(NextInput);
}

internal sealed class TestAudioManager : IAudioManager
{
    public AudioDevice[] Devices { get; set; } = [];
    public Dictionary<AudioDeviceRoleType, string> Defaults { get; } = new();
    public Dictionary<string, AudioDeviceState> States { get; } = new();
    public List<(string Id, AudioDeviceRoleType Role)> Changes { get; } = new();
    public bool FailCommunicationChange { get; set; }
    public Func<Task>? BeforeEnumeration { get; set; }
    public int Enumerations { get; private set; }

    public async Task<AudioDevice[]> GetAllDevicesAsync(AudioDeviceClass type)
    {
        ++Enumerations;
        if (BeforeEnumeration != null)
            await BeforeEnumeration();
        return Devices;
    }

    public string GetDefaultAudioId(AudioDeviceClass type, AudioDeviceRoleType role) => Defaults.GetValueOrDefault(role, "");
    public bool IsDefault(string id, AudioDeviceClass type, AudioDeviceRoleType role) => GetDefaultAudioId(type, role) == id;
    public bool SetDefaultDevice(string id, AudioDeviceRoleType role)
    {
        Changes.Add((id, role));
        if (FailCommunicationChange && role == AudioDeviceRoleType.Communications)
            return false;
        Defaults[role] = id;
        return true;
    }
    public void SetVisibility(string id, bool visible) { }
    public AudioDeviceState GetState(string id) => States.GetValueOrDefault(id, AudioDeviceState.Active);
}
