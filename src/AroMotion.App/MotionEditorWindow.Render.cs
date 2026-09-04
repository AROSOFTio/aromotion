using System.Windows;
using System.Windows.Controls;
using AroMotion.App.Services;

namespace AroMotion.App;

public partial class MotionEditorWindow
{
    private readonly MotionRenderService _renderService = new();
    private Button? _renderPreviewButton;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_renderPreviewButton is not null || Content is not Grid root) return;

        _renderPreviewButton = new Button
        {
            Content = "Render Motion Preview",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 114, 255)),
            Foreground = System.Windows.Media.Brushes.White
        };
        _renderPreviewButton.Click += async (_, _) => await RenderCurrentProjectAsync();
        Grid.SetRow(_renderPreviewButton, 2);
        root.Children.Add(_renderPreviewButton);

        _renderService.LogReceived += line => Dispatcher.InvokeAsync(() =>
        {
            if (line.Contains("frame=", StringComparison.OrdinalIgnoreCase))
                StatusText.Text = line.Length > 120 ? line[^120..] : line;
        });
    }

    private async Task RenderCurrentProjectAsync()
    {
        var master = Path.Combine(_projectDirectory, "master.mkv");
        if (!File.Exists(master))
        {
            MessageBox.Show(this, "master.mkv was not found in this project.", "Render Motion Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _renderPreviewButton!.IsEnabled = false;
            StatusText.Text = "Rendering zoom + 3D + cursor + click + spotlight + blur…";
            await _store.SaveAsync(_motionProjectPath);
            var output = Path.Combine(_projectDirectory, "AROMOTION-motion-preview.mkv");
            await _renderService.RenderPreviewAsync(master, _eventsPath, _store.Project, output, 60);
            StatusText.Text = "Motion preview rendered";

            var result = MessageBox.Show(
                this,
                $"Motion preview saved:\n{output}\n\nOpen the project folder?",
                "AROMOTION",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _projectDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Render failed";
            MessageBox.Show(this, ex.Message, "AROMOTION render", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_renderPreviewButton is not null) _renderPreviewButton.IsEnabled = true;
        }
    }
}
