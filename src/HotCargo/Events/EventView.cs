using Cosmoteer.Simulation;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>One button on an event page. Index is the choice index in the rules, or -1 for "Continue" on a result page.</summary>
public sealed record ChoiceView(int Index, string Text, bool Enabled);

/// <summary>What the dialog shows. Built from sim state without widgets, so autotests check it headless.</summary>
public sealed record EventPage(string Key, int Serial, string Title, string Body, IReadOnlyList<ChoiceView> Choices);

public static class EventView
{
    /// <summary>The page to show now: the pending event, else an unseen result, else null.</summary>
    public static EventPage? Current(Run run, SimRoot sim, int localPlayer, int lastResultShown)
    {
        var s = run.State.Events;
        if (s.Pending is { } p) return Pending(run, sim, p, localPlayer);
        if (s.LastResult is { } r && r.Serial > lastResultShown) return Result(r);
        return null;
    }

    public static EventPage Result(EventResult r)
    {
        string body = r.Lines.Length > 0 ? $"{r.Text}\n\n{string.Join("\n", r.Lines)}" : r.Text;
        return new EventPage($"result {r.Serial}", r.Serial, r.Title, body, [new ChoiceView(-1, "Continue.", true)]);
    }

    public static EventPage Pending(Run run, SimRoot sim, PendingEvent p, int localPlayer)
    {
        var e = EventLibrary.Get(p.EventId);
        int money = run.Game.GetPlayerState(localPlayer, deterministic: false).Money;
        var snapshot = EventRunner.Snap(run, sim, money, p.Tier, p.TechValue);
        var choices = new List<ChoiceView>();
        for (int i = 0; i < e.Choices.Length; i++)
        {
            var c = e.Choices[i];
            var unmet = EventModel.FirstUnmet(EventRunner.ChoiceNeed(run, c, p), snapshot);
            if (unmet is not null && c.HideIfUnmet) continue;
            choices.Add(new ChoiceView(i, ChoiceText(run, c, p, unmet), unmet is null));
        }
        string body = EventText.Fill(p.Lead.Length > 0 ? $"{p.Lead}\n\n{e.Text}" : e.Text, run.State.FleetTech);
        return new EventPage($"event {p.Serial}", p.Serial, e.Title, body, choices);
    }

    /// <summary>"2. [Sensor Array] Scan it first. (-5,000)" with the requirement tag in FTL blue, or all grey with the reason.</summary>
    /// <summary>No number prefix: the UX area draws the key numbers next to the buttons (Ux/DialogueKeys).</summary>
    static string ChoiceText(Run run, ChoiceRules c, PendingEvent p, Unmet? unmet)
    {
        string tag = RequirementTag(c.Need);
        string cost = CostText(run, c.Cost, p);
        if (unmet is not null)
            return $"<gray>{(tag.Length > 0 ? $"[{tag}] " : "")}{EventText.Fill(c.Text, run.State.FleetTech)}{(cost.Length > 0 ? $" ({cost})" : "")}  - {EventText.Reason(unmet)}</gray>";
        string tagXml = tag.Length > 0 ? EventText.Unlocked.FormatWithXml($"[{tag}]") + " " : "";
        string costXml = cost.Length > 0 ? $" <gray>({cost})</gray>" : "";
        return $"{tagXml}{EventText.Fill(c.Text, run.State.FleetTech)}{costXml}";
    }

    /// <summary>
    /// The visible requirements of a choice. Heat, sector and flag needs stay hidden, like story conditions in FTL.
    /// Tech needs stay hidden too: in events.rules they only keep the last crate safe, next to a tech Cost that the button shows.
    /// </summary>
    static string RequirementTag(NeedRules? n)
    {
        if (n is null) return "";
        var parts = new List<string>();
        parts.AddRange(n.Parts.Select(EventText.PartName));
        if (n.Crew > 0) parts.Add($"{n.Crew} crew");
        if (n.VaultSpace > 0) parts.Add($"vault room {n.VaultSpace}");
        parts.AddRange(n.Resources.Select(r => EventText.ResourceName(r.ID, r.Amount)));
        return string.Join(", ", parts);
    }

    static string CostText(Run run, CostRules? cost, PendingEvent p)
    {
        if (cost is null) return "";
        var parts = new List<string>();
        int money = EventRunner.CostCredits(run, cost, p);
        if (money > 0) parts.Add($"-{money:n0} credits");
        if (cost.Tech > 0) parts.Add($"-{cost.Tech} Stolen Tech");
        if (cost.Crew > 0) parts.Add($"-{cost.Crew} crew");
        parts.AddRange(cost.Resources.Select(r => $"-{EventText.ResourceName(r.ID, r.At(p.Tier))}"));
        return string.Join(", ", parts);
    }

    /// <summary>Plain text of a page for the log: XML tags removed.</summary>
    public static string Plain(string xml)
    {
        var sb = new System.Text.StringBuilder(xml.Length);
        bool inTag = false;
        foreach (char ch in xml)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }
        return sb.ToString();
    }
}
