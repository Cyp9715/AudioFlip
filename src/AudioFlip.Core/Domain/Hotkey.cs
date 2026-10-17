// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

public sealed record Hotkey
{
    public Hotkey(KeyModifiers modifiers, Key key)
    {
        if (!Validate(modifiers, key))
            throw new ArgumentOutOfRangeException(null, "Invalid hotkey combination.");

        Modifiers = modifiers;
        Key = key;
    }

    public Hotkey()
    {
    }

    public static Func<Key, string> KeyPrinter { get; set; } = key => key.ToString();
    public static Hotkey Empty { get; } = new();
    public KeyModifiers Modifiers { get; init; } = default;
    public Key Key { get; init; } = default;

    public static bool Validate(KeyModifiers modifiers, Key key)
    {
        if ((modifiers, key) == default)
            return true;

        var keyCode = (int)key;

        // Modifier-only combinations and the debugger's F12 cannot be
        // registered as global shortcuts.
        return (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Menu))
            && (modifiers & ~(KeyModifiers.Control | KeyModifiers.Menu | KeyModifiers.Shift | KeyModifiers.Windows)) == 0
            && keyCode is > 0 and < 0xFF
            && keyCode is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B)
            && keyCode is not (>= 0xA0 and <= 0xA5);
    }

    public override string ToString()
    {
        var keys = new List<string>(4);

        Span<KeyModifiers> mods = stackalloc KeyModifiers[4]
        {
            KeyModifiers.Control,
            KeyModifiers.Shift,
            KeyModifiers.Menu,
            KeyModifiers.Windows,
        };

        foreach (var mod in mods)
        {
            if (Modifiers.HasFlag(mod))
                keys.Add(GetPrettyModifier(mod));
        }

        if (Modifiers.HasFlag(KeyModifiers.Control) is false &&
            Modifiers.HasFlag(KeyModifiers.Menu) is false)
        {
            return string.Empty;
        }

        keys.Add(KeyPrinter(Key));
        return string.Join(" + ", keys);
    }

    private static string GetPrettyModifier(KeyModifiers modifier)
    {
        return modifier switch
        {
            KeyModifiers.Control => "Ctrl",
            KeyModifiers.Shift => "Shift",
            KeyModifiers.Menu => "Alt",
            KeyModifiers.Windows => "Windows",
            KeyModifiers.None => "None",
            _ => throw new NotImplementedException(),
        };
    }
}
