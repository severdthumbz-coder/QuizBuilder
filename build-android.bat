@echo off
REM ============================================================================
REM  Quiz Player - Android build (batch wrapper)
REM
REM  Thin wrapper over build-android.ps1 so there is ONE source of truth for the
REM  build logic. Forwards common flags:
REM
REM    build-android.bat                 Debug APK (does NOT install; prints path)
REM    build-android.bat release         Release APK + AAB (needs keystore)
REM    build-android.bat install         Debug APK, then adb-install it
REM    build-android.bat launch          Debug APK, install, then launch the app
REM    build-android.bat launch device=emulator-5554   ...on a specific device
REM    build-android.bat release install Release, then adb-install the APK
REM    build-android.bat clean           Clean first, then Debug build
REM
REM  Any of: release | install | launch | clean | nocore | device=<serial>  may
REM  be combined in any order. 'launch' implies 'install'. 'device=<serial>'
REM  targets one adb device (needed when more than one is connected).
REM  Release signing is picked up from the QB_KEYSTORE* environment variables
REM  (see build-android.ps1 for the full list).
REM ============================================================================

setlocal EnableDelayedExpansion
pushd "%~dp0"

set "CONFIG=Debug"
set "PS_ARGS="

:parse
if "%~1"=="" goto run
if /i "%~1"=="release" set "CONFIG=Release"
if /i "%~1"=="install" set "PS_ARGS=!PS_ARGS! -Install"
if /i "%~1"=="launch"  set "PS_ARGS=!PS_ARGS! -Launch"
if /i "%~1"=="clean"   set "PS_ARGS=!PS_ARGS! -Clean"
if /i "%~1"=="nocore"  set "PS_ARGS=!PS_ARGS! -NoBuildCore"
REM  device=<serial> selects a specific adb target (e.g. device=emulator-5554).
for /f "tokens=1,2 delims==" %%a in ("%~1") do (
    if /i "%%a"=="device" set "PS_ARGS=!PS_ARGS! -Device %%b"
)
shift
goto parse

:run
REM Prefer PowerShell 7+ (pwsh) if present, else Windows PowerShell.
where pwsh >nul 2>&1
if %errorlevel%==0 (
    set "PWSH=pwsh"
) else (
    set "PWSH=powershell"
)

echo Running: !PWSH! build-android.ps1 -Configuration %CONFIG%!PS_ARGS!
!PWSH! -NoProfile -ExecutionPolicy Bypass -File "build-android.ps1" -Configuration %CONFIG%!PS_ARGS!
set "EXITCODE=!errorlevel!"

popd
endlocal & exit /b %EXITCODE%
