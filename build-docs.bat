@echo off
REM Build Archon Engine API Documentation using DocFX
REM Requires: dotnet tool install -g docfx

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
docfx metadata docfx.json
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to generate metadata
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
echo ========================================
echo.
pause
