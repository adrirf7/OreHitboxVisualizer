# OreHitboxVisualizer

Mod de BepInEx para Valheim que muestra la hitbox de soporte de cada trozo de los depósitos minables (cobre, plata, piedras grandes y cualquier roca `MineRock5`).

- La hitbox aparece en cuanto golpeas el depósito por primera vez con el pico.
- **Rojo**: la caja del trozo está colisionando con algo (terreno, otra roca, construcciones). **Verde**: no toca nada. Los demás trozos del mismo depósito no cuentan.
- La tecla **L** (configurable) muestra u oculta las hitboxes.

La caja que se dibuja es exactamente la que usa el juego (`MineRock5.UpdateSupport`): los `bounds` del collider de cada trozo, alineados a los ejes del mundo. Por eso a veces se ve más grande que el trozo.

## Configuración

`BepInEx/config/OreHitboxVisualizer.adrirf7.cfg`

| Opción | Por defecto | Descripción |
|---|---|---|
| `Enabled` | `true` | Estado de la tecla de mostrar/ocultar (se guarda). |
| `ToggleKey` | `L` | Tecla para mostrar/ocultar. |
| `PickaxeOnly` | `true` | Solo se activa al golpear con un pico. |
| `Mode` | `Collision` | `Collision`: rojo si toca algo fuera de su propio depósito (terreno, otras rocas, construcciones); los trozos del mismo depósito no cuentan. `Support`: rojo si el juego considera el trozo sostenido; verde = se caerá al romper el siguiente trozo. |
| `HideDistance` | `40` | Metros a partir de los cuales se quitan las hitboxes de ese depósito. |
| `HideAfterSeconds` | `0` | Segundos desde el último golpe para quitarlas (0 = nunca). |
| `CollidingColor` / `FreeColor` | rojo / verde | Colores. |
| `LineThickness` | `0.03` | Grosor de las aristas (metros). |
| `ShowFill` / `FillOpacity` | `true` / `0.12` | Caras semitransparentes. |
| `ShowThroughWalls` | `true` | Ver las cajas a través de la roca y el terreno. |

## Compilar

```
dotnet build -c Release
```

Copia el `.dll` a `BepInEx/plugins/OreHitboxVisualizer`. Otra ruta de Valheim: `-p:ValheimDir="D:\...\Valheim"`.
