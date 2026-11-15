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
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_inputs.Count == 0) { Import_Click(sender, e); if (_inputs.Count == 0) return; }
        StatusText.Text = "Analyzing files…";
        try
        {
            var results = await Task.Run(() => _analyzer.Analyze(_inputs, _workspace));
            _assets.Clear();
            foreach (var asset in results) _assets.Add(asset);
            DetailsText.Text = $"Analysis complete. Found {_assets.Count} recognized asset(s): {_assets.Count(a => a.Kind == AssetKind.Mesh)} OBJ mesh(es), {_assets.Count(a => a.Kind == AssetKind.Texture)} texture(s), {_assets.Count(a => a.Kind == AssetKind.Other)} unsupported model file(s).";
            StatusText.Text = $"Analysis complete — {_assets.Count} asset(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            MessageBox.Show(this, ex.Message, "MeshHunter analysis error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetList.SelectedItem is not AssetEntry asset) return;
        DetailsText.Text = $"Name: {asset.Name}\nType: {asset.Kind}\nFormat: {asset.Format}\nStatus: {asset.Status}\nPath: {asset.Path}\n\n{asset.Description}";
        if (asset.Kind == AssetKind.Mesh)
        {
            try { ShowMesh(ObjMeshReader.Read(asset.Path)); }
            catch (Exception ex) { StatusText.Text = $"Could not preview mesh: {ex.Message}"; }
        }
        else MeshVisual.Content = null;
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
            MessageBox.Show(this, "Select a supported OBJ mesh before exporting.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var mesh = ObjMeshReader.Read(asset.Path);
            switch (Path.GetExtension(dialog.FileName).ToLowerInvariant())
            {
                case ".obj": new ObjExporter().Export(mesh, dialog.FileName); break;
                case ".gltf":
                case ".glb": new GltfExporter().Export(mesh, dialog.FileName); break;
                default: throw new NotSupportedException("Choose OBJ, glTF, or GLB as the export format.");
            }
            StatusText.Text = $"Exported {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
