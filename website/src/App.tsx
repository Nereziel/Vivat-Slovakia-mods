import { useCallback, useEffect, useRef, useState, type MouseEvent, type ReactNode } from 'react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import {
  ArrowDown, ArrowDownToLine, ArrowRight, ArrowUpRight, BookOpen, CalendarDays,
  CarFront, Check, ChevronDown, CircleAlert, ClipboardList, Code2, Crosshair,
  ExternalLink, FileArchive, FileCode2, Gamepad2, Gauge, Github, Info, Menu,
  Route, Shield, ShieldCheck, UsersRound, X,
  type LucideIcon,
} from 'lucide-react';
import NightDrive from './components/NightDrive';
import {
  BEPINEX_URL, INSTALL_URL, ISSUES_URL, mods, RELEASES_URL, REPO_URL,
  type ModCategory, type ModDefinition, type ModId,
} from './data/mods';
import {
  fetchReleaseCatalog, formatDate, ReleasesUnavailableError,
  type ReleaseCatalog,
} from './lib/releases';

const icons: Record<ModCategory, LucideIcon> = {
  police: Shield, combat: Crosshair, vehicles: CarFront, roads: Route,
  hud: Gauge, trainer: Gamepad2, performance: UsersRound,
};

const navItems = [
  { id: 'home', title: 'Home' },
  { id: 'mods', title: 'Mody' },
  { id: 'downloads', title: 'Stažení' },
  { id: 'installation', title: 'Instalace' },
  { id: 'faq', title: 'FAQ' },
];

const steps = [
  { title: 'Najdi instalační složku', description: 'Steam → Knihovna → Vivat Slovakia → Vlastnosti → Nainstalované soubory → Procházet. Hru nejdřív zavři.' },
  { title: 'Stáhni správný BepInEx', description: 'Vyber BepInEx 6: BepInEx-Unity.IL2CPP-win-x64. Nepoužívej varianty Mono ani x86.', link: BEPINEX_URL, linkText: 'Oficiální sestavení BepInEx' },
  { title: 'Rozbal BepInEx k souboru hry', description: 'Obsah archivu patří přímo vedle Vivat Slovakia.exe. Ve složce hry má být BepInEx/ i soubory loaderu, například winhttp.dll.' },
  { title: 'Poprvé spusť hru', description: 'Počkej na hlavní menu a hru ukonči. BepInEx vytvoří plugins/, config/ a interop/.' },
  { title: 'Vyber si mod', description: 'V sekci Stažení stáhni ZIP konkrétního modu. Starší společný release může obsahovat také samostatné DLL.' },
  { title: 'Rozbal a zachovej strukturu', description: 'ZIP rozbal do kořenové složky hry a potvrď sloučení BepInEx/. DLL patří do BepInEx/plugins/; pro Police zachovej i složku hlasů.' },
  { title: 'Ověř načtení', description: 'Spusť hru a zkontroluj BepInEx/LogOutput.log. Hledej řádek Loading [název modu a verze].' },
];

