@echo off
REM Build Archon Engine API Documentation using DocFX
REM Requires: dotnet tool install -g docfx
REM
REM IMPORTANT: Run this script from within the Hegemon project root (parent directory)
REM so that the .csproj files can be found. The docfx.json references ../../*.csproj
REM
REM Workflow:
REM 1. Run this script locally to generate API metadata (api/*.yml files)
REM 2. Commit the api/*.yml files to the repo
REM 3. GitHub Actions will build the HTML docs from the pre-committed yml files

echo ========================================
echo Archon Engine - API Documentation Build
echo ========================================
echo.

REM Check if docfx is installed
where docfx >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: docfx not found. Install it with:
    echo   dotnet tool install -g docfx
    echo.
    pause
    exit /b 1
)

echo [1/2] Generating API metadata from C# projects...
echo       (This requires Unity .csproj files to exist)
docfx metadata docfx.json
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to generate metadata
    echo.
    echo Make sure you are running this from the Hegemon project root
    echo and that Unity has generated the .csproj files.
    pause
    exit /b 1
)

echo.
echo [2/2] Building documentation site...
docfx build docfx.json
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to build documentation
    pause
    exit /b 1
)

echo.
echo ========================================
echo Documentation built successfully!
echo Output: _site/
echo.
echo To view locally, run:
echo   docfx serve _site
echo.
echo REMEMBER: Commit the api/*.yml files for GitHub Pages!
echo ========================================
echo.
pause
