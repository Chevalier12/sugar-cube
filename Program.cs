using Avalonia;
using SugarCube.Core;

namespace SugarCube;

/// <summary>Sugar Cube: a textured 3D model -> cubes -> PixelLab repaints 4 views -> back on the cubes -> any cube
/// size, .glb and sprites. Without arguments it opens the window. With --model it can also run the steps from the
/// command line (for tests and batches):
///   SugarCube --model M.glb [--turn 1] [--cubes 470] [--canvas OUT.png] [--result PIXELLAB.png] [--size 4]
///               [--glb OUT.glb] [--vox OUT.vox] [--sprites FOLDER] [--sprite-height PX] [--four] [--lit] [--outline] [--shot WINDOW.png]
/// --shot opens the window with those steps done, saves a picture of it and closes.</summary>
public static class Program
{
    public static string[] Args = Array.Empty<string>();

    public static string Arg(string name) { int i = Array.IndexOf(Args, name); return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null; }
    public static bool Flag(string name) => Array.IndexOf(Args, name) >= 0;

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        if (Arg("--model") != null && !Flag("--shot"))
            return RunCommandLine();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    /// <summary>Runs the given steps in order and prints what happened.</summary>
    public static int RunCommandLine()
    {
        try
        {
            var p = new Pipeline();
            var t = System.Diagnostics.Stopwatch.StartNew();
            p.LoadModel(Arg("--model"));
            Console.WriteLine($"model: {p.Model.Tris.Count} triangles, {p.Model.Textures.Count} textures, size {p.Model.Max - p.Model.Min} m ({t.ElapsedMilliseconds} ms)");
            if (Arg("--turn") is string turn) for (int i = 0; i < int.Parse(turn); i++) p.Turn();
            int cubes = Arg("--cubes") is string c ? int.Parse(c) : p.MaxCubesTall;
            t.Restart();
            p.Voxelize(cubes);
            Console.WriteLine($"cubes: {p.Grid.Nx} x {p.Grid.Ny} x {p.Grid.Nz}, {p.Grid.Count} cubes, {p.Grid.Cube * 100:0.###} cm each, fits the 512 picture: {p.Grid.FitsCanvas} ({t.ElapsedMilliseconds} ms)");
            if (Arg("--canvas") is string canvas) { p.Grid.MakeCanvas().Save(canvas); Console.WriteLine($"picture for PixelLab: {canvas}"); }
            if (Arg("--result") is string result)
            {
                t.Restart();
                foreach (var line in p.ApplyRepaint(result)) Console.WriteLine("  " + line);
                Console.WriteLine($"result applied ({t.ElapsedMilliseconds} ms)");
            }
            int k = Arg("--size") is string s ? int.Parse(s) : 1;
            t.Restart();
            var (g, faces) = p.AtSize(k);
            Console.WriteLine($"size k={k}: {g.Cube * 100:0.##} cm cubes, {g.Count} cubes, {faces.Count} outer faces ({t.ElapsedMilliseconds} ms)");
            if (Arg("--vox") is string vox) { var (vm, vc) = VoxExport.Write(g, vox); Console.WriteLine($"vox: {vox} ({new FileInfo(vox).Length / 1024} KB, {vm} models, {vc} colours)"); }
            if (Arg("--glb") is string glb) { Export.Glb(faces, glb); Console.WriteLine($"glb: {glb} ({new FileInfo(glb).Length / 1024} KB)"); }
            if (Arg("--sprites") is string dir)
            {
                // --sprite-height N: the model N pixels tall (seen from further away); default one cube per pixel
                float pixel = Arg("--sprite-height") is string sh ? g.Ny * g.Cube / float.Parse(sh, System.Globalization.CultureInfo.InvariantCulture) : g.Cube;
                string sheet = Export.Sprites(faces, pixel, g.Cube, dir, Path.GetFileNameWithoutExtension(p.ModelPath), !Flag("--four"), Flag("--lit"), Flag("--outline"));
                Console.WriteLine($"sprites: {sheet}");
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }
    }
}
