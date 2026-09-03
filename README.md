# AROMOTION Studio

**AROMOTION Studio** is a Windows-first screen recorder and motion editor by AROSOFT Innovations Ltd.

The goal is not to build another ordinary screen recorder. AROMOTION is designed for software tutorials, coding demonstrations, product walkthroughs, and training videos with automatic focus, cinematic zoom, smooth cursor motion, 3D perspective, annotations, and high-fidelity export.

## Core product goals

- Pixel-perfect / lossless screen master recording
- 60 FPS screen capture with 30/60/120 FPS profiles planned
- Separate mouse, click, and keyboard-shortcut metadata
- Automatic click-to-zoom generation
- Editable zoom focus, amount, duration, and easing
- Smooth/cinematic cursor reconstruction
- 3D perspective and depth motion
- Spotlight, blur, arrows, shapes, text, and live annotation
- Microphone, system audio, and webcam tracks
- MP4 delivery export plus a lossless archival master
- Local-first editing; no subscription or cloud required for core recording/editing

## Current milestone — M0 / recorder foundation

The repository currently contains the first Windows desktop foundation:

1. WPF/.NET 8 application shell.
2. FFmpeg-backed desktop capture service.
3. **Lossless master mode using FFV1 in MKV** (`-c:v ffv1 -level 3`).
4. Optional visually-lossless H.264 profile for smaller working files.
5. Global mouse/click and shortcut-event capture stored as JSONL alongside the video.
6. Project-session folder structure ready for the editor and motion engine.

> Lossless screen video can be very large. AROMOTION therefore treats the lossless file as the source-of-truth master. A later milestone will generate lightweight proxies automatically for editing while preserving the untouched master for final rendering.

## Requirements for the current development build

- Windows 10/11 x64
- .NET 8 SDK
- FFmpeg available at `tools/ffmpeg/ffmpeg.exe` or on `PATH`

## Run

```powershell
dotnet restore
dotnet run --project src/AroMotion.App/AroMotion.App.csproj
```

Choose an output folder, select **Lossless Master**, then press **Record**. The first milestone records the full desktop and writes input metadata into the same project directory.

## Recording project layout

```text
AROMOTION Projects/
  2026-09-04_010000/
    master.mkv
    events.jsonl
    project.json
```

## Quality strategy

AROMOTION will never make the editor's preview/proxy the source of final quality. The pipeline is designed as:

```text
Desktop pixels -> lossless master -> non-destructive motion/edit metadata -> final renderer
                           \\-> optional proxy for smooth editing
```

This lets us keep UI text, code, terminal output, thin lines, and small fonts sharp even after zooming.

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md) and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Status

Early development. The recording core is being built first; 3D motion and the timeline editor follow once capture fidelity and metadata synchronization are stable.