const faqs = [
  {
    question: 'Jak nainstalovat BepInEx?',
    answer: 'Stáhni BepInEx 6 pro Unity IL2CPP Windows x64, rozbal jej přímo vedle Vivat Slovakia.exe a jednou spusť hru do hlavního menu. Pak hru zavři. Podrobný postup najdeš výše a v INSTALL.md.',
  },
  {
    question: 'Mohu používat všechny mody současně?',
    answer: 'Mody jsou samostatné a můžeš instalovat jen ty, které chceš. Vivat Police a Vivat Combat už nejsou jeden plugin a mohou fungovat vedle sebe. Kompatibilita se ale může měnit s verzí hry; v případě problémů zkontroluj log.',
  },
  {
    question: 'Jak mod aktualizovat?',
    answer: 'Zavři hru, odstraň staré DLL daného modu (i z podsložek BepInEx/plugins/) a rozbal nový ZIP do složky hry. Neponechávej dvě verze pluginu se stejným ID. Zvlášť smaž Vivat Combat 0.1.34 a starší před instalací novější verze.',
  },
  {
    question: 'Jak mod vypnout?',
    answer: 'Zavři hru a odeber odpovídající DLL nebo složku modu z BepInEx/plugins/. U Vivat Police můžeš odebrat také VivatPoliceVoices/. Některé funkce lze samostatně vypínat v konfiguraci pluginu.',
  },
  {
    question: 'Kde najdu konfigurační soubory?',
    answer: 'Po prvním spuštění se vytvářejí v BepInEx/config/. Například local.vivat.police.cfg, local.vivat.combat.cfg, local.vivat.vehiclefixes.cfg a local.vivat.roadmarkingsfix.cfg. Nastavení obvykle měň při vypnuté hře a pak ji restartuj.',
  },
  {
    question: 'Proč se mod nenačítá?',
    answer: 'Zkontroluj správnou variantu BepInEx (Unity IL2CPP x64), soubory v BepInEx/plugins/, složku BepInEx/interop/ a BepInEx/LogOutput.log. Odstraň duplicitní/staré DLL. Pokud log vůbec nevznikne, zkontroluj instalaci loaderu vedle Vivat Slovakia.exe.',
  },
  {
    question: 'Jak nahlásit chybu?',
    answer: 'Otevři GitHub Issues. Uveď název a verzi modu, popis chování, kroky k reprodukci a relevantní část BepInEx/LogOutput.log. Nevkládej herní soubory ani vygenerované IL2CPP knihovny.',
  },
];

function External({ href, children, className = '' }: { href: string; children: ReactNode; className?: string }) {
  return <a className={className} href={href} target="_blank" rel="noopener noreferrer">{children}</a>;
}

function Logo({ onClick }: { onClick?: () => void }) {
  return (
    <a href="#home" className="brand" aria-label="Vivat Slovakia Mods – začátek stránky" onClick={onClick}>
      <span className="brand-mark" aria-hidden="true"><span>V</span><i /></span>
      <span className="brand-type"><b>VIVAT <em>SLOVAKIA</em></b><small>MODS COMMUNITY</small></span>
    </a>
  );
}

function Reveal({ children, className = '', delay = 0 }: { children: ReactNode; className?: string; delay?: number }) {
  const reduced = useReducedMotion();
  return (
    <motion.div
      className={className}
      initial={reduced ? false : { opacity: 0, y: 22 }}
      whileInView={reduced ? undefined : { opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.13 }}
      transition={{ duration: 0.55, delay, ease: [0.2, 0.7, 0.2, 1] }}
    >{children}</motion.div>
  );
}

function SectionHeader({ id, index, eyebrow, title, description }: {
  id: string; index: string; eyebrow: string; title: string; description: string;
}) {
  return (
    <Reveal className="section-header">
      <div className="section-kicker"><span>{index}</span><i />{eyebrow}</div>
      <h2 id={id}>{title}</h2>
      <p>{description}</p>
    </Reveal>
  );
}

