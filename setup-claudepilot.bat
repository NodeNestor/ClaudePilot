@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   ClaudePilot KSP Test Instance Setup
echo ============================================
echo.

set "SCRIPT_DIR=%~dp0"
set "KSP_STEAM="
set "KSP_MODDED="
set "CKAN_URL=https://github.com/KSP-CKAN/CKAN/releases/latest/download/ckan.exe"

:: =============================================
:: STEP 0: FIND VANILLA KSP
:: =============================================
echo [0/6] Looking for KSP install...

for %%D in (
    "E:\SteamLibrary\steamapps\common\Kerbal Space Program"
    "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
    "C:\Program Files\Steam\steamapps\common\Kerbal Space Program"
    "D:\SteamLibrary\steamapps\common\Kerbal Space Program"
    "D:\Steam\steamapps\common\Kerbal Space Program"
    "E:\Steam\steamapps\common\Kerbal Space Program"
    "F:\SteamLibrary\steamapps\common\Kerbal Space Program"
) do (
    if exist "%%~D\KSP_x64.exe" (
        set "KSP_STEAM=%%~D"
        echo    Found KSP at: %%~D
        goto :found_ksp
    )
)

echo    Could not find KSP automatically.
set /p "KSP_STEAM=   Enter KSP path: "
set "KSP_STEAM=%KSP_STEAM:"=%"

if not exist "%KSP_STEAM%\KSP_x64.exe" (
    echo    ERROR: KSP_x64.exe not found!
    pause
    exit /b 1
)

:found_ksp
for %%I in ("%KSP_STEAM%\..") do set "KSP_PARENT=%%~fI"
set "KSP_MODDED=%KSP_PARENT%\KSP ClaudePilot"
set "CKAN_EXE=%KSP_MODDED%\ckan.exe"

echo    Install to: %KSP_MODDED%
echo.

:: =============================================
:: STEP 1: COPY KSP
:: =============================================
echo [1/6] Creating KSP copy...
if exist "%KSP_MODDED%\KSP_x64.exe" (
    echo    Copy already exists, skipping.
) else (
    echo    Copying KSP... (this takes a minute)
    robocopy "%KSP_STEAM%" "%KSP_MODDED%" /E /NFL /NDL /NJH /NJS /NC /NS >nul 2>&1
    set "RC=!ERRORLEVEL!"
    if !RC! GEQ 8 (
        echo    robocopy failed, trying xcopy...
        xcopy "%KSP_STEAM%" "%KSP_MODDED%" /E /I /H /Y /Q >nul 2>&1
    )
)

if not exist "%KSP_MODDED%\KSP_x64.exe" (
    echo    ERROR: Copy failed!
    pause
    exit /b 1
)
echo    Done!
echo.

:: =============================================
:: STEP 2: DOWNLOAD CKAN + INSTALL MECHJEB
:: =============================================
echo [2/6] Setting up CKAN...
if not exist "%CKAN_EXE%" (
    echo    Downloading CKAN...
    powershell -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%CKAN_URL%' -OutFile '%CKAN_EXE%'" 2>nul
)

if not exist "%CKAN_EXE%" (
    echo    ERROR: CKAN download failed!
    pause
    exit /b 1
)

echo    Registering instance...
"%CKAN_EXE%" instance add "KSP-ClaudePilot" "%KSP_MODDED%" 2>nul
"%CKAN_EXE%" instance default "KSP-ClaudePilot" 2>nul
"%CKAN_EXE%" update
echo.

echo [3/6] Installing MechJeb2...
"%CKAN_EXE%" install --headless --no-recommends MechJeb2
echo    MechJeb2 installed!
echo.

:: =============================================
:: STEP 3: BUILD AND DEPLOY CLAUDEPILOT
:: =============================================
echo [4/6] Building ClaudePilot...

:: Try to find MSBuild or dotnet
set "MSBUILD="
for %%M in (
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
) do (
    if exist "%%~M" (
        set "MSBUILD=%%~M"
        goto :found_msbuild
    )
)

:: Try dotnet msbuild
where dotnet >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo    Using dotnet msbuild...
    pushd "%SCRIPT_DIR%"
    dotnet msbuild ClaudePilot.csproj /p:Configuration=Release /p:ReferencePath="%KSP_MODDED%\KSP_x64_Data\Managed" /nologo /v:minimal
    popd
    goto :after_build
)

