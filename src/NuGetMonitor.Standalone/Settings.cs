using System.Text.Json;

namespace NuGetMonitor;

internal sealed class Settings
{
    private const int _maxRecentItems = 10;

    private static readonly string _settingsFileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NuGetMonitor");
    private static readonly string _settingsFilePath = Path.Combine(_settingsFileFolder, "settings.json");
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// The single, shared instance to use throughout the app; ensures every part of the UI
    /// reads and writes the same in-memory state, so one part saving doesn't clobber another's changes.
    /// </summary>
    public static Settings Instance { get; } = Load();

    public List<string> RecentSolutions { get; init; } = [];

    // Comma-separated header names of the columns the user hid via the "Choose columns" flyout.
    public string HiddenColumns { get; set; } = string.Empty;

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
