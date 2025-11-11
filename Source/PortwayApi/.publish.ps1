# Get script and root directory paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent (Split-Path -Parent $scriptDir)

# Define the deployment path
$deploymentPath = Join-Path $rootDir "Deployment\PortwayApi"

# Check if the deployment folder exists, create it if not
if (-not (Test-Path -Path $deploymentPath)) {
    Write-Host "Creating deployment folder at $deploymentPath..."
    New-Item -Path $deploymentPath -ItemType Directory -Force | Out-Null
} 
else {
    # If it exists, remove its contents
    Write-Host "Removing existing deployment folder contents..."
    Get-ChildItem -Path $deploymentPath -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1 # Give the system a moment
}

# Publish the application (framework-dependent)
Write-Host "Publishing application..."
$sourceProjectPath = Join-Path $rootDir "Source\PortwayApi"
dotnet publish $sourceProjectPath -c Release --self-contained false -o $deploymentPath

# Clean up unnecessary development files
Write-Host "Removing development files..."
$filesToRemove = @(
    "*.pdb",
    "appsettings.Development.json",
    "*.publish.ps1",
    "*.db",
    ".git*"
)

foreach ($pattern in $filesToRemove) {
    Get-ChildItem -Path $deploymentPath -Filter $pattern -Recurse -ErrorAction SilentlyContinue | 
    Remove-Item -Force -ErrorAction SilentlyContinue
}

# Remove .git folder specifically (if it exists)
$gitFolder = Join-Path $deploymentPath ".git"
if (Test-Path $gitFolder) {
    Write-Host "Removing .git folder..."
    Remove-Item -Path $gitFolder -Recurse -Force -ErrorAction SilentlyContinue
}

# Remove tokens folder/files (if they exist)
$tokensFolder = Join-Path $deploymentPath "tokens"
if (Test-Path $tokensFolder) {
    Write-Host "Removing tokens folder and files..."
    Remove-Item -Path $tokensFolder -Recurse -Force -ErrorAction SilentlyContinue
}

# Remove any token files in subdirectories
Get-ChildItem -Path $deploymentPath -Directory -Recurse -ErrorAction SilentlyContinue |
Where-Object { $_.Name -eq "tokens" } |
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Remove XML documentation files (but not content files)
Get-ChildItem -Path $deploymentPath -Filter "*.xml" -Recurse -ErrorAction SilentlyContinue |
Where-Object { 
    $_.FullName -notlike "*\Endpoints\*" -and 
    $_.Name -like "*.xml" 
} |
Remove-Item -Force -ErrorAction SilentlyContinue

# Remove all localized folders with SqlClient resources, except for 'en' and 'nl'
Get-ChildItem -Path $deploymentPath -Directory -ErrorAction SilentlyContinue |
Where-Object {
    ($_.Name -ne "en" -and $_.Name -ne "nl") -and
    (Test-Path "$($_.FullName)\Microsoft.Data.SqlClient.resources.dll" -ErrorAction SilentlyContinue)
} | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Move POST.txt from examples folders ONLY within Endpoints/Proxy and remove examples/definitions folders
$endpointsPath = Join-Path $deploymentPath "Endpoints"
$proxyPath = Join-Path $endpointsPath "Proxy"

if (Test-Path $proxyPath) {
    # Find and move POST.txt files from examples folders inside Proxy only
    Get-ChildItem -Path $proxyPath -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "examples" -or $_.Name -eq "example" } |
    ForEach-Object {
        $postFile = Join-Path $_.FullName "POST.txt"
        if (Test-Path $postFile) {
            $parentFolder = Split-Path -Parent $_.FullName
            $destinationFile = Join-Path $parentFolder "entity.example"
            Copy-Item -Path $postFile -Destination $destinationFile -Force -ErrorAction SilentlyContinue
        }
    }

    Get-ChildItem -Path $endpointsPath -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "examples" } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Remove all 'definitions' folders
    Get-ChildItem -Path $endpointsPath -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "definitions" } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# Generate web.config
