# GmatConverter

A standalone Windows tool for building and converting Gorilla Tag cosmetic materials for
[GorillaCosmetics](https://github.com/KiwiOnGit/ReGorillaCosmeticsMod) (and compatible mods).
No Unity install required -- it reads/writes everything in managed code.

It does two things:

1. **Converts old `.gmat` files** (Unity AssetBundle-based, built against Unity 2019.3.2f1 /
   Built-in Render Pipeline) to the modern **`.gmatplus` "simple" format**: a small zip of
   `package.json` + `material.json` + `albedo.png` (+ an optional `tagged.png`). The mod
   builds the material fresh on the game's own live shader from that PNG, so there's zero
   AssetBundle/Unity-version/stereo-rendering risk -- the source of nearly every legacy
   `.gmat` rendering bug (pink/white materials, invisible skins, left-eye-only rendering in
   VR). Converting upgrades an old material to work reliably on the current client.
2. **Creates brand-new custom skins from scratch**: import a PNG, tweak tint/tiling/animation,
   and export straight to `.gmatplus` -- no `.gmat`/AssetBundle ever involved.

## Using it

- **Open .gmat** -- load an existing legacy material to inspect/re-export it.
- **New skin** -- start a blank material and import your own PNG.
- **Main texture** / **Tagged texture** -- the tagged texture is optional and is shown only
  while the wearer is tagged/"it" in-game; leave it empty to keep showing the main texture
  while tagged (this is the modern equivalent of the old mod's separate "infected material"
  selection, now authored as part of one material file instead of a second config pick).
- **Shader** -- metadata only. The `.gmatplus` this tool writes is always built by the mod on
  its own live shader (`GorillaTag/UberShader`) for safety, regardless of what's picked here;
  this field just records what you intended for your own reference.
- **Tint / Tiling / Offset / Animation** -- same properties the mod reads at runtime
  (`none` / `flipbook` / `hue` / `scroll`), previewed live.
- **3D preview tab** -- drag to rotate, scroll to zoom. Renders the real gorilla body mesh
  with your material applied, plus a fixed reference thumbnail of the face/chest look (that
  part isn't something this tool edits, and the separate face/chest meshes don't carry enough
  transform data outside Unity to be composited into the 3D view accurately, so it's shown
  flat instead of risking a visibly-misplaced 3D decal). The rasterizer doesn't backface-cull
  (the ripped mesh's submeshes don't all share one consistent winding order, which used to
  show the inside of some parts) -- it relies on the Z-buffer alone, which is correct
  regardless of winding for a closed mesh. **Requires a local, one-time setup step** -- see "3D
  preview meshes" below; without it this tab shows a message instead of a model.
- **Export .gmatplus** -- if there's no main texture, you'll get a warning first: shipping a
  textureless material renders as a plain white skin in-game (Unity's fallback for a missing
  texture), so this is your chance to notice before sharing a broken file.

Drop the resulting `.gmatplus` into `Gorilla Tag/BepInEx/plugins/GorillaCosmetics/Cosmetics/Materials`.

## Command-line checks

```
GmatConverter --extract <file.gmat> [out.gmatplus]   # dump what was parsed, optionally convert
GmatConverter --testmesh                              # sanity-check the bundled preview meshes
GmatConverter --renderpreview <out.png> [file.gmat]   # render one 3D preview frame to a PNG
```

## Building

.NET 8 SDK, Windows (WinForms + `net8.0-windows`).

```powershell
dotnet build -c Release
```

Publishing produces a self-contained single-file exe (`PublishSingleFile`/`SelfContained` are
set in the `.csproj`) -- no .NET runtime needs to be installed on the machine that runs it.

## 3D preview meshes (local setup, one time)

The 3D preview needs the actual gorilla body/face/chest meshes, which are Another Axiom LLC's
copyrighted game assets -- **this repo does not include them**, and `Resources/Mesh/*.asset`
is gitignored on purpose. To enable the tab on your own machine:

1. Export the game's `resources.assets` (or the relevant addressables bundle) with
   [AssetRipper](https://github.com/AssetRipper/AssetRipper) as a Unity project.
2. Find the ripped `Gorilla.asset`, `gorillaface.asset` and `gorillachest.asset` mesh files
   under its `Assets/Mesh/` output and copy them into this project's `Resources/Mesh/` folder.
3. Rebuild. `GmatConverter --testmesh` reports vertex/triangle counts for a quick sanity check.

Without them the app still builds and runs fine -- the 3D tab just shows a short message
instead of a model. Keep whatever you export for personal use only; don't redistribute them.

## How it works

- Reads the old `.gmat`'s AssetBundle directly via
  [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) + `AssetsTools.NET.Texture`
  (texture decode) -- no Unity Editor, no external tools.
- The 3D preview's gorilla body/face/chest meshes, when present locally, are parsed straight
  out of AssetRipper's "as Unity project" YAML export (interleaved vertex streams, decoded by
  hand) and rendered with a small built-in software rasterizer -- no OpenGL/DirectX/WebView
  dependency, keeping the tool a single portable exe.

## Disclaimer

This product is not affiliated with Another Axiom Inc. or its videogames Gorilla Tag and
Orion Drift and is not endorsed or otherwise sponsored by Another Axiom. Portions of the
materials contained herein are property of Another Axiom. ©2021 Another Axiom Inc
