using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class InbreedingCalculator
{
    public static InbreedingInfo Calculate(int pigeonId, string pigeonName, PedigreeNodeDto? tree)
    {
        if (tree is null)
            return new InbreedingInfo(pigeonId, pigeonName, 0, 0, 0, "Geen stamboom");

        var depth = GetMaxDepth(tree, 0);
        var ancestorPaths = new Dictionary<int, List<int>>();
        CollectAncestors(tree, [], ancestorPaths);

        var commonAncestors = ancestorPaths
            .Where(kv => kv.Value.Count > 1)
            .ToList();

        var coefficient = 0.0;
        foreach (var (_, paths) in commonAncestors)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                for (var j = i + 1; j < paths.Count; j++)
                {
                    var pathLength = paths[i] + paths[j];
                    coefficient += Math.Pow(0.5, pathLength + 1);
                }
            }
        }

        coefficient = Math.Round(coefficient, 4);

        var display = coefficient switch
        {
            0 => "Geen (0%)",
            < 0.0625 => $"Laag ({coefficient * 100:0.0}%)",
            < 0.125 => $"Matig ({coefficient * 100:0.0}%)",
            _ => $"Hoog ({coefficient * 100:0.0}%)",
        };

        return new InbreedingInfo(
            pigeonId,
            pigeonName,
            depth,
            commonAncestors.Count,
            coefficient,
            display);
    }

    private static int GetMaxDepth(PedigreeNodeDto? node, int current)
    {
        if (node is null) return current;
        var cockDepth = GetMaxDepth(node.ParentCock, current + 1);
        var henDepth = GetMaxDepth(node.ParentHen, current + 1);
        return Math.Max(cockDepth, henDepth);
    }

    private static void CollectAncestors(
        PedigreeNodeDto? node,
        List<int> currentPath,
        Dictionary<int, List<int>> ancestorPaths)
    {
        if (node is null) return;

        if (currentPath.Count > 0)
        {
            if (!ancestorPaths.TryGetValue(node.Id, out var paths))
            {
                paths = [];
                ancestorPaths[node.Id] = paths;
            }
            paths.Add(currentPath.Count);
        }

        var nextPath = new List<int>(currentPath) { node.Id };
        CollectAncestors(node.ParentCock, nextPath, ancestorPaths);
        CollectAncestors(node.ParentHen, nextPath, ancestorPaths);
    }
}
