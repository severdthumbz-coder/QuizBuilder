@echo off
REM ============================================================================
REM  Quiz Builder - portable build script
REM
REM  Usage:  build.bat [options]
REM
REM    --no-test      skip the test run
REM    --no-publish   build and test only, don't publish
REM    --quiet        don't prompt to launch at the end
REM
REM  Reads version.json, passes it to MSBuild, builds and tests, then publishes
REM  a self-contained single-file .exe of the WPF app (QuizBuilder.App) and zips
REM  that exe as QuizBuilder v<version>.zip.
REM ============================================================================

setlocal EnableDelayedExpansion

REM Run from the script's own directory so it works from anywhere.
pushd "%~dp0"

set "CONFIG=Release"
set "RUNTIME=win-x64"
set "APP_PROJECT=QuizBuilder.App\QuizBuilder.App.csproj"
set "TEST_PROJECT=QuizBuilder.Tests\QuizBuilder.Tests.csproj"
set "PUBLISH_DIR=publish"
set "DO_TEST=1"
set "DO_PUBLISH=1"
set "DO_PROMPT=1"

:parse_args
if "%~1"=="" goto args_done
if /i "%~1"=="--no-test"    set "DO_TEST=0"
if /i "%~1"=="--no-publish" set "DO_PUBLISH=0"
if /i "%~1"=="--quiet"      set "DO_PROMPT=0"
shift
goto parse_args
:args_done

REM ---------------------------------------------------------------------------
REM  Check the SDK is present before doing anything else. A clear message here
REM  beats a wall of "'dotnet' is not recognized".
REM ---------------------------------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] The .NET SDK was not found on PATH.
    echo         Install the .NET 8 SDK from https://dot.net
    goto fail
)

for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set "SDK_VERSION=%%v"
echo   SDK version    : !SDK_VERSION!

REM ---------------------------------------------------------------------------
REM  global.json asks for a .NET 8 SDK and rolls forward if none is installed.
REM  Projects target net8.0, which a newer SDK can build ONLY if the .NET 8
REM  targeting pack is present. If it is not, the failure is NETSDK1045 and
REM  the fix is to install the .NET 8 SDK -- so say so up front rather than
REM  letting MSBuild explain it in its own words.
REM ---------------------------------------------------------------------------
echo !SDK_VERSION! | findstr /b "8." >nul
if errorlevel 1 (
    echo.
    echo   [NOTE] This is not a .NET 8 SDK. The projects target net8.0.
    echo          That usually still works, but if the build fails with
    echo          NETSDK1045 or "targeting pack not found", install the
    echo          .NET 8 SDK from https://dot.net/download
    echo.
)

REM ---------------------------------------------------------------------------
REM  Read version.json.
REM
REM  Batch has no JSON parser, and shelling out to PowerShell for this would
REM  cost ~1s of startup. The file is ours and its shape is fixed, so a
REM  targeted token grab is safe here -- but it IS brittle: reformat
REM  version.json onto one line and this stops working. Hence the validation
REM  below rather than trusting the parse.
REM ---------------------------------------------------------------------------
set "V_MAJOR="
set "V_MINOR="
set "V_PATCH="
set "V_BUILD="

if not exist "version.json" (
    echo [ERROR] version.json not found. It is the central version source.
    goto fail
)

for /f "usebackq tokens=1,2 delims=:," %%a in ("version.json") do (
    set "key=%%~a"
    set "val=%%~b"
    REM Strip whitespace and quotes from the key.
    set "key=!key: =!"
    set "key=!key:"=!"
    set "val=!val: =!"

    if /i "!key!"=="major" set "V_MAJOR=!val!"
    if /i "!key!"=="minor" set "V_MINOR=!val!"
    if /i "!key!"=="patch" set "V_PATCH=!val!"
    if /i "!key!"=="build" set "V_BUILD=!val!"
)

if "!V_MAJOR!"=="" goto version_parse_failed
if "!V_MINOR!"=="" goto version_parse_failed
if "!V_PATCH!"=="" goto version_parse_failed
if "!V_BUILD!"=="" goto version_parse_failed

set "VERSION=!V_MAJOR!.!V_MINOR!.!V_PATCH!"
set "ASSEMBLY_VERSION=!VERSION!.!V_BUILD!"

