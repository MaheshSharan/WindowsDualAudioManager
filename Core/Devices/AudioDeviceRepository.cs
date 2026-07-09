using AudioDual.Core.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioDual.Core.Devices
{
    /// <summary>
    /// Enumerates render endpoints and notifies subscribers when a device is added,
    /// removed, or changes state (for example, unplugged mid-session) — the old
    /// engine had no such notification path at all, so a channel bound to a device
    /// that disappeared would keep running until its next operation happened to fail.
    /// </summary>
    public sealed class AudioDeviceRepository : IDisposable, IMMNotificationClient
    {
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly IAppLogger _logger;

        /// <summary>
        /// Exposed so components that need an MMDeviceEnumerator (currently
        /// CaptureFormatNegotiator) can share this one instead of creating their own
        /// separate COM enumerator.
        /// </summary>
        public MMDeviceEnumerator DeviceEnumerator => _deviceEnumerator;

        public event EventHandler<string>? DeviceRemoved;
        public event EventHandler<string>? DeviceStateChanged;

        public AudioDeviceRepository(IAppLogger logger)
        {
            _logger = logger;
            _deviceEnumerator = new MMDeviceEnumerator();
            _deviceEnumerator.RegisterEndpointNotificationCallback(this);
        }

        public List<AudioDevice> GetRenderDevices(IReadOnlyDictionary<string, float> activeVolumesByDeviceId)
        {
            var devices = new List<AudioDevice>();

            string defaultDeviceId;
            try
            {
                defaultDeviceId = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AudioDeviceRepository", $"Could not resolve the default render endpoint: {ex.Message}");
                defaultDeviceId = string.Empty;
            }

            foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                bool isActive = activeVolumesByDeviceId.TryGetValue(device.ID, out var volume);

                devices.Add(new AudioDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == defaultDeviceId,
                    IsEnabled = isActive,
                    Volume = isActive ? volume : 1.0f
                });
            }

            return devices;
        }

        public MMDevice GetDevice(string deviceId)
        {
            return _deviceEnumerator.GetDevice(deviceId);
        }

        public void Dispose()
        {
            _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
            _deviceEnumerator.Dispose();
        }

        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState is DeviceState.Disabled or DeviceState.NotPresent or DeviceState.Unplugged)
            {
                DeviceRemoved?.Invoke(this, deviceId);
            }
            else
            {
                DeviceStateChanged?.Invoke(this, deviceId);
            }
        }

        void IMMNotificationClient.OnDeviceAdded(string deviceId)
        {
            DeviceStateChanged?.Invoke(this, deviceId);
        }

        void IMMNotificationClient.OnDeviceRemoved(string deviceId)
        {
            DeviceRemoved?.Invoke(this, deviceId);
        }

        void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // The app routes explicit output selections, not the system default, so a
            // default-device change doesn't itself invalidate any active channel.
        }

        void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Friendly name / property changes are picked up on the next enumeration;
            // nothing needs to happen eagerly here.
        }
    }
}
