# OreHitboxVisualizer

A BepInEx mod for Valheim that shows the support hitbox of every piece of a mineable deposit (copper, silver, big rocks and any other `MineRock5` rock) as soon as you hit it with a pickaxe. A piece is drawn red when its hitbox collides with something and green when it touches nothing. Client-side only.

[Versión en español](README.es.md)

![A copper deposit in the Black Forest: 126 of its 130 nodes are colliding, 4 are free](docs/copper_deposit.jpg)

![A big rock in the Plains: the bottom pieces rest on the ground (red), the ones above touch nothing (green)](docs/big_rock.jpg)

![A silver vein in the Mountains, dug out of the snow: all 100 nodes are free](docs/silver_vein.jpg)

![A small rock in the Meadows: every piece touches the ground](docs/small_rock.jpg)

## Features

- The hitboxes appear on the first pickaxe hit on a deposit.
- **Red:** the piece's hitbox is colliding with something (ground, another rock, a building) or is buried below the terrain. **Green:** it touches nothing. Other pieces of the same deposit are ignored.
- The **L** key (configurable) shows / hides the hitboxes. The state is saved.
- The box drawn is exactly the one the game uses in `MineRock5.UpdateSupport`: the `bounds` of each piece's collider, aligned to the world axes. That's why it can look bigger than the piece.
- Aiming at a deposit shows, under its name, how many nodes (pieces) it has and how many are colliding / free.
- Only one deposit is shown at a time: hitting a different one removes the previous one's hitboxes.
- A deposit's hitboxes are removed when you walk away (`HideDistance`) or the deposit is destroyed.

## How it works

The code only uses Harmony patches and Unity components. It does not access the network or any files outside the game process, except for writing its own config file.

- `src/Patches.cs`: a prefix on `MineRock5.Damage`, which runs on the client of the player who hits, detects the local player's first hit (also in multiplayer).
- `src/HitboxManager.cs`: keeps the deposits that were hit and every 0.1 s runs `Physics.OverlapBox` for each piece with the same layers as the game. It draws only the edges, with meshes and the `Hidden/Internal-Colored` shader. With `HideHiddenEdges`, each box also draws invisible faces that only write depth (queue 3990). Red edges are drawn before them (3989), so only the rock and the terrain hide them; green edges are drawn after (3991), so the boxes hide them too. In `Support` mode it replicates the game's `UpdateSupport` / `GetSupport` logic.
- `src/Plugin.cs`: BepInEx entry point, configuration and toggle key.

## Configuration

`BepInEx/config/OreHitboxVisualizer.adrirf7.cfg`

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | State of the show / hide key (saved). |
| `ToggleKey` | `L` | Key to show / hide the hitboxes. |
| `PickaxeOnly` | `true` | Only triggers when hitting with a pickaxe. |
| `Mode` | `Collision` | `Collision`: red when it touches something outside its own deposit (ground, other rocks, buildings) or is buried below the terrain; pieces of the same deposit don't count. `Support`: red when the game considers the piece supported; green = it will break off when the next piece is mined. |
| `HideDistance` | `40` | Metres after which a deposit's hitboxes are removed. |
| `HideAfterSeconds` | `0` | Seconds since the last hit to remove them (0 = never). |
| `ShowNodeCount` | `true` | When you aim at a deposit, show under its name how many nodes (pieces) it has, how many are colliding and how many are free. |
| `CollidingColor` / `FreeColor` | red / green | Colours. |
| `LineThickness` | `0.03` | Edge thickness (metres). |
| `HideHiddenEdges` | `true` | Only draw the edges you can see: edges behind the rock or the terrain are hidden, and free (green) edges also hide behind other hitboxes. Colliding (red) edges are never hidden by other hitboxes. `false` = draw every edge through everything. |

## Building

Requirements:

- [.NET SDK 8](https://dotnet.microsoft.com/download) or newer
- Valheim with [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) installed (the project references the game's and BepInEx's assemblies from the install folder)

Steps:

```
git clone <this repository>
cd OreHitboxVisualizer
dotnet build -c Release -p:ValheimDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim" -p:DeployToGame=false
```

The compiled mod is `bin/Release/OreHitboxVisualizer.dll`. `ValheimDir` defaults to the Steam path above. Without `-p:DeployToGame=false`, the build also copies the DLL to `BepInEx/plugins/OreHitboxVisualizer/` in that Valheim install.

A Release build also creates the upload packages in `release/`:

- `OreHitboxVisualizer-<version>-nexus.zip`: `BepInEx/plugins/OreHitboxVisualizer/OreHitboxVisualizer.dll`, to extract into the Valheim folder.
- `OreHitboxVisualizer-<version>-thunderstore.zip`: `manifest.json`, `README.md`, `icon.png` and the DLL, from the `thunderstore/` folder.

The Nexus Mods page texts are in `nexus/`: `summary.txt` (short summary) and `description.bbcode` (long description).

The NuGet package `BepInEx.AssemblyPublicizer.MSBuild` is restored automatically. It lets the code access non-public members of `assembly_valheim.dll` at compile time, which is standard practice for Valheim mods.

## Installation

1. Install BepInExPack Valheim.
2. Put `OreHitboxVisualizer.dll` in `BepInEx/plugins/OreHitboxVisualizer/`.
3. Only the player who wants it needs the mod.
