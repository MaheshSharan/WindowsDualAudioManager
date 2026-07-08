using AudioDual.Core;
using AudioDual.Core.Diagnostics;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AudioDual
{
    public partial class MainForm : Form
    {
        private readonly IAppLogger _logger;
        private readonly AdvancedAudioEngine _audioEngine;
        private readonly AppConfiguration _config;
        private readonly NotifyIcon _notifyIcon;
        private readonly List<AudioDevice> _audioDevices;
        private readonly Dictionary<string, bool> _activeOutputs = new Dictionary<string, bool>();
        private AudioDevice? _selectedDevice;
        private bool _isDarkTheme = false;

        public MainForm()
        {
            InitializeComponent();
            
            // Add version number to the window title
            Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = $"Windows Dual Audio Manager v{version?.Major}.{version?.Minor}.{version?.Build}";
            
            // Initialize core components
            _logger = new FileAppLogger();
            _config = AppConfiguration.Load(_logger);
            _audioEngine = new AdvancedAudioEngine(_config, _logger);
            _audioDevices = new List<AudioDevice>();
            
            // Setup system tray icon
            _notifyIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "Windows Dual Audio Manager",
                Visible = true
            };
            
            _notifyIcon.DoubleClick += (s, e) => ShowMainForm();
            
            // Create context menu for tray icon
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowMainForm());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Toggle Theme", null, (s, e) => ToggleTheme());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            // Set up event handlers
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
            
            // Setup tooltips
            SetupTooltips();
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            // Load audio devices
            RefreshAudioDevices();
            
            // Load settings into UI
            checkStartMinimized.Checked = _config.StartMinimized;
            checkRunAtStartup.Checked = _config.RunAtStartup;
            
            // Apply theme based on system settings
            _isDarkTheme = IsSystemUsingDarkTheme();
            ApplyTheme(_isDarkTheme);
            
            // Start minimized if configured
            if (_config.StartMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                return;
            }
            
            // Cleanup resources
            _audioEngine.Dispose();
            _notifyIcon.Dispose();
        }

        private void ShowMainForm()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
                Activate();
            }
        }

        private void RefreshAudioDevices()
        {
            var devices = _audioEngine.GetAudioDevices();
            
            // Preserve volumes for devices that are already active before the list is rebuilt
            var deviceVolumes = new Dictionary<string, float>();
            
            foreach (var device in _audioDevices)
            {
                if (device.IsEnabled)
                {
                    deviceVolumes[device.Id] = device.Volume;
                }
            }
            
            // Refresh device list
            _audioDevices.Clear();
            _audioDevices.AddRange(devices);
            
            // Properly mark devices that are enabled
            foreach (var device in _audioDevices)
            {
                if (_activeOutputs.ContainsKey(device.Id))
                {
                    device.IsEnabled = true;
                    if (deviceVolumes.ContainsKey(device.Id))
                    {
                        device.Volume = deviceVolumes[device.Id];
                    }
                }
            }
            
            // Update UI with device list
            lvDevices.Items.Clear();
            foreach (var device in _audioDevices)
            {
                var item = new ListViewItem(device.Name);
                item.SubItems.Add(device.IsDefault ? "Yes" : "No");
                item.SubItems.Add(device.IsEnabled ? "Enabled" : "Disabled");
                item.SubItems.Add($"{device.Volume * 100:0}%");
                item.Tag = device;
                lvDevices.Items.Add(item);
            }
            
            // Disable device controls if no device is selected
            UpdateDeviceControls();
        }

        private void UpdateDeviceControls()
        {
            bool deviceSelected = _selectedDevice != null;
            btnToggleDevice.Enabled = deviceSelected;
            trackVolume.Enabled = deviceSelected && _selectedDevice?.IsEnabled == true;
            
            if (deviceSelected)
            {
                btnToggleDevice.Text = _selectedDevice?.IsEnabled == true ? "Disable Device" : "Enable Device";
                int volumeValue = (int)(_selectedDevice?.Volume * 100 ?? 100);
                trackVolume.Value = volumeValue;
                lblVolumePercentage.Text = $"{volumeValue}%";
            }
            else
            {
                btnToggleDevice.Text = "Enable Device";
                trackVolume.Value = 100;
                lblVolumePercentage.Text = "100%";
            }
        }
        
        private void SetStartupWithWindows(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("WindowsDualAudioManager", Application.ExecutablePath);
                        }
                        else
                        {
                            key.DeleteValue("WindowsDualAudioManager", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("MainForm", "Error setting startup-with-Windows registry key.", ex);
                MessageBox.Show($"Error setting startup: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupTooltips()
        {
            var toolTip = new ToolTip();
            toolTip.SetToolTip(btnRefreshDevices, "Refresh the list of available audio devices");
            toolTip.SetToolTip(btnToggleDevice, "Enable or disable the selected audio device");
            toolTip.SetToolTip(trackVolume, "Adjust volume for the selected device");
            toolTip.SetToolTip(lblVolumePercentage, "Current volume percentage");
            toolTip.SetToolTip(checkStartMinimized, "Start the application minimized in the system tray");
            toolTip.SetToolTip(checkRunAtStartup, "Launch automatically when Windows starts");
            toolTip.SetToolTip(btnSaveSettings, "Save your configuration settings");
        }
        
        private void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }
        
        private bool IsSystemUsingDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value != null && value is int intValue)
                    {
                        return intValue == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MainForm", $"Could not read system theme preference, defaulting to light theme: {ex.Message}");
            }
            
            return false;
        }
        
        private void ApplyTheme(bool isDark)
        {
            Color backColor = isDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
            Color foreColor = isDark ? Color.White : SystemColors.ControlText;
            Color controlBackColor = isDark ? Color.FromArgb(48, 48, 48) : SystemColors.Window;
            
            // Apply to form
            this.BackColor = backColor;
            this.ForeColor = foreColor;
            
            // Apply to all controls
            foreach (Control control in this.Controls)
            {
                control.BackColor = control is Button || control is TextBox || control is ComboBox ? 
                                    controlBackColor : backColor;
                control.ForeColor = foreColor;
                
                // Handle nested controls in groupboxes
                if (control is GroupBox groupBox)
                {
                    foreach (Control nestedControl in groupBox.Controls)
                    {
                        nestedControl.BackColor = nestedControl is Button || nestedControl is TextBox ? 
                                                controlBackColor : backColor;
                        nestedControl.ForeColor = foreColor;
                    }
                }
            }
            
            // ListView needs special handling
            lvDevices.BackColor = controlBackColor;
            lvDevices.ForeColor = foreColor;
            
            // Show audio visualization based on theme
            audioVisualizer.BackColor = isDark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 240, 240);
        }

        #region Event Handlers
        
        private void lvDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvDevices.SelectedItems.Count > 0)
            {
                _selectedDevice = lvDevices.SelectedItems[0].Tag as AudioDevice;
            }
            else
            {
                _selectedDevice = null;
            }
            
            UpdateDeviceControls();
        }
        
        private void btnRefreshDevices_Click(object sender, EventArgs e)
        {
            RefreshAudioDevices();
        }
        
        private void btnToggleDevice_Click(object sender, EventArgs e)
        {
            if (_selectedDevice == null) return;
            
            bool success = false;
            
            if (_selectedDevice.IsEnabled)
            {
                // Disable the device
                success = _audioEngine.DisableDevice(_selectedDevice.Id);
                if (success)
                {
                    _selectedDevice.IsEnabled = false;
                    _activeOutputs.Remove(_selectedDevice.Id);
                }
            }
            else
            {
                // Enable the device
                success = _audioEngine.EnableDevice(_selectedDevice.Id, _selectedDevice.Volume);
                if (success)
                {
                    _selectedDevice.IsEnabled = true;
                    _activeOutputs[_selectedDevice.Id] = true;
                }
            }
            
            // Ensure UI is updated correctly
            UpdateDeviceControls();
            RefreshAudioDevices();
        }
        
        private void trackVolume_Scroll(object sender, EventArgs e)
        {
            if (_selectedDevice != null && _selectedDevice.IsEnabled)
            {
                float volume = trackVolume.Value / 100f;
                _selectedDevice.Volume = volume;
                _audioEngine.SetDeviceVolume(_selectedDevice.Id, volume);
                
                // Update the volume percentage display
                lblVolumePercentage.Text = $"{trackVolume.Value}%";
                
                // Update the volume in the list view
                if (lvDevices.SelectedItems.Count > 0)
                {
                    lvDevices.SelectedItems[0].SubItems[3].Text = $"{volume * 100:0}%";
                }
            }
        }
        
        private void btnSaveSettings_Click(object sender, EventArgs e)
        {
            _config.StartMinimized = checkStartMinimized.Checked;
            _config.RunAtStartup = checkRunAtStartup.Checked;
            
            // Set startup with Windows
            SetStartupWithWindows(_config.RunAtStartup);
            
            _config.Save(_logger);
            MessageBox.Show("Settings saved successfully!", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        
        private void audioVisualizer_Paint(object sender, PaintEventArgs e)
        {
            // The previous implementation drew bars from each device's static Volume
            // setting rather than any real audio signal, which looked like a live meter
            // but was not one. Rather than ship fake data, this is left as an honest
            // placeholder state until LatencyTelemetry-backed real levels land (Phase 3
            // of the v1.2 refactor); see WindowsDualAudioManager_v1.2_Refactor_Plan.md.
            using var mutedTextBrush = new SolidBrush(_isDarkTheme ? Color.Gray : Color.DarkGray);
            using var font = new Font(Font.FontFamily, 8f, FontStyle.Italic);
            const string pendingMessage = "Live level meter — coming in the v1.2 UI phase";
            var textSize = e.Graphics.MeasureString(pendingMessage, font);
            e.Graphics.DrawString(
                pendingMessage,
                font,
                mutedTextBrush,
                (audioVisualizer.Width - textSize.Width) / 2f,
                (audioVisualizer.Height - textSize.Height) / 2f);
        }
        
        #endregion
    }
}
