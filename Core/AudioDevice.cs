namespace AudioDual.Core
{
    public class AudioDevice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; }
        public float Volume { get; set; } = 1.0f;

        public override string ToString()
        {
            return Name;
        }
    }
}
