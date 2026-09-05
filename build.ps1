param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT',
    [string]$BepInExDir = (Join-Path $PSScriptRoot 'reference\BepInEx'),
    [string]$ModMenuDir = (Join-Path $PSScriptRoot 'reference\ModMenu')
)
$ErrorActionPreference = 'Stop'

dotnet run --project (Join-Path $PSScriptRoot 'tests\RouteRunner.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

dotnet build (Join-Path $PSScriptRoot 'src\RouteRunner.csproj') -c Release "-p:GameDir=$GameDir" "-p:BepInExDir=$BepInExDir" "-p:ModMenuDir=$ModMenuDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dll = Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\RouteRunner.dll'
dotnet run --project (Join-Path $PSScriptRoot 'tests\LoaderSmoke\LoaderSmoke.csproj') -c Release -- $dll (Join-Path $GameDir 'STRAFTAT_Data\Managed') (Join-Path $BepInExDir 'core')
if ($LASTEXITCODE -ne 0) { throw 'Loader check failed.' }

New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $PSScriptRoot 'dist\RouteRunner.dll') -Force
& (Join-Path $PSScriptRoot 'package-release.ps1') -PluginDll $dll
