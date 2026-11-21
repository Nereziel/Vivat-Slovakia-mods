# Vivat Road Markings Fix

Mod pro **Vivat Slovakia** (BepInEx 6 / Unity IL2CPP), který zlepšuje čitelnost existujících čar a dalšího značení na silnicích. Nahrazuje část původního `VivatPerformanceProbe`, která nastavovala filtrování textur vozovky.

## Co dělá

- Používá seznam konkrétních textur EasyRoads a hlavních silnic; ostatní textury nechává beze změny.
- U textur s mipmapami nastaví trilineární filtrování, anisotropii a záporný mip bias.
- Pravidelně kontroluje nově načtené textury (výchozí interval 30 s), bez profilování metod, sběru frametimes nebo klávesy F9.
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
ScanIntervalSeconds = 30
```

Změny konfigurace se projeví po restartu hry. Pro mírnější výsledek nastav `MipBias = -0.75` nebo `0`.

Projekt používá sdílené reference z kořenového `Directory.Build.props`.
