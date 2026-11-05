using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace SugarCube.Core;

public static class Export
{
    /// <summary>The cubes' outer faces as one GLB mesh, a colour per face (glTF COLOR_0, linear) on a white material.</summary>
    public static void Glb(IReadOnlyList<VoxelGrid.Face> faces, string path)
    {
        var mat = new MaterialBuilder("voxel").WithMetallicRoughnessShader().WithBaseColor(Vector4.One);
        var mesh = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexEmpty>("voxel");
        var prim = mesh.UsePrimitive(mat);
        static float Lin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        foreach (var f in faces)
        {
            var col = new VertexColor1(new Vector4(Lin(f.Colour.X), Lin(f.Colour.Y), Lin(f.Colour.Z), 1));
            VertexBuilder<VertexPositionNormal, VertexColor1, VertexEmpty> V(Vector3 p) => new(new VertexPositionNormal(p, f.Normal), col);
            prim.AddQuadrangle(V(f.P0), V(f.P1), V(f.P2), V(f.P3));
        }
        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        scene.ToGltf2().SaveGLB(path);
    }

    /// <summary>The 8 (or 4) sprites, each its own PNG, plus a sheet with all of them in one row on equal cells
    /// (bottoms aligned, so the feet stay at the same height). Returns the sheet's path.</summary>
    public static string Sprites(IReadOnlyList<VoxelGrid.Face> faces, float pixel, float cube, string folder, string name, bool eight, bool lit, bool outline = false)
    {
        Directory.CreateDirectory(folder);
        var dirs = eight ? Render.Directions : Render.Directions.Where((_, i) => i % 2 == 0).ToArray();
        var pics = new List<Picture>();
        foreach (var (dname, from) in dirs)
        {
            var p = Render.Sprite(faces, pixel, from, lit, outline);
            p.Save(Path.Combine(folder, $"{name}_{dname}.png"));
            pics.Add(p);
        }
        int cw = pics.Max(p => p.W), ch = pics.Max(p => p.H);
        var sheet = new Picture(cw * pics.Count, ch);
        for (int i = 0; i < pics.Count; i++)
        {
            var p = pics[i];
            int ox = i * cw + (cw - p.W) / 2, oy = ch - p.H;
            for (int y = 0; y < p.H; y++)
                Buffer.BlockCopy(p.Data, y * p.W * 4, sheet.Data, ((oy + y) * sheet.W + ox) * 4, p.W * 4);
        }
        string sheetPath = Path.Combine(folder, $"{name}_spritesheet.png");
        sheet.Save(sheetPath);
        File.WriteAllText(Path.Combine(folder, $"{name}_spritesheet.txt"),
            $"{pics.Count} frames in one row, each cell {cw}x{ch} px, in this order: {string.Join(", ", dirs.Select(d => d.Name))}\n" +
            $"one pixel = {pixel * 100:0.##} cm = {pixel / cube:0.##} cubes of {cube * 100:0.##} cm\n");
        return sheetPath;
    }
}
