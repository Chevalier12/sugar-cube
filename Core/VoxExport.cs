using System.Numerics;
using System.Text;

namespace SugarCube.Core;

/// <summary>
/// Writes cubes as a MagicaVoxel <c>.vox</c> file (format version 150), so they can be edited and cleaned there.
/// MagicaVoxel limits one model to 256 cubes per side and a file to 255 colours, so:
/// - a grid bigger than 256 on a side is cut into blocks of at most 256 (models), placed side by side in the file's
///   scene (nTRN / nGRP / nSHP nodes), so MagicaVoxel shows them together as one figure;
/// - the colours are brought to at most 255 by median cut (each cube takes the nearest of those colours).
/// MagicaVoxel is Z up: our X stays X, our depth Z (front +Z) becomes -Y, our height Y becomes Z.
/// </summary>
public static class VoxExport
{
    const int Max = 256;

    public static (int models, int colours) Write(VoxelGrid g, string path)
    {
        var cubes = new List<(int x, int y, int z, Vector3 c)>();
        for (int x = 0; x < g.Nx; x++) for (int y = 0; y < g.Ny; y++) for (int z = 0; z < g.Nz; z++)
        {
            int k = g.Index(x, y, z);
            if (g.Occ[k]) cubes.Add((x, g.Nz - 1 - z, y, g.Col[k]));  // MagicaVoxel x, y (from our depth), z (our height)
        }
        var palette = MedianCut(cubes.Select(c => c.c).ToList(), 255);
        var index = new byte[cubes.Count];
        for (int i = 0; i < cubes.Count; i++) index[i] = (byte)(Nearest(palette, cubes[i].c) + 1);  // 1..255

        int sx = g.Nx, sy = g.Nz, sz = g.Ny;  // MagicaVoxel sizes
        int bx = (sx + Max - 1) / Max, by = (sy + Max - 1) / Max, bz = (sz + Max - 1) / Max;
        var blocks = new List<(int ox, int oy, int oz, int w, int d, int h, List<int> members)>();
        var map = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < bx; i++) for (int j = 0; j < by; j++) for (int l = 0; l < bz; l++)
        {
            map[(i, j, l)] = blocks.Count;
            blocks.Add((i * Max, j * Max, l * Max, Math.Min(Max, sx - i * Max), Math.Min(Max, sy - j * Max), Math.Min(Max, sz - l * Max), new List<int>()));
        }
        for (int i = 0; i < cubes.Count; i++)
            blocks[map[(cubes[i].x / Max, cubes[i].y / Max, cubes[i].z / Max)]].members.Add(i);
        blocks.RemoveAll(b => b.members.Count == 0);

        var main = new MemoryStream();
        foreach (var b in blocks)
        {
            Chunk(main, "SIZE", w => { w.Write(b.w); w.Write(b.d); w.Write(b.h); });
            Chunk(main, "XYZI", w =>
            {
                w.Write(b.members.Count);
                foreach (int i in b.members)
                {
                    w.Write((byte)(cubes[i].x - b.ox)); w.Write((byte)(cubes[i].y - b.oy)); w.Write((byte)(cubes[i].z - b.oz)); w.Write(index[i]);
                }
            });
        }
        // scene: root transform 0 -> group 1 -> (transform, shape) per model; a model's translation is its centre,
        // which MagicaVoxel takes as the model's position minus half its size (rounded down)
        Chunk(main, "nTRN", w => { w.Write(0); Dict(w); w.Write(1); w.Write(-1); w.Write(-1); w.Write(1); Dict(w); });
        Chunk(main, "nGRP", w =>
        {
            w.Write(1); Dict(w); w.Write(blocks.Count);
            for (int m = 0; m < blocks.Count; m++) w.Write(2 + 2 * m);
        });
        for (int m = 0; m < blocks.Count; m++)
        {
            var b = blocks[m];
            // centre the whole figure on the floor's middle: x and y round 0, feet at z = 0
            int tx = b.ox + b.w / 2 - sx / 2, ty = b.oy + b.d / 2 - sy / 2, tz = b.oz + b.h / 2;
            Chunk(main, "nTRN", w => { w.Write(2 + 2 * m); Dict(w); w.Write(3 + 2 * m); w.Write(-1); w.Write(0); w.Write(1); Dict(w, ("_t", $"{tx} {ty} {tz}")); });
            Chunk(main, "nSHP", w => { w.Write(3 + 2 * m); Dict(w); w.Write(1); w.Write(m); Dict(w); });
        }
        Chunk(main, "RGBA", w =>
        {
            for (int i = 0; i < 256; i++)
            {
                Vector3 c = i < palette.Count ? palette[i] * 255 : Vector3.Zero;
                w.Write((byte)Math.Clamp(MathF.Round(c.X), 0, 255)); w.Write((byte)Math.Clamp(MathF.Round(c.Y), 0, 255));
                w.Write((byte)Math.Clamp(MathF.Round(c.Z), 0, 255)); w.Write((byte)255);
            }
        });

