<#
Builds the Windows release of Capone Reborn into builds\CaponeReborn-<version> and zips it to
builds\CaponeReborn-<version>-win64.zip. The version is application/config/version in project.godot.

Needs the Godot 4.7.2 .NET editor (-Godot, or the GODOT environment variable) with its .NET export templates
installed (Godot editor: Editor > Manage Export Templates). The package holds the game, the files players may edit
(game.cfg, Data\*.json, original.cfg.example), a readme, the licence (LICENSE.txt, GPL-3.0-or-later) and the
engine's licence notices. It holds no original game files and no original.cfg: each player picks their Capone
folder on the first start.

    powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 [-Godot <path to the Godot .NET editor>]
#>
param(
    [string]$Godot = $env:GODOT
)

$project = Split-Path -Parent $PSScriptRoot
if (-not $Godot) {
    $Godot = Join-Path $project '..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
}
if (-not (Test-Path $Godot)) {
    Write-Error "Godot .NET editor not found: $Godot (pass -Godot or set GODOT)"
    exit 1
}

$versionLine = Select-String -Path (Join-Path $project 'project.godot') -Pattern '^config/version="(.+)"' | Select-Object -First 1
if (-not $versionLine) {
    Write-Error 'project.godot has no application/config/version'
    exit 1
}
$version = $versionLine.Matches[0].Groups[1].Value

$builds = Join-Path $project 'builds'
$out = Join-Path $builds "CaponeReborn-$version"
$zip = Join-Path $builds "CaponeReborn-$version-win64.zip"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

# Godot's .NET build runs MSBuild, whose helper processes may stay alive for minutes and keep an inherited output
# pipe open, so a caller reading the script's output waits for them. They are told not to stay, and Godot's output
# goes to log files in builds\ instead of a pipe; a Godot run that takes over $Minutes minutes is stopped.
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
function Invoke-Godot([string]$Name, [string]$Arguments, [int]$Minutes = 10) {
    $log = Join-Path $builds "$Name.log"
    $p = Start-Process -FilePath $Godot -ArgumentList $Arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $null = $p.Handle  # keeps ExitCode readable after the wait (Windows PowerShell)
    if (-not $p.WaitForExit($Minutes * 60000)) {
        & taskkill.exe /T /F /PID $p.Id | Out-Null
        Write-Error "Godot ($Name) didn't finish in $Minutes minutes and was stopped; see $log"
        exit 1
    }
    if ($p.ExitCode -ne 0) {
        Get-Content $log, "$log.err" -Tail 15 -ErrorAction SilentlyContinue | Write-Host
    }
    return $p.ExitCode
}

Write-Host "Exporting Capone Reborn $version to $out ..."
$exe = Join-Path $out 'CaponeReborn.exe'
$code = Invoke-Godot 'export' "--headless --path `"$project`" --export-release `"Windows Desktop`" `"$exe`""
if ($code -ne 0 -or -not (Test-Path $exe)) {
    Write-Error "Godot export failed (exit code $code; log builds\export.log). Are the 4.7.2 .NET export templates installed?"
    exit 1
}

# The files players may edit sit next to the game (Scripts/GameFiles.cs); the export leaves them out of its pack.
Copy-Item (Join-Path $project 'game.cfg') $out
New-Item -ItemType Directory -Force (Join-Path $out 'Data') | Out-Null
Copy-Item (Join-Path $project 'Data\*.json') (Join-Path $out 'Data')
Copy-Item (Join-Path $project 'originalData\original.cfg.example') (Join-Path $out 'original.cfg.example')
Copy-Item (Join-Path $project 'LICENSE') (Join-Path $out 'LICENSE.txt')
$readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'release\README.txt'), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $out 'README.txt'), $readme.Replace('{VERSION}', $version), (New-Object Text.UTF8Encoding $true))

$notices = Join-Path $out 'THIRD_PARTY_LICENSES.txt'
$code = Invoke-Godot 'licenses' "--headless --path `"$project`" --script res://tools/licenses.gd -- `"$notices`""
if (-not (Test-Path $notices)) {
    Write-Error "Writing THIRD_PARTY_LICENSES.txt failed (exit code $code; log builds\licenses.log)"
    exit 1
}

# Never ship a machine's own game path.
$ownCfg = Join-Path $out 'original.cfg'
if (Test-Path $ownCfg) { Remove-Item $ownCfg -Force }

if (Test-Path $zip) { Remove-Item $zip -Force }
# Entries named with forward slashes: Windows PowerShell 5.1 (Compress-Archive, ZipFile.CreateFromDirectory) stores
# backslashes, which unzip tools outside Windows mishandle.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zip, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
foreach ($file in Get-ChildItem $out -Recurse -File) {
    $name = $file.FullName.Substring($builds.Length).TrimStart('\').Replace('\', '/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name,
        [IO.Compression.CompressionLevel]::Optimal)
}
$archive.Dispose()
$stream.Dispose()
Write-Host "Done: $out"
Write-Host "      $zip"
