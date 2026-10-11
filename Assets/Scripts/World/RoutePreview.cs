using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// Between rounds, the way the horde will walk: a line from every gate the
    /// next round uses to the base, with markers running along it, recomputed as
    /// barricades go down. A road sealed by barricades turns red where it ends;
    /// orcs will break through there.
    /// </summary>
    public sealed class RoutePreview : MonoBehaviour
    {
        public static RoutePreview I { get; private set; }

        const float Refresh = 0.4f, Lift = 0.15f, MarkerSpacing = 7f, MarkerSpeed = 6f;
        static readonly Color Open = new Color(1f, 0.7f, 0.28f, 0.45f), Sealed = new Color(1f, 0.25f, 0.25f, 0.8f);

        public sealed class Route
        {
            public MapPoint Gate;
            public float Share, Length;
            public bool Sealed;
            internal readonly List<Vector3> Points = new List<Vector3>();
            public List<Vector3> PointsCopy() => new List<Vector3>(Points);
            internal LineRenderer Line;
            internal readonly List<Transform> Markers = new List<Transform>();
        }

        public readonly List<Route> Routes = new List<Route>();
        public bool Visible { get; private set; } = true;

        float nextRefresh, phase;
        NavMeshPath path;

        void Awake()
        {
            I = this;
            path = new NavMeshPath(); // Unity refuses this in a field initializer.
        }

        void Update()
        {
            var gm = GameManager.I;
            if (Input.GetKeyDown(KeyCode.V) && gm.Phase == Phase.Build && !gm.Paused) Visible = !Visible;
            bool show = gm.Phase == Phase.Build && Visible;
            if (Time.unscaledTime >= nextRefresh && gm.Phase == Phase.Build)
            {
                nextRefresh = Time.unscaledTime + Refresh;
                Recompute(gm);
            }
            phase += Time.unscaledDeltaTime * MarkerSpeed;
            foreach (var r in Routes)
            {
                if (r.Line != null) r.Line.enabled = show;
                PlaceMarkers(r, show);
            }
        }

        /// <summary>The gates this round sends orcs from, with each one's share of the spawns.</summary>
        static Dictionary<string, float> GateShares(RoundData round)
        {
            var shares = new Dictionary<string, float>();
            float total = 0;
            foreach (var s in round.spawnRules) { shares[s.spawn] = (shares.TryGetValue(s.spawn, out var w) ? w : 0) + s.weight; total += s.weight; }
            foreach (var k in new List<string>(shares.Keys)) shares[k] /= Mathf.Max(total, 1f);
            foreach (var b in round.bosses) if (!shares.ContainsKey(b.spawn)) shares[b.spawn] = 0;
            return shares;
        }

        void Recompute(GameManager gm)
        {
            var shares = GateShares(gm.Round);
            // One route per gate in use, reusing lines from the last pass.
            while (Routes.Count < shares.Count) Routes.Add(new Route());
            for (int i = shares.Count; i < Routes.Count; i++) Hide(Routes[i]);
            if (Routes.Count > shares.Count) Routes.RemoveRange(shares.Count, Routes.Count - shares.Count);

            int n = 0;
            foreach (var kv in shares)
            {
                var r = Routes[n++];
                r.Gate = gm.Map.Spawn(kv.Key);
                r.Share = kv.Value;
                r.Points.Clear();
                var from = gm.Map.World(r.Gate.x, r.Gate.y);
                if (NavMesh.SamplePosition(from, out var start, 6f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position, gm.Map.Base, NavMesh.AllAreas, path))
                {
                    // Corners only mark turns, so a straight run between them would cut
                    // through a rise in the road. Lay the line along the land in short steps.
                    var corners = path.corners;
                    for (int i = 0; i < corners.Length; i++)
                    {
                        if (i > 0)
                        {
                            int steps = Mathf.CeilToInt(Vector3.Distance(corners[i - 1], corners[i]) / 2f);
                            for (int k = 1; k < steps; k++)
                            {
                                var p = Vector3.Lerp(corners[i - 1], corners[i], k / (float)steps);
                                p.y = Mathf.Max(p.y, Relief.Ground(p));
                                r.Points.Add(p + Vector3.up * Lift);
                            }
                        }
                        r.Points.Add(corners[i] + Vector3.up * Lift);
                    }
                    r.Sealed = path.status != NavMeshPathStatus.PathComplete;
                }
                else r.Sealed = true;

                r.Length = 0;
                for (int i = 1; i < r.Points.Count; i++) r.Length += Vector3.Distance(r.Points[i - 1], r.Points[i]);

                if (r.Line == null)
                {
                    var go = new GameObject("Route");
                    go.transform.SetParent(transform, false);
                    r.Line = Fx.NewLine(go.transform, Open, 0.12f);
                }
                r.Line.startColor = r.Line.endColor = r.Sealed ? Sealed : Open;
                r.Line.positionCount = r.Points.Count;
                r.Line.SetPositions(r.Points.ToArray());
            }
        }

        /// <summary>Small glowing markers travelling the route toward the base, so its direction reads at a glance.</summary>
        void PlaceMarkers(Route r, bool show)
        {
            int want = show && r.Points.Count > 1 ? Mathf.Min(60, Mathf.CeilToInt(r.Length / MarkerSpacing)) : 0;
            while (r.Markers.Count < want)
            {
                var m = Shapes.Prim(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.22f, Mats.Unlit(Color.white));
                m.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.Markers.Add(m.transform);
            }
            for (int i = 0; i < r.Markers.Count; i++)
            {
                bool on = i < want;
                r.Markers[i].gameObject.SetActive(on);
                if (!on) continue;
                float d = (phase + i * MarkerSpacing) % Mathf.Max(r.Length, 0.01f);
                r.Markers[i].position = PointAt(r.Points, d);
                r.Markers[i].GetComponent<Renderer>().sharedMaterial = Mats.Unlit(r.Sealed ? Sealed : new Color(1f, 0.85f, 0.5f, 0.7f));
            }
        }

        static Vector3 PointAt(List<Vector3> pts, float distance)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (distance <= seg) return Vector3.Lerp(pts[i - 1], pts[i], seg > 0 ? distance / seg : 0);
                distance -= seg;
            }
            return pts[pts.Count - 1];
        }

        static void Hide(Route r)
        {
            if (r.Line != null) Destroy(r.Line.gameObject);
            foreach (var m in r.Markers) Destroy(m.gameObject);
        }
    }
}
