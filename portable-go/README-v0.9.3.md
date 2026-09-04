# AROMOTION Portable v0.9.3

This portable milestone fixes the motion behaviour reported during real testing and adds local automatic transcription.

## Mouse-follow zoom

- Normalizes recorded virtual-desktop mouse coordinates to the actual captured video frame.
- Prevents focus from drifting/sticking toward the top-left when desktop and video coordinate spaces differ.
- Camera follows the recorded mouse path **while zoomed**, with dead-zone and inertia smoothing.
- Adjustable follow flexibility, zoom-in duration, hold duration and zoom-out duration.
- Zoom styles: **Focus, Gentle, Punch, Follow, Cinematic**.
- Privacy-safe zoom-after-typing stores activity timestamps/position only, never typed text.
- Optional zoom-on-window-focus.

## 3D motion

- Smooth yaw and pitch tied to the focus position.
- Optional 3D follow based on the current mouse/focus target.
- Adjustable depth/intensity with eased in/hold/out movement.

## Cursor

- Uses a vector Windows-style cursor in the rendered video instead of depending on a font cursor glyph.
- Keeps cursor smoothing, halo, left/right click events, click pulse and click audio.

## Transcript + dynamic captions

- Optional local **whisper.cpp** transcription; no API key required.
- Saves `transcript.txt`, `transcript.srt`, and full JSON timing output.
- Uses Whisper token timestamps for word highlighting when available, with SRT fallback.
- Dynamic caption styles: **Pop, Karaoke, Clean**.
- Configurable number of words shown at once.
- First transcript downloads the local Whisper runtime and multilingual Base model; later runs reuse them.

## Quality pipeline

- Near Lossless is the recommended/default capture preset for sharp UI text without FFV1-sized masters.
- Motion/cursor/3D/caption rendering uses an **FFV1 4:4:4 lossless intermediate** before delivery encoding.
- Choose **Lossless MKV** when the final file itself must remain mathematically lossless.

## No-admin testing

The portable EXE runs with per-user storage and does not require Program Files or administrator rights. If an organization explicitly blocks unsigned applications, use an approved PC or request IT approval rather than bypassing policy.
