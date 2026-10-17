// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using AudioFlip.Core.Application;

public sealed partial class AudioSwitcher : IDisposable
{
    public const string StartupTaskId = "Startup";
    private readonly IStateStorage stateStorage;
    private readonly IStartupTaskManager startupTaskManager;
    private readonly IHotkeyManager hotkeyManager;
    private readonly INotificationService notificationService;
    private readonly IO io;
    private readonly Dictionary<Hotkey, int> registeredHotkeys = new();
    private readonly SemaphoreSlim executionLock = new(1, 1);
    private Task? loadTask;
    private AudioSwitcherState state;

    public AudioSwitcher(
        IAudioManager audioManager,
        IStartupTaskManager startupTaskManager,
        IO io,
        IStateStorage stateStorage,
        IHotkeyManager hotkeyManager,
        INotificationService notificationService,
        AudioSwitcherState? initialState = null)
    {
        this.stateStorage = stateStorage;
        AudioManager = audioManager;
        state = initialState ?? new();
        this.startupTaskManager = startupTaskManager;
        this.hotkeyManager = hotkeyManager;
        this.notificationService = notificationService;
        this.io = io;
    }

    public IAudioManager AudioManager { get; }
    public IReadOnlyList<Command> Commands => state.Commands;
    public bool RunAtStartup => state.RunAtStartup;
    public bool RunAtStartupMinimized => state.RunAtStartupMinimized;
    public bool RunInBackground => state.RunInBackground;
    public bool ShowDisabledDevices => state.ShowDisabledDevices;
    public bool SwitchCommunicationDevice => state.SwitchCommunicationDevice;
    public bool DarkTheme => state.DarkTheme;

    public Task LoadAsync() => loadTask ??= LoadCoreAsync();

    public void Save()
    {
        stateStorage.Save(state);
    }

    public AudioSwitcherState ExportSettings() => SettingsBackup.Copy(state);

    public async Task<IReadOnlyList<string>> ImportSettingsAsync(AudioSwitcherState imported)
    {
        SettingsBackup.Validate(imported);
        await LoadAsync();
        await executionLock.WaitAsync();
        var previous = state;
        try
        {
            // The OS owns startup permission. Import never grants or revokes it.
            var replacement = SettingsBackup.Copy(imported) with { RunAtStartup = previous.RunAtStartup };
            stateStorage.SaveImportBackup(previous);
            state = replacement;

            // Keep old registrations until saving succeeds so a failed import
            // cannot lose a working hotkey to another process during rollback.
            var errors = UpdateHotkeys(removeObsolete: false);
            Save();
            RemoveObsoleteHotkeys();
            return errors;
        }
        catch
        {
            state = previous;
            RemoveObsoleteHotkeys();
            throw;
        }
        finally
        {
            executionLock.Release();
        }
    }

    public Command AddCommand(CommandName name, AudioDeviceClass deviceClass, Hotkey? hotkey = null, string[]? devices = null)
    {
        EnsureNameIsAvailable(name, deviceClass);
        var command = new Command(name, deviceClass, hotkey, devices);
        EnsureHotkeyIsAvailable(command);
        state.Commands.Add(command);
        try
        {
            var errors = UpdateHotkeys();
            EnsureHotkeyIsRegistered(command.Hotkey, errors);
            Save();
        }
        catch
        {
            state.Commands.Remove(command);
            UpdateHotkeys();
            throw;
        }

        return command;
    }

    public void RenameCommand(CommandName name, CommandName newName, AudioDeviceClass deviceClass)
    {
        var command = GetCommand(name, deviceClass);
        if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
            EnsureNameIsAvailable(newName, deviceClass);
        SaveCommand(command with { Name = newName }, name);
    }

