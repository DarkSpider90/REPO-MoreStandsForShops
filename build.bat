@echo off
echo Building MoreStandsForShops...
dotnet build MoreStandsForShops.csproj -c Release
if %errorlevel% equ 0 (
    echo Build successful! Output: bin\Release\netstandard2.1\MoreStandsForShops.dll
) else (
    echo Build failed.
)
pause
