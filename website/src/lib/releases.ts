import { mods, type ModDefinition, type ModId } from '../data/mods';

const API_URL = 'https://api.github.com/repos/Nereziel/Vivat-Slovakia-mods/releases?per_page=100';

export type GithubAsset = {
  name: string;
  browser_download_url: string;
  size: number;
};

export type GithubRelease = {
  id: number;
  name: string;
  tag_name: string;
  html_url: string;
  published_at: string | null;
  draft: boolean;
  prerelease: boolean;
  assets: GithubAsset[];
};

export type ModRelease = {
  modId: ModId;
  version: string | null;
  publishedAt: string | null;
  tag: string;
  releaseUrl: string;
  downloadUrl: string | null;
  fileName: string | null;
  format: 'ZIP' | 'DLL' | null;
};

export type BundleRelease = {
  downloadUrl: string;
  releaseUrl: string;
  publishedAt: string | null;
};

export type ReleaseCatalog = {
  mods: Partial<Record<ModId, ModRelease>>;
  all: BundleRelease | null;
};

export class ReleasesUnavailableError extends Error {
  constructor(public readonly kind: 'rate-limit' | 'network' | 'invalid-response') {
    super(kind === 'rate-limit' ? 'GitHub API má dočasně vyčerpaný limit požadavků.' :
      kind === 'network' ? 'Nepodařilo se načíst GitHub Releases.' : 'GitHub vrátil neplatná data.');
  }
}

function isSafeLink(link: unknown): link is string {
  if (typeof link !== 'string') return false;
  try {
    const url = new URL(link);
    return url.protocol === 'https:' && url.hostname === 'github.com' &&
      url.pathname.startsWith('/Nereziel/Vivat-Slovakia-mods/releases/');
  } catch {
    return false;
  }
}

function normalized(text: string): string {
  return text.toLocaleLowerCase('en-US').replace(/[^a-z0-9]/g, '');
}

function isModFile(assetName: string, mod: ModDefinition): boolean {
  const lower = assetName.toLowerCase();
  if (!/\.(zip|dll)$/.test(lower)) return false;
  return mod.assetPrefixes.some((prefix) => {
    const p = prefix.toLowerCase();
    return lower.startsWith(p) && (lower.length === p.length || /[._-]/.test(lower.charAt(p.length)));
  });
}

function isDedicatedRelease(release: GithubRelease, mod: ModDefinition): boolean {
  const tag = normalized(release.tag_name);
  const modTag = normalized(mod.id);
  if (tag.startsWith(`${modTag}v`) || tag === modTag) return true;
  const name = normalized(release.name);
  const modName = normalized(mod.name);
  return name.startsWith(`${modName}v`) || name === modName;
}

function parseVersion(text: string): string | null {
  const match = text.match(/(?:^|[^\d])v?(\d+\.\d+\.\d+(?:-[a-zA-Z0-9][a-zA-Z0-9.-]*)?)(?=$|[._+])/i);
  return match?.[1] ?? null;
}

function compareVersions(a: string | null, b: string | null): number {
  if (a === null) return b === null ? 0 : -1;
  if (b === null) return 1;
  const partsA = a.split(/[.-]/).slice(0, 3).map(Number);
  const partsB = b.split(/[.-]/).slice(0, 3).map(Number);
  for (let i = 0; i < 3; i += 1) {
    if (partsA[i] !== partsB[i]) return partsA[i] > partsB[i] ? 1 : -1;
  }
  // Prefer stable version over an otherwise identical prerelease.
  const aPre = /-/.test(a);
  const bPre = /-/.test(b);
  if (aPre !== bPre) return aPre ? -1 : 1;
  return 0;
}

function compareDates(a: string | null, b: string | null): number {
  return (a ? Date.parse(a) || 0 : 0) - (b ? Date.parse(b) || 0 : 0);
}

function bestAsset(assets: GithubAsset[]): GithubAsset | null {
  if (!assets.length) return null;
  // ZIP includes directory structure and is preferred over a DLL of the same release/version.
  return [...assets].sort((a, b) => Number(b.name.toLowerCase().endsWith('.zip')) -
    Number(a.name.toLowerCase().endsWith('.zip')))[0];
}

