# MeshHunter

MeshHunter is a Windows-only WPF starter application for scanning game asset files, previewing supported meshes, and exporting models.

## Current features

- File menu with Import, Export, and Exit
- Import files or scan a folder; Analyze lists discovered assets in the left pane
- ZIP extraction with path traversal protection, nested archive scanning, and entry/expanded-size limits
- OBJ mesh preview with mouse-drag orbit and mouse-wheel zoom
- Export a selected OBJ mesh as OBJ, glTF 2.0 (`.gltf` + `.bin`), or binary glTF (`.glb`)
- Detect common texture extensions

## Run on Windows

Requirements: Windows 10/11, .NET 8 SDK, and Visual Studio 2022 with the .NET desktop development workload.

```powershell
dotnet restore
dotnet run --project src/MeshHunter/MeshHunter.csproj
```

## Scope

This milestone intentionally excludes Unity and Unreal Engine package analysis, which can be added later as a separate feature. It also does not claim support for encrypted archives, arbitrary proprietary game containers, or formats requiring proprietary keys. OBJ is the only mesh format currently parsed and previewed. FBX, glTF, GLB, STL, PLY, and DAE files are detected but are not yet importable. Export to OBJ/glTF/GLB works from an imported OBJ mesh.

The `IAssetFormatPlugin` and `IModelExporter` interfaces provide extension points for adding readers and exporters incrementally. Only standard ZIP archives are extracted at this stage.
