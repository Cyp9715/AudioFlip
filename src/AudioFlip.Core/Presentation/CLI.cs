// Copyright (c) 2021 Jose Torres. All rights reserved. Licensed under the Apache License, Version 2.0. See LICENSE.md file in the project root for full license information.

namespace AudioFlip;

using AudioFlip.Core.Application;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public sealed class CLI
{
    private readonly IAudioManager audioManager;
    private readonly IStateStorage stateStorage;
    private readonly INotificationService notificationService;
    private readonly Func<AudioSwitcherState, Task<IReadOnlyList<string>>>? importSettings;

    public CLI(
        IAudioManager audioManager,
        IStateStorage stateStorage,
        INotificationService notificationService,
        Func<AudioSwitcherState, Task<IReadOnlyList<string>>>? importSettings = null)
    {
        this.stateStorage = stateStorage;
        this.audioManager = audioManager;
        this.notificationService = notificationService;
        this.importSettings = importSettings;
    }

    public string? Output { get; private set; }

    public async Task RunAsync(string cmd)
    {
        await RunAsync(ParseArguments(cmd));
    }

    public async Task RunAsync(string[] cmdArgs)
    {
        Output = null;
        if (TryParseSettingsArguments(cmdArgs, out var import, out var path))
        {
            await TransferSettingsAsync(import, path);
            return;
        }

        ParseArguments(cmdArgs, out var device, out var type);

        if (!device.Any())
            throw new CLIException("Missing device name.");

        var availableDevices = await audioManager.GetAllDevicesAsync(type);
        var uniqueDeviceIds = new HashSet<string>(device.Select(x => GetDeviceId(x, availableDevices)));
        var state = stateStorage.Load();
        var toggler = new AudioSwitcherToggler(audioManager, notificationService);
        await toggler.ToggleAsync(type, uniqueDeviceIds, state.SwitchCommunicationDevice);
    }

    public static string BuildSettingsCommand(string path, bool import) =>
        $"AudioFlip {QuoteArgument(path)} -{(import ? "import" : "export")}";

    public static string GetDeviceId(string devicePart, AudioDevice[] deviceList)
    {
        var (name, uid) = DecodeName(devicePart);
        if (DeviceReference.TryResolveId(DeviceReference.EncodeName(name, uid), deviceList, out var id))
            return id;

        throw new CLIException($"Device \"{name}\" is unavailable or ambiguous. Please regenerate your command.");
    }

    public static string BuildCommand(AudioDeviceClass deviceClass, AudioDevice[] devices)
    {
        if (!devices.Any())
            return string.Empty;

        return $"AudioFlip {BuildCommandArgs(deviceClass, devices)}";
    }

    public static string BuildCommandArgs(AudioDeviceClass deviceClass, AudioDevice[] devices)
    {
        if (devices.Count() >= 1)
            return $"{string.Join(" ", devices.Select(x => EncodeName(x)))} -{EncodeClass(deviceClass)}";

        return string.Empty;
    }

    public static string EncodeName(AudioDevice name)
    {
        return QuoteArgument($"{name.Name} / {name.Id.GetDjb2HashCode()}");
    }

    public static (string Name, uint Uid) DecodeName(string text)
    {
        var match = Regex.Match(text, @"^(.+?)(?: / ?(\d+))?$");
        if (!match.Success)
            throw new CLIException("Invalid device name. Please regenerate your command.");

        string name = match.Groups[1].Value;
        uint uid = 0;
        if (match.Groups[2].Success && !uint.TryParse(match.Groups[2].Value, out uid))
            throw new CLIException("Invalid device ID hash. Please regenerate your command.");
        return (name, uid);
    }

    public static string EncodeClass(AudioDeviceClass deviceClass)
    {
        return deviceClass switch
        {
            AudioDeviceClass.Capture => "recording",
            AudioDeviceClass.Render => "playback",
            _ => throw new NotImplementedException(),
        };
    }

    public static bool DecodeClass(string text, out AudioDeviceClass? deviceClass)
    {
        switch (text)
        {
            case "-recording":
                deviceClass = AudioDeviceClass.Capture;
                return true;
            case "-playback":
                deviceClass = AudioDeviceClass.Render;
                return true;
            default:
                deviceClass = default;
                return false;
        }
    }

    public static void ParseArguments(string[] cmdArgs, out string[] devices, out AudioDeviceClass type)
    {
        AudioDeviceClass? localType = null;
        var names = new List<string>();
        foreach (var arg in cmdArgs)
        {
            if (DecodeClass(arg, out var parsedType))
            {
                if (localType.HasValue && localType != parsedType)
                    throw new CLIException("Choose either -playback or -recording, not both.");
                localType = parsedType;
            }
            else
            {
                names.Add(arg);
            }
        }

        devices = names.ToArray();
        type = localType.GetValueOrDefault(AudioDeviceClass.Render);
    }

    public static string[] ParseArguments(string cmd)
    {
        var args = new List<string>();
        var i = 0;
        while (i < cmd.Length)
        {
            while (i < cmd.Length && char.IsWhiteSpace(cmd[i]))
                ++i;
            if (i == cmd.Length)
                break;

            var insideQuote = false;
            var part = new StringBuilder();
            while (i < cmd.Length && (insideQuote || !char.IsWhiteSpace(cmd[i])))
            {
                var slashes = 0;
                while (i < cmd.Length && cmd[i] == '\\')
                {
                    ++slashes;
                    ++i;
                }

                if (i < cmd.Length && cmd[i] == '"')
                {
                    part.Append('\\', slashes / 2);
                    if (slashes % 2 == 1)
                        part.Append('"');
                    else
                        insideQuote = !insideQuote;
                    ++i;
                }
                else
                {
                    part.Append('\\', slashes);
                    if (i < cmd.Length && (insideQuote || !char.IsWhiteSpace(cmd[i])))
                        part.Append(cmd[i++]);
                }
            }

            if (insideQuote)
                throw new CLIException("Unterminated quote in command.");
            args.Add(part.ToString());
        }

        return args.ToArray();
    }

    private static bool TryParseSettingsArguments(string[] args, out bool import, out string path)
    {
        var flags = args.Where(x => x is "-import" or "-export").ToArray();
        import = flags.FirstOrDefault() == "-import";
        path = string.Empty;
        if (flags.Length == 0)
            return false;
        if (flags.Length != 1 || args.Length != 2)
            throw new CLIException("Usage: AudioFlip \"<settings.json>\" -import|-export");
        path = args.Single(x => x != flags[0]);
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('-'))
            throw new CLIException("Specify a settings file path.");
        return true;
    }

    private async Task TransferSettingsAsync(bool import, string path)
    {
        path = Path.GetFullPath(path);
        if (!import)
        {
            var state = SettingsBackup.Copy(stateStorage.Load());
            state.EnsureDefaultCommands();
            AtomicFile.WriteAllText(path, new SettingsBackup { Settings = state }.ToJson());
            Output = $"Exported settings: {path}";
            return;
        }

        if (new FileInfo(path).Length > SettingsBackup.MaximumFileSize)
            throw new InvalidDataException("The settings backup exceeds the 16 MB size limit.");
        var backup = SettingsBackup.Parse(await File.ReadAllTextAsync(path));
        var playback = await audioManager.GetAllDevicesAsync(AudioDeviceClass.Render);
        var recording = await audioManager.GetAllDevicesAsync(AudioDeviceClass.Capture);
        var plan = new SettingsImportPlan(backup, playback, recording);
        var imported = plan.BuildState();
        IReadOnlyList<string> warnings;
        if (importSettings != null)
        {
            warnings = await importSettings(imported);
        }
        else
        {
            var previous = SettingsBackup.Copy(stateStorage.Load());
            previous.EnsureDefaultCommands();
            imported.RunAtStartup = previous.RunAtStartup;
            stateStorage.SaveImportBackup(previous);
            stateStorage.Save(imported);
            warnings = Array.Empty<string>();
        }

        var lines = new List<string> { $"Imported {imported.Commands.Count} commands: {path}" };
        var unresolved = plan.Devices.Count(x => x.SelectedOption.Device == null);
        if (unresolved > 0)
            lines.Add($"Kept {unresolved} unavailable or ambiguous device selections. Review them in Settings > Import.");
        lines.AddRange(warnings);
        Output = string.Join(Environment.NewLine, lines);
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                ++slashes;
                continue;
            }

            result.Append('\\', character == '"' ? (slashes * 2) + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }

        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
