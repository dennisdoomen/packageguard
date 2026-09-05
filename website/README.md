# PackageGuard Documentation Website

This website is built using [Docusaurus](https://docusaurus.io/), a modern static website generator, and is published at [packageguard.org](https://packageguard.org/).

## Prerequisites

- [Node.js](https://nodejs.org/) version 20 or higher
- npm (comes with Node.js)

## Quick Start

### For Windows Users (PowerShell)

We provide PowerShell scripts to make testing the website easy:

```powershell
# Start the development server
.\Start-DevServer.ps1

# Build the website for production
.\Build-Website.ps1

# Test the production build locally
.\Test-ProductionBuild.ps1

# Run website tests
.\Test-Website.ps1
```

### For Linux/macOS Users

Use npm commands directly:

```bash
# Install dependencies
npm install

# Start the development server
npm start

# Build for production
npm run build

# Serve the production build
npm run serve
```

## PowerShell Scripts

### Start-DevServer.ps1

Starts the Docusaurus development server with hot-reloading.

```powershell
# Basic usage
.\Start-DevServer.ps1

# Use a different port
.\Start-DevServer.ps1 -Port 8080

# Skip dependency installation
.\Start-DevServer.ps1 -SkipInstall
```

The website will be available at `http://localhost:3000/`

### Build-Website.ps1

Builds the website for production deployment.

```powershell
# Basic usage
.\Build-Website.ps1

# Clean build directory first
.\Build-Website.ps1 -Clean

# Skip dependency installation
.\Build-Website.ps1 -SkipInstall
```

Output will be in the `build/` directory.

### Test-ProductionBuild.ps1

Serves the production build locally for testing.

```powershell
# Basic usage (must build first)
.\Test-ProductionBuild.ps1

# Build and serve in one command
.\Test-ProductionBuild.ps1 -Build

# Use a different port
.\Test-ProductionBuild.ps1 -Port 8080
```

### Test-Website.ps1

Runs comprehensive tests on the website structure and build process.

```powershell
# Run all tests
.\Test-Website.ps1

# Skip build validation (faster)
.\Test-Website.ps1 -SkipBuild
```

This script checks:
- Required files exist
- Documentation files have proper frontmatter
- Dependencies can be installed
- Website builds successfully

## Documentation Structure

- `docs/intro.md` - Introduction and key features
- `docs/installation.md` - Installing the global tool or the portable deployment, plus all CLI options
- `docs/configuration.md` - Hierarchical configuration, allow/deny lists, feeds
- `docs/usage.md` - Running a scan and reading the results
- `docs/risk-metrics.md` - Legal, security and operational risk scoring
- `docs/sbom.md` - CycloneDX and SPDX output
- `docs/caching-and-rate-limits.md` - Caching and GitHub rate limits
- `docs/roadmap.md` - Planned features
- `docs/building.md` - Building from source
- `docs/contributing.md` - Contributing guidelines

## Customization

- **Colors**: Edit `src/css/custom.css` to modify the color scheme
- **Homepage**: Edit `src/pages/index.tsx` and `src/components/HomepageFeatures/index.tsx`
- **Configuration**: Edit `docusaurus.config.ts` for site-wide settings
- **Sidebar**: Edit `sidebars.ts` to modify documentation navigation

## Deployment

The website is automatically deployed to GitHub Pages when changes under `website/` are pushed to the `main` branch. The workflow is defined in `.github/workflows/deploy-docs.yml`.

### Manual Deployment

If you need to deploy manually:

```bash
# Using SSH
USE_SSH=true npm run deploy

# Using HTTPS
GIT_USER=<Your GitHub username> npm run deploy
```

## Troubleshooting

### Port already in use

If you get an error that port 3000 is already in use, either:
- Stop the other process using that port
- Use a different port: `.\Start-DevServer.ps1 -Port 8080`

### Build fails

If the build fails:
1. Delete `node_modules` and run `npm ci` to restore the exact dependency versions from `package-lock.json`
2. Try building again

Note that the dependency versions are pinned through `package-lock.json`. Newer webpack releases break the progress reporter that Docusaurus 3.9 uses, so prefer `npm ci` over `npm install` when you only want to build the site.

### PowerShell execution policy error

If you get "cannot be loaded because running scripts is disabled", run:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## Help

For detailed help on any PowerShell script:

```powershell
Get-Help .\Start-DevServer.ps1 -Detailed
Get-Help .\Build-Website.ps1 -Detailed
Get-Help .\Test-ProductionBuild.ps1 -Detailed
Get-Help .\Test-Website.ps1 -Detailed
```