    public void DeleteCommand(CommandName name, AudioDeviceClass deviceClass)
    {
        var command = GetCommand(name, deviceClass);

        if (Commands.Count(x => x.DeviceClass == command.DeviceClass) <= 1)
            throw new AudioSwitcherException("Command list cannot be left empty.");

        var index = state.Commands.IndexOf(command);
        state.Commands.Remove(command);
        try
        {
            UpdateHotkeys();
            Save();
        }
        catch
        {
            state.Commands.Insert(index, command);
            UpdateHotkeys();
            throw;
        }
    }

    public void ChangeCommandHotkey(Command command, Hotkey hotkey)
    {
        SaveCommand(command with { Hotkey = hotkey });
    }

    public Task ExecuteCommandByHotkeyAsync(Hotkey hotkey)
    {
        var commands = Commands.Where(x => x.Hotkey == hotkey && hotkey != Hotkey.Empty)
            .OrderBy(x => x.DeviceClass == AudioDeviceClass.Render ? 0 : 1).ToArray();
        if (!commands.Any())
            throw new AudioSwitcherException($"Command with hotkey '{hotkey}' doesn't exist.");

        return ExecuteCommandsAsync(commands);
    }

    public Task ExecuteCommandAsync(Command command)
    {
        return ExecuteCommandsAsync(new[] { command });
    }

    public async Task ExecuteCommandsAsync(IEnumerable<Command> commands)
    {
        var snapshot = commands.ToArray();
        await executionLock.WaitAsync();
        try
        {
            var notifications = new GroupNotificationService(notificationService);
            foreach (var command in snapshot)
                await ToggleCoreAsync(command.DeviceClass, command.Devices, notifications);
            await notifications.CommitAsync();
        }
        finally
        {
            executionLock.Release();
        }
    }

    public void RegisterHotkeys()
    {
        var errors = UpdateHotkeys();
        if (errors.Count > 0)
            throw new AudioSwitcherException(string.Join(Environment.NewLine, errors));
    }

    public void Dispose()
    {
        foreach (var id in registeredHotkeys.Values)
            hotkeyManager.UnregisterHotkey(id);
        registeredHotkeys.Clear();
    }

