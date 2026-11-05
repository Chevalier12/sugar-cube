using System.Numerics;

namespace SugarCube.Core;

/// <summary>A small software renderer for the cube faces: z-buffered triangles sampled at pixel centres, flat colour
/// per face, optionally lit (one light from the upper left plus soft ambient). Used for the window's 3D view and for
/// the sprites, so both show exactly the same colours.</summary>
public static class Render
{
    public sealed class Camera
    {
        public Vector3 Eye, Target, Up = Vector3.UnitY;
        public bool Ortho;
        public float FovY = 0.65f;      // perspective, radians
        public float OrthoHeight = 2f;  // ortho: world height of the picture
        public Vector2 OrthoShift;      // ortho: world offset of the picture centre along (right, up)
    }

    static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.45f, 0.75f, 0.55f));

    /// <summary>Draws the faces into a w x h RGBA picture; background (r, g, b, a). With <paramref name="outline"/>,
    /// every background pixel touching the model (left, right, above, below) becomes a 1 px outline in a dark shade of
    /// the model's colour there.</summary>
    public static Picture Draw(IReadOnlyList<VoxelGrid.Face> faces, Camera cam, int w, int h, bool lit, (byte r, byte g, byte b, byte a) bg, bool outline = false)
    {
        var pic = new Picture(w, h);
        for (int i = 0; i < w * h; i++) { pic.Data[i * 4] = bg.r; pic.Data[i * 4 + 1] = bg.g; pic.Data[i * 4 + 2] = bg.b; pic.Data[i * 4 + 3] = bg.a; }
        var zbuf = new float[w * h];
        Array.Fill(zbuf, float.MaxValue);
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, cam.Up));
        Vector3 up = Vector3.Cross(right, fwd);
        float f = 1f / MathF.Tan(cam.FovY / 2), aspect = (float)w / h;

        // world -> (screen x, screen y, depth)
        Vector3 Project(Vector3 p)
        {
            Vector3 q = p - cam.Eye;
            float x = Vector3.Dot(q, right), y = Vector3.Dot(q, up), z = Vector3.Dot(q, fwd);
            if (cam.Ortho)
            {
                float scale = h / cam.OrthoHeight;
                return new Vector3(w / 2f + (x - cam.OrthoShift.X) * scale, h / 2f - (y - cam.OrthoShift.Y) * scale, z);
            }
            if (z < 1e-4f) z = 1e-4f;
            return new Vector3(w / 2f + x * f / z * h / 2, h / 2f - y * f / z * h / 2, z);
        }

        foreach (var face in faces)
        {
            Vector3 toEye = cam.Ortho ? -fwd : cam.Eye - face.P0;
            if (Vector3.Dot(face.Normal, toEye) <= 0) continue;  // back face
            Vector3 c = face.Colour;
            if (lit)
            {
                float k = 0.6f + 0.4f * MathF.Max(0, Vector3.Dot(face.Normal, Light));
                c *= k;
            }
            byte cr = (byte)Math.Clamp(MathF.Round(c.X * 255), 0, 255), cg = (byte)Math.Clamp(MathF.Round(c.Y * 255), 0, 255), cb = (byte)Math.Clamp(MathF.Round(c.Z * 255), 0, 255);
            Vector3 a0 = Project(face.P0), a1 = Project(face.P1), a2 = Project(face.P2), a3 = Project(face.P3);
            Tri(a0, a1, a2); Tri(a0, a2, a3);

            void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                if (MathF.Abs(area) < 1e-9f) return;
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
                int x1 = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
                int y1 = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
                float inv = 1 / area;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((p1.X - px) * (p2.Y - py) - (p1.Y - py) * (p2.X - px)) * inv;
                        float w1 = ((p2.X - px) * (p0.Y - py) - (p2.Y - py) * (p0.X - px)) * inv;
                        float w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
                        int idx = y * w + x;
                        if (z >= zbuf[idx]) continue;
                        zbuf[idx] = z;
                        pic.Data[idx * 4] = cr; pic.Data[idx * 4 + 1] = cg; pic.Data[idx * 4 + 2] = cb; pic.Data[idx * 4 + 3] = 255;
                    }
            }
        }
        if (outline) Outline(pic, zbuf, w, h);
        return pic;
    }

    /// <summary>Darkness of the outline: the model's neighbouring colour times this.</summary>
    public const float OutlineShade = 0.35f;

    static void Outline(Picture pic, float[] zbuf, int w, int h)
    {
        var src = (byte[])pic.Data.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (zbuf[i] != float.MaxValue) continue;  // a model pixel: kept
                int n = -1;
                if (x > 0 && zbuf[i - 1] != float.MaxValue) n = i - 1;
                else if (x < w - 1 && zbuf[i + 1] != float.MaxValue) n = i + 1;
                else if (y > 0 && zbuf[i - w] != float.MaxValue) n = i - w;
                else if (y < h - 1 && zbuf[i + w] != float.MaxValue) n = i + w;
                if (n < 0) continue;
                pic.Data[i * 4] = (byte)(src[n * 4] * OutlineShade); pic.Data[i * 4 + 1] = (byte)(src[n * 4 + 1] * OutlineShade);
                pic.Data[i * 4 + 2] = (byte)(src[n * 4 + 2] * OutlineShade); pic.Data[i * 4 + 3] = 255;
            }
    }

    /// <summary>PixelLab's 8 directions: the camera's side (where it stands) for each. The model faces +Z, so "south"
    /// (facing the viewer) is seen from +Z and "east" (facing right in the picture) from -X.</summary>
    public static readonly (string Name, Vector3 From)[] Directions =
    {
        ("south", new(0, 0, 1)), ("south-east", new(-1, 0, 1)), ("east", new(-1, 0, 0)), ("north-east", new(-1, 0, -1)),
        ("north", new(0, 0, -1)), ("north-west", new(1, 0, -1)), ("west", new(1, 0, 0)), ("south-west", new(1, 0, 1)),
    };

    /// <summary>One sprite: the model seen level from one side, orthographic, <paramref name="pixel"/> metres per pixel
    /// (the cube size gives one cube per pixel; a bigger value is the model seen from further away, each pixel taking
    /// the colour of the cube at its centre, as the 3D view does), transparent background, 2 px of air round it.</summary>
    public static Picture Sprite(IReadOnlyList<VoxelGrid.Face> faces, float pixel, Vector3 from, bool lit, bool outline = false)
    {
        from = Vector3.Normalize(from);
        Vector3 fwd = -from, right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY)), up = Vector3.UnitY;
        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        foreach (var fc in faces)
            foreach (var p in new[] { fc.P0, fc.P1, fc.P2, fc.P3 })
            {
                float r = Vector3.Dot(p, right), u = Vector3.Dot(p, up);
                minR = MathF.Min(minR, r); maxR = MathF.Max(maxR, r); minU = MathF.Min(minU, u); maxU = MathF.Max(maxU, u);
            }
        const int pad = 2;
        int w = (int)MathF.Ceiling((maxR - minR) / pixel - 1e-3f) + 2 * pad, h = (int)MathF.Ceiling((maxU - minU) / pixel - 1e-3f) + 2 * pad;
        // the picture's centre in (right, up), so its left and top edges lie on the cube grid
        float left = minR - pad * pixel, top = maxU + pad * pixel;
        var cam = new Camera
        {
            Ortho = true, Eye = from * 100f, Target = Vector3.Zero, OrthoHeight = h * pixel,
            OrthoShift = new Vector2(left + w * pixel / 2 - Vector3.Dot(from * 100f, right), top - h * pixel / 2 - Vector3.Dot(from * 100f, up)),
        };
        return Draw(faces, cam, w, h, lit, (0, 0, 0, 0), outline);
    }
}
