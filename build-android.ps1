<#
================================================================================
  Quiz Player - Android build script (PowerShell)

  Builds the .NET MAUI Android player (QuizBuilder.Player) into an installable
  artifact. Mirrors build.bat's spirit: check the toolchain up front, stage the
  output clearly, and fail with a message you can act on rather than a wall of
  MSBuild noise.

  USAGE
    ./build-android.ps1                       Debug APK (installs directly)
    ./build-android.ps1 -Configuration Release   Release APK + AAB (needs keystore)
    ./build-android.ps1 -Install              Build Debug, then adb-install it
    ./build-android.ps1 -Clean                Clean first
    ./build-android.ps1 -NoBuildCore          Skip the Core net10 sanity build

  DEFAULT: Debug. A Debug build is signed with Android's auto-generated debug
  key, needs no keystore, and can be sideloaded straight onto a phone -- the
  fast inner loop. Release trims + AOT-compiles and produces BOTH an APK (for
  direct install/testing) and an AAB (for the Play Store), signed with YOUR
  keystore if these environment variables are set:

      QB_KEYSTORE          path to the .keystore / .jks file
      QB_KEYSTORE_PASS     keystore (store) password
      QB_KEY_ALIAS         key alias inside the keystore
      QB_KEY_PASS          key password (defaults to QB_KEYSTORE_PASS if unset)

  If those are absent, a Release build still runs but stays debug-signed and
  prints how to sign it -- it never silently produces something you think is
  release-signed when it isn't.

  REQUIREMENTS
    - .NET 10 SDK (MAUI 10 ships with .NET 10)
    - The MAUI Android workload: dotnet workload install maui-android
    - A JDK + Android SDK (the workload's acquisition can install these)
================================================================================
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Install,
    [switch]$Clean,
    [switch]$NoBuildCore,
    [switch]$SkipWorkloadCheck
)

$ErrorActionPreference = 'Stop'

# Run from the script's own directory so it works from anywhere.
Set-Location -Path $PSScriptRoot

$Project     = 'QuizBuilder.Player/QuizBuilder.Player.csproj'
$CoreProject = 'QuizBuilder.Core/QuizBuilder.Core.csproj'
$Framework   = 'net10.0-android'