    public void SaveCommand(Command command, string? name = null)
    {
        name ??= command.Name;
        var index = state.Commands.FindIndex(x =>
            x.DeviceClass == command.DeviceClass
            && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        var previous = state.Commands[index];
        EnsureHotkeyIsAvailable(command, previous.Name);
        state.Commands[index] = command;
        try
        {
            var errors = UpdateHotkeys();
            if (command.Hotkey != previous.Hotkey)
                EnsureHotkeyIsRegistered(command.Hotkey, errors);
            Save();
        }
        catch
        {
            state.Commands[index] = previous;
            UpdateHotkeys();
            throw;
        }
    }

    public async Task ToggleAsync(AudioDeviceClass deviceClass, IEnumerable<string> devices, INotificationService notificationService)
    {
        var snapshot = devices.ToArray();
        await executionLock.WaitAsync();
        try
        {
            await ToggleCoreAsync(deviceClass, snapshot, notificationService);
        }
        finally
        {
            executionLock.Release();
        }
    }

    public async Task<bool> SetRunAtStartupTask(bool enable)
    {
        var startupTask = await startupTaskManager.GetTaskAsync(StartupTaskId);

        if (!enable)
            await startupTask.DisableAsync();
        else
            await startupTask.EnableAsync();

        var isEnabled = await startupTask.IsEnabledAsync();
        state.RunAtStartup = isEnabled;

        if (enable && !isEnabled)
            return false;

        Save();
        return true;
    }

    public void SetRunAtStartupMinimized(bool runAtStartupMinimized)
    {
        state.RunAtStartupMinimized = runAtStartupMinimized;
        Save();
    }

    public void SetRunInBackground(bool runInBackground)
    {
        state.RunInBackground = runInBackground;
        Save();
    }

    public void SetSwitchCommunicationDevice(bool switchCommunicationDevice)
    {
        state.SwitchCommunicationDevice = switchCommunicationDevice;
        Save();
    }

    public void SetShowDisabledDevices(bool showDisabledDevices)
    {
        state.ShowDisabledDevices = showDisabledDevices;
        Save();
    }

    public void SetDarkTheme(bool darkTheme)
    {
        state.DarkTheme = darkTheme;
        Save();
    }

    private async Task LoadCoreAsync()
    {
        state = stateStorage.Load();
        var task = await startupTaskManager.GetTaskAsync(StartupTaskId);
        if (task != null)
            state.RunAtStartup = await task.IsEnabledAsync();

        state.EnsureDefaultCommands();

        var errors = UpdateHotkeys();
        if (errors.Count > 0)
            await notificationService.ShowNotificationAsync(string.Join(Environment.NewLine, errors));
    }

    private void RemoveObsoleteHotkeys()
    {
        var desired = Commands.Select(x => x.Hotkey).Where(x => x != Hotkey.Empty).Distinct().ToArray();
        foreach (var hotkey in registeredHotkeys.Keys.Except(desired).ToArray())
        {
            hotkeyManager.UnregisterHotkey(registeredHotkeys[hotkey]);
            registeredHotkeys.Remove(hotkey);
        }
    }

    private List<string> UpdateHotkeys(bool removeObsolete = true)
    {
        if (removeObsolete)
            RemoveObsoleteHotkeys();

        var desired = Commands.Select(x => x.Hotkey).Where(x => x != Hotkey.Empty).Distinct().ToArray();
        var errors = new List<string>();
        foreach (var hotkey in desired.Where(x => !registeredHotkeys.ContainsKey(x)))
        {
            // RegisterHotKey reserves 0xC000..0xFFFF for shared DLLs. Command
            // names are not IDs: playback and recording can share one shortcut.
            var id = Enumerable.Range(1, 0xBFFF).FirstOrDefault(x => !registeredHotkeys.ContainsValue(x));
            try
            {
                if (id == 0)
                    throw new AudioSwitcherException("No more global hotkeys can be registered.");
                hotkeyManager.RegisterHotkey(id, hotkey);
                registeredHotkeys.Add(hotkey, id);
            }
            catch (AudioSwitcherException e)
            {
                errors.Add($"{hotkey}: {e.Message}");
            }
        }

        return errors;
    }

    private async Task ToggleCoreAsync(AudioDeviceClass deviceClass, IEnumerable<string> devices, INotificationService notificationService)
    {
        var toggler = new AudioSwitcherToggler(AudioManager, notificationService);
        await toggler.ToggleAsync(deviceClass, devices, state.SwitchCommunicationDevice);
    }

    private bool TryGetCommand(string name, AudioDeviceClass deviceClass, [MaybeNullWhen(false)] out Command command)
    {
        command = Commands.FirstOrDefault(x =>
            x.DeviceClass == deviceClass
            && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return command != null;
    }

    private void EnsureHotkeyIsAvailable(Command command, string? originalName = null)
    {
        if (command.Hotkey == Hotkey.Empty)
            return;

        var existing = Commands.FirstOrDefault(x => x.DeviceClass == command.DeviceClass
            && x.Hotkey == command.Hotkey
            && !string.Equals(x.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            throw new AudioSwitcherException($"'{command.Hotkey}' is already in use by command '{existing.Name}'");
    }

    private void EnsureHotkeyIsRegistered(Hotkey hotkey, List<string> errors)
    {
        if (hotkey != Hotkey.Empty && !registeredHotkeys.ContainsKey(hotkey))
            throw new AudioSwitcherException(string.Join(Environment.NewLine, errors));
    }

    private void EnsureNameIsAvailable(string name, AudioDeviceClass deviceClass)
    {
        if (TryGetCommand(name, deviceClass, out var _))
            throw new AudioSwitcherException($"Name '{name}' is already in use.");
    }

    private Command GetCommand(string name, AudioDeviceClass deviceClass)
    {
        if (!TryGetCommand(name, deviceClass, out var command))
            throw new AudioSwitcherException($"Command '{name}' doesn't exist.");

        return command;
    }
}
