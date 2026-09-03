using System.Diagnostics;

namespace AroMotion.App.Services;

public enum RecordingQuality
{
    LosslessFfv1,
    LosslessH264Rgb
}

public sealed class FfmpegRecorder : IAsyncDisposable
{
    private Process? _process;

    public event Action<string>? LogReceived;

    public bool IsRecording => _process is { HasExited: false };

    public async Task StartAsync(string outputPath, int framesPerSecond, RecordingQuality quality)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A recording is already running.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveFfmpegPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };

        // gdigrab is the portable first milestone backend. The production capture
        // engine will move to Windows Graphics Capture / Desktop Duplication.
        Add(startInfo,
            "-hide_banner",
            "-y",
            "-f", "gdigrab",
            "-framerate", framesPerSecond.ToString(),
            "-draw_mouse", "0",
            "-i", "desktop");

        switch (quality)
        {
            case RecordingQuality.LosslessFfv1:
                // FFV1 is mathematically lossless and well suited to screen masters.
                Add(startInfo,
                    "-c:v", "ffv1",
                    "-level", "3",
                    "-coder", "1",
                    "-g", "1");
                break;

            case RecordingQuality.LosslessH264Rgb:
                // libx264rgb CRF 0 is lossless RGB. We deliberately avoid YUV 4:2:0
                // here because chroma subsampling can soften coloured UI text.
                Add(startInfo,
                    "-c:v", "libx264rgb",
                    "-crf", "0",
                    "-preset", "ultrafast");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(quality), quality, null);
        }

        Add(startInfo,
            "-f", "matroska",
            outputPath);

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                LogReceived?.Invoke(e.Data);
            }
        };

        try
        {
            if (!_process.Start())
            {
                throw new InvalidOperationException("FFmpeg could not be started.");
            }

            _process.BeginErrorReadLine();
            await Task.Delay(250);

            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"FFmpeg exited immediately with code {_process.ExitCode}. Check the recorder log.");
            }
        }
        catch
        {
            _process.Dispose();
            _process = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                // FFmpeg's q command finalizes the Matroska container cleanly.
                await process.StandardInput.WriteLineAsync("q");
                await process.StandardInput.FlushAsync();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static void Add(ProcessStartInfo info, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
    }

    private static string ResolveFfmpegPath()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // Development fallback: resolve FFmpeg from PATH.
        return "ffmpeg";
    }
}
