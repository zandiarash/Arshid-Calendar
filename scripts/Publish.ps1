#Requires -Version 7.0
<#
.SYNOPSIS
    Builds release artifacts for the version set in Directory.Build.props (<AppVersion>).

.DESCRIPTION
    Microsoft Store (-Store):
      artifacts/<version>/Store/ArshidCalendar_<version>.msixbundle
        Unsigned x64 + ARM64 bundle for Partner Center (the Store signs it). Package identity comes from
        PackageIdentityName / PackagePublisher / PackagePublisherDisplayName in Directory.Build.props, which
        must match Partner Center → Product identity.

    Direct distribution (default):
      artifacts/<version>/
        ArshidCalendar-<version>-<rid>.zip            desktop widget only (single-file exe, no install)
        ArshidCalendar-Msix-<version>-<arch>.zip      signed MSIX with the desktop widget + Widgets Board widget:
          ArshidCalendar_<version>_<arch>.msix
          ArshidCalendar.cer                          signing certificate (public part)
          Install.ps1                                 trusts the certificate + installs the MSIX

    Sideload MSIX packages must be signed. Pass -CertificatePath/-CertificatePassword to use your own .pfx
    (its subject must equal PackagePublisher). Otherwise a self-signed certificate with that subject is
    created once in Cert:\CurrentUser\My and reused.

.EXAMPLE
    ./scripts/Publish.ps1 -Store                         # Partner Center upload
    ./scripts/Publish.ps1                                # zip + signed sideload MSIX
    ./scripts/Publish.ps1 -FrameworkDependent            # smaller desktop exe; needs the .NET 11 Desktop Runtime
    ./scripts/Publish.ps1 -CertificatePath my.pfx -CertificatePassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [switch] $Store,
    [ValidateSet('x64', 'ARM64')] [string] $Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [switch] $FrameworkDependent,
    [switch] $SkipDesktop,
    [switch] $SkipMsix,
    [string] $CertificatePath,
    [securestring] $CertificatePassword
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/ArshidCalendar.WidgetProvider/ArshidCalendar.WidgetProvider.csproj'

# ---- Version and identity: single source of truth -----------------------------------------
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
function Get-Prop([string] $name) { @($props.Project.PropertyGroup | ForEach-Object { $_.$name } | Where-Object { $_ })[0] }
$version = Get-Prop 'AppVersion'
$publisher = Get-Prop 'PackagePublisher'
if ($version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$') { throw "AppVersion '$version' must have four numeric parts." }
$out = Join-Path $root "artifacts/$version"
New-Item -ItemType Directory -Force $out | Out-Null

# Builds one .msix for $arch into $dest; $extra = additional MSBuild args (signing).
function Build-Msix([string] $arch, [string] $dest, [string[]] $extra) {
    $pkgDir = Join-Path $out "msix-build-$arch"
    Remove-Item $pkgDir -Recurse -Force -ErrorAction SilentlyContinue
    dotnet build $project -c Release -p:Platform=$arch -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageDir="$pkgDir\" -p:AppxPackageTestDir="$pkgDir\" -p:AppxSymbolPackageEnabled=false @extra -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "MSIX build ($arch) failed." }
    $msix = Get-ChildItem $pkgDir -Recurse -Filter *.msix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msix) { throw "No .msix was produced for $arch." }
    $target = Join-Path $dest "ArshidCalendar_${version}_$($arch.ToLowerInvariant()).msix"
    Copy-Item $msix.FullName $target
    Remove-Item $pkgDir -Recurse -Force
    return $target
}

