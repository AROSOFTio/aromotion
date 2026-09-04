# AROMOTION Portable v0.9.4

This beta fixes the most important issues found in the v0.9.3 office-PC test.

## Render failure fix

v0.9.3 could generate a very large FFmpeg filter graph for mouse-follow zoom + 3D and pass it directly on the Windows process command line. With several interaction segments this could exceed Windows process command-line limits and produce `Render failed`.

v0.9.4 writes the filter graph to `render_filtergraph.txt` and passes that file to FFmpeg. If rendering still fails, AROMOTION writes the real FFmpeg error to `render_error.log` in the project folder.

## Hidden media processes

FFmpeg, FFprobe, Whisper and helper processes now use hidden/no-window process flags. The black FFmpeg console window should no longer appear over the desktop.

## Faster MP4 finalization

The previous beta rendered an FFV1 intermediate and then re-encoded the video again for MP4. v0.9.4 renders MP4 motion at high-quality H.264 CRF 10 once, then stream-copies that video while mixing the final audio. Lossless MKV continues to use FFV1 end-to-end.

## Browser-shell cleanup

The current beta UI still uses Edge/Chrome app mode around the local-only AROMOTION interface. v0.9.4 disables extensions, first-run prompts, sync, notifications and several browser extras to reduce unwanted browser UI. A native embedded WebView2 shell remains the target for the production UI.

## Quality

Lossless MKV remains the mathematical-lossless output. MP4 High avoids a second video encode and therefore improves both speed and delivered quality compared with v0.9.3.
