# Vivat Road Markings Fix

Mod pro **Vivat Slovakia** (BepInEx 6 / Unity IL2CPP), který zlepšuje čitelnost existujících čar a dalšího značení na silnicích. Nahrazuje část původního `VivatPerformanceProbe`, která nastavovala filtrování textur vozovky.

## Co dělá

- Používá seznam konkrétních textur EasyRoads a hlavních silnic; ostatní textury nechává beze změny.
- U textur s mipmapami nastaví trilineární filtrování, anisotropii a záporný mip bias.
- Aplikuje opravu okamžitě při inicializaci a znovu při prvním snímku hry. Nově načtené textury pak kontroluje každé 2 s (nastavitelné). Žádný profiler, sběr frametimes ani klávesa F9.
- Při odinstalaci za běhu obnoví původní hodnoty sledovaných textur.

**Omezení:** Úprava zvýrazní již existující značení. Neopravuje chybějící geometrii, chybná UV, z-fighting ani nekvalitní zdrojové textury. Záporný mip bias může zvýšit třpytání nebo náročnost GPU.

## Sestavení a instalace

1. Nainstaluj BepInEx 6 pro Unity IL2CPP a spusť hru alespoň jednou kvůli vygenerování `BepInEx/interop`.
2. Sestav projekt příkazem `dotnet build VivatRoadMarkingsFix/VivatRoadMarkingsFix.csproj -c Release -p:BepInExDir="C:/cesta/ke/hre/BepInEx"`.
3. Výsledný `VivatRoadMarkingsFix.dll` vlož do `BepInEx/plugins/`.
4. Pokud máš starý `VivatPerformanceProbe.dll`, odstraň ho z `BepInEx/plugins/`, aby se neaplikovaly obě úpravy naráz.

## Konfigurace

Po prvním spuštění vznikne `BepInEx/config/local.vivat.roadmarkingsfix.cfg`:

```ini
[RoadMarkings]
Enabled = true
Anisotropy = 16
MipBias = -1.5
RefreshIntervalSeconds = 2
```

Změny konfigurace se projeví po restartu hry. Pro mírnější výsledek nastav `MipBias = -0.75` nebo `0`. Pro menší zátěž CPU lze `RefreshIntervalSeconds` zvýšit až na 10 s.

**Migrace:** Staré nastavení `ScanIntervalSeconds = 30` se už nepoužívá. Nová volba `RefreshIntervalSeconds = 2` se vytvoří automaticky také ve stávajícím konfiguračním souboru. Zápis původního klíče můžeš ručně smazat.

Projekt používá sdílené reference z kořenového `Directory.Build.props`.
