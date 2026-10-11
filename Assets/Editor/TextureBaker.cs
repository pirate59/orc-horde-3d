using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes every texture the game uses into Assets/Resources/Art, so nothing is
/// built at runtime and the results sit in the repo to look at.
///
/// Ground tiles come from Orc Horde's own hand-painted biome atlases (2x2:
/// grassland, canyon, snow, quarry dirt). Cliff faces, metal, wood and stone
/// are generated from tileable noise. Every albedo gets a normal map derived
/// from its height.
///
/// Menu: Orc Horde/Bake Textures. Headless:
///   Unity -batchmode -quit -projectPath . -executeMethod TextureBaker.Run [-orcHorde path]
/// </summary>
public static class TextureBaker
{
    const string OutDir = "Assets/Resources/Art";
    const int Size = 512;
    static readonly string[] Biomes = { "grassland", "canyon", "snow", "dirt" };

    [MenuItem("Orc Horde/Bake Textures")]
    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        string orc = OrcHordeSprites();

        // Orc Horde's atlases, quartered: the path takes the terrain tiles, the tower ground the calm rock slabs.
        SliceAtlas(Path.Combine(orc, "terrain-biomes.png"), "path");
        SliceAtlas(Path.Combine(orc, "rock-biomes.png"), "ground");

        Write("cliff", Cliff(), 3.5f);
        Write("metal", Metal(), 4f);
        Write("wood", Wood(), 3f);
        Write("stone", Stone(), 3f);
        Write("skin", Skin(), 2f);

