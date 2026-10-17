// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

public sealed class AudioDeviceWatcher : IDisposable
{
    private readonly AudioEvents events;
    private TaskCompletionSource? enumerationCompleted;
    private AudioDeviceClass deviceClass;
    private DeviceWatcher? deviceWatcher = null;
    private bool started = false;
    private bool disposed;

    public AudioDeviceWatcher(AudioEvents events)
    {
        this.events = events;
    }

    public AudioDeviceClass DeviceClass => deviceClass;

    public async Task StartAsync(AudioDeviceClass deviceClass)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            await enumerationCompleted!.Task;
            return;
        }

        this.deviceClass = deviceClass;
        started = true;
        enumerationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (deviceClass == AudioDeviceClass.Render)
            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
        else if (deviceClass == AudioDeviceClass.Capture)
            MediaDevice.DefaultAudioCaptureDeviceChanged += MediaDevice_DefaultAudioCaptureDeviceChanged;

        try
        {
            deviceWatcher = DeviceInformation.CreateWatcher(AudioManager.GetInterfaceGuid(deviceClass));
            deviceWatcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            deviceWatcher.Stopped += Watcher_Stopped;
            deviceWatcher.Added += Watcher_Added;
            deviceWatcher.Removed += Watcher_Removed;
            deviceWatcher.Updated += Watcher_Updated;
            deviceWatcher.Start();
            await enumerationCompleted.Task;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (!started)
            return;

        started = false;
        enumerationCompleted?.TrySetCanceled();
        if (deviceWatcher is { } watcher)
        {
            deviceWatcher = null;
            watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
            watcher.Stopped -= Watcher_Stopped;
            watcher.Added -= Watcher_Added;
            watcher.Removed -= Watcher_Removed;
            watcher.Updated -= Watcher_Updated;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }

        if (deviceClass == AudioDeviceClass.Render)
            MediaDevice.DefaultAudioRenderDeviceChanged -= MediaDevice_DefaultAudioRenderDeviceChanged;
        else if (deviceClass == AudioDeviceClass.Capture)
            MediaDevice.DefaultAudioCaptureDeviceChanged -= MediaDevice_DefaultAudioCaptureDeviceChanged;
    }

    public void Dispose()
    {
        disposed = true;
        Stop();
    }

    private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        if (started)
            events.Raise(new DefaultAudioDeviceChanged(args.Role.ToRole(), args.Id, AudioDeviceClass.Render));
    }

    private void MediaDevice_DefaultAudioCaptureDeviceChanged(object sender, DefaultAudioCaptureDeviceChangedEventArgs args)
    {
        if (started)
            events.Raise(new DefaultAudioDeviceChanged(args.Role.ToRole(), args.Id, AudioDeviceClass.Capture));
    }

    private void Watcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        if (sender == deviceWatcher)
            events.Raise(new AudioDeviceAdded(args.Id, new AudioDevice(args.Id, args.Name)));
    }

    private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (sender != deviceWatcher)
            return;
        string? fullName = null;
        bool? isEnabled = null;

        foreach (var property in args.Properties)
        {
            if (property.Key == "System.ItemNameDisplay")
                fullName = (string)property.Value;
            else if (property.Key == "System.Devices.InterfaceEnabled")
                isEnabled = (bool)property.Value;
        }

        events.Raise(new AudioDeviceUpdated(args.Id, new(isEnabled, fullName)));
    }

    private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (sender == deviceWatcher)
            events.Raise(new AudioDeviceRemoved(args.Id));
    }

    private void Watcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        if (sender == deviceWatcher)
            enumerationCompleted?.TrySetResult();
    }

    private void Watcher_Stopped(DeviceWatcher sender, object args)
    {
        if (sender == deviceWatcher)
        {
            enumerationCompleted?.TrySetException(new AudioSwitcherException("Audio device enumeration stopped unexpectedly."));
            Stop();
        }
    }
}
