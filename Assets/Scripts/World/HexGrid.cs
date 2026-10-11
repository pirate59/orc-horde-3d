using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Orc Horde's hex cells (src/content/hexCells.ts): flat-topped, in columns,
    /// odd columns half a cell lower, cell (0, 0) centred on the world origin,
    /// rows counting south. One cell is one tower's footprint.
    /// </summary>
    public sealed class HexGrid
    {
        public const char Rock = '#', TowerGround = 'T', Path = '.', WallPath = ',';

        readonly MapData map;
        readonly float apothemU, radiusU, columnStepU, rowStepU;

        /// <summary>Centre to corner, in metres.</summary>
        public float Radius => U.M(radiusU);
        public float Apothem => U.M(apothemU);

        public HexGrid(MapData map)
        {
            this.map = map;
            apothemU = map.hexApothem;
            radiusU = apothemU * 2f / Mathf.Sqrt(3f);
            columnStepU = radiusU * 1.5f;
            rowStepU = apothemU * 2f;
        }

        public int Columns => map.columns;
        public int FirstRow => map.firstRow;
        public int LastRow => map.firstRow + map.rows - 1;

        public char Kind(int column, int row)
        {
            int r = row - map.firstRow;
            if (column < 0 || column >= map.columns || r < 0 || r >= map.rows) return Rock;
            return map.cells[r * map.columns + column];
        }

        public char Kind(Vector2Int cell) => Kind(cell.x, cell.y);

        public static bool IsPath(char kind) => kind == Path || kind == WallPath;

        public Vector3 Center(int column, int row) =>
            map.World(columnStepU * column, rowStepU * (row + (column & 1) / 2f));

        public Vector3 Center(Vector2Int cell) => Center(cell.x, cell.y);

        /// <summary>The cell a world point falls in: axial coordinates for flat-topped hexes, then cube rounding.</summary>
        public Vector2Int CellAt(Vector3 world)
        {
            float x = world.x / U.Scale, y = map.worldHeight - world.z / U.Scale;
            float q = 2f / 3f * x / radiusU;
            float r = (-x / 3f + Mathf.Sqrt(3f) / 3f * y) / radiusU;
            float s = -q - r;
            int rq = Mathf.RoundToInt(q), rr = Mathf.RoundToInt(r), rs = Mathf.RoundToInt(s);
            float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new Vector2Int(rq, rr + (rq - (rq & 1)) / 2);
        }

        /// <summary>
        /// Corner offsets in world space, anticlockwise from the east point seen
        /// from above: east, north-east, north-west, west, south-west, south-east.
        /// </summary>
        public Vector3[] CornerOffsets()
        {
            float r = Radius, a = Apothem;
            return new[]
            {
                new Vector3(r, 0, 0), new Vector3(r / 2, 0, a), new Vector3(-r / 2, 0, a),
                new Vector3(-r, 0, 0), new Vector3(-r / 2, 0, -a), new Vector3(r / 2, 0, -a),
            };
        }

        /// <summary>
        /// The three straight lines through a cell's neighbours: north-south,
        /// north-east to south-west, north-west to south-east. A barricade runs
        /// along one of them, as in Orc Horde. Each is given by one of its two
        /// opposite sides.
        /// </summary>
        public static readonly int[] AxisSides = { 1, 0, 2 };

        /// <summary>The cell <paramref name="steps"/> cells from this one along an axis; negative steps go the other way.</summary>
        public static Vector2Int Step(Vector2Int cell, int axis, int steps)
        {
            int side = steps >= 0 ? AxisSides[axis] : (AxisSides[axis] + 3) % 6;
            for (int i = 0; i < Mathf.Abs(steps); i++) cell = Across(cell.x, cell.y, side);
            return cell;
        }

        /// <summary>World-space unit vector along an axis, in its positive direction.</summary>
        public Vector3 AxisDirection(int axis)
        {
            var from = new Vector2Int(2, 2);
            var d = Center(Step(from, axis, 1)) - Center(from);
            d.y = 0;
            return d.normalized;
        }

        /// <summary>The neighbour across the side from corner <paramref name="side"/> to the next, matching <see cref="CornerOffsets"/>.</summary>
        public static Vector2Int Across(int column, int row, int side)
        {
            int upper = (column & 1) == 0 ? row - 1 : row; // the north-side row of the neighbouring columns
            switch (side)
            {
                case 0: return new Vector2Int(column + 1, upper);
                case 1: return new Vector2Int(column, row - 1);
                case 2: return new Vector2Int(column - 1, upper);
                case 3: return new Vector2Int(column - 1, upper + 1);
                case 4: return new Vector2Int(column, row + 1);
                default: return new Vector2Int(column + 1, upper + 1);
            }
        }
    }
}
