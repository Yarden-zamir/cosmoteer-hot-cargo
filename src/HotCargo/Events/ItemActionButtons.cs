using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using Halfling.Gui.Components.Text;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Spec 1111: item action buttons in the vanilla parts card (lower left), in the same box as the part trigger and toggle buttons.
/// GUI only: a click sends the item action MP input.
/// </summary>
[HarmonyPatch(typeof(PartsCard), MethodType.Constructor, typeof(GameRoot), typeof(GameGui), typeof(IRectProvider))]
static class ItemActionButtons
{
    static void Postfix(PartsCard __instance) => Errors.Guard("item action buttons", () =>
    {
        var box = __instance._commandsBox;
        // Vanilla adds a greyed filler button last; ours go before it.
        int at = box.Children.IndexOf(__instance._extraCmdBtn);
        if (at < 0) throw new InvalidOperationException("the parts card has no filler button");
        foreach (var (item, action) in ItemActions.All()) box.Children.Insert(at++, new ItemActionButton(__instance, item, action));
    });
}

/// <summary>One action of one item. It shows while a selected part holds a unit of the item, like the vanilla trigger buttons.</summary>
sealed class ItemActionButton : ImageButton
{
    static IItemActionHandler Handlers(string name) => ItemActions.Handlers[name];

    readonly BaseCommandCard _card;
    readonly ResourceRules _item;
    readonly ItemActionRules _action;
    readonly string _baseTip;
    string _tip = "";

    void SetTip(string tip)
    {
        if (tip == _tip) return;
        _tip = tip;
        ToolTips.TextProvider = new SimpleTextProvider(tip, null);
    }

    public ItemActionButton(BaseCommandCard card, ResourceRules item, ItemActionRules action)
        : base(item.Icon, ShipGui.SHIP_COMMAND_BUTTON)
    {
        _card = card;
        _item = item;
        _action = action;
        _baseTip = action.ToolTipKey is { } t ? $"{action.NameKey.Text}\n{t.Text}" : action.NameKey.Text;
        SetTip(_baseTip);
        SelfActive = false;
        Clicked += (_, _) => Errors.Guard("item action click", OnClick);
        card.BeforeInputWhileActive((_, _) => Errors.Guard("item action button", Refresh));
    }

    /// <summary>The first selected part, by ship and part order, that holds a unit and that the local player commands.</summary>
    internal static Part? Target(GameRoot game, ResourceRules item) =>
        game.Sim.PlayerInput.SelectedParts.FirstOrDefault(p => p.Ship is { } s && s.Commands.IsCommandableByLocalPlayer && ItemActions.Holds(p, item.ID));

    void Refresh()
    {
        var target = Target(_card.Game, _item);
        SelfActive = target is not null;
        SelfInputActive = target is not null;
        if (target is not null && _action.Handler is { } h && Core.Run.Current is { } run)
            SetTip(Handlers(h).ToolTip(run, target) is { } extra ? $"{_baseTip}\n{extra}" : _baseTip);
    }

    void OnClick()
    {
        if (Target(_card.Game, _item) is not { } part) return;
        _card.Game.NetManager.EnqueueInput(new ItemActionInput(part, _item.ID, _action.ID));
    }
}
