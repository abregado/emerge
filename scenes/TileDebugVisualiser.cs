using Godot;
using System.Collections.Generic;

public partial class TileDebugVisualiser : Node3D
{
    private static readonly Color ColorFloor    = new(0.53f, 0.53f, 0.53f);
    private static readonly Color ColorWall     = new(0.27f, 0.07f, 0.07f);
    private static readonly Color ColorSand     = new(0.78f, 0.66f, 0.44f);
    private static readonly Color ColorWater    = new(0.13f, 0.33f, 0.67f);
    private static readonly Color ColorWalkable = new(0.60f, 0.70f, 0.60f);

    public void Populate(Grid<EmergeTile> grid)
    {
        GD.Print($"TileDebugVisualiser.Populate: {grid.Width}x{grid.Height}");

        // Group tile positions by display colour
        var groups = new Dictionary<Color, List<(int x, int z)>>();
        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Height; z++)
        {
            var color = TileColor(grid.Get(x, z)!.BakedTile);
            if (color == null) continue;
            if (!groups.TryGetValue(color.Value, out var list))
                groups[color.Value] = list = new();
            list.Add((x, z));
        }

        GD.Print($"  {groups.Count} colour groups, {CountAll(groups)} tiles");

        float tileSize = grid.CellSize * 0.94f;

        // One MultiMeshInstance3D per colour — single draw call per group
        foreach (var (color, positions) in groups)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode    = BaseMaterial3D.CullModeEnum.Disabled,
            };

            // PlaneMesh lies flat in XZ (faces +Y) — no rotation needed
            var plane = new PlaneMesh { Size = new Vector2(tileSize, tileSize) };
            plane.Material = mat;

            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                InstanceCount   = positions.Count,
                Mesh            = plane,
            };

            for (int i = 0; i < positions.Count; i++)
            {
                var (x, z) = positions[i];
                var c = grid.GetWorldCenter(x, z);
                mm.SetInstanceTransform(i,
                    new Transform3D(Basis.Identity, new Vector3(c.X, 0.01f, c.Z)));
            }

            AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }

        GD.Print("  TileDebugVisualiser done");
    }

    private static int CountAll(Dictionary<Color, List<(int, int)>> g)
    {
        int n = 0;
        foreach (var v in g.Values) n += v.Count;
        return n;
    }

    private static Color? TileColor(G.BakedTile bt) => bt switch
    {
        G.BakedTile.RaycastStaticPlaceable
            or G.BakedTile.Placeable_Static
            or G.BakedTile.Plateable_Static => ColorFloor,
        G.BakedTile.RaycastStaticBlock
            or G.BakedTile.Block           => ColorWall,
        G.BakedTile.Sand                   => ColorSand,
        G.BakedTile.Water                  => ColorWater,
        G.BakedTile.Walkable_Static        => ColorWalkable,
        _                                  => null
    };
}
