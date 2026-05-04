# League of Legends Announcer Editor

A powerful, high-performance desktop application designed for modifying League of Legends announcer audio files. This tool allows for seamless extraction, replacement, and packaging of WPK and BNK files, as well as reconstructing full Wwise projects from binary game files.

## ✨ Key Features

- **Full Fidelity BNK/WPK Parsing**: Deep analysis of Wwise bank and Riot package structures.
- **Audio Replacement**: Replace in-game announcer lines with custom WEM files with ease.
- **Wwise Project Reconstruction**: Automatically generate `.wproj` files from `.bin`, `.bnk`, and `.wpk` files for professional-grade editing in Wwise.
- **High Performance UI**:
  - **Virtualized TreeView**: Handles thousands of audio events with zero lag.
  - **Asynchronous Engine**: Non-blocking file I/O and parsing logic to keep the UI fluid.
- **Modern Architecture**: Clean, service-oriented design (Core, Models, Services, Utils, UI).
- **Theming System**: Full support for Dark and Light modes.

## 🚀 Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 7 SP1 or newer (for WPF and NAudio support)

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/noxianwill/LoL_Announcer_Editor.git
   ```
2. Navigate to the project directory:
   ```bash
   cd LoL_Announcer_Editor/LoL_Announcer_Editor
   ```
3. Build and run:
   ```bash
   dotnet run
   ```

## 🛠️ Built With

- **C# / .NET 8** - Modern language and runtime.
- **WPF** - Windows Presentation Foundation for the desktop UI.
- **NAudio** - Audio playback and waveform management.
- **NVorbis** - Vorbis decoding support.

## 📂 Project Structure

- `Core/`: Business logic for Wwise and Riot file formats.
- `Models/`: Data structures and cross-component enums.
- `Services/`: Persistent logic (Settings, Themes).
- `UI/`: Presentation layer (Windows, Converters, Styles).
- `Utils/`: Low-level helpers and centralized logging.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request or open an issue for any bugs or feature requests.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 💖 Credits

Developed with passion by NoxianWill.
Special thanks to the community for reverse-engineering the League of Legends audio formats.
