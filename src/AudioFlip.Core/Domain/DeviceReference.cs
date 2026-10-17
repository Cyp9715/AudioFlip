// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

public static class DeviceReference
{
    private const string Prefix = "ref|";

    public static string Encode(string id, string name)
    {
        return EncodeName(name, id.GetDjb2HashCode());
    }

    public static bool IsMatch(string reference, AudioDevice device, IReadOnlyCollection<AudioDevice> devices) =>
        TryResolveId(reference, devices, out var id) && id == device.Id;

    public static bool IsMatch(string reference, AudioDevice device)
    {
        if (reference == device.Id)
            return true;

        if (!TryDecode(reference, out var name, out var idHash))
            return false;

        if (!string.Equals(name, device.Name, StringComparison.Ordinal))
            return false;

        return idHash == 0 || device.Id.GetDjb2HashCode() == idHash;
    }

    public static bool TryResolveId(string reference, IReadOnlyCollection<AudioDevice> devices, out string deviceId)
    {
        var exact = devices.FirstOrDefault(x => x.Id == reference);
        if (exact != null)
        {
            deviceId = exact.Id;
            return true;
        }

        if (!TryDecode(reference, out var name, out var idHash))
        {
            deviceId = string.Empty;
            return false;
        }

        var sameName = devices.Where(x => string.Equals(x.Name, name, StringComparison.Ordinal)).ToArray();
        var sameNameAndHash = sameName.Where(x => x.Id.GetDjb2HashCode() == idHash).ToArray();
        if (idHash != 0 && sameNameAndHash.Length == 1)
        {
            deviceId = sameNameAndHash[0].Id;
            return true;
        }

        var sameHash = devices.Where(x => x.Id.GetDjb2HashCode() == idHash).ToArray();
        if (idHash != 0 && sameHash.Length == 1)
        {
            deviceId = sameHash[0].Id;
            return true;
        }

        // Windows updates can replace an endpoint ID. Only use its name when
        // it identifies exactly one device, and prefer a surviving ID above.
        if (sameName.Length == 1)
        {
            deviceId = sameName[0].Id;
            return true;
        }

        deviceId = string.Empty;
        return false;
    }

    public static string[] UpdateSelection(
        IEnumerable<string> references,
        IReadOnlyCollection<AudioDevice> availableDevices,
        IEnumerable<AudioDevice> selectedDevices)
    {
        var remaining = selectedDevices.DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        var result = new List<string>();
        foreach (var reference in references)
        {
            if (!TryResolveId(reference, availableDevices, out var id))
            {
                // A disconnected or ambiguous endpoint must survive unrelated edits.
                result.Add(reference);
            }
            else if (remaining.Remove(id, out var device))
            {
                result.Add(Encode(device.Id, device.Name));
            }
        }

        result.AddRange(remaining.Values.Select(x => Encode(x.Id, x.Name)));
        return result.Distinct().ToArray();
    }

    internal static string EncodeName(string name, uint idHash) =>
        $"{Prefix}{Uri.EscapeDataString(name)}|{idHash}";

    private static bool TryDecode(string reference, out string name, out uint idHash)
    {
        name = string.Empty;
        idHash = 0;

        if (!reference.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = reference.Split('|', 3, StringSplitOptions.None);
        if (parts.Length != 3)
            return false;

        name = Uri.UnescapeDataString(parts[1]);
        return uint.TryParse(parts[2], out idHash);
    }
}
