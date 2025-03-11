# Windows Dual Audio Manager v1.0.0

A lightweight application that allows Windows users to output audio to multiple devices simultaneously, with individual volume control for each output.

## Architecture

Below is the architecture diagram of Windows Dual Audio Manager showing how the different components work together:

![Windows Dual Audio Manager Architecture](Architecture.svg)

## Features

- Play audio through multiple audio outputs simultaneously
- Individual volume control for each device
- Dark/Light theme support
- Real-time audio visualization
- Low latency audio processing
- System tray integration
- Startup with Windows option

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 6.0 or newer

### Installation

1. Download the latest release from the [Releases](https://github.com/MaheshSharan/WindowsDualAudioManager/releases) page
2. Extract the zip file to your preferred location
3. Run `AudioDual.exe`

### Usage

1. Launch the application
2. Select an audio device from the list
3. Click "Enable Device"
4. Repeat steps 2-3 for additional devices
5. Adjust volume for each device using the slider

## Contributing

We welcome contributions! Please see our [Contribution Guidelines](CONTRIBUTING.md) for details.

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgments

- [NAudio](https://github.com/naudio/NAudio) - Audio library for .NET
- All contributors who have helped improve this application
