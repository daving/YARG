param(
    [Parameter(Mandatory = $true)]
    [string] $YargSourcePath
)

$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetRoot = (Resolve-Path -LiteralPath $YargSourcePath).Path

if (-not (Test-Path -LiteralPath (Join-Path $targetRoot "Assets")) -or
    -not (Test-Path -LiteralPath (Join-Path $targetRoot "ProjectSettings"))) {
    throw "Target does not look like a YARG Unity project: $targetRoot"
}

$files = @(
    "Assets\Editor\GamenightBuild.cs",
    "Assets\Script\Integration\GamenightServerClient.cs",
    "Assets\Script\Integration\GamenightServerClient.cs.meta",
    "Assets\Script\Persistent\GlobalVariables.cs",
    "Assets\Script\Menu\MusicLibrary\MusicLibraryMenu.cs",
    "Assets\Script\Menu\MusicLibrary\Sidebar.cs",
    "Assets\StreamingAssets\lang\en-US.json",
    "Assets\Plugins\Editor\Submodule\ProjectAdder.cs",
    "Assets\Script\Audio\Bass\BassAudioManager.cs",
    "Assets\packages.config",
    "YARG.Core\YARG.Core\IO\Ini\SongIniHandler.cs",
    "YARG.Core\YARG.Core\Song\Cache\CacheHandler.cs",
    "YARG.Core\YARG.Core\Song\Entries\SongEntry.cs",
    "YARG.Core\YARG.Core\Song\Entries\Types\SongMetadata.cs",
    "gamenight-yarg.ini",
    "GAMENIGHT-INTEGRATION.md",
    "build-gamenight-yarg.ps1",
    "BUILD-GAMENIGHT-YARG.bat"
)

foreach ($relativePath in $files) {
    $source = Join-Path $packageRoot $relativePath
    if (-not (Test-Path -LiteralPath $source)) {
        Write-Warning "Skipping missing package file: $relativePath"
        continue
    }

    $destination = Join-Path $targetRoot $relativePath
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

    if (Test-Path -LiteralPath $destination) {
        $backup = "$destination.gamenight-backup"
        Copy-Item -LiteralPath $destination -Destination $backup -Force
    }

    Copy-Item -LiteralPath $source -Destination $destination -Force
}

Write-Host "Gamenight YARG integration installed into:"
Write-Host $targetRoot
Write-Host ""
Write-Host "Next:"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$targetRoot\build-gamenight-yarg.ps1`""
