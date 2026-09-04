# Testing AROMOTION on a Windows PC without administrator rights

AROMOTION's test build is intentionally **self-contained and portable**. It does not need to write to `Program Files`, install a Windows service, add a driver, or change machine-wide registry settings.

## Preferred test package

Use the GitHub Actions artifact named:

`AROMOTION-motion-parity-no-admin-win-x64`

The artifact contains:

- `AROMOTION.exe`
- the self-contained .NET runtime
- `tools/ffmpeg/ffmpeg.exe`
- `tools/ffmpeg/ffprobe.exe`
- `NO-ADMIN-TEST.txt`

## Test steps

1. Download the artifact ZIP from the GitHub Actions run.
2. Extract it to a folder you own, for example:
   - `%USERPROFILE%\Downloads\AROMOTION-Test`
   - `%USERPROFILE%\Documents\AROMOTION-Test`
3. Double-click `AROMOTION.exe`.
4. Record a 10–20 second desktop sample containing:
   - several left-clicks in different screen areas;
   - one right-click;
   - a keyboard shortcut such as `Ctrl+S`;
   - a fast cursor movement across the screen.
5. Stop and save.
6. Click **Open Motion Editor**.
7. Verify:
   - Auto Zoom generated editable zoom blocks.
   - Zoom start, scale, focus point, zoom-in, hold, zoom-out and easing can be changed.
   - A manual zoom can be added and deleted.
   - Auto 3D clips are generated from click focus.
   - 3D presets Focus / Push / Tilt / Reveal / Orbit / Parallax can be selected.
   - Rotation, depth, perspective, intensity, hold, shadow and reflection values can be edited.
   - Cursor style, opacity, smoothing, motion blur and click effects can be configured.
   - Left and right click effects can use separate styles/colors.
   - Multiple spotlight clips can be created/edited/deleted.
   - Multiple privacy blur clips can be created/edited/deleted.
   - `motion-project.json` is saved inside the recording project directory.

## What does not require administrator rights

- screen capture through the current FFmpeg desktop backend;
- low-level mouse/keyboard hooks for the current user's desktop session;
- writing recordings to your user Videos/Documents folders;
- running bundled FFmpeg;
- editing/saving AROMOTION project files.

## Possible office policy restrictions

A company may enforce security policy that blocks unsigned/unapproved executables even when no administrator rights are technically required. If Windows says the application is blocked by your organization, **do not try to bypass the company policy**. Ask IT to allow the test binary or test it on a personal Windows PC.

Windows microphone/camera privacy settings can also be organization-controlled. If the device itself is blocked by policy, AROMOTION cannot override that restriction.

## Why the test build is portable

The production release can have a normal installer later. Portable testing is intentional because it lets testers verify the recorder/editor on restricted office PCs without requesting machine-level installation rights.
