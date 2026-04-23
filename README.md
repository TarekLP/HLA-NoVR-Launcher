<p align="center">
  <img src="HLA_NoVRLauncher_Avalonia/Assets/logo.png" alt="HLA-NoVR-Launcher Logo" width="200">
</p>

<h1 align="center">HLA-NoVR-Launcher</h1>

<p align="center">
  <strong>A streamlined launcher remade in Avalonia using .NET 10.0.</strong>
</p>

---
## Features

### A modernised UI - Including a First Time Setup
### Settings saving inside a .json file for easy editing
### Crash Log Viewer for easy Error Finding and reporting of Bugs


## Installation

### Windows
1. Download the [latest release](https://github.com/HLANoVR/HLA-NoVR-Launcher/releases).
2. Download your desired Build from the [HLA-NoVR Repo](https://github.com/HLANoVR/HLA-NoVR), keep it in a zip file (make sure the game folder is in it but DO NOT EXTRACT)
3. Drop the exe into a folder and run it.
4. Go through the Firs time setup to adjust everything, when it asks you to select a .zip file, select the one you downloaded earlier.
5. Click "Play" and profit.

### Steam Deck / Linux
If you are using a Steam Deck or Linux, please refer to the **FAQ** for specific installation instructions.

---
## Information


### Mod Branch Naming
The launcher currently recognises these three values:

| Branch name | Used for |
|---|---|
| `main` | Stable release for most users |
| `mods` | Community mods branch |
| `steam_deck` | Steam Deck and Linux users |


### Disclaimer
* **Compatibility:** Only official copies of the game purchased on Steam are supported.
###  Credits
* The background video was created by [Half Peeps](https://www.youtube.com/@HALFPEEPS).
* [Avalonia-UI](https://avaloniaui.net/) for allowing the easy creation of such a UI that this Launcher uses.
* [LibVLC](https://github.com/videolan/libvlcsharp) for the easy method of playing the background Video.

###  License
This program is licensed under the **GPL-3.0 License**. It utilizes a custom version of the Steam Achievement Manager, modified to facilitate command-line achievement unlocking.

---

# Technical Explanation of Installation
The zip **must** contain `version.lua` at this exact path:

```
game/hlvr/scripts/vscripts/version.lua
```

The launcher reads this file to:
- Confirm the zip is a valid HLA NoVR mod zip
- Detect which branch the zip belongs to (`main`, `mods`, or `steam_deck`)
- Display the installed mod version to the user

If this file is missing, the launcher will reject the zip entirely.



## Zip Structure

```
HLA-NoVR-main.zip
└── game/
    └── hlvr/
        ├── scripts/
        │   └── vscripts/
        │       ├── version.lua           ← required
        │       ├── bindings.lua
        │       └── (other script files)
        ├── cfg/
        │   └── (config files)
        └── (other game files)
```

The zip should **not** contain a top-level folder named after the zip itself. The `game/` folder should be at the root of the zip, not nested inside a `HLA-NoVR-main/game/` folder — otherwise the launcher will extract it to the wrong location.
