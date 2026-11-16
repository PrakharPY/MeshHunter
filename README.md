# MeshHunter

MeshHunter is a Windows-only WPF starter application for scanning game asset files, previewing supported meshes, and exporting models.

## Current features

- File menu with Import, Export, and Exit; View filters for All, Mesh, Texture, and Others
- Import files or scan a folder; Analyze lists discovered assets in the left pane
- ZIP extraction with path traversal protection, nested archive scanning, and entry/expanded-size limits
- ZIP, GZIP, and ZLIB signature/extension detection with decompression limits
- Raging Thunder FUFS and standalone PLZP extraction, including compressed `.bin` payloads; PIFF meshes, Polarbit PXC v19 scene models, and discovered PNG/DDS textures are scanned
- PXC v19 models inside a scene file are listed in a model dropdown for individual preview and export
- Asphalt 4 BAR ZLIB entry extraction, binary `.obj` mesh import, and structurally validated PNG/JPEG recovery from extensionless image collections
- PIFF, Re-Volt `.prm`, and intermediate `.i-p` mesh import, including structural detection when the file extension is unknown
- OBJ mesh preview with mouse-drag orbit and mouse-wheel zoom
- Export selected meshes as OBJ, glTF 2.0 (`.gltf` + `.bin`), or binary glTF (`.glb`); export selected textures in their original format
- Detect common texture extensions

## Run on Windows

Requirements: Windows 10/11, .NET 8 SDK, and Visual Studio 2022 with the .NET desktop development workload.

```powershell
dotnet restore
dotnet test
dotnet run --project src/MeshHunter/MeshHunter.csproj
```

## Scope

This milestone intentionally excludes Unity and Unreal Engine package analysis, and does not claim support for encrypted archives or arbitrary proprietary game containers. PIFF and Re-Volt `.prm`/`.i-p` meshes are parsed and previewable; Wavefront OBJ and Asphalt 4 binary `.obj` meshes are also supported. Raging Thunder 1 PIFF geometry and PNG textures are extracted; Raging Thunder 2 PXC v19 scene models and DDS/PNG textures are extracted from FUFS archives. Asphalt 4 BAR entries are unpacked only when their ZLIB encoding validates; its proprietary `.p` and `.RLE` assets remain unsupported. Fastlane Street Racing KAPG files and Ridge Racer Accelerated `.dat` samples are detected as unsupported or remain unidentified because their model/texture payload formats are unresolved. FBX import, STL, PLY, DAE, and Midtown Madness `.p3d` are not yet supported; glTF and GLB are export-only. Re-Volt PRM files contain mesh geometry and texture references, not embedded texture images; common standalone texture files are detected and can be exported unchanged.

The `IAssetFormatPlugin` and `IModelExporter` interfaces provide extension points for adding readers and exporters incrementally. ZIP, GZIP, ZLIB, the validated Asphalt BAR subset, and the sampled Raging Thunder FUFS variants are the supported container/compression formats at this stage.
