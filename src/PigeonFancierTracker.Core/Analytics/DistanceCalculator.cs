namespace PigeonFancierTracker.Core.Analytics;

public static class DistanceCalculator
{
    private const double EarthRadiusKm = 6371.0;

    public static int HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return (int)Math.Round(EarthRadiusKm * c);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
