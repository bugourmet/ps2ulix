# PS2Ulix: modern USBUtil alternative for Open PS2 Loader (OPL)

**PS2Ulix** is a free, open-source tool for putting **PlayStation 2 games on a USB drive** for **Open PS2 Loader (OPL)**. It is a modern replacement for the long-abandoned **USBUtil 2.0**. It splits PS2 ISOs into the **USBExtreme / UL format** (`ul.cfg`), manages **ISO and ZSO** games in OPL's `CD/` and `DVD/` folders, downloads **cover art**, and edits **per-game OPL settings**. You get a console-style desktop app and a scriptable command-line tool.

Built with C# / .NET 9 and Avalonia. MIT licensed.

> **Status:** early. All features work in automated tests, but nothing has been tried on a real PS2 with OPL yet. Back up your drive before using it on a real game library.

---

## Contents

- [Features](#features)
- [Why PS2Ulix instead of USBUtil?](#why-ps2ulix-instead-of-usbutil)
- [Getting started](#getting-started)
- [Desktop app](#desktop-app)
- [Command-line tool](#command-line-tool)
- [Supported formats](#supported-formats)
- [How it works](#how-it-works)
- [FAQ](#faq)
- [Building and testing](#building-and-testing)
- [Credits and legal](#credits-and-legal)

---

## Features

### Game library
- **Add PS2 games from ISO or ZSO** to a USB drive. The game ID (e.g. `SLUS_209.46`) is read from the disc's `SYSTEM.CNF`, and CD vs DVD is detected automatically.
- **UL format (USBExtreme / USBAdvance):** splits games into 1 GiB parts, so DVDs bigger than 4 GB fit on **FAT32**.
- **ISO and ZSO mode:** one file per game in `CD/` or `DVD/`, with optional **ZSO compression** (OPL's LZ4 format).
- **Convert between UL, ISO and ZSO** in any direction. The original is only removed after the new copy is written.
- **Rename, delete and export** games back to a plain `.iso`.

### Drive maintenance
- **Check a USB drive for problems:** missing or wrong-size parts, corrupt `ul.cfg` records, duplicates, game ID mismatches and orphaned files.
- **Rebuild a lost or corrupt `ul.cfg`** from the part files on the drive. Saves a backup first and has a dry-run preview.
- **Safe writes:** `ul.cfg` is written to a temporary file and then swapped in. Cancelled or failed copies leave nothing half-written.

### OPL extras
- **Cover art downloader:** saves `ART/<GAME_ID>_COV.png` (plus back cover, disc label and logo if you want them) from a public mirror of the OPL Manager art database.
- **Per-game OPL settings** (`CFG/<GAME_ID>.cfg`): compatibility modes 1–6, DMA mode, virtual memory cards and alternative startup. Settings PS2Ulix doesn't know about are kept.

### Two ways to use it
- **Desktop app** in the style of the PS2 system browser: a start-up scene with rising towers, floating game cases, pad-button hints and keyboard control.
- **Command-line tool** (`ps2ulix`) for scripts and batch jobs.

## Why PS2Ulix instead of USBUtil?

USBUtil 2.0 was the standard tool for preparing USB games for USBAdvance and OPL, but it is closed-source, Windows-only and hasn't been updated in over a decade. PS2Ulix:

- is open source, with automated tests for the `ul.cfg` format and part-file naming
- supports OPL's newer **ISO folder mode** and **ZSO compression**
- can **repair** a drive, not just write to it
- downloads covers and edits OPL settings in the same app
- runs on .NET 9. It has only been tested on Windows so far; the UI toolkit (Avalonia) also targets Linux and macOS, but those are untested.

## Getting started

### Requirements
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A USB drive formatted as **FAT32** (or exFAT for newer OPL versions)
- PS2 game images you own, as `.iso` (2048-byte sectors) or `.zso`

No prebuilt downloads yet; build from source:

```bash
git clone <this-repo-url> ps2ulix
```

```bash
cd ps2ulix
```

```bash
dotnet build
```

## Desktop app

```bash
dotnet run --project src/Ps2Ulix.Gui
```

1. **Browser:** pick your USB drive (or press □ / Space to open any folder).
2. **Drive:** browse your games as floating cases. Start typing to filter. Press □ / Space for options: add a game, get covers, check the drive, rebuild `ul.cfg`, sort and filter.
3. **Game:** OPL settings, convert format, rename, export as ISO, or delete.

You can also **drag and drop** `.iso` / `.zso` files onto the window to add them.

| Key | Pad button | Action |
| --- | --- | --- |
| Enter | × | Open / confirm |
| Esc, Backspace | △ | Back / cancel |
| Space | □ | Options (open folder on the Browser screen) |
| Arrow keys | D-pad | Move the highlight |
| F2 / Del | | Rename / delete the selected game |
| F5 | | Refresh drives |
| Ctrl+O | | Add a game |

## Command-line tool

```bash
dotnet run --project src/Ps2Ulix.Cli -- <command> [arguments]
```

| Command | What it does |
| --- | --- |
| `info <iso\|zso>` | Show game ID, CD/DVD, volume name and size |
| `list <drive>` | List all games (UL, ISO and ZSO) on a drive |
| `add <drive> <iso\|zso> [--name N] [--cd\|--dvd] [--iso\|--zso]` | Add a game; UL format by default |
| `join <drive> <game> <out.iso>` | Export a UL game back to one ISO |
| `rename <drive> <game> <new-name>` | Rename a game (UL part files are renamed too) |
| `delete <drive> <game>` | Delete a game |
| `convert <drive> <game> --to ul\|iso\|zso [--move]` | Convert between formats |
| `check <drive>` | Check the drive; exit code 3 if errors are found |
| `rebuild <drive> [--dry-run]` | Rebuild `ul.cfg` from the part files |
| `compress <in.iso> <out.zso>` | Compress an ISO to ZSO |
| `decompress <in.zso> <out.iso>` | Expand a ZSO back to ISO |
| `art <drive> [<game>] [--all] [--force]` | Download cover art |
| `config <drive> <game> [--compat 1,3,4\|none] [--dma 0-7] [--vmc0 N] [--vmc1 N] [--alt-startup ID]` | Show or change per-game OPL settings |

`<game>` is the exact game name or its game ID. Example:

```bash
dotnet run --project src/Ps2Ulix.Cli -- add E:\ "Okami.iso" --zso
```

## Supported formats

| Format | Where on the drive | Notes |
| --- | --- | --- |
| **UL / USBExtreme** | `ul.<CRC>.<GAME_ID>.<part>` + `ul.cfg` in the root | Works on FAT32 for any game size. Compatible with USBUtil and OPL. |
| **ISO** | `CD/<name>.iso`, `DVD/<name>.iso` | Over 4 GiB needs exFAT (or use UL/ZSO). Old-style `SLUS_123.45.Name.iso` names are read too. |
| **ZSO** | `CD/<name>.zso`, `DVD/<name>.zso` | LZ4-compressed, 2048-byte blocks, as read by OPL. |
| **Cover art** | `ART/<GAME_ID>_COV.png` (+ `_COV2`, `_LAB`, `_LGO`) | Same naming OPL Manager uses. |
| **OPL game settings** | `CFG/<GAME_ID>.cfg` | Plain `key=value` text, as written by OPL. |

Raw `.bin` / 2352-byte sector images and PS1 discs are not supported.

## How it works

PS2Ulix follows the formats in the [Open PS2 Loader source code](https://github.com/ps2homebrew/Open-PS2-Loader):

- **`ul.cfg`** is a list of 64-byte records holding game name, game ID, part count and CD/DVD type.
- **UL part file names** include a hash of the game name, OPL's own non-standard CRC32 (ported from `USBA_crc32`). That's why renaming a UL game renames its parts.
- **ZSO** files are a 24-byte header, a block index and LZ4-compressed 2048-byte blocks.

The code is split into:
- `src/Ps2Ulix.Core`: all the format and file logic, with no UI
- `src/Ps2Ulix.Cli`: the `ps2ulix` command-line tool
- `src/Ps2Ulix.Gui`: the Avalonia desktop app

## FAQ

**Is this a replacement for USBUtil 2.0?**
Yes. It reads and writes the same UL format (`ul.cfg` + `ul.*` part files), so drives made with USBUtil should open in PS2Ulix and the other way round. This hasn't been checked against real USBUtil-made drives yet; reports welcome.

**How do I put a PS2 game bigger than 4 GB on a FAT32 USB stick?**
Add it in **UL format** (the default). It is split into 1 GiB parts that OPL joins at load time. ZSO compression can also bring a game under 4 GB.

**My `ul.cfg` is corrupt or deleted. Are my games lost?**
Usually not. Run **Check drive**, then **Rebuild ul.cfg** (or `ps2ulix rebuild <drive>`). Games recovered this way are named after their game ID, because the original names can't be recovered from the part files; rename them afterwards.

**Does it download games?**
No. PS2Ulix only manages disc images you already have. Cover art comes from a public community art mirror and is not part of this project.

**Does it work with OPL over network (SMB) or internal HDD?**
It writes the USB folder layout. Pointing it at another folder with the same layout should work, but SMB and HDD setups haven't been tested.

## Building and testing

```bash
dotnet build
```

```bash
dotnet test
```

The tests cover the `ul.cfg` format, the name hash, ISO reading, ZSO round trips, drive checks and rebuilds, and the app's screens (rendered without a window).

## Credits and legal

- File formats follow [Open PS2 Loader](https://github.com/ps2homebrew/Open-PS2-Loader) (Academic Free License 3.0).
- Font: [Rounded M+ 1c](https://github.com/google/fonts/tree/main/ofl/mplusrounded1c), SIL Open Font License 1.1 (see `src/Ps2Ulix.Gui/Assets/Fonts/OFL.txt`).
- Default cover art source: [OPL Manager art database mirror](https://github.com/Luden02/psx-ps2-opl-art-database). The art belongs to its owners; it is downloaded to your drive, not shipped with PS2Ulix.
- The desktop app's look is an original homage to the PS2 system browser. All of its graphics are drawn in code; no Sony assets are used.

PlayStation and PlayStation 2 are trademarks of Sony Interactive Entertainment. PS2Ulix is an unofficial fan project and is not affiliated with or endorsed by Sony. Use it only with games you own.

**License:** [MIT](LICENSE). The bundled font keeps its own SIL Open Font License.
