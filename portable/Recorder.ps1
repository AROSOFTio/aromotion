Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$root = $PSScriptRoot
$ffmpeg = Join-Path $root 'tools\ffmpeg\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) {
    [System.Windows.Forms.MessageBox]::Show('Run START-NOW.cmd first so AROMOTION can install its recording engine.','AROMOTION Studio',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

$script:proc = $null
$script:output = $null
$videos = [Environment]::GetFolderPath('MyVideos')
if ([string]::IsNullOrWhiteSpace($videos)) { $videos = [Environment]::GetFolderPath('MyDocuments') }
$defaultDir = Join-Path $videos 'AROMOTION Projects'
New-Item -ItemType Directory -Force -Path $defaultDir | Out-Null

$form = New-Object System.Windows.Forms.Form
$form.Text = 'AROMOTION Studio - Lossless Recorder M0'
$form.StartPosition = 'CenterScreen'
$form.ClientSize = New-Object System.Drawing.Size(760,460)
$form.BackColor = [System.Drawing.Color]::FromArgb(16,18,22)
$form.ForeColor = [System.Drawing.Color]::White
$form.Font = New-Object System.Drawing.Font('Segoe UI',10)

function Add-Label($text,$x,$y,$size=10,$bold=$false) {
    $c = New-Object System.Windows.Forms.Label
    $c.Text = $text; $c.AutoSize = $true; $c.Location = New-Object System.Drawing.Point($x,$y)
    if ($bold) { $c.Font = New-Object System.Drawing.Font('Segoe UI',$size,[System.Drawing.FontStyle]::Bold) }
    elseif ($size -ne 10) { $c.Font = New-Object System.Drawing.Font('Segoe UI',$size) }
    $form.Controls.Add($c); return $c
}

$title = Add-Label 'AROMOTION' 28 22 22 $true
$subtitle = Add-Label 'Lossless Screen Recorder - immediate Windows build' 31 61 10 $false
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(165,174,190)
$status = Add-Label 'READY' 665 32 10 $true
$status.ForeColor = [System.Drawing.Color]::FromArgb(130,220,165)

Add-Label 'Project folder' 31 105 | Out-Null
$folder = New-Object System.Windows.Forms.TextBox
$folder.Text = $defaultDir; $folder.Location = New-Object System.Drawing.Point(34,130); $folder.Size = New-Object System.Drawing.Size(570,28)
$form.Controls.Add($folder)
$browse = New-Object System.Windows.Forms.Button
$browse.Text='Browse...'; $browse.Location=New-Object System.Drawing.Point(616,128); $browse.Size=New-Object System.Drawing.Size(105,31)
$browse.Add_Click({ $d=New-Object System.Windows.Forms.FolderBrowserDialog; $d.SelectedPath=$folder.Text; if($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$folder.Text=$d.SelectedPath} })
$form.Controls.Add($browse)

Add-Label 'Quality' 31 182 | Out-Null
$quality = New-Object System.Windows.Forms.ComboBox
$quality.DropDownStyle='DropDownList'; [void]$quality.Items.Add('FFV1 - mathematically lossless (recommended)'); [void]$quality.Items.Add('H.264 RGB CRF 0 - lossless RGB'); $quality.SelectedIndex=0
$quality.Location=New-Object System.Drawing.Point(34,207); $quality.Size=New-Object System.Drawing.Size(440,30); $form.Controls.Add($quality)

Add-Label 'FPS' 500 182 | Out-Null
$fps = New-Object System.Windows.Forms.ComboBox
$fps.DropDownStyle='DropDownList'; [void]$fps.Items.Add('30'); [void]$fps.Items.Add('60'); $fps.SelectedIndex=1
$fps.Location=New-Object System.Drawing.Point(503,207); $fps.Size=New-Object System.Drawing.Size(85,30); $form.Controls.Add($fps)

$cursor = New-Object System.Windows.Forms.CheckBox
$cursor.Text='Record visible mouse cursor'; $cursor.Checked=$true; $cursor.AutoSize=$true; $cursor.Location=New-Object System.Drawing.Point(34,255); $form.Controls.Add($cursor)

$record = New-Object System.Windows.Forms.Button
$record.Text='●  RECORD LOSSLESS'; $record.Font=New-Object System.Drawing.Font('Segoe UI',12,[System.Drawing.FontStyle]::Bold); $record.Location=New-Object System.Drawing.Point(34,300); $record.Size=New-Object System.Drawing.Size(325,58); $record.FlatStyle='Flat'; $record.BackColor=[System.Drawing.Color]::FromArgb(210,62,74); $record.ForeColor=[System.Drawing.Color]::White
$form.Controls.Add($record)
$stop = New-Object System.Windows.Forms.Button
$stop.Text='■  STOP & SAVE'; $stop.Font=New-Object System.Drawing.Font('Segoe UI',12,[System.Drawing.FontStyle]::Bold); $stop.Location=New-Object System.Drawing.Point(396,300); $stop.Size=New-Object System.Drawing.Size(325,58); $stop.FlatStyle='Flat'; $stop.Enabled=$false
$form.Controls.Add($stop)

$note = Add-Label 'M0 records a pixel-perfect lossless desktop master. Audio, auto-zoom, separate cursor, 3D motion and the editor are being added to the compiled AROMOTION build.' 34 392 9 $false
$note.MaximumSize = New-Object System.Drawing.Size(680,45); $note.AutoSize=$true; $note.ForeColor=[System.Drawing.Color]::FromArgb(155,164,180)

function Set-RecordingUi([bool]$isRecording) {
    $record.Enabled = -not $isRecording; $stop.Enabled = $isRecording
    $folder.Enabled = -not $isRecording; $browse.Enabled = -not $isRecording; $quality.Enabled = -not $isRecording; $fps.Enabled = -not $isRecording; $cursor.Enabled = -not $isRecording
    if ($isRecording) { $status.Text='● RECORDING'; $status.ForeColor=[System.Drawing.Color]::FromArgb(255,95,105) }
    else { $status.Text='READY'; $status.ForeColor=[System.Drawing.Color]::FromArgb(130,220,165) }
}

function Stop-AROMOTIONRecording {
    if ($null -ne $script:proc) {
        try {
            if (-not $script:proc.HasExited) {
                $script:proc.StandardInput.WriteLine('q'); $script:proc.StandardInput.Flush()
                if (-not $script:proc.WaitForExit(8000)) { $script:proc.Kill(); $script:proc.WaitForExit() }
            }
        } catch {}
        try { $script:proc.Dispose() } catch {}
        $script:proc=$null
    }
    Set-RecordingUi $false
    $status.Text='SAVED'
    if ($script:output -and (Test-Path $script:output)) {
        $r=[System.Windows.Forms.MessageBox]::Show("Lossless master saved:`r`n$script:output`r`n`r`nOpen its folder?",'AROMOTION Studio',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Information)
        if($r -eq [System.Windows.Forms.DialogResult]::Yes){ Start-Process explorer.exe -ArgumentList "/select,`"$script:output`"" }
    }
}

$record.Add_Click({
    try {
        $base=$folder.Text.Trim(); if([string]::IsNullOrWhiteSpace($base)){throw 'Choose a project folder.'}; New-Item -ItemType Directory -Force -Path $base | Out-Null
        $session=Join-Path $base (Get-Date -Format 'yyyy-MM-dd_HHmmss'); New-Item -ItemType Directory -Force -Path $session | Out-Null
        $script:output=Join-Path $session 'master.mkv'
        $f=$fps.SelectedItem.ToString(); $mouse=if($cursor.Checked){'1'}else{'0'}
        if($quality.SelectedIndex -eq 0){$codec='-c:v ffv1 -level 3 -coder 1 -g 1'}else{$codec='-c:v libx264rgb -crf 0 -preset ultrafast'}
        $safeOut='"' + $script:output.Replace('"','\"') + '"'
        $argLine="-hide_banner -y -f gdigrab -framerate $f -draw_mouse $mouse -i desktop $codec -f matroska $safeOut"
        $psi=New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName=$ffmpeg; $psi.Arguments=$argLine; $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true; $psi.RedirectStandardInput=$true
        $script:proc=New-Object System.Diagnostics.Process; $script:proc.StartInfo=$psi
        if(-not $script:proc.Start()){throw 'Could not start the recording engine.'}
        Start-Sleep -Milliseconds 400
        if($script:proc.HasExited){throw "Recording engine stopped immediately (exit code $($script:proc.ExitCode))."}
        Set-RecordingUi $true
    } catch {
        if($null -ne $script:proc){try{$script:proc.Dispose()}catch{};$script:proc=$null}
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'AROMOTION recorder error',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
})
$stop.Add_Click({Stop-AROMOTIONRecording})
$form.Add_FormClosing({if($null -ne $script:proc -and -not $script:proc.HasExited){$r=[System.Windows.Forms.MessageBox]::Show('Stop and save the current recording before closing?','AROMOTION Studio',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question);if($r -eq [System.Windows.Forms.DialogResult]::Yes){Stop-AROMOTIONRecording}else{$_.Cancel=$true}}})

[void]$form.ShowDialog()
