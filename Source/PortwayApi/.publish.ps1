# Define the deployment path
$deploymentPath = "C:\Github\portway\Deployment\PortwayApi"

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

# Publish the application
Write-Host "Publishing application..."
dotnet publish C:\Github\portway\Source\PortwayApi -c Release -o $deploymentPath

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
        <add name="Content-Security-Policy" value="default-src 'self'; script-src 'self' https://cdn.jsdelivr.net 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' https://proxy.scalar.com; font-src 'self' https://fonts.scalar.com; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'" />
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
'@

# Create parent directories if they don't exist and write the web.config file
$webConfigPath = "$deploymentPath\web.config"
$webConfigContent | Out-File -FilePath $webConfigPath -Encoding UTF8 -Force

# Ensure .gitignore exists
$gitignorePath = "C:\Github\portway\.gitignore"
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