function Header() {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState('home');
  const reduced = useReducedMotion();

  useEffect(() => {
    const observer = new IntersectionObserver((entries) => {
      const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio);
      if (visible[0]) setActive(visible[0].target.id);
    }, { rootMargin: '-27% 0px -54% 0px', threshold: [0, 0.1, 0.5, 1] });
    for (const item of navItems) {
      const element = document.getElementById(item.id);
      if (element) observer.observe(element);
    }
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', closeOnEscape);
    return () => document.removeEventListener('keydown', closeOnEscape);
  }, []);

  return (
    <header className="site-header">
      <div className="container header-inner">
        <Logo onClick={() => setOpen(false)} />
        <nav className="desktop-nav" aria-label="Hlavní navigace">
          {navItems.map(({ id, title }) => (
            <a key={id} className={active === id ? 'active' : ''} aria-current={active === id ? 'location' : undefined} href={`#${id}`}>{title}</a>
          ))}
        </nav>
        <External className="header-github" href={REPO_URL}><Github size={17} /> <span>GitHub</span> <ArrowUpRight size={14} /></External>
        <button className="mobile-menu-toggle" type="button" aria-expanded={open} aria-controls="mobile-menu" aria-label={open ? 'Zavřít menu' : 'Otevřít menu'} onClick={() => setOpen(!open)}>
          {open ? <X size={25} /> : <Menu size={25} />}
        </button>
      </div>
      <AnimatePresence>
        {open && (
          <motion.nav id="mobile-menu" className="mobile-menu" aria-label="Mobilní navigace" initial={reduced ? false : { opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }} transition={{ duration: reduced ? 0 : 0.18 }}>
            {navItems.map(({ id, title }) => <a key={id} href={`#${id}`} aria-current={active === id ? 'location' : undefined} onClick={() => setOpen(false)}>{title}<ArrowRight size={16} /></a>)}
            <External className="mobile-menu-link" href={REPO_URL}><Github size={16} /> GitHub <ArrowUpRight size={14} /></External>
          </motion.nav>
        )}
      </AnimatePresence>
    </header>
  );
}

function Hero() {
  return (
    <section className="hero" id="home" aria-labelledby="hero-title">
      <div className="hero-grid" aria-hidden="true" />
      <div className="container hero-inner">
        <div className="hero-copy">
          <Reveal>
            <div className="hero-eyebrow"><span className="live-dot" />NEOFICIÁLNÍ KOMUNITNÍ PROJEKT <span className="slash">/</span> WINDOWS</div>
            <h1 id="hero-title">VIVAT<br /><span>SLOVAKIA</span><br />MODS<span className="title-stop">.</span></h1>
            <p className="hero-subtitle">Vylepši svůj zážitek z Vivat Slovakia.</p>
            <p className="hero-description">Komunitní modifikace, které rozšiřují herní možnosti, vylepšují souboje, vozidla, policejní systém a optimalizují výkon hry.</p>
            <div className="hero-actions">
              <a href="#mods" className="btn btn-red">Prozkoumat mody <ArrowRight size={18} /></a>
              <a href="#installation" className="btn btn-outline">Jak nainstalovat <ArrowDown size={18} /></a>
            </div>
            <External href={REPO_URL} className="hero-github"><Github size={17} /> Zobrazit zdrojový kód na GitHubu <ArrowUpRight size={15} /></External>
          </Reveal>
        </div>
        <div className="hero-art">
          <NightDrive />
          <div className="hero-art-overlay" aria-hidden="true" />
          <div className="hero-stamp"><span>MODDING PROJECT</span><strong>VS<span>///</span></strong><small>BUILT FOR THE COMMUNITY</small></div>
          <div className="hero-caption"><span className="red-line" /> <span>BRATISLAVA<br /><b>AFTER DARK</b></span></div>
        </div>
      </div>
      <div className="container hero-bottom">
        <span><ShieldCheck size={16} /> BepInEx 6 / Unity IL2CPP</span>
        <span><Code2 size={16} /> Open source / GPL-3.0</span>
        <span><span className="hero-small-number">07</span> Samostatných modů v repozitáři</span>
        <a href="#mods" aria-label="Přejít na mody">SCROLL <ArrowDown size={15} /></a>
      </div>
    </section>
  );
}

function CardDownload({ modId, catalog }: { modId: ModId; catalog: ReleaseCatalog | null }) {
  const item = catalog?.mods[modId];
  if (!item?.downloadUrl) return <a className="card-download" href="#downloads" aria-label="Přejít ke stažení"><ArrowDownToLine size={17} /></a>;
  return <External className="card-download" href={item.downloadUrl}><ArrowDownToLine size={17} /><span className="sr-only">Stáhnout soubor modu</span></External>;
}

