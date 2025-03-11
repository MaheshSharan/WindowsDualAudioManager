# Windows Dual Audio Manager - User Guide

## Overview

Windows Dual Audio Manager allows you to route your system audio to multiple output devices simultaneously with individual volume control for each device. This guide will help you get the most out of the application.

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Main Interface](#main-interface)
4. [Managing Audio Devices](#managing-audio-devices)
5. [Volume Control](#volume-control)
6. [Application Settings](#application-settings)
7. [Theme Options](#theme-options)
8. [System Tray Features](#system-tray-features)
9. [Troubleshooting](#troubleshooting)
10. [Keyboard Shortcuts](#keyboard-shortcuts)
11. [Architecture](#architecture)

## Installation

1. Download the latest release from the [official repository](https://github.com/MaheshSharan/WindowsDualAudioManager/releases)
2. Extract all files to a folder of your choice
3. Run `AudioDual.exe` to start the application
4. (Optional) Enable "Run at startup" in settings for automatic launch

## Quick Start

1. Launch the application
2. Select an audio output device from the list
3. Click "Enable Device"
4. Repeat to enable additional devices
5. Adjust volume sliders as needed

## Main Interface

![Main Interface](screenshots/main_interface.png)

The main interface consists of:

- **Device List**: Shows all available audio output devices
- **Device Controls**: Enable/disable devices and control volume
- **Audio Visualization**: Visual representation of audio levels
- **Settings**: Application configuration options

## Managing Audio Devices

### Enabling a Device

1. Select the device from the list
2. Click the "Enable Device" button
3. The device status will change to "Enabled"
4. Audio will now play through this device

### Disabling a Device

1. Select the enabled device from the list
2. Click the "Disable Device" button
3. The device status will change to "Disabled"
4. Audio will no longer play through this device

### Refreshing the Device List

Click the "Refresh Devices" button to update the list of available devices. This is useful if you connect or disconnect audio devices while the application is running.

## Volume Control

Each enabled device can have its volume controlled independently:

1. Select the enabled device from the list
2. Use the volume slider to adjust the volume
3. The volume percentage is displayed next to the slider
4. Changes take effect immediately

## Application Settings

### Start Minimized

When enabled, the application will start in the system tray instead of showing the main window.

### Run at Startup

When enabled, the application will automatically start when Windows starts.

### Saving Settings

Click "Save Settings" to persist your configuration changes.

## Theme Options

Windows Dual Audio Manager supports both light and dark themes:

1. Access theme options from the system tray menu
2. Select "Toggle Theme" to switch between light and dark mode
3. The application will automatically detect and use your Windows theme setting on startup

## System Tray Features

The application runs in your system tray for quick access:

- **Double-click**: Open the main window
- **Right-click menu**:
  - Show: Open the main window
  - Toggle Theme: Switch between light and dark mode
  - Exit: Close the application

## Troubleshooting

### No Sound from Secondary Devices

1. Verify the device is properly connected and powered on
2. Check that the device shows as "Enabled" in the device list
3. Ensure the volume slider is not set to zero
4. Click "Refresh Devices" and try enabling the device again

### Audio Stuttering or Cutting Out

1. Try adjusting the volume of the affected device
2. Disable and re-enable the problematic device
3. Restart the application
4. Update your audio drivers

### Application Won't Start

1. Ensure you have .NET 6.0 or newer installed
2. Try running the application as administrator
3. Check your antivirus isn't blocking the application

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| F5 | Refresh devices |
| Space | Enable/disable selected device |
| Up/Down | Adjust volume up/down |
| Esc | Minimize to system tray |
| Alt+T | Toggle theme |
| Alt+X | Exit application |

## Architecture

The application uses a layered architecture to efficiently route audio to multiple devices:

![Architecture Diagram](Architecture.svg)

The diagram shows how audio is captured from the system, processed through various buffers, and then output to multiple devices with individual volume control.

---

For additional support, please visit our [GitHub repository](https://github.com/MaheshSharan/WindowsDualAudioManager/issues) or contact support@dualaudio.example.com.