REM Published filename carries the full 4-part version, e.g.
REM   "QuizBuilder v0.1.0.1.exe"
REM The assembly's internal name stays QuizBuilder.App: renaming the FILE is
REM safe because a single-file publish produces a self-contained bundle whose
REM filename is just a label, but renaming the ASSEMBLY would rewrite every
REM XAML pack URI ("/AssemblyName;component/...") to contain spaces, which is
REM a well-known source of resource-resolution failures.
set "EXE_NAME=QuizBuilder v!ASSEMBLY_VERSION!.exe"


echo.
echo ============================================================
echo   Quiz Builder v!VERSION! build !V_BUILD!
echo ============================================================
echo   SDK            : !SDK_VERSION!
echo   Configuration  : !CONFIG!
echo   Output         : !EXE_NAME!
echo.

REM ---------------------------------------------------------------------------
REM  Clean
REM ---------------------------------------------------------------------------
REM ---------------------------------------------------------------------------
REM  Running-instance check.
REM
REM  Must happen BEFORE clean: `rmdir /s /q publish` fails just as hard on a
REM  locked .exe, only silently. And since this script offers to launch the app
REM  at the end, it is the single most likely cause of its own next failure.
REM  Without this the user gets a 15-line MSBuild stack trace from
REM  GenerateBundle that never mentions closing the window.
REM ---------------------------------------------------------------------------
REM Wildcard, not a literal name: the published exe is version-stamped
REM ("QuizBuilder v0.1.0.1.exe"), so a literal match would silently stop
REM working on the next version bump and leave several stale copies running.
REM "QuizBuilder*" catches every version AND the older QuizBuilder.App.exe.
REM
REM Detection quirk: tasklist exits 0 even when the filter matches nothing --
REM it prints an INFO line instead. So the exit code is useless on its own and
REM the pipe to `find` is what actually detects. Searching for ".exe" works
REM because the INFO line contains no such string.
tasklist /fi "IMAGENAME eq QuizBuilder*" /nh 2>nul | find /i ".exe" >nul
if errorlevel 1 goto no_instance

echo   [warning] Quiz Builder is already running. Its .exe cannot be
echo             replaced while the process holds it open.
echo.

REM `choice` rather than `set /p`. set /p reads STDIN, and when this script is
REM launched from a PowerShell host the keystroke gets buffered by PowerShell
REM instead of reaching the child's stdin: set /p returns with the variable
REM unset, the script exits, and PowerShell then tries to run the buffered "y"
REM as its own command. choice reads the console directly.
choice /c YN /n /m "      Close it and continue? (Y/N) "

REM Descending order is mandatory: `if errorlevel 1` is true for ANY value >= 1,
REM so testing 1 first would treat N as Y. 255 (choice missing) falls into the
REM N branch, which is the safe default.
REM
REM Labels live at top level, never inside a parenthesised block: batch parses a
REM block as a single statement, so a jump out of one abandons it and any label
REM within is unreachable.
if errorlevel 2 goto abort_running
if errorlevel 1 goto kill_running
goto abort_running

:kill_running
REM /fi with a wildcard is the documented, reliable form; /im wildcard support
REM varies by Windows build. This kills every matching version.
taskkill /f /fi "IMAGENAME eq QuizBuilder*" >nul 2>&1
REM taskkill returns once the kill is signalled, not once the handle is
REM released. Pause briefly so the file is actually free.
ping -n 2 127.0.0.1 >nul 2>&1
echo       Closed.
echo.
goto no_instance

:abort_running
echo.
echo [ERROR] Cannot build while the application is running.
goto fail

:no_instance

REM ---------------------------------------------------------------------------
REM  Optional pre-build validation.
REM
REM  Catches failures a C# compiler either cannot see, or reports with a message
REM  that never names the real cause: a double-hyphen inside an XML comment
REM  (surfaces as MSB4025 during CLEAN, before the .csproj is even mentioned),
REM  duplicate XAML attributes, x:Class not matching its code-behind, pack URIs
REM  not matching AssemblyName, and DynamicResource keys with no registration
REM  (these fail SILENTLY at runtime, rendering as system defaults).
REM
REM  Two traps handled here:
REM
REM  1. Windows ships an App Execution Alias at python.exe that is NOT Python.
REM     It prints a Microsoft Store advert and exits non-zero. So `where python`
REM     finds a file that does not work. Each candidate is RUN instead, and only
REM     a clean exit counts.
REM
REM  2. No parenthesised for-loop. Inside a block, `if errorlevel` reads the
REM     value from before the block was entered, so a loop testing each
REM     interpreter would silently pick the wrong one.
REM
REM  This is a convenience, never a build dependency: any problem detecting or
REM  running it results in a skip, never a failure.
REM ---------------------------------------------------------------------------
set "PY_CMD="