function Write-Stage([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Fail([string]$text) {
    Write-Host ''
    Write-Host "[ERROR] $text" -ForegroundColor Red
    exit 1
}

# ----------------------------------------------------------------------------
#  Toolchain checks -- clear messages beat cryptic MSBuild failures.
# ----------------------------------------------------------------------------
Write-Stage 'Checking the toolchain'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail "The .NET SDK was not found on PATH. Install the .NET 10 SDK from https://dot.net"
}

$sdkVersion = (& dotnet --version).Trim()
Write-Host "  .NET SDK        : $sdkVersion"

# The Android build needs the maui-android workload. Detect it robustly via the
# machine-readable JSON (the human table is formatted and localised, so string-
# matching its text is brittle -- it produced false "not installed" errors even
# right after a successful install). We check the JSON 'installed' list, and if
# that call fails for any reason we fall back to a permissive text scan rather
# than blocking a machine that genuinely has the workload.
$hasWorkload = $false
if ($SkipWorkloadCheck) {
    Write-Host "  MAUI workload   : check skipped (-SkipWorkloadCheck)"
    $hasWorkload = $true
}
if (-not $hasWorkload) {
    try {
        $mr = & dotnet workload list --machine-readable 2>$null | Out-String
        # The JSON blob is formatted amid other text; grab the outermost braces.
        $start = $mr.IndexOf('{')
        $end   = $mr.LastIndexOf('}')
        if ($start -ge 0 -and $end -gt $start) {
            $json = $mr.Substring($start, $end - $start + 1) | ConvertFrom-Json
            if ($json.installed -and ($json.installed -contains 'maui-android' -or
                                      $json.installed -contains 'maui' -or
                                      $json.installed -contains 'maui-mobile')) {
                $hasWorkload = $true
            }
        }
    }
    catch {
        # JSON path unavailable; fall through to the text scan below.
    }
}

if (-not $hasWorkload) {
    # Fallback: scan the plain list text, joined to a single string so -match
    # sees one blob rather than an array of lines.
    $listText = (& dotnet workload list 2>$null | Out-String)
    if ($listText -match 'maui-android' -or $listText -match 'maui-mobile' -or
        $listText -match '(?m)^\s*maui\s') {
        $hasWorkload = $true
    }
}

if (-not $hasWorkload) {
    Fail @"
The MAUI Android workload does not appear to be installed.
Install it with:
    dotnet workload install maui-android
(You may need an elevated / admin shell.)

If you just installed it and still see this, run 'dotnet workload list' by hand
to confirm; you can also re-run this script with -SkipWorkloadCheck to bypass.
"@
}
Write-Host "  MAUI workload   : present"
Write-Host "  Configuration   : $Configuration"
Write-Host "  Target framework: $Framework"

# ----------------------------------------------------------------------------
#  Android SDK: find it, pass it to MSBuild explicitly, and make sure its
#  licenses are accepted. The MAUI workload installs the Android *build tooling*
#  but NOT the Android SDK itself (the platform jars / build-tools / platform-
#  tools), so a fresh machine fails with XA5300 until the SDK is present. We
#  detect the standard install locations rather than relying on .NET's auto-
#  detection, which misses non-default layouts. The resolved path is handed to
#  the build via -p:AndroidSdkDirectory so it works even when ANDROID_HOME is
#  unset.
# ----------------------------------------------------------------------------
$androidSdkArgs = @()
$sdkCandidates = @(
    $env:ANDROID_HOME,
    $env:ANDROID_SDK_ROOT,
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    (Join-Path $env:ProgramData  'Android\Sdk'),
    (Join-Path $env:USERPROFILE  'Android\Sdk')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

$androidSdk = $null
foreach ($cand in $sdkCandidates) {
    # A real SDK has a platforms/ or platform-tools/ folder; a bare empty dir
    # (which .NET would still try and fail on) does not.
    if ((Test-Path (Join-Path $cand 'platform-tools')) -or
        (Test-Path (Join-Path $cand 'platforms'))) {
        $androidSdk = $cand
        break
    }
}

if ($androidSdk) {
    Write-Host "  Android SDK     : $androidSdk"
    $androidSdkArgs = @("-p:AndroidSdkDirectory=$androidSdk")

    # Accept SDK licenses if the command-line tools are present. A pending
    # licence also causes a build failure, and accepting is safe and idempotent.
    $sdkManager = Get-ChildItem -Path (Join-Path $androidSdk 'cmdline-tools') `
                    -Filter 'sdkmanager.bat' -Recurse -ErrorAction SilentlyContinue |
                  Select-Object -First 1
    if ($sdkManager) {
        try {
            Write-Host "  Accepting Android SDK licenses (idempotent)..."
            # 'y' to every prompt; suppress the noisy output.
            cmd /c "echo y| `"$($sdkManager.FullName)`" --licenses" *> $null
        }
        catch {
            Write-Host "  (Could not auto-accept licenses; if the build reports a" -ForegroundColor Yellow
            Write-Host "   licence error, run sdkmanager --licenses by hand.)" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host ''
    Write-Host "  Android SDK     : NOT FOUND." -ForegroundColor Yellow
    Write-Host "  The MAUI workload does not include the Android SDK itself." -ForegroundColor Yellow
    Write-Host "  Easiest fix: install Android Studio (https://developer.android.com/studio)" -ForegroundColor Yellow
    Write-Host "  and let its first-run wizard download the SDK to the default location." -ForegroundColor Yellow
    Write-Host "  Then re-run this script; it will detect and use it automatically." -ForegroundColor Yellow
    Write-Host "  (If your SDK is in a custom path, set ANDROID_HOME to it and re-run.)" -ForegroundColor Yellow
    Fail 'Android SDK not found (XA5300 would follow). See the note above.'
}

# ----------------------------------------------------------------------------
#  Java SDK (JDK): the Android build compiles Java glue and needs a JDK 17.
#  Like the Android SDK, the MAUI workload does not supply it -- but Android
#  Studio bundles one (its JetBrains Runtime, a full JDK), so on most machines
#  it is already present and we just need to point the build at it. We detect
#  the usual homes and pass -p:JavaSdkDirectory explicitly; only if none is
#  found do we ask the user to install one.
# ----------------------------------------------------------------------------
$javaSdkArgs = @()
$jdkCandidates = @()
if ($env:JAVA_HOME) { $jdkCandidates += $env:JAVA_HOME }
# Android Studio's bundled JetBrains Runtime (a complete JDK).
$jdkCandidates += (Join-Path $env:ProgramFiles 'Android\Android Studio\jbr')
$jdkCandidates += (Join-Path ${env:ProgramFiles(x86)} 'Android\Android Studio\jbr')
$jdkCandidates += (Join-Path $env:LOCALAPPDATA 'Programs\Android Studio\jbr')
# Microsoft build of OpenJDK, and Adoptium/Temurin, newest-first.
foreach ($root in @((Join-Path $env:ProgramFiles 'Microsoft'),
                    (Join-Path $env:ProgramFiles 'Eclipse Adoptium'),
                    (Join-Path $env:ProgramFiles 'Java'))) {
    if (Test-Path $root) {
        Get-ChildItem $root -Directory -Filter 'jdk*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { $jdkCandidates += $_.FullName }
    }
}
# The Android workload sometimes lays down its own OpenJDK under the SDK tree.
if ($androidSdk) {
    $wljdk = Join-Path $androidSdk 'jdk'
    if (Test-Path $wljdk) { $jdkCandidates += $wljdk }
}

$javaSdk = $null
foreach ($cand in ($jdkCandidates | Where-Object { $_ } | Select-Object -Unique)) {
    # A usable JDK has bin\java.exe.
    if (Test-Path (Join-Path $cand 'bin\java.exe')) {
        $javaSdk = $cand
        break
    }
}

if ($javaSdk) {
    Write-Host "  Java SDK (JDK)  : $javaSdk"
    $javaSdkArgs = @("-p:JavaSdkDirectory=$javaSdk")
}
else {
    Write-Host ''
    Write-Host "  Java SDK (JDK)  : NOT FOUND." -ForegroundColor Yellow
    Write-Host "  The Android build needs a JDK 17. If you installed Android Studio it" -ForegroundColor Yellow
    Write-Host "  bundles one at '...\Android\Android Studio\jbr' -- if that path exists," -ForegroundColor Yellow
    Write-Host "  set JAVA_HOME to it and re-run. Otherwise install the Microsoft build of" -ForegroundColor Yellow
    Write-Host "  OpenJDK 17: https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.msi" -ForegroundColor Yellow
    Write-Host "  then re-run this script (it self-registers and will be detected)." -ForegroundColor Yellow
    Fail 'Java SDK (JDK) not found (XA5300 would follow). See the note above.'
}

# ----------------------------------------------------------------------------
#  Android platform (android.jar): the build compiles against a specific API
#  level's android.jar (the Microsoft.Android.Sdk 36.x default is API 36). A
#  freshly-installed Android SDK often lacks that exact platform, giving XA5207.
#  The .NET Android tooling ships an InstallAndroidDependencies target that
#  downloads the missing platform + build-tools; we run it once if the expected
#  android.jar is absent. This touches the network, so it prints a clear notice.
#  Skippable implicitly: if the jar is already there, nothing happens.
# ----------------------------------------------------------------------------
# Determine the compile API level. The SDK pack folder name encodes it
# (Microsoft.Android.Sdk.Windows\36.1.69 -> 36); default to 36 if not found.
$apiLevel = 36
$sdkPackRoot = Join-Path $env:ProgramFiles 'dotnet\packs\Microsoft.Android.Sdk.Windows'
if (Test-Path $sdkPackRoot) {
    $newestPack = Get-ChildItem $sdkPackRoot -Directory -ErrorAction SilentlyContinue |
                  Sort-Object Name -Descending | Select-Object -First 1
    if ($newestPack -and $newestPack.Name -match '^(\d+)\.') {
        $apiLevel = [int]$Matches[1]
    }
}

$androidJar = Join-Path $androidSdk "platforms\android-$apiLevel\android.jar"
if (-not (Test-Path $androidJar)) {
    Write-Stage "Installing Android platform API $apiLevel (one-time, downloads from Google)"
    Write-Host "  Missing: $androidJar"
    Write-Host "  Running InstallAndroidDependencies (accepts SDK licenses)..."
    & dotnet build $Project -t:InstallAndroidDependencies -f $Framework `
        "-p:AndroidSdkDirectory=$androidSdk" `
        "-p:JavaSdkDirectory=$javaSdk" `
        "-p:AcceptAndroidSDKLicenses=true" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        $manualCmd = "  dotnet build -t:InstallAndroidDependencies -f $Framework ""-p:AndroidSdkDirectory=$androidSdk"" ""-p:AcceptAndroidSDKLicenses=true"" $Project"
        Fail ("Failed to install the Android API $apiLevel platform. You can run it manually:`n" + $manualCmd)
    }
    if (-not (Test-Path $androidJar)) {
        Write-Host "  (InstallAndroidDependencies ran but android.jar still absent; the" -ForegroundColor Yellow
        Write-Host "   build below will report the precise remaining gap.)" -ForegroundColor Yellow
    } else {
        Write-Host "  Android platform API $apiLevel installed." -ForegroundColor Green
    }
}
else {
    Write-Host "  Android platform: API $apiLevel present"
}

# ----------------------------------------------------------------------------
#  Optional clean.
# ----------------------------------------------------------------------------
if ($Clean) {
    Write-Stage 'Cleaning'
    & dotnet clean $Project -c $Configuration -f $Framework | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'Clean failed.' }
}

