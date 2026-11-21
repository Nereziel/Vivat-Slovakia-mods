# Vivat Slovakia – mody

Samostatné mody pro **Vivat Slovakia** na Windows (BepInEx 6 / Unity IL2CPP). Každý mod lze nainstalovat nezávisle; policejní systém již **není součástí Vivat Combat**.

## Web projektu

**[Vivat Slovakia Mods – přehled modifikací a návod na instalaci](https://nereziel.github.io/Vivat-Slovakia-mods/)**

Na webu najdeš přehled dostupných pluginů, informace o jejich funkcích, odkazy ke stažení a postup instalace.

## Stažení jednotlivých modů

**[Stáhnout mody – GitHub Releases](https://github.com/Nereziel/Vivat-Slovakia-mods/releases)**

Na stránce Releases najdeš jednotlivé DLL soubory i společný archiv `vsechny.zip`. Dostupné modifikace:

| Mod | Funkce |
| --- | --- |
| **Vivat Police** | Vlastní dispečink policie, posily, navigace, vystupování a policejní pokřiky |
| **Vivat Combat** | Úpravy poškození, reakcí na zásahy, zpětného rázu, střelby, zvuku a bojové minimapy |
| **Vivat Vehicle Fixes** | Opravy vozidel a provozu |
| **Vivat Road Markings Fix** | Ostřejší silniční značení |
| **Vivat Speedometer** | Tachometr |
| **Vivat Trainer** | Herní trainer (F8) |
| **Vivat Population Performance** | Nastavení limitů populace |

> **Poznámka:** Ve zveřejněném balíčku je zatím šest modů bez Vivat Police. Policejní plugin dosud nemá zveřejněný instalační soubor. Jednotlivé DLL patří do `BepInEx/plugins/`, společný archiv `vsechny.zip` rozbal do kořenové složky hry.

## Instalace pro hráče

Podrobný návod krok za krokem: **[INSTALL.md – instalace BepInEx 6 a modů](INSTALL.md)**.

Ve zkratce: stáhni správný BepInEx 6 **Unity IL2CPP Windows x64**, rozbal do složky s `Vivat Slovakia.exe` a jednou spusť hru. Potom stáhni vybranou **DLL** a vlož ji do `BepInEx/plugins/`, nebo stáhni `vsechny.zip` a rozbal ho **do kořenové složky hry**.

**Při aktualizaci odstraň staré DLL.** Zvlášť při přechodu na Vivat Police smaž předchozí soubory `VivatCombat.*.dll`, aby se nenačítalo více verzí pluginu. Policejní logika je nyní oddělená od Vivat Combat.

## Vývoj a lokální vydávání

- [RELEASE.md](RELEASE.md) – postup pro jediné lokální CMD sestavení a zveřejnění samostatných Releases
- [build-and-install.cmd](build-and-install.cmd) – lokální sestavení všech projektů s knihovnami z nainstalované hry
- [build-and-release.cmd](build-and-release.cmd) – sestavení sedmi modů, vytvoření ZIPů a zveřejnění jednotlivých Releases z Windows
- [LOGGING.md](LOGGING.md) – nastavení logování

Každý projekt má vlastní `.csproj` a `BepInPlugin` verzi. Pro budoucí samostatná vydání lze použít `build-and-release.cmd` s aktualizovanou verzí pluginu; existující tagy se nepřepisují.
