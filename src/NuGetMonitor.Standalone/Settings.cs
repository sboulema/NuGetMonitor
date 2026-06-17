using System.Text.Json;

namespace NuGetMonitor;

internal sealed class Settings
{
    private const int _maxRecentItems = 10;

    private static readonly string _settingsFileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NuGetMonitor");
    private static readonly string _settingsFilePath = Path.Combine(_settingsFileFolder, "settings.json");
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    public List<string> RecentSolutions { get; init; } = [];

    public static Settings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch
        {
            // ignore
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_settingsFileFolder);
            var json = JsonSerializer.Serialize(this, _jsonSerializerOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // ignore
        }
    }

    public void AddRecentSolution(string path)
    {
        RecentSolutions.Remove(path);
        RecentSolutions.Insert(0, path);
        if (RecentSolutions.Count > _maxRecentItems)
        {
            RecentSolutions.RemoveRange(_maxRecentItems, RecentSolutions.Count - _maxRecentItems);
        }
        Save();
    }
}
