param(
    [string]$PluginDll = (Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\RouteRunner.dll')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseAssets = Join-Path $PSScriptRoot 'packaging\thunderstore'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$manifestPath = Join-Path $releaseAssets 'manifest.json'
$manifest = $utf8.GetString([IO.File]::ReadAllBytes($manifestPath)) | ConvertFrom-Json
if ($manifest.name -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Invalid package name.' }
if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
if (!$manifest.description -or $manifest.description.Length -gt 250) { throw 'Invalid description.' }
if ($null -eq $manifest.website_url -or ($manifest.website_url -ne '' -and $manifest.website_url -notmatch '^https?://')) { throw 'Invalid website URL.' }
foreach ($dependency in $manifest.dependencies) {
    if ($dependency -notmatch '^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$') { throw "Invalid dependency: $dependency" }
}
$version = [string]$manifest.version_number
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($PluginDll).Version.ToString(3)
if ($assemblyVersion -ne $version) { throw 'Plugin and manifest versions differ. Build the matching release first.' }
$iconPath = Join-Path $PSScriptRoot 'packaging\icon.png'
$png = [IO.File]::ReadAllBytes($iconPath)
if ($png.Length -lt 24 -or [BitConverter]::ToString($png, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Icon is not a PNG.' }
function Read-BigEndian32([byte[]]$Bytes, [int]$Offset) {
    return ([uint32]$Bytes[$Offset] -shl 24) -bor ([uint32]$Bytes[$Offset + 1] -shl 16) -bor ([uint32]$Bytes[$Offset + 2] -shl 8) -bor [uint32]$Bytes[$Offset + 3]
}
if ((Read-BigEndian32 $png 16) -ne 256 -or (Read-BigEndian32 $png 20) -ne 256) { throw 'Icon must be exactly 256x256.' }
# Explicit allowlist: no test executables, reports, source, personal routes, configs or dependency DLLs.
$files = [ordered]@{
    'manifest.json' = $manifestPath
    'README.md' = (Join-Path $releaseAssets 'README.md')
    'CHANGELOG.md' = (Join-Path $releaseAssets 'CHANGELOG.md')
    'icon.png' = $iconPath
    'LICENSE' = (Join-Path $PSScriptRoot 'LICENSE')
    'plugins/RouteRunner/RouteRunner.dll' = $PluginDll
}
foreach ($entry in $files.GetEnumerator()) {
    if (!(Test-Path -LiteralPath $entry.Value -PathType Leaf)) { throw "Missing release file: $($entry.Value)" }
    if ($entry.Key.EndsWith('.md')) { $null = $utf8.GetString([IO.File]::ReadAllBytes($entry.Value)) }
}
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zipPath = Join-Path $dist "Route_Runner-$version-Thunderstore.zip"
$output = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($entry in $files.GetEnumerator()) {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }
} finally { $output.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $files.Count) { throw 'Unexpected ZIP contents.' }
    foreach ($entry in $check.Entries) {
        if (!$files.Contains($entry.FullName)) { throw "Unexpected ZIP entry: $($entry.FullName)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $packed = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
            if ($packed -ne (Get-FileHash -LiteralPath $files[$entry.FullName] -Algorithm SHA256).Hash) { throw "ZIP content mismatch: $($entry.FullName)" }
        } finally { $sha.Dispose(); $stream.Dispose() }
    }
} finally { $check.Dispose() }
Write-Output "Validated release package: $zipPath"
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