function ModCard({ mod, index, catalog, onDetail }: {
  mod: ModDefinition; index: number; catalog: ReleaseCatalog | null; onDetail: (mod: ModDefinition) => void;
}) {
  const Icon = icons[mod.category];
  return (
    <Reveal delay={(index % 3) * 0.07} className="mod-card-wrap">
      <article className="mod-card">
        <div className="mod-card-top"><div className="mod-icon"><Icon size={27} strokeWidth={1.75} /></div><span className="mod-number">/ 0{index + 1}</span></div>
        <span className="mod-category">{mod.categoryLabel}</span>
        <h3>{mod.name}</h3>
        <p className="mod-description">{mod.description}</p>
        <div className="card-divider" />
        <ul className="mod-features">{mod.features.map((feature) => <li key={feature}><Check size={15} />{feature}</li>)}</ul>
        <div className="card-actions"><button type="button" className="card-detail" onClick={() => onDetail(mod)}>Více informací <ArrowRight size={17} /></button><CardDownload modId={mod.id} catalog={catalog} /></div>
      </article>
    </Reveal>
  );
}

function Mods({ catalog, onDetail }: { catalog: ReleaseCatalog | null; onDetail: (mod: ModDefinition) => void }) {
  return (
    <section className="section mods-section" id="mods" aria-labelledby="mods-title">
      <div className="container">
        <SectionHeader id="mods-title" index="01" eyebrow="KNIHOVNA MODIFIKACÍ" title="Uprav si pravidla hry." description="Sedm nezávislých pluginů, jeden svět. Vyber si funkce, které chceš ve Vivat Slovakia používat." />
        <div className="mod-grid">{mods.map((mod, index) => <ModCard key={mod.id} mod={mod} index={index} catalog={catalog} onDetail={onDetail} />)}</div>
        <Reveal className="mods-footnote"><Info size={18} /><span>Mody fungují přes BepInEx 6. Žádný instalační program, účet ani další služba není potřeba.</span><a href="#installation">Instalace <ArrowRight size={16} /></a></Reveal>
      </div>
    </section>
  );
}

function DownloadRow({ mod, entry }: { mod: ModDefinition; entry: ReleaseCatalog['mods'][ModId] }) {
  const Icon = icons[mod.category];
  return (
    <div className="download-row">
      <div className="download-name"><span className="download-icon"><Icon size={20} /></span><span><b>{mod.name}</b><small>{mod.categoryLabel}</small></span></div>
      <div className="download-version">{entry?.version ? <span className="version-badge">v{entry.version}</span> : <span className="muted-label">{entry ? 'Verze neuvedena' : 'Zatím bez releasu'}</span>}</div>
      <div className="download-date"><CalendarDays size={15} />{entry ? formatDate(entry.publishedAt) : '—'}</div>
      <div className="download-actions">
        {entry?.downloadUrl ? (
          <External className="download-file" href={entry.downloadUrl}><ArrowDownToLine size={16} /> Stáhnout {entry.format}</External>
        ) : <span className="download-missing">{entry ? 'Soubor není přiložen' : 'Nevydáno'}</span>}
        <External className="download-release" href={entry?.releaseUrl ?? RELEASES_URL} aria-label={`Zobrazit GitHub Release pro ${mod.name}`}><ArrowUpRight size={19} /></External>
      </div>
    </div>
  );
}

