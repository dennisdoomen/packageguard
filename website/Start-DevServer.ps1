<#
.SYNOPSIS
    Starts the Docusaurus development server for local testing.

.DESCRIPTION
    This script installs dependencies if needed and starts the Docusaurus 
    development server. The website will be available at http://localhost:3000/

.EXAMPLE
    .\Start-DevServer.ps1
    Starts the development server with default settings.

.EXAMPLE
    .\Start-DevServer.ps1 -Port 8080
    Starts the development server on port 8080.

.PARAMETER Port
    The port number to run the development server on. Default is 3000.

.PARAMETER SkipInstall
    Skip the npm install step if dependencies are already installed.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [int]$Port = 3000,
    
    [Parameter()]
    [switch]$SkipInstall
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
    # Install dependencies if not skipping
    if (-not $SkipInstall) {
        Write-Host "`nInstalling dependencies..." -ForegroundColor Cyan
        npm install
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install dependencies"
        }
        Write-Host "✓ Dependencies installed successfully" -ForegroundColor Green
    }

    # Start the development server
    Write-Host "`nStarting development server on port $Port..." -ForegroundColor Cyan
    Write-Host "The website will be available at http://localhost:$Port/" -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop the server`n" -ForegroundColor Yellow
    
    if ($Port -eq 3000) {
        npm start
    } else {
        npm start -- --port $Port
    }
} catch {
    Write-Error "An error occurred: $_"
    exit 1
} finally {
    Pop-Location
}
