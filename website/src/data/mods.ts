export type ModId =
  | 'VivatPolice'
  | 'VivatCombat'
  | 'VivatVehicleFixes'
  | 'VivatRoadMarkingsFix'
  | 'VivatSpeedometer'
  | 'VivatTrainer'
  | 'VivatPopulationPerformance';

export type ModCategory = 'police' | 'combat' | 'vehicles' | 'roads' | 'hud' | 'trainer' | 'performance';

export type ModDefinition = {
  id: ModId;
  name: string;
  category: ModCategory;
  categoryLabel: string;
  description: string;
  features: string[];
  detail: string;
  installationNote?: string;
  // Names verified in the repository and historical release assets.
  assetPrefixes: readonly string[];
  sourcePath: string;
};

export const REPO_URL = 'https://github.com/Nereziel/Vivat-Slovakia-mods';
export const RELEASES_URL = `${REPO_URL}/releases`;
export const ISSUES_URL = `${REPO_URL}/issues`;
export const INSTALL_URL = `${REPO_URL}/blob/main/INSTALL.md`;
export const BEPINEX_URL = 'https://builds.bepinex.dev/projects/bepinex_be';

export const mods: ModDefinition[] = [
  {
    id: 'VivatPolice',
    name: 'Vivat Police',
    category: 'police',
    categoryLabel: 'POLICIE / AI',
    description: 'Samostatný systém policejních posil, hlídek a hlasových výzev.',
    features: ['Koordinace policejních posil', 'Příjezdy hlídek a vystupování', 'Slovenské policejní pokřiky'],
    detail: 'Rozšiřuje vlastní logiku policejních výjezdů, dopravu posil, výzbroj hlídek a navigaci. Podporuje prostorové hlasové pokřiky z dodávaných WAV souborů. Policejní systém je oddělený od modu Vivat Combat.',
    installationNote: 'Při instalaci zachovej i BepInEx/plugins/VivatPoliceVoices/ s hlasovými soubory.',
    assetPrefixes: ['VivatPolice'],
    sourcePath: 'VivatPolice/PolicePlugin.cs',
  },
  {
    id: 'VivatCombat',
    name: 'Vivat Combat',
    category: 'combat',
    categoryLabel: 'SOUBOJE / STŘELBA',
    description: 'Citlivější bojové mechaniky, úpravy střelby a reakcí NPC.',
    features: ['Úpravy poškození NPC', 'Ladění zpětného rázu', 'Nepřátelé na minimapě'],
    detail: 'Upravuje poškození při zásazích, zpětný ráz kamery, reakce NPC na zásah a hlasitost jejich výstřelů. Volitelně přidává značky útočících nepřátel na minimapu. Policejní logika je nyní v samostatném pluginu.',
    installationNote: 'Před aktualizací odstraň DLL Vivat Combat verze 0.1.34 a starší.',
    assetPrefixes: ['VivatCombat'],
    sourcePath: 'VivatCombat/CombatPlugin.cs',
  },
  {
    id: 'VivatVehicleFixes',
    name: 'Vivat Vehicle Fixes',
    category: 'vehicles',
    categoryLabel: 'VOZIDLA / PROVOZ',
    description: 'Úpravy chování aut, kolizí a dynamiky městského provozu.',
    features: ['Filtrování chybných hlášení poškození', 'Rychlost běžné dopravy', 'Netrpěliví řidiči'],
    detail: 'Filtruje některá chybná hlášení poškození dílů po kolizi, upravuje rychlost běžné dopravy, chování netrpělivých řidičů a nastavení vykreslovací vzdálenosti detailů. Obsahuje také reakci na silnou kolizi s policejním autem.',
    assetPrefixes: ['VivatVehicleFixes'],
    sourcePath: 'VivatVehicleFixes/VehiclePlugin.cs',
  },
  {
    id: 'VivatRoadMarkingsFix',
    name: 'Vivat Road Markings Fix',
    category: 'roads',
    categoryLabel: 'GRAFIKA / SILNICE',
    description: 'Ostřejší a lépe čitelné existující čáry na vozovce.',
    features: ['Trilineární filtrování textur', 'Anizotropní filtrování', 'Nastavitelný mip bias'],
    detail: 'Zlepšuje filtrování vybraných silničních textur pomocí trilineárního filtrování, anizotropie a mip bias. Průběžně kontroluje nově načtené textury. Neopravuje chybějící geometrii nebo špatně vytvořené původní textury.',
    installationNote: 'Pokud máš starý VivatPerformanceProbe.dll, odeber jej.',
    assetPrefixes: ['VivatRoadMarkingsFix'],
    sourcePath: 'VivatRoadMarkingsFix/README.md',
  },
  {
    id: 'VivatSpeedometer',
    name: 'Vivat Speedometer',
    category: 'hud',
    categoryLabel: 'HUD / JÍZDA',
    description: 'Přehledný ukazatel rychlosti vozidla přímo ve hře.',
    features: ['Zobrazení km/h', 'Funguje s externí kamerou', 'Nastavitelné odsazení od okraje'],
    detail: 'Zobrazuje rychlost aktuálně řízeného vozidla v km/h ve spodní části obrazu. Ukazatel se zobrazuje během jízdy při použití externí kamery a lze upravit jeho odsazení od pravého okraje.',
    assetPrefixes: ['VivatSpeedometer'],
    sourcePath: 'VivatSpeedometer/SpeedometerPlugin.cs',
  },
  {
    id: 'VivatTrainer',
    name: 'Vivat Trainer',
    category: 'trainer',
    categoryLabel: 'NÁSTROJE / GAMEPLAY',
    description: 'Herní nabídka s praktickými nástroji dostupná klávesou F8.',
    features: ['Zbraně, munice a peníze', 'Oprava auta a uzdravení', 'Teleport a zrušení pátrání'],
    detail: 'Klávesou F8 otevřeš menu pro přidání peněz a zbraní, doplnění nábojů, opravu auta, uzdravení postavy, přivolání vlastního auta a teleport podle souřadnic nebo waypointu na mapě.',
    assetPrefixes: ['VivatTrainer'],
    sourcePath: 'VivatTrainer/TrainerPlugin.cs',
  },
  {
    id: 'VivatPopulationPerformance',
    name: 'Vivat Population Performance',
    category: 'performance',
    categoryLabel: 'VÝKON / POPULACE',
    description: 'Nastavitelné limity hustoty aut a chodců pro snížení zátěže.',
    features: ['Limity vozidel', 'Limity vzdálených vozidel', 'Limity chodců (25–100 %)'],
    detail: 'Upravuje původní maximální počty aut, vzdálených vozidel a chodců pomocí procentuálních limitů v konfiguraci. Výchozí hodnota je 75 % a pro změny je nutný restart hry.',
    assetPrefixes: ['VivatPopulationPerformance', 'VivatPopulationMod'],
    sourcePath: 'VivatPopulationPerformance/PopulationPlugin.cs',
  },
];
