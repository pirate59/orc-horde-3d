using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The lie of the land. Orc Horde's maps are flat. Here a smooth height field
    /// rolls under each one, so the road climbs and dips, and the tower ground
    /// beside it steps up in terraces.
    ///
    /// The field fades to level ground around everything placed at a fixed
    /// height: gates, the base, and the bridges and tunnels the horde crosses on
    /// scripted paths. Those keep working exactly as on a flat map.
    /// </summary>
    public static class Relief
    {
        /// <summary>Tallest rise of the road, in metres, on a map with full relief.</summary>
        const float Amplitude = 6.5f;
        /// <summary>Metres across one swell of the land: long enough that no slope tops about 25°.</summary>
        const float Wavelength = 64f;
        /// <summary>Level ground kept around fixed points, then the distance over which the land rises out of it.</summary>
        const float Flat = 4f, Fade = 24f;
        /// <summary>Tower ground stands in steps of this height: low enough to walk up one, high enough that orcs never climb it.</summary>
        public const float Terrace = 0.4f;
        /// <summary>The least tower ground stands above the road beside it.</summary>
        const float AboveRoad = 0.35f;

        static MapData map;
        static HexGrid grid;
        static Vector2[] anchors = new Vector2[0];
        static Rect[] spans = new Rect[0];
        static Vector2 seed;
        static float amplitude;
        static Vector3[] corners;

        /// <summary>Sets up the field for a map. Each map gets its own landscape, the same every time.</summary>
        public static void Init(MapData m, HexGrid g)
        {
            map = m;
            grid = g;
            corners = g.CornerOffsets();
            int hash = m.id.GetHashCode() & 0xffff;
            seed = new Vector2(hash % 97 * 13.1f, hash / 97 * 7.7f);
            amplitude = Amplitude;

            var points = new System.Collections.Generic.List<Vector2>();
            foreach (var s in m.spawns) points.Add(Plan(m.World(s.x, s.y)));
            points.Add(Plan(m.Base));
            anchors = points.ToArray();
            var links = m.links ?? new MapLink[0];
            spans = new Rect[links.Length];
            for (int i = 0; i < links.Length; i++) spans[i] = Links.SpanRect(m, links[i]);
        }

        static Vector2 Plan(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>0 at a fixed point, rising to 1 clear of every one.</summary>
        static float Freedom(Vector2 p)
        {
            float near = float.MaxValue;
            foreach (var a in anchors) near = Mathf.Min(near, Vector2.Distance(p, a));
            foreach (var r in spans)
            {
                var grown = new Rect(r.xMin - 2f, r.yMin - 2f, r.width + 4f, r.height + 4f);
                float dx = Mathf.Max(grown.xMin - p.x, 0, p.x - grown.xMax);
                float dz = Mathf.Max(grown.yMin - p.y, 0, p.y - grown.yMax);
                near = Mathf.Min(near, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return Mathf.SmoothStep(0, 1, (near - Flat) / Fade);
        }

        /// <summary>The road's height at a point: the land itself.</summary>
        public static float Ground(float x, float z)
        {
            if (map == null) return 0;
            var p = new Vector2(x, z);
            // Two octaves, the second a gentle ripple on the first.
            float n = Mathf.PerlinNoise(seed.x + x / Wavelength, seed.y + z / Wavelength) * 0.8f
                    + Mathf.PerlinNoise(seed.y + x / (Wavelength * 0.45f), seed.x + z / (Wavelength * 0.45f)) * 0.2f;
            float rise = Mathf.Clamp01((n - 0.3f) / 0.55f);
            return amplitude * rise * rise * (3 - 2 * rise) * Freedom(p);
        }

        public static float Ground(Vector3 p) => Ground(p.x, p.z);

        /// <summary>A point on the road, standing on the land.</summary>
        public static Vector3 OnGround(Vector3 p) => new Vector3(p.x, Ground(p.x, p.z), p.z);

        /// <summary>The highest the land comes anywhere on a cell: its centre or a corner.</summary>
        public static float CellPeak(Vector2Int cell)
        {
            var c = grid.Center(cell);
            float peak = Ground(c);
            foreach (var k in corners) peak = Mathf.Max(peak, Ground(c + k));
            return peak;
        }

        /// <summary>
        /// The top of a piece of tower ground: clear of the road beside it, then
        /// lifted by a slow terrace noise so ledges stand out from the slope.
        /// Rounded up to whole terraces, so neighbouring ledges differ by whole steps.
        /// </summary>
        public static float TowerGround(Vector2Int cell)
        {
            float lowest = CellPeak(cell) + AboveRoad;
            if (map == null || amplitude <= 0) return lowest;
            var c = grid.Center(cell);
            float lift = Mathf.PerlinNoise(seed.y + c.x / 19f, seed.x + c.z / 19f);
            int steps = Mathf.FloorToInt(Mathf.Clamp01((lift - 0.3f) / 0.5f) * 5.99f * Freedom(Plan(c)));
            return Mathf.Ceil(lowest / Terrace - 0.001f) * Terrace + steps * Terrace;
        }

        /// <summary>Range gained per metre a tower stands above the road it covers, and the limits either way.</summary>
        const float RangePerMetre = 0.12f, MostRange = 0.3f, LeastRange = -0.2f;
        /// <summary>Height above the road that counts as level: an average ledge, so height trades reach rather than adding it.</summary>
        const float LevelLedge = 1.3f;

        /// <summary>
        /// How far a tower on this cell stands above the road within its reach:
        /// its ledge top less the mean height of the road cells it covers.
        /// Negative in a dip, with the road climbing away on every side.
        /// </summary>
        public static float HeightAbove(Vector2Int cell, float range)
        {
            if (grid == null) return LevelLedge;
            var c = grid.Center(cell);
            float sum = 0;
            int n = 0;
            int reach = Mathf.CeilToInt(range / (grid.Apothem * 1.5f)) + 1;
            for (int dc = -reach; dc <= reach; dc++)
            for (int dr = -reach; dr <= reach; dr++)
            {
                var other = new Vector2Int(cell.x + dc, cell.y + dr);
                if (!HexGrid.IsPath(grid.Kind(other))) continue;
                var p = grid.Center(other);
                if ((p - c).sqrMagnitude > range * range) continue;
                sum += Ground(p);
                n++;
            }
            return n == 0 ? LevelLedge : TowerGround(cell) - sum / n;
        }

        /// <summary>
        /// The range multiplier height gives a tower here: more from high
        /// ground, less from low, level on an ordinary ledge.
        /// </summary>
        public static float RangeFactor(Vector2Int cell, float range) =>
            1f + Mathf.Clamp((HeightAbove(cell, range) - LevelLedge) * RangePerMetre, LeastRange, MostRange);

        /// <summary>Where a tower on this cell stands.</summary>
        public static Vector3 TowerSpot(Vector2Int cell) => With(grid.Center(cell), TowerGround(cell));

        /// <summary>Where something standing on the road at this cell's centre rests.</summary>
        public static Vector3 RoadSpot(Vector2Int cell) => OnGround(grid.Center(cell));

        static Vector3 With(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    }
}
