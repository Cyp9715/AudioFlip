// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

public sealed class StateStorage : IStateStorage
{
    private readonly JsonFileStateStorage storage;

    public StateStorage()
    {
        var appData = Windows.Storage.ApplicationData.Current;
        storage = new JsonFileStateStorage(
            Path.Combine(appData.LocalFolder.Path, "settings.json"),
            () => appData.LocalSettings.Values["settings"]?.ToString());
    }

    public AudioSwitcherState Load() => storage.Load();

    public void Save(AudioSwitcherState state) => storage.Save(state);

    public void SaveImportBackup(AudioSwitcherState state) => storage.SaveImportBackup(state);
}
