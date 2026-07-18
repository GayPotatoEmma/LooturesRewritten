using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using LooturesRewritten.Services;

namespace LooturesRewritten.Windows;

public unsafe class LootRollAddon : NativeAddon
{
    private const float ContentWidth      = 280f;
    private const float RowHeight         = 22f;
    private const float SectionGap        = 8f;
    private const float WindowPadding     = 16f;
    private const float DropDownHeight    = 23f;
    private const int   MaxDropDownItems  = 8;
    private const int   MaxMemberRows     = 8;
    private const float MemberSectionH    = MaxMemberRows * RowHeight;
    private const float ContentHeight     = DropDownHeight + SectionGap + MemberSectionH;
    private const float TotalWindowWidth  = ContentWidth + WindowPadding;
    private const float TotalWindowHeight = ContentHeight + 53f;

    private const float ColumnSpacing     = 6f;
    private const float MemberColWidth    = (ContentWidth - ColumnSpacing * 2f) / 3f;

    private const int AllianceGroupSize   = 8;
    private const int AllianceOtherCount  = 16;

    private readonly LootTracker lootTracker;
    private readonly IPartyList  partyList;

    private DropDownNode<LootItem>      itemDropDown  = null!;
    private readonly VerticalListNode[] memberColumns = new VerticalListNode[3];

    private int?  selectedSlot;
    private bool  lastIsAlliance;
    private bool  setupComplete;
    private volatile bool pendingRebuild;

    public LootRollAddon(LootTracker lootTracker, IPartyList partyList)
    {
        this.lootTracker = lootTracker;
        this.partyList   = partyList;
        lootTracker.Updated += OnTrackerUpdated;
    }

    public new virtual void Dispose()
    {
        lootTracker.Updated -= OnTrackerUpdated;
        base.Dispose();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        SetWindowSize(new Vector2(TotalWindowWidth, TotalWindowHeight));
        var s = ContentStartPosition;

        itemDropDown = new DropDownNode<LootItem>
        {
            Position          = s,
            Size              = new Vector2(ContentWidth, DropDownHeight),
            MaxListOptions    = MaxDropDownItems,
            PlaceholderString = "Select an item...",
            GetLabelFunction  = item => item.Name,
        };
        itemDropDown.OnOptionSelected = item =>
        {
            selectedSlot      = item.SlotIndex;
            lootTracker.SetSelectedSlot(item.SlotIndex, () =>
            {
                selectedSlot                = null;
                itemDropDown.SelectedOption = default;
                RebuildMemberList(null);
            });
            RebuildMemberList(item.SlotIndex);
        };
        itemDropDown.AttachNode(this);

        var membersY = DropDownHeight + SectionGap;
        for (var i = 0; i < 3; i++)
        {
            memberColumns[i] = new VerticalListNode
            {
                Position         = s + new Vector2(i * (MemberColWidth + ColumnSpacing), membersY),
                Size             = new Vector2(i == 0 ? ContentWidth : MemberColWidth, MemberSectionH),
                ItemSpacing      = 0f,
                FitWidth         = true,
                ClipListContents = true,
                IsVisible        = i == 0,
            };
            memberColumns[i].AttachNode(this);
        }

        RebuildDropDown();
        setupComplete = true;
    }

    protected override void OnShow(AtkUnitBase* addon)
    {
        if (!setupComplete) return;
        lastIsAlliance = partyList.IsAlliance;
        ApplyAlliance(lastIsAlliance);
    }

    protected override void OnDraw(AtkUnitBase* addon)
    {
        if (pendingRebuild)
        {
            pendingRebuild = false;
            RebuildDropDown();
            if (selectedSlot is not null)
                RebuildMemberList(selectedSlot.Value);
        }

        var isAlliance = partyList.IsAlliance;
        if (isAlliance == lastIsAlliance) return;
        lastIsAlliance = isAlliance;
        ApplyAlliance(isAlliance);
    }

    private void ApplyAlliance(bool isAlliance)
    {
        if (memberColumns[0] is null) return;
        memberColumns[0].Size = new Vector2(isAlliance ? MemberColWidth : ContentWidth, MemberSectionH);
        for (var i = 0; i < 3; i++)
            memberColumns[i].IsVisible = isAlliance || i == 0;
        if (selectedSlot is not null)
            RebuildMemberList(selectedSlot.Value);
    }

    private void OnTrackerUpdated()
    {
        if (!setupComplete || InternalAddon is null) return;
        pendingRebuild = true;
    }

    private void RebuildDropDown()
    {
        var items = new System.Collections.Generic.List<LootItem>(lootTracker.Items);
        itemDropDown.Options = items;

        if (selectedSlot is not null)
        {
            var match = items.Find(i => i.SlotIndex == selectedSlot.Value);
            itemDropDown.SelectedOption = match;
            if (match is null)
            {
                selectedSlot = null;
                RebuildMemberList(null);
            }
        }
    }

    private void RebuildMemberList(int? slot)
    {
        HashSet<string> rollers = [];
        if (slot is not null)
            lootTracker.Rolls.TryGetValue(slot.Value, out rollers!);
        rollers ??= [];

        var groups = GetMemberGroups();

        for (var col = 0; col < 3; col++)
        {
            foreach (var node in new List<NodeBase>(memberColumns[col].Nodes))
                memberColumns[col].RemoveNode(node);

            if (col >= groups.Count) continue;

            foreach (var name in groups[col])
                memberColumns[col].AddNode(BuildMemberRow(name, rollers.Contains(name), col == 0 && !partyList.IsAlliance ? ContentWidth : MemberColWidth));
        }
    }

    private ListButtonNode BuildMemberRow(string name, bool hasRolled, float width)
    {
        var btn = new ListButtonNode
        {
            Size     = new Vector2(width, RowHeight),
            String   = name,
            Selected = hasRolled,
        };
        btn.LabelNode.TextColor = hasRolled
            ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
            : new Vector4(0.8f, 0.8f, 0.8f, 1f);
        return btn;
    }

    private List<List<string>> GetMemberGroups()
    {
        var result = new List<List<string>>();

        var ownGroup = new List<string>();
        foreach (var member in partyList)
        {
            if (member is null) continue;
            var name = PlayerNames.StripWorld(member.Name.TextValue);
            if (!string.IsNullOrEmpty(name))
                ownGroup.Add(name);
        }
        result.Add(ownGroup);

        if (!partyList.IsAlliance) return result;

        for (var groupStart = 0; groupStart < AllianceOtherCount; groupStart += AllianceGroupSize)
        {
            var group = new List<string>();
            for (var i = groupStart; i < groupStart + AllianceGroupSize; i++)
            {
                var addr = partyList.GetAllianceMemberAddress(i);
                if (addr == nint.Zero) continue;
                var member = partyList.CreateAllianceMemberReference(addr);
                if (member is null) continue;
                var name = PlayerNames.StripWorld(member.Name.TextValue);
                if (!string.IsNullOrEmpty(name))
                    group.Add(name);
            }
            result.Add(group);
        }

        return result;
    }
}
