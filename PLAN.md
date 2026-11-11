# PS2 USBUtil remake — research + plan

Target: USBUtil 2.0/2.1 (closed-source Windows app, abandoned ~2008–2010). Manages PS2 games on FAT32 USB drives for USBAdvance / USBExtreme / Open PS2 Loader (OPL).

## Original features
- Create game from ISO (or CD/DVD drive) → split into ≤1 GB parts, add entry to `ul.cfg`
- List games in `ul.cfg`: name, ID, parts, media, size
- Rename game (rewrites name + renames all parts, since CRC of name is in filename)
- Delete game (parts + entry)
- Join parts back → ISO
- Check integrity: missing parts, orphan `ul.*` files not in cfg, size mismatches
- Recover/rebuild `ul.cfg` from part files on disk
- Fragmentation check (USBAdvance needed contiguous files) + defrag hint
- Convert ISO ⇄ UL, copy/move between drives
- Free-space display

## File format (verified from OPL source `include/supportbase.h`, `src/supportbase.c`)
`ul.cfg` = array of 64-byte records, no header:

| Off  | Size | Field    | Value |
|------|------|----------|-------|
| 0x00 | 32   | name     | char[], not NUL-terminated if 32 long |
| 0x20 | 3    | magic    | `"ul."` |
| 0x23 | 12   | startup  | e.g. `SLUS_123.45` |
| 0x2F | 1    | parts    | slice count |
| 0x30 | 1    | media    | `0x12` CD / `0x14` DVD (SCECdPS2CD/DVD — verify in ps2sdk libcdvd) |
| 0x31 | 4    | zero     | |
| 0x35 | 1    | 0x08     | always 0x08 |
| 0x36 | 10   | zero     | |

Part filename: `ul.%08X.%s.%02x` → `ul.<USBA_crc32(name)>.<startup>.<part hex>`.

`USBA_crc32` = non-standard CRC32 (poly 0x04C11DB7, reversed table index, includes trailing NUL). Port exactly from OPL source (`src/util.c` or similar — grep `USBA_crc32`). Below is from memory, verify:

```c
unsigned int USBA_crc32(const char *string) {
    int crc, table, count, byte;
    for (table = 0; table < 256; table++) {
        crc = table << 24;
        for (count = 8; count > 0; count--)
            crc = (crc < 0) ? (crc << 1) : ((crc << 1) ^ 0x04C11DB7);
        crctab[255 - table] = crc;
    }
    do {
        byte = string[count++];
        crc = crctab[byte ^ ((crc >> 24) & 0xFF)] ^ ((crc << 8) & 0xFFFFFF00);
    } while (string[count - 1] != 0);
    return crc;
}
```

Game ID: read `SYSTEM.CNF` from ISO9660 root → `BOOT2 = cdrom0:\SLUS_123.45;1` → `SLUS_123.45`.
Media: ISO size > ~700 MB or DVD layout → DVD (0x14), else CD (0x12). Let user override.

Modern OPL extras worth adding: plain ISO mode (`CD/`, `DVD/` folders, `SLUS_123.45.Name.iso` naming, ≤4 GB on FAT32), ZSO compression, cover art download to `ART/` (`<ID>_COV.png`), `CFG/` per-game config, exFAT support.

## Architecture
Core lib (no UI) + thin UIs.
- **core**: `ulcfg` (parse/write 64-byte records, atomic write via `ul.cfg.new` → rename), `crc` (USBA_crc32), `iso9660` (read SYSTEM.CNF, no extraction needed), `splitter` (stream ISO → 1 GB parts, progress + cancel, hash), `joiner`, `scanner` (integrity, orphans, rebuild cfg), `drives` (list removable drives, free space, FS type).
- **cli**: `ps2ulix list|add|rename|delete|join|check|rebuild <drive>` — easy to test, scriptable.
- **gui**: drive picker, game table, drag-drop ISO, progress bar.

Stack (chosen): **C# / .NET 9** — `src/Ps2Ulix.Core` (lib), CLI, **Avalonia** GUI; xUnit tests in `tests/`.

## Milestones
1. ✅ `crc` + `ulcfg` read/write, unit tests (still need real USBUtil/OPL files to confirm CRC)
2. ✅ ISO9660 ID reader, media detection (UDF bridge → DVD, else >360000 sectors → DVD; tested on synthetic ISOs only)
3. ✅ Split/add game, join, delete, rename (CLI `ps2ulix`)
4. ✅ Integrity check + cfg rebuild (`check`, `rebuild`; fragmentation check not done)
5. ✅ GUI (`ps2ulix-gui`, Avalonia 11.3: cover library, living backdrop, in-window sheets; headless tests)
6. ✅ ISO/ZSO folder mode + convert, ZSO compress/decompress, cover art (ART/), per-game OPL config (CFG/)

Test data needed: a real `ul.cfg` + part names produced by original USBUtil or OPL to confirm CRC byte-for-byte.

## Sources
- OPL source: https://github.com/ps2homebrew/Open-PS2-Loader (include/supportbase.h, src/supportbase.c)
- OPL USB wiki: https://github.com/ps2homebrew/Open-PS2-Loader/wiki/usb-mode
- Related tools: libopl (https://pypi.org/project/libopl), PS2IsoManager (https://github.com/imadali120/PS2IsoManager)
