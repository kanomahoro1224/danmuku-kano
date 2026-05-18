using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace damuku_kano.Services;

/// <summary>
/// 替代 ApplicationData.Current.LocalSettings，使用本地 JSON 文件存储设置。
/// 在非打包 (WindowsPackageType=None) 模式下也能正常工作。
/// </summary>
public static class SettingsService
{
    private static readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KanoDanmaku");

    private static readonly string _settingsFile = Path.Combine(_settingsDir, "settings.json");

    private static Dictionary<string, JsonElement>? _cache;
    private static System.Threading.Timer? _debounceTimer;

    private static Dictionary<string, JsonElement> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                _cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                         ?? new Dictionary<string, JsonElement>();
                return _cache;
            }
        }
        catch { }
        _cache = new Dictionary<string, JsonElement>();
        return _cache;
    }

    private static void Flush()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }

    public static void Set(string key, object value)
    {
        var dict = Load();
        // 先序列化再反序列化为 JsonElement，保证类型一致
        var raw = JsonSerializer.SerializeToUtf8Bytes(value);
        using var doc = JsonDocument.Parse(raw);
        dict[key] = doc.RootElement.Clone();

        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ => Flush(), null, 300, System.Threading.Timeout.Infinite);
    }

    public static bool ContainsKey(string key)
    {
        return Load().ContainsKey(key);
    }

    public static T? Get<T>(string key, T? defaultValue = default)
    {
        var dict = Load();
        if (dict.TryGetValue(key, out var elem))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(elem.GetRawText());
            }
            catch { }
        }
        return defaultValue;
    }
}