        using var f = new BinaryWriter(File.Create(path));
        f.Write(Encoding.ASCII.GetBytes("VOX ")); f.Write(150);
        f.Write(Encoding.ASCII.GetBytes("MAIN")); f.Write(0); f.Write((int)main.Length);
        f.Write(main.ToArray());
        return (blocks.Count, palette.Count);
    }

    static void Chunk(Stream s, string id, Action<BinaryWriter> content)
    {
        var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.ASCII, true)) content(w);
        using var o = new BinaryWriter(s, Encoding.ASCII, true);
        o.Write(Encoding.ASCII.GetBytes(id)); o.Write((int)body.Length); o.Write(0); o.Write(body.ToArray());
    }

    static void Dict(BinaryWriter w, params (string key, string value)[] pairs)
    {
        w.Write(pairs.Length);
        foreach (var (k, v) in pairs) { Str(w, k); Str(w, v); }
    }

    static void Str(BinaryWriter w, string s) { var b = Encoding.UTF8.GetBytes(s); w.Write(b.Length); w.Write(b); }

    /// <summary>At most <paramref name="n"/> colours that stand for all of them: the box of colours with the widest spread
    /// is cut in two at its median, again and again; each box gives its mean colour.</summary>
    static List<Vector3> MedianCut(List<Vector3> colours, int n)
    {
        var distinct = colours.Distinct().ToList();
        if (distinct.Count <= n) return distinct;
        var boxes = new List<List<Vector3>> { colours };
        while (boxes.Count < n)
        {
            int bi = -1; float spread = -1; int axis = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                Vector3 lo = boxes[i].Aggregate(Vector3.Min), hi = boxes[i].Aggregate(Vector3.Max), r = hi - lo;
                float m = MathF.Max(r.X, MathF.Max(r.Y, r.Z));
                if (m > spread) { spread = m; bi = i; axis = r.X >= r.Y && r.X >= r.Z ? 0 : r.Y >= r.Z ? 1 : 2; }
            }
            if (bi < 0 || spread <= 0) break;
            var box = boxes[bi];
            box.Sort((a, b) => (axis == 0 ? a.X : axis == 1 ? a.Y : a.Z).CompareTo(axis == 0 ? b.X : axis == 1 ? b.Y : b.Z));
            int half = box.Count / 2;
            boxes[bi] = box.GetRange(0, half); boxes.Add(box.GetRange(half, box.Count - half));
        }
        return boxes.Select(b => b.Aggregate(Vector3.Zero, (s, c) => s + c) / b.Count).ToList();
    }

    static int Nearest(List<Vector3> palette, Vector3 c)
    {
        int best = 0; float d = float.MaxValue;
        for (int i = 0; i < palette.Count; i++) { float e = Vector3.DistanceSquared(palette[i], c); if (e < d) { d = e; best = i; } }
        return best;
    }
}