# =============================================================================================
# Microsoft Store: unsigned x64 + ARM64 .msixbundle
# =============================================================================================
if ($Store) {
    if ($version -notmatch '\.0$') { throw "AppVersion '$version': the Microsoft Store requires the last (Revision) part to be 0." }
    if ($publisher -eq 'CN=ArshidCalendar') {
        Write-Warning 'PackagePublisher is still the placeholder (CN=ArshidCalendar). Copy the Package/Identity values from Partner Center → Product identity into Directory.Build.props, or Partner Center will reject the upload.'
    }
    Write-Host "Building Arshid Calendar v$version for the Microsoft Store" -ForegroundColor Cyan
    $storeOut = Join-Path $out 'Store'
    $layout = Join-Path $storeOut 'packages'
    Remove-Item $storeOut -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $layout | Out-Null

    foreach ($arch in 'x64', 'ARM64') {
        Write-Host "  [$arch] MSIX…" -ForegroundColor Cyan
        Build-Msix $arch $layout @('-p:AppxPackageSigningEnabled=false') | Out-Null
    }

    # makeappx.exe from the Microsoft.Windows.SDK.BuildTools package the build already restored.
    [xml]$pkgs = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
    $sdkVer = ($pkgs.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools').Version
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $makeappx = Get-ChildItem (Join-Path $nuget "microsoft.windows.sdk.buildtools/$sdkVer/bin") -Recurse -Filter makeappx.exe |
        Where-Object { $_.Directory.Name -eq $hostArch } | Select-Object -First 1
    if (-not $makeappx) { throw "makeappx.exe not found in Microsoft.Windows.SDK.BuildTools $sdkVer." }

    $bundle = Join-Path $storeOut "ArshidCalendar_$version.msixbundle"
    & $makeappx.FullName bundle /d $layout /p $bundle /bv $version /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle failed.' }
    Remove-Item $layout -Recurse -Force

    Write-Host "`n✔ Store package ready: $bundle" -ForegroundColor Green
    Write-Host '  Upload it in Partner Center → your app → Submission → Packages.' -ForegroundColor Green
    exit 0
}

# =============================================================================================
# Direct distribution
# =============================================================================================
$rid = "win-$($Platform.ToLowerInvariant())"
if (-not $SkipDesktop) { Remove-Item (Join-Path $out 'Desktop'), (Join-Path $out "ArshidCalendar-$version-$rid.zip") -Recurse -Force -ErrorAction SilentlyContinue }
if (-not $SkipMsix) { Remove-Item (Join-Path $out 'Msix'), (Join-Path $out "ArshidCalendar-Msix-$version-*.zip") -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "Publishing Arshid Calendar v$version ($rid)" -ForegroundColor Cyan

# ---- 1. Desktop widget (portable zip) ------------------------------------------------------
if (-not $SkipDesktop) {
    Write-Host "`n[1/2] Desktop widget…" -ForegroundColor Cyan
    $desktopOut = Join-Path $out 'Desktop'
    # WPF's DirectWriteForwarder.dll (mixed-mode) can't load from inside a single-file bundle,
    # so self-contained builds extract their content to %TEMP%\.net on first start.
    dotnet publish (Join-Path $root 'src/ArshidCalendar.Desktop/ArshidCalendar.Desktop.csproj') `
        -c Release -r $rid --self-contained:$(-not $FrameworkDependent) `
        -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=$(-not $FrameworkDependent) -p:EnableCompressionInSingleFile=$(-not $FrameworkDependent) -p:DebugType=none `
        -o $desktopOut -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    $zip = Join-Path $out "ArshidCalendar-$version-$rid.zip"
    Compress-Archive -Path (Join-Path $desktopOut '*') -DestinationPath $zip
    Write-Host "  → $zip" -ForegroundColor Green
}

# ---- 2. Signed sideload MSIX (desktop widget + Widgets Board widget) ------------------------
if (-not $SkipMsix) {
    Write-Host "`n[2/2] MSIX package…" -ForegroundColor Cyan
    $msixOut = Join-Path $out 'Msix'
    New-Item -ItemType Directory -Force $msixOut | Out-Null

    if ($CertificatePath) {
        $plain = if ($CertificatePassword) { ConvertFrom-SecureString $CertificatePassword -AsPlainText } else { '' }
        $certArgs = @("-p:PackageCertificateKeyFile=$((Resolve-Path $CertificatePath).Path)")
        if ($plain) { $certArgs += "-p:PackageCertificatePassword=$plain" }
        $pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path $CertificatePath).Path, $plain)
        if ($pfx.Subject -ne $publisher) { throw "Certificate subject '$($pfx.Subject)' must equal PackagePublisher '$publisher'." }
        [IO.File]::WriteAllBytes((Join-Path $msixOut 'ArshidCalendar.cer'), $pfx.Export('Cert'))
    }
    else {
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $cert) {
            Write-Host "  Creating self-signed code-signing certificate $publisher" -ForegroundColor Yellow
            $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'Arshid Calendar' `
                -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5) `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        }
        Export-Certificate -Cert $cert -FilePath (Join-Path $msixOut 'ArshidCalendar.cer') | Out-Null
        $certArgs = @("-p:PackageCertificateThumbprint=$($cert.Thumbprint)")
    }

    Build-Msix $Platform $msixOut (@('-p:AppxPackageSigningEnabled=true') + $certArgs) | Out-Null

    # End-user installer: trust the certificate (admin, once) and install the package.
    @'
#Requires -RunAsAdministrator
# Installs Arshid Calendar (desktop widget + Windows 11 Widgets Board widget).
$here = $PSScriptRoot
Import-Certificate -FilePath (Join-Path $here 'ArshidCalendar.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Get-ChildItem $here -Filter *.msix | ForEach-Object { Add-AppxPackage -Path $_.FullName -ForceApplicationShutdown }
Write-Host 'Installed. Start «تقویم آرشید» from the Start menu, or open the Widgets Board (Win+W) → + → «تقویم آرشید».' -ForegroundColor Green
'@ | Set-Content (Join-Path $msixOut 'Install.ps1') -Encoding utf8BOM

    $msixZip = Join-Path $out "ArshidCalendar-Msix-$version-$($Platform.ToLowerInvariant()).zip"
    Compress-Archive -Path (Join-Path $msixOut '*') -DestinationPath $msixZip
    Write-Host "  → $msixZip" -ForegroundColor Green
}

Write-Host "`n✔ Release v$version ready in $out" -ForegroundColor Green
exit 0