        AssetDatabase.Refresh();
        ConfigureImporters();
        Debug.Log("[TextureBaker] Baked textures to " + OutDir);
    }

    static string OrcHordeSprites()
    {
        var args = System.Environment.GetCommandLineArgs();
        string root = System.Environment.GetEnvironmentVariable("HOME") + "/code/orc-horde/branches/claude-new-build";
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-orcHorde") root = args[i + 1];
        return Path.Combine(root, "public/sprites/enhanced");
    }

    // ---------------------------------------------------------------- output

    /// <summary>Writes an albedo and, from its height, a normal map.</summary>
    static void Write(string name, (Color[] albedo, float[] height) tex, float strength)
    {
        Save(name, tex.albedo);
        Save(name + "_n", NormalsFrom(tex.height, strength));
    }

    static void Save(string name, Color[] pixels)
    {
        var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        t.SetPixels(pixels);
        t.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static Color[] NormalsFrom(float[] h, float strength)
    {
        var n = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = H(h, x + 1, y) - H(h, x - 1, y);
            float dy = H(h, x, y + 1) - H(h, x, y - 1);
            var v = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
            n[y * Size + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1);
        }
        return n;
    }

    static float H(float[] h, int x, int y) => h[((y + Size) % Size) * Size + (x + Size) % Size];

    static void ConfigureImporters()
    {
        foreach (var path in Directory.GetFiles(OutDir, "*.png"))
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (imp == null) continue;
            bool normal = path.EndsWith("_n.png");
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            // The painted atlas tiles were not drawn to tile; mirroring hides their seams.
            bool painted = Path.GetFileName(path).StartsWith("path_") || Path.GetFileName(path).StartsWith("ground_");
            imp.wrapMode = painted ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 1024;
            imp.sRGBTexture = !normal;
            imp.SaveAndReimport();
        }
    }

    // ---------------------------------------------------------------- atlases

    static void SliceAtlas(string file, string prefix)
    {
        if (!File.Exists(file)) { Debug.LogWarning("[TextureBaker] Missing " + file); return; }
        var atlas = new Texture2D(2, 2);
        atlas.LoadImage(File.ReadAllBytes(file));
        int half = atlas.width / 2;
        // Atlas order, reading from the top left: grassland, canyon, snow, dirt. Texture rows run upward.
        var origins = new[] { (0, half), (half, half), (0, 0), (half, 0) };
        for (int i = 0; i < 4; i++)
        {
            var (ox, oy) = origins[i];
            var albedo = new Color[Size * Size];
            var height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // A small inset keeps the quadrant edges, which can bleed into a neighbour, out of the tile.
                float u = (ox + 6 + (half - 12) * (x + 0.5f) / Size) / atlas.width;
                float v = (oy + 6 + (half - 12) * (y + 0.5f) / Size) / atlas.height;
                var c = atlas.GetPixelBilinear(u, v);
                c.a = 1;
                albedo[y * Size + x] = c;
                height[y * Size + x] = c.grayscale;
            }
            Write($"{prefix}_{Biomes[i]}", (albedo, height), 2.5f);
        }
        Object.DestroyImmediate(atlas);
    }

    // ---------------------------------------------------------------- noise

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }
    }

    /// <summary>Value noise that wraps at <paramref name="period"/> cells, so every texture tiles.</summary>
    static float Noise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int Wrap(int v) => ((v % period) + period) % period;
        float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
        float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>Fractal noise over the unit tile, in [0, 1]: <paramref name="cells"/> lattice cells across at the first octave.</summary>
    static float Fbm(float u, float v, int cells, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            int p = cells << o;
            sum += Noise(u * p, v * p, p, seed + o * 31) * amp;
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    static (Color[], float[]) Make(System.Func<float, float, (Color, float)> f)
    {
        var albedo = new Color[Size * Size];
        var height = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            var (c, h) = f((x + 0.5f) / Size, (y + 0.5f) / Size);
            c.a = 1;
            albedo[y * Size + x] = c;
            height[y * Size + x] = h;
        }
        return (albedo, height);
    }

    static Color Grey(float v) => new Color(v, v, v, 1);

    // ---------------------------------------------------------------- procedural

    /// <summary>
    /// Distances to the nearest and second-nearest feature points of tileable
    /// Worley noise, with the nearest cell's id. Cells are <paramref name="cx"/>
    /// by <paramref name="cy"/> across the tile, so they can be stretched.
    /// </summary>
    static (float f1, float f2, int id) Worley(float u, float v, int cx, int cy, int seed)
    {
        float x = u * cx, y = v * cy;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float f1 = 9, f2 = 9;
        int id = 0;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int gx = ix + dx, gy = iy + dy;
            int wx = ((gx % cx) + cx) % cx, wy = ((gy % cy) + cy) % cy;
            float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 7);
            // Measured in tile units, so stretched cells keep round-ish distances.
            float ddx = (px - x) / cx, ddy = (py - y) / cy;
            float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
            if (d < f1) { f2 = f1; f1 = d; id = wx * 977 + wy; }
            else if (d < f2) f2 = d;
        }
        return (f1, f2, id);
    }

    /// <summary>
    /// Cliff face: broken rock in wide, flat blocks (stretched Worley cells)
    /// with dark fractures between them, laid in strata, with fine grain.
    /// Grey, tinted per biome in the material.
    /// </summary>
    static (Color[], float[]) Cliff() => Make((u, v) =>
    {
        float warp = (Fbm(u, v, 4, 3, 11) - 0.5f) * 0.06f;
        var (f1, f2, id) = Worley(u + warp, v + warp * 0.5f, 4, 12, 13);
        float edge = Mathf.Clamp01((f2 - f1) / 0.018f);
        var (s1, s2, _) = Worley(u * 1f, v, 14, 22, 19);
        float fine = Mathf.Lerp(1f, Mathf.Clamp01((s2 - s1) / 0.006f), 0.5f);
        float strata = Mathf.Sin((v + warp) * Mathf.PI * 2 * 10) * 0.5f + 0.5f;
        float grain = Fbm(u, v, 32, 3, 29);
        float tone = 0.8f + 0.35f * Hash(id, 1, 31);
        // Each block bulges a little towards its middle.
        float dome = Mathf.Clamp01(f1 / 0.08f);
        float h = edge * (0.55f + 0.25f * (1 - dome)) + fine * 0.15f + grain * 0.15f;
        float shade = (0.36f + 0.2f * grain + 0.08f * strata) * tone * Mathf.Lerp(0.35f, 1f, edge) * Mathf.Lerp(0.8f, 1f, fine);
        return (Grey(shade), h);
    });

    /// <summary>Riveted plate: four panels with bevelled seams, rivets at the corners, scratches and grime. Grey, painted by the material.</summary>
    static (Color[], float[]) Metal() => Make((u, v) =>
    {
        float pu = u * 2 % 1f, pv = v * 2 % 1f;
        float edge = Mathf.Min(Mathf.Min(pu, 1 - pu), Mathf.Min(pv, 1 - pv));
        float seam = Mathf.Clamp01(edge / 0.025f);
        float rivet = 0;
        foreach (var cx in new[] { 0.08f, 0.92f })
        foreach (var cy in new[] { 0.08f, 0.5f, 0.92f })
        {
            float d = Mathf.Sqrt((pu - cx) * (pu - cx) + (pv - cy) * (pv - cy));
            rivet = Mathf.Max(rivet, Mathf.Clamp01(1 - d / 0.035f));
        }
        float grime = Fbm(u, v, 4, 5, 41);
        // Short straight scratches: a few thin strokes per cell of a coarse grid, each at its own angle.
        float scratch = 0;
        int sx = Mathf.FloorToInt(u * 8), sy = Mathf.FloorToInt(v * 8);
        for (int k = 0; k < 2; k++)
        {
            float a = Hash(sx, sy, 45 + k) * Mathf.PI;
            var c = new Vector2((sx + Hash(sx, sy, 47 + k)) / 8f, (sy + Hash(sx, sy, 49 + k)) / 8f);
            var d = new Vector2(u, v) - c;
            float along = Mathf.Abs(d.x * Mathf.Cos(a) + d.y * Mathf.Sin(a));
            float across = Mathf.Abs(-d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            if (along < 0.05f) scratch = Mathf.Max(scratch, Mathf.Clamp01(1 - across / 0.0025f) * (1 - along / 0.05f));
        }
        scratch *= 0.5f;
        float h = seam * 0.6f + Mathf.Sqrt(rivet) * 0.5f - scratch * 0.1f;
        float shade = 0.62f + 0.12f * grime - 0.28f * (1 - seam) + 0.18f * rivet + 0.1f * scratch;
        return (Grey(shade), h);
    });

    /// <summary>Weathered planks: four boards with dark gaps, long grain, the odd knot.</summary>
    static (Color[], float[]) Wood() => Make((u, v) =>
    {
        int board = Mathf.FloorToInt(v * 4);
        float bv = v * 4 % 1f;
        float gap = Mathf.Clamp01(Mathf.Min(bv, 1 - bv) / 0.04f);
        float offset = Hash(board, 0, 51);
        float grain = Fbm((u + offset) % 1f, v * 0.25f + board * 0.25f, 3, 4, 53);
        float lines = Mathf.Sin((v * 4 + grain * 1.5f) * Mathf.PI * 18) * 0.5f + 0.5f;
        var knotC = new Vector2((offset * 7 % 1f), (board + 0.5f) / 4f);
        float knot = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(u, v), knotC) / 0.035f);
        float tone = 0.75f + 0.25f * Hash(board, 1, 57);
        var c = Color.Lerp(new Color(0.36f, 0.24f, 0.15f), new Color(0.58f, 0.42f, 0.27f), 0.35f + 0.4f * lines * grain) * tone;
        c = Color.Lerp(c, new Color(0.2f, 0.13f, 0.08f), knot * 0.8f);
        c *= Mathf.Lerp(0.25f, 1f, gap);
        return (c, gap * 0.7f + lines * 0.15f - knot * 0.2f);
    });

    /// <summary>Running-bond stone blocks with mortar joints and chipped faces.</summary>
    static (Color[], float[]) Stone() => Make((u, v) =>
    {
        int row = Mathf.FloorToInt(v * 4);
        float bu = (u * 2 + (row % 2) * 0.5f) % 1f, bv = v * 4 % 1f;
        int col = Mathf.FloorToInt(u * 2 + (row % 2) * 0.5f);
        float edge = Mathf.Min(Mathf.Min(bu, 1 - bu) * 2, Mathf.Min(bv, 1 - bv));
        float joint = Mathf.Clamp01(edge / 0.06f);
        float chip = Fbm(u, v, 16, 3, 61);
        float tone = 0.8f + 0.2f * Hash(col, row, 63);
        float shade = (0.38f + 0.22f * chip) * tone * Mathf.Lerp(0.45f, 1f, joint);
        return (Grey(shade), joint * 0.7f + chip * 0.3f);
    });

    /// <summary>Hide: mottled with pores and fine wrinkles, for the orcs. Grey, tinted by vertex colour.</summary>
    static (Color[], float[]) Skin() => Make((u, v) =>
    {
        float mottle = Fbm(u, v, 6, 4, 71);
        float pores = Fbm(u, v, 64, 2, 73);
        float wrinkle = Mathf.Sin((v + Fbm(u, v, 4, 3, 79) * 0.3f) * Mathf.PI * 2 * 20) * 0.5f + 0.5f;
        float shade = 0.78f + 0.16f * mottle - 0.1f * pores + 0.04f * wrinkle;
        return (Grey(shade), mottle * 0.4f + pores * 0.4f + wrinkle * 0.2f);
    });
}
