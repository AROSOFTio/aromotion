# AROMOTION Studio — Quick Start

This is the first development build of AROMOTION Studio.

## Install / run

1. Download the latest **AROMOTION-win-x64-portable** artifact from GitHub Actions.
2. Extract the ZIP to a normal folder such as `C:\AROMOTION`.
3. Double-click **START.cmd**.
4. On first run, the setup downloads the local FFmpeg recording engine into `tools\ffmpeg` and starts AROMOTION.
5. Choose a project folder, select **Lossless Master — FFV1 / MKV**, choose 60 FPS, then click **RECORD**.
6. Click **STOP & SAVE** when finished.

## What this build already records

- Full Windows desktop
- Mathematically lossless FFV1 master video
- Optional lossless H.264 RGB master
- 30 / 60 / 120 FPS capture modes
- Mouse movement metadata stored separately
- Left/right/middle clicks stored separately
- Mouse wheel events stored separately
- Useful keyboard shortcuts stored separately
- Project metadata for the coming auto-zoom / 3D editor

Ordinary typed characters are deliberately not stored by the input logger.

## Project output

Each recording creates a timestamped project directory containing:

- `master.mkv` — untouched lossless screen master
- `events.jsonl` — cursor/click/shortcut timeline metadata
- `project.json` — AROMOTION project metadata

## Important

Lossless recording produces very large files. This is intentional for the master capture. The editor will later generate proxy files for smooth editing while final renders use the untouched lossless master.

## Next engineering milestones

1. Windows Graphics Capture / Desktop Duplication capture engine
2. System audio + microphone tracks
3. Auto-zoom generation from click and cursor events
4. Smooth reconstructed cursor
5. 3D camera motion and perspective
6. Timeline editor and manual keyframes
7. Annotations, spotlight, blur and magnifier
8. Webcam track
9. High-quality MP4 export presets