echo    WARNING: No MSBuild or dotnet found. Cannot build automatically.
echo    Build ClaudePilot.dll manually and place it in:
echo    %KSP_MODDED%\GameData\ClaudePilot\
goto :deploy_mod

:found_msbuild
echo    Using MSBuild: %MSBUILD%
pushd "%SCRIPT_DIR%"
"%MSBUILD%" ClaudePilot.csproj /p:Configuration=Release /p:ReferencePath="%KSP_MODDED%\KSP_x64_Data\Managed" /nologo /v:minimal
popd

:after_build
if exist "%SCRIPT_DIR%bin\Release\ClaudePilot.dll" (
    echo    Build successful!
) else if exist "%SCRIPT_DIR%bin\Debug\ClaudePilot.dll" (
    echo    Build successful (Debug)!
) else (
    echo    WARNING: Build may have failed. Check output above.
)

:deploy_mod
echo.
echo [5/6] Deploying ClaudePilot mod...

:: Create GameData folder structure
if not exist "%KSP_MODDED%\GameData\ClaudePilot" mkdir "%KSP_MODDED%\GameData\ClaudePilot"

:: Copy DLL
if exist "%SCRIPT_DIR%bin\Release\ClaudePilot.dll" (
    copy /Y "%SCRIPT_DIR%bin\Release\ClaudePilot.dll" "%KSP_MODDED%\GameData\ClaudePilot\" >nul
    echo    Copied ClaudePilot.dll (Release)
) else if exist "%SCRIPT_DIR%bin\Debug\ClaudePilot.dll" (
    copy /Y "%SCRIPT_DIR%bin\Debug\ClaudePilot.dll" "%KSP_MODDED%\GameData\ClaudePilot\" >nul
    echo    Copied ClaudePilot.dll (Debug)
) else (
    echo    WARNING: No ClaudePilot.dll found to deploy.
    echo    Build the project first, then copy ClaudePilot.dll to:
    echo    %KSP_MODDED%\GameData\ClaudePilot\
)

:: Copy config
if exist "%SCRIPT_DIR%GameData\ClaudePilot\config.cfg" (
    if not exist "%KSP_MODDED%\GameData\ClaudePilot\config.cfg" (
        copy /Y "%SCRIPT_DIR%GameData\ClaudePilot\config.cfg" "%KSP_MODDED%\GameData\ClaudePilot\" >nul
        echo    Copied config.cfg
    ) else (
        echo    config.cfg already exists, not overwriting (preserves API key)
    )
)
echo.

:: =============================================
:: STEP 6: ADD TO STEAM
:: =============================================
echo [6/6] Adding to Steam...

set "STEAM_DIR="
for %%D in (
    "C:\Program Files (x86)\Steam"
    "C:\Program Files\Steam"
    "D:\Steam"
    "E:\Steam"
) do (
    if exist "%%~D\steam.exe" (
        set "STEAM_DIR=%%~D"
        goto :found_steam
    )
)

for /f "tokens=2*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul') do (
    set "STEAM_DIR=%%B"
    set "STEAM_DIR=!STEAM_DIR:/=\!"
)

:found_steam
if not defined STEAM_DIR (
    echo    Could not find Steam, skipping.
    goto :done
)

set "STEAM_USER_DIR="
for /d %%U in ("%STEAM_DIR%\userdata\*") do (
    if not "%%~nxU"=="0" (
        set "STEAM_USER_DIR=%%U"
        goto :found_user
    )
)
:found_user

if not defined STEAM_USER_DIR (
    echo    Could not find Steam user folder, skipping.
    goto :done
)

set "SHORTCUTS_VDF=%STEAM_USER_DIR%\config\shortcuts.vdf"
set "KSP_EXE=%KSP_MODDED%\KSP_x64.exe"

