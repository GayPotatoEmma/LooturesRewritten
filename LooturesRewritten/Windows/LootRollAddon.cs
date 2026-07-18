using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using LooturesRewritten.Services;

namespace LooturesRewritten.Windows;

public unsafe class LootRollAddon : NativeAddon
{
    private const float LeftWidth        = 220f;
    private const float RightWidthParty  = 200f;
    private const float RightWidth       = 200f;
    private const float ColumnGap        = 6f;
    private const float RowHeight        = 22f;
    private const float HeaderHeight     = 24f;
    private const float WindowPadding    = 16f;
    private const int   MaxItemRows      = 8;
    private const int   MaxMemberRows    = 8;
    private const float ItemSectionH     = MaxItemRows * RowHeight;
    private const float MemberSectionH   = MaxMemberRows * RowHeight;
    private const float ContentHeight    = 200f;
    private const float TabHeight        = 24f;

    private const float ContentWidth     = LeftWidth + ColumnGap + RightWidth;
    private const float TotalWindowWidth = ContentWidth + WindowPadding;
    private const float TotalWindowHeight = ContentHeight + HeaderHeight + TabHeight + 53f;

    private const float ColSpacing       = 6f;
    private const float ColWidth         = (RightWidth - ColSpacing * 2f) / 3f;
    private const int   AllianceGroupSize = 8;
    private const int   AllianceOtherCount = 16;

    private readonly LootTracker lootTracker;
    private readonly IPartyList  partyList;

    private TextNode?               headerNode;
    private TabBarNode?             allianceTabsNode;
    private VerticalListNode            itemListNode  = null!;
    private readonly VerticalListNode[] memberColumns = new VerticalListNode[3];

    private int?            selectedSlot;
    private ListButtonNode? selectedItemButton;
    private int             selectedAllianceGroup = 0;
    private bool            lastIsAlliance;
    private bool            setupComplete;

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

        headerNode = new TextNode
        {
            Position = s,
            Size = new Vector2(ContentWidth, HeaderHeight),
            String = "No item selected",
            TextColor = new Vector4(1f, 1f, 1f, 1f),
            TextOutlineColor = new Vector4(0f, 0f, 0f, 0.8f),
            FontSize = 12,
            AlignmentType = AlignmentType.Center,
        };
        headerNode.AttachNode(this);

        allianceTabsNode = new TabBarNode
        {
            Position = s + new Vector2(LeftWidth + ColumnGap, HeaderHeight),
            Size = new Vector2(RightWidth, TabHeight),
        };
        allianceTabsNode.AddTab("A", () => {
            selectedAllianceGroup = 0;
            RebuildMemberList(selectedSlot);
        });
        allianceTabsNode.AddTab("B", () => {
            selectedAllianceGroup = 1;
            RebuildMemberList(selectedSlot);
        });
        allianceTabsNode.AddTab("C", () => {
            selectedAllianceGroup = 2;
            RebuildMemberList(selectedSlot);
        });
        allianceTabsNode.IsVisible = false;
        allianceTabsNode.AttachNode(this);

        itemListNode = new VerticalListNode
        {
            Position         = s + new Vector2(0f, HeaderHeight),
            Size             = new Vector2(LeftWidth, ItemSectionH),
            ItemSpacing      = 0f,
            FitWidth         = true,
            ClipListContents = true,
        };
        itemListNode.AttachNode(this);

        memberColumns[0] = new VerticalListNode
        {
            Position         = s + new Vector2(LeftWidth + ColumnGap, HeaderHeight),
            Size             = new Vector2(RightWidth, MemberSectionH),
            ItemSpacing      = 0f,
            FitWidth         = true,
            ClipListContents = true,
        };
        memberColumns[0].AttachNode(this);

        for (var i = 1; i < 3; i++)
        {
            memberColumns[i] = new VerticalListNode();
        }

