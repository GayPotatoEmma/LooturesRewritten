using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using FfxivLoot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot;

namespace LooturesRewritten.Services;

public sealed record LootItem(int SlotIndex, string Name, uint IconId);


public sealed unsafe class LootTracker : IDisposable
{
    private readonly IFramework framework;

    private readonly Dictionary<int, LootItem> slotItems = new();

    public Dictionary<int, HashSet<string>> Rolls { get; } = new();

    public List<LootItem> Items { get; } = [];

    public event System.Action? Updated;

    public LootTracker(IFramework framework, IPartyList partyList, IAddonLifecycle addonLifecycle)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    public void Clear()
    {
        slotItems.Clear();
        Rolls.Clear();
        Items.Clear();
        Updated?.Invoke();
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        RefreshFromLootInstance();
    }

    private bool lootWasActive;

    private void RefreshFromLootInstance()
    {
        var loot = FfxivLoot.Instance();
        if (loot is null)
        {
            Plugin.Log.Warning("[LootTracker] Loot.Instance() returned null");
            return;
        }

        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var changed   = false;

        var activeSlots = new HashSet<int>();

        var lootItems = loot->Items;
        for (var i = 0; i < lootItems.Length; i++)
        {
            var slot = lootItems[i];
            if (slot.ItemId == 0) continue;

            var cleanId = slot.ItemId & 0x1FFFFFFFu;

            activeSlots.Add(i);

            if (!lootWasActive)
                Plugin.Log.Debug($"[LootTracker] Loot detected — Slot {i}: ItemId={slot.ItemId} cleanId={cleanId} RollState={slot.RollState}");

            if (!slotItems.ContainsKey(i))
            {
                var row = itemSheet?.GetRowOrDefault(cleanId);
                if (row is null) continue;

                var name   = row.Value.Name.ToString();
                var iconId = row.Value.Icon;
                if (string.IsNullOrEmpty(name)) continue;

                Plugin.Log.Debug($"[LootTracker] Adding item '{name}' (slot {i})");
                var lootItem = new LootItem(i, name, iconId);
                slotItems[i] = lootItem;
                Rolls[i] = [];
                Items.Add(lootItem);
                changed = true;
            }
        }

        var lootIsActive = activeSlots.Count > 0;
        if (lootIsActive && !lootWasActive)
        {
            Plugin.Log.Debug($"[LootTracker] Loot session started with {activeSlots.Count} slot(s)");
            lootWasActive = true;
        }
        else if (!lootIsActive && lootWasActive)
        {
            Plugin.Log.Debug("[LootTracker] Loot session ended — clearing");
            lootWasActive = false;
            slotItems.Clear();
            Rolls.Clear();
            Items.Clear();
            selectedSlot = null;
            selectedItemRemovedCallback = null;
            Updated?.Invoke();
            return;
        }

        var removedSlots = new List<int>();
        foreach (var key in slotItems.Keys)
        {
            if (!activeSlots.Contains(key))
                removedSlots.Add(key);
        }

        foreach (var key in removedSlots)
        {
            var removed = slotItems[key];
            slotItems.Remove(key);
            Items.Remove(removed);
            Rolls.Remove(key);
            if (selectedItemRemovedCallback is not null && removed.SlotIndex == selectedSlot)
                selectedItemRemovedCallback();
            changed = true;
        }

        if (changed)
            Updated?.Invoke();
    }

    private int?           selectedSlot;
    private System.Action? selectedItemRemovedCallback;

    public void SetSelectedSlot(int? slot, System.Action? onRemoved)
    {
        selectedSlot                = slot;
        selectedItemRemovedCallback = onRemoved;
    }

    public void RecordRoll(string playerName, string itemName)
    {
        var changed = false;
        foreach (var (slot, item) in slotItems)
        {
            if (!string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase)) continue;
            if (Rolls.TryGetValue(slot, out var rollers) && rollers.Add(playerName))
                changed = true;
        }
        if (changed)
            Updated?.Invoke();
    }
}
