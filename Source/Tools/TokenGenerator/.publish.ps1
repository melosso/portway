# Set the correct paths for your environment
$repoBase = "C:\Github\portway"
$toolsDir = "$repoBase\Source\Tools\TokenGenerator"
$deploymentDir = "$repoBase\Deployment\PortwayApi\tools\TokenGenerator"

# First completely clear the deployment directory if it exists
if (Test-Path -Path $deploymentDir) {
    Write-Host "Clearing deployment directory $deploymentDir..." -ForegroundColor Yellow
    Remove-Item -Path "$deploymentDir\*" -Recurse -Force
    Write-Host "Deployment directory cleared." -ForegroundColor Green
} else {
    # Ensure deployment directory exists
    New-Item -Path $deploymentDir -ItemType Directory -Force
    Write-Host "Created deployment directory $deploymentDir" -ForegroundColor Green
}

# Clean the project
dotnet clean $toolsDir -c Release

# Remove obj and bin directories to avoid assembly conflicts
Remove-Item -Path "$toolsDir\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$toolsDir\bin" -Recurse -Force -ErrorAction SilentlyContinue

# Create a TrimmerRoots.xml file to preserve System.Text.Json functionality
$trimmerRootsContent = @"
<linker>
  <assembly fullname="System.Text.Json" preserve="all" />
  <assembly fullname="System.Text.Json.Serialization" preserve="all" />
  <assembly fullname="Microsoft.Extensions.Configuration.Json" preserve="all" />
  <assembly fullname="Microsoft.EntityFrameworkCore.Sqlite" preserve="all" />
</linker>
"@
$trimmerRootsPath = "$toolsDir\TrimmerRoots.xml"
Set-Content -Path $trimmerRootsPath -Value $trimmerRootsContent
Write-Host "Created TrimmerRoots.xml to preserve JSON functionality" -ForegroundColor Cyan

# Publish self-contained with size optimizations but preserve JSON functionality
Write-Host "Publishing optimized self-contained version..." -ForegroundColor Cyan
dotnet publish $toolsDir -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:TrimmerRootDescriptor=$trimmerRootsPath `
    -o "$deploymentDir"

# Remove TrimmerRoots.xml after publishing
Remove-Item -Path $trimmerRootsPath -Force

# Remove unnecessary files
Write-Host "Removing unnecessary files..." -ForegroundColor Yellow
$filesToRemove = @(
    "*.pdb",
    "*.xml",
    "*.deps.json",
    "*.dev.json"
)

foreach ($pattern in $filesToRemove) {
    Get-ChildItem -Path $deploymentDir -Filter $pattern -Recurse | Remove-Item -Force
}

# Copy the batch file for convenience
Copy-Item "$toolsDir\TokenGenerator.bat" "$deploymentDir" -Force

# Get file size info
$exeFile = Get-ChildItem "$deploymentDir\TokenGenerator.exe"
$sizeInMB = [math]::Round($exeFile.Length / 1MB, 2)

Write-Host "✅ TokenGenerator published successfully to $deploymentDir" -ForegroundColor Green
Write-Host "   - Size: $sizeInMB MB" -ForegroundColor Green