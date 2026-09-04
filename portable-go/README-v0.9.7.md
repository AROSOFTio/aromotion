# AROMOTION Portable v0.9.7

This release fixes cursor/zoom/3D synchronization in the portable renderer.

## Motion synchronization

Older portable builds used different paths for the visible cursor, raw click target, camera follower and 3D direction. During fast pointer travel those paths could diverge, so the cursor moved one way while the camera focused somewhere else.

v0.9.7 uses one canonical cursor trajectory for:

- visible reconstructed cursor
- click pulse position
- click-triggered zoom center
- follow camera
- 3D direction

All systems query the same interpolated cursor position at the same event timestamp.

## Random zoom reduction

`Zoom after typing` and `Zoom on window focus` are now OFF by default. Normal automatic focus is click-driven unless the user explicitly enables the extra triggers.

## Camera behavior

The camera starts from the exact visible cursor position at the click timestamp, follows the same trajectory with low-pass damping and a speed limit, and guarantees that the cursor stays inside the zoom viewport even during fast corner-to-corner movements.

Recommended initial values:

- Zoom strength: 1.55x–1.65x
- Follow flexibility: 58%
- Zoom in: 320 ms
- Hold: 1150 ms
- Zoom out: 420 ms
- 3D depth: 35–45%

If a machine still shows a mismatch, capture `events.csv` and `timing.json` from that exact recording project for deterministic diagnosis.