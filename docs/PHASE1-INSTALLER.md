# AROMOTION Phase 1 — Installed Windows Build

This phase replaces the M0 portable recorder with a normal per-user Windows installation.

## Working Phase 1 scope

### Capture
- Full desktop capture at 30, 60 or 120 FPS.
- Quality presets:
  - Compact — H.264 CRF 23
  - Standard — H.264 CRF 18
  - High — H.264 4:4:4 CRF 14
  - Near Lossless — H.264 4:4:4 CRF 8
  - Lossless RGB — libx264rgb CRF 0
  - Archival Lossless — FFV1 level 3
- The original screen master is never overwritten by generated effects.

### Audio
- Microphone selection from FFmpeg/DirectShow devices.
- Microphone master: 48 kHz PCM, 24-bit WAV where the selected device supports it.
- Separate system-audio source selection when Windows exposes Stereo Mix / loopback / a virtual audio cable as a capture device.
- Screen, microphone and system audio are stored as separate sources for non-destructive editing.

### Webcam
- Webcam source selection from DirectShow video devices.
- Webcam is recorded as an independent source so its position, crop and shape can be changed later.

### Mouse and motion
- Global mouse movement and click metadata recorded with timestamps.
- Visible cursor halo option.
- Click pulse option.
- Click-driven automatic zoom generation.
- First-pass perspective motion mode built on FFmpeg's per-frame `perspective` filter. It is deliberately kept non-destructive: the lossless screen master remains untouched.

### Installation
- Installs to `%LOCALAPPDATA%\Programs\AROMOTION` without administrator rights.
- Creates Start Menu and optional Desktop shortcuts.
- Adds an uninstall entry to Windows Apps/Installed Apps.
- Downloads a local FFmpeg build on first install.

## Next phase after this installer

Phase 2 turns the recorder into the full FocuSee/Bandicam replacement:

- Proper multi-track timeline editor.
- Editable auto-zoom keyframes.
- Advanced 3D X/Y perspective, depth, parallax and easing presets.
- Cursor path smoothing/reconstruction independent of the baked screen video.
- Click sounds and configurable mouse effects.
- Spotlight, magnifier and privacy blur.
- Pen/highlighter/arrows/boxes/circles/text/number markers.
- Webcam shapes, borders, shadows and backgrounds.
- System audio + mic mixer, gain, limiter, compressor and noise reduction.
- Captions/transcription.
- H.264/H.265/AV1 delivery exports while retaining the original lossless master.
- Crash recovery and proxy media for very large lossless projects.

## Quality principle

Lossless recording can be extremely large. That is expected. AROMOTION keeps two separate concepts:

1. **Master capture quality** — including true mathematical lossless modes.
2. **Delivery/export quality** — smaller files for YouTube, training platforms and messaging.

The editor will always be able to render from the original master rather than repeatedly recompressing an already compressed recording.
