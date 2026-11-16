using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickRewrite.Models;

namespace QuickRewrite.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var overridePath = Environment.GetEnvironmentVariable("QUICKREWRITE_CONFIG");
        _settingsPath = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickRewrite", "settings.json")
            : Path.GetFullPath(overridePath);
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.ApiKey = DpapiSecretStore.Unprotect(settings.ApiKeyProtected);
            if (!string.IsNullOrWhiteSpace(settings.ApiKeyProtected) && string.IsNullOrEmpty(settings.ApiKey))
            {
                AppLog.Warn("API Key 解密失败，请在设置中重新填写。");
            }

            settings.Normalize();
            return settings;
        }
        catch (JsonException ex)
        {
            AppLog.Error($"配置内容损坏：{_settingsPath}", ex);
            BackupCorruptSettings();
            return new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error($"读取配置失败：{_settingsPath}", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.Normalize();
        settings.ApiKeyProtected = DpapiSecretStore.Protect(settings.ApiKey ?? "");
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        var tempPath = _settingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 临时文件清理失败不影响已写入的配置。
            }
        }
    }

    private void BackupCorruptSettings()
    {
        try
        {
            var backupPath = $"{_settingsPath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_settingsPath, backupPath, overwrite: false);
            AppLog.Warn($"已将损坏的配置备份到：{backupPath}");
        }
        catch
        {
            // 备份失败时仍返回默认配置，避免阻断启动。
        }
    }
}
