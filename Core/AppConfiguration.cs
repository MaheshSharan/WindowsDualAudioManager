using AudioDual.Core.Diagnostics;
using Newtonsoft.Json;
using System.IO;

namespace AudioDual.Core
{
    /// <summary>
    /// Persisted application settings. Every property here is read by at least one
    /// consumer — see AdvancedAudioEngine for AudioBufferMs. Settings that existed in
    /// prior versions but were never actually consumed anywhere (device presets, native
    /// Windows 11 routing) have been removed rather than left in place as decorative
    /// config; see the v1.2 refactor plan for where a real preset feature is tracked.
    /// </summary>
    public class AppConfiguration
    {
        private const string ConfigFileName = "config.json";
        private const int MinimumAudioBufferMs = 15;
        private const int MaximumAudioBufferMs = 500;
        private const int DefaultAudioBufferMs = 120;

        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioDual",
            ConfigFileName);

        public bool StartMinimized { get; set; }

        public bool RunAtStartup { get; set; }

        public bool PreferExclusiveModeOutput { get; set; }

        /// <summary>
        /// Target output latency in milliseconds, consumed directly by
        /// <see cref="AdvancedAudioEngine"/> when sizing each device's playback buffer.
        /// Lower values reduce delay but increase the risk of stutter on slower hardware.
        /// </summary>
        public int AudioBufferMs { get; set; } = DefaultAudioBufferMs;

        public static AppConfiguration Load(IAppLogger? logger = null)
        {
            logger ??= new FileAppLogger();

            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (directory is not null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var loaded = JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
                    loaded.AudioBufferMs = ClampAudioBufferMs(loaded.AudioBufferMs);
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("AppConfiguration", "Failed to load configuration; falling back to defaults.", ex);
            }

            return new AppConfiguration();
        }

        public void Save(IAppLogger? logger = null)
        {
            logger ??= new FileAppLogger();

            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (directory is not null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AudioBufferMs = ClampAudioBufferMs(AudioBufferMs);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                logger.LogError("AppConfiguration", "Failed to save configuration.", ex);
            }
        }

        private static int ClampAudioBufferMs(int value)
        {
            return Math.Clamp(value, MinimumAudioBufferMs, MaximumAudioBufferMs);
        }
    }
}
