using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Gui;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Halfling.Network;
using HotCargo.Core;

namespace HotCargo.SharedChoice;

/// <summary>One option button of a dialog and the domain option that it stands for.</summary>
public readonly record struct OptionSlot(ImageButton Button, int Option);

/// <summary>
/// The co-op layer of a dialog with one button per option (spec 601 to 606). A dialog creates one panel, adds Bar above its
/// buttons, calls Update each frame and sends clicks through Click. In single player the bar stays hidden and Click only chooses.
/// UI only: it reads sim state, sends SharedChoiceInput and reports hovers to Presence.
/// </summary>
public sealed class SharedChoicePanel
{
    /// <summary>Marker colors of the players, by their place in the voter order, so every machine shows the same color.</summary>
    static readonly Color[] PlayerColors =
    [
        new(0.35f, 0.75f, 1f), new(1f, 0.6f, 0.2f), new(0.55f, 1f, 0.45f), new(1f, 0.45f, 0.75f),
        new(0.85f, 0.75f, 1f), new(1f, 0.95f, 0.4f), new(0.4f, 1f, 0.9f), new(1f, 0.4f, 0.4f),
    ];
    /// <summary>Room on the right of each option button for the markers, in co-op.</summary>
    const float MarkerWidth = 230f;

    sealed class Marker(WidgetTextRenderer text)
    {
        public readonly WidgetTextRenderer Text = text;
        public Color? Highlight;
    }

    static readonly ConditionalWeakTable<ImageButton, Marker> Markers = new();

    readonly Run _run;
    readonly string _domain;
    readonly ImageButton _mode;
    readonly ImageButton _force;
    readonly Label _status;
    int _serial;

    /// <summary>The row with the mode toggle, the host's "decide now" button and the vote status. Add it to the dialog.</summary>
    public LayoutBox Bar { get; }

    public SharedChoicePanel(Run run, string domain)
    {
        _run = run;
        _domain = domain;
        SharedChoices.Domain(domain);
        Bar = new LayoutBox();
        Bar.NineSlice.Flags = NineSliceFlags.None;
        Bar.PercentileWidth = 100f;
        Bar.Height = 34f;
        Bar.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftHorizontal;
        Bar.Children.BorderPadding = new Borders(30f, 0f, 0f, 0f);
        Bar.Children.WidgetPadding = new Vector2(10f);
        _mode = BarButton(170f, () => _run.Game.NetManager.EnqueueInput(SharedChoiceInput.SetVoteMode(!SharedChoices.Voting(_run))));
        _force = BarButton(130f, () => Send(SharedChoiceAction.Force, 0));
        _force.Text = SharedChoices.Text("DecideNow");
        _status = new Label { InputBlockMode = InputBlockMode.Never };
        _status.NineSlice.Flags = NineSliceFlags.None;
        _status.Size = new Vector2(320f, 34f);
        _status.TextRenderer.XmlFormatting = true;
        _status.TextRenderer.FontSize = 16;
        _status.TextRenderer.HAlignment = HAlignment.Left;
        _status.TextRenderer.VAlignment = VAlignment.Center;
        Bar.Children.Add(_mode);
        Bar.Children.Add(_force);
        Bar.Children.Add(_status);
        Bar.SelfActive = run.IsMultiplayer;
    }

    ImageButton BarButton(float width, Action onClick)
    {
        var b = new ImageButton();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        b.Size = new Vector2(width, 34f);
        foreach (var r in TextRenderers(b)) r.FontSize = 16;
        b.Clicked += (_, _) => onClick();
        return b;
    }

    /// <summary>The local vote on the page, or -1.</summary>
    public int LocalVote => Box()?.Votes.FirstOrDefault(v => v.Player == _run.Game.NetManager.LocalPlayerID)?.Option ?? -1;

    /// <summary>
    /// A click on an option. First pick mode: sends the choice and returns true, so the dialog locks its buttons until the page
    /// changes. Vote mode: casts the vote, or takes it back on a second click, and returns false.
    /// </summary>
    public bool Click(int serial, int option)
    {
        _serial = serial;
        if (!SharedChoices.Voting(_run))
        {
            Send(SharedChoiceAction.Choose, option);
            return true;
        }
        Send(LocalVote == option ? SharedChoiceAction.Unvote : SharedChoiceAction.Vote, option);
        return false;
    }

    void Send(SharedChoiceAction action, int option) =>
        _run.Game.NetManager.EnqueueInput(new SharedChoiceInput(_domain, _serial, action, option));

    VoteBox? Box() => _run.State.SharedChoice.Box(_domain, _serial);

