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
    private readonly IClipboard clipboard;
    [Property] private bool _runInBackground;
    [Property] private bool _runAtStartup;
    [Property] private bool _runAtStartupMinimized;
    [Property] private bool _switchCommunicationDevice;
    [Property] private bool _showDisabledDevices;
    [Property] private bool _darkTheme;
    [Property] private bool _canTransfer = true;
    [Property] private string _exportCommandText = string.Empty;
    [Property] private string _importCommandText = string.Empty;

    public SettingsPageViewModel(AudioSwitcher audioSwitcher, IO io, IApp app, ISettingsTransfer transfer, IClipboard clipboard)
    {
        _audioSwitcher = audioSwitcher;
        _io = io;
        this.app = app;
        this.transfer = transfer;
        this.clipboard = clipboard;
        OnInitialize();
        UpdateCommands(transfer.LastPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AudioFlip-backup.json"));
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
            {
                UpdateCommands(name);
                await _io.ShowMessageAsync("Export complete", name);
            }
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
            var file = await transfer.ReadImportAsync();
            if (file == null)
                return;
            var backup = SettingsBackup.Parse(file.Contents);
            UpdateCommands(file.Path);
            var playback = await _audioSwitcher.AudioManager.GetAllDevicesAsync(AudioDeviceClass.Render);
            var recording = await _audioSwitcher.AudioManager.GetAllDevicesAsync(AudioDeviceClass.Capture);
            var plan = new SettingsImportPlan(backup, playback, recording);
            if (!await transfer.ConfirmImportAsync(plan))
                return;

            var errors = await _audioSwitcher.ImportSettingsAsync(plan.BuildState());
            await LoadAsync();
            app.UpdateTheme();
            var message = $"Imported {backup.Settings.Commands.Count} commands.";
            if (plan.Devices.Any(x => x.SelectedOption.Device == null && !x.SelectedOption.Omit))
                message += "\nUnavailable device selections were kept.";
            if (errors.Count > 0)
                message += "\n\nHotkeys already in use:\n" + string.Join("\n", errors);
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

    [Command] public void CopyExportCommandToClipboard() => clipboard.SetTextContent(ExportCommandText);
    [Command] public void CopyImportCommandToClipboard() => clipboard.SetTextContent(ImportCommandText);

    private void UpdateCommands(string path)
    {
        transfer.LastPath = path;
        ExportCommandText = CLI.BuildSettingsCommand(path, import: false);
        ImportCommandText = CLI.BuildSettingsCommand(path, import: true);
    }
}
