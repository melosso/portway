@echo off
setlocal enabledelayedexpansion

:: Scalar Configuration Script for Portway API
:: This script allows you to modify Scalar UI settings in appsettings.json

echo ========================================
echo Portway API - Scalar Configuration Tool
echo ========================================
echo.

:: Set the path to appsettings.json
set "APPSETTINGS_PATH=..\..\..\appsettings.json"
set "SOURCE_APPSETTINGS_PATH=..\..\appsettings.json"

:: Check if deployment appsettings.json exists
if exist "%APPSETTINGS_PATH%" (
    set "TARGET_FILE=%APPSETTINGS_PATH%"
    echo Using deployment configuration: %APPSETTINGS_PATH%
) else if exist "%SOURCE_APPSETTINGS_PATH%" (
    set "TARGET_FILE=%SOURCE_APPSETTINGS_PATH%"
    echo Using source configuration: %SOURCE_APPSETTINGS_PATH%
) else (
    echo Error: Could not find appsettings.json file
    echo Checked:
    echo   - %APPSETTINGS_PATH%
    echo   - %SOURCE_APPSETTINGS_PATH%
    pause
    exit /b 1
)

echo.

:: Create backup
set "BACKUP_FILE=%TARGET_FILE%.backup_%date:~10,4%%date:~4,2%%date:~7,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
set "BACKUP_FILE=%BACKUP_FILE: =0%"
copy "%TARGET_FILE%" "%BACKUP_FILE%" >nul 2>&1
if %errorlevel% equ 0 (
    echo Backup created: %BACKUP_FILE%
) else (
    echo Warning: Could not create backup file
)

echo.

:MAIN_MENU
echo Available Scalar Configuration Options:
echo.
echo 1. Change Theme
echo 2. Change Layout
echo 3. Toggle Sidebar Visibility
echo 4. Toggle Download Button
echo 5. Toggle Models Section
echo 6. Toggle Client Button
echo 7. Toggle Test Request Button
echo 8. View Current Configuration
echo 9. Reset to Defaults
echo 10. Exit
echo.
set /p "CHOICE=Select an option (1-10): "

if "%CHOICE%"=="1" goto CHANGE_THEME
if "%CHOICE%"=="2" goto CHANGE_LAYOUT
if "%CHOICE%"=="3" goto TOGGLE_SIDEBAR
if "%CHOICE%"=="4" goto TOGGLE_DOWNLOAD
if "%CHOICE%"=="5" goto TOGGLE_MODELS
if "%CHOICE%"=="6" goto TOGGLE_CLIENT
if "%CHOICE%"=="7" goto TOGGLE_TEST_REQUEST
if "%CHOICE%"=="8" goto VIEW_CONFIG
if "%CHOICE%"=="9" goto RESET_DEFAULTS
if "%CHOICE%"=="10" goto EXIT

echo Invalid choice. Please select 1-10.
echo.
goto MAIN_MENU

:CHANGE_THEME
echo.
echo Available Themes:
echo.
echo 1. default
echo 2. alternate
echo 3. moon
echo 4. purple
echo 5. solarized
echo 6. bluePlanet
echo 7. saturn
echo 8. kepler
echo 9. mars
echo 10. deepSpace
echo 11. elysiajs
echo 12. fastify
echo 13. laserwave
echo 14. none
echo.
set /p "THEME_CHOICE=Select theme (1-14): "

set "NEW_THEME="
if "%THEME_CHOICE%"=="1" set "NEW_THEME=default"
if "%THEME_CHOICE%"=="2" set "NEW_THEME=alternate"
if "%THEME_CHOICE%"=="3" set "NEW_THEME=moon"
if "%THEME_CHOICE%"=="4" set "NEW_THEME=purple"
if "%THEME_CHOICE%"=="5" set "NEW_THEME=solarized"
if "%THEME_CHOICE%"=="6" set "NEW_THEME=bluePlanet"
if "%THEME_CHOICE%"=="7" set "NEW_THEME=saturn"
if "%THEME_CHOICE%"=="8" set "NEW_THEME=kepler"
if "%THEME_CHOICE%"=="9" set "NEW_THEME=mars"
if "%THEME_CHOICE%"=="10" set "NEW_THEME=deepSpace"
if "%THEME_CHOICE%"=="11" set "NEW_THEME=elysiajs"
if "%THEME_CHOICE%"=="12" set "NEW_THEME=fastify"
if "%THEME_CHOICE%"=="13" set "NEW_THEME=laserwave"
if "%THEME_CHOICE%"=="14" set "NEW_THEME=none"

if "%NEW_THEME%"=="" (
    echo Invalid theme choice.
    echo.
    goto MAIN_MENU
)

call :UPDATE_JSON "ScalarTheme" "%NEW_THEME%"
echo Theme updated to: %NEW_THEME%
echo.
goto MAIN_MENU

:CHANGE_LAYOUT
echo.
echo Available Layouts:
echo.
echo 1. modern
echo 2. classic
echo.
set /p "LAYOUT_CHOICE=Select layout (1-2): "

set "NEW_LAYOUT="
if "%LAYOUT_CHOICE%"=="1" set "NEW_LAYOUT=modern"
if "%LAYOUT_CHOICE%"=="2" set "NEW_LAYOUT=classic"

if "%NEW_LAYOUT%"=="" (
    echo Invalid layout choice.
    echo.
    goto MAIN_MENU
)

