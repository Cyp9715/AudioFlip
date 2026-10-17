// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using MvvmGen;

[ViewModel]
public sealed partial class SettingsPageViewModel
{
    private readonly AudioSwitcher _audioSwitcher;
    private readonly IO _io;
    private readonly IApp app;
    private readonly ISettingsTransfer transfer;
    [Property] private bool _runInBackground;
    [Property] private bool _runAtStartup;
    [Property] private bool _runAtStartupMinimized;
    [Property] private bool _switchCommunicationDevice;
    [Property] private bool _showDisabledDevices;
    [Property] private bool _darkTheme;
    [Property] private bool _canTransfer = true;

    public SettingsPageViewModel(AudioSwitcher audioSwitcher, IO io, IApp app, ISettingsTransfer transfer)
    {
        _audioSwitcher = audioSwitcher;
        _io = io;
        this.app = app;
        this.transfer = transfer;
        OnInitialize();
    }

    public async Task LoadAsync()
    {
        await _audioSwitcher.LoadAsync();
        RunAtStartup = _audioSwitcher.RunAtStartup;
        RunInBackground = _audioSwitcher.RunInBackground;
        RunAtStartupMinimized = _audioSwitcher.RunAtStartupMinimized;
        SwitchCommunicationDevice = _audioSwitcher.SwitchCommunicationDevice;
        ShowDisabledDevices = _audioSwitcher.ShowDisabledDevices;
        DarkTheme = _audioSwitcher.DarkTheme;
    }

    [Command]
    public async Task SetRunAtStartupAsync()
    {
        try
        {
            if (!await _audioSwitcher.SetRunAtStartupTask(RunAtStartup))
            {
                var message = "Please go to Task Manager > Startup to enable it. Windows only allows you to enable it from the same place where you previously disabled it.";
                await _io.ShowErrorAsync(message);
            }
        }
        catch (Exception e)
        {
            await _io.ShowErrorAsync(e.Message);
        }
        finally
        {
            RunAtStartup = _audioSwitcher.RunAtStartup;
        }
    }

    [Command] public void SetRunAtStartupMinimized() => _audioSwitcher.SetRunAtStartupMinimized(RunAtStartupMinimized);
    [Command] public void SetRunInBackground() => _audioSwitcher.SetRunInBackground(RunInBackground);
    [Command] public void SetSwitchCommunicationDevice() => _audioSwitcher.SetSwitchCommunicationDevice(SwitchCommunicationDevice);
    [Command] public void SetShowDisabledDevices() => _audioSwitcher.SetShowDisabledDevices(ShowDisabledDevices);

    [Command]
    public void SetDarkTheme()
    {
        _audioSwitcher.SetDarkTheme(DarkTheme);
        app.UpdateTheme();
    }

    [Command]
    public async Task ExportAsync()
    {
        if (!CanTransfer)
            return;
        CanTransfer = false;
        try
        {
            await _audioSwitcher.LoadAsync();
            var backup = new SettingsBackup { Settings = _audioSwitcher.ExportSettings() };
            var name = await transfer.ExportAsync(backup.ToJson(), $"AudioFlip-backup-{DateTime.Now:yyyyMMdd-HHmmss}");
            if (name != null)
                await _io.ShowMessageAsync("Export complete", $"Settings saved to {name}.\n\nBefore reinstalling Windows, keep this file on another drive or in a backed-up cloud folder.");
        }
        catch (Exception e)
        {
            await _io.ShowErrorAsync($"Could not export settings.\n{e.Message}");
        }
        finally
        {
            CanTransfer = true;
        }
    }

    [Command]
    public async Task ImportAsync()
    {
        if (!CanTransfer)
            return;
        CanTransfer = false;
        try
        {
            await _audioSwitcher.LoadAsync();
            var json = await transfer.ReadImportAsync();
            if (json == null)
                return;
            var backup = SettingsBackup.Parse(json);
            var playback = await _audioSwitcher.AudioManager.GetAllDevicesAsync(AudioDeviceClass.Render);
            var recording = await _audioSwitcher.AudioManager.GetAllDevicesAsync(AudioDeviceClass.Capture);
            var plan = new SettingsImportPlan(backup, playback, recording);
            if (!await transfer.ConfirmImportAsync(plan))
                return;

            var errors = await _audioSwitcher.ImportSettingsAsync(plan.BuildState());
            await LoadAsync();
            app.UpdateTheme();
            var message = "Commands, hotkeys, and app preferences have been restored.\n\nRun at startup keeps this Windows installation's current permission. Check it above if needed.";
            if (plan.Devices.Any(x => x.SelectedOption.Device == null && !x.SelectedOption.Omit))
                message += "\n\nUnavailable selections were kept. They are skipped until they match a connected device. Review devices with duplicate names before using a command.";
            if (errors.Count > 0)
                message += "\n\nThese hotkeys could not be registered. Their commands were kept; choose different hotkeys or close the other app using them:\n" + string.Join("\n", errors);
            await _io.ShowMessageAsync("Import complete", message);
        }
        catch (Exception e)
        {
            await _io.ShowErrorAsync($"Could not import settings.\n{e.Message}");
        }
        finally
        {
            CanTransfer = true;
        }
    }
}