powershell -Command ^
    "$vdfPath = '%SHORTCUTS_VDF%';" ^
    "$exePath = '%KSP_EXE%';" ^
    "$startDir = '%KSP_MODDED%';" ^
    "$appName = 'KSP ClaudePilot';" ^
    "" ^
    "$ms = New-Object System.IO.MemoryStream;" ^
    "$bw = New-Object System.IO.BinaryWriter($ms);" ^
    "" ^
    "function Write-VdfString($bw, $key, $val) {" ^
    "    $bw.Write([byte]1);" ^
    "    $bw.Write([System.Text.Encoding]::UTF8.GetBytes($key + [char]0));" ^
    "    $bw.Write([System.Text.Encoding]::UTF8.GetBytes($val + [char]0));" ^
    "}" ^
    "" ^
    "function Write-VdfInt($bw, $key, $val) {" ^
    "    $bw.Write([byte]2);" ^
    "    $bw.Write([System.Text.Encoding]::UTF8.GetBytes($key + [char]0));" ^
    "    $bw.Write([BitConverter]::GetBytes([int]$val));" ^
    "}" ^
    "" ^
    "$nextIdx = 0;" ^
    "if (Test-Path $vdfPath) {" ^
    "    $existing = [System.IO.File]::ReadAllBytes($vdfPath);" ^
    "    $content = [System.Text.Encoding]::UTF8.GetString($existing);" ^
    "    if ($content -match 'KSP ClaudePilot') {" ^
    "        Write-Host '   Already added to Steam!';" ^
    "        exit 0;" ^
    "    }" ^
    "    $matches2 = [regex]::Matches($content, '\x00(\d+)\x00');" ^
    "    foreach ($m in $matches2) { $idx = [int]$m.Groups[1].Value; if ($idx -ge $nextIdx) { $nextIdx = $idx + 1 } };" ^
    "    $trimCount = 0;" ^
    "    for ($i = $existing.Length - 1; $i -ge 0; $i--) {" ^
    "        if ($existing[$i] -eq 8) { $trimCount++ } else { break }" ^
    "    };" ^
    "    if ($trimCount -ge 2) {" ^
    "        $ms.Write($existing, 0, $existing.Length - $trimCount);" ^
    "    } else {" ^
    "        $ms.Write($existing, 0, $existing.Length);" ^
    "    }" ^
    "} else {" ^
    "    $bw.Write([byte]0);" ^
    "    $bw.Write([System.Text.Encoding]::UTF8.GetBytes('shortcuts' + [char]0));" ^
    "}" ^
    "" ^
    "$bw.Write([byte]0);" ^
    "$bw.Write([System.Text.Encoding]::UTF8.GetBytes($nextIdx.ToString() + [char]0));" ^
    "" ^
    "Write-VdfInt $bw 'appid' (-$nextIdx - 1);" ^
    "Write-VdfString $bw 'AppName' $appName;" ^
    "Write-VdfString $bw 'Exe' ('\"' + $exePath + '\"');" ^
    "Write-VdfString $bw 'StartDir' ('\"' + $startDir + '\"');" ^
    "Write-VdfString $bw 'icon' '';" ^
    "Write-VdfString $bw 'ShortcutPath' '';" ^
    "Write-VdfString $bw 'LaunchOptions' '';" ^
    "Write-VdfInt $bw 'IsHidden' 0;" ^
    "Write-VdfInt $bw 'AllowDesktopConfig' 1;" ^
    "Write-VdfInt $bw 'AllowOverlay' 1;" ^
    "Write-VdfInt $bw 'OpenVR' 0;" ^
    "Write-VdfInt $bw 'LastPlayTime' 0;" ^
    "" ^
    "$bw.Write([byte]0);" ^
    "$bw.Write([System.Text.Encoding]::UTF8.GetBytes('tags' + [char]0));" ^
    "$bw.Write([byte]8); $bw.Write([byte]8);" ^
    "" ^
    "$bw.Write([byte]8); $bw.Write([byte]8);" ^
    "" ^
    "$bw.Flush();" ^
    "[System.IO.File]::WriteAllBytes($vdfPath, $ms.ToArray());" ^
    "$bw.Close(); $ms.Close();" ^
    "Write-Host '   Added \"KSP ClaudePilot\" to Steam!';" ^
    "Write-Host '   Restart Steam to see it.';"

:done
echo.
echo ============================================
echo        CLAUDEPILOT SETUP COMPLETE!
echo ============================================
echo.
echo   KSP Instance:  %KSP_MODDED%
echo   Launch:        %KSP_MODDED%\KSP_x64.exe
echo   Steam:         Look for "KSP ClaudePilot" in your library
echo.
echo   BEFORE FIRST USE:
echo   1. Edit config.cfg and add your Claude API key:
echo      %KSP_MODDED%\GameData\ClaudePilot\config.cfg
echo   2. Launch KSP, start/load a game
echo   3. Press Alt+C to open ClaudePilot chat
echo   4. Try: "Take me to the Mun and back"
echo.
pause
