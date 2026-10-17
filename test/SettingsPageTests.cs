namespace AudioFlip.Tests;

public sealed class SettingsPageTests
{
    private static string Backup() => new SettingsBackup
    {
        Settings = new()
        {
            DarkTheme = false,
            Commands = [new("Imported output", AudioDeviceClass.Render), new("Imported input", AudioDeviceClass.Capture)],
        },
    }.ToJson();

    [Theory]
    [InlineData(null)]
    [InlineData("valid")]
    [InlineData("{")]
    public async Task CancelOrInvalidImportLeavesCurrentSettingsUntouched(string? input)
    {
        var services = new TestServices(new Command("Original", AudioDeviceClass.Render));
        var transfer = new Transfer { Input = input == "valid" ? Backup() : input, Confirmed = false };
        var page = new SettingsPageViewModel(services.Switcher, services.IO, new App(), transfer, new Clipboard());
        await page.ImportAsync();
        Assert.Equal("Original", services.Switcher.Commands[0].Name);
        Assert.Equal(0, services.Storage.Saves);
        Assert.Null(services.Storage.ImportBackup);
        Assert.True(page.CanTransfer);
    }

    [Fact]
    public async Task ImportAppliesPreferencesAndRefreshesThePage()
    {
        var services = new TestServices();
        var transfer = new Transfer { Input = Backup(), Confirmed = true };
        var app = new App();
        var page = new SettingsPageViewModel(services.Switcher, services.IO, app, transfer, new Clipboard());
        await page.ImportAsync();
        Assert.False(page.DarkTheme);
        Assert.Equal("Imported output", services.Switcher.Commands[0].Name);
        Assert.Equal(1, app.ThemeUpdates);
        Assert.NotNull(services.Storage.ImportBackup);
        Assert.True(page.CanTransfer);
    }

    [Fact]
    public async Task ExportWritesAPortableFileWithoutSavingOrChangingSettings()
    {
        var services = new TestServices(new Command("Original", AudioDeviceClass.Render));
        var transfer = new Transfer();
        var clipboard = new Clipboard();
        var page = new SettingsPageViewModel(services.Switcher, services.IO, new App(), transfer, clipboard);
        await page.ExportAsync();
        Assert.Equal("Original", SettingsBackup.Parse(transfer.Exported!).Settings.Commands[0].Name);
        Assert.Equal(0, services.Storage.Saves);
        Assert.True(page.CanTransfer);
        Assert.Equal("AudioFlip \"backup.json\" -export", page.ExportCommandText);
        Assert.Equal("AudioFlip \"backup.json\" -import", page.ImportCommandText);
        page.CopyExportCommandToClipboard();
        Assert.Equal(page.ExportCommandText, clipboard.Text);
        page.CopyImportCommandToClipboard();
        Assert.Equal(page.ImportCommandText, clipboard.Text);
    }

    [Fact]
    public void NavigatingBackKeepsTheLastSelectedFileForBothCommands()
    {
        var services = new TestServices();
        var transfer = new Transfer { LastPath = @"D:\Backups\설정 file.json" };
        var page = new SettingsPageViewModel(services.Switcher, services.IO, new App(), transfer, new Clipboard());
        Assert.Equal(CLI.BuildSettingsCommand(transfer.LastPath, false), page.ExportCommandText);
        Assert.Equal(CLI.BuildSettingsCommand(transfer.LastPath, true), page.ImportCommandText);
    }

    private sealed class App : IApp
    {
        public int ThemeUpdates { get; private set; }
        public void UpdateTheme() => ++ThemeUpdates;
    }

    private sealed class Transfer : ISettingsTransfer
    {
        public string? LastPath { get; set; }
        public string? Input { get; init; }
        public bool Confirmed { get; init; }
        public string? Exported { get; private set; }
        public Task<SettingsImportFile?> ReadImportAsync() => Task.FromResult(Input == null ? null : new SettingsImportFile("backup.json", Input));
        public Task<bool> ConfirmImportAsync(SettingsImportPlan plan) => Task.FromResult(Confirmed);
        public Task<string?> ExportAsync(string contents, string suggestedFileName)
        {
            Exported = contents;
            return Task.FromResult<string?>("backup.json");
        }
    }

    private sealed class Clipboard : IClipboard
    {
        public string? Text { get; private set; }
        public void SetTextContent(string text) => Text = text;
    }
}
