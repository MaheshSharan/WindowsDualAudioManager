using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AudioDual.Core
{
    public class AudioEngine : IDisposable
    {
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly ConcurrentDictionary<string, AudioDeviceOutput> _activeOutputs;
        private WasapiCapture? _loopbackCapture;
        private WaveFormat _captureFormat;
        private readonly bool _useNativeWindowsRouting;
        private AudioSessionManager? _sessionManager;

        public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

        public AudioEngine(bool useNativeWindowsRouting = false)
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _activeOutputs = new ConcurrentDictionary<string, AudioDeviceOutput>();
            _captureFormat = new WaveFormat(48000, 16, 2); // Default format
            _useNativeWindowsRouting = useNativeWindowsRouting;
            
            if (_useNativeWindowsRouting)
            {
                try
                {
                    // Get the default device for audio session management
                    var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _sessionManager = defaultDevice.AudioSessionManager;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize session manager: {ex.Message}");
                }
            }
        }

        public List<AudioDevice> GetAudioDevices()
        {
            var devices = new List<AudioDevice>();
            string defaultDeviceId = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            
            foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == defaultDeviceId,
                    IsEnabled = _activeOutputs.ContainsKey(device.ID),
                    Volume = _activeOutputs.TryGetValue(device.ID, out var output) ? output.Volume : 1.0f
                });
            }
            
            return devices;
        }

        public bool EnableDevice(string deviceId, float volume = 1.0f)
        {
            try
            {
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device == null) return false;

                // Check if device is already enabled
                if (_activeOutputs.ContainsKey(deviceId))
                    return true;

                // Try to use the Windows native routing if possible and enabled
                if (_useNativeWindowsRouting && TryEnableNativeRouting(device, volume))
                {
                    return true;
                }

                // Fall back to our manual routing implementation
                StartManualRouting();
                
                // Use a larger buffer to prevent stuttering (600ms)
                var outputDevice = new AudioDeviceOutput(device, volume, _captureFormat, 600);
                _activeOutputs[deviceId] = outputDevice;
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling device: {ex.Message}");
                return false;
            }
        }

        private bool TryEnableNativeRouting(MMDevice targetDevice, float volume)
        {
            // This is a placeholder for Windows 11 audio routing APIs
            // Currently not fully implemented as it would require system-level APIs
            
            // For Windows 11, we could potentially use the audio policy APIs
            // or the IAudioRoutingManager interface, but these are not 
            // directly accessible through NAudio and may require COM interop
            
            return false;
        }

        private void StartManualRouting()
        {
            if (_loopbackCapture == null)
            {
                StartAudioCapture();
            }
        }

        public bool DisableDevice(string deviceId)
        {
            if (_activeOutputs.TryRemove(deviceId, out var output))
            {
                output.Dispose();
                
                if (_activeOutputs.Count == 0 && _loopbackCapture != null)
                {
                    StopAudioCapture();
                }
                
                return true;
            }
            
            return false;
        }

        public bool SetDeviceVolume(string deviceId, float volume)
        {
            if (_activeOutputs.TryGetValue(deviceId, out var output))
            {
                output.SetVolume(volume);
                return true;
            }
            
            return false;
        }

        private void StartAudioCapture()
        {
            try
            {
                // Get the default device for capturing system audio
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                
                // Create a loopback capture to record system audio with a larger buffer
                _loopbackCapture = new WasapiLoopbackCapture(defaultDevice)
                {
                    // Use a buffer that's large enough to prevent stuttering
                    WaveFormat = new WaveFormat(48000, 16, 2)
                };
                _captureFormat = _loopbackCapture.WaveFormat;
                
                _loopbackCapture.DataAvailable += OnAudioDataAvailable;
                _loopbackCapture.RecordingStopped += (s, e) => {
                    _loopbackCapture?.Dispose();
                    _loopbackCapture = null;
                };
                
                // Configure capture for stability rather than low latency
                _loopbackCapture.ShareMode = AudioClientShareMode.Shared;
                
                // Start the loopback capture
                _loopbackCapture.StartRecording();
                
                Console.WriteLine($"Audio capture started with format: {_captureFormat}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio capture: {ex.Message}");
            }
        }

        private void StopAudioCapture()
        {
            try
            {
                _loopbackCapture?.StopRecording();
                _loopbackCapture = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping audio capture: {ex.Message}");
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            
            // Send audio data to all active outputs with proper error handling
            foreach (var output in _activeOutputs.Values)
            {
                try
                {
                    // Process the audio data using the queuing system
                    output.ProcessAudioData(e.Buffer, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing audio: {ex.Message}");
                }
            }
            
            // Notify subscribers about available audio data
            AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
        }

        public void Dispose()
        {
            StopAudioCapture();
            
            foreach (var output in _activeOutputs.Values)
            {
                output.Dispose();
            }
            
            _activeOutputs.Clear();
            _sessionManager?.Dispose();
            _deviceEnumerator?.Dispose();
        }
    }

    public class AudioDataEventArgs : EventArgs
    {
        public byte[] Buffer { get; }
        public int BytesRecorded { get; }

        public AudioDataEventArgs(byte[] buffer, int bytesRecorded)
        {
            Buffer = buffer;
            BytesRecorded = bytesRecorded;
        }
    }
}
