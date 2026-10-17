namespace AudioFlip;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;

internal sealed class WinSettingsTransfer : ISettingsTransfer
{
    private readonly Frame frame;

    public WinSettingsTransfer(Frame frame) => this.frame = frame;

    public string? LastPath { get; set; }

    public async Task<string?> ExportAsync(string contents, string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = ".json",
        };
        picker.FileTypeChoices.Add("AudioFlip settings backup", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.SharedHwnd);
        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return null;

        CachedFileManager.DeferUpdates(file);
        try
        {
            await FileIO.WriteTextAsync(file, contents);
        }
        finally
        {
            var status = await CachedFileManager.CompleteUpdatesAsync(file);
            if (status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed))
                throw new IOException("The file provider could not finish saving the backup. Choose a local folder and try again.");
        }

        return string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
    }

    public async Task<SettingsImportFile?> ReadImportAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.SharedHwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return null;
        if ((await file.GetBasicPropertiesAsync()).Size > SettingsBackup.MaximumFileSize)
            throw new InvalidDataException("The settings backup exceeds the 16 MB size limit.");
        return new(string.IsNullOrEmpty(file.Path) ? file.Name : file.Path, await FileIO.ReadTextAsync(file));
    }

    public async Task<bool> ConfirmImportAsync(SettingsImportPlan plan)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Wrapped($"Replace settings with {plan.Backup.Settings.Commands.Count} commands?"));
        content.Children.Add(Wrapped("Current settings will be backed up. Run at startup stays unchanged.", 12));

        foreach (var mapping in plan.Devices)
        {
            var entry = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 8) };
            entry.Children.Add(Wrapped($"{mapping.Category}: {mapping.SavedName}"));
            entry.Children.Add(Wrapped(mapping.MatchDescription, 12));
            var choices = new ComboBox
            {
                ItemsSource = mapping.Options,
                DisplayMemberPath = nameof(ImportDeviceChoice.Label),
                SelectedItem = mapping.SelectedOption,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 280,
            };
            AutomationProperties.SetName(choices, $"Restore {mapping.Category}: {mapping.SavedName}");
            choices.SelectionChanged += (_, _) =>
            {
                if (choices.SelectedItem is ImportDeviceChoice selected)
                    mapping.SelectedOption = selected;
            };
            entry.Children.Add(choices);
            content.Children.Add(entry);
        }

        var dialog = new ContentDialog
        {
            Title = "Import settings",
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 380,
            },
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = frame.XamlRoot,
            RequestedTheme = App.Theme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static TextBlock Wrapped(string text, double fontSize = 14) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = fontSize,
    };
}