$webConfigContent = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\PortwayApi.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
  <system.webServer>
    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <httpProtocol>
      <customHeaders>
        <remove name="X-Powered-By" />
        <remove name="X-Content-Type-Options" />
        <remove name="X-Frame-Options" />
        <remove name="Strict-Transport-Security" />
        <remove name="Referrer-Policy" />
        <remove name="Permissions-Policy" />
        <remove name="Content-Security-Policy" />
        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="X-Frame-Options" value="DENY" />
        <add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains; preload" />
        <add name="Referrer-Policy" value="strict-origin-when-cross-origin" />
        <add name="Permissions-Policy" value="geolocation=(), camera=(), microphone=(), payment=()" />
        <add name="Content-Security-Policy" value="default-src 'self'; script-src 'self' https://cdn.jsdelivr.net 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' https://proxy.scalar.com; font-src 'self' https://fonts.scalar.com; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'" />      
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
'@

# Create parent directories if they don't exist and write the web.config file
$webConfigPath = "$deploymentPath\web.config"
$webConfigContent | Out-File -FilePath $webConfigPath -Encoding UTF8 -Force

# Copy LICENSE file to deployment directory under /license/
$licenseDir = Join-Path $deploymentPath "license"
if (-not (Test-Path $licenseDir)) {
  New-Item -Path $licenseDir -ItemType Directory -Force | Out-Null
}
$licensePath = Join-Path $rootDir "LICENSE"
$deploymentLicensePath = Join-Path $licenseDir "license.txt"
if (Test-Path $licensePath) {
  Write-Host "Copying LICENSE file to deployment directory under /license/..."
  Copy-Item -Path $licensePath -Destination $deploymentLicensePath -Force
}

# Create source file
$sourceFilePath = Join-Path $licenseDir "source.txt"
"https://github.com/melosso/portway" | Out-File -FilePath $sourceFilePath -Encoding UTF8 -Force

# Generate and include third-party package licenses
Write-Host "Collecting package licenses..."
$packagesLicenseDir = Join-Path $licenseDir "packages"
if (-not (Test-Path $packagesLicenseDir)) {
  New-Item -Path $packagesLicenseDir -ItemType Directory -Force | Out-Null
}

# Install dotnet-project-licenses tool if not already installed
$toolInstalled = dotnet tool list -g | Select-String "dotnet-project-licenses"
if (-not $toolInstalled) {
  Write-Host "Installing dotnet-project-licenses tool..."
  dotnet tool install -g dotnet-project-licenses
}

# Generate package licenses
$projectPath = Join-Path $sourceProjectPath "PortwayApi.csproj"
try {
  dotnet-project-licenses --input $projectPath --export-license-texts --output-directory $packagesLicenseDir
  
  # Remove Microsoft and Azure package license files
  Get-ChildItem -Path $packagesLicenseDir -File -ErrorAction SilentlyContinue |
  Where-Object {
    $_.Name -like "Microsoft.*" -or
    $_.Name -like "Azure.*" -or
    $_.Name -like "OpenTelemetry.*" -or
    $_.Name -like "SqlKata.Execution.*" -or
    ( ($_.Name -like "Serilog.*") -and -not ($_.Name -like "Serilog.AspNetCore*") )
  } |
  Remove-Item -Force -ErrorAction SilentlyContinue

  # Rename remaining files to remove version numbers
  Get-ChildItem -Path $packagesLicenseDir -File -Filter "*.html" -ErrorAction SilentlyContinue |
  ForEach-Object {
    # Extract package name without version (everything before the last underscore)
    if ($_.Name -match "^(.+)_[\d\.]+\.html$") {
      $newName = $matches[1] + ".html"
      Rename-Item -Path $_.FullName -NewName $newName -Force -ErrorAction SilentlyContinue
    }
  }
  
  Write-Host "Package licenses collected successfully (Microsoft and Azure packages excluded)."
} catch {
  Write-Host "Warning: Failed to collect package licenses - $_"
}

# Ensure .gitignore exists
$gitignorePath = Join-Path $rootDir ".gitignore"
$logIgnoreRules = @(
    "# Ignore log files",
    "*.log",
    "/logs/"
)
if (-not (Test-Path $gitignorePath)) {
    New-Item -Path $gitignorePath -ItemType File -Force | Out-Null
}
$existingRules = Get-Content $gitignorePath -ErrorAction SilentlyContinue
foreach ($rule in $logIgnoreRules) {
    if ($existingRules -notcontains $rule) {
        Add-Content -Path $gitignorePath -Value $rule
    }
}

Write-Host ".gitignore updated to exclude log files and /logs/ directory"
Write-Host "Deployment complete. The application has been published to $deploymentPath"
Write-Host "web.config file generated at $webConfigPath"
Write-Host "Third-party package licenses collected in $packagesLicenseDir"