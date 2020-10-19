
# Welcome to Optime GBA!

<div align="center">
<img width="360px" src="img/icon-condensed.png">
</div>

Optime GBA is a work-in-progress Game Boy Advance emulator.  
The goal is to develop the emulator to at least decent compatibility and speed.
So far, optimization is lackluster, only reaching 200 frames
per second in Pokémon Emerald on a high-end machine.

## Screenshots

As graphics are work-in-progress, the screenshots below likely contain errors.

![Pokémon Emerald](/img/emerald.png)
![Pokémon Mystery Dungeon: Red Rescue Team](/img/pmd.png)
![Kirby: Nightmare in Dreamland](/img/kirby_nightmare_in_dreamland.png)

## Running

For building and using Optime GBA, .NET 5 is recommended. 

A compatible Game Boy Advance BIOS image is required to run the emulator. Place the BIOS in the emulator working directory named as `gba_bios.bin`. 

```
# OpenTK Debugger
dotnet run -c Release -p OptimeGBA-OpenTK.csproj
# Simple SDL Frontend 
dotnet run -c Release -p OptimeGBA-SDL.csproj
```

---

Copyright © 2020 Powerlated  
All Rights Reserved