REM Each candidate is tested at top level, never inside parentheses. Inside a
REM block, `if errorlevel` reads the value from BEFORE the block was entered,
REM so a version check followed by `if errorlevel` in the same block silently
REM tests the wrong thing. Flat statements with an explicit jump are clearer.
python --version >nul 2>&1
if not errorlevel 1 (
    set "PY_CMD=python"
    goto py_found
)

py --version >nul 2>&1
if not errorlevel 1 (
    set "PY_CMD=py"
    goto py_found
)

python3 --version >nul 2>&1
if not errorlevel 1 (
    set "PY_CMD=python3"
    goto py_found
)

:py_found

if not defined PY_CMD (
    echo   Validation     : skipped ^(no working Python found^)
    goto validated
)

if not exist "tools\validate.py" (
    echo   Validation     : skipped ^(tools\validate.py not found^)
    goto validated
)

call %PY_CMD% "tools\validate.py" >nul 2>&1
if errorlevel 1 goto validation_problems

echo   Validation     : passed
goto validated

:validation_problems
echo   Validation     : PROBLEMS FOUND ^(advisory, build continues^)
echo.
call %PY_CMD% "tools\validate.py"
echo.
echo   These checks are advisory, so the build will continue. Several of them
echo   catch failures that are silent at runtime, so fix them before shipping.
echo.

:validated
echo.

echo [1/5] Cleaning...
dotnet clean --configuration !CONFIG! --verbosity quiet --nologo
if errorlevel 1 (
    echo [ERROR] Clean failed.
    goto fail
)

REM  Preserve the user's own files before wiping the directory.
REM
REM  The app is portable: settings.json lives BESIDE the .exe, by design, never
REM  in %AppData%. But the .exe lives in publish\, so `rmdir /s /q publish` was
REM  deleting the user's settings, GitHub token, recent files and custom theme
REM  on EVERY build. Nothing reported it; the app just came back factory-fresh.
REM
REM  Quizzes count too: a portable app invites saving .qbx files next to it.
REM
REM  This is copy-out/copy-back rather than a selective delete because a clean
REM  should genuinely empty the directory -- an allowlist of "things the build
REM  produces" drifts every time the output changes.
REM  Beside the repo, not %TEMP%: if this script dies mid-build, the user's
REM  settings are somewhere they can actually find them. A random %TEMP%
REM  subdirectory would be a data-loss report waiting to happen.
set "PRESERVE_DIR=.build-preserve"
set "PRESERVED=0"

REM  A leftover .build-preserve means a previous run died between the clean and
REM  the restore. Those files are the user's, and they are NEWER than nothing --
REM  put them back first, then carry on. Wiping them here would complete the
REM  data loss the crash only started.
if exist "!PRESERVE_DIR!" (
    echo       [notice] Recovering settings from an interrupted build...
    if not exist "!PUBLISH_DIR!" mkdir "!PUBLISH_DIR!" 2>nul
    copy /y "!PRESERVE_DIR!\settings.json" "!PUBLISH_DIR!\" >nul 2>&1
    copy /y "!PRESERVE_DIR!\*.qbx" "!PUBLISH_DIR!\" >nul 2>&1
    rmdir /s /q "!PRESERVE_DIR!" 2>nul
)

if exist "!PUBLISH_DIR!" (
    if exist "!PUBLISH_DIR!\settings.json" set "PRESERVED=1"
    if exist "!PUBLISH_DIR!\*.qbx" set "PRESERVED=1"

    if "!PRESERVED!"=="1" (
        mkdir "!PRESERVE_DIR!" 2>nul
        copy /y "!PUBLISH_DIR!\settings.json" "!PRESERVE_DIR!\" >nul 2>&1
        copy /y "!PUBLISH_DIR!\*.qbx" "!PRESERVE_DIR!\" >nul 2>&1
        echo       preserving settings.json and any saved quizzes...
    )
)

if exist "!PUBLISH_DIR!" rmdir /s /q "!PUBLISH_DIR!"
echo       done.
echo.

