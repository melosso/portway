# Set the correct paths for your environment
$repoBase = "C:\Github\portway"
$toolsDir = "$repoBase\Source\Tools\Scalar"
$deploymentDir = "$repoBase\Deployment\PortwayApi\tools\Scalar"

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

# Copy Scalar configuration files
Write-Host "Copying Scalar configuration files..." -ForegroundColor Cyan

$filesToCopy = @(
    "Configure.bat",
    "Configure.ps1", 
    "README.md"
)

foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $toolsDir $file
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath $deploymentDir -Force
        Write-Host "  Copied $file" -ForegroundColor Green
    } else {
        Write-Host "  Warning: $file not found in source directory" -ForegroundColor Yellow
    }
}

Write-Host "Scalar tool published successfully to $deploymentDir" -ForegroundColor Green