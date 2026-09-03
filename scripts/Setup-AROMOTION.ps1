$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root 'AROMOTION.exe'))) {
    $Root = $PSScriptRoot
}

$FfmpegDir = Join-Path $Root 'tools\ffmpeg'
$FfmpegExe = Join-Path $FfmpegDir 'ffmpeg.exe'
$AppExe = Join-Path $Root 'AROMOTION.exe'

Write-Host ''
Write-Host 'AROMOTION Studio - first run setup' -ForegroundColor Cyan
Write-Host '------------------------------------'

if (-not (Test-Path $AppExe)) {
    throw "AROMOTION.exe was not found in $Root"
}

if (-not (Test-Path $FfmpegExe)) {
    Write-Host 'Installing the local FFmpeg recording engine...' -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $FfmpegDir | Out-Null

    $TempRoot = Join-Path $env:TEMP ('aromotion-ffmpeg-' + [Guid]::NewGuid().ToString('N'))
    $ZipPath = Join-Path $TempRoot 'ffmpeg.zip'
    $ExtractPath = Join-Path $TempRoot 'extract'
    New-Item -ItemType Directory -Force -Path $TempRoot, $ExtractPath | Out-Null

    try {
        $Url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
        Write-Host 'Downloading FFmpeg (about 75 MB)...'
        Invoke-WebRequest -Uri $Url -OutFile $ZipPath -UseBasicParsing

        Write-Host 'Extracting...'
        Expand-Archive -Path $ZipPath -DestinationPath $ExtractPath -Force

        $Found = Get-ChildItem -Path $ExtractPath -Filter 'ffmpeg.exe' -File -Recurse | Select-Object -First 1
        if ($null -eq $Found) {
            throw 'The FFmpeg package downloaded, but ffmpeg.exe was not found.'
        }

        Copy-Item -Path (Join-Path $Found.Directory.FullName '*') -Destination $FfmpegDir -Recurse -Force
    }
    finally {
        if (Test-Path $TempRoot) {
            Remove-Item -Path $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path $FfmpegExe)) {
        throw 'FFmpeg setup did not complete successfully.'
    }

    Write-Host 'Recording engine installed.' -ForegroundColor Green
}
else {
    Write-Host 'Recording engine is ready.' -ForegroundColor Green
}

Write-Host 'Starting AROMOTION Studio...' -ForegroundColor Cyan
Start-Process -FilePath $AppExe -WorkingDirectory $Root
