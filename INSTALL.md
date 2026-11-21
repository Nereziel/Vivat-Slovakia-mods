# Instalace modů do Vivat Slovakia

## 1. Najdi složku hry

Ve Steamu otevři **Knihovna → Vivat Slovakia → Vlastnosti → Nainstalované soubory → Procházet**. Otevře se složka obsahující `Vivat Slovakia.exe`. Před instalací hru zavři.

Doporučení: zálohuj složku `BepInEx/config` a uložené pozice.

## 2. Nainstaluj BepInEx 6 pro Unity IL2CPP

1. Otevři oficiální [BepInEx Bleeding Edge builds](https://builds.bepinex.dev/projects/bepinex_be).
2. Stáhni **BepInEx-Unity.IL2CPP-win-x64** (BepInEx **6**). **Nepoužívej** variantu Unity.Mono ani win-x86. Vivat Slovakia je 64bitová IL2CPP hra.
3. Rozbal ZIP **přímo do složky hry** vedle `Vivat Slovakia.exe`. Po rozbalení zde musí být `BepInEx/` a soubory BepInEx loaderu (například `winhttp.dll`), nikoli další vnořená složka.
4. Spusť hru, počkej na načtení hlavního menu a hru ukonči. BepInEx při prvním spuštění vytvoří `BepInEx/config`, `BepInEx/plugins` a vygeneruje `BepInEx/interop`.
5. Ve `BepInEx/LogOutput.log` ověř, že se BepInEx načetl. Když zde není log, ověř umístění souborů a správnou verzi x64 IL2CPP.

Oficiální dokumentace: [Installing BepInEx on Il2Cpp Unity](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).

## 3. Stáhni a nainstaluj konkrétní mod

1. Otevři [GitHub Releases](https://github.com/Nereziel/Vivat-Slovakia-mods/releases) a v části **Assets** stáhni `.dll` požadovaného modu. Dostupné soubory se mohou lišit podle vydání; nynější společný release obsahuje jednotlivá DLL a `vsechny.zip`.
2. Staženou `.dll` vlož **přímo do `Vivat Slovakia/BepInEx/plugins/`**. Nepotřebuje vlastní podsložku. Soubor nepřejmenovávej; například `VivatCombat.0.1.35.dll` je platný název.
3. Pokud používáš `vsechny.zip`, rozbal z něj DLL soubory do stejné složky `BepInEx/plugins/`. Předem odstraň staré verze modů.
4. Spusť hru a ověř načtení v `BepInEx/LogOutput.log` (`Loading [název modu]`).

Ukázka správné struktury:

```text
Vivat Slovakia/
├── Vivat Slovakia.exe
├── winhttp.dll
└── BepInEx/
    ├── plugins/
    │   ├── VivatCombat.0.1.35.dll
    │   ├── VivatVehicleFixes.0.1.14.dll
    │   ├── VivatSpeedometer.0.1.0.dll
    │   └── ...
    ├── config/
    ├── interop/
    └── LogOutput.log
```

Každý mod lze instalovat samostatně. **Vivat Police** a **Vivat Combat** jsou oddělené pluginy; nemusíš instalovat oba.

### Policejní pokřiky (pokud jsou součástí vydání)

Pokud stahuješ vydání Vivat Police s hlasovými soubory, hlasy nejsou DLL a potřebují zachovat očekávanou cestu `BepInEx/plugins/VivatPoliceVoices/charon/` a `BepInEx/plugins/VivatPoliceVoices/fenrir/`. Samotné `VivatPolice.dll` může být přímo v `BepInEx/plugins/`.

Pokud hlasové soubory ve vydání nejsou, nestahuj je z neověřených zdrojů. Aktuální společný release `build-1` neobsahuje samostatný soubor Vivat Police.

## 4. Aktualizace a odstranění starých DLL

**Před aktualizací zavři hru.** BepInEx může načíst soubory ze všech podsložek `plugins`, takže zkontroluj také staré DLL v kořeni `BepInEx/plugins`.

Zvlášť po oddělení policie od boje:

- Smaž staré soubory `VivatCombat.0.1.34.dll`, `VivatCombat.0.1.33.dll` a případné další staré verze pluginu `local.vivat.combat`.
- Nainstaluj DLL Vivat Combat **0.1.35+** a případně samostatné DLL Vivat Police (je-li zveřejněné); názvy distribuovaných souborů mohou obsahovat číslo verze.
- U ostatních modů také ponech pouze jednu aktuální DLL se stejným ID pluginu.

Pokud máš policejní hlasy nainstalované samostatně, při aktualizaci je nahraď pouze tehdy, když nové vydání skutečně obsahuje hlasové soubory.

## 5. Konfigurace

Po prvním spuštění vznikají konfigurace v `BepInEx/config/`:

| Mod | Konfigurační soubor |
| --- | --- |
| Vivat Police | `local.vivat.police.cfg` |
| Vivat Combat | `local.vivat.combat.cfg` |
| Vivat Vehicle Fixes | `local.vivat.vehiclefixes.cfg` |
| Vivat Road Markings Fix | `local.vivat.roadmarkingsfix.cfg` |

Policejní nastavení se po oddělení přesunula ze starého Combat configu do `local.vivat.police.cfg`. Počáteční doporučení: `CustomLogicEnabled = true`, `DebugInitialCars = 0`. Chceš-li zachovat staré vlastní hodnoty, přepiš příslušné volby ručně do nové konfigurace.

## 6. Když něco nefunguje

- **Mod se nenačítá:** ověř `BepInEx/LogOutput.log`, správné složky, typ IL2CPP x64 a absenci duplicitních DLL.
- **Chybí `BepInEx/interop`:** spusť hru jednou s BepInEx; interop knihovny se generují z konkrétní instalované verze hry.
- **Policie hlásí NavMesh chyby:** pošli souvislý úsek logu s řádky `NavMesh:` a `Policejní posily:` včetně verze pluginu; samotné varování Unity neříká, které NPC je chybné.
- **Chceš mod odebrat:** zavři hru a smaž jeho DLL z `BepInEx/plugins/` (včetně případných starých kopií v podsložkách). U policie případně také `VivatPoliceVoices/`.

Nepřidávej do veřejných issue ani release archivů soubory původní hry či vygenerované IL2CPP knihovny.