function modCandidate(release: GithubRelease, mod: ModDefinition): ModRelease | null {
  const matched = release.assets.filter((asset) => isSafeLink(asset.browser_download_url) &&
    isModFile(asset.name, mod));
  const dedicated = isDedicatedRelease(release, mod);
  let asset = bestAsset(matched);

  // A ZIP with a generic filename is associated only with an explicitly named
  // single-mod release, never with a general mods pack or another mod's release.
  if (!asset && dedicated) {
    const hasAnotherMod = release.assets.some((candidate) =>
      mods.some((other) => other.id !== mod.id && isModFile(candidate.name, other)));
    if (!hasAnotherMod) {
      asset = bestAsset(release.assets.filter((candidate) =>
        isSafeLink(candidate.browser_download_url) &&
        candidate.name.toLowerCase().endsWith('.zip') &&
        candidate.name.toLowerCase() !== 'vsechny.zip'));
    }
  }
  if (!asset && !dedicated) return null;

  // A shared build release carries versions in filenames; the build tag itself
  // is NOT a mod version.
  const version = asset ? parseVersion(asset.name) ?? (dedicated ? parseVersion(release.tag_name) : null) :
    parseVersion(release.tag_name) ?? parseVersion(release.name);

  return {
    modId: mod.id,
    version,
    publishedAt: release.published_at,
    tag: release.tag_name,
    releaseUrl: release.html_url,
    downloadUrl: asset?.browser_download_url ?? null,
    fileName: asset?.name ?? null,
    format: asset ? asset.name.toLowerCase().endsWith('.zip') ? 'ZIP' : 'DLL' : null,
  };
}

function isNewer(a: ModRelease, b: ModRelease): boolean {
  const versions = compareVersions(a.version, b.version);
  if (versions !== 0) return versions > 0;
  if (a.format !== b.format && (a.format === 'ZIP' || b.format === 'ZIP')) return a.format === 'ZIP';
  const dates = compareDates(a.publishedAt, b.publishedAt);
  if (dates !== 0) return dates > 0;
  return Boolean(a.downloadUrl && !b.downloadUrl);
}

export function buildCatalog(releases: GithubRelease[]): ReleaseCatalog {
  const catalog: ReleaseCatalog = { mods: {}, all: null };
  const published = releases.filter((release) => !release.draft);

  for (const release of published) {
    if (!isSafeLink(release.html_url)) continue;

    for (const mod of mods) {
      const candidate = modCandidate(release, mod);
      if (!candidate) continue;
      const previous = catalog.mods[mod.id];
      if (!previous || isNewer(candidate, previous)) catalog.mods[mod.id] = candidate;
    }

    const archive = release.assets.find((asset) => asset.name.toLowerCase() === 'vsechny.zip' &&
      isSafeLink(asset.browser_download_url));
    if (archive && (!catalog.all || compareDates(release.published_at, catalog.all.publishedAt) > 0)) {
      catalog.all = {
        downloadUrl: archive.browser_download_url,
        releaseUrl: release.html_url,
        publishedAt: release.published_at,
      };
    }
  }
  return catalog;
}

export async function fetchReleaseCatalog(signal?: AbortSignal): Promise<ReleaseCatalog> {
  let response: Response;
  try {
    response = await fetch(API_URL, {
      signal,
      headers: { Accept: 'application/vnd.github+json' },
    });
  } catch (error) {
    if (signal?.aborted) throw error;
    throw new ReleasesUnavailableError('network');
  }

  if (response.status === 429 || (response.status === 403 && response.headers.get('x-ratelimit-remaining') === '0')) {
    throw new ReleasesUnavailableError('rate-limit');
  }
  if (!response.ok) throw new ReleasesUnavailableError('network');

  let json: unknown;
  try { json = await response.json(); } catch { throw new ReleasesUnavailableError('invalid-response'); }
  if (!Array.isArray(json)) throw new ReleasesUnavailableError('invalid-response');

  const releases: GithubRelease[] = json.filter((entry): entry is GithubRelease => {
    if (!entry || typeof entry !== 'object') return false;
    const value = entry as Record<string, unknown>;
    return typeof value.tag_name === 'string' && typeof value.name === 'string' &&
      typeof value.html_url === 'string' && Array.isArray(value.assets);
  }).map((entry) => ({
    ...entry,
    assets: entry.assets.filter((asset) => asset && typeof asset.name === 'string' &&
      typeof asset.browser_download_url === 'string'),
  }));
  return buildCatalog(releases);
}

export function formatDate(value: string | null): string {
  if (!value) return 'Datum neuvedeno';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Datum neuvedeno';
  return new Intl.DateTimeFormat('cs-CZ', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Europe/Prague' }).format(date);
}
