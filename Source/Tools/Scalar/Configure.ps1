# Scalar Configuration Script for Portway API
# This PowerShell script allows you to modify Scalar UI settings in appsettings.json

param(
    [string]$ConfigPath,
    [string]$Theme,
    [string]$Layout,
    [switch]$ShowSidebar,
    [switch]$HideSidebar,
    [switch]$ShowDownloadButton,
    [switch]$HideDownloadButton,
    [switch]$ShowModels,
    [switch]$HideModels,
    [switch]$ShowClientButton,
    [switch]$HideClientButton,
    [switch]$ShowTestRequestButton,
    [switch]$HideTestRequestButton,
    [switch]$Reset,
    [switch]$View,
    [switch]$Interactive
)

# Available themes
$ValidThemes = @('default', 'alternate', 'moon', 'purple', 'solarized', 'bluePlanet', 'saturn', 'kepler', 'mars', 'deepSpace', 'laserwave', 'none')

# Available layouts
$ValidLayouts = @('modern', 'classic')

function Write-Header {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Portway API - Scalar Configuration Tool" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host
}

function Find-ConfigFile {
    param([string]$CustomPath)
    
    if ($CustomPath -and (Test-Path $CustomPath)) {
        return $CustomPath
    }
    
    $DeploymentPath = "..\..\..\Deployment\PortwayApi\appsettings.json"
    $SourcePath = "..\..\PortwayApi\appsettings.json"
    
    if (Test-Path $DeploymentPath) {
        Write-Host "Using deployment configuration: $DeploymentPath" -ForegroundColor Green
        return (Resolve-Path $DeploymentPath).Path
    }
    elseif (Test-Path $SourcePath) {
        Write-Host "Using source configuration: $SourcePath" -ForegroundColor Green
        return (Resolve-Path $SourcePath).Path
    }
    else {
        Write-Host "Error: Could not find appsettings.json file" -ForegroundColor Red
        Write-Host "Checked:" -ForegroundColor Yellow
        Write-Host "  - $DeploymentPath" -ForegroundColor Yellow
        Write-Host "  - $SourcePath" -ForegroundColor Yellow
        return $null
    }
}

function Backup-ConfigFile {
    param([string]$FilePath)
    
    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $BackupPath = "$FilePath.backup_$Timestamp"
    
    try {
        Copy-Item $FilePath $BackupPath
        Write-Host "Backup created: $BackupPath" -ForegroundColor Green
        return $BackupPath
    }
    catch {
        Write-Host "Warning: Could not create backup file" -ForegroundColor Yellow
        return $null
    }
}

