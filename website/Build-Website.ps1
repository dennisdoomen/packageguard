<#
.SYNOPSIS
    Builds the Docusaurus website for production.

.DESCRIPTION
    This script installs dependencies if needed and builds the static website 
    for production deployment. The output will be in the 'build' directory.

.EXAMPLE
    .\Build-Website.ps1
    Builds the website with default settings.

.EXAMPLE
    .\Build-Website.ps1 -SkipInstall
    Builds the website without running npm install first.

.PARAMETER SkipInstall
    Skip the npm install step if dependencies are already installed.

.PARAMETER Clean
    Clean the build directory before building.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$SkipInstall,
    
    [Parameter()]
    [switch]$Clean
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Check if Node.js is installed
try {
    $nodeVersion = node --version
    Write-Host "✓ Node.js version: $nodeVersion" -ForegroundColor Green
} catch {
    Write-Error "Node.js is not installed. Please install Node.js from https://nodejs.org/"
    exit 1
}

# Check if npm is installed
try {
    $npmVersion = npm --version
    Write-Host "✓ npm version: $npmVersion" -ForegroundColor Green
} catch {
    Write-Error "npm is not installed. Please install Node.js which includes npm."
    exit 1
}

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

try {
    # Clean build directory if requested
    if ($Clean -and (Test-Path "build")) {
        Write-Host "`nCleaning build directory..." -ForegroundColor Cyan
        Remove-Item -Path "build" -Recurse -Force
        Write-Host "✓ Build directory cleaned" -ForegroundColor Green
    }

    # Install dependencies if not skipping
    if (-not $SkipInstall) {
        Write-Host "`nInstalling dependencies..." -ForegroundColor Cyan
        npm install
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install dependencies"
        }
        Write-Host "✓ Dependencies installed successfully" -ForegroundColor Green
    }

    # Build the website
    Write-Host "`nBuilding website for production..." -ForegroundColor Cyan
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    Write-Host "`n✓ Build completed successfully!" -ForegroundColor Green
    Write-Host "Output directory: $(Join-Path $scriptDir 'build')" -ForegroundColor Yellow
    Write-Host "`nTo test the production build locally, run:" -ForegroundColor Cyan
    Write-Host "  .\Test-ProductionBuild.ps1" -ForegroundColor White
} catch {
    Write-Error "An error occurred: $_"
    exit 1
} finally {
    Pop-Location
}