call :UPDATE_JSON "ScalarLayout" "%NEW_LAYOUT%"
echo Layout updated to: %NEW_LAYOUT%
echo.
goto MAIN_MENU

:TOGGLE_SIDEBAR
call :GET_CURRENT_VALUE "ScalarShowSidebar"
if "!CURRENT_VALUE!"=="true" (
    call :UPDATE_JSON "ScalarShowSidebar" "false"
    echo Sidebar visibility set to: false
) else (
    call :UPDATE_JSON "ScalarShowSidebar" "true"
    echo Sidebar visibility set to: true
)
echo.
goto MAIN_MENU

:TOGGLE_DOWNLOAD
call :GET_CURRENT_VALUE "ScalarHideDownloadButton"
if "!CURRENT_VALUE!"=="true" (
    call :UPDATE_JSON "ScalarHideDownloadButton" "false"
    echo Download button is now: visible
) else (
    call :UPDATE_JSON "ScalarHideDownloadButton" "true"
    echo Download button is now: hidden
)
echo.
goto MAIN_MENU

:TOGGLE_MODELS
call :GET_CURRENT_VALUE "ScalarHideModels"
if "!CURRENT_VALUE!"=="true" (
    call :UPDATE_JSON "ScalarHideModels" "false"
    echo Models section is now: visible
) else (
    call :UPDATE_JSON "ScalarHideModels" "true"
    echo Models section is now: hidden
)
echo.
goto MAIN_MENU

:TOGGLE_CLIENT
call :GET_CURRENT_VALUE "ScalarHideClientButton"
if "!CURRENT_VALUE!"=="true" (
    call :UPDATE_JSON "ScalarHideClientButton" "false"
    echo Client button is now: visible
) else (
    call :UPDATE_JSON "ScalarHideClientButton" "true"
    echo Client button is now: hidden
)
echo.
goto MAIN_MENU

:TOGGLE_TEST_REQUEST
call :GET_CURRENT_VALUE "ScalarHideTestRequestButton"
if "!CURRENT_VALUE!"=="true" (
    call :UPDATE_JSON "ScalarHideTestRequestButton" "false"
    echo Test Request button is now: visible
) else (
    call :UPDATE_JSON "ScalarHideTestRequestButton" "true"
    echo Test Request button is now: hidden
)
echo.
goto MAIN_MENU

:VIEW_CONFIG
echo.
echo Current Scalar Configuration:
echo ============================
call :GET_CURRENT_VALUE "ScalarTheme"
echo Theme: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarLayout"
echo Layout: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarShowSidebar"
echo Show Sidebar: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarHideDownloadButton"
echo Hide Download Button: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarHideModels"
echo Hide Models: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarHideClientButton"
echo Hide Client Button: !CURRENT_VALUE!
call :GET_CURRENT_VALUE "ScalarHideTestRequestButton"
echo Hide Test Request Button: !CURRENT_VALUE!
echo.
goto MAIN_MENU

:RESET_DEFAULTS
echo.
echo Resetting Scalar settings to defaults...
call :UPDATE_JSON "ScalarTheme" "default"
call :UPDATE_JSON "ScalarLayout" "modern"
call :UPDATE_JSON "ScalarShowSidebar" "true"
call :UPDATE_JSON "ScalarHideDownloadButton" "true"
call :UPDATE_JSON "ScalarHideModels" "true"
call :UPDATE_JSON "ScalarHideClientButton" "true"
call :UPDATE_JSON "ScalarHideTestRequestButton" "false"
echo.
echo All Scalar settings have been reset to defaults.
echo.
goto MAIN_MENU

:UPDATE_JSON
:: Function to update JSON values
:: %1 = property name, %2 = new value
set "PROPERTY=%~1"
set "VALUE=%~2"

:: Use PowerShell to update the JSON file
powershell -Command ^
    "$json = Get-Content '%TARGET_FILE%' | ConvertFrom-Json; " ^
    "if ($json.Swagger.%PROPERTY% -ne $null) { " ^
        "if ('%VALUE%' -eq 'true' -or '%VALUE%' -eq 'false') { " ^
            "$json.Swagger.%PROPERTY% = [bool]'%VALUE%'; " ^
        "} else { " ^
            "$json.Swagger.%PROPERTY% = '%VALUE%'; " ^
        "} " ^
    "} else { " ^
        "Write-Host 'Property %PROPERTY% not found in Swagger configuration'; " ^
    "} " ^
    "$json | ConvertTo-Json -Depth 10 | Set-Content '%TARGET_FILE%'"

if %errorlevel% neq 0 (
    echo Error updating configuration file.
)
goto :eof

:GET_CURRENT_VALUE
:: Function to get current JSON values
:: %1 = property name
set "PROPERTY=%~1"

for /f "delims=" %%i in ('powershell -Command ^
    "$json = Get-Content '%TARGET_FILE%' | ConvertFrom-Json; " ^
    "if ($json.Swagger.%PROPERTY% -ne $null) { " ^
        "$json.Swagger.%PROPERTY% " ^
    "} else { " ^
        "'not found' " ^
    "}"') do set "CURRENT_VALUE=%%i"
goto :eof

:EXIT
echo.
echo Configuration tool exited.
echo.
if exist "%BACKUP_FILE%" (
    echo Backup file available at: %BACKUP_FILE%
)
pause
exit /b 0