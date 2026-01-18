@echo off
REM Update API metadata for DocFX documentation
REM Run this after making code changes to update the API docs
REM Then commit the api/*.yml files

echo ========================================
echo Archon Engine - Update API Metadata
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

echo Generating API metadata from C# projects...
docfx metadata docfx.json
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to generate metadata
    echo.
    echo Make sure Unity has generated the .csproj files.
    pause
    exit /b 1
)

echo.
echo ========================================
echo API metadata updated successfully!
echo.
echo Don't forget to commit the api/*.yml files:
echo   git add api/*.yml
echo   git commit -m "Update API metadata"
echo ========================================
echo.
pause