function Downloads({ catalog, status, error }: { catalog: ReleaseCatalog | null; status: 'loading' | 'ready' | 'error'; error: string }) {
  return (
    <section className="section downloads-section" id="downloads" aria-labelledby="downloads-title">
      <div className="container">
        <div className="downloads-heading">
          <SectionHeader id="downloads-title" index="02" eyebrow="AKTUÁLNÍ VYDÁNÍ" title="Stáhni. Rozbal. Hraj." description="Verze a soubory se načítají přímo z GitHub Releases. Každý plugin kontrolujeme samostatně podle názvu, tagu a přiložených souborů." />
          <External className="subtle-external" href={RELEASES_URL}>Všechna vydání na GitHubu <ArrowUpRight size={16} /></External>
        </div>
        {status === 'ready' && catalog?.all && (
          <Reveal className="all-download">
            <div className="all-download-icon"><FileArchive size={29} strokeWidth={1.6} /></div>
            <div className="all-download-copy"><span>SPOLEČNÝ ARCHIV</span><h3>Všechny mody v jednom ZIPu.</h3><p>Soubor <code>vsechny.zip</code> z GitHub Releases. Před instalací zkontroluj obsah balíčku a starší DLL.</p></div>
            <External href={catalog.all.downloadUrl} className="btn btn-white">Stáhnout všechny mody <ArrowDownToLine size={18} /></External>
          </Reveal>
        )}
        {status === 'error' && (
          <div className="release-message" role="alert"><CircleAlert size={24} /><div><b>Vydání se nepodařilo načíst</b><p>{error} Otevři seznam vydání přímo na GitHubu.</p></div><External href={RELEASES_URL} className="btn btn-outline">GitHub Releases <ArrowUpRight size={16} /></External></div>
        )}
        {status === 'loading' && <div className="release-message" role="status" aria-live="polite"><span className="loading-spinner" /><div><b>Načítám GitHub Releases</b><p>Kontroluji dostupné soubory jednotlivých pluginů.</p></div></div>}
        {status === 'ready' && (
          <Reveal className="downloads-panel">
            <div className="downloads-panel-head"><span>MODIFIKACE</span><span>VERZE</span><span>DATUM VYDÁNÍ</span><span>SOUBORY / RELEASE</span></div>
            {mods.map((mod) => <DownloadRow key={mod.id} mod={mod} entry={catalog?.mods[mod.id]} />)}
          </Reveal>
        )}
        <div className="download-note"><Info size={17} /><p>Starší společný release může obsahovat samotné DLL, zatímco nové samostatné releasy obsahují instalační ZIP se strukturou BepInEx. Tlačítka uvádějí skutečný typ dostupného souboru.</p></div>
      </div>
    </section>
  );
}

function Installation() {
  return (
    <section className="section install-section" id="installation" aria-labelledby="installation-title">
      <div className="container">
        <SectionHeader id="installation-title" index="03" eyebrow="PRŮVODCE INSTALACÍ" title="Sedm kroků a hotovo." description="Nepotřebuješ žádný mod manager. Stačí správná verze BepInEx, stažený ZIP a pár minut ve složce hry." />
        <div className="install-layout">
          <div className="steps-grid">
            {steps.map((step, index) => (
              <Reveal key={step.title} className="step-card" delay={(index % 2) * 0.05}>
                <span className="step-number">{String(index + 1).padStart(2, '0')}</span>
                <h3>{step.title}</h3><p>{step.description}</p>
                {step.link && <External href={step.link} className="step-link">{step.linkText} <ArrowUpRight size={16} /></External>}
              </Reveal>
            ))}
          </div>
          <aside className="install-aside">
            <Reveal className="folder-panel">
              <div className="panel-title"><span><FileCode2 size={18} /> STRUKTURA SLOŽEK</span><span className="status-dots"><i /><i /><i /></span></div>
              <pre aria-label="Příklad správné struktury složky hry"><code>{`Vivat Slovakia/
├── Vivat Slovakia.exe
├── winhttp.dll
└── BepInEx/
    ├── plugins/
    │   ├── VivatCombat/
    │   │   └── VivatCombat.dll
    │   ├── VivatPolice/
    │   │   └── VivatPolice.dll
    │   └── VivatPoliceVoices/
    │       ├── charon/
    │       └── fenrir/
    ├── config/
    ├── interop/
    └── LogOutput.log`}</code></pre>
              <div className="folder-tip"><CircleAlert size={17} /><span>ZIP rozbal do složky hry, ne do <code>BepInEx/plugins/BepInEx/</code>.</span></div>
            </Reveal>
            <Reveal className="install-docs"><BookOpen size={26} /><h3>Potřebuješ podrobnější návod?</h3><p>Originální INSTALL.md obsahuje postup řešení problémů, aktualizací i poznámky k policejním hlasům.</p><External href={INSTALL_URL}>Přečíst INSTALL.md <ArrowUpRight size={17} /></External></Reveal>
          </aside>
        </div>
      </div>
    </section>
  );
}

