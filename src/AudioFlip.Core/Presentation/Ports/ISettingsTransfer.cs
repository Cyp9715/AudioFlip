namespace AudioFlip;

public interface ISettingsTransfer
{
    string? LastPath { get; set; }
    Task<string?> ExportAsync(string contents, string suggestedFileName);
    Task<SettingsImportFile?> ReadImportAsync();
    Task<bool> ConfirmImportAsync(SettingsImportPlan plan);
}

public sealed record SettingsImportFile(string Path, string Contents);
