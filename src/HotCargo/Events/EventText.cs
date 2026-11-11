using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Gui;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Halfling.Graphics;

namespace HotCargo.Events;

/// <summary>XML text for event results, costs and needs, in the colors of the game's reward lines.</summary>
public static class EventText
{
    static WidgetRules W => WidgetRules.Instance;

    /// <summary>FTL's blue: options that a requirement unlocks.</summary>
    public static readonly Color Unlocked = new(0.45f, 0.78f, 1f, 1f);
    public static readonly Color Heading = new(1f, 0.85f, 0.45f, 1f);

    /// <summary>ASCII signs only: the game fonts may not have the Unicode minus.</summary>
    static string Signed(int n) => n > 0 ? $"+{n:n0}" : $"-{-n:n0}";

    public static string Money(int amount) =>
        (amount > 0 ? W.MoneyTextColor : W.BadTextColor).FormatWithXml($"<img name='money' colored='true'/> {Signed(amount)}");

    public static string Fame(int amount) =>
        (amount > 0 ? W.FameTextColor : W.BadTextColor).FormatWithXml($"<img name='fame' colored='true'/> {Signed(amount)}");

    /// <summary>Heat up is bad news, heat down is good news.</summary>
    public static string Heat(float amount) =>
        (amount > 0 ? W.BadTextColor : W.GoodTextColor).FormatWithXml($"Heat {Signed((int)MathF.Round(amount))}");

    public static string Crew(int amount) =>
        (amount > 0 ? W.GoodTextColor : W.BadTextColor).FormatWithXml($"{Signed(amount)} crew");

    public static string Resource(ID<ResourceRules> id, int amount)
    {
        var r = GameApp.Rules.GetResourceRules(id);
        string name = (Math.Abs(amount) == 1 ? r.NameKey : r.GetPluralNameKey()).Text;
        return (amount > 0 ? W.GoodTextColor : W.BadTextColor).FormatWithXml($"{r.TextIconXml} {Signed(amount)} {name}");
    }

    public static string Reputation(ID<Cosmoteer.Factions.FactionRules> faction, int amount)
    {
        var f = GameApp.Rules.GetFactionRules(faction);
        return (amount > 0 ? W.GoodTextColor : W.BadTextColor).FormatWithXml($"{f.TextIconXml} {Signed(amount)} reputation with {f.NameKey.Text}");
    }

    public static string NoFaction() => "<gray>No faction controls this system.</gray>";

    public static string BuyerPrice(float multiplier) =>
        W.BadTextColor.FormatWithXml($"Buyer price now {multiplier * 100:0}% of normal");

    public static string NoVaultRoom(int units) => $"<gray>No vault room for {units} more.</gray>";

    public static string NoBunk(int crew) => $"<gray>No free bunk for {crew} more crew.</gray>";

    public static string NoStorage(ID<ResourceRules> id, int amount) =>
        $"<gray>No storage for {amount} {GameApp.Rules.GetResourceRules(id).GetPluralNameKey().Text}.</gray>";

    public static string HullDamage(int parts) => W.BadTextColor.FormatWithXml($"Hull damage to {parts} parts");

    public static string ResourceName(string id, int amount)
    {
        var r = GameApp.Rules.GetResourceRules(new ID<ResourceRules>(id));
        return $"{amount} {(amount == 1 ? r.NameKey : r.GetPluralNameKey()).Text}";
    }

    public static string PartName(string id)
    {
        var pid = new ID<PartRules>(id);
        foreach (var ship in GameApp.Rules.Ships)
            if (ship.PartsByID.TryGetValue(pid, out var part)) return part.NameKey.Text;
        return id;
    }

    /// <summary>Short reason text for a need that is not met. Shown on a greyed choice.</summary>
    public static string Reason(Unmet u) => u.Kind switch
    {
        UnmetKind.Money => $"Needs {(int)u.Amount:n0} credits",
        UnmetKind.Tech => $"Needs {(int)u.Amount} Stolen Tech in vaults",
        UnmetKind.VaultSpace => $"Needs room for {(int)u.Amount} in Tech Vaults",
        UnmetKind.Crew => $"Needs {(int)u.Amount} crew",
        UnmetKind.MinHeat => $"Needs heat {u.Amount:0} or more",
        UnmetKind.MaxHeat => $"Only below heat {u.Amount:0}",
        UnmetKind.MinSector => $"Needs sector {u.Amount:0}",
        UnmetKind.MaxSector => $"Only up to sector {u.Amount:0}",
        UnmetKind.Resource => $"Needs {ResourceName(u.Id, (int)u.Amount)}",
        UnmetKind.Part => $"Needs a {PartName(u.Id)}",
        UnmetKind.Flag or UnmetKind.NotFlag or UnmetKind.FlagAge => "Not available",
        UnmetKind.MinSectorsLeft or UnmetKind.MaxSectorsLeft => "Not available here",
        _ => throw new ArgumentOutOfRangeException(nameof(u), u.Kind, null),
    };
}
