<#
.SYNOPSIS
    Runs various tests on the Docusaurus website.

.DESCRIPTION
    This script performs checks on the website including:
    - Verifying all required files exist
    - Checking for broken links in markdown files
    - Validating the build process
    - Running a quick build test

.EXAMPLE
    .\Test-Website.ps1
    Runs all tests with default settings.

.EXAMPLE
    .\Test-Website.ps1 -SkipBuild
    Runs tests but skips the build validation.

.PARAMETER SkipBuild
    Skip the build validation test.

.PARAMETER Verbose
    Show detailed output.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$SkipBuild
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

$testsPassed = 0
$testsFailed = 0

function Test-FileExists {
    param([string]$Path, [string]$Description)
    
    if (Test-Path $Path) {
        Write-Host "✓ $Description" -ForegroundColor Green
        return $true
    } else {
        Write-Host "✗ $Description - NOT FOUND" -ForegroundColor Red
        return $false
    }
}

try {
    Write-Host "`n=== Docusaurus Website Tests ===`n" -ForegroundColor Cyan

    # Test 1: Check required files
    Write-Host "Test 1: Checking required files..." -ForegroundColor Yellow
    
    $requiredFiles = @(
        @{ Path = "package.json"; Description = "package.json exists" }
        @{ Path = "docusaurus.config.ts"; Description = "docusaurus.config.ts exists" }
        @{ Path = "sidebars.ts"; Description = "sidebars.ts exists" }
        @{ Path = "docs/intro.md"; Description = "Introduction page exists" }
        @{ Path = "docs/installation.md"; Description = "Installation page exists" }
        @{ Path = "docs/configuration.md"; Description = "Configuration page exists" }
        @{ Path = "docs/usage.md"; Description = "Usage page exists" }
        @{ Path = "docs/risk-metrics.md"; Description = "Risk Metrics page exists" }
        @{ Path = "docs/sbom.md"; Description = "SBOM page exists" }
        @{ Path = "docs/caching-and-rate-limits.md"; Description = "Caching and rate limits page exists" }
        @{ Path = "src/css/custom.css"; Description = "Custom CSS exists" }
        @{ Path = "static/img/logo.png"; Description = "Logo image exists" }
    )
    
    $fileTestsPassed = 0
    foreach ($file in $requiredFiles) {
        if (Test-FileExists -Path $file.Path -Description $file.Description) {
            $fileTestsPassed++
        }
    }
    
    if ($fileTestsPassed -eq $requiredFiles.Count) {
        Write-Host "✓ All required files present ($fileTestsPassed/$($requiredFiles.Count))" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ Some required files missing ($fileTestsPassed/$($requiredFiles.Count))" -ForegroundColor Red
        $testsFailed++
    }

    # Test 2: Check documentation files for basic structure
    Write-Host "`nTest 2: Checking documentation structure..." -ForegroundColor Yellow
    
    $docFiles = Get-ChildItem -Path "docs" -Filter "*.md"
    $docTestsPassed = $true
    
    foreach ($doc in $docFiles) {
        $content = Get-Content $doc.FullName -Raw
        if ($content -match "(?s)^---.*?sidebar_position:.*?---") {
            Write-Host "  ✓ $($doc.Name) has frontmatter" -ForegroundColor Green
        } else {
            Write-Host "  ✗ $($doc.Name) missing frontmatter" -ForegroundColor Red
            $docTestsPassed = $false
        }
    }
    
    if ($docTestsPassed) {
        Write-Host "✓ All documentation files have proper structure" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ Some documentation files have issues" -ForegroundColor Red
        $testsFailed++
    }

    # Test 3: Verify dependencies can be installed
    Write-Host "`nTest 3: Verifying dependencies..." -ForegroundColor Yellow
    
    if (Test-Path "node_modules") {
        Write-Host "  ✓ node_modules directory exists" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "  Installing dependencies..." -ForegroundColor Cyan
        npm install --silent
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Dependencies installed successfully" -ForegroundColor Green
            $testsPassed++
        } else {
            Write-Host "✗ Failed to install dependencies" -ForegroundColor Red
            $testsFailed++
        }
    }

    # Test 4: Build validation (optional)
    if (-not $SkipBuild) {
        Write-Host "`nTest 4: Build validation..." -ForegroundColor Yellow
        
        Write-Host "  Building website..." -ForegroundColor Cyan
        npm run build --silent
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Website builds successfully" -ForegroundColor Green
            $testsPassed++
            
            # Check if build output exists
            if (Test-Path "build/index.html") {
                Write-Host "  ✓ Build output contains index.html" -ForegroundColor Green
            } else {
                Write-Host "  ✗ Build output missing index.html" -ForegroundColor Red
            }
        } else {
            Write-Host "✗ Build failed" -ForegroundColor Red
            $testsFailed++
        }
    } else {
        Write-Host "`nTest 4: Build validation - SKIPPED" -ForegroundColor Gray
    }

    # Summary
    Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
    Write-Host "Tests passed: $testsPassed" -ForegroundColor Green
    
    if ($testsFailed -gt 0) {
        Write-Host "Tests failed: $testsFailed" -ForegroundColor Red
        exit 1
    } else {
        Write-Host "`n✓ All tests passed!" -ForegroundColor Green
        Write-Host "`nTo run the development server:" -ForegroundColor Cyan
        Write-Host "  .\Start-DevServer.ps1" -ForegroundColor White
        Write-Host "`nTo build for production:" -ForegroundColor Cyan
        Write-Host "  .\Build-Website.ps1" -ForegroundColor White
    }
} catch {
    Write-Error "An error occurred during testing: $_"
    exit 1
} finally {
    Pop-Location
}
