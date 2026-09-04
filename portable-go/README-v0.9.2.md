# AROMOTION Portable v0.9.2 beta

This portable build fixes the mouse/motion renderer tested on the office PC.

## Important output files

- `01_clean_screen_master.mkv` — untouched source master. It intentionally has **no cursor, zoom, click effects, or 3D**.
- `02_motion_with_cursor_zoom_3d.mkv` — lossless motion intermediate with the reconstructed cursor and effects.
- `AROMOTION_FINAL_WITH_EFFECTS.mp4` — the normal file to play/share after rendering finishes.

## v0.9.2 changes

- Replaced the font-dependent cursor glyph with a vector Windows-style cursor so the cursor cannot disappear because of font support.
- Added smooth camera follow while zoomed.
- Added privacy-safe typing activity markers; typed characters are never stored.
- Added focused-window targets when switching apps/dialogs.
- Auto zoom can trigger from clicks, shortcuts, typing activity, and window focus.
- Added Focus, Gentle, Punch, and Follow zoom timing styles.
- Strengthened separate left/right click rings and click sound.
- Perspective 3D remains tied to interaction targets.
- The clean master remains untouched for later editing/re-rendering.

## No-admin testing

Run the portable EXE from a normal writable folder such as Downloads. It keeps its engine in `AROMOTION-Data` beside the EXE and does not require Program Files, HKLM changes, drivers, services, or administrator elevation.

After stopping a recording, wait until the Studio status says `READY — FINAL WITH CURSOR + ZOOM + 3D CREATED`, then open `AROMOTION_FINAL_WITH_EFFECTS.mp4`.
