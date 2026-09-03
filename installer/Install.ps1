$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceFile = Join-Path $PSScriptRoot 'AROMOTION.cs'
$target = Join-Path $env:LOCALAPPDATA 'Programs\AROMOTION'
$toolTarget = Join-Path $target 'tools\ffmpeg'
$exe = Join-Path $target 'AROMOTION.exe'
$uninstall = Join-Path $target 'Uninstall-AROMOTION.ps1'

Write-Host ''
Write-Host '=============================================' -ForegroundColor DarkCyan
Write-Host '         AROMOTION STUDIO INSTALLER' -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor DarkCyan
Write-Host 'Installed app: Recorder + Mouse Motion Engine' -ForegroundColor Gray
Write-Host ''

if (-not (Test-Path $sourceFile)) { throw "Installer source is missing: $sourceFile" }
New-Item -ItemType Directory -Force -Path $target,$toolTarget | Out-Null

# Reuse the FFmpeg engine already downloaded by the M0 portable build when possible.
$existingCandidates = @(
    (Join-Path $repoRoot 'portable\tools\ffmpeg\ffmpeg.exe'),
    (Join-Path $PSScriptRoot 'tools\ffmpeg\ffmpeg.exe')
)
$reused = $false
foreach ($candidate in $existingCandidates) {
    if (Test-Path $candidate) {
        $sourceDir = Split-Path $candidate -Parent
        Write-Host 'Reusing the recording engine already on this PC...' -ForegroundColor Green
        Copy-Item (Join-Path $sourceDir '*') $toolTarget -Recurse -Force
        $reused = $true
        break
    }
}

$ffmpeg = Join-Path $toolTarget 'ffmpeg.exe'
$ffprobe = Join-Path $toolTarget 'ffprobe.exe'
if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe)) {
    Write-Host 'Downloading the AROMOTION media engine (one-time)...' -ForegroundColor Yellow
    $temp = Join-Path $env:TEMP ('aromotion-installer-' + [Guid]::NewGuid().ToString('N'))
    $zip = Join-Path $temp 'ffmpeg.zip'
    $extract = Join-Path $temp 'extract'
    New-Item -ItemType Directory -Force -Path $temp,$extract | Out-Null
    try {
        $url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip $extract -Force
        $found = Get-ChildItem $extract -Filter ffmpeg.exe -Recurse -File | Select-Object -First 1
        if ($null -eq $found) { throw 'ffmpeg.exe was not found in the downloaded package.' }
        Copy-Item (Join-Path $found.Directory.FullName '*') $toolTarget -Recurse -Force
    }
    finally {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe)) { throw 'AROMOTION media engine installation failed.' }

Write-Host 'Preparing AROMOTION.exe...' -ForegroundColor Cyan
$buildSource = Join-Path $env:TEMP ('AROMOTION-' + [Guid]::NewGuid().ToString('N') + '.cs')
$src = Get-Content $sourceFile -Raw
# Phase-1 source compatibility patches for the inbox .NET Framework compiler.
$src = $src.Replace('Environment.GetFolderPath', 'System.Environment.GetFolderPath')
$src = $src.Replace('Environment.SpecialFolder', 'System.Environment.SpecialFolder')
$src = $src.Replace('long age = Environment.TickCount64Compat() - capture.LastClickMs; // replaced below by safe approximation at runtime', 'long age = 0;')
Set-Content -Path $buildSource -Value $src -Encoding UTF8

$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'Windows .NET Framework compiler was not found. Enable .NET Framework 4.8 and run this installer again.' }

$compilerArgs = @(
    '/nologo','/target:winexe','/optimize+','/platform:anycpu',
    "/out:$exe",
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
    $buildSource
)
$compiler = Start-Process -FilePath $csc -ArgumentList $compilerArgs -Wait -PassThru -NoNewWindow
Remove-Item $buildSource -Force -ErrorAction SilentlyContinue
if ($compiler.ExitCode -ne 0 -or -not (Test-Path $exe)) { throw "AROMOTION.exe compilation failed with exit code $($compiler.ExitCode)." }

$uninstallContent = @'
$ErrorActionPreference='SilentlyContinue'
$target = Join-Path $env:LOCALAPPDATA 'Programs\AROMOTION'
$desktop = [Environment]::GetFolderPath('Desktop')
$start = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
Remove-Item (Join-Path $desktop 'AROMOTION Studio.lnk') -Force
Remove-Item (Join-Path $start 'AROMOTION Studio.lnk') -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AROMOTION Studio' -Recurse -Force
Start-Sleep -Milliseconds 400
Remove-Item $target -Recurse -Force
'@
Set-Content $uninstall $uninstallContent -Encoding UTF8

# Shortcuts.
$ws = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$start = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
foreach ($link in @((Join-Path $desktop 'AROMOTION Studio.lnk'),(Join-Path $start 'AROMOTION Studio.lnk'))) {
    $sc = $ws.CreateShortcut($link)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $target
    $sc.Description = 'AROMOTION Studio — screen recorder and motion editor'
    $sc.Save()
}

# Windows Installed Apps / uninstall registration (per-user, no admin required).
$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AROMOTION Studio'
New-Item $reg -Force | Out-Null
New-ItemProperty $reg -Name DisplayName -Value 'AROMOTION Studio' -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name DisplayVersion -Value '0.1.0-phase1' -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name Publisher -Value 'AROSOFT Innovations Ltd' -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name InstallLocation -Value $target -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name DisplayIcon -Value $exe -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstall`"" -PropertyType String -Force | Out-Null
New-ItemProperty $reg -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty $reg -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

Write-Host ''
Write-Host 'AROMOTION Studio installed successfully.' -ForegroundColor Green
Write-Host "Location: $target" -ForegroundColor Gray
Write-Host 'A Desktop shortcut and Start Menu shortcut were created.' -ForegroundColor Gray
Write-Host ''
Write-Host 'Opening AROMOTION Studio...' -ForegroundColor Cyan
Start-Process $exe