REM ---------------------------------------------------------------------------
REM  Build
REM ---------------------------------------------------------------------------
echo [2/5] Building...
dotnet build --configuration !CONFIG! --nologo ^
    -p:Version=!VERSION! ^
    -p:AssemblyVersion=!ASSEMBLY_VERSION! ^
    -p:FileVersion=!ASSEMBLY_VERSION! ^
    -p:InformationalVersion=!VERSION!+build.!V_BUILD!
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the errors above.
    goto fail
)
echo       done.
echo.

REM ---------------------------------------------------------------------------
REM  Test
REM ---------------------------------------------------------------------------
if "!DO_TEST!"=="0" (
    echo [3/5] Tests skipped ^(--no-test^).
    echo.
    goto after_tests
)

if not exist "!TEST_PROJECT!" (
    echo [3/5] No test project found, skipping.
    echo.
    goto after_tests
)

echo [3/5] Running tests...
dotnet test "!TEST_PROJECT!" --configuration !CONFIG! --no-build --nologo --verbosity minimal
if errorlevel 1 (
    echo.
    echo [ERROR] Tests failed. Build stopped.
    echo         Publishing a build with failing tests would defeat the point.
    goto fail
)
echo       done.
echo.

:after_tests

REM ---------------------------------------------------------------------------
REM  Publish
REM
REM  Gated on the App project existing. Right now it does not -- the WPF host
REM  is a later slice -- so this is skipped with a notice rather than failing.
REM ---------------------------------------------------------------------------
if "!DO_PUBLISH!"=="0" (
    echo [4/5] Publish skipped ^(--no-publish^).
    goto success_no_exe
)

if not exist "!APP_PROJECT!" (
    echo [4/5] Publish skipped: !APP_PROJECT! does not exist yet.
    echo       The WPF host arrives in a later slice; the Core library and
    echo       its tests are what this build covers for now.
    goto success_no_exe
)

echo [4/5] Publishing portable single-file exe...

dotnet publish "!APP_PROJECT!" ^
    --configuration !CONFIG! ^
    --runtime !RUNTIME! ^
    --self-contained true ^
    --output "!PUBLISH_DIR!" ^
    --nologo ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:Version=!VERSION! ^
    -p:AssemblyVersion=!ASSEMBLY_VERSION! ^
    -p:FileVersion=!ASSEMBLY_VERSION! ^
    -p:InformationalVersion=!VERSION!+build.!V_BUILD!
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed.
    goto fail
)
echo       done.
echo.

REM MSBuild always emits QuizBuilder.App.exe (the assembly name). Rename it
REM to the version-stamped filename here rather than overriding AssemblyName,
REM which would put spaces into every XAML pack URI.
set "BUILT_EXE=!PUBLISH_DIR!\QuizBuilder.App.exe"
set "EXE_PATH=!PUBLISH_DIR!\!EXE_NAME!"

if not exist "!BUILT_EXE!" (
    echo [WARN] Publish reported success but !BUILT_EXE! is missing.
    goto success_no_exe
)

if exist "!EXE_PATH!" del /q "!EXE_PATH!"
ren "!BUILT_EXE!" "!EXE_NAME!"
if errorlevel 1 (
    echo [WARN] Could not rename the executable to !EXE_NAME!
    set "EXE_PATH=!BUILT_EXE!"
)

REM ---------------------------------------------------------------------------
REM  Package
REM
REM  Zip ONLY the version-stamped exe we just built -- not the whole publish\
REM  folder. Two reasons, both learned the hard way:
REM
REM    1. Stale/locked leftovers. Old version-stamped exes from previous builds
REM       can linger in publish\ (a running instance defeats `rmdir /s /q`, which
REM       fails silently on a locked file). Zipping publish\* then chokes on the
REM       locked file and the whole package step fails -- even though THIS build
REM       succeeded. Naming the single current exe sidesteps every leftover.
REM
REM    2. No accidental data in the distributable. publish\ also holds the user's
REM       settings.json (with their encrypted GitHub token) and any .qbx they
REM       saved beside the app. Packaging just the exe means those can never end
REM       up in a zip handed to someone else, regardless of restore ordering.
REM
REM  The exe is a self-contained single-file publish, so it IS the whole app --
REM  nothing else in publish\ is needed in the archive.
REM
REM  Uses PowerShell's Compress-Archive rather than tar.exe. Windows does ship
REM  bsdtar (10 1803+), whose -a flag infers zip from the extension, but GNU
REM  tar silently ignores -a and writes a TAR file with a .zip name instead.
REM  Since that failure is silent and produces a corrupt-looking archive,
REM  Compress-Archive is the safer call: it is present on every Windows that
REM  can run this script, and it cannot be mistaken for another format.
REM ---------------------------------------------------------------------------
set "ZIP_NAME=QuizBuilder v!ASSEMBLY_VERSION!.zip"
set "ZIP_PATH=!ZIP_NAME!"

