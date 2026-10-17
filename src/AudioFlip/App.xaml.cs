// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Activation = Windows.ApplicationModel.Activation;
using PInvoke;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Security.Principal;

public sealed partial class App : Application, IApp
{
    public const string Id = "JoseTorres:AudioFlip";
    private static Kernel32.SafeObjectHandle? _mutex;
    private static MainWindow? _window;

    static App()
    {
        Hotkey.KeyPrinter = key => key.ToVirtualKey().ToString();
    }

    public App()
    {
        Services = ConfigureServices();
        InitializeComponent();
    }

    public static AudioSwitcher AudioSwitcher { get; set; } = default!;
    public static MainWindow Window => _window!;
    public static ElementTheme Theme => AudioSwitcher.DarkTheme ? ElementTheme.Dark : ElementTheme.Light;
    public IServiceProvider Services { get; }
    public static new App Current => (App)Application.Current;
    internal static string ImportPipeName { get; } = GetImportPipeName();

    public void UpdateTheme()
    {
        if (_window?.Content is FrameworkElement e)
            e.RequestedTheme = Theme;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Length >= 2)
        {
            var exitCode = 0;
            var consoleAttached = Kernel32.GetConsoleWindow() != IntPtr.Zero || Kernel32.AttachConsole(-1);
            try
            {
                var storage = Services.GetRequiredService<IStateStorage>();
                var audioManager = Services.GetRequiredService<IAudioManager>();
                var cli = new CLI(audioManager, storage, new NoNotificationService(), ImportFromCliAsync);
                await cli.RunAsync(cmdArgs.Skip(1).ToArray());
                if (cli.Output != null)
                    Console.WriteLine(cli.Output);
            }
            catch (Exception e)
            {
                exitCode = 1;
                Console.Error.WriteLine(e.Message);
                var errorHandle = Kernel32.GetStdHandle(Kernel32.StdHandle.STD_ERROR_HANDLE);
                if (!consoleAttached && (errorHandle == IntPtr.Zero || errorHandle == new IntPtr(-1)))
                    User32.MessageBox(IntPtr.Zero, e.Message, AudioSwitcherState.Title, User32.MessageBoxOptions.MB_ICONERROR);
            }

            ExitNow(exitCode);
            return;
        }

        _mutex = Kernel32.CreateMutex(IntPtr.Zero, false, Id);
        if (_mutex == null ||
            Kernel32.GetLastError() == Win32ErrorCode.ERROR_ALREADY_EXISTS ||
            Kernel32.GetLastError() == Win32ErrorCode.ERROR_ACCESS_DENIED)
        {
            DesktopWindow.BroadcastRestore();
            ExitNow();
            return;
        }

        try
        {
            _window = new MainWindow();
            await _window.InitializeAsync();
        }
        catch (Exception e)
        {
            User32.MessageBox(IntPtr.Zero, e.Message, AudioSwitcherState.Title, User32.MessageBoxOptions.MB_ICONERROR);
            Environment.Exit(1);
            return;
        }

        var background = AppInstance.GetActivatedEventArgs() is Activation.IStartupTaskActivatedEventArgs task
            && task?.TaskId == AudioSwitcher.StartupTaskId
            && AudioSwitcher.RunAtStartupMinimized
            && AudioSwitcher.RunInBackground;

        if (background)
            _window.RunInBackround();
        else
            _window.Activate();

        // TODO: Remove workaround once Application.Exit() gets fixed
        // See https://github.com/microsoft/microsoft-ui-xaml/issues/5931
        static void ExitNow(int exitCode = 0)
        {
            Environment.Exit(exitCode);
        }
    }

    private async Task<IReadOnlyList<string>> ImportFromCliAsync(AudioSwitcherState imported)
    {
        // The instance marker serializes a standalone import with GUI startup.
        // If a GUI already owns it, that process performs the transaction so
        // its cached state and registered hotkeys cannot overwrite the import.
        using var instance = Kernel32.CreateMutex(IntPtr.Zero, false, Id);
        var error = Kernel32.GetLastError();
        if (instance == null || error == Win32ErrorCode.ERROR_ACCESS_DENIED)
            throw new AudioSwitcherException("Cannot access the AudioFlip instance.");
        if (error == Win32ErrorCode.ERROR_ALREADY_EXISTS)
            return await SettingsImportPipe.SendAsync(ImportPipeName, imported);

        var storage = Services.GetRequiredService<IStateStorage>();
        var previous = SettingsBackup.Copy(storage.Load());
        previous.EnsureDefaultCommands();
        var task = await Services.GetRequiredService<IStartupTaskManager>().GetTaskAsync(AudioSwitcher.StartupTaskId);
        previous.RunAtStartup = await task.IsEnabledAsync();
        var replacement = SettingsBackup.Copy(imported) with { RunAtStartup = previous.RunAtStartup };
        storage.SaveImportBackup(previous);
        storage.Save(replacement);
        return Array.Empty<string>();
    }

    private static string GetImportPipeName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return $"AudioFlip.Settings.{identity.User?.Value}.{Process.GetCurrentProcess().SessionId}";
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupTaskManager, WinStartupTaskManager>();
        services.AddSingleton<IStateStorage, StateStorage>();
        services.AddSingleton<IO, WinIO>();
        services.AddSingleton<ISettingsTransfer, WinSettingsTransfer>();
        services.AddSingleton(x => MainWindow.Instance?.DispatcherQueue ?? throw new ArgumentNullException());
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IHotkeyManager>(x => new HotkeyManager(MainWindow.SharedHwnd));
        services.AddSingleton<IAudioManager, AudioManager>();
        services.AddSingleton<IDispatcher, WinDispatcher>();
        services.AddSingleton<IClipboard, WinClipboard>();
        services.AddSingleton<IApp>(App.Current);
        services.AddSingleton<AudioSwitcher>();
        services.AddSingleton<AudioEvents>();
        services.AddSingleton<Frame>();
        services.AddTransient<AudioDeviceWatcher>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AudioPageViewModel>();
        return services.BuildServiceProvider();
    }
}
