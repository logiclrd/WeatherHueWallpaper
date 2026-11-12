using System.Text.Json;

namespace WeatherHueWallpaper;

internal sealed class AppConfig
{
    public string TemplateFile { get; set; } = "template.png";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int HueTransitionMinutes { get; set; } = 60;

    public static AppConfig LoadOrCreate(AppPaths paths)
    {
        try
        {
            if (File.Exists(paths.ConfigFile))
            {
                var json = File.ReadAllText(paths.ConfigFile);
                var config = JsonSerializer.Deserialize<AppConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config is not null)
                    return config;
            }
            else
            {
                File.WriteAllText(paths.ConfigFile,
                    JsonSerializer.Serialize(new AppConfig(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
        }
        catch
        {
            // If configuration is missing or damaged, use safe in-memory defaults.
        }

        return new AppConfig();
    }
}

internal sealed record AppPaths(string Directory, string ConfigFile, string SlotA, string SlotB)
{
    public static AppPaths Create()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeatherHueWallpaper");
        DirectoryInfo info = System.IO.Directory.CreateDirectory(directory);
        return new AppPaths(
            info.FullName,
            Path.Combine(info.FullName, "config.json"),
            Path.Combine(info.FullName, "wallpaper-a.png"),
            Path.Combine(info.FullName, "wallpaper-b.png"));
    }

    public string ResolveTemplate(string fileName)
    {
        if (Path.IsPathRooted(fileName))
            return fileName;

        var personalTemplate = Path.Combine(Directory, fileName);
        if (File.Exists(personalTemplate))
            return personalTemplate;

        return Path.Combine(AppContext.BaseDirectory, fileName);
    }
}
