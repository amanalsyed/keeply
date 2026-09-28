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

$compiler = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $compiler) {
    $innoPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $compilerPath = $innoPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    $compilerPath = $compiler.Source
}

if ($compilerPath) {
    $installerPath = Join-Path $releaseDir "Keeply-Setup-$Version.exe"
    & $compilerPath "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" "/O$releaseDir" "/FKeeply-Setup-$Version" (Join-Path $PSScriptRoot "Keeply.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    Write-Host "Installer created: $installerPath"
} else {
    Write-Warning "Inno Setup 6 is not installed. The self-contained portable ZIP is ready; install Inno Setup and rerun this script to create Keeply-Setup-$Version.exe."
}

Write-Host "Portable release created: $zipPath"