function Faq() {
  const [opened, setOpened] = useState<number | null>(0);
  const reduced = useReducedMotion();
  return (
    <section className="section faq-section" id="faq" aria-labelledby="faq-title">
      <div className="container faq-layout">
        <div className="faq-intro">
          <SectionHeader id="faq-title" index="04" eyebrow="POTŘEBUJEŠ POMOC?" title="Časté otázky." description="Základní odpovědi k instalaci, aktualizacím a řešení problémů." />
          <Reveal className="faq-help"><div className="help-icon"><ClipboardList size={23} /></div><h3>Něco nefunguje?</h3><p>Popiš problém na GitHubu a přilož relevantní část logu BepInEx.</p><External href={ISSUES_URL}>Nahlásit problém <ArrowUpRight size={17} /></External></Reveal>
        </div>
        <Reveal className="faq-list">
          {faqs.map((item, index) => <div key={item.question} className={`faq-item ${opened === index ? 'open' : ''}`}>
            <h3><button type="button" aria-expanded={opened === index} aria-controls={`faq-panel-${index}`} id={`faq-question-${index}`} onClick={() => setOpened(opened === index ? null : index)}><span><b>{String(index + 1).padStart(2, '0')}</b>{item.question}</span><ChevronDown size={21} /></button></h3>
            <AnimatePresence initial={false}>
              {opened === index && <motion.div id={`faq-panel-${index}`} role="region" aria-labelledby={`faq-question-${index}`} initial={reduced ? false : { height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }} exit={{ height: 0, opacity: 0 }} transition={{ duration: reduced ? 0 : 0.22, ease: 'easeOut' }} className="faq-panel"><p>{item.answer}</p></motion.div>}
            </AnimatePresence>
          </div>)}
        </Reveal>
      </div>
    </section>
  );
}

