using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.SharedChoice;

namespace HotCargo.StartJobs;

/// <summary>Spec 1204: the start job choice as a shared choice domain. The serial comes from the offered job IDs.</summary>
internal sealed class StartJobsDomain : ISharedChoiceDomain
{
    public const string Name = "startjobs";
    public static readonly StartJobsDomain Instance = new();

    string ISharedChoiceDomain.Name => Name;

    public int OptionCount(Run run, int serial) => StartJobs.Waiting(run) && serial == StartJobs.Serial(run) ? StartJobs.Offer(run).Count : 0;

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option) =>
        serial == StartJobs.Serial(run) && StartJobs.Pick(run, sim, option);

    /// <summary>The input that the panel and the test commands send for a pick.</summary>
    public static SharedChoiceInput PickInput(Run run, int option) => new(Name, StartJobs.Serial(run), SharedChoiceAction.Choose, option);
}
