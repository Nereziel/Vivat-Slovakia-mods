param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$Repository = 'Nereziel/Vivat-Slovakia-mods'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BepInExDir = Join-Path $GameDir 'BepInEx'

function Require-Tool([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Chybi prikaz '$Name'. Nainstaluj ho a zkus to znovu (viz RELEASE.md)."
    }
}

function Assert-LastExit([string]$Description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Description selhalo (exit code $LASTEXITCODE)."
    }
}

try {
    Require-Tool 'git'
    Require-Tool 'gh'
    Require-Tool 'dotnet'

    if (-not (Test-Path (Join-Path $BepInExDir 'core/BepInEx.Core.dll') -PathType Leaf)) {
        throw "BepInEx/core/BepInEx.Core.dll nenalezen: $BepInExDir"
    }
    if (-not (Test-Path (Join-Path $BepInExDir 'interop/Assembly-CSharp.dll') -PathType Leaf)) {
        throw "Chybi interop/Assembly-CSharp.dll. Nejdriv spust hru s BepInEx."
    }

    Push-Location $Root
    try {
        # Published assets must correspond to their tagged GitHub source revision.
        $currentBranch = (& git branch --show-current | Out-String).Trim()
        Assert-LastExit 'git branch'
        if ($currentBranch -ne 'main') {
            throw "Release se vydava jen z main. Aktualni vetev: $currentBranch"
        }
        $changes = (& git status --porcelain --untracked-files=no | Out-String).Trim()
        Assert-LastExit 'git status'
        if ($changes) {
            throw 'Zdrojove soubory jsou zmenene. Nejdřív proved commit a push, potom release.'
        }
        $localSha = (& git rev-parse HEAD | Out-String).Trim()
        Assert-LastExit 'git rev-parse'
        $remoteSha = (& gh api "repos/$Repository/commits/main" --jq '.sha' | Out-String).Trim()
        Assert-LastExit 'gh api'
        if ($localSha -ne $remoteSha) {
            throw "Lokalni main neni shodny s GitHub main. Proved git pull/push; lokalni=$localSha GitHub=$remoteSha"
        }

        $projects = @(Get-ChildItem -LiteralPath $Root -Directory -Filter 'Vivat*' |
            ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.csproj' })
        if ($projects.Count -lt 7) {
            throw "Ocekavano alespon 7 projektu, nalezeno $($projects.Count)."
        }

        $dist = Join-Path $Root 'dist'
        New-Item -ItemType Directory -Path $dist -Force | Out-Null
        $packages = [System.Collections.Generic.List[object]]::new()

        foreach ($project in $projects) {
            $directory = $project.Directory.FullName
            $module = Split-Path $directory -Leaf
            [xml]$projectXml = Get-Content -LiteralPath $project.FullName -Raw
            $assembly = [string]$projectXml.Project.PropertyGroup.AssemblyName
            if ([string]::IsNullOrWhiteSpace($assembly)) { $assembly = $project.BaseName }

            $csFiles = @(Get-ChildItem -LiteralPath $directory -Filter '*.cs' -File)
            $source = ($csFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
            $pluginMatches = [regex]::Matches($source,
                '\[BepInPlugin\([^,\r\n]+,\s*"(?<name>[^"]+)",\s*"(?<version>\d+\.\d+\.\d+)"\)\]')
            if ($pluginMatches.Count -ne 1) {
                throw "Projekt $module musi mit presne jednu BepInPlugin verzi, nalezeno $($pluginMatches.Count)."
            }
            $version = $pluginMatches[0].Groups['version'].Value
            $displayName = $pluginMatches[0].Groups['name'].Value
            $tag = $module + '-v' + $version

            Write-Host "==> Sestavuji $displayName $version" -ForegroundColor Cyan
            & dotnet build $project.FullName -c Release "-p:BepInExDir=$BepInExDir"
            Assert-LastExit "dotnet build $module"

            $dll = Join-Path $directory "bin/Release/net6.0/$assembly.dll"
            if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
                throw "DLL nebylo vytvoreno: $dll"
            }

            $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("vivat-release-" + [guid]::NewGuid().ToString('N'))
            try {
                $pluginDir = Join-Path $stage "BepInEx/plugins/$module"
                New-Item -Path $pluginDir -ItemType Directory -Force | Out-Null
                Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir "$assembly.dll") -Force

                if ($module -eq 'VivatPolice') {
                    foreach ($voice in @('charon', 'fenrir')) {
                        $voiceSource = Join-Path $directory "PoliceVoiceSamples/Relay/$voice"
                        $voiceFiles = @(Get-ChildItem -LiteralPath $voiceSource -Filter '*.wav' -File)
                        if ($voiceFiles.Count -eq 0) {
                            throw "Policejni hlasove soubory chybi: $voiceSource"
                        }
                        $voiceDest = Join-Path $stage "BepInEx/plugins/VivatPoliceVoices/$voice"
                        New-Item -Path $voiceDest -ItemType Directory -Force | Out-Null
                        foreach ($wav in $voiceFiles) {
                            Copy-Item -LiteralPath $wav.FullName -Destination $voiceDest
                        }
                    }
                }

                $archive = Join-Path $dist ($tag + '.zip')
                Compress-Archive -Path (Join-Path $stage 'BepInEx') -DestinationPath $archive -CompressionLevel Optimal -Force
                if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
                    throw "ZIP nebyl vytvoren: $archive"
                }
                $packages.Add([pscustomobject]@{
                    Module = $module
                    Name = $displayName
                    Version = $version
                    Tag = $tag
                    Archive = $archive
                })
                Write-Host "    Vytvoreno: $archive"
            }
            finally {
                if (Test-Path -LiteralPath $stage) {
                    Remove-Item -LiteralPath $stage -Recurse -Force
                }
            }
        }

        # Never publish a partial build; packages above must all succeed first.
        Write-Host "==> Kontroluji existujici GitHub Releases..." -ForegroundColor Cyan
        $existingJson = (& gh release list -R $Repository --limit 1000 --json tagName | Out-String).Trim()
        Assert-LastExit 'gh release list'
        $existing = @{}
        foreach ($release in @(ConvertFrom-Json -InputObject $existingJson)) {
            if ($null -ne $release -and $release.tagName) {
                $existing[[string]$release.tagName] = $true
            }
        }

        foreach ($package in $packages) {
            if ($existing.ContainsKey($package.Tag)) {
                Write-Host "Preskakuji existujici release: $($package.Tag)" -ForegroundColor Yellow
                continue
            }
            Write-Host "==> Publikuji $($package.Tag)" -ForegroundColor Cyan
            $notes = "Samostatny mod $($package.Name) v$($package.Version). " +
                     'Rozbal ZIP do korenove slozky Vivat Slovakia. Navod: ' +
                     "https://github.com/$Repository/blob/main/INSTALL.md"
            & gh release create $package.Tag $package.Archive --repo $Repository --target $remoteSha --title "$($package.Name) v$($package.Version)" --notes $notes --latest=false
            Assert-LastExit "gh release create $($package.Tag)"
            $existing[$package.Tag] = $true
        }

        Write-Host "Hotovo. Vydani: https://github.com/$Repository/releases" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Error $_
    exit 1
}
