using System.Collections.Generic;

public static class CVTargetRegistry
{
    static Dictionary<string, CVTargetDefinition> targets = new();

    public static void Register(CVTargetDefinition target)
    {
        if (!targets.ContainsKey(target.targetID))
            targets.Add(target.targetID, target);
    }

    public static CVTargetDefinition Get(string id)
    {
        targets.TryGetValue(id, out var t);
        return t;
    }

    public static IEnumerable<CVTargetDefinition> AllTargets => targets.Values;

    public static void Clear()
    {
        targets.Clear();
    }
}
