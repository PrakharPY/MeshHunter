# MeshHunter

MeshHunter is a Windows-only WPF starter application for analyzing imported game asset packages and previewing their meshes.

## What this starter includes

- Menu bar with File → Import / Export / Exit
- Left-side details panel listing imported files and discovered assets
- Analyze action for files and directories
- Mesh detection for standard OBJ files
- ZIP archive extraction with path safety checks
- Basic 3D mesh preview in a viewport
- Export selected OBJ mesh as OBJ
- Recognition of common texture file extensions and Unity/Unreal package extensions

## Run on Windows

Requirements:
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with .NET desktop development workload

From the repository root:

```powershell
dotnet restore
cd src/MeshHunter
dotnet build
```

Then run:

```powershell
dotnet run --project src/MeshHunter/MeshHunter.csproj
```

## Notes

This is an MVP and intentionally does not claim to fully parse proprietary engine formats such as Unity `.assets` or Unreal `.uasset` files yet. The structure is designed as a plugin architecture so those and other formats can be added incrementally.
