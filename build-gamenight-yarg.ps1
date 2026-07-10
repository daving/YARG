param(
    [string] $UnityPath = "",
    [string] $OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "Build\Gamenight-Windows"
}

function Find-Unity {
    param([string] $RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath)) {
            throw "UnityPath does not exist: $RequestedPath"
        }
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $hubRoot = "C:\Program Files\Unity\Hub\Editor"
    if (Test-Path -LiteralPath $hubRoot) {
        $candidate = Get-ChildItem -LiteralPath $hubRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw "Could not find Unity.exe. Pass -UnityPath `"C:\Path\To\Unity.exe`"."
}

function Ensure-DotNet9 {
    $dotnetRoot = Join-Path $env:USERPROFILE ".dotnet"
    $runtimePath = Join-Path $dotnetRoot "shared\Microsoft.NETCore.App\9.0.0"

    if (-not (Test-Path -LiteralPath $runtimePath)) {
        $installer = Join-Path $projectRoot "dotnet-install.ps1"
        if (-not (Test-Path -LiteralPath $installer)) {
            Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
        }
        & $installer -Runtime dotnet -Version 9.0.0 -InstallDir $dotnetRoot
    }

    $env:DOTNET_ROOT = $dotnetRoot
    if ($env:PATH -notlike "*$dotnetRoot*") {
        $env:PATH = "$env:PATH;$dotnetRoot"
    }
}

function Invoke-Checked {
    param(
        [string] $FilePath,
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Restore-YargDependencies {
    Push-Location $projectRoot
    try {
        Invoke-Checked "git" @("submodule", "update", "--init", "--recursive")

        Ensure-DotNet9

        $dotnet = "C:\Program Files\dotnet\dotnet.exe"
        if (-not (Test-Path -LiteralPath $dotnet)) {
            $dotnet = "dotnet"
        }

        $tools = & $dotnet tool list --global
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to list .NET tools."
        }
        if ($tools -notmatch "nugetforunity.cli") {
            Invoke-Checked $dotnet @("tool", "install", "--global", "NuGetForUnity.Cli")
        }

        Invoke-Checked "nugetforunity" @("restore")

        Remove-Item -LiteralPath (Join-Path $projectRoot "Assets\Packages\Microsoft.VisualStudio.SolutionPersistence.1.0.52") -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $projectRoot "Assets\Packages\Microsoft.VisualStudio.SolutionPersistence.1.0.52.meta") -Force -ErrorAction SilentlyContinue
    }
    finally {
        Pop-Location
    }
}

function Run-Unity {
    param(
        [string] $UnityExe,
        [string[]] $UnityArgs,
        [string] $LogFile
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $UnityExe
    $startInfo.UseShellExecute = $false
    $allArgs = @($UnityArgs) + @("-logFile", $LogFile)
    $startInfo.Arguments = ($allArgs | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " "

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) {
        Write-Warning "Unity exited with code $exitCode. See log: $LogFile"
    }
    return $exitCode
}

function ConvertTo-CommandLineArgument {
    param([string] $Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '\\(?=\\*")', '$0$0' -replace '"', '\"') + '"'
}

$unityExe = Find-Unity $UnityPath
$buildExe = Join-Path $OutputDirectory "YARG.exe"
$compileLog = Join-Path $projectRoot "unity-compile.log"
$buildLog = Join-Path $projectRoot "unity-build.log"

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "Restoring YARG dependencies..."
Restore-YargDependencies

Write-Host "Running Unity compile/import..."
$compileExit = Run-Unity $unityExe @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath",
    $projectRoot
) $compileLog

if ($compileExit -ne 0) {
    throw "Unity compile/import failed. See $compileLog"
}

Write-Host "Building Windows player..."
$buildExit = Run-Unity $unityExe @(
    "-batchmode",
    "-nographics",
    "-projectPath",
    $projectRoot,
    "-gamenightAutobuild",
    "-gamenightOutput",
    $buildExe
) $buildLog

if ((Test-Path -LiteralPath $buildExe) -and $buildExit -eq 0) {
    Write-Host "Build complete:"
    Write-Host $OutputDirectory
    Write-Host ""
    Write-Host "Copy gamenight-yarg.ini next to YARG.exe and edit the server/Home Assistant URLs if needed."
    Copy-Item -LiteralPath (Join-Path $projectRoot "gamenight-yarg.ini") -Destination (Join-Path $OutputDirectory "gamenight-yarg.ini") -Force
    exit 0
}

Write-Warning "The automated Unity build did not create YARG.exe."
Write-Host ""
Write-Host "Manual fallback:"
Write-Host "1. Open this folder in Unity Hub:"
Write-Host "   $projectRoot"
Write-Host "2. Let Unity finish importing/compiling."
Write-Host "3. Use File > Build Profiles, select Windows, and build to:"
Write-Host "   $OutputDirectory"
Write-Host "4. Copy gamenight-yarg.ini next to the built YARG.exe and edit the server/Home Assistant URLs."
Write-Host ""
Write-Host "The compile/import step already passed if no exception was thrown above."
exit 1
