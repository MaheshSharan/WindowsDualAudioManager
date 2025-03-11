using Newtonsoft.Json;
using System.IO;

namespace AudioDual.Core
{
    public class AppConfiguration
    {
        private const string ConfigFileName = "config.json";
        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "AudioDual", 
            ConfigFileName);

        public List<AudioDevicePreset> DevicePresets { get; set; } = new();
        public bool StartMinimized { get; set; } = false;
        public bool RunAtStartup { get; set; } = false;
        public bool LastPresetAutoload { get; set; } = true;
        public string? LastUsedPreset { get; set; }
        public bool UseNativeWindows11Routing { get; set; } = true;
        public int AudioBufferMs { get; set; } = 250;
        public int AudioQuality { get; set; } = 60; // 0-100, higher is better quality

        public static AppConfiguration Load()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    return JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }

            return new AppConfiguration();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }
    }

    public class AudioDevicePreset
    {
        public string Name { get; set; } = string.Empty;
        public List<AudioDeviceSettings> Devices { get; set; } = new();
    }

    public class AudioDeviceSettings
    {
        public string DeviceId { get; set; } = string.Empty;
        public float Volume { get; set; } = 1.0f;
    }
}
