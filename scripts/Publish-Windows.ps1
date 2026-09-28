param(
    [string]$Version = "1.0.0",
    [string]$ApiBaseUrl = "",
    [string]$CheckoutUrl = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "PhotoKeepKill.csproj"
$artifactRoot = Join-Path $root "artifacts"
$publishDir = Join-Path $artifactRoot "Keeply-win-x64"
$releaseDir = Join-Path $artifactRoot "release"

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl) -or $ApiBaseUrl -notmatch '^https://') {
    throw "Pass the deployed public HTTPS website URL with -ApiBaseUrl before creating a release."
}

New-Item -ItemType Directory -Force -Path $artifactRoot, $releaseDir | Out-Null
if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$config = [ordered]@{
    apiBaseUrl = $ApiBaseUrl.TrimEnd('/') + "/"
    checkoutUrl = $CheckoutUrl
}
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDir "licensing.json") -Encoding utf8

$zipPath = Join-Path $releaseDir "Keeply-$Version-win-x64-portable.zip"
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$compiler = Get-Command "makensis.exe" -ErrorAction SilentlyContinue
if (-not $compiler) {
    $nsisPaths = @(
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "$env:ProgramFiles\NSIS\makensis.exe"
    )
    $compilerPath = $nsisPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    $compilerPath = $compiler.Source
}

if ($compilerPath) {
    $installerPath = Join-Path $releaseDir "Keeply-Setup-$Version.exe"
    & $compilerPath "/DAPP_VERSION=$Version" "/DPUBLISH_DIR=$publishDir" "/DOUTPUT_DIR=$releaseDir" (Join-Path $PSScriptRoot "Keeply.nsi")
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE." }
    $websiteDownloads = Join-Path $root "website\public\downloads"
    New-Item -ItemType Directory -Force -Path $websiteDownloads | Out-Null
    Copy-Item -LiteralPath $installerPath -Destination (Join-Path $websiteDownloads "Keeply-Setup.exe") -Force
    Write-Host "Website download updated: $(Join-Path $websiteDownloads 'Keeply-Setup.exe')"
    Write-Host "Installer created: $installerPath"
} else {
    Write-Warning "NSIS is not installed. The self-contained portable ZIP is ready; install NSIS and rerun this script to create Keeply-Setup-$Version.exe."
}

Write-Host "Portable release created: $zipPath"
