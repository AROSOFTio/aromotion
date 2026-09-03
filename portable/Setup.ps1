$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root=$PSScriptRoot
$ffmpegDir=Join-Path $root 'tools\ffmpeg'
$ffmpegExe=Join-Path $ffmpegDir 'ffmpeg.exe'

if(-not (Test-Path $ffmpegExe)){
    Write-Host ''
    Write-Host 'AROMOTION Studio - first run setup' -ForegroundColor Cyan
    Write-Host 'Downloading the local lossless recording engine (about 75 MB)...' -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ffmpegDir | Out-Null
    $temp=Join-Path $env:TEMP ('aromotion-'+[Guid]::NewGuid().ToString('N'))
    $zip=Join-Path $temp 'ffmpeg.zip'; $extract=Join-Path $temp 'extract'
    New-Item -ItemType Directory -Force -Path $temp,$extract | Out-Null
    try{
        $url='https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Write-Host 'Extracting recording engine...'
        Expand-Archive -Path $zip -DestinationPath $extract -Force
        $found=Get-ChildItem -Path $extract -Filter 'ffmpeg.exe' -File -Recurse | Select-Object -First 1
        if($null -eq $found){throw 'ffmpeg.exe was not found in the downloaded package.'}
        Copy-Item -Path (Join-Path $found.Directory.FullName '*') -Destination $ffmpegDir -Recurse -Force
    }finally{
        if(Test-Path $temp){Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue}
    }
}

if(-not (Test-Path $ffmpegExe)){throw 'The AROMOTION recording engine could not be installed.'}
Write-Host 'AROMOTION recording engine is ready.' -ForegroundColor Green
Write-Host 'Opening AROMOTION Studio...'
Start-Process powershell.exe -WorkingDirectory $root -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'Recorder.ps1'))
