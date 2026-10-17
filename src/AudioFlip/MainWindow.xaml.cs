// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.System;

public sealed partial class MainWindow : DesktopWindow
{
    private readonly Frame frame;
    private readonly AudioSwitcher audioSwitcher;
    private readonly IAudioManager audioManager;
    private bool exiting;
    private bool openingNotifyMenu;
    private SettingsImportPipe? importPipe;

    public MainWindow()
        : base(AudioSwitcherState.Title, "AudioSwitch.ico")
    {
        var services = App.Current.Services;
        Instance = this;
        frame = services.GetRequiredService<Frame>();
        audioManager = services.GetRequiredService<IAudioManager>();
        App.AudioSwitcher = audioSwitcher = services.GetRequiredService<AudioSwitcher>();
        SetWindowSize(400, 700);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        NavView.Content = frame;
        Closed += (_, _) =>
        {
            importPipe?.Dispose();
            if (frame.Content is IDisposable page)
                page.Dispose();
            audioSwitcher.Dispose();
        };
    }

    public UIElement TitleBar => CustomTitleBar;
    internal static MainWindow? Instance { get; private set; }

    public async Task InitializeAsync()
    {
        // Load startup preferences before App decides whether to show a window.
        await audioSwitcher.LoadAsync();
        App.Current.UpdateTheme();
        NavView.SelectedItem = NavView.MenuItems.First();
        importPipe = new SettingsImportPipe(App.ImportPipeName, ImportSettingsAsync);
    }

    public void RunInBackround()
    {
        Hide();
        ShellIcon.Create();
    }

    protected override bool OnClose()
    {
        if (exiting || !audioSwitcher.RunInBackground)
            return true;

        Hide();
        return false;
    }

    protected override async void OnHotkey(int id, KeyModifiers modifiers, Key key)
    {
        try
        {
            var audioPage = frame.Content as AudioPage;
            if (audioPage != null && audioPage.IsHotKeyFocused() && IsActive)
            {
                await audioPage.ViewModel.TrySetHotkeyAsync(new(modifiers, key));
                return;
            }

            await audioSwitcher.ExecuteCommandByHotkeyAsync(new Hotkey(modifiers, key));
        }
        catch (Exception e)
        {
            await NotificationService.ShowErrorNotificationAsync(e.Message);
        }
    }

    protected override async void OnShellNotifyIconMessage(PInvoke.User32.WindowMessage id)
    {
        try
        {
            switch (id)
            {
                case PInvoke.User32.WindowMessage.WM_LBUTTONDBLCLK:
                    Restore();
                    PInvoke.User32.SetForegroundWindow(Hwnd);
                    break;
                case PInvoke.User32.WindowMessage.WM_RBUTTONUP when !openingNotifyMenu:
                    openingNotifyMenu = true;
                    try
                    {
                        await OpenNotifyMenuAsync();
                    }
                    finally
                    {
                        openingNotifyMenu = false;
                    }

                    break;
            }
        }
        catch (Exception e)
        {
            await NotificationService.ShowErrorNotificationAsync(e.Message);
        }
    }

    private Task<IReadOnlyList<string>> ImportSettingsAsync(AudioSwitcherState state)
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var warnings = await audioSwitcher.ImportSettingsAsync(state);
                if (frame.Content is IDisposable page)
                    page.Dispose();
                App.Current.UpdateTheme();
                NavigateToSelectedPage();
                result.SetResult(warnings);
            }
            catch (Exception e)
            {
                result.SetException(e);
            }
        }))
        {
            result.SetException(new AudioSwitcherException("AudioFlip is closing. Retry the import."));
        }

        return result.Task;
    }

    private async Task OpenNotifyMenuAsync()
    {
        var menu = new WinMenu(Hwnd);
        var playbackDevices = await audioManager.GetAllDevicesAsync(AudioDeviceClass.Render);
        var recordingDevices = await audioManager.GetAllDevicesAsync(AudioDeviceClass.Capture);
        var showDisabledDevices = audioSwitcher.ShowDisabledDevices;

        var playbackCount = AddDevices(AudioDeviceClass.Render, playbackDevices);
        if (playbackCount > 0 && HasVisibleDevices(recordingDevices))
            menu.Add(null);
        AddDevices(AudioDeviceClass.Capture, recordingDevices);

        if (!menu.IsEmpty)
            menu.Add(null);
        menu.Add(new("&Exit", () =>
        {
            exiting = true;
            Close();
        }));
        menu.Track();

        bool HasVisibleDevices(IEnumerable<AudioDevice> devices)
        {
            return devices.Any(ShouldShowDevice);
        }

        int AddDevices(AudioDeviceClass deviceClass, IEnumerable<AudioDevice> devices)
        {
            var count = 0;

            foreach (var device in devices)
            {
                if (!ShouldShowDevice(device))
                    continue;

                var isDefault = audioManager.IsDefault(device.Id, deviceClass, AudioDeviceRoleType.Default);
                var isDisabled = !audioManager.IsActive(device.Id);
                async void Callback()
                {
                    try
                    {
                        await audioSwitcher.ToggleAsync(deviceClass, new[] { device.Id }, new NoNotificationService());
                    }
                    catch (Exception e)
                    {
                        await NotificationService.ShowErrorNotificationAsync(e.Message);
                    }
                }

                menu.Add(new(device.Name, Callback, isDefault, isDisabled));
                ++count;
            }

            return count;
        }

        bool ShouldShowDevice(AudioDevice device)
        {
            return showDisabledDevices || audioManager.IsActive(device.Id);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        NavigateToSelectedPage();
    }

    private void NavigateToSelectedPage()
    {
        if (NavView.SelectedItem == NavView.SettingsItem)
        {
            frame.Navigate(typeof(SettingsPage), this);
            return;
        }

        if (NavView.SelectedItem is NavigationViewItem item && item.Content is string content)
        {
            switch (content)
            {
                case "Playback":
                    frame.Navigate(typeof(AudioPage), AudioDeviceClass.Render);
                    break;
                case "Recording":
                    frame.Navigate(typeof(AudioPage), AudioDeviceClass.Capture);
                    break;
                case "About":
                    frame.Navigate(typeof(AboutPage));
                    break;
            }
        }
    }
}
