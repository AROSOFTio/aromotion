Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$script:RecorderProcess = $null
$script:CurrentOutput = $null
$script:StartedAt = $null

$Root = $PSScriptRoot
$Ffmpeg = Join-Path $Root 'tools\ffmpeg\ffmpeg.exe'
if (-not (Test-Path $Ffmpeg)) {
    [System.Windows.Forms.MessageBox]::Show(
        'FFmpeg is not installed in the AROMOTION folder. Close this window and run START-NOW.cmd first.',
        'AROMOTION Studio',
        'OK',
        'Error'
    ) | Out-Null
    exit 1
}

$Videos = [Environment]::GetFolderPath('MyVideos')
if ([string]::IsNullOrWhiteSpace($Videos)) { $Videos = [Environment]::GetFolderPath('MyDocuments') }
$DefaultOutput = Join-Path $Videos 'AROMOTION Projects'
New-Item -ItemType Directory -Force -Path $DefaultOutput | Out-Null

$form = New-Object System.Windows.Forms.Form
$form.Text = 'AROMOTION Studio - Lossless Quick Recorder'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(760, 520)
$form.MinimumSize = New-Object System.Drawing.Size(760, 520)
$form.BackColor = [System.Drawing.Color]::FromArgb(16, 18, 22)
$form.ForeColor = [System.Drawing.Color]::White
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'AROMOTION'
$title.Font = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::White
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(28, 24)
$form.Controls.Add($title)

$sub = New-Object System.Windows.Forms.Label
$sub.Text = 'Lossless Screen Recorder - immediate development build'
$sub.ForeColor = [System.Drawing.Color]::FromArgb(165, 174, 190)
$sub.AutoSize = $true
$sub.Location = New-Object System.Drawing.Point(31, 66)
$form.Controls.Add($sub)

$status = New-Object System.Windows.Forms.Label
$status.Text = 'READY'
$status.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
$status.ForeColor = [System.Drawing.Color]::FromArgb(130, 220, 165)
$status.AutoSize = $true
$status.Location = New-Object System.Drawing.Point(650, 35)
$form.Controls.Add($status)

$folderLabel = New-Object System.Windows.Forms.Label
$folderLabel.Text = 'Save projects to'
$folderLabel.AutoSize = $true
$folderLabel.Location = New-Object System.Drawing.Point(31, 120)
$form.Controls.Add($folderLabel)

$folderBox = New-Object System.Windows.Forms.TextBox
$folderBox.Text = $DefaultOutput
$folderBox.Location = New-Object System.Drawing.Point(34, 146)
$folderBox.Size = New-Object System.Drawing.Size(570, 30)
$form.Controls.Add($folderBox)

$browse = New-Object System.Windows.Forms.Button
$browse.Text = 'Browse...'
$browse.Location = New-Object System.Drawing.Point(616, 144)
$browse.Size = New-Object System.Drawing.Size(100, 31)
$browse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.SelectedPath = $folderBox.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $folderBox.Text = $dialog.SelectedPath
    }
})
$form.Controls.Add($browse)

$qualityLabel = New-Object System.Windows.Forms.Label
$qualityLabel.Text = 'Capture quality'
$qualityLabel.AutoSize = $true
$qualityLabel.Location = New-Object System.Drawing.Point(31, 199)
$form.Controls.Add($qualityLabel)

$quality = New-Object System.Windows.Forms.ComboBox
$quality.DropDownStyle = 'DropDownList'
[void]$quality.Items.Add('FFV1 / MKV - mathematically lossless (recommended)')
[void]$quality.Items.Add('H.264 RGB CRF 0 / MKV - lossless RGB')
$quality.SelectedIndex = 0
$quality.Location = New-Object System.Drawing.Point(34, 226)
$quality.Size = New-Object System.Drawing.Size(450, 31)
$form.Controls.Add($quality)

$fpsLabel = New-Object System.Windows.Forms.Label
$fpsLabel.Text = 'FPS'
$fpsLabel.AutoSize = $true
$fpsLabel.Location = New-Object System.Drawing.Point(510, 199)
$form.Controls.Add($fpsLabel)

$fps = New-Object System.Windows.Forms.ComboBox
$fps.DropDownStyle = 'DropDownList'
[void]$fps.Items.Add('30')
[void]$fps.Items.Add('60')
$fps.SelectedIndex = 1
$fps.Location = New-Object System.Drawing.Point(513, 226)
$fps.Size = New-Object System.Drawing.Size(90, 31)
$form.Controls.Add($fps)

$cursor = New-Object System.Windows.Forms.CheckBox
$cursor.Text = 'Record visible mouse cursor'
$cursor.Checked = $true
$cursor.AutoSize = $true
$cursor.Location = New-Object System.Drawing.Point(34, 278)
$form.Controls.Add($cursor)

$record = New-Object System.Windows.Forms.Button
$record.Text = '●  RECORD LOSSLESS'
$record.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$record.Location = New-Object System.Drawing.Point(34, 323)
$record.Size = New-Object System.Drawing.Size(325, 58)
$record.BackColor = [System.Drawing.Color]::FromArgb(220, 64, 75)
$record.ForeColor = [System.Drawing.Color]::White
$record.FlatStyle = 'Flat'
$form.Controls.Add($record)

