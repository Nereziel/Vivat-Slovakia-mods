# Vivat Slovakia – mody

Samostatné mody pro **Vivat Slovakia** na Windows (BepInEx 6 / Unity IL2CPP). Každý mod lze nainstalovat nezávisle; policejní systém již **není součástí Vivat Combat**.

## Stažení jednotlivých modů

Každý projekt má vlastní GitHub Release a vlastní ZIP. Verze se vydávají lokálně přes `build-and-release.cmd` (sestavení a nahrání přes GitHub CLI). Odkazy na konkrétní vydání:

| Mod | Verze | Stažení / release | Funkce |
| --- | --- | --- | --- |
| **Vivat Police** | 0.1.0 | [Vivat Police v0.1.0](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatPolice-v0.1.0) | Vlastní dispečink policie, posily, navigace, vystupování a policejní pokřiky |
| **Vivat Combat** | 0.1.35 | [Vivat Combat v0.1.35](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatCombat-v0.1.35) | Úpravy poškození, reakcí na zásahy, zpětného rázu, střelby, zvuku a bojové minimapy |
| **Vivat Vehicle Fixes** | 0.1.14 | [Vivat Vehicle Fixes v0.1.14](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatVehicleFixes-v0.1.14) | Opravy vozidel a provozu |
| **Vivat Road Markings Fix** | 1.0.0 | [Vivat Road Markings Fix v1.0.0](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatRoadMarkingsFix-v1.0.0) | Ostřejší silniční značení |
| **Vivat Speedometer** | 0.1.0 | [Vivat Speedometer v0.1.0](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatSpeedometer-v0.1.0) | Tachometr |
| **Vivat Trainer** | 0.1.10 | [Vivat Trainer v0.1.10](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatTrainer-v0.1.10) | Herní trainer (F8) |
| **Vivat Population Performance** | 0.1.0 | [Vivat Population Performance v0.1.0](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tags/VivatPopulationPerformance-v0.1.0) | Nastavení limitů populace |

[Zobrazit všechna vydání](https://github.com/Nereziel/Vivat-Slovakia-mods/releases)

> **Poznámka:** Odkazy začnou fungovat po prvním místním vydání. Release ZIP obsahuje pouze mody, ne herní DLL. Postup: [RELEASE.md](RELEASE.md).

## Instalace pro hráče

Podrobný návod krok za krokem: **[INSTALL.md – instalace BepInEx 6 a modů](INSTALL.md)**.

Ve zkratce: stáhni správný BepInEx 6 **Unity IL2CPP Windows x64**, rozbal do složky s `Vivat Slovakia.exe`, jednou spusť hru, poté stáhni požadované ZIPy z Releases a rozbal je **do kořenové složky hry** (nikoli do vnořeného `BepInEx/plugins/BepInEx/`).

**Při aktualizaci odstraň staré DLL.** Zvlášť při přechodu na Vivat Police smaž staré `VivatCombat.*.dll` verze 0.1.34 a starší. Vivat Combat 0.1.35 už vlastní policejní logiku neobsahuje.

## Vývoj a lokální vydávání

- [RELEASE.md](RELEASE.md) – postup pro jediné lokální CMD sestavení a zveřejnění samostatných Releases
- [build-and-install.cmd](build-and-install.cmd) – lokální sestavení všech projektů s knihovnami z nainstalované hry
- [build-and-release.cmd](build-and-release.cmd) – sestavení sedmi modů, vytvoření ZIPů a zveřejnění jednotlivých Releases z Windows
- [LOGGING.md](LOGGING.md) – nastavení logování

Každý projekt má vlastní `.csproj` a `BepInPlugin` verzi. Nové vydání vznikne po spuštění `build-and-release.cmd` s aktualizovanou verzí pluginu. Existující tag se nepřepisuje.
