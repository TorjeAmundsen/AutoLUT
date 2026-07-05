# How the calibration savestates work

AutoLUT calibrates against gz savestates that fill the whole screen with a **known** color. Here's where that fill comes from and how each savestate carries its color.

## The fill is a normal game feature

*Ocarina of Time* already knows how to paint the screen a solid color. The function is `Environment_FillScreen()`, defined in [`src/code/z_kankyo.c`](https://github.com/zeldaret/oot/blob/main/src/code/z_kankyo.c) of the [zeldaret/oot](https://github.com/zeldaret/oot) decompilation. The main draw loop in [`src/code/z_play.c`](https://github.com/zeldaret/oot/blob/main/src/code/z_play.c) calls it every frame, reading two values out of the scene's environment context (`envCtx`, part of the `PlayState` struct):

- **`fillScreen`** - a switch: `1` = fill the screen, `0` = don't.
- **`screenFillColor`** - the color, as four bytes: red, green, blue, alpha (`FF` alpha = fully opaque).

```c
// z_play.c, once per frame
switch (this->envCtx.fillScreen) {
    case 1:
        Environment_FillScreen(gfxCtx,
                               this->envCtx.screenFillColor[0],  // R
                               this->envCtx.screenFillColor[1],  // G
                               this->envCtx.screenFillColor[2],  // B
                               this->envCtx.screenFillColor[3],  // A
                               FILL_SCREEN_OPA | FILL_SCREEN_XLU);
        break;
}
```

Several parts of the game *set* these two fields - **cutscene color fades** (`CutsceneCmd_Transition` in `z_demo.c`), **warp-song transitions** (`ovl_Door_Warp1`), the **game-over darkening** (`z_kankyo.c`) - but none of them call `Environment_FillScreen` themselves; they just write `fillScreen` and `screenFillColor` into memory. The one draw call that turns those bytes into pixels is the `z_play.c` loop above. So all a savestate needs is the two field values - the game does the rest.

This is stock game code - no ROM hack. gz only *edits the memory*; the game does the drawing.

The fill covers the 3D image but not the HUD, which draws on top - so the hearts/buttons in the corners are fine, but the **center** of the screen shows the pure color.

## How a savestate stores the color

A gz savestate isn't a dump of all of RAM - its `save_state()` ([`src/gz/state.c`](https://github.com/glankk/gz/blob/master/src/gz/state.c)) saves a specific set of game state: the game/scene context, the save file, loaded overlays, the actor heap, audio state, and more. Importantly, it copies the **entire `PlayState` struct** verbatim, and `screenFillColor` lives inside `PlayState` - so the color is captured in the file. Load the state and the game re-draws that exact color. That's the whole trick: AutoLUT knows the color because it's baked into the savestate.

The `.gzs` file has no compression and no checksum - the only rule checked on load is that a `size` field in its header matches the file length. So a color can be swapped just by **overwriting those 4 color bytes in place** (same length in, same length out), and the file stays valid.

## How the set is generated

We keep one **template** savestate per game version, then stamp colors into copies:

- `savestates/deadbeef-1.0.gzs` / `deadbeef-1.2.gzs` - templates. Their fill color is set to a recognizable marker (`DE AD BE EF`) so it's easy to find in the file.
- `savestates/PALETTE.txt` - one line per output: `RRGGBBAA name`.
- `savestates/generate_states.py` - for each palette line, copies the template, finds the marker, and overwrites it with the real color. Outputs go to `lut_gzs_<version>/`.

## Version locking

gz savestates only load on the exact game version they were made for, so AutoLUT ships separate `lut_gzs_1.0/` and `lut_gzs_1.2/` sets - use the one matching your ROM. The screen-fill feature itself is the same across versions; only the savestate file is version-specific.

## Per-version addresses

`fillScreen` and `screenFillColor` sit at the same offset inside `PlayState`, but `PlayState` itself is loaded at a different RAM address in each game version - so the absolute addresses differ. Values below are for editing memory directly (e.g. in gz's memory editor) when building a template:

| Version | `fillScreen` | `screenFillColor` (R,G,B,A) |
|---|---|---|
| NTSC 1.0 | `0x801D8FA5` | `0x801D8FA6`–`0x801D8FA9` |
| NTSC 1.1 | `0x801D9165` | `0x801D9166`–`0x801D9169` |
| NTSC 1.2 | `0x801D9865` | `0x801D9866`–`0x801D9869` |

**NTSC 1.1 savestates have not been made yet** - only 1.0 and 1.2 sets ship today. The 1.1 addresses are listed above and a 1.1 set will be generated on request.
