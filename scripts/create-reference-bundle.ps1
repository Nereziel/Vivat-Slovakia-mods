param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$Output = (Join-Path (Get-Location) "BepInEx-build-refs.zip")
)

$ErrorActionPreference = "Stop"
$source = Join-Path $GameDir "BepInEx"
$core = Join-Path $source "core"
$interop = Join-Path $source "interop"
if (-not (Test-Path (Join-Path $core "BepInEx.Core.dll"))) {
    throw "BepInEx 6 not installed in $GameDir (core/BepInEx.Core.dll missing)."
}
if (-not (Test-Path (Join-Path $interop "Assembly-CSharp.dll"))) {
    throw "Run Vivat Slovakia once with BepInEx to generate interop/Assembly-CSharp.dll."
}
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("vivat-build-refs-" + [guid]::NewGuid().ToString("N"))
try {
    $dest = Join-Path $stage "BepInEx"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -LiteralPath $core -Destination (Join-Path $dest "core") -Recurse
    Copy-Item -LiteralPath $interop -Destination (Join-Path $dest "interop") -Recurse
    $outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
    Compress-Archive -Path $dest -DestinationPath $outputFile -Force -CompressionLevel Optimal
    Write-Host "Created private build reference bundle: $outputFile"
    Write-Host "Do NOT publish it publicly: it includes game-generated and BepInEx assemblies."
}
finally {
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
