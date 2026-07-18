using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace LooturesRewritten.Windows;

public sealed unsafe class NeedOrGreedOverlay : IDisposable
{
    private readonly LootRollAddon   lootRollAddon;
    private          TextButtonNode?  button;
    private          bool injected;

    public NeedOrGreedOverlay(LootRollAddon lootRollAddon)
    {
        this.lootRollAddon = lootRollAddon;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "NeedGreed", OnSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "NeedGreed", OnDraw);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "NeedGreed", OnFinalize);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup,   "NeedGreed", OnSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw,    "NeedGreed", OnDraw);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "NeedGreed", OnFinalize);
        DestroyButton();
    }

    private void OnSetup(AddonEvent type, AddonArgs args)
    {
        injected = false;
        TryInject();
    }

    private void OnDraw(AddonEvent type, AddonArgs args)
    {
        if (injected) return;
        TryInject();
    }

    private void OnFinalize(AddonEvent type, AddonArgs args)
    {
        Plugin.Log.Debug("[NeedOrGreedOverlay] PreFinalize — removing button");
        injected = false;
        DestroyButton();
    }

    private void TryInject()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("NeedGreed");
        if (addon is null || addon->RootNode is null) return;

        var closeButton = FindCloseButton(addon->RootNode);
        if (closeButton is null)
        {
            Plugin.Log.Warning("[NeedOrGreedOverlay] Could not find close button");
            return;
        }

        Plugin.Log.Debug($"[NeedOrGreedOverlay] Close btn: x={closeButton->X} y={closeButton->Y} w={closeButton->Width} h={closeButton->Height}, root w={addon->RootNode->Width}");

        DestroyButton();

        const float buttonH = 20f;
        const float buttonW = 46f;
        var posX = closeButton->X > 0 && closeButton->X < 2000 ? closeButton->X - buttonW - 4f : addon->RootNode->Width - buttonW - 28f;
        var posY = closeButton->Y > 0 && closeButton->Y < 500  ? (float)closeButton->Y : 4f;

        button = new TextButtonNode
        {
            Size        = new System.Numerics.Vector2(buttonW, buttonH),
            String      = "Rolls",
            Position    = new System.Numerics.Vector2(posX, posY),
            TextTooltip = new ReadOnlySeString("[Lootures Rewritten] Show loot rolls window"),
        };
        button.OnClick = () =>
        {
            if (lootRollAddon.IsOpen)
                lootRollAddon.Close();
            else
                lootRollAddon.Open();
        };

        button.AttachNode(closeButton, NodePosition.BeforeTarget);
        addon->UldManager.UpdateDrawNodeList();
        addon->UpdateCollisionNodeList(false);

        injected = true;
        Plugin.Log.Debug("[NeedOrGreedOverlay] Button injected successfully");
    }

    private void DestroyButton()
    {
        button?.Dispose();
        button = null;
    }

    private static AtkResNode* FindCloseButton(AtkResNode* rootNode)
    {
        if (rootNode is null) return null;

        var node = rootNode->ChildNode;
        AtkResNode* best = null;
        while (node is not null)
        {
            if ((uint)node->Type >= 1000)
            {
                if (best is null || node->X > best->X)
                    best = node;
            }
            node = node->PrevSiblingNode;
        }
        return best;
    }
}
