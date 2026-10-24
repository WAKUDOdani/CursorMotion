<p align="center"><a href="README.md">Русский</a> / <strong>English</strong></p>

<p align="center"><img src="docs/assets/logo.png" width="104" height="104" alt="CursorMotion logo"></p>
<h1 align="center">CursorMotion</h1>
<p align="center"><strong>Smooth motion. Your cursor style.</strong><br>Whole-cursor motion blur, soft shape transitions, and glass themes for Windows.</p>
<p align="center">
  <a href="https://github.com/WAKUDOdani/CursorMotion/releases/download/v3.9.0/CursorMotion-3.9.0-portable-x64.exe"><img src="docs/assets/download-portable.svg" width="266" height="48" alt="Download CursorMotion 3.9.0 Portable for Windows x64"></a>
  <a href="docs/USAGE.en.md"><img src="docs/assets/guide-en.svg" width="164" height="48" alt="Read the guide"></a>
</p>
<p align="center">
  <a href="https://github.com/WAKUDOdani/CursorMotion/releases/latest"><img src="https://img.shields.io/github/v/release/WAKUDOdani/CursorMotion?style=flat&label=release&color=0a84ff" alt="Latest release"></a>
  <a href="https://github.com/WAKUDOdani/CursorMotion/actions/workflows/ci.yml"><img src="https://github.com/WAKUDOdani/CursorMotion/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI checks"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-7565f6?style=flat" alt="MIT license"></a>
</p>
<p align="center"><a href="#features">Features</a> · <a href="docs/USAGE.en.md">Guide</a> · <a href="https://github.com/WAKUDOdani/CursorMotion/releases">Releases</a> · <a href="https://t.me/WAKUDOdani">Telegram</a> · <a href="https://github.com/WAKUDOdani/CursorMotion/issues">Help and issues</a></p>
<p align="center"><a href="docs/assets/collection.png"><img src="docs/assets/collection-rounded.svg" width="1080" alt="Actual CursorMotion interface showing the cursor collection"></a><br><sub>The real application with demonstration settings. Shape samples use the same renderer as the Windows cursor.</sub></p>

## Features

| Customize | What CursorMotion does |
| --- | --- |
| **Motion** | Blurs the whole cursor image along its trajectory, smooths effects, adds tilt and a soft click response. The system pointer position stays exact. |
| **Themes** | Crystal, Aurora, Carbon, Rose, and Classic. Apply a complete pack or mix individual materials. |
| **Shapes** | Arrow, text, wait, hand, and four window-resize directions, with smooth shape morphs. |
| **Appearance** | Independent size, fill, outline, shadow, and transition duration for the four primary shapes. An optional switch links their sizes. |
| **Workspace** | All monitors, one display, or a custom region; system tray, opt-in startup, profile import and export. |

<details>
<summary><strong>See cursor settings</strong></summary>

<a href="docs/assets/cursor.png"><img src="docs/assets/cursor-rounded.svg" width="1080" alt="Independent cursor shape settings"></a>

Motion settings and the applied pack are saved automatically. Appearance drafts require applying; export saves the applied profile.

</details>

## Download

**Windows x64**, with a Direct3D 11 GPU. The application interface is in Russian. .NET and Electron are included.

| Edition | How it works | Updates |
| --- | --- | --- |
| [**Portable 3.9.0**](https://github.com/WAKUDOdani/CursorMotion/releases/download/v3.9.0/CursorMotion-3.9.0-portable-x64.exe) | One EXE; no installation or administrator rights required | Fully exit through the tray and replace the EXE |

[All releases and checksums](https://github.com/WAKUDOdani/CursorMotion/releases). Only the EXE is needed to run the app. `SHA256SUMS.txt` verifies the download; `Source code` is for developers.

Builds currently have no Windows publisher signature. Download from this repository and, if needed, compare SHA-256 with the release checksum.

## Quick start

1. Download the EXE to a permanent folder and run it. You can name it `CursorMotion.exe` to simplify future updates.
2. Choose and apply a pack in **«Коллекция»**. Adjust blur in **«Движение»** and individual shapes in **«Курсор»**.
3. Close settings to keep the effect in the tray. **Ctrl + Alt + F8** pauses it; **Ctrl + Alt + F12** restores the ordinary cursor and exits.

First launch extracts components to an AppData cache. Later launches reuse verified files, with no additional downloads. The app creates nothing next to the EXE.

[Settings, profile transfer, startup, and screen recording →](docs/USAGE.en.md)

## Things to know

- Windows presents the cursor through its system mechanism, above ordinary windows, notifications, and context menus. Application-drawn pointers can retain their own appearance.
- NVIDIA App can distort large cursors or long trails in recordings. **«Пространство → Движок и профиль → Ограничение для записи»** bounds native frame sizes, but does not yet guarantee correct recording in every case.
- Settings are at `%LOCALAPPDATA%\CursorMotion\settings.json`. Use profile export/import to transfer them to another PC.
- Startup is opt-in. After moving the EXE, toggle startup off and on to update its path.
- Pause and exit restore the registered Windows scheme. A separate watchdog monitors cursor recovery when the engine stops.

## Stay in touch

[Telegram WAKUDO Dani](https://t.me/WAKUDOdani) for updates and other projects; [Issues](https://github.com/WAKUDOdani/CursorMotion/issues) for reports and ideas. Another project by the author: [WakuShare](https://github.com/WAKUDOdani/WakuShare).

<details>
<summary><strong>Developers: build and verify</strong></summary>

Windows x64, .NET SDK **10.0.401**, Node.js **24**, and PowerShell **7**.

```powershell
pwsh ./prepare-build.ps1
cd desktop/renderer
npm ci
cd ../..
pwsh ./build-single-file.ps1 -SkipTests
pwsh ./tools/verify-package.ps1 -Executable ./dist/single-file/CursorMotion-3.9.0-portable-x64.exe
```

The EXE is written to `dist/single-file/`. `-SkipTests` skips checks during building; the next command verifies the actual package. [Build and CI](docs/BUILDING.md) · [Release process](docs/RELEASING.md).

Routine tests use synthetic data, private cursors, and the application's own hidden window. They do not replace the live cursor or change real startup. CI can use WARP only in diagnostics.

</details>

## Credits and license

CursorMotion is developed by **WAKUDO Dani**. Initial cursor geometry and exposure integration were adapted from the **Motion Blur Cursor** OBS script, distributed under the Unlicense. OBS is not required to use CursorMotion.

[MIT License](LICENSE) · [Third-party components](THIRD-PARTY-NOTICES.txt) · [Algorithm provenance](docs/UPSTREAM.md)
