<#
.SYNOPSIS
    Tests the production build of the website locally.

.DESCRIPTION
    This script serves the production build locally for testing. The website
    must be built first using Build-Website.ps1.

.EXAMPLE
    .\Test-ProductionBuild.ps1
    Serves the production build on the default port (3000).

.EXAMPLE
    .\Test-ProductionBuild.ps1 -Port 8080
    Serves the production build on port 8080.

.PARAMETER Port
    The port number to serve the website on. Default is 3000.

.PARAMETER Build
    Build the website before serving (equivalent to running Build-Website.ps1 first).
#>

[CmdletBinding()]
param(
    [Parameter()]
    [int]$Port = 3000,
    
    [Parameter()]
    [switch]$Build
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

try {
    # Build if requested
    if ($Build) {
        Write-Host "Building website first..." -ForegroundColor Cyan
        & "$scriptDir\Build-Website.ps1" -SkipInstall
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
    }

    # Check if build directory exists
    if (-not (Test-Path "build")) {
        Write-Error "Build directory not found. Please run Build-Website.ps1 first."
        exit 1
    }

    # Serve the production build
    Write-Host "`nServing production build on port $Port..." -ForegroundColor Cyan
    Write-Host "The website will be available at http://localhost:$Port/" -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop the server`n" -ForegroundColor Yellow
    
    if ($Port -eq 3000) {
        npm run serve
    } else {
        npm run serve -- --port $Port
    }
} catch {
    Write-Error "An error occurred: $_"
    exit 1
} finally {
    Pop-Location
}
