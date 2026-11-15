using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using MeshHunter.Models;
using MeshHunter.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace MeshHunter;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AssetEntry> _assets = new();
    private readonly List<string> _inputs = new();
    private readonly AssetAnalyzer _analyzer = new();
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "MeshHunter", Guid.NewGuid().ToString("N"));

    public MainWindow()
    {
        InitializeComponent();
        AssetList.ItemsSource = _assets;
        Directory.CreateDirectory(_workspace);
        Closed += (_, _) =>
        {
            try
            {
                if (Directory.Exists(_workspace))
                    Directory.Delete(_workspace, true);
            }
            catch
            {
                // best effort cleanup only
            }
        };
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import game files or archives",
            Filter = "All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
            AddInputs(dialog.FileNames);
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to scan",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            AddInputs(new[] { dialog.SelectedPath });
    }

    private void AddInputs(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!_inputs.Contains(path, StringComparer.OrdinalIgnoreCase))
                _inputs.Add(path);
        }

        SourceText.Text = _inputs.Count == 0 ? "No input selected" : string.Join(Environment.NewLine, _inputs);
        StatusText.Text = $"{_inputs.Count} input location(s) ready to analyze.";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_inputs.Count == 0)
        {
            Import_Click(sender, e);
            if (_inputs.Count == 0)
                return;
        }

        StatusText.Text = "Analyzing files…";

        try
        {
            var results = await Task.Run(() => _analyzer.Analyze(_inputs, _workspace));
            _assets.Clear();
            foreach (var asset in results)
                _assets.Add(asset);

            var meshCount = _assets.Count(a => a.Kind == AssetKind.Mesh);
            var textureCount = _assets.Count(a => a.Kind == AssetKind.Texture);
            var packageCount = _assets.Count(a => a.Kind == AssetKind.GamePackage);
            var otherCount = _assets.Count(a => a.Kind == AssetKind.Other);

            DetailsText.Text = $"Analysis complete. Found {_assets.Count} asset(s): {meshCount} mesh(es), {textureCount} texture(s), {packageCount} game package(s), {otherCount} unsupported/other file(s).";
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
        if (AssetList.SelectedItem is not AssetEntry asset)
            return;

        DetailsText.Text = $"Name: {asset.Name}\nType: {asset.Kind}\nFormat: {asset.Format}\nStatus: {asset.Status}\nPath: {asset.Path}\n\n{asset.Description}";

        if (asset.Kind == AssetKind.Mesh && asset.Format.Equals("OBJ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mesh = ObjMeshReader.Read(asset.Path);
                ShowMesh(mesh);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not preview mesh: {ex.Message}";
            }
        }
        else
        {
            MeshVisual.Content = null;
        }
    }

    private void ShowMesh(ObjMesh mesh)
    {
        var geometry = new MeshGeometry3D();
        foreach (var (x, y, z) in mesh.Vertices)
            geometry.Positions.Add(new Point3D(x, y, z));

        for (var i = 0; i + 2 < mesh.Triangles.Count; i += 3)
        {
            geometry.TriangleIndices.Add(mesh.Triangles[i]);
            geometry.TriangleIndices.Add(mesh.Triangles[i + 1]);
            geometry.TriangleIndices.Add(mesh.Triangles[i + 2]);
        }

        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(90, 160, 225)));
        var model = new GeometryModel3D(geometry, material)
        {
            BackMaterial = material
        };

        MeshVisual.Content = model;

        var camera = MeshViewport.Camera as PerspectiveCamera;
        if (camera is not null)
        {
            camera.Position = new Point3D(2, 2, 4);
            camera.LookDirection = new Vector3D(-2, -2, -4);
            camera.UpDirection = new Vector3D(0, 1, 0);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (AssetList.SelectedItem is not AssetEntry { Kind: AssetKind.Mesh, Format: "OBJ" } asset)
        {
            MessageBox.Show(this, "Select a supported OBJ mesh before exporting.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export mesh",
            FileName = Path.GetFileNameWithoutExtension(asset.Name) + ".obj",
            DefaultExt = ".obj",
            Filter = "Wavefront OBJ (*.obj)|*.obj"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var mesh = ObjMeshReader.Read(asset.Path);
            ObjExporter.Export(mesh, dialog.FileName);
            StatusText.Text = $"Exported {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