$stop = New-Object System.Windows.Forms.Button
$stop.Text = '■  STOP & SAVE'
$stop.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$stop.Location = New-Object System.Drawing.Point(391, 323)
$stop.Size = New-Object System.Drawing.Size(325, 58)
$stop.Enabled = $false
$stop.FlatStyle = 'Flat'
$form.Controls.Add($stop)

$info = New-Object System.Windows.Forms.Label
$info.Text = 'This immediate build records the full Windows desktop to an untouched lossless master. Audio, auto-zoom, reconstructed cursor, 3D motion and the timeline editor are the next compiled milestones.'
$info.ForeColor = [System.Drawing.Color]::FromArgb(155, 164, 180)
$info.Location = New-Object System.Drawing.Point(34, 408)
$info.Size = New-Object System.Drawing.Size(680, 55)
$form.Controls.Add($info)

function Stop-Recording {
    if ($null -ne $script:RecorderProcess) {
        try {
            if (-not $script:RecorderProcess.HasExited) {
                $script:RecorderProcess.StandardInput.WriteLine('q')
                $script:RecorderProcess.StandardInput.Flush()
                if (-not $script:RecorderProcess.WaitForExit(8000)) {
                    $script:RecorderProcess.Kill()
                    $script:RecorderProcess.WaitForExit()
                }
            }
        } catch {}
        try { $script:RecorderProcess.Dispose() } catch {}
        $script:RecorderProcess = $null
    }

    $record.Enabled = $true
    $stop.Enabled = $false
    $folderBox.Enabled = $true
    $browse.Enabled = $true
    $quality.Enabled = $true
    $fps.Enabled = $true
    $cursor.Enabled = $true
    $status.Text = 'SAVED'
    $status.ForeColor = [System.Drawing.Color]::FromArgb(130, 220, 165)

    if ($script:CurrentOutput -and (Test-Path $script:CurrentOutput)) {
        $result = [System.Windows.Forms.MessageBox]::Show(
            "Lossless master saved:`n$script:CurrentOutput`n`nOpen the project folder?",
            'AROMOTION Studio',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Information
        )
        if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
            Start-Process explorer.exe -ArgumentList "/select,`"$script:CurrentOutput`""
        }
    }
}

$record.Add_Click({
    try {
        $base = $folderBox.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($base)) { throw 'Choose an output folder.' }
        New-Item -ItemType Directory -Force -Path $base | Out-Null

        $session = Join-Path $base (Get-Date -Format 'yyyy-MM-dd_HHmmss')
        New-Item -ItemType Directory -Force -Path $session | Out-Null
        $script:CurrentOutput = Join-Path $session 'master.mkv'

        $selectedFps = $fps.SelectedItem.ToString()
        $drawMouse = if ($cursor.Checked) { '1' } else { '0' }

        $args = @(
            '-hide_banner', '-y',
            '-f', 'gdigrab',
            '-framerate', $selectedFps,
            '-draw_mouse', $drawMouse,
            '-i', 'desktop'
        )

        if ($quality.SelectedIndex -eq 0) {
            $args += @('-c:v', 'ffv1', '-level', '3', '-coder', '1', '-g', '1')
        } else {
            $args += @('-c:v', 'libx264rgb', '-crf', '0', '-preset', 'ultrafast')
        }

        $args += @('-f', 'matroska', $script:CurrentOutput)

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $Ffmpeg
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $true
        foreach ($a in $args) { [void]$psi.ArgumentList.Add($a) }

        $script:RecorderProcess = New-Object System.Diagnostics.Process
        $script:RecorderProcess.StartInfo = $psi
        if (-not $script:RecorderProcess.Start()) { throw 'Could not start FFmpeg.' }
        Start-Sleep -Milliseconds 350
        if ($script:RecorderProcess.HasExited) { throw "FFmpeg stopped immediately with exit code $($script:RecorderProcess.ExitCode)." }

        $record.Enabled = $false
        $stop.Enabled = $true
        $folderBox.Enabled = $false
        $browse.Enabled = $false
        $quality.Enabled = $false
        $fps.Enabled = $false
        $cursor.Enabled = $false
        $status.Text = '● RECORDING'
        $status.ForeColor = [System.Drawing.Color]::FromArgb(255, 100, 110)
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'AROMOTION recorder error', 'OK', 'Error') | Out-Null
        if ($null -ne $script:RecorderProcess) {
            try { $script:RecorderProcess.Dispose() } catch {}
            $script:RecorderProcess = $null
        }
    }
})

$stop.Add_Click({ Stop-Recording })

$form.Add_FormClosing({
    if ($null -ne $script:RecorderProcess -and -not $script:RecorderProcess.HasExited) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            'A recording is still running. Stop and save it before closing?',
            'AROMOTION Studio',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question
        )
        if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) {
            Stop-Recording
        }
        else {
            $_.Cancel = $true
        }
    }
})

[void]$form.ShowDialog()