# ----------------------------------------------------------------------------
#  Sanity-build Core for net10 first. Core is multi-targeted (net8.0;net10.0);
#  building its net10 slice alone gives a fast, clear failure if the multi-
#  target is wrong, BEFORE the much longer Android build. Skippable with
#  -NoBuildCore once you trust it.
# ----------------------------------------------------------------------------
if (-not $NoBuildCore) {
    Write-Stage 'Building QuizBuilder.Core (net10.0 slice)'
    & dotnet build $CoreProject -c $Configuration -f net10.0 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Fail 'Core failed to build for net10.0. Fix this before building the Android app.'
    }
}

# ----------------------------------------------------------------------------
#  Resolve signing for Release. Detected, never required: absent keystore ->
#  debug-signed Release with a notice.
# ----------------------------------------------------------------------------
$signArgs = @()
$signed = $false

if ($Configuration -eq 'Release') {
    $ks    = $env:QB_KEYSTORE
    $ksPw  = $env:QB_KEYSTORE_PASS
    $alias = $env:QB_KEY_ALIAS
    $keyPw = if ($env:QB_KEY_PASS) { $env:QB_KEY_PASS } else { $ksPw }

    if ($ks -and $ksPw -and $alias) {
        if (-not (Test-Path $ks)) {
            Fail "QB_KEYSTORE is set to '$ks' but no file exists there."
        }
        $signArgs = @(
            '-p:AndroidKeyStore=true',
            "-p:AndroidSigningKeyStore=$ks",
            "-p:AndroidSigningStorePass=$ksPw",
            "-p:AndroidSigningKeyAlias=$alias",
            "-p:AndroidSigningKeyPass=$keyPw"
        )
        $signed = $true
        Write-Host ''
        Write-Host "  Signing         : release keystore detected ($alias)" -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host "  Signing         : NO keystore env vars -- Release will be DEBUG-SIGNED." -ForegroundColor Yellow
        Write-Host "                    Set QB_KEYSTORE, QB_KEYSTORE_PASS and QB_KEY_ALIAS to sign." -ForegroundColor Yellow
    }
}

