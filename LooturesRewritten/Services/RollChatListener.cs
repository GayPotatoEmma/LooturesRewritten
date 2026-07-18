using System;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
namespace LooturesRewritten.Services;

public sealed class RollChatListener : IDisposable
{
    private readonly IChatGui    chatGui;
    private readonly LootTracker lootTracker;

    public RollChatListener(IChatGui chatGui, IPartyList partyList, LootTracker lootTracker)
    {
        this.chatGui     = chatGui;
        this.lootTracker = lootTracker;
        chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        Plugin.Log.Verbose($"[RollChat] LogKind={message.LogKind} Text={message.Message?.TextValue}");
        if ((int)message.LogKind != 65) return;

        var text = message.Message?.TextValue ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        const string lotFor = " lot for ";
        var lotForIndex = text.IndexOf(lotFor, StringComparison.OrdinalIgnoreCase);
        if (lotForIndex < 0) return;

        var beforeLotFor = text[..lotForIndex];

        var castsIndex = beforeLotFor.LastIndexOf(" casts ", StringComparison.OrdinalIgnoreCase);
        string playerName;
        if (castsIndex >= 0)
        {
            playerName = PlayerNames.StripWorld(beforeLotFor[..castsIndex].Trim());
        }
        else
        {
            var castIndex = beforeLotFor.LastIndexOf(" cast ", StringComparison.OrdinalIgnoreCase);
            if (castIndex < 0) return;
            var subject = beforeLotFor[..castIndex].Trim();
            if (!string.Equals(subject, "You", StringComparison.OrdinalIgnoreCase)) return;
            playerName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        }

        if (string.IsNullOrEmpty(playerName)) return;

        var afterLotFor = text[(lotForIndex + lotFor.Length)..].TrimEnd('.');
        if (string.IsNullOrEmpty(afterLotFor)) return;

        foreach (var item in lootTracker.Items)
        {
            if (afterLotFor.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.Debug($"[RollChat] Recording roll: {playerName} -> {item.Name}");
                lootTracker.RecordRoll(playerName, item.Name);
                return;
            }
        }

        Plugin.Log.Warning($"[RollChat] No item match for '{afterLotFor}' (player={playerName}). Tracked items: {string.Join(", ", lootTracker.Items.Select(x => x.Name))}");
        lootTracker.RecordRoll(playerName, afterLotFor);
    }
}
