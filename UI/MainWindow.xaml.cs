using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioDual.Core;
using AudioDual.Core.Diagnostics;
using Microsoft.Win32;

namespace AudioDual.UI
{
    public partial class MainWindow : Window
    {
        private readonly IAppLogger _logger;
        private readonly AdvancedAudioEngine _audioEngine;
        private readonly AppConfiguration _config;
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly ObservableCollection<DeviceViewModel> _devices = new();
        private readonly DispatcherTimer _telemetryTimer;
        private readonly Dictionary<string, bool> _activeOutputs = new();

        public MainWindow()
        {
            InitializeComponent();

            _logger = new FileAppLogger();
            _config = AppConfiguration.Load(_logger);
            _audioEngine = new AdvancedAudioEngine(_config, _logger);

            // Version display
            Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            txtVersion.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

            // ItemsControl binding
            icDevices.ItemsSource = _devices;

            // Telemetry update timer for level meters
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _telemetryTimer.Tick += TelemetryTimer_Tick;

            // System Tray Setup
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Windows Dual Audio Manager",
                Visible = true
            };

            // Re-use application icon
            try
            {
                var iconUri = new Uri("pack://application:,,,/UI/app.ico", UriKind.RelativeOrAbsolute);
                var iconStream = System.Windows.Application.GetResourceStream(iconUri);
                if (iconStream != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream.Stream);
                }
                else
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => {
                _notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            _notifyIcon.ContextMenuStrip = contextMenu;

            // Subscribe to real-time hotplug/default changes from engine
            _audioEngine.DeviceRemoved += Engine_DeviceRemoved;
            _audioEngine.DeviceStateChanged += Engine_DeviceStateChanged;

            // Hook Window lifetime events
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;

            // Latency slider label binding
            sliderLatency.ValueChanged += (s, e) => {
                txtLatencyVal.Text = $"{(int)sliderLatency.Value} ms";
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load settings into UI
            chkStartMinimized.IsChecked = _config.StartMinimized;
            chkRunAtStartup.IsChecked = _config.RunAtStartup;
            sliderLatency.Value = _config.AudioBufferMs;

            RefreshAudioDevices();

            // Handle start minimized config
            if (_config.StartMinimized)
            {
                Hide();
                WindowState = WindowState.Minimized;
            }

            _telemetryTimer.Start();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Minimize to tray instead of closing unless system shutdown or app exit
            e.Cancel = true;
            Hide();
            WindowState = WindowState.Minimized;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void RefreshAudioDevices()
        {
            var rawDevices = _audioEngine.GetAudioDevices();
            string? captureDeviceId = _audioEngine.GetCaptureDeviceId();

            // Keep track of volumes of enabled devices to preserve them
            var volumeMap = _devices.ToDictionary(d => d.Id, d => d.VolumePercent);

            _devices.Clear();

            foreach (var dev in rawDevices)
            {
                string status = "Disabled";
                string actionText = "ENABLE";
                Visibility meterVis = Visibility.Collapsed;

                string? effectiveCaptureId = captureDeviceId ?? rawDevices.FirstOrDefault(d => d.IsDefault)?.Id;
                bool isCaptureSource = dev.Id == effectiveCaptureId;

                if (isCaptureSource)
                {
                    status = "Capture Source";
                    actionText = "DEFAULT";
                }
                else if (dev.IsEnabled || _activeOutputs.ContainsKey(dev.Id))
                {
                    status = "Enabled";
                    actionText = "DISABLE";
                    meterVis = Visibility.Visible;
                }

                double initialVol = dev.Volume * 100;
                if (volumeMap.TryGetValue(dev.Id, out var prevVol))
                {
                    initialVol = prevVol;
                }

                _devices.Add(new DeviceViewModel
                {
                    Id = dev.Id,
                    Name = dev.Name,
                    Status = status,
                    ActionText = actionText,
                    VolumePercent = initialVol,
                    IsEnabled = status == "Enabled" || status == "Capture Source",
                    IsButtonEnabled = status != "Capture Source",
                    MeterVisibility = meterVis,
                    IsDefault = dev.IsDefault
                });
            }
        }

        private void Engine_DeviceRemoved(object? sender, string deviceId)
        {
            Dispatcher.Invoke(() => {
                _activeOutputs.Remove(deviceId);
                RefreshAudioDevices();
                _logger.LogWarning("UI", $"Device with ID '{deviceId}' disconnected.");
            });
        }

        private void Engine_DeviceStateChanged(object? sender, string deviceId)
        {
            Dispatcher.Invoke(() => {
                RefreshAudioDevices();
            });
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshAudioDevices();
        }

        private void BtnToggleDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is DeviceViewModel model)
            {
                if (model.Status == "Enabled")
                {
                    if (_audioEngine.DisableDevice(model.Id))
                    {
                        model.Status = "Disabled";
                        model.ActionText = "ENABLE";
                        model.IsEnabled = false;
                        model.MeterVisibility = Visibility.Collapsed;
                        _activeOutputs.Remove(model.Id);
                    }
                }
                else if (model.Status == "Disabled")
                {
                    // Loopback feedback prevention
                    string? captureId = _audioEngine.GetCaptureDeviceId();
                    bool isCaptureSource = model.Id == captureId;

                    if (!isCaptureSource && captureId == null && model.IsDefault)
                    {
                        isCaptureSource = true;
                    }

                    if (isCaptureSource)
                    {
                        System.Windows.MessageBox.Show(
                            "This device is currently the Windows default audio output.\n\n" +
                            "The application captures system audio from the default device. " +
                            "Routing that captured audio back to the same device would create " +
                            "an audio feedback loop (echo).\n\n" +
                            "To use this device as a secondary output, first change your Windows default " +
                            "audio device to a different one.",
                            "Feedback Loop Prevention",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (_audioEngine.EnableDevice(model.Id, (float)(model.VolumePercent / 100.0)))
                    {
                        model.Status = "Enabled";
                        model.ActionText = "DISABLE";
                        model.IsEnabled = true;
                        model.MeterVisibility = Visibility.Visible;
                        _activeOutputs[model.Id] = true;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Failed to enable output device. Please check log details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                RefreshAudioDevices();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && slider.Tag is DeviceViewModel model && model.IsEnabled)
            {
                float volume = (float)(slider.Value / 100.0);
                _audioEngine.SetDeviceVolume(model.Id, volume);
                model.VolumePercent = slider.Value;
            }
        }

        private void TelemetryTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var dev in _devices)
            {
                if (dev.Status == "Enabled")
                {
                    var snapshot = _audioEngine.Telemetry.GetSnapshot(dev.Id);
                    dev.PeakValue = Math.Clamp(snapshot.PeakLevel * 100, 0, 100);
                    dev.RmsValue = Math.Clamp(snapshot.RmsLevel * 100, 0, 100);
                }
                else
                {
                    dev.PeakValue = 0;
                    dev.RmsValue = 0;
                }
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _config.StartMinimized = chkStartMinimized.IsChecked ?? false;
            _config.RunAtStartup = chkRunAtStartup.IsChecked ?? false;
            _config.AudioBufferMs = (int)sliderLatency.Value;

            SetStartupWithWindows(_config.RunAtStartup);
            _config.Save(_logger);

            System.Windows.MessageBox.Show("Settings saved successfully!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SetStartupWithWindows(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        string runPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                        if (!string.IsNullOrEmpty(runPath))
                        {
                            key.SetValue("WindowsDualAudioManager", runPath);
                        }
                    }
                    else
                    {
                        key.DeleteValue("WindowsDualAudioManager", false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UI", "Error setting Windows startup registry key.", ex);
            }
        }

        public void Shutdown()
        {
            _telemetryTimer.Stop();
            _audioEngine.Dispose();
            _notifyIcon.Dispose();
        }
    }

    public class DeviceViewModel : INotifyPropertyChanged
    {
        private string _status = "Disabled";
        private string _actionText = "ENABLE";
        private double _volumePercent = 100;
        private bool _isEnabled = false;
        private bool _isButtonEnabled = true;
        private Visibility _meterVisibility = Visibility.Collapsed;
        private double _peakValue = 0;
        private double _rmsValue = 0;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        public bool IsButtonEnabled
        {
            get => _isButtonEnabled;
            set { _isButtonEnabled = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string ActionText
        {
            get => _actionText;
            set { _actionText = value; OnPropertyChanged(); }
        }

        public double VolumePercent
        {
            get => _volumePercent;
            set
            {
                _volumePercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeText));
            }
        }

        public string VolumeText => $"{_volumePercent:0}%";

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public Visibility MeterVisibility
        {
            get => _meterVisibility;
            set { _meterVisibility = value; OnPropertyChanged(); }
        }

        public double PeakValue
        {
            get => _peakValue;
            set { _peakValue = value; OnPropertyChanged(); }
        }

        public double RmsValue
        {
            get => _rmsValue;
            set { _rmsValue = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
