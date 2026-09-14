using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

/// <summary>
/// Editable grid row wrapping a <see cref="CompletedFlightSummary"/>. Location and
/// DistanceKm are two-way bound so the user can manually correct a flight whose
/// synced values are wrong; the corrected distance re-classifies its category live.
/// Everything else is a read-only passthrough used by the detail panel.
/// </summary>
public sealed class CompletedFlightRow
{
    public CompletedFlightSummary Summary { get; }

    public int FlightId => Summary.FlightId;
    public System.DateTime FlightDate => Summary.FlightDate;
    public string FlightType => Summary.FlightType;
    public int BestPosition => Summary.BestPosition;
    public int TotalParticipants => Summary.TotalParticipants;
    public int OwnPigeonCount => Summary.OwnPigeonCount;
    public int TotalPoints => Summary.TotalPoints;

    public string? Location { get; set; }
    public int DistanceKm { get; set; }

    /// <summary>Category derived live from the (possibly edited) distance.</summary>
    public DistanceCategory Category => DistanceProfileCalculator.Classify(DistanceKm);

    public CompletedFlightRow(CompletedFlightSummary summary)
    {
        Summary = summary;
        Location = summary.Location;
        DistanceKm = summary.DistanceKm;
    }
}
