# Logování modů Vivat Slovakia

Moduly kromě `VivatCombat` mají vlastní sekci `[Diagnostics]`:

```ini
[Diagnostics]
VerboseLogging = false
```

Ve výchozím nastavení jsou informační a ladicí výpisy vypnuté. Varování a chyby zůstávají zapnuté. Nastavení se používá samostatně v těchto konfiguračních souborech:

- `BepInEx/config/local.vivat.vehiclefixes.cfg`
- `BepInEx/config/local.vivat.populationperformance.cfg`
- `BepInEx/config/local.vivat.roadmarkingsfix.cfg`
- `BepInEx/config/local.vivat.speedometer.cfg`
- `BepInEx/config/local.vivat.trainer.cfg`

## Vivat Vehicle Fixes / Vivat Part Damage

```ini
[Diagnostics]
VerboseLogging = false
LogPartDamage = false

[Collision]
TracePlayerContacts = false
FilterSpuriousPartDamage = true
```

- `LogPartDamage = false` vypne hlášky `Preskoceno poskozeni dilu` a výpisy prasklých kloubů. Oprava poškození zůstává aktivní.
- `VerboseLogging = false` vypne ostatní informační hlášky modulu včetně záznamů dopravy.
- `TracePlayerContacts = false` vypne diagnostické záznamy kontaktů auta. Lze zapnout samostatně.
- `FilterSpuriousPartDamage = true` zachovává filtr nesmyslného poškození vozidla.

V existujících souborech `.cfg` se dříve uložené hodnoty automaticky nepřepíší. Pokud je `TracePlayerContacts = true`, změň hodnotu ručně na `false`.

Po změně konfigurace restartuj hru. Po změně zdrojového kódu znovu sestav a nahraď příslušné DLL. `VivatCombat` nebyl měněn.
