# Windows Dual Audio Manager v1.2

Route your system audio to multiple output devices simultaneously — speakers, headphones, Bluetooth — with per-device volume control.

---

> [!IMPORTANT]
> **Known Limitation: Bluetooth Sync Delay**
>
> If your **Windows default device is a wired/built-in speaker** and you enable a **Bluetooth device** as a secondary output, you will hear an audible delay between the two (typically 150–300ms or more depending on your Bluetooth hardware).
>
> **Why this happens:** Bluetooth audio (A2DP) must encode, packetize, and wirelessly transmit every audio frame before it reaches the speaker. This is a hardware-level constraint — no software can eliminate it.
>
> **Workaround (sync both devices):** Set your **Bluetooth speaker as the Windows default audio device** first (right-click speaker icon → Sound settings → choose your BT device). Then launch this app and enable your wired/built-in speaker as the secondary output. The result: both devices play in near-sync because our low-latency pipeline handles the fast wired device, and Windows handles the BT device natively.
>
> **Volume Scaling Dependency:** Because Windows loopback captures audio *after* the default device's volume mix is applied, lowering the default device's master volume slider will automatically reduce the incoming audio volume sent to secondary devices. Set your primary device volume first, then balance secondary outputs relative to it.
>
> There is no universal automatic fix for this — the best pairing depends on what devices you have. Experiment with which device you set as the Windows default.

---

## What It Does

- **Fan-out audio** — play the same system audio on multiple devices at the same time
- **Per-device volume control** — independently adjust each output's level
- **Low-latency pipeline** — event-driven WASAPI routing (~30–45ms), not polling buffers
- **Feedback loop protection** — the app blocks you from enabling the capture source device as an output (which would cause an echo loop) and explains why
- **Device hotplug** — detects when a device is disconnected mid-session and stops that channel cleanly
- **System tray** — runs quietly in the background, restore with a double-click
- **Dark / Light theme** — auto-detects your Windows theme preference
- **Startup with Windows** — optional, configurable in Settings

---

## How to Use

1. Launch `AudioDual.exe`
2. The device list shows all active audio endpoints. The device marked **Capture Source** is what the app is capturing audio from (your Windows default device) — **you cannot enable it as an output**
3. Select any other device and click **Enable Device**
4. Repeat for additional devices
5. Use the volume slider to adjust each device independently
6. Click **Refresh** if you plug in a new device and it doesn't appear

---

## Prerequisites

- Windows 10 / 11
- .NET 6.0 Runtime or newer ([download](https://dotnet.microsoft.com/download/dotnet/6.0))

## Installation

1. Download the latest release from the [Releases](https://github.com/MaheshSharan/WindowsDualAudioManager/releases) page
2. Extract the zip to any folder
3. Run `AudioDual.exe`

---

Key properties:
- **One buffer per output** — no ConcurrentQueue + CircularBuffer + BufferedWaveProvider stack
- **No polling** — data moves on WASAPI's own callback threads, no `Task.Delay` loop
- **Adaptive ring buffer** — sized to the device's actual WASAPI period, so Bluetooth devices with large hardware periods don't trigger cascading underruns
- **MMCSS thread scheduling** — capture thread registered with Windows' Multimedia Class Scheduler for glitch-resistant priority, not a reflection hack

---

## Building from Source

```powershell
git clone https://github.com/MaheshSharan/WindowsDualAudioManager
cd WindowsDualAudioManager
dotnet restore
dotnet build AudioDual.sln -c Release
```

Run tests:

```powershell
dotnet test AudioDual.Core.Tests\AudioDual.Core.Tests.csproj
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## License

MIT — see [LICENSE.md](LICENSE.md)

---

## Acknowledgements

- [NAudio](https://github.com/naudio/NAudio) — WASAPI loopback capture and render
