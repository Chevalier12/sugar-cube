namespace SugarCube.Core;

/// <summary>The whole method in one place, used by the window and by the command line: model -> cubes -> picture for
/// PixelLab -> its result back on the cubes -> any cube size -> .glb and sprites.</summary>
public sealed class Pipeline
{
    public const string Prompt =
        "This image is a texture sheet for a 3D game character: four views of the same character standing side by side, " +
        "from left to right: front, their left side, back, their right side. Repaint all four as richly detailed, " +
        "high-quality pixel art of the same character: subtle colour tones with selective dithering, a detailed face " +
        "(eyes with irises, eyebrows, nose, lips, ears), hands, fingers, clothing and materials with fine texture. " +
        "Keep the exact silhouette, pose, size and position of every figure, pixel for pixel, and keep the four views " +
        "consistent with each other. Flat even lighting, no cast shadows. No outline around the figures. " +
        "Transparent background stays transparent.";

    public static readonly int[] Sizes = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20 };

    public string ModelPath;
    public ModelImport.Result Model;
    public VoxelGrid Grid;        // full resolution, repainted when Repainted is true
    public bool IsRepainted => Grid?.Repainted != null;
    readonly Dictionary<int, (VoxelGrid grid, List<VoxelGrid.Face> faces)> cache = new();

    public void LoadModel(string path)
    {
        Model = ModelImport.Load(path);
        ModelPath = path;
        Grid = null; cache.Clear();
    }

    public int MaxCubesTall => VoxelGrid.MaxCubesTall(Model.Max - Model.Min);

    public void Turn() { ModelImport.TurnQuarter(Model, 1); Grid = null; cache.Clear(); }

    public void Voxelize(int cubesTall, Action<string> progress = null)
    {
        Grid = VoxelGrid.FromModel(Model, cubesTall, progress);
        cache.Clear();
    }

    public List<string> ApplyRepaint(string picturePath)
    {
        var r = Grid.ApplyRepaint(Picture.Load(picturePath));
        cache.Clear();
        return r;
    }

    /// <summary>The 4 original sprites (south, west, north, east) straight onto the cubes, instead of PixelLab's picture.</summary>
    public List<string> ApplySprites(string south, string west, string north, string east)
    {
        var r = Grid.ApplySprites(new[] { Picture.Load(south), Picture.Load(west), Picture.Load(north), Picture.Load(east) });
        cache.Clear();
        return r;
    }

    /// <summary>The model at cube size k (k x k x k of the full grid), and its outer faces; kept once made.</summary>
    public (VoxelGrid grid, List<VoxelGrid.Face> faces) AtSize(int k)
    {
        if (!cache.TryGetValue(k, out var v))
        {
            var g = Grid.Merged(k);
            v = (g, g.OuterFaces());
            cache[k] = v;
        }
        return v;
    }
}
