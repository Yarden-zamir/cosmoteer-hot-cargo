using Cosmoteer.Gui;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui;
using HotCargo.Core;
using HotCargo.SharedChoice;
using HotCargo.Ui;

namespace HotCargo.StartJobs;

/// <summary>
/// Spec 1203, 1204: the run intro panel with one button for each offered job. It stays until the players pick.
/// UI only: it reads sim state and sends shared choice inputs.
/// </summary>
internal static class StartJobPanel
{
    /// <summary>The sim can reject a sent pick (an offer change in the same tick). The buttons come back after this many seconds.</summary>
    const double ResendSeconds = 3;

    static Run? _run;
    static LayoutBox? _box;
    static LayoutBox? _buttonBox;
    static SharedChoicePanel? _shared;
    static readonly List<ImageButton> Buttons = [];
    static int _serial;
    /// <summary>The serial of an offer whose panel a test closed (closepanel). That panel does not open again in this run.</summary>
    static int _closedSerial;
    static double _sentAt = double.NegativeInfinity;

    /// <summary>For autotests: the panel is open, and the plain texts of its job buttons.</summary>
    internal static bool IsShown => _box?.Parent is not null;
    internal static IEnumerable<string> ButtonTexts => Buttons.Select(b => Events.EventView.Plain(b.Text));

    /// <summary>Called by the run intro for a new run with a waiting choice: the intro text goes on top of the jobs.</summary>
    internal static void ShowWithIntro(Run run, string title, string introBody)
    {
        Reset(run);
        Build(run, title, $"{introBody}\n\n{StartJobs.Text("Prompt")}");
    }

    /// <summary>Each UI frame. Opens the panel for a waiting choice without an intro (a loaded run, or the intro off) and closes it after the pick.</summary>
    internal static void Update(Run run)
    {
        if (_run != run) Reset(run);
        if (!StartJobs.Waiting(run))
        {
            if (_box is not null) CenterPanel.Untrack(run.Game, _box);
            _box = null;
            return;
        }
        int serial = StartJobs.Serial(run);
        if (_box is { Parent: null })
        {
            _closedSerial = _serial;
            _box = null;
        }
        if (_box is null)
        {
            if (Ui.RunEndUi.IntroPending == run || _closedSerial == serial) return;
            Build(run, StartJobs.Text("Title"), StartJobs.Text("Prompt"));
        }
        Refresh(run, serial);
    }

    static void Reset(Run run)
    {
        _run = run;
        _box = null;
        _closedSerial = 0;
        _serial = 0;
        Buttons.Clear();
    }

    static void Build(Run run, string title, string body)
    {
        var box = _box = CenterPanel.NewBox(760f);
        box.Children.WidgetPadding = new Vector2(16f);
        var titleLabel = CenterPanel.Text(title, 44, center: true);
        titleLabel.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        titleLabel.TextRenderer.Color = WidgetRules.Instance.FameTextColor;
        box.AddChild(titleLabel);
        box.AddChild(CenterPanel.Text(body, 18, center: false));
        _shared = new SharedChoicePanel(run, StartJobsDomain.Name);
        box.AddChild(_shared.Bar);
        var buttons = _buttonBox = new LayoutBox();
        buttons.NineSlice.Flags = NineSliceFlags.None;
        buttons.PercentileWidth = 100f;
        buttons.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        buttons.Children.WidgetPadding = new Vector2(10f);
        buttons.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        buttons.Children.ClipChildren = false;
        box.AddChild(buttons);
        Buttons.Clear();
        _serial = 0;
        CenterPanel.Track(run.Game, box);
        Refresh(run, StartJobs.Serial(run));
    }

    static void Refresh(Run run, int serial)
    {
        if (_buttonBox is null || _shared is null) throw new InvalidOperationException("start job panel without its widgets");
        var offer = StartJobs.Offer(run);
        if (serial != _serial)
        {
            _serial = serial;
            _sentAt = double.NegativeInfinity;
            foreach (var b in Buttons) _buttonBox.Children.Remove(b);
            Buttons.Clear();
            for (int i = 0; i < offer.Count; i++)
            {
                var b = ChoiceButton.New();
                int option = i;
                b.Clicked += (_, _) => Errors.Guard("start job click", () => Click(option));
                b.Text = ButtonText(run, offer[i]);
                Buttons.Add(b);
                _buttonBox.AddChild(b);
            }
        }
        bool locked = App.Clock.Time.Seconds < _sentAt + ResendSeconds;
        foreach (var b in Buttons) b.SelfInputActive = !locked;
        _shared.Update(serial, [.. Buttons.Select((b, i) => new OptionSlot(b, i))]);
    }

    /// <summary>Spec 1202: the name, the short text, and every rule of the job.</summary>
    static string ButtonText(Run run, StartJobRow job) =>
        $"<b>{StartJobs.Name(job)}</b>   <gray>{StartJobs.Blurb(job)}</gray>\n<fame_color>{string.Join("   ", StartJobs.RuleTexts(run, job))}</fame_color>";

    /// <summary>Autotests: a click on the button of the option, like a player.</summary>
    internal static void ClickForTest(int option)
    {
        if (!IsShown) throw new InvalidOperationException("the start job panel is not open");
        Click(option);
    }

    static void Click(int option)
    {
        if (_shared is null || App.Clock.Time.Seconds < _sentAt + ResendSeconds) return;
        if (_shared.Click(_serial, option)) _sentAt = App.Clock.Time.Seconds;
    }
}
