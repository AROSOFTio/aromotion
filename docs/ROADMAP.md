# AROMOTION Studio Roadmap

## Product principle

AROMOTION is built around **capture fidelity first**. Every effect is non-destructive. The untouched screen master remains available for every final render.

## M0 — Recorder foundation

**Goal:** reliably capture a sharp Windows desktop and synchronized interaction metadata.

- [x] Windows WPF/.NET 8 shell
- [x] FFmpeg capture service
- [x] FFV1 lossless master profile
- [x] H.264 RGB lossless profile
- [x] Mouse movement metadata
- [x] Mouse click metadata
- [x] Shortcut metadata without logging normal typed text
- [x] Per-recording project folder and metadata
- [ ] Bundle signed FFmpeg build in installer
- [ ] System-audio track via WASAPI
- [ ] Microphone track
- [ ] Capture a selected display/window/region
- [ ] Recorder crash recovery
- [ ] Exact frame/event clock synchronization tests

## M1 — Motion engine

**Goal:** make the first recording automatically look better than a normal capture.

- [ ] Generate zoom segments from clicks
- [ ] Cursor-follow zoom option
- [ ] Smart focus-area selection around UI controls
- [ ] Configurable zoom strength and hold time
- [ ] Cubic/spring easing presets
- [ ] Cursor smoothing / reconstructed cursor path
- [ ] Cursor scale, style, shadow, and click ripple
- [ ] Shortcut overlays (`Ctrl+S`, `Alt+Tab`, etc.)
- [ ] Motion blur

## M2 — Timeline editor

**Goal:** every automatic decision remains editable.

- [ ] Video preview canvas
- [ ] Multi-track timeline
- [ ] Trim/split/delete
- [ ] Drag zoom segments
- [ ] Change zoom focal point after recording
- [ ] Keyframes for position/scale/rotation
- [ ] Undo/redo
- [ ] Auto-save project state
- [ ] Background and rounded frame presets

## M3 — 3D Motion

**Goal:** exceed simple FocuSee-style zoom with an editable virtual camera.

- [ ] Perspective camera
- [ ] Rotate X / Y / Z
- [ ] Depth / Z translation
- [ ] Parallax
- [ ] Edge-aware 3D framing
- [ ] Reflection/shadow plane
- [ ] 3D motion presets: Focus, Push, Tilt, Orbit, Reveal
- [ ] Automatic 3D moves generated from click/focus events
- [ ] Per-keyframe easing and intensity

## M4 — Teaching / annotation tools

- [ ] Spotlight
- [ ] Privacy blur
- [ ] Pen and highlighter
- [ ] Arrows
- [ ] Rectangle / circle
- [ ] Number markers
- [ ] Text labels
- [ ] Magnifier
- [ ] Live annotation while recording
- [ ] Post-recording annotations on the timeline

## M5 — Camera, audio, captions

- [ ] Webcam as independent track
- [ ] Round/rounded/square camera frames
- [ ] Background removal (optional local model)
- [ ] Noise suppression
- [ ] Independent mic/system-audio gain
- [ ] Local Whisper transcription option
- [ ] Editable captions
- [ ] SRT/VTT export

## M6 — Rendering and performance

- [ ] Automatic editing proxy generation
- [ ] GPU preview renderer
- [ ] Hardware-accelerated delivery encode (NVENC/QSV/AMF where available)
- [ ] Lossless archival render
- [ ] H.264/H.265/AV1 delivery presets
- [ ] 1080p/1440p/4K exports
- [ ] 30/60/120 FPS profiles
- [ ] High-DPI and mixed-DPI monitor testing

## M7 — Installer and production hardening

- [ ] Self-contained Windows x64 build
- [ ] Installer (`AROMOTION-Setup.exe`)
- [ ] Bundled FFmpeg and license notices
- [ ] Code signing
- [ ] Auto updater
- [ ] Diagnostics/log export
- [ ] Recovery of interrupted recordings
- [ ] Performance tests on low/mid/high-spec PCs

## First release definition

A practical first release is reached when a user can:

1. Record screen + mic + system audio in lossless/high-quality mode.
2. Open the recording immediately in AROMOTION.
3. Get automatic click zooms and smooth cursor motion.
4. Edit zooms and add 3D motion/annotations.
5. Export a sharp 1080p/60 or 4K video without UI text becoming blurry.
