# AROMOTION Architecture

## 1. Quality model

AROMOTION uses a **master + metadata + render** architecture.

```text
Windows Desktop Pixels
        |
        v
Capture Engine ------------------------+
        |                               |
        v                               v
Lossless Master                  Input/Event Metadata
(master.mkv)                     (events.jsonl)
        |                               |
        +---------------+---------------+
                        |
                        v
               Non-destructive Project
                        |
                 +------+------+
                 |             |
                 v             v
              Proxy        Final Renderer
           (optional)           |
                                v
                         MP4 / MKV / MOV
```

The editor never repeatedly re-encodes the source while the user edits. Zoom, 3D, annotations, cursor smoothing, trims, and captions are project instructions applied at preview/render time.

## 2. Lossless capture profiles

### FFV1 master — default

Container: Matroska (`.mkv`)  
Codec: FFV1 level 3  
Purpose: archival/editing master with mathematically lossless frames.

Advantages:
- exact preservation of screen pixels after encode/decode;
- excellent for text, code, terminals and UI details;
- open codec and robust for archival use.

Tradeoff: large files and significant disk throughput.

### H.264 RGB lossless — alternative

Codec: `libx264rgb` with `-crf 0`  
Purpose: lossless RGB master where it performs better on a user's hardware/storage combination.

The application must not call a YUV 4:2:0 encode "lossless screen recording" because chroma subsampling can visibly soften coloured text and thin UI edges even at high bitrates.

## 3. Cursor strategy

The source screen is captured **without baking in FFmpeg's mouse cursor**. AROMOTION records cursor position and mouse-button events separately.

Benefits:
- smooth/reconstruct cursor movement after recording;
- replace cursor style and size;
- add click ripples/sounds;
- hide cursor during inactive periods;
- move/retime cursor presentation without modifying the source master.

## 4. Keyboard privacy

AROMOTION does **not** record normal text typed by default. The event collector records shortcut combinations and special/function keys needed for tutorial overlays. This avoids turning the recorder into a keylogger and reduces the chance of recording passwords or private text.

## 5. Event clock

Capture events use a monotonic stopwatch beginning with the project recording session. Upcoming work will align this event clock to the exact video start timestamp/frame clock and store synchronization anchors in `project.json`.

## 6. Windows capture backend plan

M0 uses FFmpeg desktop capture so that the recorder can become usable early.

Production direction:

```text
Windows Graphics Capture / Desktop Duplication
                |
              D3D11
                |
       GPU frame texture stream
          /             \
 preview/render       encoder
```

This removes extra copies, improves multi-monitor/high-refresh capture, and makes GPU effects/preview integration cleaner.

## 7. 3D renderer direction

The motion renderer will treat the recorded desktop as a textured plane controlled by an orthographic/perspective virtual camera. Editable properties include:

- position X/Y;
- scale/zoom;
- rotate X/Y/Z;
- perspective/FOV;
- depth translation;
- anchor/focus point;
- shadow/reflection;
- easing curve.

Automatic 3D presets are generated from the same click/focus events used by automatic zoom, but remain editable keyframes.

## 8. Audio design

Planned audio tracks are independent:

```text
Track A: system audio
Track B: microphone
Track C: optional webcam audio
```

They should not be destructively mixed during recording. Final mix happens during render/export.

## 9. Project format

`project.json` is the stable user project description. Source assets are referenced, not embedded.

Planned structure:

```json
{
  "schemaVersion": 1,
  "source": { "video": "master.mkv" },
  "capture": { "fps": 60, "quality": "ffv1-lossless" },
  "timeline": [],
  "zoomSegments": [],
  "cameraKeyframes": [],
  "annotations": [],
  "captions": []
}
```

Schema migrations will allow future application versions to open older projects.
