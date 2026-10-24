# CursorMotion guide

[Русский](USAGE.md) · [Home](../README.en.md)

## Launch and update

Download a single `CursorMotion-<version>-portable-x64.exe` from [Releases](https://github.com/WAKUDOdani/CursorMotion/releases). Installation and separate .NET/Electron downloads are unnecessary. Rename it `CursorMotion.exe` if you want a stable filename.

First launch extracts and verifies components in `%LOCALAPPDATA%\CursorMotion\runtime`. Later launches reuse the cache. Damaged files cause a fresh runtime generation, without resetting settings. Launching requires no internet connection.

To update, fully exit from the tray or press **Ctrl + Alt + F12**, then replace the EXE. A second running version forwards to the existing instance's settings window.

## Packs and shapes

Choose and apply a pack in **«Коллекция»**. Crystal and Aurora use transparent glass materials; Carbon, Rose, and Classic provide other finishes. Shape illustrations come from the actual renderer.

In **«Курсор»**, independently configure arrow, text, wait, and hand: size, fill/opacity, outline color/width, shadow color/offset/softness, transition duration, and idle hiding. The shared-size switch links all four sizes; other settings remain independent. Resize arrows inherit the arrow appearance.

Apply appearance drafts with the apply button. Switching pages alone does not apply them.

## Motion

**«Движение»** controls effect smoothing, shutter, trail falloff, quality, tilt, and click response. Smoothing affects exposure and effects; the app does not warp or delay the system mouse position. A **0°** shutter disables blur. Exposure timing is independent of render FPS. Large cursors and long, high-quality blur cost more resources.

## Recording

For NVIDIA cursor corruption, enable **«Пространство → Движок и профиль → Совместимость с NVIDIA»**. This trims transparent padding and uses compact cursor frames without scaling the glyph. An exceptionally long blur is replaced by the full-size static shape when it does not fit. If even the static shape with its shadow is too large, the app asks for a smaller size or shadow instead of submitting an oversized frame. Ordinary mode does not have these recording limits.

The user confirmed correct NVIDIA recordings and a single Discord pointer on Windows 11 / RTX 4070. Other capture paths and driver versions may differ.

## Tray and startup

| Action | Control |
| --- | --- |
| Enable/pause | **Ctrl + Alt + F8**, app switch, or tray menu |
| Restore Windows cursor and exit | **Ctrl + Alt + F12** or tray exit |
| Reopen settings | Double-click tray icon or launch the EXE again |
| Start with Windows | Tray setting or **«Пространство → Движок и профиль»** |

Closing settings keeps the native engine in the tray and unloads Electron. Startup targets the main portable EXE. After moving it, toggle startup off and on to refresh the path.

## Settings and transfer

The profile lives at `%LOCALAPPDATA%\CursorMotion\settings.json`; UI data also stays in AppData. Nothing is created beside the EXE.

Use **«Пространство → Движок и профиль → Экспорт»**, transfer the JSON, and choose **«Импорт»** on the other PC. Export includes applied forms, so apply drafts first. Enable startup separately on the new PC.

The footer's **«Стандартные настройки»** restores application defaults after confirmation. It does not independently change startup registration.

## Troubleshooting

For extraction errors, check disk space and AppData write access. Relaunching verifies the runtime. Report app/Windows/GPU versions, capture method, and reproduction steps in [Issues](https://github.com/WAKUDOdani/CursorMotion/issues).

Launcher log: `%LOCALAPPDATA%\CursorMotion\bootstrap.log`; engine log: `%LOCALAPPDATA%\CursorMotion\errors.log`. Avoid uploading personal recordings or your entire AppData directory.
