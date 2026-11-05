using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SugarCube.Core;

namespace SugarCube.Ui;

/// <summary>The window: five steps on the left, the 3D view on the right. Everything heavy runs off the UI thread.</summary>
public sealed class MainWindow : Window
{
    static readonly IBrush Bg = new SolidColorBrush(Color.Parse("#14161C"));
    static readonly IBrush Card = new SolidColorBrush(Color.Parse("#1E222C"));
    static readonly IBrush CardEdge = new SolidColorBrush(Color.Parse("#2C3240"));
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#F2A65A"));
    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9AA3B5"));
    static readonly IBrush ViewBg = new SolidColorBrush(Color.Parse("#0F1116"));

    readonly Pipeline pipe = new();
    readonly Image view = new() { Stretch = Stretch.None };
    readonly TextBlock emptyHint = new() { Text = "Drop the textured .glb from Meshy here\n(or use Open model in step 1)", FontSize = 20, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#5C6475")) };
    readonly TextBlock status = new() { Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    readonly TextBlock modelInfo = Hint("No model yet. Press the button or drop a .glb file onto the window.");
    readonly Slider cubesSlider = new() { Minimum = 16, Maximum = 470, Value = 470, IsEnabled = false };
    readonly TextBlock cubesLabel = Hint("");
    readonly Button voxelizeBtn = Primary("Make the cubes");
    readonly Button canvasBtn = Secondary("Save the picture for PixelLab…");
    readonly Button copyBtn = Secondary("Copy the prompt");
    readonly Button resultBtn = Primary("Load the PixelLab result…");
    readonly TextBlock repaintInfo = Hint("");
    readonly Slider sizeSlider = new() { Minimum = 0, Maximum = Pipeline.Sizes.Length - 1, Value = 0, IsSnapToTickEnabled = true, TickFrequency = 1, IsEnabled = false };
    readonly TextBlock sizeLabel = Hint("");
    readonly ToggleSwitch litSwitch = new() { OnContent = "Lighting", OffContent = "Flat colours (sprite look)", IsChecked = false };
    readonly CheckBox eightBox = new() { Content = "8 directions (otherwise 4)", IsChecked = true };
    readonly Button glbBtn = Secondary("Export .glb…");
    readonly Button spritesBtn = Secondary("Export sprites…");
    readonly Button turnBtn = Secondary("Turn 90°");
    readonly Button openBtn = Primary("Open model (.glb)…");

    // view toolbar: zoom (- / + and the percent), slow turning with a speed, background colour
    readonly TextBlock zoomLabel = new() { Text = "100%", Width = 52, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly ToggleButton spinBtn = new() { Content = "⟳ Rotate", Padding = new Thickness(10, 4) };
    readonly Slider spinSpeed = new() { Minimum = 2, Maximum = 120, Value = 20, Width = 110, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock spinLabel = new() { Text = "20°/s", Width = 48, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly TextBox bgHex = new() { Text = "#0F1116", Width = 84, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    Border viewport;
    Color viewBg = Color.Parse("#0F1116");
    readonly DispatcherTimer spinTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly System.Diagnostics.Stopwatch spinClock = new();

    // camera angle round the model: 0° = in front of her (PixelLab's south), 90° = at her left side (west),
    // 180° = behind her (north), 270° = at her right side (east); tilt = how far above her the camera looks down
    readonly Slider angleSlider = new() { Minimum = 0, Maximum = 359, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock angleLabel = new() { Width = 118, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly Slider tiltSlider = new() { Minimum = -30, Maximum = 85, Width = 110, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock tiltLabel = new() { Width = 40, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    bool syncingAngle;
    int viewHeightPx = 600;
    readonly TextBlock spriteInfo = new() { Foreground = new SolidColorBrush(Color.Parse("#9AA3B5")), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    readonly CheckBox outlineBox = new() { Content = "Outline", IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
    const float MinTilt = -30 * MathF.PI / 180, MaxTilt = 85 * MathF.PI / 180;

    float yaw = 0.45f, pitch = 0.12f, zoom = 1f; Vector2 pan;
    Point? dragFrom; bool rendering, renderAgain;
    string loadWarning = "";
    List<VoxelGrid.Face> shownFaces; VoxelGrid shownGrid;

    public MainWindow()
    {
        Title = "Sugar Cube";
        Width = 1500; Height = 920; MinWidth = 1100; MinHeight = 700;
        Background = Bg;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);

        var steps = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
        steps.Children.Add(new TextBlock { Text = "Sugar Cube", FontSize = 26, FontWeight = FontWeight.SemiBold });
        steps.Children.Add(Hint("A textured 3D model becomes cubes, PixelLab paints them as pixel art, and you pick the cube size and export a .glb or sprites."));
        var pixellabLink = new Button { Content = "Open pixellab.ai", Classes = { "link" } };
        pixellabLink.Click += (_, _) => OpenUrl("https://www.pixellab.ai");
        var meshyLink = new Button { Content = "Open meshy.ai", Classes = { "link" } };
        meshyLink.Click += (_, _) => OpenUrl("https://www.meshy.ai");
        steps.Children.Add(Step("0", "Before you start",
            Bullet("a", "In PixelLab, make your character with 4 directions: south (front), east, north (back), west."),
            Bullet("b", "In Meshy, use Image to 3D with several views: give it the 4 pictures (front, left, back, right) and let it make the texture too."),
            Bullet("c", "Download the textured model as GLB. GLB keeps the texture inside the file; FBX and OBJ keep it in separate files and often arrive white."),
            Bullet("d", "Load that .glb in step 1. Everything else happens here."),
            Row(pixellabLink, meshyLink)));
        steps.Children.Add(Step("1", "Model", modelInfo, Row(openBtn, turnBtn),
            Hint("The model must face you in the front view. If it does not, turn it.")));
        steps.Children.Add(Step("2", "Cubes", cubesLabel, cubesSlider, voxelizeBtn));
        var link = new Button { Content = "Open pixellab.ai", Classes = { "link" } };
        link.Click += (_, _) => OpenUrl("https://www.pixellab.ai");
        steps.Children.Add(Step("3", "PixelLab", Hint("Save the picture (512 x 512, the 4 views). On pixellab.ai, edit it with the prompt below (about 25 generations), download the result and load it here."),
            canvasBtn, Row(copyBtn, link), PromptBox(), resultBtn, repaintInfo));
        steps.Children.Add(Step("4", "Cube size", sizeLabel, sizeSlider, litSwitch));
        steps.Children.Add(Step("5", "Export", spriteInfo, eightBox, Row(glbBtn, spritesBtn)));

        var left = new Border { Width = 400, Background = Bg, Child = new ScrollViewer { Content = steps } };
        viewport = new Border
        {
            Background = ViewBg, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 18, 18, 8), ClipToBounds = true,
            Child = Docked(ViewToolbar(), new Grid { Children = { emptyHint, view, new TextBlock { Text = "drag: turn · wheel: zoom · double-click: back to the front", Foreground = Muted, FontSize = 12, Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Bottom } } }),
        };
        var right = new DockPanel();
        var statusBar = new Border { Padding = new Thickness(4, 0, 18, 14), Child = status };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        right.Children.Add(statusBar); right.Children.Add(viewport);
        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left); root.Children.Add(right);
        Content = root;
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Button>().Class("link"))
        {
            Setters = { new Avalonia.Styling.Setter(ForegroundProperty, Accent), new Avalonia.Styling.Setter(BackgroundProperty, Brushes.Transparent) },
        });

        openBtn.Click += async (_, _) => { var f = await PickFile("Open the model", "3D model (GLB from Meshy)", "*.glb", "*.gltf", "*.fbx", "*.obj"); if (f != null) await LoadModel(f); };
        turnBtn.Click += async (_, _) => { if (pipe.Model == null) return; pipe.Turn(); await Preview(); };
        cubesSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) UpdateCubesLabel(); };
        voxelizeBtn.Click += async (_, _) => await Voxelize();
        canvasBtn.Click += async (_, _) => await SaveCanvas();
        copyBtn.Click += async (_, _) => { await Clipboard!.SetTextAsync(Pipeline.Prompt); Say("Prompt copied. Paste it into PixelLab."); };
        resultBtn.Click += async (_, _) => { var f = await PickFile("The PixelLab result", "Picture", "*.png", "*.webp", "*.jpg"); if (f != null) await ApplyResult(f); };
        sizeSlider.PropertyChanged += async (_, e) => { if (e.Property == Slider.ValueProperty) await ShowSize(); };
        litSwitch.IsCheckedChanged += (_, _) => Redraw();
        glbBtn.Click += async (_, _) => await ExportGlb();
        spritesBtn.Click += async (_, _) => await ExportSprites();
        view.PointerPressed += (_, e) => { dragFrom = e.GetPosition(view); if (e.ClickCount == 2) { yaw = 0; pitch = 0.05f; pan = default; SetZoom(1); } };
        view.PointerReleased += (_, _) => dragFrom = null;
        view.PointerMoved += (_, e) =>
        {
            if (dragFrom is not Point from) return;
            var now = e.GetPosition(view);
            yaw -= (float)(now.X - from.X) * 0.008f; pitch = Math.Clamp(pitch + (float)(now.Y - from.Y) * 0.006f, MinTilt, MaxTilt);
            dragFrom = now; Redraw();
        };
        viewport.PointerWheelChanged += (_, e) => SetZoom(zoom * MathF.Pow(0.88f, (float)e.Delta.Y));
        spinSpeed.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) spinLabel.Text = $"{spinSpeed.Value:0}°/s"; };
        spinBtn.IsCheckedChanged += (_, _) => { if (spinBtn.IsChecked == true) { spinClock.Restart(); spinTimer.Start(); } else spinTimer.Stop(); };
        spinTimer.Tick += (_, _) =>
        {
            float dt = (float)spinClock.Elapsed.TotalSeconds; spinClock.Restart();
            if (shownFaces == null) return;
            yaw -= (float)(spinSpeed.Value * Math.PI / 180) * dt;  // turns her to her right, like a turntable seen from above
            Redraw();
        };
        angleSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !syncingAngle) { yaw = (float)(angleSlider.Value * Math.PI / 180); Redraw(); } };
        tiltSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !syncingAngle) { pitch = (float)(tiltSlider.Value * Math.PI / 180); Redraw(); } };
        outlineBox.IsCheckedChanged += (_, _) => Redraw();
        bgHex.LostFocus += (_, _) => ApplyHex();
        bgHex.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        viewport.SizeChanged += (_, _) => Redraw();
        Opened += async (_, _) => await RunArguments();
        SetEnabled();
    }

    // ---------------------------------------------------------------- layout helpers

    /// <summary>The bar over the 3D view: zoom out / percent / zoom in, slow turning and its speed, background colour
    /// (a few swatches and any #RRGGBB).</summary>
    Control ViewToolbar()
    {
        var zoomOut = new Button { Content = "−", Width = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        var zoomIn = new Button { Content = "+", Width = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        zoomOut.Click += (_, _) => SetZoom(zoom * 1.25f);
        zoomIn.Click += (_, _) => SetZoom(zoom * 0.8f);
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        void Group(params Control[] items)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 18, 6) };
            foreach (var i in items) g.Children.Add(i);
            bar.Children.Add(g);
        }
        Group(new TextBlock { Text = "Zoom", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }, zoomOut, zoomLabel, zoomIn);
        Group(new TextBlock { Text = "Angle", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }, angleSlider, angleLabel,
              new TextBlock { Text = "Tilt", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }, tiltSlider, tiltLabel);
        Group(outlineBox);
        Group(spinBtn, new TextBlock { Text = "Speed", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }, spinSpeed, spinLabel);
        var swatches = new List<Control> { new TextBlock { Text = "Background", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 } };
        foreach (var hex in new[] { "#0F1116", "#3A3F4B", "#808080", "#FFFFFF", "#00B140", "#FF00FF" })
        {
            var b = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.Parse(hex)), BorderBrush = CardEdge, BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(b, hex);
            b.Click += (_, _) => { bgHex.Text = hex; ApplyHex(); };
            swatches.Add(b);
        }
        swatches.Add(bgHex);
        Group(swatches.ToArray());
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#D91E222C")), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 0, 2),
            Margin = new Thickness(12, 12, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = bar,
        };
    }

    /// <summary>The toolbar above the 3D view (not over it, so it never hides the model's head).</summary>
    static Control Docked(Control top, Control fill)
    {
        var d = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        d.Children.Add(top); d.Children.Add(fill);
        return d;
    }

    /// <summary>The camera's angle round the model and its tilt, as numbers and the nearest of PixelLab's 8 directions
    /// (the side the camera sees: 0° sees her front = south). Kept in step with dragging, turning and the sliders.</summary>
    void ShowAngle()
    {
        float deg = yaw * 180 / MathF.PI % 360; if (deg < 0) deg += 360;
        string[] names = { "south", "south-west", "west", "north-west", "north", "north-east", "east", "south-east" };
        string name = names[(int)MathF.Round(deg / 45) % 8];
        float tilt = pitch * 180 / MathF.PI;
        syncingAngle = true;
        angleSlider.Value = Math.Min(359, deg); tiltSlider.Value = tilt;
        syncingAngle = false;
        angleLabel.Text = $"{deg:0}° ({name})";
        tiltLabel.Text = $"{tilt:0}°";
    }

    /// <summary>Metres per screen pixel at the model's centre in the 3D view right now: sprites are drawn at this scale,
    /// so they look like the view (zoom out to make them smaller and more sprite-like).</summary>
    float ViewPixel()
    {
        if (shownGrid == null) return 0.01f;
        float dist = shownGrid.Ny * shownGrid.Cube * 1.9f * zoom;
        return 2 * dist * MathF.Tan(0.55f / 2) / viewHeightPx;
    }

    void ShowSpriteInfo()
    {
        if (shownGrid == null) { spriteInfo.Text = ""; return; }
        float px = ViewPixel(), tall = shownGrid.Ny * shownGrid.Cube / px;
        spriteInfo.Text = $"Sprites are drawn as the view shows the model now: about {tall:0} px tall ({px / shownGrid.Cube:0.#} cubes per pixel). Zoom out for smaller, more sprite-like sprites.";
    }

    /// <summary>Zoom is how far the camera is (1 = the model fills the view); shown as a percent, 100% / distance.</summary>
    void SetZoom(float z)
    {
        zoom = Math.Clamp(z, 0.15f, 4f);
        zoomLabel.Text = $"{100 / zoom:0}%";
        Redraw();
    }

    void ApplyHex()
    {
        string t = (bgHex.Text ?? "").Trim();
        if (!t.StartsWith("#")) t = "#" + t;
        if (t.Length == 7 && Color.TryParse(t, out var c))
        {
            viewBg = c; bgHex.Text = t.ToUpperInvariant();
            viewport.Background = new SolidColorBrush(c);
            Redraw();
        }
        else Say("Background colour: write it as #RRGGBB, for example #808080.");
    }

    static Control Bullet(string mark, string text)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
        var m = new TextBlock { Text = mark, Foreground = Accent, FontWeight = FontWeight.SemiBold, FontSize = 13 };
        var t = Hint(text);
        Grid.SetColumn(t, 1);
        g.Children.Add(m); g.Children.Add(t);
        return g;
    }

    /// <summary>What to tell the user about a file the app cannot read, so nobody is left guessing.</summary>
    static string WhyNot(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".fbx" or ".obj" or ".usdz" or ".stl" or ".blend" or ".3mf" =>
                $"This is a {ext} file. Sugar Cube reads GLB: in Meshy, download the same model again and choose GLB (it keeps the texture inside the file).",
            _ => $"Sugar Cube cannot use a {ext} file. Drop a textured .glb model, or, after step 2, the picture from PixelLab.",
        };
    }

    static TextBlock Hint(string text) => new() { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    static Button Primary(string text) => new() { Content = text, Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    static Button Secondary(string text) => new() { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    static Control Row(params Control[] items)
    {
        var g = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < items.Length; i++) { g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); Grid.SetColumn(items[i], i); g.Children.Add(items[i]); }
        return g;
    }
    static Control PromptBox() => new TextBox
    {
        Text = Pipeline.Prompt, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 12,
    };

    static Control Step(string number, string title, params Control[] body)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Accent,
            Child = new TextBlock { Text = number, Foreground = Brushes.Black, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        head.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(head);
        foreach (var c in body) panel.Children.Add(c);
        return new Border { Background = Card, BorderBrush = CardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = panel };
    }

    void Say(string text) => status.Text = text;

    void SetEnabled()
    {
        bool model = pipe.Model != null, grid = pipe.Grid != null;
        turnBtn.IsEnabled = model; cubesSlider.IsEnabled = model; voxelizeBtn.IsEnabled = model;
        canvasBtn.IsEnabled = grid && pipe.Grid.FitsCanvas; resultBtn.IsEnabled = grid && pipe.Grid.FitsCanvas;
        sizeSlider.IsEnabled = grid; glbBtn.IsEnabled = grid; spritesBtn.IsEnabled = grid;
    }

    void UpdateCubesLabel()
    {
        if (pipe.Model == null) return;
        int n = (int)cubesSlider.Value;
        float h = pipe.Model.Max.Y - pipe.Model.Min.Y;
        cubesLabel.Text = $"{n} cubes tall: {h / n * 100:0.##} cm cubes. The most that fit the PixelLab picture: {pipe.MaxCubesTall}.";
    }

    // ---------------------------------------------------------------- steps

    async Task LoadModel(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".glb" or ".gltf")) { Say(WhyNot(path)); return; }
        Say($"Reading {Path.GetFileName(path)}…");
        try { await Task.Run(() => pipe.LoadModel(path)); }
        catch (Exception e) { Say("Could not read the model: " + e.Message); return; }
        loadWarning = pipe.Model.Textures.Count == 0
            ? " Note: this model has no texture, so its cubes take its plain colours. In Meshy, download it with its texture, as GLB."
            : "";
        var size = pipe.Model.Max - pipe.Model.Min;
        modelInfo.Text = $"{Path.GetFileName(path)}\n{pipe.Model.Tris.Count:N0} triangles, {pipe.Model.Textures.Count} textures, {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m";
        cubesSlider.Maximum = Math.Max(16, pipe.MaxCubesTall);
        cubesSlider.Value = cubesSlider.Maximum;
        UpdateCubesLabel();
        repaintInfo.Text = "";
        await Preview();
    }

    /// <summary>A quick look at the model, made of cubes 96 high, before the real cubes.</summary>
    async Task Preview()
    {
        Say("Preview…");
        var m = pipe.Model;
        var g = await Task.Run(() => VoxelGrid.FromModel(m, 96));
        var faces = await Task.Run(() => g.OuterFaces());
        shownGrid = g; shownFaces = faces; pan = default; emptyHint.IsVisible = false;
        SetEnabled(); Redraw();
        Say("Preview (96 cubes tall). Choose how many cubes you want and press Make the cubes." + loadWarning);
    }

    async Task Voxelize()
    {
        int n = (int)cubesSlider.Value;
        voxelizeBtn.IsEnabled = false;
        var t = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(() => pipe.Voxelize(n, s => Dispatcher.UIThread.Post(() => Say(s))));
        repaintInfo.Text = "";
        sizeSlider.Value = 0;
        await ShowSize();
        var g = pipe.Grid;
        Say($"{g.Count:N0} cubes ({g.Nx} x {g.Ny} x {g.Nz}), {g.Cube * 100:0.##} cm each, in {t.ElapsedMilliseconds / 1000.0:0.0} s." +
            (g.FitsCanvas ? " Now save the picture for PixelLab." : " Too many cubes for the 512 picture: choose fewer."));
        SetEnabled();
    }

    async Task SaveCanvas()
    {
        string name = Path.GetFileNameWithoutExtension(pipe.ModelPath) + "_for_pixellab.png";
        var path = await PickSave("The picture for PixelLab", name, "*.png");
        if (path == null) return;
        await Task.Run(() => pipe.Grid.MakeCanvas().Save(path));
        Say($"Saved: {path}. The prompt is in the box above (Copy the prompt).");
    }

    async Task ApplyResult(string path)
    {
        Say("Putting the PixelLab colours on the cubes…");
        List<string> report;
        try { report = await Task.Run(() => pipe.ApplyRepaint(path)); }
        catch (Exception e) { Say("Could not use the picture: " + e.Message); return; }
        repaintInfo.Text = string.Join("\n", report);
        await ShowSize();
        Say("Done: the model has the PixelLab colours. Move the size slider or export.");
    }

    async Task ShowSize()
    {
        if (pipe.Grid == null) return;
        int k = Pipeline.Sizes[(int)sizeSlider.Value];
        var (g, faces) = await Task.Run(() => pipe.AtSize(k));
        shownGrid = g; shownFaces = faces;
        sizeLabel.Text = $"{g.Cube * 100:0.##} cm cubes · {g.Count:N0} cubes · {g.Ny} tall";
        Redraw();
    }

    async Task ExportGlb()
    {
        var path = await PickSave("Export .glb", Path.GetFileNameWithoutExtension(pipe.ModelPath) + $"_cubes_{shownGrid.Ny}.glb", "*.glb");
        if (path == null) return;
        var faces = shownFaces;
        await Task.Run(() => Export.Glb(faces, path));
        Say($"Saved: {path} ({new FileInfo(path).Length / 1024:N0} KB, {faces.Count:N0} faces).");
    }

    async Task ExportSprites()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where to save the sprites" });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (folder == null) return;
        var (g, faces) = (shownGrid, shownFaces);
        bool eight = eightBox.IsChecked == true, lit = litSwitch.IsChecked == true, outline = outlineBox.IsChecked == true;
        string name = Path.GetFileNameWithoutExtension(pipe.ModelPath) + $"_{g.Ny}";
        float pixel = ViewPixel();
        string sheet = await Task.Run(() => Export.Sprites(faces, pixel, g.Cube, folder, name, eight, lit, outline));
        Say($"Saved: {(eight ? 8 : 4)} sprites, {g.Ny * g.Cube / pixel:0} px tall as in the view, and the sprite sheet {sheet}.");
    }

    // ---------------------------------------------------------------- files

    async Task<string> PickFile(string title, string kind, params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(kind) { Patterns = patterns } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task<string> PickSave(string title, string name, string pattern)
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = name, FileTypeChoices = new[] { new FilePickerFileType(pattern) { Patterns = new[] { pattern } } },
        });
        return f?.TryGetLocalPath();
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".glb" or ".gltf") await LoadModel(path);
        else if (ext is ".png" or ".webp" or ".jpg" && pipe.Grid != null) await ApplyResult(path);
        else if (ext is ".png" or ".webp" or ".jpg") Say("First make the cubes (step 2), then load the PixelLab picture.");
        else Say(WhyNot(path));
    }

    static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ---------------------------------------------------------------- the 3D view

    void Redraw()
    {
        ShowAngle();
        ShowSpriteInfo();
        if (shownFaces == null) return;
        if (rendering) { renderAgain = true; return; }
        rendering = true;
        var bounds = (view.Parent as Control)?.Bounds ?? Bounds;
        int w = Math.Max(64, (int)bounds.Width), h = Math.Max(64, (int)bounds.Height);
        viewHeightPx = h;
        var faces = shownFaces; var g = shownGrid; bool lit = litSwitch.IsChecked == true, outline = outlineBox.IsChecked == true; var bg = viewBg;
        float y = yaw, p = pitch, z = zoom;
        Task.Run(() =>
        {
            Vector3 lo = g.Origin, hi = g.Origin + new Vector3(g.Nx, g.Ny, g.Nz) * g.Cube, c = (lo + hi) / 2;
            float size = (hi - lo).Y;
            float dist = size * 1.9f * z;
            var cam = new Core.Render.Camera
            {
                Target = c, Eye = c + dist * new Vector3(MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), MathF.Cos(y) * MathF.Cos(p)), FovY = 0.55f,
            };
            var pic = Core.Render.Draw(faces, cam, w, h, lit, (bg.R, bg.G, bg.B, 255), outline);
            Dispatcher.UIThread.Post(() =>
            {
                var bmp = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                using (var fb = bmp.Lock()) System.Runtime.InteropServices.Marshal.Copy(pic.Data, 0, fb.Address, pic.Data.Length);
                view.Source = bmp;
                rendering = false;
                if (renderAgain) { renderAgain = false; Redraw(); }
            });
        });
    }

    // ---------------------------------------------------------------- command line (--shot)

    async Task RunArguments()
    {
        if (Program.Arg("--model") is not string model) return;
        await LoadModel(model);
        if (pipe.Model != null)  // a file it cannot read leaves only its message, as for a user
        {
            if (Program.Arg("--cubes") is string c) cubesSlider.Value = int.Parse(c);
            await Voxelize();
            if (Program.Arg("--result") is string r) await ApplyResult(r);
            if (Program.Arg("--size") is string s) { sizeSlider.Value = Array.IndexOf(Pipeline.Sizes, int.Parse(s)); await ShowSize(); }
        }
        if (Program.Arg("--bg") is string bgArg) { bgHex.Text = bgArg; ApplyHex(); }
        if (Program.Arg("--zoom") is string zArg) SetZoom(float.Parse(zArg, System.Globalization.CultureInfo.InvariantCulture));
        if (Program.Flag("--spin")) spinBtn.IsChecked = true;
        if (Program.Flag("--outline")) outlineBox.IsChecked = true;
        if (Program.Arg("--export-sprites") is string exDir && pipe.Grid != null)
        {
            await Task.Delay(800);  // let the view take its size: the sprites use its scale
            var (eg, ef) = (shownGrid, shownFaces);
            float pixel = ViewPixel();
            bool eight = eightBox.IsChecked == true, lit = litSwitch.IsChecked == true, outline = outlineBox.IsChecked == true;  // read on the UI thread
            string sheet = await Task.Run(() => Export.Sprites(ef, pixel, eg.Cube, exDir, Path.GetFileNameWithoutExtension(pipe.ModelPath), eight, lit, outline));
            Say($"sprites {eg.Ny * eg.Cube / pixel:0} px tall: {sheet}");
        }
        if (Program.Arg("--angle") is string aArg) angleSlider.Value = double.Parse(aArg, System.Globalization.CultureInfo.InvariantCulture);
        if (Program.Arg("--tilt") is string tArg) tiltSlider.Value = double.Parse(tArg, System.Globalization.CultureInfo.InvariantCulture);
        if (Program.Arg("--shot") is string shot)
        {
            await Task.Delay(1500);
            var px = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new RenderTargetBitmap(px);
            rtb.Render(this);
            rtb.Save(shot);
            Close();
        }
    }
}
