# 🎙️ League of Legends Announcer Editor

## 📖 What is this software?

The **LoL Announcer Editor** is a specialized, high-performance utility designed for technical modders and audio enthusiasts who want to deeply customize the audio experience in League of Legends. 

Unlike simple file replacers, this software understands the complex relationship between League's binary data formats and the **Audiokinetic Wwise** engine. It acts as a bridge, allowing you to manipulate the game's audio logic with surgical precision.

### 🔍 Deep Logic Integration
League of Legends announcers aren't just a list of sounds; they are a complex web of "Events" (like a Kill or a Pentakill) triggered by the game engine. This tool parses three core file types simultaneously to give you a complete picture:
- **`.bin` (The Logic)**: Maps internal game events to Wwise Event IDs.
- **`.bnk` (The Bank)**: Defines how those events behave (e.g., randomizing between multiple voice lines, volume levels, and triggers).
- **`.wpk` (The Package)**: The physical storage for the compressed audio files (`.wem`).

By combining these, the editor shows you exactly which sound plays for which event, even if that event uses a complex "Random Container" with multiple variations.

### 🏗️ Wwise Project Reconstruction (The "Holy Grail")
The standout feature of this software is its ability to **reverse-engineer** binary game files back into a functional **Wwise Project (`.wproj`)**. 
- Normally, once a game is built, the original project files are gone. 
- This tool analyzes the binary HIRC (Hierarchy) data in the `.bnk` files to reconstruct the original Actor-Mixer hierarchy, containers, and event mappings.
- You can then open this project in the professional Wwise editor to add sophisticated effects, change randomization logic, or completely redesign the announcer from scratch using industry-standard tools.

### 🚀 Built for Scale
Modding a modern announcer file can mean managing over **10,000 individual audio triggers**. Most tools would lag or crash under this load. Our editor features:
- **Virtualized Rendering**: A custom-built UI that only "renders" what you see on screen, allowing you to scroll through thousands of events instantly.
- **Asynchronous Execution**: Heavy tasks like file decompression and structural analysis happen on background threads, ensuring the application remains fluid and responsive.

---

## 🚀 Quick Start Guide

### 1. Select Your Files
Locate your extracted announcer files (usually found via a WAD extractor):
- **BIN**: `.../announcer_global_female1_vo.bin`
- **WPK**: `.../announcer_global_female1_vo.wpk`
- **BNK**: `.../announcer_global_female1_vo_events.bnk`

### 2. Parse and Explore
Click **"Parse Files"**. Use the search bar to find specific triggers. You can preview the original audio directly in the editor to ensure you're editing the right line.

### 3. Replace and Save
- Select an event or a specific WEM file.
- Click **"Replace"** to swap it with your custom audio.
- Click **"Save Changes"** to generate the modified files ready for game injection.

---

## 🎨 Professional Features
- **Wwise Project Export**: Rebuild the original editing environment.
- **Batch Export**: Save all original audio files at once for backup or reference.
- **Dark/Light Mode**: A modern, sleek UI designed for long modding sessions.
- **Detailed Logging**: A built-in diagnostic console to track exactly what's happening under the hood.

---

## 📄 License & Credits
Developed by **NoxianWill**. This project is open-source under the **MIT License**.

*Disclaimer: This software is for educational and personal customization purposes only. It is not affiliated with or endorsed by Riot Games.*
