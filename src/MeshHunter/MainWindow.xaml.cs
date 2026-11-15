using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using MeshHunter.Models;
using MeshHunter.Services;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Color = System.Windows.Media.Color;

namespace MeshHunter;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AssetEntry> _assets = new();
    private readonly List<string> _inputs = new();
    private readonly AssetAnalyzer _analyzer = new();
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "MeshHunter", Guid.NewGuid().ToString("N"));
    private Point _lastMousePosition;
    private bool _isOrbiting;
    private double _orbitYaw = 0.7;
    private double _orbitPitch = 0.45;
    private double _cameraDistance = 4;
    private Point3D _modelCenter = new(0, 0, 0);

    public MainWindow()
    {
        InitializeComponent();
        AssetList.ItemsSource = _assets;
        Directory.CreateDirectory(_workspace);
        Closed += (_, _) =>
        {
            try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); }
            catch { /* Temporary-file cleanup is best effort. */ }
        };
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import game files or archives", Filter = "All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddInputs(dialog.FileNames);
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderBrowserDialog { Description = "Choose a folder to scan", ShowNewFolderButton = false, UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) AddInputs(new[] { dialog.SelectedPath });
    }

    private void AddInputs(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            if (!_inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) _inputs.Add(path);
        SourceText.Text = _inputs.Count == 0 ? "No input selected" : string.Join(Environment.NewLine, _inputs);
        StatusText.Text = $"{_inputs.Count} input location(s) ready to analyze.";
        ActivityText.Text = $"{_inputs.Count} input location(s) ready to analyze.";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_inputs.Count == 0) { Import_Click(sender, e); if (_inputs.Count == 0) return; }
        StatusText.Text = "Analyzing files…";
        ActivityText.Text = "Analyzing input locations…";
        try
        {
            IProgress<string> progress = new Progress<string>(message =>
            {
                ActivityText.Text = message;
                StatusText.Text = message;
            });
            var results = await Task.Run(() => _analyzer.Analyze(_inputs.ToArray(), _workspace, progress.Report));
            ActivityText.Text = "Loading discovered assets…";
            _assets.Clear();
            foreach (var asset in results) _assets.Add(asset);
            DetailsText.Text = $"Analysis complete. Found {_assets.Count} recognized asset(s): {_assets.Count(a => a.Kind == AssetKind.Mesh)} mesh(es), {_assets.Count(a => a.Kind == AssetKind.Texture)} texture(s), {_assets.Count(a => a.Kind == AssetKind.Other)} unsupported model file(s).";
            StatusText.Text = $"Analysis complete — {_assets.Count} asset(s) found.";
            ActivityText.Text = $"Loaded {_assets.Count} asset(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            ActivityText.Text = $"Analysis failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "MeshHunter analysis error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selection = AssetList.SelectedItems.Cast<AssetEntry>().ToArray();
        if (selection.Length == 0)
        {
            DetailsText.Clear();
            MeshVisual.Content = null;
            ActivityText.Text = "No assets selected.";
            return;
        }

        var meshes = selection.Where(asset => asset.Kind == AssetKind.Mesh).ToArray();
        if (selection.Length == 1)
        {
            var asset = selection[0];
            DetailsText.Text = $"Name: {asset.Name}\nType: {asset.Kind}\nFormat: {asset.Format}\nStatus: {asset.Status}\nPath: {asset.Path}\n\n{asset.Description}";
        }
        else
        {
            DetailsText.Text = $"{selection.Length} assets selected\n{meshes.Length} mesh(es), {selection.Count(asset => asset.Kind == AssetKind.Texture)} texture(s)\n\nRight-click the selection to export supported meshes and textures.";
        }

        if (meshes.Length == 0)
        {
            MeshVisual.Content = null;
            ActivityText.Text = "Selection contains no previewable meshes.";
            return;
        }

        try
        {
            var asset = meshes[0];
            ActivityText.Text = $"Loading mesh preview: {asset.Name}";
            ShowMesh(MeshFormatReader.Read(asset.Path));
            ActivityText.Text = selection.Length == 1
                ? $"Loaded mesh: {asset.Name}"
                : $"Loaded preview of {asset.Name}; {meshes.Length} mesh(es) selected.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not preview mesh: {ex.Message}";
            ActivityText.Text = $"Could not load mesh: {ex.Message}";
        }
    }

    private void AssetList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(AssetList, source) is not ListBoxItem item ||
            item.IsSelected)
            return;

        AssetList.SelectedItems.Clear();
        item.IsSelected = true;
    }

    private void ShowMesh(ObjMesh mesh)
    {
        var geometry = new MeshGeometry3D();
        foreach (var (x, y, z) in mesh.Vertices) geometry.Positions.Add(new Point3D(x, y, z));
        for (var i = 0; i + 2 < mesh.Triangles.Count; i += 3)
        {
            geometry.TriangleIndices.Add(mesh.Triangles[i]);
            geometry.TriangleIndices.Add(mesh.Triangles[i + 1]);
            geometry.TriangleIndices.Add(mesh.Triangles[i + 2]);
        }
        geometry.Freeze();
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(90, 160, 225)));
        MeshVisual.Content = new GeometryModel3D(geometry, material) { BackMaterial = material };

        var bounds = geometry.Bounds;
        _modelCenter = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var radius = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ) / 2;
        _cameraDistance = Math.Max(radius * 2.8, 1);
        _orbitYaw = 0.7;
        _orbitPitch = 0.45;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (MeshViewport.Camera is not PerspectiveCamera camera) return;
        var horizontal = _cameraDistance * Math.Cos(_orbitPitch);
        camera.Position = new Point3D(
            _modelCenter.X + horizontal * Math.Sin(_orbitYaw),
            _modelCenter.Y + _cameraDistance * Math.Sin(_orbitPitch),
            _modelCenter.Z + horizontal * Math.Cos(_orbitYaw));
        camera.LookDirection = _modelCenter - camera.Position;
        camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void MeshViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isOrbiting = true;
        _lastMousePosition = e.GetPosition(MeshViewport);
        MeshViewport.CaptureMouse();
        e.Handled = true;
    }

    private void MeshViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isOrbiting = false;
        MeshViewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void MeshViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isOrbiting) return;
        var position = e.GetPosition(MeshViewport);
        var delta = position - _lastMousePosition;
        _lastMousePosition = position;
        _orbitYaw += delta.X * 0.01;
        _orbitPitch = Math.Clamp(_orbitPitch - delta.Y * 0.01, -1.45, 1.45);
        UpdateCamera();
    }

    private void MeshViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _cameraDistance = Math.Clamp(_cameraDistance * (e.Delta > 0 ? 0.85 : 1.18), 0.05, 1_000_000);
        UpdateCamera();
        e.Handled = true;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (AssetList.SelectedItem is not AssetEntry { Kind: AssetKind.Mesh } asset)
        {
            MessageBox.Show(this, "Select a supported mesh before exporting.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Export mesh",
            FileName = Path.GetFileNameWithoutExtension(asset.Name) + ".obj",
            DefaultExt = ".obj",
            Filter = "Wavefront OBJ (*.obj)|*.obj|glTF JSON (*.gltf)|*.gltf|Binary glTF (*.glb)|*.glb",
            FilterIndex = 1,
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var extension = dialog.FilterIndex switch
            {
                2 => ".gltf",
                3 => ".glb",
                _ => ".obj"
            };
            var outputPath = Path.GetExtension(dialog.FileName).Equals(extension, StringComparison.OrdinalIgnoreCase)
                ? dialog.FileName
                : Path.ChangeExtension(dialog.FileName, extension);
            var mesh = MeshFormatReader.Read(asset.Path);
            ExportMesh(mesh, outputPath);
            StatusText.Text = $"Exported {outputPath}";
            ActivityText.Text = $"Exported {asset.Name} to {outputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed.";
            ActivityText.Text = $"Export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSelectedMeshes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string extension } ||
            extension is not (".obj" or ".gltf" or ".glb"))
            return;

        var selected = AssetList.SelectedItems.Cast<AssetEntry>()
            .Where(asset => asset.Kind == AssetKind.Mesh)
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Select at least one supported mesh.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new FolderBrowserDialog
        {
            Description = $"Choose a folder for the selected {extension} mesh export(s)",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in selected)
            {
                var outputName = GetUniqueMeshFileName(Path.GetFileNameWithoutExtension(asset.Name), extension, dialog.SelectedPath, usedNames);
                var outputPath = Path.Combine(dialog.SelectedPath, outputName);
                ExportMesh(MeshFormatReader.Read(asset.Path), outputPath);
            }

            StatusText.Text = $"Exported {selected.Length} mesh(es) as {extension}.";
            ActivityText.Text = $"Exported {selected.Length} selected mesh(es) as {extension} to {dialog.SelectedPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed.";
            ActivityText.Text = $"Export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSelectedTextures_Click(object sender, RoutedEventArgs e)
    {
        var selected = AssetList.SelectedItems.Cast<AssetEntry>()
            .Where(asset => asset.Kind == AssetKind.Texture)
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Select at least one texture.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for the selected textures",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in selected)
            {
                var extension = Path.GetExtension(asset.Path);
                if (string.IsNullOrEmpty(extension))
                    extension = GetTextureExtension(asset.Format);
                var outputName = GetUniqueFileName(Path.GetFileNameWithoutExtension(asset.Name), extension, dialog.SelectedPath, usedNames);
                File.Copy(asset.Path, Path.Combine(dialog.SelectedPath, outputName));
            }

            StatusText.Text = $"Exported {selected.Length} texture(s).";
            ActivityText.Text = $"Exported {selected.Length} selected texture(s) to {dialog.SelectedPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed.";
            ActivityText.Text = $"Export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ExportMesh(ObjMesh mesh, string outputPath)
    {
        switch (Path.GetExtension(outputPath).ToLowerInvariant())
        {
            case ".obj":
                new ObjExporter().Export(mesh, outputPath);
                break;
            case ".gltf":
            case ".glb":
                new GltfExporter().Export(mesh, outputPath);
                break;
            default:
                throw new NotSupportedException("Choose OBJ, glTF, or GLB as the export format.");
        }
    }

    private static string GetUniqueMeshFileName(string name, string extension, string directory, HashSet<string> usedNames)
    {
        var baseName = GetSafeFileBaseName(name);
        var requiresBuffer = extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase);
        for (var suffix = 1; ; suffix++)
        {
            var candidate = suffix == 1 ? baseName + extension : $"{baseName} ({suffix}){extension}";
            var bufferName = requiresBuffer ? Path.ChangeExtension(candidate, ".bin") : null;
            if (usedNames.Contains(candidate) || File.Exists(Path.Combine(directory, candidate)) ||
                bufferName is not null && (usedNames.Contains(bufferName) || File.Exists(Path.Combine(directory, bufferName))))
                continue;

            usedNames.Add(candidate);
            if (bufferName is not null)
                usedNames.Add(bufferName);
            return candidate;
        }
    }

    private static string GetUniqueFileName(string name, string extension, string directory, HashSet<string> usedNames)
    {
        var baseName = GetSafeFileBaseName(name);
        var candidate = baseName + extension;
        var suffix = 2;
        while (!usedNames.Add(candidate) || File.Exists(Path.Combine(directory, candidate)))
            candidate = $"{baseName} ({suffix++}){extension}";
        return candidate;
    }

    private static string GetSafeFileBaseName(string name)
    {
        var baseName = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(baseName) ? "asset" : baseName;
    }

    private static string GetTextureExtension(string format) => format.ToUpperInvariant() switch
    {
        "JPEG" => ".jpg",
        "TIFF" => ".tif",
        "WEBP" => ".webp",
        "KTX2" => ".ktx2",
        "PNG" => ".png",
        "DDS" => ".dds",
        "BMP" => ".bmp",
        "KTX" => ".ktx",
        _ => ".texture"
    };

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
