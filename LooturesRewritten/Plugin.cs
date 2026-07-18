using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using KamiToolKit;
using LooturesRewritten.Services;
using LooturesRewritten.Windows;
using Lumina.Text.ReadOnly;

namespace LooturesRewritten;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/lootures";
    private const string ClearCommandName = "/lootclear";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("LooturesRewritten");

    private LootTracker LootTracker { get; init; }
    private RollChatListener RollChatListener { get; init; }
    private LootRollAddon LootRollAddon { get; init; }
    private NeedOrGreedOverlay NeedOrGreedOverlay { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        KamiToolKitLibrary.Initialize(PluginInterface, "Lootures Rewritten");

        LootTracker = new LootTracker(Framework, PartyList, AddonLifecycle);
        RollChatListener = new RollChatListener(ChatGui, PartyList, LootTracker);

        LootRollAddon = new LootRollAddon(LootTracker, PartyList)
        {
            InternalName = "LooturesRollWindow",
            Title = new ReadOnlySeString("Loot Rolls"),
        };

        NeedOrGreedOverlay = new NeedOrGreedOverlay(LootRollAddon);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Lootures loot roll window"
        });

        CommandManager.AddHandler(ClearCommandName, new CommandInfo(OnClearCommand)
        {
            HelpMessage = "Clear all tracked loot rolls"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleLootWindow;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleLootWindow;

        WindowSystem.RemoveAllWindows();

        if (LootRollAddon.IsOpen)
            LootRollAddon.Close();
        NeedOrGreedOverlay.Dispose();
        RollChatListener.Dispose();
        LootTracker.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ClearCommandName);

        KamiToolKitLibrary.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleLootWindow();

    private void OnClearCommand(string command, string args) => LootTracker.Clear();

    public void ToggleLootWindow()
    {
        if (LootRollAddon.IsOpen)
            LootRollAddon.Close();
        else
            LootRollAddon.Open();
    }
}

