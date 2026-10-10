param(
    [string]$Version = "1.0.6.0",
    [string]$SdkBuildToolsVersion = "10.0.26100.7705",
    [string]$PublishedPath = "artifacts\Keeply-win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$published = Join-Path $root $PublishedPath
$stage = Join-Path $root "artifacts\Keeply-MSIX-$Version"
$output = Join-Path $root "artifacts\release\Keeply-$Version.msix"
$toolsRoot = Join-Path $env:TEMP "keeply-msix-sdktools"
$sdkTools = Join-Path $toolsRoot "pkg"
$nupkg = Join-Path $toolsRoot "sdktools.nupkg"
$buildToolsUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$SdkBuildToolsVersion/microsoft.windows.sdk.buildtools.$SdkBuildToolsVersion.nupkg"
$identityName = "Keeply.Keeply"
$publisher = "CN=F3A6FD3C-6C85-414B-9213-7DA5FE23A6A1"

if (-not (Test-Path (Join-Path $published "Keeply.exe"))) {
    throw "The self-contained app build is missing. Run scripts/Publish-Windows.ps1 first."
}
if (-not (Test-Path (Join-Path $published "licensing.json"))) {
    throw "licensing.json is missing from the published app output."
}

if (-not (Test-Path (Join-Path $sdkTools "bin\10.0.26100.0\x64\makeappx.exe"))) {
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    if (-not (Test-Path $nupkg)) { Invoke-WebRequest -Uri $buildToolsUrl -OutFile $nupkg }
    Expand-Archive -LiteralPath $nupkg -DestinationPath $sdkTools -Force
}
$makeAppx = Join-Path $sdkTools "bin\10.0.26100.0\x64\makeappx.exe"
$signTool = Join-Path $sdkTools "bin\10.0.26100.0\x64\signtool.exe"
if (-not (Test-Path $makeAppx) -or -not (Test-Path $signTool)) {
    throw "MakeAppx or SignTool was not found in the Windows SDK BuildTools package."
}

if (Test-Path $stage) { throw "Staging folder already exists; choose another version or remove it after verifying its contents: $stage" }
New-Item -ItemType Directory -Force -Path $stage, (Join-Path $stage "Assets"), (Split-Path -Parent $output) | Out-Null
Copy-Item -Path (Join-Path $published "*") -Destination $stage -Recurse -Force

# Generate the tile images from the app's existing icon so the package carries Keeply branding.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new((Join-Path $root "assets\Keeply.ico"))
try {
    foreach ($spec in @(
        @{ Name = "StoreLogo.png"; Width = 50; Height = 50 },
        @{ Name = "Square44x44Logo.png"; Width = 44; Height = 44 },
        @{ Name = "Square150x150Logo.png"; Width = 150; Height = 150 },
        @{ Name = "Wide310x150Logo.png"; Width = 310; Height = 150 }
    )) {
        $bitmap = [System.Drawing.Bitmap]::new($spec.Width, $spec.Height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $size = [Math]::Min($spec.Width, $spec.Height)
                $offsetX = [int](($spec.Width - $size) / 2)
                $offsetY = [int](($spec.Height - $size) / 2)
                $iconBitmap = $icon.ToBitmap()
                try {
                    $destination = [System.Drawing.Rectangle]::new($offsetX, $offsetY, $size, $size)
                    $graphics.DrawImage($iconBitmap, $destination)
                } finally { $iconBitmap.Dispose() }
            } finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $stage "Assets\$($spec.Name)"), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
} finally { $icon.Dispose() }

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap desktop rescap">
  <Identity Name="$identityName" Publisher="$publisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Keeply</DisplayName>
    <PublisherDisplayName>Keeply</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="Keeply" Executable="Keeply.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Keeply" Description="Sort, compare, compress, and convert photos locally." BackgroundColor="transparent" Square44x44Logo="Assets\Square44x44Logo.png" Square150x150Logo="Assets\Square150x150Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
      <Extensions>
        <desktop:Extension Category="windows.fullTrustProcess" Executable="Keeply.exe">
          <desktop:FullTrustProcess />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
$manifest | Set-Content -LiteralPath (Join-Path $stage "AppxManifest.xml") -Encoding utf8

if (Test-Path $output) { throw "Output package already exists; refusing to overwrite it: $output" }
& $makeAppx pack /d $stage /p $output /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }

# Partner Center requires the package publisher to match the reserved Store identity.
# A temporary matching certificate signs this submission package; the Store re-signs
# packages after certification. The private key is not kept in the repository or output.
$tempPfx = Join-Path $env:TEMP ("Keeply-Store-" + [Guid]::NewGuid().ToString("N") + ".pfx")
$tempCer = Join-Path $env:TEMP ("Keeply-Store-" + [Guid]::NewGuid().ToString("N") + ".cer")
$password = [Guid]::NewGuid().ToString("N")
$cert = $null
try {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -CertStoreLocation "Cert:\CurrentUser\My" -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
    $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $tempPfx -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $tempCer | Out-Null
    Import-Certificate -FilePath $tempCer -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
    & $signTool sign /fd SHA256 /f $tempPfx /p $password $output
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa /v $output
    if ($LASTEXITCODE -ne 0) { throw "SignTool could not verify the signed package." }
} finally {
    if (Test-Path $tempPfx) { Remove-Item -LiteralPath $tempPfx -Force }
    if (Test-Path $tempCer) { Remove-Item -LiteralPath $tempCer -Force }
    if ($cert) {
        & certutil.exe -user -delstore Root $cert.Thumbprint *> $null
        & certutil.exe -user -delstore My $cert.Thumbprint *> $null
    }
}

Write-Host "Store package created: $output"
Write-Host "Identity: $identityName ($publisher)"
Write-Host "Version: $Version (x64)"