echo [5/5] Packaging !ZIP_NAME! ...

if exist "!ZIP_PATH!" del /q "!ZIP_PATH!"

powershell -NoProfile -NonInteractive -Command ^
    "$ErrorActionPreference='Stop'; Compress-Archive -Path '!EXE_PATH!' -DestinationPath '!ZIP_PATH!' -Force"
if errorlevel 1 (
    echo [WARN] Could not create the zip archive.
    echo        The executable is still available in !PUBLISH_DIR!\
    goto zip_skipped
)

echo       done.
echo.

:zip_skipped
echo ============================================================
REM  Restore the user's files here: AFTER the zip, BEFORE the launch prompt.
REM
REM  Both halves of that are load-bearing.
REM
REM  After the zip, as defence in depth. The package step now zips only the
REM  version-stamped exe, so the user's settings.json (encrypted GitHub token,
REM  recent-files list) and saved .qbx files can no longer reach the
REM  distributable even if restored first. Restoring after the zip anyway keeps
REM  the guarantee robust against a future change back to a folder-wide archive.
REM
REM  Before the launch prompt, because `start` returns immediately: restoring at
REM  :done would race the app reading its own settings.json.
REM
REM  If you move this call, check both constraints still hold.
call :restore_user_files

echo   BUILD SUCCEEDED
echo ============================================================
echo   Version : !VERSION! ^(build !V_BUILD!^)
echo   Output  : !EXE_PATH!
if exist "!ZIP_PATH!" echo   Package : !ZIP_PATH!
if "!PRESERVED!"=="1" echo   Settings: preserved
echo.

if "!DO_PROMPT!"=="0" goto done

REM ---------------------------------------------------------------------------
REM  Launch prompt. Uses choice, which reads the console directly.
REM  A missing choice.exe or Ctrl+Break both fall through to not launching.
REM
REM  Note: launching here is what makes the next run hit a locked .exe. The
REM  running-instance check at the top of this script handles that, so the
REM  cycle closes cleanly rather than surfacing as an MSBuild stack trace.
REM ---------------------------------------------------------------------------
choice /c YN /n /m "Would you like to open the application? (Y/N) "

REM Descending: errorlevel 2 = N, 1 = Y. Anything else (255 if choice is
REM missing, 0 on Ctrl+Break) falls through to not launching, which is the
REM safe default.
if errorlevel 2 goto done
if errorlevel 1 goto launch_app
goto done

:launch_app
echo Launching...
start "" "!EXE_PATH!"
goto done

:success_no_exe
echo ============================================================
echo   BUILD SUCCEEDED ^(no executable produced^)
echo ============================================================
echo   Version : !VERSION! ^(build !V_BUILD!^)
echo.
goto done

:version_parse_failed
echo [ERROR] Could not read version numbers from version.json.
echo         Expected major/minor/patch/build each on their own line.
echo         If the file was reformatted, restore the one-key-per-line layout.
goto fail

:restore_user_files
REM  Put the user's files back.
REM
REM  Called from BOTH exit paths. Batch has no try/finally, so a build that
REM  fails AFTER the clean would otherwise leave the settings in %TEMP% and the
REM  publish directory gone -- the user's data would depend on the build
REM  succeeding, which is exactly backwards.
REM  Idempotent by design: this is called once when the publish directory is
REM  final and again at the exit label, so the second call must do nothing.
REM  Clearing PRESERVED after a successful restore is what guarantees that.
if not "!PRESERVED!"=="1" goto :eof
if not exist "!PRESERVE_DIR!" goto :eof

if not exist "!PUBLISH_DIR!" mkdir "!PUBLISH_DIR!" 2>nul

copy /y "!PRESERVE_DIR!\settings.json" "!PUBLISH_DIR!\" >nul 2>&1
copy /y "!PRESERVE_DIR!\*.qbx" "!PUBLISH_DIR!\" >nul 2>&1

rmdir /s /q "!PRESERVE_DIR!" 2>nul
set "PRESERVED=0"
goto :eof

:fail
echo.
call :restore_user_files
popd
endlocal
exit /b 1

:done
call :restore_user_files
popd
endlocal
exit /b 0
