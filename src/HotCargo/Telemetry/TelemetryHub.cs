using Halfling.Serialization;
using HotCargo.Telemetry;

namespace HotCargo.Core;

// Telemetry's part of the shared Tunables. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public TelemetryTunables Telemetry = new();
}
