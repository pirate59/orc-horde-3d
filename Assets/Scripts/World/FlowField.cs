using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// How the horde flows, after Orc Horde's navigation: a field over the road giving every
    /// metre-square cell its walking distance to the base, with barricades as holes and bridges and
    /// tunnels as one-way shortcuts. An orc steps towards whichever neighbouring cell is lowest, but
    /// a cell counts as further away the more orcs are already in it, so a crowd spills sideways
    /// into open road instead of filing round the inside of every bend. Neighbours that overlap
    /// push apart (<see cref="Separation"/>).
    ///
    /// Built once per map after the navmesh, and again whenever the barricades change.
    /// </summary>
    public static class FlowField
    {
        const float Cell = 1f;
        /// <summary>How close to the base counts as arrived: just inside the breach distance.</summary>
        const float GoalRadius = 3f;
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] Step = { 1, 1, 1, 1, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

        static int width, depth;
        static bool[] walkable, blocked;
        static float[] cost;
        static int[] density, head, next;
        static readonly Dictionary<int, List<(int entry, float length)>> linksInto = new Dictionary<int, List<(int, float)>>();
        static readonly Dictionary<int, Links.Info> linkAt = new Dictionary<int, Links.Info>();
        static int countedFrame = -1;
        static readonly Collider[] overlap = new Collider[8];
        static MapData map;

        public static bool Ready => cost != null;

        static int Index(int x, int z) => z * width + x;

        static bool CellOf(Vector3 p, out int i)
        {
            int x = Mathf.FloorToInt(p.x / Cell), z = Mathf.FloorToInt(p.z / Cell);
            i = x >= 0 && z >= 0 && x < width && z < depth ? Index(x, z) : -1;
            return i >= 0;
        }

        static Vector3 Centre(int i) => new Vector3((i % width + 0.5f) * Cell, 0, (i / width + 0.5f) * Cell);

        /// <summary>Lays the field over a map's navmesh. Call after the navmesh and the links are built.</summary>
        public static void Build(MapData m)
        {
            map = m;
            width = Mathf.CeilToInt(m.Width / Cell);
            depth = Mathf.CeilToInt(m.Depth / Cell);
            int n = width * depth;
            walkable = new bool[n];
            blocked = new bool[n];
            cost = new float[n];
            density = new int[n];
            head = new int[n];
            next = new int[512];
            for (int i = 0; i < n; i++)
            {
                var c = Centre(i);
                c.y = Relief.Ground(c);
                // Walkable where the navmesh is right under the cell's centre.
                walkable[i] = NavMesh.SamplePosition(c, out var hit, 1.5f, NavMesh.AllAreas) &&
                              new Vector2(hit.position.x - c.x, hit.position.z - c.z).sqrMagnitude < 0.45f * 0.45f;
            }
            linksInto.Clear();
            linkAt.Clear();
            foreach (var l in Links.All)
            {
                if (!CellOf(l.Entry, out int entry) || !CellOf(l.Exit, out int exit)) continue;
                walkable[entry] = walkable[exit] = true;
                if (!linksInto.TryGetValue(exit, out var list)) linksInto[exit] = list = new List<(int, float)>();
                list.Add((entry, Vector3.Distance(l.Entry, l.Exit)));
                linkAt[entry] = l;
            }
            Rebuild();
        }

        /// <summary>Recomputes every cell's distance to the base: a barricade went up, came down or broke.</summary>
        public static void Rebuild()
        {
            if (walkable == null) return;
            // Barricades just placed or moved must be where the overlap checks look.
            Physics.SyncTransforms();
            int n = walkable.Length;
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.PositiveInfinity;
                blocked[i] = false;
                if (!walkable[i]) continue;
                var c = Centre(i);
                c.y = Relief.Ground(c) + 1f;
                // Only barricades still standing and still in play: one just removed lingers until the frame ends.
                // Any cell a barricade touches, plus an orc's width round it, so the grid never leaves a
                // gap at a corner that the navmesh (carved solid by the barricade) doesn't have.
                int hits = Physics.OverlapBoxNonAlloc(c, new Vector3(Cell * 0.5f + 0.45f, 0.9f, Cell * 0.5f + 0.45f), overlap, Quaternion.identity, Layers.WallMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits && !blocked[i]; h++)
                {
                    var b = overlap[h].GetComponentInParent<Barricade>();
                    blocked[i] = b != null && b.Standing && Barricade.All.Contains(b);
                }
            }

            // Dijkstra out from the base, on a binary heap.
            var heap = new List<(float cost, int cell)>();
            void Push(float c, int i)
            {
                heap.Add((c, i));
                int k = heap.Count - 1;
                while (k > 0)
                {
                    int p = (k - 1) / 2;
                    if (heap[p].cost <= heap[k].cost) break;
                    (heap[p], heap[k]) = (heap[k], heap[p]);
                    k = p;
                }
            }
            (float cost, int cell) Pop()
            {
                var top = heap[0];
                heap[0] = heap[heap.Count - 1];
                heap.RemoveAt(heap.Count - 1);
                int k = 0;
                while (true)
                {
                    int l = k * 2 + 1, r = l + 1, s = k;
                    if (l < heap.Count && heap[l].cost < heap[s].cost) s = l;
                    if (r < heap.Count && heap[r].cost < heap[s].cost) s = r;
                    if (s == k) break;
                    (heap[s], heap[k]) = (heap[k], heap[s]);
                    k = s;
                }
                return top;
            }

            for (int i = 0; i < n; i++)
            {
                if (!walkable[i] || blocked[i]) continue;
                var c = Centre(i);
                if (new Vector2(c.x - map.Base.x, c.z - map.Base.z).magnitude <= GoalRadius) { cost[i] = 0; Push(0, i); }
            }
            while (heap.Count > 0)
            {
                var (c, i) = Pop();
                if (c > cost[i]) continue;
                int x = i % width, z = i / width;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx[d], nz = z + Dz[d];
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
                    int j = Index(nx, nz);
                    if (!Open(j)) continue;
                    // No cutting a corner past rock.
                    if (d >= 4 && (!Open(Index(nx, z)) || !Open(Index(x, nz)))) continue;
                    float nc = c + Step[d] * Cell;
                    if (nc < cost[j]) { cost[j] = nc; Push(nc, j); }
                }
                // A bridge or tunnel whose far end is here: its near end is this far plus the crossing.
                if (linksInto.TryGetValue(i, out var into))
                    foreach (var (entry, length) in into)
                    {
                        float nc = c + length;
                        if (Open(entry) && nc < cost[entry]) { cost[entry] = nc; Push(nc, entry); }
                    }
            }
        }

        static bool Open(int i) => walkable[i] && !blocked[i];

        /// <summary>Whether the base can be walked to from here round every barricade.</summary>
        public static bool Reaches(Vector3 p) => cost != null && CellOf(p, out int i) && !float.IsInfinity(cost[i]);

        /// <summary>Walking distance to the base from here, for "first" targeting.</summary>
        public static float Distance(Vector3 p) => cost != null && CellOf(p, out int i) ? cost[i] : float.PositiveInfinity;

        /// <summary>Counts the horde into cells, once a frame: for crowding and for separation.</summary>
        static void Count()
        {
            if (countedFrame == Time.frameCount) return;
            countedFrame = Time.frameCount;
            System.Array.Clear(density, 0, density.Length);
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            if (next.Length < Enemy.All.Count) next = new int[Enemy.All.Count * 2];
            for (int k = 0; k < Enemy.All.Count; k++)
            {
                if (!CellOf(Enemy.All[k].transform.position, out int c)) continue;
                density[c]++;
                next[k] = head[c];
                head[c] = k;
            }
        }

        /// <summary>
        /// Where an orc here should head next: the centre of the neighbouring cell with the lowest
        /// distance to the base, each orc already in a cell adding <paramref name="crowding"/> metres
        /// to it. If the best way on is through a bridge or tunnel from this cell, the link comes back
        /// too. False where the base can't be reached.
        /// </summary>
        public static bool Steer(Vector3 p, float crowding, out Vector3 toward, out Links.Info link)
        {
            toward = p;
            link = null;
            if (cost == null || !CellOf(p, out int i) || float.IsInfinity(cost[i])) return false;
            Count();
            if (linkAt.TryGetValue(i, out var l))
            {
                // Is the crossing this cell's best way on?
                CellOf(l.Exit, out int exit);
                if (Mathf.Abs(cost[exit] + Vector3.Distance(l.Entry, l.Exit) - cost[i]) < 0.01f) { link = l; toward = l.Entry; return true; }
            }
            int x = i % width, z = i / width;
            float best = float.PositiveInfinity;
            int bestCell = -1;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], nz = z + Dz[d];
                if (nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
                int j = Index(nx, nz);
                if (!Open(j) || float.IsInfinity(cost[j])) continue;
                if (d >= 4 && (!Open(Index(nx, z)) || !Open(Index(x, nz)))) continue;
                float score = cost[j] + density[j] * crowding;
                if (score < best) { best = score; bestCell = j; }
            }
            if (bestCell < 0) return false;
            toward = Centre(bestCell);
            return true;
        }

        /// <summary>The push away from orcs this one overlaps, deeper overlaps pushing harder.</summary>
        public static Vector3 Separation(Enemy self, int most = 14)
        {
            if (cost == null || !CellOf(self.transform.position, out int i)) return Vector3.zero;
            Count();
            var p = self.transform.position;
            var push = Vector3.zero;
            int x = i % width, z = i / width, seen = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
                for (int k = head[Index(nx, nz)]; k >= 0 && seen < most; k = next[k])
                {
                    if (k >= Enemy.All.Count) break;
                    var other = Enemy.All[k];
                    if (other == self) continue;
                    seen++;
                    var d = p - other.transform.position;
                    d.y = 0;
                    float reach = self.Def.Radius + other.Def.Radius + 0.15f, dist = d.magnitude;
                    if (dist >= reach || dist < 1e-4f) continue;
                    push += d / dist * ((reach - dist) / reach);
                }
            }
            return push;
        }

        public static void Clear()
        {
            cost = null;
            walkable = null;
            linksInto.Clear();
            linkAt.Clear();
        }
    }
}