function Get-ScalarConfig {
    param([string]$ConfigPath)
    
    try {
        $Config = Get-Content $ConfigPath | ConvertFrom-Json
        if (-not $Config.Swagger) {
            Write-Host "Error: Swagger configuration not found in file" -ForegroundColor Red
            return $null
        }
        return $Config
    }
    catch {
        Write-Host "Error reading configuration file: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Save-ScalarConfig {
    param([object]$Config, [string]$ConfigPath)
    
    try {
        $Config | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath
        Write-Host "Configuration saved successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "Error saving configuration: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Show-CurrentConfig {
    param([object]$Config)
    
    Write-Host
    Write-Host "Current Scalar Configuration:" -ForegroundColor Cyan
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host "Theme: $($Config.Swagger.ScalarTheme)" -ForegroundColor White
    Write-Host "Layout: $($Config.Swagger.ScalarLayout)" -ForegroundColor White
    Write-Host "Show Sidebar: $($Config.Swagger.ScalarShowSidebar)" -ForegroundColor White
    Write-Host "Hide Download Button: $($Config.Swagger.ScalarHideDownloadButton)" -ForegroundColor White
    Write-Host "Hide Models: $($Config.Swagger.ScalarHideModels)" -ForegroundColor White
    Write-Host "Hide Client Button: $($Config.Swagger.ScalarHideClientButton)" -ForegroundColor White
    Write-Host "Hide Test Request Button: $($Config.Swagger.ScalarHideTestRequestButton)" -ForegroundColor White
    Write-Host
}

function Show-InteractiveMenu {
    Write-Host "Available Scalar Configuration Options:" -ForegroundColor Cyan
    Write-Host
    Write-Host "1. Change Theme" -ForegroundColor White
    Write-Host "2. Change Layout" -ForegroundColor White
    Write-Host "3. Toggle Sidebar Visibility" -ForegroundColor White
    Write-Host "4. Toggle Download Button" -ForegroundColor White
    Write-Host "5. Toggle Models Section" -ForegroundColor White
    Write-Host "6. Toggle Client Button" -ForegroundColor White
    Write-Host "7. Toggle Test Request Button" -ForegroundColor White
    Write-Host "8. View Current Configuration" -ForegroundColor White
    Write-Host "9. Reset to Defaults" -ForegroundColor White
    Write-Host "10. Exit" -ForegroundColor White
    Write-Host
}

function Show-LayoutMenu {
    Write-Host
    Write-Host "Available Layouts:" -ForegroundColor Cyan
    Write-Host
    for ($i = 0; $i -lt $ValidLayouts.Count; $i++) {
        Write-Host "$($i + 1). $($ValidLayouts[$i])" -ForegroundColor White
    }
    Write-Host
}
function Show-ThemeMenu {
    Write-Host
    Write-Host "Available Themes:" -ForegroundColor Cyan
    Write-Host
    for ($i = 0; $i -lt $ValidThemes.Count; $i++) {
        Write-Host "$($i + 1). $($ValidThemes[$i])" -ForegroundColor White
    }
    Write-Host
}

function Reset-ToDefaults {
    param([object]$Config)
    
    $Config.Swagger.ScalarTheme = "default"
    $Config.Swagger.ScalarLayout = "modern"
    $Config.Swagger.ScalarShowSidebar = $true
    $Config.Swagger.ScalarHideDownloadButton = $true
    $Config.Swagger.ScalarHideModels = $true
    $Config.Swagger.ScalarHideClientButton = $true
    $Config.Swagger.ScalarHideTestRequestButton = $false
    
    Write-Host "All Scalar settings have been reset to defaults." -ForegroundColor Green
    return $Config
}

function Start-InteractiveMode {
    param([object]$Config, [string]$ConfigPath)
    
    do {
        Show-InteractiveMenu
        $Choice = Read-Host "Select an option (1-10)"
        
        switch ($Choice) {
            "1" {
                Show-ThemeMenu
                $ThemeChoice = Read-Host "Select theme (1-$($ValidThemes.Count))"
                $ThemeIndex = [int]$ThemeChoice - 1
                if ($ThemeIndex -ge 0 -and $ThemeIndex -lt $ValidThemes.Count) {
                    $Config.Swagger.ScalarTheme = $ValidThemes[$ThemeIndex]
                    Write-Host "Theme updated to: $($ValidThemes[$ThemeIndex])" -ForegroundColor Green
                    Save-ScalarConfig $Config $ConfigPath | Out-Null
                } else {
                    Write-Host "Invalid theme choice." -ForegroundColor Red
                }
                Write-Host
            }
            "2" {
                Show-LayoutMenu
                $LayoutChoice = Read-Host "Select layout (1-$($ValidLayouts.Count))"
                $LayoutIndex = [int]$LayoutChoice - 1
                if ($LayoutIndex -ge 0 -and $LayoutIndex -lt $ValidLayouts.Count) {
                    $Config.Swagger.ScalarLayout = $ValidLayouts[$LayoutIndex]
                    Write-Host "Layout updated to: $($ValidLayouts[$LayoutIndex])" -ForegroundColor Green
                    Save-ScalarConfig $Config $ConfigPath | Out-Null
                } else {
                    Write-Host "Invalid layout choice." -ForegroundColor Red
                }
                Write-Host
            }
            "3" {
                $Config.Swagger.ScalarShowSidebar = -not $Config.Swagger.ScalarShowSidebar
                Write-Host "Sidebar visibility set to: $($Config.Swagger.ScalarShowSidebar)" -ForegroundColor Green
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "4" {
                $Config.Swagger.ScalarHideDownloadButton = -not $Config.Swagger.ScalarHideDownloadButton
                $Status = if ($Config.Swagger.ScalarHideDownloadButton) { "hidden" } else { "visible" }
                Write-Host "Download button is now: $Status" -ForegroundColor Green
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "5" {
                $Config.Swagger.ScalarHideModels = -not $Config.Swagger.ScalarHideModels
                $Status = if ($Config.Swagger.ScalarHideModels) { "hidden" } else { "visible" }
                Write-Host "Models section is now: $Status" -ForegroundColor Green
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "6" {
                $Config.Swagger.ScalarHideClientButton = -not $Config.Swagger.ScalarHideClientButton
                $Status = if ($Config.Swagger.ScalarHideClientButton) { "hidden" } else { "visible" }
                Write-Host "Client button is now: $Status" -ForegroundColor Green
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "7" {
                $Config.Swagger.ScalarHideTestRequestButton = -not $Config.Swagger.ScalarHideTestRequestButton
                $Status = if ($Config.Swagger.ScalarHideTestRequestButton) { "hidden" } else { "visible" }
                Write-Host "Test Request button is now: $Status" -ForegroundColor Green
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "8" {
                Show-CurrentConfig $Config
            }
            "9" {
                $Config = Reset-ToDefaults $Config
                Save-ScalarConfig $Config $ConfigPath | Out-Null
                Write-Host
            }
            "10" {
                Write-Host "Configuration tool exited." -ForegroundColor Cyan
                break
            }
            default {
                Write-Host "Invalid choice. Please select 1-10." -ForegroundColor Red
                Write-Host
            }
        }
    } while ($Choice -ne "10")
}

# Main script execution
Write-Header

# Find configuration file
$ConfigFile = Find-ConfigFile $ConfigPath
if (-not $ConfigFile) {
    exit 1
}

# Create backup
$BackupFile = Backup-ConfigFile $ConfigFile

# Load configuration
$Config = Get-ScalarConfig $ConfigFile
if (-not $Config) {
    exit 1
}

# Handle command line parameters
$Changes = $false

if ($Theme) {
    if ($ValidThemes -contains $Theme) {
        $Config.Swagger.ScalarTheme = $Theme
        Write-Host "Theme set to: $Theme" -ForegroundColor Green
        $Changes = $true
    } else {
        Write-Host "Invalid theme. Valid themes are: $($ValidThemes -join ', ')" -ForegroundColor Red
        exit 1
    }
}

if ($Layout) {
    if ($ValidLayouts -contains $Layout) {
        $Config.Swagger.ScalarLayout = $Layout
        Write-Host "Layout set to: $Layout" -ForegroundColor Green
        $Changes = $true
    } else {
        Write-Host "Invalid layout. Valid layouts are: $($ValidLayouts -join ', ')" -ForegroundColor Red
        exit 1
    }
}

if ($ShowSidebar) {
    $Config.Swagger.ScalarShowSidebar = $true
    Write-Host "Sidebar visibility set to: true" -ForegroundColor Green
    $Changes = $true
}

if ($HideSidebar) {
    $Config.Swagger.ScalarShowSidebar = $false
    Write-Host "Sidebar visibility set to: false" -ForegroundColor Green
    $Changes = $true
}

if ($ShowDownloadButton) {
    $Config.Swagger.ScalarHideDownloadButton = $false
    Write-Host "Download button is now: visible" -ForegroundColor Green
    $Changes = $true
}

if ($HideDownloadButton) {
    $Config.Swagger.ScalarHideDownloadButton = $true
    Write-Host "Download button is now: hidden" -ForegroundColor Green
    $Changes = $true
}

if ($ShowModels) {
    $Config.Swagger.ScalarHideModels = $false
    Write-Host "Models section is now: visible" -ForegroundColor Green
    $Changes = $true
}

if ($HideModels) {
    $Config.Swagger.ScalarHideModels = $true
    Write-Host "Models section is now: hidden" -ForegroundColor Green
    $Changes = $true
}

if ($ShowClientButton) {
    $Config.Swagger.ScalarHideClientButton = $false
    Write-Host "Client button is now: visible" -ForegroundColor Green
    $Changes = $true
}

if ($HideClientButton) {
    $Config.Swagger.ScalarHideClientButton = $true
    Write-Host "Client button is now: hidden" -ForegroundColor Green
    $Changes = $true
}

if ($ShowTestRequestButton) {
    $Config.Swagger.ScalarHideTestRequestButton = $false
    Write-Host "Test Request button is now: visible" -ForegroundColor Green
    $Changes = $true
}

if ($HideTestRequestButton) {
    $Config.Swagger.ScalarHideTestRequestButton = $true
    Write-Host "Test Request button is now: hidden" -ForegroundColor Green
    $Changes = $true
}

if ($Reset) {
    $Config = Reset-ToDefaults $Config
    $Changes = $true
}

if ($View) {
    Show-CurrentConfig $Config
}

# Save changes if any were made
if ($Changes) {
    Save-ScalarConfig $Config $ConfigFile | Out-Null
}

# Start interactive mode if no parameters provided or if explicitly requested
if ((-not $Changes -and -not $View) -or $Interactive) {
    Start-InteractiveMode $Config $ConfigFile
}

if ($BackupFile) {
    Write-Host
    Write-Host "Backup file available at: $BackupFile" -ForegroundColor Yellow
}