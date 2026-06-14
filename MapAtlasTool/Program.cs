using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SoulsFormats;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

AtlasOptions options = AtlasOptions.Parse(args);
OodleLoader.TryConfigure(options.OodlePath, options.SourceRoot);
string mapId = options.MapId;
string msbPath = options.MsbPath ?? FindMsb(options.SourceRoot, mapId);

if (!File.Exists(msbPath))
{
    Console.Error.WriteLine($"MSB not found for {mapId}.");
    Console.Error.WriteLine($"Tried source root: {Path.GetFullPath(options.SourceRoot)}");
    Console.Error.WriteLine("Pass an explicit path with --msb <path>.");
    return 2;
}

Directory.CreateDirectory(options.OutputDir);

MSBS msb;
try
{
    msb = MSBS.Read(msbPath);
}
catch (DllNotFoundException ex) when (ex.Message.Contains("oo2core", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unable to load oo2core_6_win64.dll. Sekiro MSB/DCX files need Oodle decompression.");
    Console.Error.WriteLine("Pass the DLL or its folder with --oodle <path>, for example:");
    Console.Error.WriteLine("  dotnet run --project MapAtlasTool -- --oodle \"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Sekiro\\oo2core_6_win64.dll\"");
    return 3;
}
AtlasDocument atlas = AtlasBuilder.Build(mapId, msbPath, msb, options.ImageWidth, options.ImageHeight);

JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

string atlasPath = Path.Combine(options.OutputDir, $"{mapId}.atlas.json");
string previewPath = Path.Combine(options.OutputDir, $"{mapId}.preview.html");

if (!options.OutlineOnly)
{
    File.WriteAllText(atlasPath, JsonSerializer.Serialize(atlas, jsonOptions), Encoding.UTF8);
    File.WriteAllText(previewPath, PreviewWriter.Write(atlas, options.OutputDir), Encoding.UTF8);
}

if (!string.IsNullOrWhiteSpace(options.PieceName))
{
    PieceOutlineDocument outline = PieceOutlineBuilder.Build(options.SourceRoot, mapId, msb, atlas.Layers[0], options.PieceName);
    string outlinePath = Path.Combine(options.OutputDir, $"{options.PieceName}.outline.json");
    string outlinePreviewPath = Path.Combine(options.OutputDir, $"{options.PieceName}.outline.preview.html");
    File.WriteAllText(outlinePath, JsonSerializer.Serialize(outline, jsonOptions), Encoding.UTF8);
    File.WriteAllText(outlinePreviewPath, PieceOutlinePreviewWriter.Write(outline), Encoding.UTF8);
    Console.WriteLine($"Wrote: {Path.GetFullPath(outlinePath)}");
    Console.WriteLine($"Wrote: {Path.GetFullPath(outlinePreviewPath)}");
}

Console.WriteLine($"Read: {Path.GetFullPath(msbPath)}");
if (!options.OutlineOnly)
{
    Console.WriteLine($"Wrote: {Path.GetFullPath(atlasPath)}");
    Console.WriteLine($"Wrote: {Path.GetFullPath(previewPath)}");
}
Console.WriteLine($"Markers: {atlas.Layers[0].Markers.Count}");
Console.WriteLine($"Bounds: X {atlas.Layers[0].WorldBounds.MinX:0.###}..{atlas.Layers[0].WorldBounds.MaxX:0.###}, Z {atlas.Layers[0].WorldBounds.MinZ:0.###}..{atlas.Layers[0].WorldBounds.MaxZ:0.###}");
return 0;

static string FindMsb(string sourceRoot, string mapId)
{
    string[] candidates =
    [
        Path.Combine(sourceRoot, "mapstudio", $"{mapId}.msb.dcx"),
        Path.Combine(sourceRoot, "MapStudio", $"{mapId}.msb.dcx"),
        Path.Combine(sourceRoot, "map", "MapStudio", $"{mapId}.msb.dcx"),
        Path.Combine(sourceRoot, mapId, $"{mapId}.msb.dcx"),
        Path.Combine("SekiroAPClient", "dists", "Base", "basegame", "map", $"{mapId}.msb.dcx"),
    ];

    return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
}

sealed record AtlasOptions(
    string SourceRoot,
    string OutputDir,
    string MapId,
    string? MsbPath,
    string? OodlePath,
    string? PieceName,
    bool OutlineOnly,
    int ImageWidth,
    int ImageHeight)
{
    public static AtlasOptions Parse(string[] args)
    {
        string sourceRoot = Path.Combine("artifacts", "map_ForTests");
        string outputDir = Path.Combine("artifacts", "map_atlas", "m11_00_00_00");
        string mapId = "m11_00_00_00";
        string? msbPath = null;
        string? oodlePath = null;
        string? pieceName = null;
        bool outlineOnly = false;
        int imageWidth = 2400;
        int imageHeight = 1600;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");
                return args[++i];
            }

            switch (arg)
            {
                case "--source":
                    sourceRoot = Next();
                    break;
                case "--out":
                    outputDir = Next();
                    break;
                case "--map":
                    mapId = Next();
                    break;
                case "--msb":
                    msbPath = Next();
                    break;
                case "--oodle":
                    oodlePath = Next();
                    break;
                case "--piece":
                    pieceName = Next();
                    break;
                case "--outline-only":
                    outlineOnly = true;
                    break;
                case "--width":
                    imageWidth = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--height":
                    imageHeight = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run --project MapAtlasTool -- [--source artifacts/map_ForTests] [--map m11_00_00_00] [--msb path] [--oodle path] [--piece m800000_8000] [--outline-only] [--out artifacts/map_atlas/m11_00_00_00]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new AtlasOptions(sourceRoot, outputDir, mapId, msbPath, oodlePath, pieceName, outlineOnly, imageWidth, imageHeight);
    }
}

static class OodleLoader
{
    private const string DllName = "oo2core_6_win64.dll";

    public static void TryConfigure(string? explicitPath, string sourceRoot)
    {
        string? dllPath = ResolveDllPath(explicitPath)
            ?? ResolveDllPath(Path.Combine(sourceRoot, DllName))
            ?? ResolveDllPath(Path.Combine(sourceRoot, "oodle", DllName))
            ?? ResolveDllPath(Path.Combine("artifacts", DllName))
            ?? ResolveDllPath(Path.Combine("SekiroAPClient", DllName));

        if (dllPath is null)
            return;

        string dir = Path.GetDirectoryName(Path.GetFullPath(dllPath))!;
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!currentPath.Split(Path.PathSeparator).Any(path => string.Equals(Path.GetFullPath(path.Trim()), dir, StringComparison.OrdinalIgnoreCase)))
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + currentPath);
    }

    private static string? ResolveDllPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Directory.Exists(path))
        {
            string dll = Path.Combine(path, DllName);
            return File.Exists(dll) ? dll : null;
        }

        return File.Exists(path) ? path : null;
    }
}

