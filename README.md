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
### Improved Handling and Logic upgrades (You wont really notice this but it helps with Development of better features)
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

## Information
### Disclaimer
* **Compatibility:** Only official copies of the game purchased on Steam are supported.
###  Credits
* The background video was created by [Half Peeps](https://www.youtube.com/@HALFPEEPS).
* [Avalonia-UI](https://avaloniaui.net/) for allowing the easy creation of such a UI that this Launcher uses.
* [LibVLC](https://github.com/videolan/libvlcsharp) for the easy method of playing the background Video.

###  License
This program is licensed under the **GPL-3.0 License**. It utilizes a custom version of the Steam Achievement Manager, modified to facilitate command-line achievement unlocking.



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

---

## version.lua Format

The file must contain a line in this exact format:

```lua
DebugDrawScreenTextLine(5, GlobalSys:CommandLineInt("-h", 15) - 10, 0, "NoVR Version: Jan 04 15:08 main", 255, 255, 255, 255, 999999)
```

The important part is `"NoVR Version: <date> <branch>"` — the launcher parses:
- Everything after `NoVR Version:` as the version string
- The last word as the branch name

So for example:
- `"NoVR Version: Jan 04 15:08 main"` → branch is `main`
- `"NoVR Version: Jan 04 15:08 mods"` → branch is `mods`
- `"NoVR Version: Jan 04 15:08 steam_deck"` → branch is `steam_deck`

---

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

---

## Branch Naming

The last word in the version string determines the branch. The launcher currently recognises these three values:

| Branch name | Used for |
|---|---|
| `main` | Stable release for most users |
| `mods` | Community mods branch |
| `steam_deck` | Steam Deck and Linux users |

If a user has the wrong branch zip selected, the launcher will warn them and refuse to install.

---

## What Happens During Install

1. User clicks **Install Mod** in the launcher
2. A file picker opens — user selects the zip
3. Launcher opens the zip and finds `version.lua`
4. Launcher reads the branch from `version.lua`
5. If the branch doesn't match what the user expects, a warning is shown
6. If valid, the zip is extracted directly into the `Half-Life Alyx` folder, overwriting existing files
7. The launcher confirms installation and shows the detected mod version
