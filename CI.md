# GitHub Actions: vlastní vydání pro každý mod

Workflow: [`.github/workflows/mod-releases.yml`](.github/workflows/mod-releases.yml)

Automatické vydávání má **sedm nezávislých release tagů** (`VivatPolice-v0.1.0`, `VivatCombat-v0.1.35`, atd.). Při pushi do `main` nebo ručním `workflow_dispatch` se sestaví všechny projekty. GitHub Actions nahraje ZIPy jako build artifacts a zveřejní **samostatný GitHub Release pro každou verzi, která dosud nemá release**.

## Proč je potřeba jednorázové nastavení

Zdrojové projekty odkazují na `BepInEx/core/*.dll` a `BepInEx/interop/*.dll`, které vznikají v instalaci hry. Na čistém GitHub runneru nejsou. Závislosti zahrnují herní IL2CPP reference a **nesmějí se bez svolení zveřejňovat ve veřejném repozitáři či veřejném release**.

### 1. Vytvoř soukromý archiv referencí

Po instalaci BepInEx a prvním spuštění Vivat Slovakia spusť na svém Windows počítači v PowerShellu:

```powershell
.\scripts\create-reference-bundle.ps1 -GameDir "D:\Hry\Steam\steamapps\common\Vivat Slovakia"
```

Vznikne soubor `BepInEx-build-refs.zip`, který obsahuje pouze potřebné adresáře `BepInEx/core` a `BepInEx/interop` (nikoli celou hru). Archiv je určen **výhradně pro soukromé uložení**.

### 2. Ulož ho do privátního GitHub repozitáře

1. Vytvoř nový **private** GitHub repozitář, např. `Nereziel/vivat-build-references`.
2. V něm vytvoř Release s tagem přesně `build-refs`.
3. Nahraj do jeho Assets soubor s přesným názvem `BepInEx-build-refs.zip`.
4. Vytvoř **fine-grained personal access token** s oprávněním **Contents: Read-only** pouze pro tento private repozitář.

### 3. Nastav GitHub Actions v tomto veřejném repozitáři

V `Settings → Secrets and variables → Actions`:

- **Repository variable:** `MOD_BUILD_REFS_REPO` = `Nereziel/vivat-build-references` (případně jiný owner/repo).
- **Repository secret:** `MOD_BUILD_REFS_TOKEN` = PAT s přístupem ke čtení privátního archivu.

Do veřejného repozitáře **nepřidávej ZIP, herní DLL ani PAT**.

### 4. První sestavení a vydání

Otevři `Actions → Build and release mods → Run workflow` a vyber `main`. Po úspěšném sestavení vznikne pro každý mod vlastní release:

```text
VivatPolice-v0.1.0
VivatCombat-v0.1.35
VivatVehicleFixes-v0.1.14
VivatRoadMarkingsFix-v1.0.0
VivatSpeedometer-v0.1.0
VivatTrainer-v0.1.10
VivatPopulationPerformance-v0.1.0
```

Každé vydání obsahuje samostatný instalační ZIP se strukturou `BepInEx/plugins/...`. Vivat Police ZIP obsahuje i WAV zvuky policie. Při dalším pushi do `main` se vše sestaví znovu, ale **existující release tagy se nepřepisují**. Pro nové vydání tedy zvyš číslo verze u `[BepInPlugin(...)]` daného modu a změny pushni do `main`.

Pokud nejsou přístupné potřebné reference, workflow skončí **chybou bez publikování nových release**. To je bezpečnější než označit neověřenou nebo nekompletní DLL za hotové vydání.