        RebuildItemList();
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
        if (!setupComplete) return;
        var isAlliance = partyList.IsAlliance;
        if (isAlliance == lastIsAlliance) return;
        lastIsAlliance = isAlliance;
        SetWindowSize(new Vector2(TotalWindowWidth, TotalWindowHeight));
        ApplyAlliance(isAlliance);
    }

    private void ApplyAlliance(bool isAlliance)
    {
        if (memberColumns[0] is null || allianceTabsNode is null) return;
        allianceTabsNode.IsVisible = isAlliance;

        var s = ContentStartPosition;
        var memberListY = isAlliance ? HeaderHeight + TabHeight : HeaderHeight;
        memberColumns[0].Position = s + new Vector2(LeftWidth + ColumnGap, memberListY);
        memberColumns[0].Size = new Vector2(RightWidth, MemberSectionH);

        selectedAllianceGroup = 0;
        RebuildMemberList(selectedSlot);
    }

    private void OnTrackerUpdated()
    {
        if (!setupComplete || InternalAddon is null) return;

        var currentItemCount = lootTracker.Items.Count;
        if (itemListNode.Nodes.Count != currentItemCount)
        {
            RebuildItemList();
        }

        if (selectedSlot is not null)
        {
            if (!lootTracker.Rolls.ContainsKey(selectedSlot.Value) || lootTracker.Rolls[selectedSlot.Value].Count == 0)
            {
                selectedSlot = null;
                selectedItemButton = null;
                if (headerNode is not null)
                    headerNode.String = "No item selected";
                RebuildMemberList(null);
            }
            else
            {
                RebuildMemberList(selectedSlot.Value);
            }
        }
    }

    private void RebuildItemList()
    {
        selectedItemButton = null;
        try
        {
            foreach (var node in new List<NodeBase>(itemListNode.Nodes))
                itemListNode.RemoveNode(node);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[LootRollAddon] Error clearing item list: {ex.Message}");
        }

        foreach (var item in lootTracker.Items)
        {
            var captured = item;
            var btn = new ListButtonNode
            {
                Size     = new Vector2(LeftWidth, RowHeight),
                String   = item.Name,
                Selected = item.SlotIndex == selectedSlot,
            };
            btn.OnClick = () => SelectItem(captured, btn);

            if (item.SlotIndex == selectedSlot)
                selectedItemButton = btn;

            itemListNode.AddNode(btn);
        }
    }

    private void SelectItem(LootItem item, ListButtonNode btn)
    {
        if (selectedItemButton is not null)
            selectedItemButton.Selected = false;
        selectedItemButton = btn;
        btn.Selected = true;

        selectedSlot = item.SlotIndex;
        if (headerNode is not null)
            headerNode.String = item.Name;

        RebuildItemList();

        lootTracker.SetSelectedSlot(item.SlotIndex, () =>
        {
            selectedSlot       = null;
            selectedItemButton = null;
            if (headerNode is not null)
                headerNode.String = "No item selected";
            RebuildMemberList(null);
        });
        RebuildMemberList(item.SlotIndex);
    }

    private void RebuildMemberList(int? slot)
    {
        HashSet<string> rollers = [];
        if (slot is not null)
            lootTracker.Rolls.TryGetValue(slot.Value, out rollers!);
        rollers ??= [];

        var groups = GetMemberGroups();

        try
        {
            foreach (var node in new List<NodeBase>(memberColumns[0].Nodes))
                memberColumns[0].RemoveNode(node);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[LootRollAddon] Error clearing member list: {ex.Message}");
        }

        if (slot is null) return;

        var groupIndex = partyList.IsAlliance ? selectedAllianceGroup : 0;
        if (groupIndex >= groups.Count) return;

        foreach (var name in groups[groupIndex])
            memberColumns[0].AddNode(BuildMemberRow(name, rollers.Contains(name), RightWidth));
    }

    private NodeBase BuildMemberRow(string name, bool hasRolled, float width)
    {
        const float iconSize = 16f;
        const float iconSpacing = 2f;

        var container = new HorizontalListNode
        {
            Size = new Vector2(width, RowHeight),
            ItemSpacing = iconSpacing,
            FitHeight = true,
        };

        var icon = new SimpleImageNode
        {
            Size = new Vector2(iconSize, iconSize),
            TexturePath = "ui/uld/readycheck_hr1.tex",
            TextureCoordinates = new Vector2(0f, 0f),
            TextureSize = new Vector2(24f, 24f),
            Alpha = hasRolled ? 1f : 0f,
        };
        container.AddNode(icon);

        var btn = new ListButtonNode
        {
            Size     = new Vector2(width - iconSize - iconSpacing, RowHeight),
            String   = name,
            Selected = false,
        };
        btn.LabelNode.TextColor = new Vector4(0.8f, 0.8f, 0.8f, 1f);
        btn.LabelNode.Size = new Vector2(btn.Width - 4f, RowHeight - 2f);
        container.AddNode(btn);

        return container;
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