function DetailModal({ mod, catalog, onClose }: {
  mod: ModDefinition | null; catalog: ReleaseCatalog | null; onClose: () => void;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const reduced = useReducedMotion();

  useEffect(() => {
    if (!mod) return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    dialogRef.current?.focus();

    const handleKeyboard = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      if (event.key !== 'Tab' || !dialogRef.current) return;
      const focusable = [...dialogRef.current.querySelectorAll<HTMLElement>('button:not([disabled]),a[href], [tabindex]:not([tabindex="-1"])')];
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKeyboard);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', handleKeyboard);
      previousFocus?.focus();
    };
  }, [mod, onClose]);

  const closeBackdrop = (event: MouseEvent<HTMLDivElement>) => {
    if (event.currentTarget === event.target) onClose();
  };

  const Icon = mod ? icons[mod.category] : Shield;
  const release = mod ? catalog?.mods[mod.id] : null;
  return (
    <AnimatePresence>
      {mod && (
        <motion.div className="modal-backdrop" onMouseDown={closeBackdrop} initial={reduced ? false : { opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} transition={{ duration: reduced ? 0 : 0.2 }}>
          <motion.div key={mod.id} ref={dialogRef} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="detail-modal-title" aria-describedby="detail-modal-desc" className="detail-modal" initial={reduced ? false : { opacity: 0, scale: 0.96, y: 16 }} animate={{ opacity: 1, scale: 1, y: 0 }} exit={{ opacity: 0, scale: 0.98, y: 8 }} transition={{ duration: reduced ? 0 : 0.22 }}>
            <div className="modal-top"><span className="modal-eyebrow">DETAIL MODIFIKACE</span><button type="button" onClick={onClose} className="modal-close" aria-label="Zavřít detail"><X size={21} /></button></div>
            <div className="modal-icon"><Icon size={30} /></div>
            <div className="modal-category">{mod.categoryLabel}</div>
            <h2 id="detail-modal-title">{mod.name}</h2>
            <p id="detail-modal-desc" className="modal-description">{mod.detail}</p>
            <div className="modal-divider" />
            <h3>Hlavní funkce</h3>
            <ul className="modal-features">{mod.features.map((feature) => <li key={feature}><Check size={16} />{feature}</li>)}</ul>
            {mod.installationNote && <div className="modal-note"><Info size={18} /><p>{mod.installationNote}</p></div>}
            <div className="modal-actions">
              {release?.downloadUrl ? <External href={release.downloadUrl} className="btn btn-red">Stáhnout {release.format} <ArrowDownToLine size={17} /></External> : <a href="#downloads" className="btn btn-red" onClick={onClose}>Přejít ke stažení <ArrowRight size={17} /></a>}
              <External href={`${REPO_URL}/blob/main/${mod.sourcePath}`} className="btn btn-outline">Zdrojový kód <ExternalLink size={16} /></External>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function Footer() {
  return (
    <footer className="site-footer">
      <div className="container">
        <div className="footer-main">
          <div><Logo /><p>Neoficiální komunitní modifikace pro Vivat Slovakia.<br />Projekt není spojen s vývojáři ani vydavatelem hry.</p></div>
          <div className="footer-links"><span>NAVIGACE</span><a href="#mods">Modifikace</a><a href="#downloads">Stažení</a><a href="#installation">Instalace</a><a href="#faq">FAQ</a></div>
          <div className="footer-links"><span>PROJEKT</span><External href={REPO_URL}>GitHub <ArrowUpRight size={14} /></External><External href={ISSUES_URL}>GitHub Issues <ArrowUpRight size={14} /></External><External href={`${REPO_URL}/blob/main/LICENSE`}>Licence GPL-3.0 <ArrowUpRight size={14} /></External><External href={INSTALL_URL}>Instalační návod <ArrowUpRight size={14} /></External></div>
        </div>
        <div className="footer-bottom"><span>© {new Date().getFullYear()} Vivat Slovakia Mods · Created by <External href="https://github.com/Nereziel">Nereziel</External></span><span>MADE FOR THE COMMUNITY <span className="footer-red">///</span></span></div>
      </div>
    </footer>
  );
}

export default function App() {
  const [selected, setSelected] = useState<ModDefinition | null>(null);
  const [catalog, setCatalog] = useState<ReleaseCatalog | null>(null);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState('');
  const closeDetail = useCallback(() => setSelected(null), []);

  useEffect(() => {
    const controller = new AbortController();
    fetchReleaseCatalog(controller.signal)
      .then((data) => { setCatalog(data); setStatus('ready'); })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return;
        setError(reason instanceof ReleasesUnavailableError ? reason.message : 'Došlo k chybě při komunikaci s GitHubem.');
        setStatus('error');
      });
    return () => controller.abort();
  }, []);

  return (
    <>
      <a className="skip-link" href="#main-content">Přeskočit na obsah</a>
      <Header />
      <main id="main-content">
        <Hero />
        <Mods catalog={catalog} onDetail={setSelected} />
        <Downloads catalog={catalog} status={status} error={error} />
        <Installation />
        <Faq />
      </main>
      <Footer />
      <DetailModal mod={selected} catalog={catalog} onClose={closeDetail} />
    </>
  );
}
