using System.Numerics;

namespace SugarCube.Core;

/// <summary>
/// A coloured voxel model: cubes on a grid, each with a colour (sRGB, 0..1), plus optional colours per cube FACE
/// (what PixelLab painted). The steps of the method that worked on 2026-11-05 (game_characters/textura_corp/voxel):
/// 1. <see cref="FromModel"/>: a textured model becomes cubes, each the mean colour of the surface inside it.
/// 2. <see cref="View"/>, <see cref="MakeCanvas"/>: the 4 side views at ONE CUBE PER PIXEL (front, her left, back, her
///    right), side by side on one 512 x 512 picture for PixelLab.
/// 3. <see cref="ApplyRepaint"/>: PixelLab's picture back onto the faces it shows, pixel to face; every cube it did not
///    show takes the colour of the nearest painted cube along the surface.
/// 4. <see cref="Merged"/>: k x k x k blocks become one cube (the cube-size slider).
/// The model faces +Z (glTF's front), Y is up. A cube's index is (x * Ny + y) * Nz + z.
/// </summary>
public sealed class VoxelGrid
{
    public int Nx, Ny, Nz;
    public float Cube;           // metres per cube
    public Vector3 Origin;       // world position of the grid's lowest corner
    public bool[] Occ;
    public Vector3[] Col;        // sRGB 0..1, one per cube
    public Vector3[][] FaceCol;  // [direction][cube] sRGB; null on grids not repainted
    public bool[][] FaceSet;     // [direction][cube] the face colour is set
    public bool[] Repainted;     // the cube got a colour from PixelLab

    // directions: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z
    public static readonly (int X, int Y, int Z)[] Dir = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    public int Index(int x, int y, int z) => (x * Ny + y) * Nz + z;
    public bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Nx && y < Ny && z < Nz;
    public bool At(int x, int y, int z) => Inside(x, y, z) && Occ[Index(x, y, z)];
    public int Count => Occ.Count(b => b);
    public (int x, int y, int z) Cell(int k) => (k / (Ny * Nz), k / Nz % Ny, k % Nz);

    static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    static float ToSrgb(float c) { c = Math.Clamp(c, 0, 1); return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f; }
    public static Vector3 ToSrgb(Vector3 c) => new(ToSrgb(c.X), ToSrgb(c.Y), ToSrgb(c.Z));

    // ---------------------------------------------------------------- 1. model -> cubes

    /// <summary>The largest height in cubes whose 4 side views fit side by side on the 512 picture with its margin
    /// (front and back as wide as the model, the two sides as deep).</summary>
    public static int MaxCubesTall(Vector3 size)
    {
        const int room = CanvasSize - 2 * Margin;
        float cube = Math.Max(size.Y / room, 2 * (size.X + size.Z) / room) * 1.01f;
        return Math.Max(8, (int)(size.Y / cube));
    }