static class AtlasBuilder
{
    public static AtlasDocument Build(string mapId, string msbPath, MSBS msb, int imageWidth, int imageHeight)
    {
        List<AtlasMarker> markers = [];
        List<AtlasPart> parts = [];

        foreach (MSBS.Part part in msb.Parts.GetEntries())
        {
            string kind = PartKind(part);
            parts.Add(new AtlasPart(
                part.Name,
                kind,
                part.ModelName,
                part.EntityID,
                Vec(part.Position),
                Vec(part.Rotation),
                Vec(part.Scale)));

            if (part is MSBS.Part.Player)
                markers.Add(Marker("player_start", part.Name, kind, part.Position, part.EntityID, part.ModelName, null));
        }

        Dictionary<string, MSBS.Part> partByName = msb.Parts.GetEntries()
            .GroupBy(part => part.Name)
            .ToDictionary(group => group.Key, group => group.First());

        Dictionary<string, MSBS.Region> regionByName = msb.Regions.GetEntries()
            .GroupBy(region => region.Name)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (MSBS.Event.Treasure treasure in msb.Events.Treasures)
        {
            Vector3 pos = ResolveEventPosition(treasure, partByName, regionByName);
            Dictionary<string, object?> data = new()
            {
                ["eventId"] = treasure.EventID,
                ["itemLotId"] = treasure.ItemLotID,
                ["actionButtonId"] = treasure.ActionButtonID,
                ["pickupAnimId"] = treasure.PickupAnimID,
                ["inChest"] = treasure.InChest,
                ["startDisabled"] = treasure.StartDisabled,
                ["partName"] = treasure.PartName,
                ["regionName"] = treasure.RegionName,
                ["treasurePartName"] = treasure.TreasurePartName,
            };
            markers.Add(Marker("treasure", treasure.Name, "treasure", pos, treasure.EntityID, null, data));
        }

        foreach (MSBS.Event.ResourceItemInfo resource in msb.Events.ResourceItemInfo)
        {
            Vector3 pos = ResolveEventPosition(resource, partByName, regionByName);
            Dictionary<string, object?> data = new()
            {
                ["eventId"] = resource.EventID,
                ["resourceItemLotParamId"] = resource.ResourceItemLotParamID,
                ["partName"] = resource.PartName,
                ["regionName"] = resource.RegionName,
            };
            markers.Add(Marker("resource_item", resource.Name, "resource_item", pos, resource.EntityID, null, data));
        }

        foreach (MSBS.Part.Enemy enemy in msb.Parts.Enemies.Where(enemy => enemy.EntityID > 0))
            markers.Add(Marker("enemy", enemy.Name, "enemy", enemy.Position, enemy.EntityID, enemy.ModelName, null));

        foreach (MSBS.Part.Object obj in msb.Parts.Objects.Where(obj => obj.EntityID > 0))
            markers.Add(Marker("object", obj.Name, "object", obj.Position, obj.EntityID, obj.ModelName, null));

        IEnumerable<Vec3> boundsPositions = markers.Count > 0
            ? markers.Select(marker => marker.World)
            : parts.Select(part => part.Position);
        WorldBounds bounds = BoundsFromPositions(boundsPositions);
        bounds = bounds.Pad(Math.Max(bounds.Width, bounds.Depth) * 0.12f + 10f);

        List<AtlasMarker> projected = markers
            .Select(marker => marker with { Image = Project(marker.World, bounds, imageWidth, imageHeight) })
            .OrderBy(marker => marker.Type)
            .ThenBy(marker => marker.Name, StringComparer.Ordinal)
            .ToList();

        List<AtlasPart> orderedParts = parts
            .OrderBy(part => part.Type)
            .ThenBy(part => part.Name, StringComparer.Ordinal)
            .ToList();

        AtlasLayer layer = new(
            Id: "ground",
            Name: "Ground",
            Kind: "world-xz",
            ImageWidth: imageWidth,
            ImageHeight: imageHeight,
            WorldBounds: bounds,
            Markers: projected,
            Parts: orderedParts);

        return new AtlasDocument(
            SchemaVersion: 1,
            Game: "Sekiro",
            Area: "Ashina Outskirts",
            MapId: mapId,
            SourceMsb: msbPath,
            CoordinateSystem: "Sekiro world coordinates; map projection uses X horizontally and inverted Z vertically.",
            Layers: [layer]);
    }

    private static Vector3 ResolveEventPosition(MSBS.Event evnt, Dictionary<string, MSBS.Part> parts, Dictionary<string, MSBS.Region> regions)
    {
        if (evnt is MSBS.Event.Treasure treasure
            && !string.IsNullOrWhiteSpace(treasure.TreasurePartName)
            && parts.TryGetValue(treasure.TreasurePartName, out MSBS.Part? treasurePart))
            return treasurePart.Position;

        if (!string.IsNullOrWhiteSpace(evnt.PartName) && parts.TryGetValue(evnt.PartName, out MSBS.Part? part))
            return part.Position;

        if (!string.IsNullOrWhiteSpace(evnt.RegionName) && regions.TryGetValue(evnt.RegionName, out MSBS.Region? region))
            return region.Position;

        return Vector3.Zero;
    }

    private static AtlasMarker Marker(string idPrefix, string name, string type, Vector3 world, int entityId, string? modelName, Dictionary<string, object?>? data)
    {
        string id = $"{idPrefix}:{entityId}:{name}".Replace(' ', '_');
        return new AtlasMarker(id, name, type, entityId > 0 ? entityId : null, modelName, Vec(world), null, data);
    }

    private static string PartKind(MSBS.Part part) => part switch
    {
        MSBS.Part.MapPiece => "map_piece",
        MSBS.Part.Object => "object",
        MSBS.Part.Enemy => "enemy",
        MSBS.Part.Player => "player",
        MSBS.Part.Collision => "collision",
        MSBS.Part.DummyObject => "dummy_object",
        MSBS.Part.DummyEnemy => "dummy_enemy",
        MSBS.Part.ConnectCollision => "connect_collision",
        _ => part.GetType().Name,
    };

