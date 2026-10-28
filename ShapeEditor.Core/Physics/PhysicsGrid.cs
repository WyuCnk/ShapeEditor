using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Physics;

/// <summary>
/// 物理运算的底层工具：分组、支撑关系、坍塌、晶体破碎、截断。
///
/// 不使用位运算压缩，直接操作逻辑单元，因此支持任意层数与象限数。
/// </summary>
internal static class PhysicsGrid
{
    #region 基础

    public static ShapeCell[,] Clone(
        ShapeDocument document)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        var cells = new ShapeCell[layers, parts];

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                cells[layer, part] = document[layer, part].Clone();
            }
        }

        return cells;
    }

    public static ShapeDocument ToDocument(
        ShapeCell[,] cells,
        int layers,
        int parts)
    {
        var document = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                document[layer, part] = cells[layer, part];
            }
        }

        return document;
    }

    public static bool IsSolid(
        ShapeCell cell)
    {
        return cell.Type != CellType.Empty;
    }

    /// <summary>
    /// 水平连通：同一层内相邻象限的普通形状或晶体。
    /// </summary>
    private static bool ConnectsHorizontally(
        CellType left,
        CellType right)
    {
        return IsShapeLike(left) && IsShapeLike(right);
    }

    /// <summary>
    /// 竖直连通：只有晶体与晶体上下融合。
    /// </summary>
    private static bool ConnectsVertically(
        CellType lower,
        CellType upper)
    {
        return lower == CellType.Crystal &&
               upper == CellType.Crystal;
    }

    private static bool IsShapeLike(
        CellType type)
    {
        return type is CellType.Shape or CellType.Crystal;
    }

    #endregion

    #region 分组与支撑

    public static List<List<(int Layer, int Part)>> BuildGroups(
        ShapeCell[,] cells,
        int layers,
        int parts)
    {
        var visited = new bool[layers, parts];

        var groups = new List<List<(int Layer, int Part)>>();

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                if (visited[layer, part] ||
                    !IsSolid(cells[layer, part]))
                {
                    continue;
                }

                var group = new List<(int, int)>();

                var queue = new Queue<(int Layer, int Part)>();

                queue.Enqueue((layer, part));

                visited[layer, part] = true;

                while (queue.Count > 0)
                {
                    (int currentLayer, int currentPart) = queue.Dequeue();

                    group.Add((currentLayer, currentPart));

                    int left = (currentPart - 1 + parts) % parts;
                    int right = (currentPart + 1) % parts;

                    TryEnqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        left,
                        currentLayer,
                        currentPart,
                        horizontal: true);

                    TryEnqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        right,
                        currentLayer,
                        currentPart,
                        horizontal: true);

                    TryEnqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer - 1,
                        currentPart,
                        currentLayer,
                        currentPart,
                        horizontal: false);

                    TryEnqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer + 1,
                        currentPart,
                        currentLayer,
                        currentPart,
                        horizontal: false);
                }

                groups.Add(group);
            }
        }

        return groups;
    }

    private static void TryEnqueue(
        ShapeCell[,] cells,
        bool[,] visited,
        Queue<(int Layer, int Part)> queue,
        int targetLayer,
        int targetPart,
        int sourceLayer,
        int sourcePart,
        bool horizontal)
    {
        int layers = cells.GetLength(0);
        int parts = cells.GetLength(1);

        if (targetLayer < 0 ||
            targetLayer >= layers ||
            targetPart < 0 ||
            targetPart >= parts ||
            visited[targetLayer, targetPart])
        {
            return;
        }

        bool connected = horizontal
            ? ConnectsHorizontally(
                cells[sourceLayer, sourcePart].Type,
                cells[targetLayer, targetPart].Type)
            : ConnectsVertically(
                cells[targetLayer, targetPart].Type,
                cells[sourceLayer, sourcePart].Type);

        if (!connected)
        {
            return;
        }

        visited[targetLayer, targetPart] = true;

        queue.Enqueue((targetLayer, targetPart));
    }

    /// <summary>
    /// 支撑关系：X 支撑 Y，当且仅当 Y 的某个格子正下方是 X 的格子。
    /// </summary>
    public static List<HashSet<int>> BuildSupport(
        List<List<(int Layer, int Part)>> groups,
        int layers,
        int parts,
        out int[,] groupOf)
    {
        groupOf = new int[layers, parts];

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                groupOf[layer, part] = -1;
            }
        }

        for (int index = 0;
             index < groups.Count;
             index++)
        {
            foreach ((int layer, int part) in groups[index])
            {
                groupOf[layer, part] = index;
            }
        }

        var edges = new List<HashSet<int>>(groups.Count);

        for (int index = 0;
             index < groups.Count;
             index++)
        {
            edges.Add(new HashSet<int>());
        }

        for (int group = 0;
             group < groups.Count;
             group++)
        {
            foreach ((int layer, int part) in groups[group])
            {
                if (layer == 0)
                {
                    continue;
                }

                int below = groupOf[layer - 1, part];

                if (below >= 0 && below != group)
                {
                    edges[below].Add(group);
                }
            }
        }

        return edges;
    }

    #endregion

    #region 连通分量与可达性

    /// <summary>
    /// Kosaraju 求强连通分量（迭代实现，避免深层递归）。
    /// </summary>
    private static List<List<int>> FindComponents(
        List<HashSet<int>> edges)
    {
        int count = edges.Count;

        var reverse = new List<List<int>>(count);

        for (int index = 0;
             index < count;
             index++)
        {
            reverse.Add(new List<int>());
        }

        for (int from = 0;
             from < count;
             from++)
        {
            foreach (int to in edges[from])
            {
                reverse[to].Add(from);
            }
        }

        var visited = new bool[count];

        var order = new List<int>(count);

        for (int start = 0;
             start < count;
             start++)
        {
            if (visited[start])
            {
                continue;
            }

            var stack = new Stack<(int Node, int Next)>();

            stack.Push((start, 0));

            visited[start] = true;

            while (stack.Count > 0)
            {
                (int node, int next) = stack.Pop();

                var neighbours = new List<int>(edges[node]);

                if (next < neighbours.Count)
                {
                    stack.Push((node, next + 1));

                    int target = neighbours[next];

                    if (!visited[target])
                    {
                        visited[target] = true;

                        stack.Push((target, 0));
                    }
                }
                else
                {
                    order.Add(node);
                }
            }
        }

        var assigned = new bool[count];

        var components = new List<List<int>>();

        for (int index = order.Count - 1;
             index >= 0;
             index--)
        {
            int start = order[index];

            if (assigned[start])
            {
                continue;
            }

            var component = new List<int>();

            var stack = new Stack<int>();

            stack.Push(start);

            assigned[start] = true;

            while (stack.Count > 0)
            {
                int node = stack.Pop();

                component.Add(node);

                foreach (int target in reverse[node])
                {
                    if (!assigned[target])
                    {
                        assigned[target] = true;

                        stack.Push(target);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static bool[] Reachable(
        IEnumerable<int> roots,
        List<HashSet<int>> edges)
    {
        var reachable = new bool[edges.Count];

        var queue = new Queue<int>();

        foreach (int root in roots)
        {
            if (!reachable[root])
            {
                reachable[root] = true;

                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();

            foreach (int target in edges[node])
            {
                if (!reachable[target])
                {
                    reachable[target] = true;

                    queue.Enqueue(target);
                }
            }
        }

        return reachable;
    }

    #endregion

    #region 坍塌

    public static ShapeDocument Collapse(
        ShapeDocument document,
        CollapseMode mode)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        ShapeCell[,] cells = Clone(document);

        List<List<(int Layer, int Part)>> groups =
            BuildGroups(cells, layers, parts);

        List<HashSet<int>> edges = BuildSupport(
            groups,
            layers,
            parts,
            out _);

        var roots = new List<int>();

        for (int group = 0;
             group < groups.Count;
             group++)
        {
            if (groups[group].Exists(
                    cell => cell.Layer == 0))
            {
                roots.Add(group);
            }
        }

        if (mode == CollapseMode.IncludeCycles)
        {
            foreach (List<int> component in FindComponents(edges))
            {
                if (component.Count > 1)
                {
                    roots.AddRange(component);
                }
            }
        }

        bool[] supported = Reachable(roots, edges);

        var falling = new List<int>();

        for (int group = 0;
             group < groups.Count;
             group++)
        {
            if (!supported[group])
            {
                falling.Add(group);
            }
        }

        if (falling.Count == 0)
        {
            return ToDocument(cells, layers, parts);
        }

        /*
         * 把未支撑组拆成“单层块”：组内晶体先碎掉，
         * 剩下的普通形状按水平连通性重新分组。
         */
        var pieces = new List<List<(int Layer, int Part, ShapeCell Cell)>>();

        foreach (int group in falling)
        {
            var remaining = new List<(int Layer, int Part)>();

            foreach ((int layer, int part) in groups[group])
            {
                if (cells[layer, part].Type == CellType.Crystal)
                {
                    // 下落消晶
                    cells[layer, part] = ShapeCell.Empty();
                }
                else
                {
                    remaining.Add((layer, part));
                }
            }

            foreach (List<(int Layer, int Part)> piece in SplitHorizontally(
                         remaining,
                         cells,
                         parts))
            {
                var entries = new List<(int, int, ShapeCell)>();

                foreach ((int layer, int part) in piece)
                {
                    entries.Add((layer, part, cells[layer, part].Clone()));
                }

                pieces.Add(entries);
            }
        }

        foreach (List<(int Layer, int Part, ShapeCell Cell)> piece in pieces)
        {
            foreach ((int layer, int part, _) in piece)
            {
                cells[layer, part] = ShapeCell.Empty();
            }
        }

        pieces.Sort((left, right) =>
        {
            int byLayer = MinLayer(left).CompareTo(MinLayer(right));

            return byLayer != 0
                ? byLayer
                : MinPart(left).CompareTo(MinPart(right));
        });

        foreach (List<(int Layer, int Part, ShapeCell Cell)> piece in pieces)
        {
            DropPiece(cells, piece);
        }

        return ToDocument(cells, layers, parts);
    }

    private static int MinLayer(
        List<(int Layer, int Part, ShapeCell Cell)> piece)
    {
        int minimum = int.MaxValue;

        foreach ((int layer, _, _) in piece)
        {
            minimum = Math.Min(minimum, layer);
        }

        return minimum;
    }

    private static int MinPart(
        List<(int Layer, int Part, ShapeCell Cell)> piece)
    {
        int minimum = int.MaxValue;

        foreach ((_, int part, _) in piece)
        {
            minimum = Math.Min(minimum, part);
        }

        return minimum;
    }

    private static List<List<(int Layer, int Part)>> SplitHorizontally(
        List<(int Layer, int Part)> cells,
        ShapeCell[,] grid,
        int parts)
    {
        var pending = new HashSet<(int Layer, int Part)>(cells);

        var result = new List<List<(int Layer, int Part)>>();

        while (pending.Count > 0)
        {
            (int Layer, int Part) seed = pending.First();

            pending.Remove(seed);

            var piece = new List<(int, int)>();

            var queue = new Queue<(int Layer, int Part)>();

            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                (int layer, int part) = queue.Dequeue();

                piece.Add((layer, part));

                int left = (part - 1 + parts) % parts;
                int right = (part + 1) % parts;

                foreach ((int targetLayer, int targetPart) in new[]
                         {
                             (layer, left),
                             (layer, right)
                         })
                {
                    if (!pending.Contains((targetLayer, targetPart)))
                    {
                        continue;
                    }

                    if (!ConnectsHorizontally(
                            grid[layer, part].Type,
                            grid[targetLayer, targetPart].Type))
                    {
                        continue;
                    }

                    pending.Remove((targetLayer, targetPart));

                    queue.Enqueue((targetLayer, targetPart));
                }
            }

            result.Add(piece);
        }

        return result;
    }

    private static void DropPiece(
        ShapeCell[,] cells,
        List<(int Layer, int Part, ShapeCell Cell)> piece)
    {
        while (true)
        {
            bool canFall = true;

            foreach ((int layer, int part, _) in piece)
            {
                if (layer <= 0 ||
                    cells[layer - 1, part].Type != CellType.Empty)
                {
                    canFall = false;

                    break;
                }
            }

            if (!canFall)
            {
                break;
            }

            var values = new ShapeCell[piece.Count];

            for (int index = 0;
                 index < piece.Count;
                 index++)
            {
                (int layer, int part, ShapeCell cell) = piece[index];

                values[index] = cell;

                cells[layer, part] = ShapeCell.Empty();
            }

            for (int index = 0;
                 index < piece.Count;
                 index++)
            {
                (int layer, int part, _) = piece[index];

                cells[layer - 1, part] = values[index];

                piece[index] = (layer - 1, part, values[index]);
            }
        }
    }

    #endregion

    #region 晶体破碎与截断

    /// <summary>
    /// 从种子出发，沿晶体网络（环形水平 + 垂直）洪泛，整块删除。
    /// </summary>
    public static void BreakCrystals(
        ShapeCell[,] cells,
        int layers,
        int parts,
        IEnumerable<(int Layer, int Part)> seeds)
    {
        var visited = new bool[layers, parts];

        var queue = new Queue<(int Layer, int Part)>();

        foreach ((int layer, int part) in seeds)
        {
            if (layer < 0 ||
                layer >= layers ||
                part < 0 ||
                part >= parts ||
                visited[layer, part] ||
                cells[layer, part].Type != CellType.Crystal)
            {
                continue;
            }

            visited[layer, part] = true;

            queue.Enqueue((layer, part));
        }

        while (queue.Count > 0)
        {
            (int layer, int part) = queue.Dequeue();

            cells[layer, part] = ShapeCell.Empty();

            int left = (part - 1 + parts) % parts;
            int right = (part + 1) % parts;

            TryEnqueueCrystal(
                cells,
                visited,
                queue,
                layer,
                left,
                layers,
                parts);

            TryEnqueueCrystal(
                cells,
                visited,
                queue,
                layer,
                right,
                layers,
                parts);

            TryEnqueueCrystal(
                cells,
                visited,
                queue,
                layer - 1,
                part,
                layers,
                parts);

            TryEnqueueCrystal(
                cells,
                visited,
                queue,
                layer + 1,
                part,
                layers,
                parts);
        }
    }

    private static void TryEnqueueCrystal(
        ShapeCell[,] cells,
        bool[,] visited,
        Queue<(int Layer, int Part)> queue,
        int layer,
        int part,
        int layers,
        int parts)
    {
        if (layer < 0 ||
            layer >= layers ||
            part < 0 ||
            part >= parts ||
            visited[layer, part] ||
            cells[layer, part].Type != CellType.Crystal)
        {
            return;
        }

        visited[layer, part] = true;

        queue.Enqueue((layer, part));
    }

    /// <summary>
    /// 超上限删除：先让超出上限的晶体整块破碎，再截断。
    /// </summary>
    public static ShapeDocument Truncate(
        ShapeDocument document,
        int heightLimit)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        ShapeCell[,] cells = Clone(document);

        var seeds = new List<(int Layer, int Part)>();

        for (int layer = heightLimit;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                if (cells[layer, part].Type == CellType.Crystal)
                {
                    seeds.Add((layer, part));
                }
            }
        }

        BreakCrystals(cells, layers, parts, seeds);

        var result = new ShapeDocument(
            heightLimit,
            parts);

        int copyLayers = Math.Min(heightLimit, layers);

        for (int layer = 0;
             layer < copyLayers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                result[layer, part] = cells[layer, part];
            }
        }

        return result;
    }

    #endregion

    #region 切割

    /// <summary>
    /// 跨切割线（东西两半都有格子）的晶体整块破碎。
    /// </summary>
    public static void BreakSpanningCrystals(
        ShapeCell[,] cells,
        int layers,
        int parts,
        int half)
    {
        var visited = new bool[layers, parts];

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                if (visited[layer, part] ||
                    cells[layer, part].Type != CellType.Crystal)
                {
                    continue;
                }

                var component = new List<(int Layer, int Part)>();

                var queue = new Queue<(int Layer, int Part)>();

                queue.Enqueue((layer, part));

                visited[layer, part] = true;

                bool west = false;
                bool east = false;

                while (queue.Count > 0)
                {
                    (int currentLayer, int currentPart) = queue.Dequeue();

                    component.Add((currentLayer, currentPart));

                    if (currentPart < half)
                    {
                        west = true;
                    }
                    else
                    {
                        east = true;
                    }

                    int left = (currentPart - 1 + parts) % parts;
                    int right = (currentPart + 1) % parts;

                    EnqueueCrystal(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        left,
                        layers,
                        parts);

                    EnqueueCrystal(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        right,
                        layers,
                        parts);

                    EnqueueCrystal(
                        cells,
                        visited,
                        queue,
                        currentLayer - 1,
                        currentPart,
                        layers,
                        parts);

                    EnqueueCrystal(
                        cells,
                        visited,
                        queue,
                        currentLayer + 1,
                        currentPart,
                        layers,
                        parts);
                }

                if (west && east)
                {
                    foreach ((int layerIndex, int partIndex) in component)
                    {
                        cells[layerIndex, partIndex] = ShapeCell.Empty();
                    }
                }
            }
        }
    }

    private static void EnqueueCrystal(
        ShapeCell[,] cells,
        bool[,] visited,
        Queue<(int Layer, int Part)> queue,
        int layer,
        int part,
        int layers,
        int parts)
    {
        TryEnqueueCrystal(
            cells,
            visited,
            queue,
            layer,
            part,
            layers,
            parts);
    }

    public static ShapeDocument TakeHalf(
        ShapeCell[,] cells,
        int layers,
        int parts,
        int fromPart,
        int toPart)
    {
        var result = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = fromPart;
                 part < toPart;
                 part++)
            {
                result[layer, part] = cells[layer, part].Clone();
            }
        }

        return result;
    }

    #endregion
}
