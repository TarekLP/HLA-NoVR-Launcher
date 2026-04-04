<p align="center">
  <img src="HLA_NoVRLauncher_Avalonia/Assets/logo.png" alt="HLA-NoVR-Launcher Logo" width="200">
</p>

<h1 align="center">HLA-NoVR-Launcher</h1>

<p align="center">
  <strong>A streamlined launcher remade in Avalonia using .NET 10.0.</strong>
</p>

---
## Features

### A modernised UI
### Fallback Version installation - Incase of HTTP Errors / Wonky Internet (Refer to the Information Section)
### Settings saving inside a .json file for easy editing
### Improved Handling and Logic upgrades (You wont really notice this but it helps with Development of better features)


## Installation

### Windows
1. Download the [latest release](https://github.com/HLANoVR/HLA-NoVR-Launcher/releases).
2. Drop the exe into a folder and run it.
3. Under the Settings page, go to the Game path section and select the Half-Life: Alyx folder (the one containing the `game` and `content` folders) using the `Browse` Button.
4. Click "Play" and profit.

### Steam Deck / Linux
If you are using a Steam Deck or Linux, please refer to the **FAQ** for specific installation instructions.

## Information

### Creation of a Fallback build
Grab a manual Install build from [here](https://www.moddb.com/mods/half-life-alyx-novr).
Name it: "hla_novr_mod".
Drop the `zip` into a Folder titled `Fallback` that's next to the EXE.
The final file path will look something like this: `HLA-NoVR-Launcher\Fallback\hla_novr_mod.zip`.

### Disclaimers
* **Folder Selection:** You'll have to select the game path folder yourself, this option is under settings where you can click the `Browse` button and navigate to your **Half-Life Alyx** installation directory (the one containing the `game` and `content` folders).
* **Compatibility:** Only official copies of the game purchased on Steam are supported.

###  Credits
* The background video was created by [Half Peeps](https://www.youtube.com/@HALFPEEPS).
* [Avalonia-UI](https://avaloniaui.net/) for allowing the easy creation of such a UI that this Launcher uses.
* [LibVLC](https://github.com/videolan/libvlcsharp) for the easy method of playing the background Video.

###  License
This program is licensed under the **GPL-3.0 License**. It utilizes a custom version of the Steam Achievement Manager, modified to facilitate command-line achievement unlocking.
