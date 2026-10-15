# Builds the Overmark installer: runs the tests, makes a self-contained precompiled build, then packs it with
# Velopack into releases\ (Overmark-win-Setup.exe plus the packages updates download).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 0.2.0            # build the installer only
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 0.2.0 -Publish   # and publish to GitHub
#
# -Publish needs a GitHub token allowed to create releases, in the GITHUB_TOKEN environment variable.

param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$repoUrl = 'https://github.com/PartyLime99/overmark'
$publishDir = Join-Path $root 'publish'
$releaseDir = Join-Path $root 'releases'

function Run([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE) { throw "$what failed (exit code $LASTEXITCODE)" }
}

Push-Location $root
try {
    if ($Publish -and -not $env:GITHUB_TOKEN) { throw 'Set GITHUB_TOKEN to publish.' }

    Run 'Restoring tools' { dotnet tool restore }
    Run 'Tests' { dotnet test --nologo }

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    # Self-contained, so nobody needs .NET installed; ready-to-run, so the first capture after starting is quicker.
    Run 'Publishing' { dotnet publish src/Overmark/Overmark.csproj -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true "-p:Version=$Version" -o $publishDir }

    if ($Publish) {
        # The previous release, so Velopack can build a small "delta" update from it. There isn't one the first time.
        dotnet vpk download github --repoUrl $repoUrl --token $env:GITHUB_TOKEN --outputDir $releaseDir
        if ($LASTEXITCODE) { Write-Warning 'No previous release downloaded, so this one has no delta update.' }
    }

    Run 'Packing' {
        dotnet vpk pack --packId Overmark --packVersion $Version --packDir $publishDir --mainExe Overmark.exe `
            --packTitle Overmark --packAuthors 'Ben Fowler' --icon src/Overmark/Assets/Overmark.ico `
            --shortcuts StartMenuRoot --outputDir $releaseDir
    }

    if ($Publish) {
        Run 'Uploading to GitHub' {
            dotnet vpk upload github --repoUrl $repoUrl --token $env:GITHUB_TOKEN --outputDir $releaseDir `
                --publish --releaseName "Overmark $Version" --tag "v$Version"
        }
    }

    Write-Output "Done: $releaseDir\Overmark-win-Setup.exe"
}
finally {
    Pop-Location
}
