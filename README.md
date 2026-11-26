# SP2 Server Mod

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **SimplePlanes 2** that lets a host organize custom matches:

- **Fixed spawns** for one or two teams, edited from a window inside the game (no files to write by hand).
- **Several crafts per team**: each player with the mod picks one; crafts can come from files or from the SimplePlanes website.
- **Lives and a kill limit**, enforced by the host: a player who is out cannot come back, and the match ends by itself.
- **On-screen feedback**: messages for everybody (also players without the mod), match bars and spawn markers for players with the mod.
- **Spectator camera** for players who are out of lives.

Only the host needs the mod for the rules to apply. Players without it can still play the same match.

> **Status: test build.** The model, the files and the rules are tested outside the game; the multiplayer behavior is being tested with real players.

## Install

1. Install BepInEx 5.4.23.5 x64 (Mono) in the SimplePlanes 2 folder.
2. Download `SP2ServerMod-<version>.zip` from the [Releases](../../releases) page and unzip it **into the game folder**. You should end up with `BepInEx\plugins\SP2ServerMod\SP2ServerMod.dll`.
3. Start the game and open the flight menu: there is a blue **SP2 Server Mod** button.

The zip also has `README.txt` (how to make a match) and `TEST-PLAN.txt` (what to test and what to send back).

## Build

```
dotnet build SP2-Server-Mod\SP2-Server-Mod.csproj -c Release
```

The project is compiled against the game's own DLLs; it expects the game in `C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2` (override with `-p:GameDir=...`).

| | |
|---|---|
| `python tools/make-dist.py` | builds the Release DLL and the test package `dist/SP2ServerMod-<version>.zip` |
| `tools/model-test` | tests that run outside the game against the mod's DLL and the game's own DLLs (`dotnet build`, then `dotnet run --no-build`) |
| `PLANO.md`, `CLAUDE.md` | the plan and the working notes (in Portuguese) |

## License

No license has been chosen yet: all rights reserved for now.
