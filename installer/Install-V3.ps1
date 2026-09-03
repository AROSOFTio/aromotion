$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Stage($text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

try {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\AROMOTION'
    $tools = Join-Path $target 'tools\ffmpeg'
    $exe = Join-Path $target 'AROMOTION.exe'
    $compileLog = Join-Path $target 'compile.log'
    $installLog = Join-Path $target 'install.log'
    $downloads = Join-Path $env:USERPROFILE 'Downloads'

    New-Item -ItemType Directory -Force -Path $target,$tools | Out-Null
    "AROMOTION Installer V3 - $(Get-Date -Format o)" | Set-Content $installLog -Encoding UTF8

    Stage "Getting the current AROMOTION Phase 1 source"
    $source = Join-Path $target 'AROMOTION.cs'
    $sourceUrl = 'https://raw.githubusercontent.com/AROSOFTio/aromotion/main/installer/AROMOTION.cs'
    Invoke-WebRequest -Uri $sourceUrl -OutFile $source -UseBasicParsing
    if (-not (Test-Path $source)) { throw "Could not download AROMOTION.cs." }
    Write-Host "Source ready." -ForegroundColor Green

    Stage "Preparing source for the Windows inbox compiler"
    $src = Get-Content $source -Raw
    $src = $src.Replace('using System.Threading;', '')
    $src = $src.Replace('Thread.Sleep(', 'System.Threading.Thread.Sleep(')
    $src = $src.Replace(
        'readonly MouseCapture capture; readonly bool halo, pulse; readonly Timer timer = new Timer();',
        'readonly MouseCapture capture; readonly bool halo, pulse; readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();'
    )
    $src = $src.Replace('Environment.GetFolderPath', 'System.Environment.GetFolderPath')
    $src = $src.Replace('Environment.SpecialFolder', 'System.Environment.SpecialFolder')
    $src = $src.Replace(
        'public Point CursorPoint { get; private set; } public long LastClickMs { get; private set; }',
        'public Point CursorPoint { get; private set; } public long LastClickMs { get; private set; } public long ElapsedMs { get { return clock.ElapsedMilliseconds; } }'
    )
    $src = $src.Replace(
        'long age = Environment.TickCount64Compat() - capture.LastClickMs; // replaced below by safe approximation at runtime',
        'long age = capture.ElapsedMs - capture.LastClickMs;'
    )
    $src = $src.Replace(
        'age = 0; // hook time and overlay clock are intentionally not mixed; pulse is drawn as a small persistent click marker in Phase 1.',
        ''
    )
    $src = $src.Replace(
        'if (capture.LastClickMs > 0) using (var pen = new Pen(Color.FromArgb(220,255,95,70),3)) e.Graphics.DrawEllipse(pen,p.X-16,p.Y-16,32,32);',
        'if (capture.LastClickMs > 0 && age >= 0 && age < 500) { int r = 14 + (int)(age / 18); int alpha = Math.Max(35, 230 - (int)(age * 0.38)); using (var pen = new Pen(Color.FromArgb(alpha,255,95,70),3)) e.Graphics.DrawEllipse(pen,p.X-r,p.Y-r,r*2,r*2); }'
    )
    Set-Content -Path $source -Value $src -Encoding UTF8

    Stage "Locating the recording engine you already downloaded"
    $candidates = @(
        (Join-Path $downloads 'aromotion-main\aromotion-main\portable\tools\ffmpeg\ffmpeg.exe'),
        (Join-Path $downloads 'aromotion-main\portable\tools\ffmpeg\ffmpeg.exe'),
        (Join-Path $downloads 'aromotion-main (1)\aromotion-main\portable\tools\ffmpeg\ffmpeg.exe'),
        (Join-Path $downloads 'aromotion-main (1)\portable\tools\ffmpeg\ffmpeg.exe')
    )

    $existing = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($existing) {
        Write-Host "Found existing engine:" -ForegroundColor Green
        Write-Host $existing -ForegroundColor Gray
        $sourceTools = Split-Path $existing -Parent
        Copy-Item (Join-Path $sourceTools '*') $tools -Recurse -Force
    } else {
        Write-Host "Existing engine was not found at the known AROMOTION paths." -ForegroundColor Yellow
        Write-Host "Downloading it once..." -ForegroundColor Yellow

        $temp = Join-Path $env:TEMP ('aromotion-ffmpeg-' + [guid]::NewGuid().ToString('N'))
        $zip = Join-Path $temp 'ffmpeg.zip'
        $extract = Join-Path $temp 'extract'
        New-Item -ItemType Directory -Force -Path $temp,$extract | Out-Null
        try {
            $url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
            Expand-Archive -Path $zip -DestinationPath $extract -Force
            $found = Get-ChildItem $extract -Filter ffmpeg.exe -File -Recurse | Select-Object -First 1
            if (-not $found) { throw 'ffmpeg.exe was not found in the downloaded archive.' }
            Copy-Item (Join-Path $found.Directory.FullName '*') $tools -Recurse -Force
        }
        finally {
            Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $ffmpeg = Join-Path $tools 'ffmpeg.exe'
    $ffprobe = Join-Path $tools 'ffprobe.exe'
    if (-not (Test-Path $ffmpeg)) { throw "FFmpeg installation failed: $ffmpeg not found." }
    if (-not (Test-Path $ffprobe)) { throw "FFprobe installation failed: $ffprobe not found." }
    Write-Host "Media engine ready." -ForegroundColor Green

    Stage "Compiling AROMOTION.exe"
    $cscCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $csc) {
        throw "Windows C# compiler was not found. .NET Framework 4.x is required."
    }

    if (Test-Path $exe) { Remove-Item $exe -Force }

    $compilerArgs = @(
        '/nologo',
        '/target:winexe',
        '/optimize+',
        '/platform:anycpu',
        "/out:$exe",
        '/reference:System.dll',
        '/reference:System.Core.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        $source
    )

    $compilerOutput = & $csc @compilerArgs 2>&1
    $compilerOutput | Tee-Object -FilePath $compileLog | ForEach-Object { Write-Host $_ }
    $compileExit = $LASTEXITCODE

    if ($compileExit -ne 0 -or -not (Test-Path $exe)) {
        Write-Host ""
        Write-Host "COMPILER FAILED. Log: $compileLog" -ForegroundColor Red
        if (Test-Path $compileLog) { Start-Process notepad.exe $compileLog }
        throw "AROMOTION.exe was not created. Compiler exit code: $compileExit"
    }

    $exeInfo = Get-Item $exe
    Write-Host ("AROMOTION.exe created: {0:N0} bytes" -f $exeInfo.Length) -ForegroundColor Green

    Stage "Creating Windows shortcuts"
    $ws = New-Object -ComObject WScript.Shell
    $desktop = [System.Environment]::GetFolderPath('Desktop')
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

    foreach ($shortcutPath in @(
        (Join-Path $desktop 'AROMOTION Studio.lnk'),
        (Join-Path $startMenu 'AROMOTION Studio.lnk')
    )) {
        $sc = $ws.CreateShortcut($shortcutPath)
        $sc.TargetPath = $exe
        $sc.WorkingDirectory = $target
        $sc.Description = 'AROMOTION Studio'
        $sc.Save()
    }

    Stage "Registering AROMOTION in Windows Installed Apps"
    $uninstall = Join-Path $target 'Uninstall-AROMOTION.ps1'
    @"
`$ErrorActionPreference='SilentlyContinue'
`$target=Join-Path `$env:LOCALAPPDATA 'Programs\AROMOTION'
`$desktop=[System.Environment]::GetFolderPath('Desktop')
`$start=Join-Path `$env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
Remove-Item (Join-Path `$desktop 'AROMOTION Studio.lnk') -Force
Remove-Item (Join-Path `$start 'AROMOTION Studio.lnk') -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AROMOTION Studio' -Recurse -Force
Start-Sleep -Milliseconds 500
Remove-Item `$target -Recurse -Force
"@ | Set-Content $uninstall -Encoding UTF8

    $reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AROMOTION Studio'
    New-Item $reg -Force | Out-Null
    New-ItemProperty $reg -Name DisplayName -Value 'AROMOTION Studio' -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name DisplayVersion -Value '0.1.0-phase1-v3' -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name Publisher -Value 'AROSOFT Innovations Ltd' -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name InstallLocation -Value $target -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name DisplayIcon -Value $exe -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstall`"" -PropertyType String -Force | Out-Null
    New-ItemProperty $reg -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $reg -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

    "SUCCESS - $(Get-Date -Format o)`r`nEXE=$exe" | Add-Content $installLog -Encoding UTF8

    Stage "Installation verified"
    Write-Host "Installed EXE:" -ForegroundColor Green
    Write-Host $exe -ForegroundColor White
    Write-Host ""
    Write-Host "Opening AROMOTION Studio..." -ForegroundColor Cyan
    Start-Process $exe

    exit 0
}
catch {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "AROMOTION INSTALLATION FAILED" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host ""
    if ($installLog) {
        ("FAILED - $(Get-Date -Format o)`r`n" + $_.Exception.ToString()) | Add-Content $installLog -Encoding UTF8 -ErrorAction SilentlyContinue
        Write-Host "Install log: $installLog" -ForegroundColor Gray
    }
    if ($compileLog -and (Test-Path $compileLog)) {
        Write-Host "Compile log: $compileLog" -ForegroundColor Gray
    }
    exit 1
}