    /// <summary>Cubes <paramref name="cubesTall"/> high round the model; a cube exists where the surface passes through
    /// it, its colour the mean (in linear light) of the surface colour sampled in it: every triangle is sampled on a
    /// lattice of half a cube or finer. Texture colours are sRGB, factors and vertex colours linear, as in glTF.</summary>
    public static VoxelGrid FromModel(ModelImport.Result m, int cubesTall, Action<string> progress = null)
    {
        float cube = (m.Max.Y - m.Min.Y) / cubesTall;
        var g = new VoxelGrid { Cube = cube, Origin = m.Min, Ny = cubesTall };
        g.Nx = Math.Max(1, (int)MathF.Ceiling((m.Max.X - m.Min.X) / cube - 1e-4f));
        g.Nz = Math.Max(1, (int)MathF.Ceiling((m.Max.Z - m.Min.Z) / cube - 1e-4f));
        int n = g.Nx * g.Ny * g.Nz;
        var sum = new Vector3[n]; var cnt = new int[n];
        float step = cube * 0.5f;
        int done = 0;
        foreach (var t in m.Tris)
        {
            if (++done % 20000 == 0) progress?.Invoke($"making cubes: {done * 100 / m.Tris.Count}%");
            float edge = MathF.Max((t.B - t.A).Length(), MathF.Max((t.C - t.B).Length(), (t.A - t.C).Length()));
            int lat = Math.Max(1, (int)MathF.Ceiling(edge / step));
            Picture tex = t.Texture >= 0 ? m.Textures[t.Texture] : null;
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lat - i; j++)
                {
                    float a = (float)i / lat, b = (float)j / lat, c = 1 - a - b;
                    Vector3 p = t.A * a + t.B * b + t.C * c;
                    int x = Math.Clamp((int)((p.X - m.Min.X) / cube), 0, g.Nx - 1);
                    int y = Math.Clamp((int)((p.Y - m.Min.Y) / cube), 0, g.Ny - 1);
                    int z = Math.Clamp((int)((p.Z - m.Min.Z) / cube), 0, g.Nz - 1);
                    Vector4 lin = t.Factor * (t.Ca * a + t.Cb * b + t.Cc * c);
                    if (tex != null)
                    {
                        Vector2 uv = t.Ua * a + t.Ub * b + t.Uc * c;
                        float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);  // glTF: v = 0 is the top row
                        var (r, gg, bb, _) = tex.Get(Math.Clamp((int)(u * tex.W), 0, tex.W - 1), Math.Clamp((int)(v * tex.H), 0, tex.H - 1));
                        lin *= new Vector4(ToLinear(r / 255f), ToLinear(gg / 255f), ToLinear(bb / 255f), 1);
                    }
                    int k = g.Index(x, y, z);
                    sum[k] += new Vector3(lin.X, lin.Y, lin.Z); cnt[k]++;
                }
        }
        g.Occ = new bool[n]; g.Col = new Vector3[n];
        for (int k = 0; k < n; k++)
            if (cnt[k] > 0) { g.Occ[k] = true; g.Col[k] = ToSrgb(sum[k] / cnt[k]); }
        return g;
    }

    // ---------------------------------------------------------------- outside air

    /// <summary>Empty cells reachable from outside the grid (air sealed inside a hollow shell is not outside). A cube
    /// face toward such a cell is an outer face.</summary>
    public bool[] Outside()
    {
        int px = Nx + 2, py = Ny + 2, pz = Nz + 2;
        var ext = new bool[px * py * pz];
        var queue = new Queue<int>();
        int P(int x, int y, int z) => (x * py + y) * pz + z;
        ext[0] = true; queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int x = c / (py * pz), y = c / pz % py, z = c % pz;
            foreach (var d in Dir)
            {
                int ax = x + d.X, ay = y + d.Y, az = z + d.Z;
                if (ax < 0 || ay < 0 || az < 0 || ax >= px || ay >= py || az >= pz) continue;
                int q = P(ax, ay, az);
                if (ext[q] || At(ax - 1, ay - 1, az - 1)) continue;
                ext[q] = true; queue.Enqueue(q);
            }
        }
        var outside = new bool[Nx * Ny * Nz];
        for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++)
            outside[Index(x, y, z)] = ext[P(x + 1, y + 1, z + 1)];
        return outside;
    }

    // ---------------------------------------------------------------- 2. views and the picture for PixelLab

    public const int Margin = 21, CanvasSize = 512;
    public static readonly string[] ViewNames = { "front", "left", "back", "right" };
    public static readonly int[] ViewDir = { 4, 0, 5, 1 };  // the face direction each view shows: +Z, +X, -Z, -X

    /// <summary>View v (0 front, from +Z; 1 her left, from +X; 2 back, from -Z; 3 her right, from -X) at one cube per
    /// pixel: for each pixel the index of the nearest cube, or -1. Row 0 is the top.</summary>
    public int[,] View(int v, out int width)
    {
        width = v % 2 == 0 ? Nx : Nz;
        var img = new int[Ny, width];
        var depth = new int[Ny, width];
        for (int r = 0; r < Ny; r++) for (int c = 0; c < width; c++) { img[r, c] = -1; depth[r, c] = int.MinValue; }
        for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++)
        {
            int k = Index(x, y, z);
            if (!Occ[k]) continue;
            int row = Ny - 1 - y, col, near;
            switch (v)
            {
                case 0: col = x; near = z; break;
                case 1: col = Nz - 1 - z; near = x; break;
                case 2: col = Nx - 1 - x; near = -z; break;
                default: col = z; near = -x; break;
            }
            if (near > depth[row, col]) { depth[row, col] = near; img[row, col] = k; }
        }
        return img;
    }

    /// <summary>Left edge of each view on the 512 picture; all start at y = Margin.</summary>
    public int[] ViewLeft() => new[] { Margin, Margin + Nx, Margin + Nx + Nz, Margin + 2 * Nx + Nz };

    public bool FitsCanvas => 2 * (Nx + Nz) <= CanvasSize - 2 * Margin && Ny <= CanvasSize - 2 * Margin;

    /// <summary>The picture for PixelLab: the 4 views side by side, one cube per pixel, transparent around them.</summary>
    public Picture MakeCanvas()
    {
        var img = new Picture(CanvasSize, CanvasSize);
        int[] left = ViewLeft();
        for (int v = 0; v < 4; v++)
        {
            int[,] view = View(v, out int w);
            for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
            {
                int k = view[r, c];
                if (k < 0) continue;
                Vector3 col = Col[k] * 255;
                img.Set(left[v] + c, Margin + r, (byte)MathF.Round(col.X), (byte)MathF.Round(col.Y), (byte)MathF.Round(col.Z), 255);
            }
        }
        return img;
    }

    // ---------------------------------------------------------------- 3. PixelLab's picture back onto the cubes

    /// <summary>Each view of the repainted picture is first moved by the whole-pixel shift (up to 8 px) that lays its
    /// silhouette best on the sent one (PixelLab sometimes moves a figure); then each pixel paints the face of the cube
    /// it shows. Returns one line per step, for the user.</summary>
    public List<string> ApplyRepaint(Picture result)
    {
        var report = new List<string>();
        if (result.W != CanvasSize || result.H != CanvasSize)
        {
            report.Add($"the picture is {result.W}x{result.H}; resized to {CanvasSize}x{CanvasSize}");
            result = result.ResizedNearest(CanvasSize, CanvasSize);
        }
        int n = Nx * Ny * Nz;
        FaceCol = new Vector3[6][]; FaceSet = new bool[6][];
        for (int d = 0; d < 6; d++) { FaceCol[d] = new Vector3[n]; FaceSet[d] = new bool[n]; }
        Repainted = new bool[n];
        int[] left = ViewLeft();
        bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < CanvasSize && y < CanvasSize && result.Data[(y * CanvasSize + x) * 4 + 3] > 127;
        for (int v = 0; v < 4; v++)
        {
            int[,] view = View(v, out int w);
            int bestDx = 0, bestDy = 0; double best = -1;
            for (int dy = -8; dy <= 8; dy++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    long both = 0, either = 0;
                    for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
                    {
                        bool s = view[r, c] >= 0, p = Opaque(left[v] + c + dx, Margin + r + dy);
                        if (s && p) both++;
                        if (s || p) either++;
                    }
                    double iou = either > 0 ? (double)both / either : 0;
                    if (iou > best) { best = iou; bestDx = dx; bestDy = dy; }
                }
            int painted = 0, shown = 0, d = ViewDir[v];
            for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
            {
                int k = view[r, c];
                if (k < 0) continue;
                shown++;
                int px = left[v] + c + bestDx, py = Margin + r + bestDy;
                if (!Opaque(px, py)) continue;
                var (cr, cg, cb, _) = result.Get(px, py);
                FaceCol[d][k] = new Vector3(cr, cg, cb) / 255f; FaceSet[d][k] = true; Repainted[k] = true; painted++;
            }
            report.Add($"{ViewNames[v]}: moved by ({bestDx}, {bestDy}) px, silhouette overlap {best:0.000}, {painted} of {shown} faces painted");
        }
        Finish(report, "PixelLab");
        return report;
    }

    /// <summary>Each cube's colour: the mean of its painted faces; cubes no view painted take the nearest painted colour.</summary>
    void Finish(List<string> report, string by)
    {
        int n = Nx * Ny * Nz;
        for (int k = 0; k < n; k++)
        {
            if (!Repainted[k]) continue;
            Vector3 s = Vector3.Zero; int c = 0;
            for (int d = 0; d < 6; d++) if (FaceSet[d][k]) { s += FaceCol[d][k]; c++; }
            Col[k] = s / c;
        }
        int filled = FillFromRepainted();
        report.Add($"{Repainted.Count(b => b)} cubes painted from {by}; {filled} unseen cubes (top of the head, soles, inner arms) took the colour of the nearest painted cube");
    }

    // ---------------------------------------------------------------- 3b. the original sprites instead of PixelLab

    /// <summary>
    /// Paints the cubes straight from the 4 original sprites the model was made from (the ones given to Meshy), in view
    /// order: [0] south (front), [1] west, [2] north (back), [3] east. The model and the sprite never match pixel for
    /// pixel (Meshy guesses the 3D shape), so each sprite is first fitted onto its view: scaled so the figure is as tall
    /// as the view's, centred and set on the same feet line, then the scale (plus or minus 4%) and the shift (up to 6
    /// sprite pixels) with the best silhouette overlap are kept. Before that, a dark 1 px outline round the sprite's edge
    /// is taken out (each edge pixel clearly darker than the pixel inside it takes that pixel's colour): seen from the
    /// front it would sit on the cubes at the edge of the silhouette, and turned 45 degrees it would show as a dark stripe
    /// along the body. The app's own Outline draws it again, round whatever angle is shown.
    /// </summary>
    public List<string> ApplySprites(Picture[] sprites)
    {
        var report = new List<string>();
        int n = Nx * Ny * Nz;
        FaceCol = new Vector3[6][]; FaceSet = new bool[6][];
        for (int d = 0; d < 6; d++) { FaceCol[d] = new Vector3[n]; FaceSet[d] = new bool[n]; }
        Repainted = new bool[n];
        string[] names = { "south", "west", "north", "east" };
        for (int v = 0; v < 4; v++)
        {
            var sp = sprites[v];
            int stripped = StripOutline(sp);
            int[,] view = View(v, out int w);
            // bounding boxes: the view's cubes and the sprite's opaque pixels
            int vr0 = int.MaxValue, vr1 = -1, vc0 = int.MaxValue, vc1 = -1;
            for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
                if (view[r, c] >= 0) { vr0 = Math.Min(vr0, r); vr1 = Math.Max(vr1, r); vc0 = Math.Min(vc0, c); vc1 = Math.Max(vc1, c); }
            int sr0 = int.MaxValue, sr1 = -1, sc0 = int.MaxValue, sc1 = -1;
            for (int y = 0; y < sp.H; y++) for (int x = 0; x < sp.W; x++)
                if (sp.Data[(y * sp.W + x) * 4 + 3] > 127) { sr0 = Math.Min(sr0, y); sr1 = Math.Max(sr1, y); sc0 = Math.Min(sc0, x); sc1 = Math.Max(sc1, x); }
            if (vr1 < 0 || sr1 < 0) { report.Add($"{names[v]}: nothing to paint (an empty picture?)"); continue; }
            float baseScale = (float)(sr1 - sr0 + 1) / (vr1 - vr0 + 1);  // sprite pixels per cube
            float vcx = (vc0 + vc1 + 1) / 2f, scx = (sc0 + sc1 + 1) / 2f;

            // view cell (r, c) -> sprite pixel, for a scale factor and a shift in sprite pixels
            (int x, int y) Map(int r, int c, float f, float dx, float dy)
            {
                float s = baseScale * f;
                return ((int)MathF.Floor(scx + (c + 0.5f - vcx) * s + dx), (int)MathF.Floor(sr1 + 1 - (vr1 + 1 - (r + 0.5f)) * s + dy));
            }
            bool Opaque((int x, int y) p) => p.x >= 0 && p.y >= 0 && p.x < sp.W && p.y < sp.H && sp.Data[(p.y * sp.W + p.x) * 4 + 3] > 127;
            float bestF = 1, bestDx = 0, bestDy = 0; double best = -1;
            for (float f = 0.96f; f <= 1.041f; f += 0.02f)
                for (float dy = -6; dy <= 6; dy += 1)
                    for (float dx = -6; dx <= 6; dx += 1)
                    {
                        long both = 0, either = 0;
                        for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
                        {
                            bool s = view[r, c] >= 0, p = Opaque(Map(r, c, f, dx, dy));
                            if (s && p) both++;
                            if (s || p) either++;
                        }
                        double iou = either > 0 ? (double)both / either : 0;
                        if (iou > best) { best = iou; bestF = f; bestDx = dx; bestDy = dy; }
                    }
            int painted = 0, shown = 0, dir = ViewDir[v];
            for (int r = 0; r < Ny; r++) for (int c = 0; c < w; c++)
            {
                int k = view[r, c];
                if (k < 0) continue;
                shown++;
                var p = Map(r, c, bestF, bestDx, bestDy);
                if (!Opaque(p)) continue;
                var (cr, cg, cb, _) = sp.Get(p.x, p.y);
                FaceCol[dir][k] = new Vector3(cr, cg, cb) / 255f; FaceSet[dir][k] = true; Repainted[k] = true; painted++;
            }
            report.Add($"{names[v]}: {1 / (baseScale * bestF):0.##} cubes per sprite pixel, shift ({bestDx}, {bestDy}) px, silhouette overlap {best:0.000}, " +
                       $"{painted} of {shown} faces painted, {stripped} outline pixels taken out");
        }
        Finish(report, "the original sprites");
        return report;
    }

    /// <summary>Takes a dark 1 px outline off a sprite, in place: an opaque pixel next to a transparent one, clearly
    /// darker (below 70% of the brightness) than an opaque pixel further inside, takes that pixel's colour. A sprite
    /// drawn without outline is left as it is. Returns how many pixels changed.</summary>
    static int StripOutline(Picture p)
    {
        bool Op(int x, int y) => x >= 0 && y >= 0 && x < p.W && y < p.H && p.Data[(y * p.W + x) * 4 + 3] > 127;
        float Lum(int x, int y) { int i = (y * p.W + x) * 4; return 0.299f * p.Data[i] + 0.587f * p.Data[i + 1] + 0.114f * p.Data[i + 2]; }
        bool Edge(int x, int y) => Op(x, y) && (!Op(x - 1, y) || !Op(x + 1, y) || !Op(x, y - 1) || !Op(x, y + 1));
        var src = (byte[])p.Data.Clone();
        int changed = 0;
        for (int y = 0; y < p.H; y++) for (int x = 0; x < p.W; x++)
        {
            if (!Edge(x, y)) continue;
            int bx = -1, by = -1; float bl = -1;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int ax = x + dx, ay = y + dy;
                if ((dx == 0 && dy == 0) || !Op(ax, ay) || Edge(ax, ay)) continue;
                float l = Lum(ax, ay);
                if (l > bl) { bl = l; bx = ax; by = ay; }
            }
            if (bx < 0 || Lum(x, y) >= 0.7f * bl) continue;
            int i = (y * p.W + x) * 4, j = (by * p.W + bx) * 4;
            p.Data[i] = src[j]; p.Data[i + 1] = src[j + 1]; p.Data[i + 2] = src[j + 2];
            changed++;
        }
        return changed;
    }

    /// <summary>Cubes PixelLab did not paint take the colour of the nearest painted cube, walking cube to cube from all
    /// painted cubes at once. Returns how many were coloured that way.</summary>
    int FillFromRepainted()
    {
        int n = Nx * Ny * Nz, filled = 0;
        var done = (bool[])Repainted.Clone();
        var queue = new Queue<int>();
        for (int k = 0; k < n; k++) if (done[k]) queue.Enqueue(k);
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            var (x, y, z) = Cell(k);
            foreach (var d in Dir)
            {
                int ax = x + d.X, ay = y + d.Y, az = z + d.Z;
                if (!At(ax, ay, az)) continue;
                int q = Index(ax, ay, az);
                if (done[q]) continue;
                done[q] = true; Col[q] = Col[k]; filled++; queue.Enqueue(q);
            }
        }
        return filled;
    }

    // ---------------------------------------------------------------- 4. bigger cubes

    /// <summary>Every k x k x k block becomes one cube when it holds any; its colour the mean of the painted cubes in
    /// it (of all its cubes when none is painted). Faces then take their cube's colour.</summary>
    public VoxelGrid Merged(int k)
    {
        if (k <= 1) return this;
        var g = new VoxelGrid { Cube = Cube * k, Origin = Origin, Nx = (Nx + k - 1) / k, Ny = (Ny + k - 1) / k, Nz = (Nz + k - 1) / k };
        int n = g.Nx * g.Ny * g.Nz;
        g.Occ = new bool[n]; g.Col = new Vector3[n];
        var sum = new Vector3[n]; var cnt = new int[n]; var sumAll = new Vector3[n]; var cntAll = new int[n];
        for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++)
        {
            int i = Index(x, y, z);
            if (!Occ[i]) continue;
            int b = g.Index(x / k, y / k, z / k);
            g.Occ[b] = true;
            sumAll[b] += Col[i]; cntAll[b]++;
            if (Repainted != null && Repainted[i]) { sum[b] += Col[i]; cnt[b]++; }
        }
        for (int b = 0; b < n; b++)
            if (g.Occ[b]) g.Col[b] = cnt[b] > 0 ? sum[b] / cnt[b] : sumAll[b] / cntAll[b];
        return g;
    }

    // ---------------------------------------------------------------- smooth surface directions

    /// <summary>For every cube, the direction the body's surface faces there, as if the cubes were smooth: the solid body
    /// (cubes plus the air sealed inside) is blurred with a box of <paramref name="radius"/> cubes, twice, along each
    /// axis, and the direction is where that blurred density falls fastest. Toon bands use it, so a staircase of cubes on
    /// a curved arm gets one band instead of stripes.</summary>
    public Vector3[] SmoothNormals(bool[] outside, int radius)
    {
        int n = Nx * Ny * Nz;
        var d = new float[n];
        for (int k = 0; k < n; k++) d[k] = outside[k] ? 0 : 1;
        var tmp = new float[n];
        for (int pass = 0; pass < 2; pass++)
        {
            Blur(d, tmp, Nx, Ny * Nz, radius);           // along x: stride Ny * Nz
            Blur(d, tmp, Ny, Nz, radius, alongY: true);  // along y: stride Nz
            Blur(d, tmp, Nz, 1, radius);            // along z: stride 1
        }
        var normal = new Vector3[n];
        float D(int x, int y, int z) => Inside(x, y, z) ? d[Index(x, y, z)] : 0;
        for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++)
        {
            int k = Index(x, y, z);
            if (!Occ[k]) continue;
            var g = new Vector3(D(x + 1, y, z) - D(x - 1, y, z), D(x, y + 1, z) - D(x, y - 1, z), D(x, y, z + 1) - D(x, y, z - 1));
            if (g.LengthSquared() > 1e-12f) normal[k] = -Vector3.Normalize(g);
        }
        return normal;
    }

    /// <summary>A box blur of radius r along one axis of the grid, in place (running sums, so its cost does not grow with r).
    /// The axis has <paramref name="len"/> cells <paramref name="stride"/> apart.</summary>
    void Blur(float[] d, float[] tmp, int len, int stride, int r, bool alongY = false)
    {
        // every line along the axis: its first cell index
        var starts = new List<int>();
        if (stride == Ny * Nz) { for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++) starts.Add(Index(0, y, z)); }
        else if (alongY) { for (int x = 0; x < Nx; x++) for (int z = 0; z < Nz; z++) starts.Add(Index(x, 0, z)); }
        else { for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) starts.Add(Index(x, y, 0)); }
        float inv = 1f / (2 * r + 1);
        foreach (int s0 in starts)
        {
            float sum = 0;
            for (int i = 0; i <= Math.Min(r, len - 1); i++) sum += d[s0 + i * stride];
            for (int i = 0; i < len; i++)
            {
                tmp[i] = sum * inv;
                int add = i + r + 1, drop = i - r;
                if (add < len) sum += d[s0 + add * stride];
                if (drop >= 0) sum -= d[s0 + drop * stride];
            }
            for (int i = 0; i < len; i++) d[s0 + i * stride] = tmp[i];
        }
    }

    // ---------------------------------------------------------------- outer faces (for drawing and export)

    public struct Face
    {
        public Vector3 P0, P1, P2, P3;  // counter-clockwise seen from outside
        public Vector3 Normal;
        public Vector3 Shade;           // the body's smooth surface direction here (for toon bands), not the cube's
        public Vector3 Colour;          // sRGB 0..1
    }

    /// <summary>Every cube face that looks at outside air, with its colour: the painted face colour where PixelLab
    /// painted that face, else the cube's colour.</summary>
    public List<Face> OuterFaces()
    {
        bool[] outside = Outside();
        Vector3[] shade = SmoothNormals(outside, Math.Max(2, Ny / 60));
        var faces = new List<Face>();
        for (int x = 0; x < Nx; x++) for (int y = 0; y < Ny; y++) for (int z = 0; z < Nz; z++)
        {
            int k = Index(x, y, z);
            if (!Occ[k]) continue;
            for (int d = 0; d < 6; d++)
            {
                var dv = Dir[d];
                int ax = x + dv.X, ay = y + dv.Y, az = z + dv.Z;
                if (Inside(ax, ay, az) && !outside[Index(ax, ay, az)]) continue;
                Vector3 nrm = new(dv.X, dv.Y, dv.Z);
                Vector3 b = Origin + new Vector3(x, y, z) * Cube, start, u, w;
                if (dv.X != 0) { start = b + new Vector3(dv.X > 0 ? Cube : 0, 0, 0); u = new Vector3(0, Cube, 0); w = new Vector3(0, 0, Cube); }
                else if (dv.Y != 0) { start = b + new Vector3(0, dv.Y > 0 ? Cube : 0, 0); u = new Vector3(0, 0, Cube); w = new Vector3(Cube, 0, 0); }
                else { start = b + new Vector3(0, 0, dv.Z > 0 ? Cube : 0); u = new Vector3(Cube, 0, 0); w = new Vector3(0, Cube, 0); }
                Vector3 p0 = start, p1 = start + u, p2 = start + u + w, p3 = start + w;
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), nrm) < 0) (p1, p3) = (p3, p1);
                faces.Add(new Face
                {
                    P0 = p0, P1 = p1, P2 = p2, P3 = p3, Normal = nrm, Shade = shade[k] == Vector3.Zero ? nrm : shade[k],
                    Colour = FaceSet != null && FaceSet[d][k] ? FaceCol[d][k] : Col[k],
                });
            }
        }
        return faces;
    }
}
