# Build and verification

Requirements: Windows x64, PowerShell 7, .NET SDK10.0.401, Node.js24/npm.
`global.json`, `src/packages.lock.json`, the npm lockfile, and `runtime-lock.json`
record the toolchain/package inputs. Electron is downloaded only at build time,
from its official release, and SHA-256 checked. No runtime download is needed.

```powershell
pwsh ./prepare-build.ps1
npm ci --prefix desktop/renderer
pwsh ./build-single-file.ps1 -SkipTests
pwsh ./tools/verify-package.ps1 -Executable ./dist/single-file/CursorMotion-3.9.0-portable-x64.exe
```

The folder build is an intermediate; distribute the final EXE. Existing local UI
can be reused with `-ReuseBuiltUi` after building it; provenance checks reject
stale shader/palette artwork. `-InputDirectory` accepts an existing folder build.
Personal `data/`, portable flags, command files and PDBs are excluded from the
embedded payload. Licenses/notices remain included.

## Architecture

- `src/`: native .NET host, tray, GPU shader, native HCURSOR publication and watchdog.
- `desktop/`: Electron settings loaded on demand; React UI and genuine engine artwork.
- `bootstrap/`: small trimmed single-file launcher, embedded ZIP/manifest, cache validation.
- `bootstrap-tests/`: synthetic cache/path/integrity/concurrent-launch checks.
- `tools/`: packaging and hidden application checks.

The bootstrap's runtime is content-addressed in AppData. Every cached file is
verified; an incomplete/corrupt generation is replaced by a new generation.
No running generation is deleted or overwritten. Extraction validates names,
sizes, SHA-256 and completeness. The launcher forwards CLI arguments and backend
pipes; a normal tray launch exits the bootstrap, adding no resident wrapper.
`CURSORMOTION_LAUNCHER_EXE` tells the child which public path belongs in startup.

All user settings use AppData. A diagnostic `--bootstrap-cache <folder>` override
is available only with diagnostic/report flags; `--bootstrap-info <report.json>`
extracts/verifies and reports paths without starting the cursor engine.

## Tests

```powershell
dotnet run --project bootstrap-tests/Bootstrap.Tests.csproj -c Release
pwsh ./tools/check-folder.ps1 -Directory ./artifacts/folder
```

Folder verification runs native offscreen groups, JS tests, backend protocol,
hidden real Electron UI and tray lifecycle. Package verification runs the actual
EXE with an isolated diagnostic cache, checking warm reuse, recovery, renamed
Unicode paths, public startup path and preservation of the installed profile.

Native diagnostics can use WARP when no GPU exists on CI. Explicit `--warp`
requires `--offscreen`/`--diagnostic`; production keeps hardware rendering.
Routine tests never call global cursor replacement or real startup registration.
Live shell, recording and feel checks are separate from these tests.

The approved3.8.5 cursor renderer/simulation/compact capture behavior is preserved.
Do not restore the rejected512px capture canvas or add input/typing debounce.
