
# Welcome to Optime GBA!

<div align="center">
<img width="360px" src="img/optime-gba-full.gif">
</div>

Optime GBA is a work-in-progress Game Boy Advance emulator.  

I aim to develop Optime GBA to the point where it can run most popular games with decent performance.
So far, optimization is somewhat lackluster, only managing around 250 frames
per second in Pokémon Emerald on a high-end machine.

## Current Progress
 - Passing all of [ARMWrestler](https://github.com/destoer/armwrestler-gba-fixed), a simple test of the GBA's CPU
 - Passing all of [jsmolka's gba-suite](https://github.com/jsmolka/gba-suite) CPU instruction tests
 - Timers and DMA are implemented
 - Audio is fully implemented and sounds great on most games
 - Limited emulation of the Pixel Processing Unit (PPU)
    - Non-affine background rendering
    - Affine and non-affine sprite support
    - Alpha blending
    - Windowing
 - Save files work for most games using flash memory

## Future Plans
 - Optimization, optimization, optimization
 - Implement and fix the remaining PPU features
    - Affine backgrounds
 - Seek out any unimplemented ARM7TDMI quirks that may remain

## Controls
 - **Z** - B
 - **X** - A
 - **Start** - Enter
 - **Select** - Backspace
 - **Left** - Left
 - **Right** - Right
 - **Up** - Up
 - **Down** - Down

### Accessory Controls
 - **Turbo** - Tab, Space
 - **Toggle Color Correction** - F1
 - **Toggle Sound FIFO A** - F3
 - **Toggle Sound FIFO B** - F4
 - **Toggle Sound PSG 1** - F5
 - **Toggle Sound PSG 2** - F6
 - **Toggle Sound PSG 3** - F7
 - **Toggle Sound PSG 4** - F8
 - **Fullscreen** - ALT + Enter, F11


## Screenshots

As graphics are under construction, screenshots below likely contain errors.

![Pokémon Emerald](/img/emerald.png)
![Pokémon Mystery Dungeon: Red Rescue Team](/img/pmd.png)
![Kirby: Nightmare in Dreamland](/img/kirby_nightmare_in_dreamland.png)

## Running

For building and using Optime GBA, .NET Core 3.1 is recommended. 

A compatible Game Boy Advance BIOS image is required to run the emulator. Place the BIOS in the emulator working directory (the root of the repository when using `dotnet run`) named as `gba_bios.bin`. 

```
# OpenTK Debugger
dotnet run -c Release -p OptimeGBA-OpenTK.csproj
# Simple SDL Frontend 
dotnet run -c Release -p OptimeGBA-SDL.csproj
```
