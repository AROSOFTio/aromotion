using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class ProjectSession
{
    private ProjectSession(string projectDirectory, string qualityProfile, int fps)
    {
        ProjectDirectory = projectDirectory;
        QualityProfile = qualityProfile;
        FramesPerSecond = fps;
        VideoPath = Path.Combine(projectDirectory, "master.mkv");
        EventsPath = Path.Combine(projectDirectory, "events.jsonl");
        ProjectFilePath = Path.Combine(projectDirectory, "project.json");
    }

    public string ProjectDirectory { get; }
    public string VideoPath { get; }
    public string EventsPath { get; }
    public string ProjectFilePath { get; }
    public string QualityProfile { get; }
    public int FramesPerSecond { get; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public IReadOnlyList<ZoomSegment> ZoomSegments { get; private set; } = Array.Empty<ZoomSegment>();

    public static async Task<ProjectSession> CreateAsync(string rootDirectory, string qualityProfile, int fps)
    {
        Directory.CreateDirectory(rootDirectory);

        var folderName = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var projectDirectory = Path.Combine(rootDirectory, folderName);
        var suffix = 1;

        while (Directory.Exists(projectDirectory))
        {
            projectDirectory = Path.Combine(rootDirectory, $"{folderName}_{suffix++}");
        }

        Directory.CreateDirectory(projectDirectory);

        var session = new ProjectSession(projectDirectory, qualityProfile, fps)
        {
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        await session.WriteProjectFileAsync("recording");
        return session;
    }

    public async Task CompleteAsync(IReadOnlyList<ZoomSegment>? zoomSegments = null)
    {
        EndedAtUtc = DateTimeOffset.UtcNow;
        ZoomSegments = zoomSegments ?? Array.Empty<ZoomSegment>();
        await WriteProjectFileAsync("recorded");
    }

    private async Task WriteProjectFileAsync(string state)
    {
        var model = new
        {
            schemaVersion = 1,
            app = "AROMOTION Studio",
            state,
            source = new
            {
                video = Path.GetFileName(VideoPath),
                events = Path.GetFileName(EventsPath)
            },
            capture = new
            {
                fps = FramesPerSecond,
                quality = QualityProfile,
                cursorBakedIntoVideo = false
            },
            startedAtUtc = StartedAtUtc,
            endedAtUtc = EndedAtUtc,
            timeline = Array.Empty<object>(),
            zoomSegments = ZoomSegments,
            cameraKeyframes = Array.Empty<object>(),
            annotations = Array.Empty<object>(),
            captions = Array.Empty<object>()
        };

        await using var stream = File.Create(ProjectFilePath);
        await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
