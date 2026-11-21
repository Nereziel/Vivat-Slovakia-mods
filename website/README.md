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
- Modifikace se přiřazují podle názvu skutečného souboru a případně podle názvu/tagu vydání. Pokud je pro stejnou verzi k dispozici DLL i ZIP, přednost má DLL. V současném společném release `build-1` jsou jednotlivá DLL (ne všechny mody), nikoli samostatné instalační ZIPy.
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

V aktuálním vydání jsou samostatná `.dll`; ulož je přímo do `Vivat Slovakia/BepInEx/plugins/`. Stejné místo použij pro jednotlivé DLL ze společného `vsechny.zip`. Soubory se nemusí ukládat do zvláštních složek pro každý mod. Vydávání nových balíčků přes `build-and-release.cmd` zůstává beze změny. Případné přibalené policejní hlasové soubory mají samostatnou cestu uvedenou v [INSTALL.md](../INSTALL.md).

## Licence a původ

Projekt repozitáře: GPL-3.0 dle [LICENSE](../LICENSE). Neoficiální komunitní projekt bez vazby na vývojáře hry.