    /// <summary>Call each frame while the page shows. Reports the local hover and draws the hovers and votes of all players.</summary>
    public void Update(int serial, IReadOnlyList<OptionSlot> slots)
    {
        _serial = serial;
        var hovered = slots.FirstOrDefault(s => s.Button.IsMouseOver && s.Button.IsInputActive);
        Presence.Report(_domain, serial, hovered.Button is null ? -1 : hovered.Option);
        Bar.SelfActive = _run.IsMultiplayer;
        if (!_run.IsMultiplayer) return;

        bool voting = SharedChoices.Voting(_run);
        var voters = SharedChoices.Electorate(_run);
        var box = voting ? Box() : null;
        var hovers = Presence.Others(_run, _domain, serial);
        _mode.Text = SharedChoices.Text(voting ? "ModeVote" : "ModeFirstPick");
        _force.SelfActive = voting && box is { Votes.Count: > 0 } && _run.Game.NetManager.IsHost;
        _status.Text = Status(voting, box, voters.Count);

        foreach (var slot in slots)
        {
            var marker = MarkerOf(slot.Button);
            var names = hovers.Where(h => h.Option == slot.Option).Select(h => Colored(voters, h.Player, $"[{SharedChoices.PlayerName(_run, h.Player)}]"));
            var votes = box?.Votes.Where(v => v.Option == slot.Option).Select(v => Colored(voters, v.Player, SharedChoices.PlayerName(_run, v.Player))).ToList() ?? [];
            string voteText = votes.Count > 0 ? SharedChoices.Format("VotesFmt", votes.Count, string.Join(", ", votes)) : "";
            marker.Text.Text = string.Join("\n", new[] { string.Join(" ", names), voteText }.Where(t => t.Length > 0));
            var first = hovers.FirstOrDefault(h => h.Option == slot.Option);
            marker.Highlight = first is null ? null : ColorOf(voters, first.Player);
            foreach (var r in TextRenderers(slot.Button))
                if (r.Padding.Right < MarkerWidth) r.Padding = r.Padding with { Right = MarkerWidth };
        }
    }

    /// <summary>Call each frame while the dialog shows a page without shared options (a result page). Clears the markers on its buttons.</summary>
    public void Hide(IEnumerable<ImageButton> buttons)
    {
        Bar.SelfActive = false;
        foreach (var b in buttons)
        {
            if (!Markers.TryGetValue(b, out var m)) continue;
            m.Text.Text = "";
            m.Highlight = null;
        }
    }

    string Status(bool voting, VoteBox? box, int voterCount)
    {
        if (!voting) return SharedChoices.Text("StatusFirstPick");
        int votes = box?.Votes.Count ?? 0;
        float timeout = _run.T.SharedChoice.VoteTimeoutSeconds;
        if (box is null || box.FirstVoteSeconds < 0 || timeout <= 0) return SharedChoices.Format("StatusVoteFmt", votes, voterCount);
        double left = Math.Max(0, timeout - (_run.State.GameSeconds - box.FirstVoteSeconds));
        return SharedChoices.Format("StatusVoteTimerFmt", votes, voterCount, Math.Ceiling(left));
    }

    static Color ColorOf(IReadOnlyList<MessengerID> voters, MessengerID player)
    {
        int i = voters.ToList().IndexOf(player);
        return PlayerColors[(i < 0 ? 0 : i) % PlayerColors.Length];
    }

    static string Colored(IReadOnlyList<MessengerID> voters, MessengerID player, string text) => ColorOf(voters, player).FormatWithXml(text);

    static WidgetTextRenderer[] TextRenderers(ImageButton b) =>
        [b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer];

    /// <summary>The marker of a button: a right-aligned text and a colored frame, both drawn on top of the button.</summary>
    static Marker MarkerOf(ImageButton b)
    {
        if (Markers.TryGetValue(b, out var m)) return m;
        var text = new WidgetTextRenderer(b)
        {
            Font = WidgetRules.Instance.ShadowFont,
            FontSize = 15,
            XmlFormatting = true,
            HAlignment = HAlignment.Right,
            VAlignment = VAlignment.Center,
            OversizeMode = OversizeMode.Wrap,
            Padding = new Borders(0f, 4f, 12f, 4f),
        };
        m = new Marker(text);
        var marker = m;
        b.Components.Add(new WidgetDelegatedRenderer((gfx, _, r, colorScale) => DrawHighlight(gfx, r, colorScale, marker), b));
        b.Components.Add(text);
        Markers.AddOrUpdate(b, m);
        return m;
    }

    static void DrawHighlight(GraphicsManager gfx, Rect r, Color colorScale, Marker m)
    {
        if (m.Highlight is not { } c) return;
        const float w = 3f;
        gfx.SetToMaterial(null);
        gfx.Draw<Quad>(new Quad(r, new Color(c.R, c.G, c.B, 0.12f) * colorScale));
        var edge = c * colorScale;
        gfx.Draw<Quad>(new Quad(new Rect(r.X, r.Y, r.Width, w), edge));
        gfx.Draw<Quad>(new Quad(new Rect(r.X, r.Bottom - w, r.Width, w), edge));
        gfx.Draw<Quad>(new Quad(new Rect(r.X, r.Y, w, r.Height), edge));
        gfx.Draw<Quad>(new Quad(new Rect(r.Right - w, r.Y, w, r.Height), edge));
    }
}
