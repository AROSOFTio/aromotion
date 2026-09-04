using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using AroMotion.App.Services;

namespace AroMotion.App;

public partial class MainWindow : Window
{
    private readonly FfmpegRecorder _recorder = new();
    private readonly InputHookService _inputHook = new();
    private ProjectSession? _session;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
        {
            videos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        OutputFolderTextBox.Text = Path.Combine(videos, "AROMOTION Projects");
        _recorder.LogReceived += Recorder_LogReceived;
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            return;
        }

        var rootDirectory = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            MessageBox.Show(this, "Choose a project folder first.", "AROMOTION", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fps = GetSelectedFps();
        var quality = QualityComboBox.SelectedIndex == 1
            ? RecordingQuality.LosslessH264Rgb
            : RecordingQuality.LosslessFfv1;
        var qualityName = quality == RecordingQuality.LosslessFfv1
            ? "ffv1-lossless"
            : "h264rgb-lossless";

        SetUiRecordingState(starting: true);

        try
        {
            _session = await ProjectSession.CreateAsync(rootDirectory, qualityName, fps);
            SessionPathText.Text = _session.ProjectDirectory;
            OpenFolderButton.IsEnabled = true;
            MotionEditorButton.IsEnabled = false;
            RecorderLogText.Text = "Starting FFmpeg lossless capture…";

            await _recorder.StartAsync(_session.VideoPath, fps, quality);
            await _inputHook.StartAsync(_session.EventsPath);

            HeaderStatusText.Text = "● RECORDING";
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            OutputFolderTextBox.IsEnabled = false;
            QualityComboBox.IsEnabled = false;
            FpsComboBox.IsEnabled = false;
        }
        catch (Exception ex)
        {
            await SafeStopAsync();
            SetUiIdleState();

            MessageBox.Show(
                this,
                $"Recording could not start.\n\n{ex.Message}\n\nFFmpeg must be bundled at tools\\ffmpeg\\ffmpeg.exe next to the app, or available on PATH.",
                "AROMOTION recorder error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        StopButton.IsEnabled = false;
        HeaderStatusText.Text = "FINALIZING…";

        try
        {
            await _inputHook.StopAsync();
            await _recorder.StopAsync();

            if (_session is not null)
            {
                var generator = new AutoZoomGenerator();
                var zooms = await generator.GenerateAsync(_session.EventsPath);
                await _session.CompleteAsync(zooms);
                MotionEditorButton.IsEnabled = File.Exists(_session.EventsPath);
            }

            RecorderLogText.Text = "Recording finalized. Clean master and interaction metadata saved. Motion Editor is ready.";
        }
        catch (Exception ex)
        {
            RecorderLogText.Text = $"Finalize warning: {ex.Message}";
        }
        finally
        {
            SetUiIdleState();
        }
    }

    private async Task SafeStopAsync()
    {
        try
        {
            await _inputHook.StopAsync();
        }
        catch
        {
        }

        try
        {
            await _recorder.StopAsync();
        }
        catch
        {
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose AROMOTION project folder",
            InitialDirectory = Directory.Exists(OutputFolderTextBox.Text)
                ? OutputFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !Directory.Exists(_session.ProjectDirectory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _session.ProjectDirectory,
            UseShellExecute = true
        });
    }

    private void OpenMotionEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !Directory.Exists(_session.ProjectDirectory) || !File.Exists(_session.EventsPath))
        {
            MessageBox.Show(this, "Record and stop a session first so AROMOTION has interaction metadata to edit.", "Motion Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new MotionEditorWindow(_session.ProjectDirectory, _session.EventsPath)
        {
            Owner = this
        };
        editor.Show();
    }

    private int GetSelectedFps()
    {
        if (FpsComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && int.TryParse(item.Content?.ToString(), out var fps))
        {
            return fps;
        }

        return 60;
    }

    private void SetUiRecordingState(bool starting)
    {
        HeaderStatusText.Text = starting ? "STARTING…" : "● RECORDING";
        RecordButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        MotionEditorButton.IsEnabled = false;
    }

    private void SetUiIdleState()
    {
        HeaderStatusText.Text = "READY";
        RecordButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        OutputFolderTextBox.IsEnabled = true;
        QualityComboBox.IsEnabled = true;
        FpsComboBox.IsEnabled = true;
    }

    private void Recorder_LogReceived(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            RecorderLogText.Text = line.Length > 220 ? line[^220..] : line;
        });
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_recorder.IsRecording || _inputHook.IsRunning)
        {
            e.Cancel = true;
            await StopRecordingAsync();
            _allowClose = true;
            Close();
        }
    }
}
