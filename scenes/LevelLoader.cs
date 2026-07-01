using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class LevelLoader : Node3D
{
    [Export] private NodePath LevelRootPath;

    private Node3D _levelRoot;
    public Grid<EmergeTile> Grid { get; private set; }
    private List<Body> _allBodies = new();

    public override void _Ready()
    {
        _levelRoot = GetNode<Node3D>(LevelRootPath);
    }

    public void LoadLevel(string scenarioName)
    {
        _allBodies.Clear();
        var text = FileAccess.GetFileAsString("res://levels/emerge.ldtk");
        var root = JsonNode.Parse(text)!.AsObject();
        var level = FindLevel(root, scenarioName);
        BuildGrid(level);
        SpawnBodies(level);
        VisualiseDebug();
        RunPasses();
    }

    private JsonObject FindLevel(JsonObject root, string name)
    {
        foreach (var node in root["levels"]!.AsArray())
        {
            var level = node!.AsObject();
            if (level["identifier"]!.GetValue<string>() == name) return level;
        }
        throw new Exception($"Level '{name}' not found in LDTK file");
    }

    private void BuildGrid(JsonObject level)
    {
        float offsetX = 0, offsetZ = 0, cellSize = 1;
        foreach (var node in level["fieldInstances"]!.AsArray())
        {
            var fi = node!.AsObject();
            switch (fi["__identifier"]!.GetValue<string>())
            {
                case "world_offset_x": offsetX = fi["__value"]!.GetValue<float>(); break;
                case "world_offset_z": offsetZ = fi["__value"]!.GetValue<float>(); break;
                case "cell_size":      cellSize = fi["__value"]!.GetValue<float>(); break;
            }
        }

        JsonObject tilesLayer = null;
        foreach (var node in level["layerInstances"]!.AsArray())
        {
            var layer = node!.AsObject();
            if (layer["__identifier"]!.GetValue<string>() == "Tiles")
            { tilesLayer = layer; break; }
        }

        int cWid = tilesLayer!["__cWid"]!.GetValue<int>();
        int cHei = tilesLayer["__cHei"]!.GetValue<int>();
        Grid = new Grid<EmergeTile>(cWid, cHei, cellSize, new Vector3(offsetX, 0, offsetZ));

        for (int x = 0; x < cWid; x++)
            for (int z = 0; z < cHei; z++)
                Grid.Set(x, z, new EmergeTile(x, z));

        var csv = tilesLayer["intGridCsv"]!.AsArray();
        for (int cy = 0; cy < cHei; cy++)
        {
            int z = (cHei - 1) - cy;
            for (int cx = 0; cx < cWid; cx++)
            {
                int val = csv[cy * cWid + cx]!.GetValue<int>();
                G.BakedTile bt = val switch
                {
                    1 => G.BakedTile.RaycastStaticPlaceable,
                    2 => G.BakedTile.RaycastStaticBlock,
                    3 => G.BakedTile.Sand,
                    4 => G.BakedTile.Water,
                    _ => G.BakedTile.Null
                };
                Grid.Get(cx, z)!.SetBakedTile(bt);
            }
        }

        GD.Print($"Grid built: {cWid}x{cHei}  offset=({offsetX},{offsetZ})  cell={cellSize}");
    }

    private void SpawnBodies(JsonObject level)
    {
        JsonObject bodiesLayer = null;
        int cHei = 0;
        foreach (var node in level["layerInstances"]!.AsArray())
        {
            var layer = node!.AsObject();
            if (layer["__identifier"]!.GetValue<string>() == "Bodies")
            { bodiesLayer = layer; cHei = layer["__cHei"]!.GetValue<int>(); break; }
        }
        if (bodiesLayer == null) return;

        int i = 0;
        foreach (var node in bodiesLayer["entityInstances"]!.AsArray())
        {
            var entity = node!.AsObject();
            int cx = entity["__cx"]!.GetValue<int>();
            int cy = entity["__cy"]!.GetValue<int>();
            int z = (cHei - 1) - cy;

            string bodyType = "Unknown";
            int rotDeg = 0;
            string triggerGroups = "";
            int triggerWeightNeeded = 1;
            bool reverseState = false;

            foreach (var fNode in entity["fieldInstances"]!.AsArray())
            {
                var fi = fNode!.AsObject();
                var val = fi["__value"];
                if (val == null) continue;
                switch (fi["__identifier"]!.GetValue<string>())
                {
                    case "body_type":             bodyType            = val.GetValue<string>(); break;
                    case "rotation":              rotDeg              = val.GetValue<int>();    break;
                    case "trigger_groups":        triggerGroups       = val.GetValue<string>(); break;
                    case "trigger_weight_needed": triggerWeightNeeded = val.GetValue<int>();    break;
                    case "reverse_state":         reverseState        = val.GetValue<bool>();   break;
                }
            }

            var body = MakeBody(bodyType, i++);

            // Apply trigger configuration before Init so AfterGridPopulated can use it
            if (body is LaserTarget lt && !string.IsNullOrEmpty(triggerGroups))
                lt.GroupId = triggerGroups;
            if (body is Door door)
            {
                if (!string.IsNullOrEmpty(triggerGroups))
                    door.TriggerGroups = triggerGroups.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
                door.TriggerWeightNeeded = triggerWeightNeeded;
                door.ReverseState = reverseState;
            }
            if (body is SignalCable sc && !string.IsNullOrEmpty(triggerGroups))
                sc.TriggerGroups = triggerGroups.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            if (body is TriggerIntermediate ti && !string.IsNullOrEmpty(triggerGroups))
                ti.InputGroup = triggerGroups;

            _levelRoot.AddChild(body);
            body.GlobalPosition = Grid.GetWorldCenter(cx, z);
            // Unity uses CW Y rotation; Godot uses CCW → flip: godotY = 180 + unityY
            body.RotationDegrees = new Vector3(0, 180f + rotDeg, 0);
            _allBodies.Add(body);
        }
    }

    // Hardcoded fallback paths — used when a type isn't registered in Res.BodyScenes
    private static readonly System.Collections.Generic.Dictionary<string, string> BuiltinScenes = new()
    {
        ["EmitterAlien"]        = "res://scenes/laser/EmitterAlien.tscn",
        ["LaserTarget"]         = "res://scenes/laser/LaserTarget.tscn",
        ["Reflector"]           = "res://scenes/laser/Reflector.tscn",
        ["Door"]                = "res://scenes/Door.tscn",
        ["SignalCable"]         = "res://scenes/SignalCable.tscn",
        ["TriggerIntermediate"] = "res://scenes/TriggerIntermediate.tscn",
    };

    private Body MakeBody(string type, int index)
    {
        var name = $"{type}_{index}";

        // Prefer Res.BodyScenes (editor-assigned, allows override)
        var scenes = Main.Res?.BodyScenes;
        if (scenes != null && scenes.TryGetValue(type, out var regScene) && regScene != null)
        {
            var body = regScene.Instantiate<Body>();
            body.Name = name;
            return body;
        }

        // Fallback: load by hardcoded path
        if (BuiltinScenes.TryGetValue(type, out var path))
        {
            var packed = GD.Load<PackedScene>(path);
            if (packed != null)
            {
                var body = packed.Instantiate<Body>();
                body.Name = name;
                return body;
            }
        }

        return new PlaceholderBody { Name = name };
    }

    private void VisualiseDebug()
    {
        var vis = _levelRoot.GetNodeOrNull<TileDebugVisualiser>("TileDebugVisualiser");
        if (vis == null)
        {
            GD.Print("TileDebugVisualiser not found in scene — creating at runtime");
            vis = new TileDebugVisualiser { Name = "TileDebugVisualiser" };
            _levelRoot.AddChild(vis);
        }
        vis.Populate(Grid);
    }

    private void RunPasses()
    {
        GD.Print("--- Pass 1: Init ---");
        foreach (var b in _allBodies) b.Init(Grid);
        GD.Print("--- Pass 2: ActivateOnGrid ---");
        foreach (var b in _allBodies) b.ActivateOnGrid();
        GD.Print("--- Pass 3: AfterGridPopulated ---");
        foreach (var b in _allBodies) b.AfterGridPopulated();

        foreach (var b in _allBodies)
            System.Diagnostics.Debug.Assert(b.OccupiedTile != null,
                $"{b.Name} has no tile after ActivateOnGrid");

        GD.Print($"Level ready: {_allBodies.Count} bodies, {Grid.Width}x{Grid.Height} grid");
    }
}
