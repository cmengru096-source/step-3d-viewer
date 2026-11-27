# STEP 3D Viewer

A portable, offline Windows viewer for STEP models. Open a `.step` or `.stp` file to inspect the geometry in 3D.

## Download

Download **STEP-3D-Viewer.exe** from [Releases](../../releases/latest) and double-click it on 64-bit Windows. The viewer runs using Windows .NET Framework and includes its STEP importer. Models are read locally.

- Click **Open STEP…**, press **Ctrl+O**, or drag a STEP file into the window.
- Files with left or right in their names update the matching assembly button. Chinese 左/右 and Russian Левый/Правый names are also recognized. Other filenames update the currently selected side, with left as the initial choice.
- Each assembly button retains its latest successful import during the current session. You can inspect the demo and then return to either loaded assembly.
- Drag with the left mouse button to rotate, drag with the right to pan, and scroll to zoom. Double-click the viewport to reset.
- Pick a standard view from the toolbar. Long models also have end-detail shortcuts.
- Use the language selector to switch between English and Simplified Chinese.

The built-in [stepped block](samples/stepped-block.step) is generated from simple geometric dimensions. The viewer renders tessellated geometry for read-only inspection.

## Build and test

Use 64-bit Windows with the .NET Framework 4.x C# compiler, PowerShell, and Python 3.11 or later.

```powershell
.\Build.ps1
.\Test.ps1
```

The first build downloads and verifies the pinned Node.js and occt-import-js packages. The single executable is written to `dist/STEP-3D-Viewer.exe`. The tests render the viewer off-screen and exercise file import, language switching, and view controls.

## Licensing

Viewer code and demo model are available under the [MIT License](LICENSE). The bundled importer and runtime retain their own terms in [Third-party notices](THIRD-PARTY-NOTICES.txt). Binary releases also include a separate archive with the corresponding source snapshots for the LGPL components.