    private static WorldBounds BoundsFromPositions(IEnumerable<Vec3> positions)
    {
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;

        foreach (Vec3 pos in positions)
        {
            minX = Math.Min(minX, pos.X);
            minY = Math.Min(minY, pos.Y);
            minZ = Math.Min(minZ, pos.Z);
            maxX = Math.Max(maxX, pos.X);
            maxY = Math.Max(maxY, pos.Y);
            maxZ = Math.Max(maxZ, pos.Z);
        }

        if (float.IsInfinity(minX))
            return new WorldBounds(-10, -10, -10, 10, 10, 10);

        if (Math.Abs(maxX - minX) < 0.001f)
        {
            minX -= 1;
            maxX += 1;
        }

        if (Math.Abs(maxZ - minZ) < 0.001f)
        {
            minZ -= 1;
            maxZ += 1;
        }

        return new WorldBounds(minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static Vec2 Project(Vec3 world, WorldBounds bounds, int width, int height)
    {
        float x = (world.X - bounds.MinX) / bounds.Width * width;
        float y = (bounds.MaxZ - world.Z) / bounds.Depth * height;
        return new Vec2(x, y);
    }

    private static Vec3 Vec(Vector3 value) => new(value.X, value.Y, value.Z);
}

static class PieceOutlineBuilder
{
    public static PieceOutlineDocument Build(string sourceRoot, string mapId, MSBS msb, AtlasLayer layer, string pieceName)
    {
        MSBS.Part.MapPiece part = msb.Parts.MapPieces.FirstOrDefault(piece => string.Equals(piece.Name, pieceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Map piece not found in {mapId}: {pieceName}");

        string modelId = part.ModelName;
        string mapbndPath = Path.Combine(sourceRoot, mapId, $"{mapId}_{modelId[1..]}.mapbnd.dcx");
        if (!File.Exists(mapbndPath))
            throw new FileNotFoundException($"MapBND not found for {pieceName} ({modelId})", mapbndPath);

        BND4 bnd = BND4.Read(mapbndPath);
        BinderFile flverFile = bnd.Files.FirstOrDefault(file => file.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No FLVER found in {mapbndPath}");

        FLVER2 flver = FLVER2.Read(flverFile.Bytes);
        Matrix4x4 transform = BuildTransform(part.Position, part.Rotation, part.Scale);

        List<Vec2> projectedVertices = [];
        BoundaryBuilder boundaryBuilder = new(0.03f);
        WorldBounds meshWorldBounds = EmptyBounds();
        foreach (FLVER2.Mesh mesh in flver.Meshes)
        {
            foreach (FLVER.Vertex vertex in mesh.Vertices)
            {
                Vector3 world = Vector3.Transform(vertex.Position, transform);
                meshWorldBounds = Include(meshWorldBounds, world);
                projectedVertices.Add(Project(new Vec3(world.X, world.Y, world.Z), layer.WorldBounds, layer.ImageWidth, layer.ImageHeight));
            }

            foreach (FLVER.Vertex[] face in mesh.GetFaces())
            {
                Vector3 a = Vector3.Transform(face[0].Position, transform);
                Vector3 b = Vector3.Transform(face[1].Position, transform);
                Vector3 c = Vector3.Transform(face[2].Position, transform);
                boundaryBuilder.AddTriangle(a, b, c);
            }
        }

        List<Vec2> hull = ConvexHull(projectedVertices);
        List<List<Vec2>> boundaryLoops = boundaryBuilder.BuildLoops()
            .OrderByDescending(loop => Math.Abs(SignedArea(loop)))
            .ToList();

        return new PieceOutlineDocument(
            PieceName: part.Name,
            ModelName: modelId,
            MapBndPath: mapbndPath,
            VertexCount: projectedVertices.Count,
            HullPointCount: hull.Count,
            Position: new Vec3(part.Position.X, part.Position.Y, part.Position.Z),
            Rotation: new Vec3(part.Rotation.X, part.Rotation.Y, part.Rotation.Z),
            Scale: new Vec3(part.Scale.X, part.Scale.Y, part.Scale.Z),
            MeshWorldBounds: meshWorldBounds,
            ImageHull: hull,
            BoundaryLoops: boundaryLoops,
            SvgPath: ToSvgPath(hull));
    }

    private static WorldBounds EmptyBounds()
        => new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

    private static WorldBounds Include(WorldBounds bounds, Vector3 point)
        => new(
            Math.Min(bounds.MinX, point.X),
            Math.Min(bounds.MinY, point.Y),
            Math.Min(bounds.MinZ, point.Z),
            Math.Max(bounds.MaxX, point.X),
            Math.Max(bounds.MaxY, point.Y),
            Math.Max(bounds.MaxZ, point.Z));

    private static Matrix4x4 BuildTransform(Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        Vector3 radians = rotationDegrees * (MathF.PI / 180f);
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationZ(radians.Z)
            * Matrix4x4.CreateTranslation(position);
    }

    private static Vec2 Project(Vec3 world, WorldBounds bounds, int width, int height)
    {
        float x = (world.X - bounds.MinX) / bounds.Width * width;
        float y = (bounds.MaxZ - world.Z) / bounds.Depth * height;
        return new Vec2(x, y);
    }

    private static List<Vec2> ConvexHull(List<Vec2> points)
    {
        List<Vec2> sorted = points
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();

        if (sorted.Count <= 1)
            return sorted;

        List<Vec2> lower = [];
        foreach (Vec2 point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }

        List<Vec2> upper = [];
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            Vec2 point = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Vec2 origin, Vec2 a, Vec2 b)
        => (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);

    private static string ToSvgPath(List<Vec2> points)
    {
        if (points.Count == 0)
            return "";

        Vec2 first = points[0];
        return $"M {first.X:0.###} {first.Y:0.###} "
            + string.Join(" ", points.Skip(1).Select(point => $"L {point.X:0.###} {point.Y:0.###}"))
            + " Z";
    }

    private static float SignedArea(List<Vec2> points)
    {
        if (points.Count < 3)
            return 0;

        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vec2 a = points[i];
            Vec2 b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return (float)(area / 2);
    }

    private sealed class BoundaryBuilder(float tolerance)
    {
        private readonly Dictionary<VertexKey, Vec2> vertices = [];
        private readonly Dictionary<EdgeKey, BoundaryEdge> edges = [];

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            VertexKey ak = AddVertex(a);
            VertexKey bk = AddVertex(b);
            VertexKey ck = AddVertex(c);

            AddEdge(ak, bk);
            AddEdge(bk, ck);
            AddEdge(ck, ak);
        }

        public List<List<Vec2>> BuildLoops()
        {
            Dictionary<VertexKey, List<VertexKey>> adjacency = [];
            foreach (BoundaryEdge edge in edges.Values.Where(edge => edge.Count == 1))
            {
                AddAdjacent(adjacency, edge.A, edge.B);
                AddAdjacent(adjacency, edge.B, edge.A);
            }

            List<List<Vec2>> loops = [];
            HashSet<EdgeKey> visited = [];

            foreach (BoundaryEdge startEdge in edges.Values.Where(edge => edge.Count == 1))
            {
                EdgeKey startKey = EdgeKey.Create(startEdge.A, startEdge.B);
                if (visited.Contains(startKey))
                    continue;

                List<Vec2> loop = [];
                VertexKey start = startEdge.A;
                VertexKey current = startEdge.A;
                VertexKey next = startEdge.B;
                VertexKey? previous = null;

                for (int guard = 0; guard < adjacency.Count + 8; guard++)
                {
                    visited.Add(EdgeKey.Create(current, next));
                    loop.Add(vertices[current]);

                    previous = current;
                    current = next;

                    if (current.Equals(start))
                        break;

                    if (!adjacency.TryGetValue(current, out List<VertexKey>? neighbors))
                        break;

                    VertexKey? candidate = null;
                    foreach (VertexKey neighbor in neighbors)
                    {
                        if (!neighbor.Equals(previous) && !visited.Contains(EdgeKey.Create(current, neighbor)))
                        {
                            candidate = neighbor;
                            break;
                        }
                    }

                    if (candidate is null)
                        break;

                    next = candidate.Value;
                }

                if (loop.Count >= 3)
                    loops.Add(Simplify(loop, tolerance * 1.5f));
            }

            return loops;
        }

        private VertexKey AddVertex(Vector3 value)
        {
            VertexKey key = new(
                (int)MathF.Round(value.X / tolerance),
                (int)MathF.Round(value.Z / tolerance));

            vertices.TryAdd(key, new Vec2(key.X * tolerance, key.Z * tolerance));
            return key;
        }

        private void AddEdge(VertexKey a, VertexKey b)
        {
            if (a.Equals(b))
                return;

            EdgeKey key = EdgeKey.Create(a, b);
            if (edges.TryGetValue(key, out BoundaryEdge? edge))
                edge.Count++;
            else
                edges[key] = new BoundaryEdge(a, b);
        }

        private static void AddAdjacent(Dictionary<VertexKey, List<VertexKey>> adjacency, VertexKey a, VertexKey b)
        {
            if (!adjacency.TryGetValue(a, out List<VertexKey>? neighbors))
            {
                neighbors = [];
                adjacency[a] = neighbors;
            }

            if (!neighbors.Contains(b))
                neighbors.Add(b);
        }

        private static List<Vec2> Simplify(List<Vec2> points, float epsilon)
        {
            if (points.Count <= 3)
                return points;

            List<Vec2> simplified = [];
            for (int i = 0; i < points.Count; i++)
            {
                Vec2 previous = points[(i - 1 + points.Count) % points.Count];
                Vec2 current = points[i];
                Vec2 next = points[(i + 1) % points.Count];
                if (DistanceToLine(current, previous, next) > epsilon)
                    simplified.Add(current);
            }

            return simplified.Count >= 3 ? simplified : points;
        }

        private static float DistanceToLine(Vec2 point, Vec2 a, Vec2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.000001f)
                return MathF.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));

            return MathF.Abs(dy * point.X - dx * point.Y + b.X * a.Y - b.Y * a.X) / MathF.Sqrt(lengthSquared);
        }
    }

    private readonly record struct VertexKey(int X, int Z);

    private readonly record struct EdgeKey(VertexKey A, VertexKey B)
    {
        public static EdgeKey Create(VertexKey a, VertexKey b)
            => Compare(a, b) <= 0 ? new EdgeKey(a, b) : new EdgeKey(b, a);

        private static int Compare(VertexKey a, VertexKey b)
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Z.CompareTo(b.Z);
        }
    }