# ----------------------------------------------------------------------------
#  Build / publish.
#    Debug   -> dotnet build. We set EmbedAssembliesIntoApk=true so the produced
#               APK is SELF-CONTAINED and can be installed with `adb install`
#               and launched directly. Without it, MAUI debug builds use "Fast
#               Deployment": the .NET assemblies are pushed to the device
#               SEPARATELY from the APK (into a .__override__ folder), so an
#               apk installed by hand launches, finds no assemblies, and aborts
#               with "No assemblies found ... Assuming this is part of Fast
#               Deployment. Exiting". Embedding trades a little rebuild speed for
#               an apk that actually runs when sideloaded -- the right default
#               for a script whose job is to hand you an installable file.
#    Release -> dotnet publish (trim + AOT; embeds assemblies already).
# ----------------------------------------------------------------------------
if ($Configuration -eq 'Debug') {
    Write-Stage 'Building the Android app (Debug APK, self-contained)'
    & dotnet build $Project -c Debug -f $Framework `
        -p:EmbedAssembliesIntoApk=true `
        @androidSdkArgs @javaSdkArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'Android build failed.' }
}
else {
    Write-Stage 'Publishing the Android app (Release APK + AAB)'
    & dotnet publish $Project -c Release -f $Framework @signArgs @androidSdkArgs @javaSdkArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'Android publish failed.' }
}

