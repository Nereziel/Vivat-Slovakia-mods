# Lokální sestavení a vydání modů (bez Dockeru a GitHub Actions)

Stačí jedno spuštění **`build-and-release.cmd`** v lokálním klonu repozitáře na Windows. Skript sestaví všechny moduly, vytvoří **samostatný ZIP pro každý mod** a nahraje je do GitHub Releases.

## Jednorázová příprava

1. Nainstaluj **.NET SDK** podporující sestavení projektů `net6.0` a **Git for Windows**.
2. Nainstaluj [GitHub CLI](https://cli.github.com/) (`gh`) a v příkazovém řádku se jednou přihlas příkazem `gh auth login`. Přihlášený účet potřebuje oprávnění vytvářet vydání v `Nereziel/Vivat-Slovakia-mods`.
3. Nainstaluj BepInEx 6 IL2CPP do Vivat Slovakia podle [INSTALL.md](INSTALL.md), spusť hru alespoň jednou a ověř `BepInEx/core/BepInEx.Core.dll` a `BepInEx/interop/Assembly-CSharp.dll`.
4. Používej lokální git klon na větvi `main`. Změny nejdřív commitni a pushni, protože release odkazuje na stejný commit jako sestavené DLL.

**Není potřeba Docker, runner, GitHub Actions, soukromý repozitář ani přenášet herní DLL na GitHub.** Reference zůstávají v místní instalaci hry.

## Vydání jedním CMD

Při hře na výchozí cestě ze stávajícího `build-and-install.cmd` stačí dvojklik nebo:

```bat
build-and-release.cmd
```

Je-li hra jinde:

```bat
build-and-release.cmd "D:\SteamLibrary\steamapps\common\Vivat Slovakia"
```

Skript ověří `git`, `gh`, `dotnet`, shodu místního `main` s GitHub `main` a potřebné IL2CPP reference. Sestaví **všech sedm projektů**, vytvoří oddělené ZIPy do `dist/` a teprve po úspěchu všech buildů vytvoří samostatné GitHub Releases (např. `VivatPolice-v0.1.0`, `VivatCombat-v0.1.35`). Každý ZIP obsahuje instalovatelnou strukturu `BepInEx/plugins/...`; Vivat Police navíc obsahuje WAV policejních hlasů.

Existující vydání **nepřepisuje**. Pokud některý build selže, nic se nenahraje. Pokud selže samotný upload, lze CMD spustit znovu: hotové Releases přeskočí a dokončí ostatní.

## Jak vydat další verzi

1. Uprav kód a zvyš verzi `[BepInPlugin(...)]` daného modu.
2. Aktualizuj odkaz na verzi v `README.md`, vše commitni a pushni do `main`.
3. Spusť `build-and-release.cmd`. Pro nové verze vzniknou nové Releases; stávající tagy zůstanou beze změny.

Do veřejných Releases se nikdy nepřikládá `BepInEx/core`, `BepInEx/interop` ani herní soubory. Přidávají se pouze výsledné DLL jednotlivých modů a vlastní zvuky.
