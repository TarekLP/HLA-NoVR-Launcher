## Required File

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

## Zip Structure Example

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