# ----------------------------------------------------------------------------
#  Locate and report the artifacts.
# ----------------------------------------------------------------------------
Write-Stage 'Artifacts'

$outputRoot = "QuizBuilder.Player/bin/$Configuration/$Framework"
$apks = @()
$aabs = @()
if (Test-Path $outputRoot) {
    $apks = Get-ChildItem -Path $outputRoot -Recurse -Filter '*.apk' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch '-Signed' -or $true } | Sort-Object LastWriteTime
    $aabs = Get-ChildItem -Path $outputRoot -Recurse -Filter '*.aab' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
}

if ($apks.Count -eq 0 -and $aabs.Count -eq 0) {
    Write-Host "  No .apk/.aab found under $outputRoot." -ForegroundColor Yellow
    Write-Host "  (The build reported success; check the output path above.)"
}
foreach ($f in $apks) { Write-Host "  APK  $($f.FullName)" -ForegroundColor Green }
foreach ($f in $aabs) { Write-Host "  AAB  $($f.FullName)" -ForegroundColor Green }

if ($Configuration -eq 'Release' -and -not $signed) {
    Write-Host ''
    Write-Host "  NOTE: this Release build is debug-signed and NOT suitable for the" -ForegroundColor Yellow
    Write-Host "        Play Store. Provide keystore env vars to produce a signed build." -ForegroundColor Yellow
}

# ----------------------------------------------------------------------------
#  Optional: install a Debug APK on a connected device via adb.
# ----------------------------------------------------------------------------
if ($Install) {
    Write-Stage 'Installing on a connected device (adb)'
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $adb) {
        Write-Host "  adb not on PATH; skipping install. The APK path is listed above." -ForegroundColor Yellow
    }
    elseif ($apks.Count -eq 0) {
        Write-Host "  No APK to install (Release produces an AAB for the store; use Debug to sideload)." -ForegroundColor Yellow
    }
    else {
        $target = $apks[-1].FullName
        Write-Host "  adb install -r `"$target`""
        & adb install -r $target | Out-Host
        if ($LASTEXITCODE -ne 0) { Fail 'adb install failed (is a device connected and USB debugging on?).' }
        Write-Host "  Installed." -ForegroundColor Green
    }
}

Write-Host ''
Write-Host "Done." -ForegroundColor Cyan
