param(
    [string]$Version = "1.0.7.0",
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
Get-ChildItem -LiteralPath $stage -Filter "*.pdb" -File -Recurse | Remove-Item -Force

# Render each package tile from the vector-like Keeply mark at high resolution. Do not use
# Icon.ToBitmap() here: it returns a small ICO frame (32x32 on this machine), which becomes
# blurry when enlarged for the 150px and wide tiles.
Add-Type -AssemblyName System.Drawing
function New-KeeplyTileImage {
    param([int]$Width, [int]$Height, [string]$Path)

    $supersample = 8
    $largeWidth = $Width * $supersample
    $largeHeight = $Height * $supersample
    $large = [System.Drawing.Bitmap]::new($largeWidth, $largeHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($large)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

            $side = [double][Math]::Min($largeWidth, $largeHeight)
            $left = ($largeWidth - $side) / 2.0
            $top = ($largeHeight - $side) / 2.0
            $inset = $side * 0.025
            $tileLeft = $left + $inset
            $tileTop = $top + $inset
            $tileSide = $side - (2.0 * $inset)
            $corner = $tileSide * 0.23
            $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $tilePath.AddArc([single]$tileLeft, [single]$tileTop, [single]$corner, [single]$corner, 180, 90)
                $tilePath.AddArc([single]($tileLeft + $tileSide - $corner), [single]$tileTop, [single]$corner, [single]$corner, 270, 90)
                $tilePath.AddArc([single]($tileLeft + $tileSide - $corner), [single]($tileTop + $tileSide - $corner), [single]$corner, [single]$corner, 0, 90)
                $tilePath.AddArc([single]$tileLeft, [single]($tileTop + $tileSide - $corner), [single]$corner, [single]$corner, 90, 90)
                $tilePath.CloseFigure()
                $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 166, 231, 195))
                try { $g.FillPath($tileBrush, $tilePath) } finally { $tileBrush.Dispose() }
            } finally { $tilePath.Dispose() }

            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 16, 21, 18), [single]($tileSide * 0.105))
            try {
                $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
                $g.DrawLine($pen, [single]($tileLeft + $tileSide * 0.37), [single]($tileTop + $tileSide * 0.25), [single]($tileLeft + $tileSide * 0.37), [single]($tileTop + $tileSide * 0.75))
                $g.DrawLine($pen, [single]($tileLeft + $tileSide * 0.39), [single]($tileTop + $tileSide * 0.50), [single]($tileLeft + $tileSide * 0.70), [single]($tileTop + $tileSide * 0.28))
                $g.DrawLine($pen, [single]($tileLeft + $tileSide * 0.39), [single]($tileTop + $tileSide * 0.50), [single]($tileLeft + $tileSide * 0.70), [single]($tileTop + $tileSide * 0.72))
            } finally { $pen.Dispose() }
        } finally { $g.Dispose() }

        $final = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $out = [System.Drawing.Graphics]::FromImage($final)
            try {
                $out.Clear([System.Drawing.Color]::Transparent)
                $out.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $out.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $out.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $out.DrawImage($large, 0, 0, $Width, $Height)
            } finally { $out.Dispose() }
            $final.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $final.Dispose() }
    } finally { $large.Dispose() }
}

foreach ($spec in @(
    @{ Name = "StoreLogo.png"; Width = 50; Height = 50 },
    @{ Name = "Square44x44Logo.png"; Width = 44; Height = 44 },
    @{ Name = "Square150x150Logo.png"; Width = 150; Height = 150 },
    @{ Name = "Wide310x150Logo.png"; Width = 310; Height = 150 }
)) {
    New-KeeplyTileImage -Width $spec.Width -Height $spec.Height -Path (Join-Path $stage "Assets\$($spec.Name)")
}

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
$verifyStage = Join-Path $env:TEMP ("Keeply-Package-Verify-" + [Guid]::NewGuid().ToString("N"))
$password = [Guid]::NewGuid().ToString("N")
$cert = $null
try {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -CertStoreLocation "Cert:\CurrentUser\My" -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
    $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $tempPfx -Password $securePassword | Out-Null
    & $signTool sign /fd SHA256 /f $tempPfx /p $password $output
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }

    # Validate that the completed package can be read after signing. Avoid installing
    # a temporary self-signed certificate into the Windows Root store just to verify it.
    & $makeAppx unpack /p $output /d $verifyStage /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx could not validate the completed package." }
} finally {
    if (Test-Path $tempPfx) { Remove-Item -LiteralPath $tempPfx -Force }
    if (Test-Path $verifyStage) { Remove-Item -LiteralPath $verifyStage -Recurse -Force }
    if ($cert) {
        & certutil.exe -user -delstore My $cert.Thumbprint *> $null
    }
}

Write-Host "Store package created: $output"
Write-Host "Identity: $identityName ($publisher)"
Write-Host "Version: $Version (x64)"
