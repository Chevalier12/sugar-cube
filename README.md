# Sugar Cube

**Turn a textured 3D model into a voxel character that looks exactly like a pixel-art sprite, from every angle.**

![Sugar Cube](docs/screenshot.png)

Sugar Cube takes a textured 3D model (for example one made by Meshy from your PixelLab sprite), turns it into cubes,
hands four views of those cubes to PixelLab to repaint as real pixel art, and puts every painted pixel back onto the
cube it came from. Then you pick the cube size with a slider and export a `.glb`, a MagicaVoxel `.vox` or a sprite
sheet in 8 directions.

**[Download Sugar Cube for Windows](https://github.com/Chevalier12/sugar-cube/releases/latest)**: one `SugarCube.exe`, nothing to install.

![The 8 sprites Sugar Cube exported: 465 cubes tall, cube size one step up, zoomed out to 25%, outline on, no lighting](docs/sprites.png)

> **Sprite or voxel: it depends on the zoom.** The model looks like a sprite when you zoom out until it is about the
> size of the original sprite on screen, so that one cube is roughly one screen pixel. The closer you zoom in, the more
> each cube shows as a cube and the more it looks like a voxel model. That is not a problem if a voxel look is what you
> want; for the sprite look, keep the camera far enough away (or export sprites, which are always one cube per pixel).

> **Tested only with humanoid characters so far.** Everything here was made and checked on standing human figures.
> What it does with an animal or an object (a sword, a chair, a table) is not known yet: it may work, or the four side
> views may not be the right ones to paint.

## Why it exists

I am making a game that should **look 2D but be 3D underneath**.

Hand-drawn sprites look great, but they are dead ends: every outfit has to be redrawn for every frame of every
animation, and every new animation has to be drawn by hand. A 3D character can wear clothes that are fitted once and
follow the body, and it can be animated from real movement with **RGBMoCap** (motion capture from ordinary video).
The catch is the look: a 3D character does not look like pixel art. Sugar Cube closes that gap. The character stays 3D,
so it can be dressed and animated, and it is drawn as cubes whose colours were painted as a sprite, so on screen it reads
as a sprite.

## How much it took to get here

This app is the one thing that worked at the end of a very long list of things that did not. From late October to
early November 2026, almost every day ended in a dead end. Here is that list, roughly in order.

**Making the characters move**

- **Text-to-motion (ARDY, Kimodo).** Asked for a low obstacle, it jumped; asked to go "over a box", it climbed a 2.5 m
  wall in almost every seed. Text alone never put the jump on the box, and ARDY ships no training code.
- **MotionBricks, fine-tuned ("Recipe D").** Our own primitives, a ghost, target poses. It ruined the idles.
- **Looping idles with a generated seam.** It actually looked good, and it was dropped with the rest of the machine
  learning route. What stayed: **RGBMoCap**, motion capture from ordinary video, the one tool that worked well.

**Making 3D look like 2D**

- **Rendering 3D to sprites, the Dead Cells way,** then a live anime look in Godot: 3-band shading, 2x pixels, 10 fps
  stepped animation. Judged: the baked sprite scene looked terrible; the model had to stay 3D in the game.
- **A "sprite oracle".** A VRoid model shaped and coloured until it matched the PixelLab sprite from the same view.
  The body got within about 10%, the face never did: a face does not survive at 18 pixels wide. Abandoned.
- **A Blender pixel filter** (scale down, pixelate, scale up), tried on a chair and then on the characters. Another
  step, not the answer.
- **A Genshin-style shader** on VRoid characters, kept as an extra look behind a key.
- **A voxel woman from a single sprite.** Height from a normal map, front plus back, then our own rewrite of ISOMER
  from 8 views, then a MakeHuman body fitted to the outlines. Filters, cel shading and painted looks were all rejected.

**Bodies**

- **VRoid bodies with home-made morph sliders** (183 zone morphs plus bone sliders). Slimming pushed the skin through the clothes.
- **MakeHuman** became the base body, then **11 fixed body types** instead of sliders, then back to sliders on a
  **low-poly MakeHuman** with every main slider working in the game.
- **Free asset packs.** Quaternius only offers superhero bodies for free (even the baby looked like a bodybuilder);
  City People has the clothes modelled into the bodies.

**Clothes**

- **Fitting Meshy garments by measurement** onto the bodies, then **MetaTailor**, then **our own MetaTailor replacement**
  (Croitor). The walk still looked worse than MetaTailor's.
- **Cloak physics**: SoftBody, SpringBone, a GPU cloth simulator. After a day of clipping, physics was taken out of the
  game entirely until there is a good solution.
- **Clothes on the 2D sprites instead**: FLUX.2 klein in ComfyUI on a 6 GB card, then ChatGPT frame by frame (a
  different tunic in every frame), then ChatGPT on a whole 3x3 grid (the same tunic, but the body and face redrawn,
  and never the pixel grid it was given).

**Textures**

- **Painting the 3D body's texture map** with ChatGPT and PixelLab. ChatGPT painted the face upright on a head piece
  that lies sideways, so the eyes landed on the cheeks. PixelLab drew an outline round every piece of the map, and on
  the body every outline became a red seam down the back and round the knees.
- **The Meshy texture.** Hundreds of small scraps in no order. PixelLab painted eyes on the back and on the forehead.
- **A body rebuilt from its texture pattern alone,** like sewing and inflating a plush toy. A little person came out,
  23 to 26 cm away from the real body on average.

**Engines, on the way**

- **An Unreal port**, designed and dropped. **Godot embedded as a library** (LibGodot), started and abandoned
  mid-build for lack of time.

**What finally worked: one cube per pixel.** Seen straight from the front, a voxel model already *is* pixel art: one
cube is one pixel. So the four side views (front, left, back, right) go to PixelLab side by side on one 512 x 512
picture, at their real size, with nothing scaled. PixelLab is at its best there, kept every figure in place, and its
pixels go back onto the cubes one to one, with no seams and nothing to guess.

That last step was done by hand once, on one character, with a pile of scripts. Sugar Cube makes it a few clicks, so
anyone can do it.

## Before you start: from a sprite to a model

1. In **PixelLab**, make your character with 4 directions: south (front), east, north (back), west.
2. In **Meshy**, use Image to 3D with several views: give it the 4 pictures and let it make the texture too.
3. Download the textured model as **GLB**. GLB keeps the texture inside the one file; FBX and OBJ keep it in separate
   files and often import white ([Meshy: export formats](https://docs.meshy.ai/en/webapp/guides/platform/export-formats)).
4. Load that `.glb` in Sugar Cube. Everything else happens in the app.

## The steps in the app

1. **Model.** Open or drop the `.glb`. The model must face you in the front view; if it does not, press *Turn 90°*.
2. **Cubes.** Choose how many cubes tall (the maximum is what fits the 512 picture for PixelLab) and press
   *Make the cubes*.
3. **PixelLab.** *Save the picture for PixelLab* writes the 512 x 512 PNG with the 4 views, one cube per pixel. On
   pixellab.ai, edit it with the app's prompt (*Copy the prompt*); it costs about 25 generations from your account.
   Download the result and load it with *Load the PixelLab result* (or drop it onto the window). The app lines each
   view up again by itself if PixelLab moved a figure, and cubes none of the views can see (top of the head, soles,
   inner arms) take the colour of the nearest painted cube.
4. **Cube size.** A slider merges the cubes 2 x 2 x 2, 3 x 3 x 3 and so on up to 20 x 20 x 20, live.
5. **Export.** *.glb* writes the cubes at the chosen size (one colour per face). *Sprites* writes 8 (or 4) pictures
   drawn exactly as the 3D view shows the model at that moment, so the zoom sets their size: zoom out for small,
   sprite-like pictures. It also writes a one-row sprite sheet and a `.txt` with the order and the scale. The sprites
   above were made with 465 cubes, the cube-size slider one step from the left, the view at 25%, outline on and
   lighting off.
   *.vox for MagicaVoxel* writes the cubes at the chosen size for editing and cleaning in
   [MagicaVoxel](https://ephtracy.github.io/): one colour per cube, at most 255 colours (MagicaVoxel's limit, reached by
   median cut). MagicaVoxel models stop at 256 cubes per side, so a taller figure (465 cubes, for example) is cut into
   blocks of 256 that the file places side by side, and MagicaVoxel shows them together as one figure.

Above the 3D view: zoom (with the percent), the camera's angle round the model (with the PixelLab direction it shows)
and its tilt, slow turning with a speed, a 1 px outline, a toon slider (3 flat bands of light, mid and shadow, lit from
the viewer's upper left, from 0 = off to 100% = full bands; the bands follow the smooth shape of the body, not each
cube), and the background colour. The outline and the toon shading are used by the sprite export too.

![What was sent to PixelLab (left) and what came back (right)](docs/pixellab_before_after.png)

## Command line

Every step also runs without the window, for tests and batches:

    SugarCube --model M.glb [--turn 1] [--cubes 465] [--canvas OUT.png] [--result PIXELLAB.png] [--size 4]
              [--glb OUT.glb] [--vox OUT.vox] [--sprites FOLDER] [--sprite-height PX] [--four] [--lit] [--outline] [--toon 0-100] [--shot WINDOW.png]

## Build

Needs the .NET 8 SDK. Windows:

    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

`publish\SugarCube.exe` then runs on any Windows PC with nothing installed.

## What it does not do (yet)

- It has only been tried on humanoid characters (see the note at the top).
- It reads `.glb` and `.gltf` only. Meshy exports `.glb`; Blender can turn an `.fbx` into one.
- It does not talk to PixelLab itself: you put the picture on the site (no API key in the app).
- It does not add clothes: whatever is on the model goes to PixelLab as it is.
- The outline is drawn round the silhouette only, not inside the model.

## Built with

[Avalonia](https://avaloniaui.net/) for the window, [SharpGLTF](https://github.com/vpenades/SharpGLTF) to read and write
glTF, and [SkiaSharp](https://github.com/mono/SkiaSharp) for pictures. The 3D view and the sprites are drawn by a small
software renderer in the app, so what you see is exactly what you export.
