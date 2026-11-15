# MeshHunter

MeshHunter is a Windows-only WPF starter application for scanning game asset files, previewing supported meshes, and exporting models.

## Current features

- File menu with Import, Export, and Exit
- Import files or scan a folder; Analyze lists discovered assets in the left pane
- ZIP extraction with path traversal protection, nested archive scanning, and entry/expanded-size limits
- ZIP, GZIP, and ZLIB signature/extension detection with decompression limits
- Re-Volt `.prm` and intermediate `.i-p` mesh import, including structural detection when the file extension is unknown
- OBJ mesh preview with mouse-drag orbit and mouse-wheel zoom
- Export selected meshes as OBJ, glTF 2.0 (`.gltf` + `.bin`), or binary glTF (`.glb`); export selected textures in their original format
- Detect common texture extensions

## Run on Windows

Requirements: Windows 10/11, .NET 8 SDK, and Visual Studio 2022 with the .NET desktop development workload.

```powershell
dotnet restore
dotnet run --project src/MeshHunter/MeshHunter.csproj
```

## Scope

This milestone intentionally excludes Unity and Unreal Engine package analysis, and does not claim support for encrypted archives or arbitrary proprietary game containers. OBJ and Re-Volt `.prm`/`.i-p` are the only mesh formats currently parsed and previewed. FBX export/import, STL, PLY, DAE, and Midtown Madness `.p3d` are not yet supported; glTF and GLB are export-only. Need for Speed and EGO Engine package formats are not yet supported; their containers vary by game and need format-specific specifications and samples. Re-Volt PRM files contain mesh geometry and texture references, not embedded texture images; common standalone texture files are detected and can be exported unchanged. ZIP, GZIP, and ZLIB content can be unpacked and scanned, but arbitrary proprietary compression/container schemes are not assumed.

The `IAssetFormatPlugin` and `IModelExporter` interfaces provide extension points for adding readers and exporters incrementally. ZIP, GZIP, and ZLIB are the supported container/compression formats at this stage.
