param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path $PSScriptRoot -Parent
$sdkRoot = Join-Path $env:LOCALAPPDATA 'AROMOTION-SDK'
$dotnet = Join-Path $sdkRoot 'dotnet.exe'
$output = Join-Path $repoRoot 'artifacts\AROMOTION-no-admin-win-x64'

Write-Host ''
Write-Host 'AROMOTION no-admin Windows build' -ForegroundColor Cyan
Write-Host 'Nothing is installed into Program Files and no elevation is requested.' -ForegroundColor Gray
Write-Host ''

if (-not (Test-Path $dotnet)) {
    Write-Host 'Installing .NET 8 SDK into your user profile...' -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
    $installScript = Join-Path $env:TEMP 'dotnet-install-aromotion.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -UseBasicParsing
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript -Channel 8.0 -InstallDir $sdkRoot -NoPath
    if (-not (Test-Path $dotnet)) { throw '.NET SDK local installation failed.' }
}

Write-Host "Using: $dotnet" -ForegroundColor Green
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$project = Join-Path $repoRoot 'src\AroMotion.App\AroMotion.App.csproj'
Write-Host 'Restoring packages...' -ForegroundColor Cyan
& $dotnet restore $project -r win-x64 -p:EnableWindowsTargeting=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Host 'Publishing self-contained Windows x64 build...' -ForegroundColor Cyan
& $dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:EnableWindowsTargeting=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host 'Bundling FFmpeg...' -ForegroundColor Cyan
$ffmpegTarget = Join-Path $output 'tools\ffmpeg'
New-Item -ItemType Directory -Force -Path $ffmpegTarget | Out-Null
$ffzip = Join-Path $env:TEMP 'aromotion-ffmpeg.zip'
$ffextract = Join-Path $env:TEMP 'aromotion-ffmpeg-extract'
Remove-Item $ffzip -Force -ErrorAction SilentlyContinue
Remove-Item $ffextract -Recurse -Force -ErrorAction SilentlyContinue
Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $ffzip -UseBasicParsing
Expand-Archive $ffzip $ffextract -Force
$ffmpeg = Get-ChildItem $ffextract -Filter ffmpeg.exe -Recurse -File | Select-Object -First 1
$ffprobe = Get-ChildItem $ffextract -Filter ffprobe.exe -Recurse -File | Select-Object -First 1
if (-not $ffmpeg -or -not $ffprobe) { throw 'FFmpeg download did not contain ffmpeg.exe and ffprobe.exe.' }
Copy-Item $ffmpeg.FullName (Join-Path $ffmpegTarget 'ffmpeg.exe') -Force
Copy-Item $ffprobe.FullName (Join-Path $ffmpegTarget 'ffprobe.exe') -Force
Remove-Item $ffzip -Force -ErrorAction SilentlyContinue
Remove-Item $ffextract -Recurse -Force -ErrorAction SilentlyContinue

$exe = Join-Path $output 'AROMOTION.exe'
if (-not (Test-Path $exe)) { throw "Build finished but AROMOTION.exe was not found: $exe" }

Write-Host ''
Write-Host 'SUCCESS' -ForegroundColor Green
Write-Host "Portable build: $output" -ForegroundColor White
Write-Host 'No administrator rights are required to run this build.' -ForegroundColor Gray
Start-Process explorer.exe -ArgumentList $output
