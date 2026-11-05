using System.Numerics;
using SharpGLTF.Schema2;

namespace SugarCube.Core;

/// <summary>Reads a textured glTF / GLB model (for example a Meshy export) as world-space triangles with the colour
/// information needed to voxelize it: UVs, the base-colour texture, the base-colour factor and vertex colours.</summary>
public static class ModelImport
{
    public sealed class Tri
    {
        public Vector3 A, B, C;
        public Vector2 Ua, Ub, Uc;
        public Vector4 Ca = Vector4.One, Cb = Vector4.One, Cc = Vector4.One;  // vertex colours (linear in glTF)
        public int Texture = -1;
        public Vector4 Factor = Vector4.One;  // base-colour factor (linear in glTF)
    }

    public sealed class Result
    {
        public List<Tri> Tris = new();
        public List<Picture> Textures = new();
        public Vector3 Min, Max;
    }

    public static Result Load(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".glb" && ext != ".gltf")
            throw new InvalidDataException($"unsupported format: {ext} (only .glb and .gltf; Blender can turn an .fbx into a .glb)");
        var model = ModelRoot.Load(path);
        var res = new Result();
        var texIndex = new Dictionary<Image, int>();
        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault()
                    ?? throw new InvalidDataException("the file has no scene");
        foreach (var node in Walk(scene.VisualChildren))
        {
            if (node.Mesh == null) continue;
            Matrix4x4 world = node.WorldMatrix;
            foreach (var prim in node.Mesh.Primitives)
            {
                var pos = prim.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (pos == null) continue;
                var uv = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                var col = prim.GetVertexAccessor("COLOR_0");
                IList<Vector4> cols = col == null ? null
                    : col.Dimensions == DimensionType.VEC4 ? col.AsVector4Array() : col.AsVector3Array().Select(c => new Vector4(c, 1)).ToList();
                int tex = -1; Vector4 factor = Vector4.One;
                var mat = prim.Material;
                var ch = mat?.FindChannel("BaseColor");
                if (ch != null)
                {
                    factor = ch.Value.Color;
                    var image = ch.Value.Texture?.PrimaryImage;
                    if (image != null)
                    {
                        if (!texIndex.TryGetValue(image, out tex))
                        {
                            tex = res.Textures.Count;
                            res.Textures.Add(Picture.Decode(image.Content.Content.ToArray()));
                            texIndex[image] = tex;
                        }
                    }
                }
                foreach (var (a, b, c) in prim.GetTriangleIndices())
                {
                    var t = new Tri
                    {
                        A = Vector3.Transform(pos[a], world), B = Vector3.Transform(pos[b], world), C = Vector3.Transform(pos[c], world),
                        Texture = tex, Factor = factor,
                    };
                    if (uv != null) { t.Ua = uv[a]; t.Ub = uv[b]; t.Uc = uv[c]; }
                    if (cols != null) { t.Ca = cols[a]; t.Cb = cols[b]; t.Cc = cols[c]; }
                    res.Tris.Add(t);
                }
            }
        }
        if (res.Tris.Count == 0) throw new InvalidDataException("the model has no triangles");
        res.Min = new Vector3(float.MaxValue); res.Max = new Vector3(float.MinValue);
        foreach (var t in res.Tris)
            foreach (var p in new[] { t.A, t.B, t.C }) { res.Min = Vector3.Min(res.Min, p); res.Max = Vector3.Max(res.Max, p); }
        return res;
    }

    static IEnumerable<Node> Walk(IEnumerable<Node> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Walk(n.VisualChildren)) yield return c;
        }
    }

    /// <summary>Turns the model about the vertical axis by quarter turns (for a model that does not face +Z).</summary>
    public static void TurnQuarter(Result r, int quarters)
    {
        quarters = ((quarters % 4) + 4) % 4;
        if (quarters == 0) return;
        var m = Matrix4x4.CreateRotationY(MathF.PI / 2 * quarters);
        foreach (var t in r.Tris) { t.A = Vector3.Transform(t.A, m); t.B = Vector3.Transform(t.B, m); t.C = Vector3.Transform(t.C, m); }
        r.Min = new Vector3(float.MaxValue); r.Max = new Vector3(float.MinValue);
        foreach (var t in r.Tris)
            foreach (var p in new[] { t.A, t.B, t.C }) { r.Min = Vector3.Min(r.Min, p); r.Max = Vector3.Max(r.Max, p); }
    }
}
