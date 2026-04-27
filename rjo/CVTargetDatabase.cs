using System.Collections.Generic;

public static class CVTargetDatabase
{
    private static Dictionary<string, CVTargetData> targets = new();
    private static Dictionary<string, bool> anchors = new();

    public static bool AddTarget(string name, CVTargetData data, bool isAnchor = false)
    {
        if (string.IsNullOrEmpty(name) || data == null) return false;
        targets[name] = data;
        anchors[name] = isAnchor;
        return true;
    }

    public static bool RemoveTarget(string name)
    {
        anchors.Remove(name);
        return targets.Remove(name);
    }

    public static CVTargetData GetTarget(string name)
    {
        targets.TryGetValue(name, out var t);
        return t;
    }

    public static bool IsAnchor(string name)
    {
        return anchors.TryGetValue(name, out var v) && v;
    }

    public static void SetAnchor(string name, bool anchor)
    {
        if (targets.ContainsKey(name))
            anchors[name] = anchor;
    }

    public static IEnumerable<string> AllTargets() => targets.Keys;

    public static void Clear()
    {
        targets.Clear();
        anchors.Clear();
    }
}
