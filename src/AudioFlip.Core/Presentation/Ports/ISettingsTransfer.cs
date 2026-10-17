namespace AudioFlip;

public interface ISettingsTransfer
{
    Task<string?> ExportAsync(string contents, string suggestedFileName);
    Task<string?> ReadImportAsync();
    Task<bool> ConfirmImportAsync(SettingsImportPlan plan);
}
