using System.Text.Json;
using System.Text.Json.Serialization;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class MotionProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MotionProject Project { get; private set; } = MotionProject.CreateDefault();

    public event Action? Changed;

    public void Replace(MotionProject project)
    {
        Project = project;
        Changed?.Invoke();
    }

    public void AddZoom(ZoomClip clip)
    {
        Project.Zooms.Add(clip);
        Project.Zooms.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        Changed?.Invoke();
    }

    public void UpdateZoom(ZoomClip clip)
    {
        var index = Project.Zooms.FindIndex(x => x.Id == clip.Id);
        if (index < 0) return;
        Project.Zooms[index] = clip;
        Project.Zooms.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        Changed?.Invoke();
    }

    public void RemoveZoom(Guid id)
    {
        Project.Zooms.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    public void Add3D(Motion3DClip clip)
    {
        Project.Motions3D.Add(clip);
        Project.Motions3D.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        Changed?.Invoke();
    }

    public void Update3D(Motion3DClip clip)
    {
        var index = Project.Motions3D.FindIndex(x => x.Id == clip.Id);
        if (index < 0) return;
        Project.Motions3D[index] = clip;
        Project.Motions3D.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        Changed?.Invoke();
    }

    public void Remove3D(Guid id)
    {
        Project.Motions3D.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    public void AddSpotlight(SpotlightClip clip)
    {
        Project.Spotlights.Add(clip);
        Changed?.Invoke();
    }

    public void UpdateSpotlight(SpotlightClip clip)
    {
        var index = Project.Spotlights.FindIndex(x => x.Id == clip.Id);
        if (index < 0) return;
        Project.Spotlights[index] = clip;
        Changed?.Invoke();
    }

    public void RemoveSpotlight(Guid id)
    {
        Project.Spotlights.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    public void AddBlur(BlurClip clip)
    {
        Project.Blurs.Add(clip);
        Changed?.Invoke();
    }

    public void UpdateBlur(BlurClip clip)
    {
        var index = Project.Blurs.FindIndex(x => x.Id == clip.Id);
        if (index < 0) return;
        Project.Blurs[index] = clip;
        Changed?.Invoke();
    }

    public void RemoveBlur(Guid id)
    {
        Project.Blurs.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    public void UpdateCursor(CursorEffectSettings cursor)
    {
        Project = Project with { Cursor = cursor };
        Changed?.Invoke();
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Project, JsonOptions, cancellationToken);
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<MotionProject>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Motion project file is empty or invalid.");
        Replace(project);
    }
}