    private sealed class BoundaryEdge(VertexKey a, VertexKey b)
    {
        public VertexKey A { get; } = a;
        public VertexKey B { get; } = b;
        public int Count { get; set; } = 1;
    }
}

static class PreviewWriter
{
    public static string Write(AtlasDocument atlas, string outputDir)
    {
        AtlasLayer layer = atlas.Layers[0];
        List<PreviewPieceOutline> pieceOutlines = LoadPieceOutlines(outputDir, layer);
        List<ManualContour> manualContours = LoadManualContours(outputDir);
        string json = JsonSerializer.Serialize(atlas, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        StringBuilder sb = new();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{Escape(atlas.Area)} Atlas Preview</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            :root { color-scheme: dark; font-family: Segoe UI, system-ui, sans-serif; background: #11100d; color: #eee4d2; }
            html, body { margin: 0; height: 100%; overflow: hidden; }
            .shell { display: grid; grid-template-columns: 280px 1fr; height: 100%; }
            aside { border-right: 1px solid #3c3328; background: #171511; padding: 16px; overflow: auto; }
            h1 { font-size: 18px; margin: 0 0 6px; font-weight: 700; }
            .sub { color: #b7aa93; font-size: 12px; line-height: 1.45; margin-bottom: 14px; }
            .stat { display: grid; grid-template-columns: 1fr auto; gap: 8px; font-size: 13px; padding: 6px 0; border-bottom: 1px solid #2a251d; }
            .filters { display: grid; gap: 8px; margin-top: 14px; }
            .filter { display: flex; align-items: center; gap: 8px; color: #d8ccb7; font-size: 13px; }
            .filter input { accent-color: #d8a93c; }
            .tools { display: grid; gap: 8px; margin-top: 14px; }
            .tool-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
            textarea { box-sizing: border-box; width: 100%; min-height: 86px; resize: vertical; border: 1px solid #4b3b2a; border-radius: 6px; color: #f1e8d8; background: #211b14; padding: 8px; font: 12px Consolas, monospace; }
            .viewport { position: relative; overflow: hidden; background:
                radial-gradient(circle at 20% 10%, rgba(179, 55, 47, .18), transparent 26%),
                linear-gradient(145deg, #18140f, #0b0b0a); cursor: grab; }
            .viewport.dragging { cursor: grabbing; }
            .viewport.editing-pieces { cursor: crosshair; }
            .viewport.drawing-contour { cursor: crosshair; }
            .map { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
            svg { display: block; background: #211d16; box-shadow: 0 0 0 1px #4a3a29 inset; }
            .piece { fill: rgba(214, 203, 178, .12); stroke: rgba(238, 225, 196, .28); stroke-width: 1; vector-effect: non-scaling-stroke; }
            .piece.collision { fill: rgba(78, 117, 122, .08); stroke: rgba(114, 163, 168, .16); }
            .piece-outline { fill: rgba(221, 211, 182, .26); stroke: rgba(245, 229, 190, .62); stroke-width: 1.2; vector-effect: non-scaling-stroke; }
            .piece-outline:hover { fill: rgba(135, 219, 140, .28); stroke: rgba(135, 219, 140, .9); }
            .piece.excluded { opacity: .18; stroke: rgba(240, 92, 76, .85); stroke-width: 2; }
            .hidden-by-filter { display: none; }
            .hidden-by-exclusion { display: none; }
            .saved-contour { fill: rgba(103, 218, 118, .16); stroke: rgba(112, 220, 122, .92); stroke-width: 2.5; vector-effect: non-scaling-stroke; pointer-events: none; }
            .marker { cursor: pointer; opacity: .88; transition: opacity .12s ease; }
            .marker:hover { opacity: 1; }
            .marker circle { stroke: #17120d; stroke-width: 2; filter: drop-shadow(0 1px 5px rgba(0,0,0,.65)); }
            .treasure circle { fill: #f0c85a; }
            .resource_item circle { fill: #75d0c5; }
            .enemy circle { fill: #ce5347; }
            .object circle { fill: #96b66a; }
            .player_start circle { fill: #9cbcff; }
            .marker text { display: none; pointer-events: none; fill: #f4ead7; font-size: 12px; paint-order: stroke; stroke: #17120d; stroke-width: 4px; }
            .marker:hover text { display: block; }
            .manual-contour { fill: rgba(91, 177, 103, .18); stroke: #70dc7a; stroke-width: 3; vector-effect: non-scaling-stroke; pointer-events: none; }
            .contour-point { fill: #70dc7a; stroke: #17120d; stroke-width: 2; pointer-events: none; }
            .hud { position: absolute; right: 16px; top: 16px; display: flex; gap: 8px; }
            button { border: 1px solid #5a4732; color: #f1e8d8; background: #2b241b; border-radius: 6px; padding: 8px 10px; font-weight: 700; }
            button.active { border-color: #80d987; background: #22351f; }
            button:hover { background: #3a3024; }
            .tooltip { position: absolute; left: 16px; bottom: 16px; max-width: 460px; background: rgba(20,17,13,.94); border: 1px solid #5a4732; border-radius: 8px; padding: 12px; box-shadow: 0 14px 40px rgba(0,0,0,.35); }
            .tooltip strong { display: block; margin-bottom: 4px; }
            code { color: #f0c85a; }
            @media (max-width: 800px) { .shell { grid-template-columns: 1fr; grid-template-rows: auto 1fr; } aside { max-height: 180px; border-right: 0; border-bottom: 1px solid #3c3328; } }
            """);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"shell\">");
        sb.AppendLine("<aside>");
        sb.AppendLine($"<h1>{Escape(atlas.Area)}</h1>");
        sb.AppendLine($"<div class=\"sub\"><code>{Escape(atlas.MapId)}</code><br>{Escape(atlas.CoordinateSystem)}</div>");
        sb.AppendLine($"<div class=\"stat\"><span>Markers</span><strong>{layer.Markers.Count}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Parts</span><strong>{layer.Parts.Count}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>World X</span><strong>{layer.WorldBounds.MinX:0.#} .. {layer.WorldBounds.MaxX:0.#}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>World Z</span><strong>{layer.WorldBounds.MinZ:0.#} .. {layer.WorldBounds.MaxZ:0.#}</strong></div>");
        sb.AppendLine("<div class=\"filters\">");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" data-filter=\"treasure\" checked> Treasures</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" data-filter=\"resource_item\" checked> Resource items</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" data-filter=\"player_start\" checked> Player starts</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" data-filter=\"enemy\"> Enemies</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" data-filter=\"object\"> Objects</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" id=\"showMapPieces\" checked> Map Pieces</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" id=\"showOutlines\" checked> Auto outlines</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" id=\"showContours\" checked> Manual contours</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" id=\"showCollisions\"> Collisions</label>");
        sb.AppendLine("<label class=\"filter\"><input type=\"checkbox\" id=\"hideExcludedPieces\" checked> Hide excluded pieces</label>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"tools\">");
        sb.AppendLine("<div class=\"tool-row\"><button id=\"editPieces\" type=\"button\">Edit Pieces</button><button id=\"drawContour\" type=\"button\">Draw Contour</button></div>");
        sb.AppendLine("<div class=\"tool-row\"><button id=\"copyExcluded\" type=\"button\">Copy Excluded</button><button id=\"clearContour\" type=\"button\">Clear Contour</button></div>");
        sb.AppendLine("<textarea id=\"excludedInput\" spellcheck=\"false\" placeholder=\"Paste excluded Map Piece names JSON or one per line\"></textarea>");
        sb.AppendLine("<div class=\"tool-row\"><button id=\"importExcluded\" type=\"button\">Import Excluded</button><button id=\"copyContour\" type=\"button\">Copy Contour</button></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</aside>");
        sb.AppendLine("<main id=\"viewport\" class=\"viewport\">");
        sb.AppendLine("<div id=\"map\" class=\"map\">");
        sb.AppendLine($"<svg id=\"svg\" width=\"{layer.ImageWidth}\" height=\"{layer.ImageHeight}\" viewBox=\"0 0 {layer.ImageWidth} {layer.ImageHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.AppendLine("<defs><pattern id=\"grid\" width=\"24\" height=\"24\" patternUnits=\"userSpaceOnUse\"><path d=\"M 24 0 L 0 0 0 24\" fill=\"none\" stroke=\"rgba(238,225,196,.07)\" stroke-width=\"1\"/></pattern></defs>");
        sb.AppendLine($"<rect width=\"{layer.ImageWidth}\" height=\"{layer.ImageHeight}\" fill=\"url(#grid)\"/>");

        foreach (PreviewPieceOutline outline in pieceOutlines)
        {
            foreach (string path in outline.Paths)
                sb.AppendLine($"<path class=\"piece-outline\" data-piece-name=\"{Escape(outline.PieceName)}\" data-piece-type=\"map_piece\" d=\"{Escape(path)}\"><title>{Escape(outline.PieceName)} geometry</title></path>");
        }

        foreach (ManualContour contour in manualContours)
            sb.AppendLine($"<path class=\"saved-contour\" data-contour-name=\"{Escape(contour.Name)}\" d=\"{Escape(contour.Path)}\"><title>{Escape(contour.Name)} contour</title></path>");

        foreach (AtlasPart part in layer.Parts.Where(part => part.Type is "map_piece" or "collision"))
        {
            if (part.Type == "map_piece" && pieceOutlines.Any(outline => string.Equals(outline.PieceName, part.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            Vec2 pos = Project(part.Position, layer.WorldBounds, layer.ImageWidth, layer.ImageHeight);
            string css = part.Type == "collision" ? "piece collision" : "piece";
            sb.AppendLine($"<rect class=\"{css}\" data-piece-name=\"{Escape(part.Name)}\" data-piece-type=\"{Escape(part.Type)}\" x=\"{pos.X - 3:0.###}\" y=\"{pos.Y - 3:0.###}\" width=\"6\" height=\"6\"><title>{Escape(part.Name)} ({part.Type})</title></rect>");
        }

        sb.AppendLine("<path id=\"manualContour\" class=\"manual-contour\" d=\"\"/>");
        sb.AppendLine("<g id=\"manualContourPoints\"></g>");

        foreach (AtlasMarker marker in layer.Markers)
        {
            if (marker.Image is null)
                continue;

            Vec2 image = marker.Image.Value;
            string css = marker.Type;
            sb.AppendLine($"<g class=\"marker {css}\" data-id=\"{Escape(marker.Id)}\" data-type=\"{Escape(marker.Type)}\" data-x=\"{image.X:0.###}\" data-y=\"{image.Y:0.###}\">");
            sb.AppendLine($"<circle r=\"7\"><title>{Escape(marker.Name)}</title></circle>");
            sb.AppendLine($"<text x=\"11\" y=\"4\">{Escape(ShortName(marker.Name))}</text>");
            sb.AppendLine("</g>");
        }

        sb.AppendLine("</svg>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"hud\"><button id=\"zoomOut\">-</button><button id=\"reset\">Reset</button><button id=\"zoomIn\">+</button></div>");
        sb.AppendLine("<div id=\"tooltip\" class=\"tooltip\"><strong>Atlas preview</strong>Wheel to zoom, drag to pan. Click a marker to inspect exact world coordinates.</div>");
        sb.AppendLine("</main>");
        sb.AppendLine("</div>");
        sb.AppendLine("<script>");
        sb.AppendLine($"const atlas = {json};");
        sb.AppendLine("""
            const viewport = document.getElementById('viewport');
            const map = document.getElementById('map');
            const tooltip = document.getElementById('tooltip');
            const manualContour = document.getElementById('manualContour');
            const manualContourPoints = document.getElementById('manualContourPoints');
            let scale = 0.42;
            let tx = 80;
            let ty = 50;
            let dragging = false;
            let editPieces = false;
            let drawContour = false;
            let lastX = 0;
            let lastY = 0;
            let markerNodes = [];
            let pieceNodes = [];
            let savedContourNodes = [];
            let contourPoints = [];
            const excludedPieces = new Set(JSON.parse(localStorage.getItem('atlasExcludedPieces') || '[]'));

            function apply() {
              map.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;
              const markerScale = 1 / scale;
              markerNodes.forEach(node => {
                node.setAttribute('transform', `translate(${node.dataset.x} ${node.dataset.y}) scale(${markerScale})`);
              });
              manualContour.style.strokeWidth = Math.max(1.5, 3 / scale);
              [...manualContourPoints.children].forEach(node => {
                node.setAttribute('r', Math.max(2.5, 7 / scale));
                node.style.strokeWidth = Math.max(1, 2 / scale);
              });
            }

            function zoomAt(delta, x, y) {
              const next = Math.min(40, Math.max(0.15, scale * delta));
              const wx = (x - tx) / scale;
              const wy = (y - ty) / scale;
              tx = x - wx * next;
              ty = y - wy * next;
              scale = next;
              apply();
            }

            viewport.addEventListener('wheel', event => {
              event.preventDefault();
              const rect = viewport.getBoundingClientRect();
              zoomAt(event.deltaY < 0 ? 1.12 : 0.88, event.clientX - rect.left, event.clientY - rect.top);
            }, { passive: false });

            viewport.addEventListener('pointerdown', event => {
              if (editPieces) {
                const piece = event.target.closest?.('[data-piece-type]');
                if (piece && piece.dataset.pieceType === 'map_piece') {
                  const name = piece.dataset.pieceName;
                  if (excludedPieces.has(name)) excludedPieces.delete(name);
                  else excludedPieces.add(name);
                  saveExcluded();
                  applyPieceVisibility();
                  event.preventDefault();
                  event.stopPropagation();
                }
                return;
              }

              if (drawContour) {
                const point = clientToMap(event.clientX, event.clientY);
                contourPoints.push(point);
                renderContour();
                event.preventDefault();
                event.stopPropagation();
                return;
              }

              dragging = true;
              viewport.classList.add('dragging');
              lastX = event.clientX;
              lastY = event.clientY;
              viewport.setPointerCapture(event.pointerId);
            });

            viewport.addEventListener('pointermove', event => {
              if (!dragging) return;
              tx += event.clientX - lastX;
              ty += event.clientY - lastY;
              lastX = event.clientX;
              lastY = event.clientY;
              apply();
            });

            viewport.addEventListener('pointerup', event => {
              dragging = false;
              viewport.classList.remove('dragging');
              viewport.releasePointerCapture(event.pointerId);
            });

            document.getElementById('zoomIn').addEventListener('click', () => zoomAt(1.18, viewport.clientWidth / 2, viewport.clientHeight / 2));
            document.getElementById('zoomOut').addEventListener('click', () => zoomAt(0.82, viewport.clientWidth / 2, viewport.clientHeight / 2));
            document.getElementById('reset').addEventListener('click', () => { scale = 0.42; tx = 80; ty = 50; apply(); });

            const byId = new Map(atlas.layers[0].markers.map(marker => [marker.id, marker]));
            const visibleTypes = new Set([...document.querySelectorAll('[data-filter]:checked')].map(input => input.dataset.filter));

            function applyFilters() {
              markerNodes.forEach(node => {
                node.style.display = visibleTypes.has(node.dataset.type) ? '' : 'none';
              });
            }

            function saveExcluded() {
              localStorage.setItem('atlasExcludedPieces', JSON.stringify([...excludedPieces].sort()));
              document.getElementById('excludedInput').value = JSON.stringify([...excludedPieces].sort(), null, 2);
            }

            function applyPieceVisibility() {
              const showMapPieces = document.getElementById('showMapPieces').checked;
              const showOutlines = document.getElementById('showOutlines').checked;
              const showCollisions = document.getElementById('showCollisions').checked;
              const hideExcluded = document.getElementById('hideExcludedPieces').checked;

              pieceNodes.forEach(node => {
                const isMapPiece = node.dataset.pieceType === 'map_piece';
                const isCollision = node.dataset.pieceType === 'collision';
                const isOutline = node.classList.contains('piece-outline');
                const isExcluded = excludedPieces.has(node.dataset.pieceName);
                node.classList.toggle('excluded', isExcluded);
                node.classList.toggle('hidden-by-filter', (isOutline && !showOutlines) || (!isOutline && isMapPiece && !showMapPieces) || (isCollision && !showCollisions));
                node.classList.toggle('hidden-by-exclusion', isMapPiece && isExcluded && hideExcluded);
              });
            }

            function applyContourVisibility() {
              const showContours = document.getElementById('showContours').checked;
              savedContourNodes.forEach(node => {
                node.classList.toggle('hidden-by-filter', !showContours);
              });
            }

            function clientToMap(clientX, clientY) {
              const rect = viewport.getBoundingClientRect();
              return {
                x: (clientX - rect.left - tx) / scale,
                y: (clientY - rect.top - ty) / scale
              };
            }

            function renderContour() {
              if (contourPoints.length === 0) {
                manualContour.setAttribute('d', '');
                manualContourPoints.replaceChildren();
                return;
              }

              const [first, ...rest] = contourPoints;
              const d = `M ${first.x.toFixed(3)} ${first.y.toFixed(3)} `
                + rest.map(point => `L ${point.x.toFixed(3)} ${point.y.toFixed(3)}`).join(' ')
                + (contourPoints.length > 2 ? ' Z' : '');
              manualContour.setAttribute('d', d);
              manualContourPoints.replaceChildren(...contourPoints.map(point => {
                const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
                circle.setAttribute('class', 'contour-point');
                circle.setAttribute('cx', point.x);
                circle.setAttribute('cy', point.y);
                return circle;
              }));
              apply();
            }

            async function copyText(value) {
              try {
                await navigator.clipboard.writeText(value);
              } catch {
                document.getElementById('excludedInput').value = value;
                document.getElementById('excludedInput').select();
              }
            }

            document.querySelectorAll('[data-filter]').forEach(input => {
              input.addEventListener('change', () => {
                if (input.checked) visibleTypes.add(input.dataset.filter);
                else visibleTypes.delete(input.dataset.filter);
                applyFilters();
              });
            });

            document.getElementById('showMapPieces').addEventListener('change', applyPieceVisibility);
            document.getElementById('showOutlines').addEventListener('change', applyPieceVisibility);
            document.getElementById('showContours').addEventListener('change', applyContourVisibility);
            document.getElementById('showCollisions').addEventListener('change', applyPieceVisibility);
            document.getElementById('hideExcludedPieces').addEventListener('change', applyPieceVisibility);
            document.getElementById('editPieces').addEventListener('click', event => {
              editPieces = !editPieces;
              if (editPieces) drawContour = false;
              event.currentTarget.classList.toggle('active', editPieces);
              document.getElementById('drawContour').classList.toggle('active', drawContour);
              viewport.classList.toggle('editing-pieces', editPieces);
              viewport.classList.toggle('drawing-contour', drawContour);
            });
            document.getElementById('drawContour').addEventListener('click', event => {
              drawContour = !drawContour;
              if (drawContour) editPieces = false;
              event.currentTarget.classList.toggle('active', drawContour);
              document.getElementById('editPieces').classList.toggle('active', editPieces);
              viewport.classList.toggle('editing-pieces', editPieces);
              viewport.classList.toggle('drawing-contour', drawContour);
            });
            document.getElementById('copyExcluded').addEventListener('click', () => copyText(JSON.stringify([...excludedPieces].sort(), null, 2)));
            document.getElementById('importExcluded').addEventListener('click', () => {
              const raw = document.getElementById('excludedInput').value.trim();
              if (!raw) return;
              let names;
              try {
                names = JSON.parse(raw);
              } catch {
                names = raw.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean);
              }
              excludedPieces.clear();
              names.forEach(name => excludedPieces.add(String(name)));
              saveExcluded();
              applyPieceVisibility();
            });
            document.getElementById('clearContour').addEventListener('click', () => {
              contourPoints = [];
              renderContour();
            });
            document.getElementById('copyContour').addEventListener('click', () => {
              copyText(JSON.stringify(contourPoints.map(point => ({ x: Number(point.x.toFixed(3)), y: Number(point.y.toFixed(3)) })), null, 2));
            });

            markerNodes = [...document.querySelectorAll('.marker')];
            pieceNodes = [...document.querySelectorAll('[data-piece-type]')];
            savedContourNodes = [...document.querySelectorAll('.saved-contour')];
            markerNodes.forEach(node => {
              node.addEventListener('click', event => {
                event.stopPropagation();
                const marker = byId.get(node.dataset.id);
                const data = marker.data || {};
                tooltip.innerHTML = `<strong>${marker.name}</strong>
                  <div>Type: <code>${marker.type}</code>${marker.entityId ? `, entity <code>${marker.entityId}</code>` : ''}</div>
                  <div>World: <code>${marker.world.x.toFixed(3)}, ${marker.world.y.toFixed(3)}, ${marker.world.z.toFixed(3)}</code></div>
                  ${data.itemLotId ? `<div>Item lot: <code>${data.itemLotId}</code></div>` : ''}
                  ${marker.modelName ? `<div>Model: <code>${marker.modelName}</code></div>` : ''}`;
              });
            });

            applyFilters();
            saveExcluded();
            applyPieceVisibility();
            applyContourVisibility();
            apply();
            """);
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static List<PreviewPieceOutline> LoadPieceOutlines(string outputDir, AtlasLayer layer)
    {
        string outlineDir = Path.Combine(outputDir, "map_pieces_outlines");
        if (!Directory.Exists(outlineDir))
            outlineDir = Path.Combine(outputDir, "map_piece_outlines");
        if (!Directory.Exists(outlineDir))
            return [];

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        List<PreviewPieceOutline> outlines = [];
        foreach (string path in Directory.EnumerateFiles(outlineDir, "*.outline.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                PieceOutlineDocument? document = JsonSerializer.Deserialize<PieceOutlineDocument>(File.ReadAllText(path), options);
                if (document is null || document.BoundaryLoops.Count == 0)
                    continue;

                List<Vec2> displayContour = BuildDisplayContour(document.BoundaryLoops);
                List<string> paths = displayContour.Count >= 3
                    ? [ToSvgPath(displayContour.Select(point => ProjectWorldXZ(point, layer.WorldBounds, layer.ImageWidth, layer.ImageHeight)).ToList())]
                    : [];

                if (paths.Count > 0)
                    outlines.Add(new PreviewPieceOutline(document.PieceName, paths));
            }
            catch
            {
                // Keep preview generation resilient while outline files are experimental.
            }
        }

        return outlines;
    }

    private static List<ManualContour> LoadManualContours(string outputDir)
    {
        string contourDir = Path.Combine(outputDir, "contours");
        if (!Directory.Exists(contourDir))
            return [];

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        List<ManualContour> contours = [];
        foreach (string path in Directory.EnumerateFiles(contourDir, "*.txt").OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                List<Vec2>? points = JsonSerializer.Deserialize<List<Vec2>>(File.ReadAllText(path), options);
                if (points is null || points.Count < 3)
                    continue;

                string name = Path.GetFileNameWithoutExtension(path);
                contours.Add(new ManualContour(name, ToSvgPath(points)));
            }
            catch
            {
                // Hand-authored contour files are optional; skip malformed drafts.
            }
        }

        return contours;
    }

    private static List<Vec2> BuildDisplayContour(List<List<Vec2>> loops)
    {
        List<Vec2>? outer = loops
            .Where(loop => loop.Count >= 3)
            .OrderByDescending(loop => Math.Abs(SignedArea(loop)))
            .FirstOrDefault();

        if (outer is null)
            return [];

        List<Vec2> simplified = SimplifyClosedLoop(outer, 0.25f);
        return SmoothClosedLoop(simplified, 1);
    }

    private static List<Vec2> SimplifyClosedLoop(List<Vec2> points, float epsilon)
    {
        if (points.Count <= 8)
            return points;

        List<Vec2> simplified = [];
        for (int i = 0; i < points.Count; i++)
        {
            Vec2 previous = points[(i - 1 + points.Count) % points.Count];
            Vec2 current = points[i];
            Vec2 next = points[(i + 1) % points.Count];
            if (DistanceToLine(current, previous, next) > epsilon)
                simplified.Add(current);
        }

        return simplified.Count >= 3 ? simplified : points;
    }

    private static List<Vec2> SmoothClosedLoop(List<Vec2> points, int iterations)
    {
        List<Vec2> current = points;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (current.Count < 3)
                return current;

            List<Vec2> next = [];
            for (int i = 0; i < current.Count; i++)
            {
                Vec2 a = current[i];
                Vec2 b = current[(i + 1) % current.Count];
                next.Add(new Vec2(a.X * 0.75f + b.X * 0.25f, a.Y * 0.75f + b.Y * 0.25f));
                next.Add(new Vec2(a.X * 0.25f + b.X * 0.75f, a.Y * 0.25f + b.Y * 0.75f));
            }

            current = next;
        }

        return current;
    }

    private static float DistanceToLine(Vec2 point, Vec2 a, Vec2 b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001f)
            return MathF.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));

        return MathF.Abs(dy * point.X - dx * point.Y + b.X * a.Y - b.Y * a.X) / MathF.Sqrt(lengthSquared);
    }

    private static Vec2 Project(Vec3 world, WorldBounds bounds, int width, int height)
    {
        float x = (world.X - bounds.MinX) / bounds.Width * width;
        float y = (bounds.MaxZ - world.Z) / bounds.Depth * height;
        return new Vec2(x, y);
    }

    private static Vec2 ProjectWorldXZ(Vec2 worldXZ, WorldBounds bounds, int width, int height)
    {
        float x = (worldXZ.X - bounds.MinX) / bounds.Width * width;
        float y = (bounds.MaxZ - worldXZ.Y) / bounds.Depth * height;
        return new Vec2(x, y);
    }

    private static float SignedArea(List<Vec2> points)
    {
        if (points.Count < 3)
            return 0;

        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vec2 a = points[i];
            Vec2 b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return (float)(area / 2);
    }

    private static string ToSvgPath(List<Vec2> points)
    {
        if (points.Count == 0)
            return "";

        Vec2 first = points[0];
        return $"M {first.X:0.###} {first.Y:0.###} "
            + string.Join(" ", points.Skip(1).Select(point => $"L {point.X:0.###} {point.Y:0.###}"))
            + " Z";
    }

    private static string ShortName(string value)
    {
        int separator = value.LastIndexOf('_');
        if (separator >= 0 && separator + 1 < value.Length)
            return value[(separator + 1)..];

        return value.Length <= 18 ? value : value[..18];
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}

static class PieceOutlinePreviewWriter
{
    private const int CanvasWidth = 1600;
    private const int CanvasHeight = 1200;

    public static string Write(PieceOutlineDocument outline)
    {
        WorldBounds bounds = outline.MeshWorldBounds.Pad(Math.Max(outline.MeshWorldBounds.Width, outline.MeshWorldBounds.Depth) * 0.08f + 1f);
        List<Vec2> hull = outline.BoundaryLoops.Count > 0
            ? ConvexHull(outline.BoundaryLoops.SelectMany(loop => loop).Select(point => ProjectWorldXZ(point, bounds)).ToList())
            : [];
        string hullPath = ToSvgPath(hull);

        StringBuilder sb = new();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{Escape(outline.PieceName)} Geometry Preview</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            :root { color-scheme: dark; font-family: Segoe UI, system-ui, sans-serif; background: #11100d; color: #eee4d2; }
            html, body { margin: 0; height: 100%; overflow: hidden; }
            .shell { display: grid; grid-template-columns: 300px 1fr; height: 100%; }
            aside { border-right: 1px solid #3c3328; background: #171511; padding: 16px; overflow: auto; }
            h1 { font-size: 18px; margin: 0 0 6px; }
            .sub { color: #b7aa93; font-size: 12px; line-height: 1.45; margin-bottom: 14px; overflow-wrap: anywhere; }
            .stat { display: grid; grid-template-columns: 1fr auto; gap: 8px; font-size: 13px; padding: 6px 0; border-bottom: 1px solid #2a251d; }
            .viewport { position: relative; overflow: hidden; background: #15130f; cursor: grab; }
            .viewport.dragging { cursor: grabbing; }
            .map { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
            svg { display: block; background: #1d1a14; box-shadow: 0 0 0 1px #4a3a29 inset; }
            .triangle { fill: rgba(205, 194, 165, .58); stroke: rgba(67, 55, 39, .34); stroke-width: .45; vector-effect: non-scaling-stroke; }
            .outline { fill: rgba(126, 218, 129, .12); stroke: #74e374; stroke-width: 3; vector-effect: non-scaling-stroke; }
            .origin { fill: #f0c85a; stroke: #11100d; stroke-width: 2; vector-effect: non-scaling-stroke; }
            .hud { position: absolute; right: 16px; top: 16px; display: flex; gap: 8px; }
            button { border: 1px solid #5a4732; color: #f1e8d8; background: #2b241b; border-radius: 6px; padding: 8px 10px; font-weight: 700; }
            button:hover { background: #3a3024; }
            code { color: #f0c85a; }
            @media (max-width: 800px) { .shell { grid-template-columns: 1fr; grid-template-rows: auto 1fr; } aside { max-height: 180px; border-right: 0; border-bottom: 1px solid #3c3328; } }
            """);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"shell\">");
        sb.AppendLine("<aside>");
        sb.AppendLine($"<h1>{Escape(outline.PieceName)}</h1>");
        sb.AppendLine($"<div class=\"sub\">Model <code>{Escape(outline.ModelName)}</code><br>{Escape(outline.MapBndPath)}</div>");
        sb.AppendLine($"<div class=\"stat\"><span>Vertices</span><strong>{outline.VertexCount}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Boundary loops</span><strong>{outline.BoundaryLoops.Count}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Boundary points</span><strong>{outline.BoundaryLoops.Sum(loop => loop.Count)}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Hull points</span><strong>{outline.HullPointCount}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Position</span><strong>{outline.Position.X:0.###}, {outline.Position.Y:0.###}, {outline.Position.Z:0.###}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Rotation</span><strong>{outline.Rotation.X:0.###}, {outline.Rotation.Y:0.###}, {outline.Rotation.Z:0.###}</strong></div>");
        sb.AppendLine($"<div class=\"stat\"><span>Size X/Z</span><strong>{outline.MeshWorldBounds.Width:0.###} / {outline.MeshWorldBounds.Depth:0.###}</strong></div>");
        sb.AppendLine("</aside>");
        sb.AppendLine("<main id=\"viewport\" class=\"viewport\">");
        sb.AppendLine("<div id=\"map\" class=\"map\">");
        sb.AppendLine($"<svg width=\"{CanvasWidth}\" height=\"{CanvasHeight}\" viewBox=\"0 0 {CanvasWidth} {CanvasHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        foreach (List<Vec2> loop in outline.BoundaryLoops)
        {
            string path = ToSvgPath(loop.Select(point => ProjectWorldXZ(point, bounds)).ToList());
            sb.AppendLine($"<path class=\"triangle\" d=\"{Escape(path)}\"/>");
        }

        sb.AppendLine($"<path class=\"outline\" d=\"{Escape(hullPath)}\"/>");
        Vec2 origin = Project(outline.Position, bounds);
        sb.AppendLine($"<circle class=\"origin\" cx=\"{origin.X:0.###}\" cy=\"{origin.Y:0.###}\" r=\"5\"><title>MSB position</title></circle>");
        sb.AppendLine("</svg>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"hud\"><button id=\"zoomOut\">-</button><button id=\"reset\">Reset</button><button id=\"zoomIn\">+</button></div>");
        sb.AppendLine("</main>");
        sb.AppendLine("</div>");
        sb.AppendLine("<script>");
        sb.AppendLine("""
            const viewport = document.getElementById('viewport');
            const map = document.getElementById('map');
            const outline = document.querySelector('.outline');
            const origin = document.querySelector('.origin');
            let scale = 1;
            let tx = 0;
            let ty = 0;
            let dragging = false;
            let lastX = 0;
            let lastY = 0;

            function apply() {
              map.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;
              outline.style.strokeWidth = Math.max(1.5, 3 / scale);
              origin.setAttribute('r', Math.max(2.5, 5 / scale));
              origin.style.strokeWidth = Math.max(1, 2 / scale);
            }

            function resetView() {
              const box = outline.getBBox();
              const pad = 1.18;
              scale = Math.min(40, Math.max(0.15, Math.min(viewport.clientWidth / (box.width * pad), viewport.clientHeight / (box.height * pad))));
              tx = viewport.clientWidth / 2 - (box.x + box.width / 2) * scale;
              ty = viewport.clientHeight / 2 - (box.y + box.height / 2) * scale;
              apply();
            }

            function zoomAt(delta, x, y) {
              const next = Math.min(40, Math.max(0.15, scale * delta));
              const wx = (x - tx) / scale;
              const wy = (y - ty) / scale;
              tx = x - wx * next;
              ty = y - wy * next;
              scale = next;
              apply();
            }

            viewport.addEventListener('wheel', event => {
              event.preventDefault();
              const rect = viewport.getBoundingClientRect();
              zoomAt(event.deltaY < 0 ? 1.12 : 0.88, event.clientX - rect.left, event.clientY - rect.top);
            }, { passive: false });

            viewport.addEventListener('pointerdown', event => {
              dragging = true;
              viewport.classList.add('dragging');
              lastX = event.clientX;
              lastY = event.clientY;
              viewport.setPointerCapture(event.pointerId);
            });

            viewport.addEventListener('pointermove', event => {
              if (!dragging) return;
              tx += event.clientX - lastX;
              ty += event.clientY - lastY;
              lastX = event.clientX;
              lastY = event.clientY;
              apply();
            });

            viewport.addEventListener('pointerup', event => {
              dragging = false;
              viewport.classList.remove('dragging');
              viewport.releasePointerCapture(event.pointerId);
            });

            document.getElementById('zoomIn').addEventListener('click', () => zoomAt(1.18, viewport.clientWidth / 2, viewport.clientHeight / 2));
            document.getElementById('zoomOut').addEventListener('click', () => zoomAt(0.82, viewport.clientWidth / 2, viewport.clientHeight / 2));
            document.getElementById('reset').addEventListener('click', resetView);
            window.addEventListener('resize', resetView);
            resetView();
            """);
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static Vec2 Project(Vec3 world, WorldBounds bounds)
    {
        float x = (world.X - bounds.MinX) / bounds.Width * CanvasWidth;
        float y = (bounds.MaxZ - world.Z) / bounds.Depth * CanvasHeight;
        return new Vec2(x, y);
    }

    private static Vec2 ProjectWorldXZ(Vec2 worldXZ, WorldBounds bounds)
    {
        float x = (worldXZ.X - bounds.MinX) / bounds.Width * CanvasWidth;
        float y = (bounds.MaxZ - worldXZ.Y) / bounds.Depth * CanvasHeight;
        return new Vec2(x, y);
    }

    private static List<Vec2> ConvexHull(List<Vec2> points)
    {
        List<Vec2> sorted = points
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();

        if (sorted.Count <= 1)
            return sorted;

        List<Vec2> lower = [];
        foreach (Vec2 point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }

        List<Vec2> upper = [];
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            Vec2 point = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Vec2 origin, Vec2 a, Vec2 b)
        => (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);

    private static string ToSvgPath(List<Vec2> points)
    {
        if (points.Count == 0)
            return "";

        Vec2 first = points[0];
        return $"M {first.X:0.###} {first.Y:0.###} "
            + string.Join(" ", points.Skip(1).Select(point => $"L {point.X:0.###} {point.Y:0.###}"))
            + " Z";
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}

sealed record AtlasDocument(
    int SchemaVersion,
    string Game,
    string Area,
    string MapId,
    string SourceMsb,
    string CoordinateSystem,
    List<AtlasLayer> Layers);

sealed record AtlasLayer(
    string Id,
    string Name,
    string Kind,
    int ImageWidth,
    int ImageHeight,
    WorldBounds WorldBounds,
    List<AtlasMarker> Markers,
    List<AtlasPart> Parts);

sealed record AtlasMarker(
    string Id,
    string Name,
    string Type,
    int? EntityId,
    string? ModelName,
    Vec3 World,
    Vec2? Image,
    Dictionary<string, object?>? Data);

sealed record AtlasPart(
    string Name,
    string Type,
    string ModelName,
    int EntityId,
    Vec3 Position,
    Vec3 Rotation,
    Vec3 Scale);

sealed record PieceOutlineDocument(
    string PieceName,
    string ModelName,
    string MapBndPath,
    int VertexCount,
    int HullPointCount,
    Vec3 Position,
    Vec3 Rotation,
    Vec3 Scale,
    WorldBounds MeshWorldBounds,
    List<Vec2> ImageHull,
    List<List<Vec2>> BoundaryLoops,
    string SvgPath);

sealed record PreviewPieceOutline(string PieceName, List<string> Paths);

sealed record ManualContour(string Name, string Path);

readonly record struct Vec2(float X, float Y);

readonly record struct Vec3(float X, float Y, float Z);

readonly record struct WorldBounds(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
{
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public float Depth => MaxZ - MinZ;

    public WorldBounds Pad(float amount) => new(
        MinX - amount,
        MinY - amount,
        MinZ - amount,
        MaxX + amount,
        MaxY + amount,
        MaxZ + amount);
}
