# Vivat Slovakia Mods – web

Single-page web pro komunitní projekt [Vivat Slovakia Mods](https://github.com/Nereziel/Vivat-Slovakia-mods). React + TypeScript + Vite + Tailwind CSS 4 + lucide-react + Motion. Bez backendu a bez API klíčů.

## Lokální spuštění

Požadavky: Node.js 22+ a npm.

```bash
cd website
npm install
npm run dev
```

Připravení produkčního buildu:

```bash
npm run build
npm run preview
```

Vite má `base: '/Vivat-Slovakia-mods/'` pro projektové GitHub Pages. Výsledné soubory vznikají v `website/dist/`. V produkci používej adresu s prefixem `/Vivat-Slovakia-mods/` (nikoliv kořen domény).

## GitHub Pages

Workflow je v kořeni repozitáře: `.github/workflows/deploy-pages.yml`. Při změnách `website/**` na `main` (nebo ručně přes `workflow_dispatch`) sestaví pouze web a nasadí `website/dist/` pomocí oficiálních GitHub Pages Actions. **Nemění ani nesestavuje herní pluginy** a nedotýká se stávajících CMD release skriptů.

V repozitáři otevři **Settings → Pages → Build and deployment → Source: GitHub Actions**. Musí být povolené Actions a Pages. Potom commit změn ve `website/` spustí deployment. Očekávaná adresa: <https://nereziel.github.io/Vivat-Slovakia-mods/>.

## Odkud web bere data

- Funkce modů a instalační postup vychází z aktuálních zdrojových souborů, `README.md`, `INSTALL.md` a `VivatRoadMarkingsFix/README.md` v tomto repozitáři.
- Seznam aktuálních vydání načítá prohlížeč z veřejného GitHub API `GET /repos/Nereziel/Vivat-Slovakia-mods/releases?per_page=100`.
- Modifikace se přiřazují podle názvu souboru a případně podle názvu/tagu samostatného vydání. ZIP má při stejné verzi přednost před DLL. Legacy společný release `build-1` obsahuje samostatné DLL, ale neobsahuje automaticky všechny mody.
- `vsechny.zip` se nabízí pouze tehdy, pokud je skutečně přiložen k některému zveřejněnému releasu.
- Pokud GitHub API selže nebo dosáhne anonymního rate limitu (běžně 60 požadavků za hodinu na IP), web zobrazí odkaz na GitHub Releases. Žádné API klíče ani přihlášení nejsou potřeba.
- Metadata Open Graph používají statický obrázek `public/og-cover.png`.

## Struktura

```text
website/
├── index.html             # SEO / OG metadata, root SPA
├── vite.config.ts         # base pro project pages
├── public/                # favicon a OG náhled
├── src/
│   ├── App.tsx            # sekce webu, navigace, modal a FAQ
│   ├── styles.css         # responzivní herní design
│   ├── components/        # původní SVG ilustrace
│   ├── data/mods.ts       # ověřené popisy a funkce
│   └── lib/releases.ts    # GitHub API a párování vydání
└── package.json
```

### Poznámka k instalaci modů

Samostatné ZIPy připravuje existující `build-and-release.cmd`. ZIP obsahuje `BepInEx/plugins/<Mod>/<DLL>`, u `VivatPolice` také `VivatPoliceVoices/`. Ve starších vydáních lze narazit na samotná DLL, jejichž umístění vyžaduje ruční kontrolu. Viz [INSTALL.md](../INSTALL.md).

## Licence a původ

Projekt repozitáře: GPL-3.0 dle [LICENSE](../LICENSE). Neoficiální komunitní projekt bez vazby na vývojáře hry.
