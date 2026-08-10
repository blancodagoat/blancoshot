using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlancoShot;

/// <summary>The on-disk shape of config.json. Startup state deliberately lives in the registry only.</summary>
internal sealed class ConfigFile
{
    public string? RegionHotkey { get; set; }
    public string? FullDisplayHotkey { get; set; }
    public string? SaveRoot { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConfigFile))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}

internal sealed class AppConfig
{
    public HotkeyBinding RegionHotkey { get; set; } = HotkeyBinding.DefaultRegion;

    public HotkeyBinding FullDisplayHotkey { get; set; } = HotkeyBinding.DefaultFullDisplay;

    public string SaveRoot { get; set; } = AppInfo.DefaultSaveRoot;

    /// <summary>
    /// Never throws. Anything unreadable or malformed collapses to defaults, and the
    /// file is rewritten so the next launch starts from something valid.
    /// </summary>
    public static AppConfig Load()
    {
        var config = new AppConfig();
        bool rewrite = true;

        try
        {
            if (File.Exists(AppInfo.ConfigPath))
            {
                var json = File.ReadAllText(AppInfo.ConfigPath);
                var file = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ConfigFile);
                if (file is not null)
                {
                    rewrite = false;

                    if (HotkeyBinding.TryParse(file.RegionHotkey, out var region))
                    {
                        config.RegionHotkey = region;
                    }
                    else
                    {
                        rewrite = true;
                    }

                    if (HotkeyBinding.TryParse(file.FullDisplayHotkey, out var full))
                    {
                        config.FullDisplayHotkey = full;
                    }
                    else
                    {
                        rewrite = true;
                    }

                    if (!string.IsNullOrWhiteSpace(file.SaveRoot))
                    {
                        config.SaveRoot = file.SaveRoot;
                    }
                    else
                    {
                        rewrite = true;
                    }
                }
            }
        }
        catch
        {
            config = new AppConfig();
        }

        if (rewrite)
        {
            config.Save();
        }

        return config;
    }

    /// <summary>Called on every edit. Failures are swallowed: a lost setting must not kill capture.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            var file = new ConfigFile
            {
                RegionHotkey = RegionHotkey.ToString(),
                FullDisplayHotkey = FullDisplayHotkey.ToString(),
                SaveRoot = SaveRoot,
            };
            File.WriteAllText(
                AppInfo.ConfigPath,
                JsonSerializer.Serialize(file, ConfigJsonContext.Default.ConfigFile));
        }
        catch
        {
            // Ignored by design.
        }
    }

    public HotkeyBinding Get(HotkeyId id) =>
        id == HotkeyId.Region ? RegionHotkey : FullDisplayHotkey;

    public void Set(HotkeyId id, HotkeyBinding binding)
    {
        if (id == HotkeyId.Region)
        {
            RegionHotkey = binding;
        }
        else
        {
            FullDisplayHotkey = binding;
        }
    }
}
