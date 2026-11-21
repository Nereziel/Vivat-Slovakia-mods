# Vivat Slovakia – mody

Samostatné mody pro **Vivat Slovakia** na Windows (BepInEx 6 / Unity IL2CPP). Každý mod lze nainstalovat nezávisle; policejní systém již **není součástí Vivat Combat**.

## Stažení jednotlivých modů

Aktuálně zveřejněné vydání [Vivat Slovakia – Mods Pack (`build-1`)](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/tag/build-1) obsahuje šest samostatných DLL souborů a společný ZIP archiv. Odkazy níže vedou přímo na existující soubory ke stažení.

| Mod | Verze | Stažení | Funkce |
| --- | --- | --- | --- |
| **Vivat Police** | 0.1.0 | Zatím nevydáno | Vlastní dispečink policie, posily, navigace, vystupování a policejní pokřiky |
| **Vivat Combat** | 0.1.35 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatCombat.0.1.35.dll) | Úpravy poškození, reakcí na zásahy, zpětného rázu, střelby, zvuku a bojové minimapy |
| **Vivat Vehicle Fixes** | 0.1.14 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatVehicleFixes.0.1.14.dll) | Opravy vozidel a provozu |
| **Vivat Road Markings Fix** | 1.0.0 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatRoadMarkingsFix.1.0.0.dll) | Ostřejší silniční značení |
| **Vivat Speedometer** | 0.1.0 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatSpeedometer.0.1.0.dll) | Tachometr |
| **Vivat Trainer** | 0.1.10 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatTrainer.0.1.10.dll) | Herní trainer (F8) |
| **Vivat Population Performance** | 0.1.0 | [Stáhnout DLL](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/VivatPopulationMod.0.1.0.dll) | Nastavení limitů populace |

**[Stáhnout všechny dostupné mody – `vsechny.zip`](https://github.com/Nereziel/Vivat-Slovakia-mods/releases/download/build-1/vsechny.zip)**

[Zobrazit všechna vydání](https://github.com/Nereziel/Vivat-Slovakia-mods/releases)

> **Poznámka:** Balíček `build-1` obsahuje šest modů bez Vivat Police. Policejní plugin zatím nemá zveřejněný instalační balíček. Samostatné DLL soubory patří do `BepInEx/plugins/`; archiv `vsechny.zip` rozbal do kořenové složky hry. Připravovaný postup pro budoucí samostatná vydání je popsán v [RELEASE.md](RELEASE.md).

## Instalace pro hráče

Podrobný návod krok za krokem: **[INSTALL.md – instalace BepInEx 6 a modů](INSTALL.md)**.

Ve zkratce: stáhni správný BepInEx 6 **Unity IL2CPP Windows x64**, rozbal do složky s `Vivat Slovakia.exe` a jednou spusť hru. Potom stáhni vybranou **DLL** a vlož ji do `BepInEx/plugins/`, nebo stáhni `vsechny.zip` a rozbal ho **do kořenové složky hry**.

**Při aktualizaci odstraň staré DLL.** Zvlášť při přechodu na Vivat Police smaž staré `VivatCombat.*.dll` verze 0.1.34 a starší. Vivat Combat 0.1.35 už vlastní policejní logiku neobsahuje.

## Vývoj a lokální vydávání

- [RELEASE.md](RELEASE.md) – postup pro jediné lokální CMD sestavení a zveřejnění samostatných Releases
- [build-and-install.cmd](build-and-install.cmd) – lokální sestavení všech projektů s knihovnami z nainstalované hry
- [build-and-release.cmd](build-and-release.cmd) – sestavení sedmi modů, vytvoření ZIPů a zveřejnění jednotlivých Releases z Windows
- [LOGGING.md](LOGGING.md) – nastavení logování

Každý projekt má vlastní `.csproj` a `BepInPlugin` verzi. Pro budoucí samostatná vydání lze použít `build-and-release.cmd` s aktualizovanou verzí pluginu; existující tagy se nepřepisují.
