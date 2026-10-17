// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using AudioFlip.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class HotkeyManager : IHotkeyManager
{
    private readonly IntPtr hwnd;

    public HotkeyManager(IntPtr hwnd)
    {
        this.hwnd = hwnd;
    }

    public void RegisterHotkey(int id, Hotkey hotkey)
    {
        if (!User32.RegisterHotKey(hwnd, id, hotkey.Modifiers.ToModifiers() | User32.Modifiers.MOD_NOREPEAT, (uint)hotkey.Key))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            throw new AudioSwitcherException($"Could not register hotkey '{hotkey}': {error.Message}");
        }
    }

    public void UnregisterHotkey(int id)
    {
        while (User32.UnregisterHotKey(hwnd, id))
        {
        }
    }
}
