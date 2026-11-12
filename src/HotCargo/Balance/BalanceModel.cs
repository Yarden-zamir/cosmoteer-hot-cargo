namespace HotCargo.Balance;

/// <summary>Pure balance math on plain numbers. No game types, so unit tests run on the Mac.</summary>
public static class BalanceModel
{
    /// <summary>
    /// Spec 304: the fuel that every station keeps for sale. It is enough for one relay jump of about 10 LY for a fleet worth
    /// shipValue (the ship value maximum of the next tier): a ship carries about 3 mass per 1,000 credits of value, and a jump costs
    /// 2 fuel per 100 mass per 10 LY (spec 300) at about 70 % drive efficiency, so about 90 fuel per million of value.
    /// </summary>
    public static int StationFuelFloor(int shipValue, float fuelPerMillion, int minimum)
    {
        if (shipValue <= 0) throw new ArgumentOutOfRangeException(nameof(shipValue), "must be above 0");
        return Math.Max(minimum, (int)Math.Ceiling(shipValue / 1_000_000.0 * Math.Max(0f, fuelPerMillion)));
    }

    /// <summary>
    /// Spec 304: a trade between a trade ship and a station never takes the station's fuel below the floor.
    /// delta is the vanilla trade delta for the station (negative = the station gives fuel). Returns the clamped delta.
    /// </summary>
    public static int ClampStationFuelDelta(int delta, int stationFuel, int floor) =>
        delta >= 0 ? delta : Math.Max(delta, Math.Min(0, floor - stationFuel));
}
