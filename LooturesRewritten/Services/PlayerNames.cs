using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace LooturesRewritten.Services;

internal static class PlayerNames
{
    private static readonly Lazy<HashSet<string>> WorldNames = new(() =>
    {
        var sheet = Plugin.DataManager.GetExcelSheet<World>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sheet is null) return set;
        foreach (var row in sheet)
        {
            var name = row.Name.ToString();
            if (!string.IsNullOrEmpty(name))
                set.Add(name);
        }
        return set;
    });

    internal static string StripWorld(string playerName)
    {
        foreach (var world in WorldNames.Value)
        {
            if (playerName.EndsWith($" {world}", StringComparison.OrdinalIgnoreCase))
            {
                return playerName[..^(world.Length + 1)].TrimEnd();
            }
        }

        return playerName;
    }
}
