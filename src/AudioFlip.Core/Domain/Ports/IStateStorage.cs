// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

public interface IStateStorage
{
    AudioSwitcherState Load();
    void Save(AudioSwitcherState state);
    void SaveImportBackup(AudioSwitcherState state);
}
