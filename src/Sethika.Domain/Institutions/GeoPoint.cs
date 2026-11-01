namespace Sethika.Domain.Institutions;

/// <summary>A point in WGS 84 degrees: an institution's address, or where a donor searches from (plan 06).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>The earth's mean radius (IUGG), in km.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>
    /// Inside Sri Lanka's box, the one <c>CK_RegReq_Latitude</c> and <c>CK_RegReq_Longitude</c> hold
    /// every institution to.
    /// </summary>
    public bool InSriLanka => Latitude is >= 5.5 and <= 10.0 && Longitude is >= 79.3 and <= 82.1;

    /// <summary>The great-circle distance to <paramref name="other"/> in km, on a sphere of <see cref="EarthRadiusKm"/> (haversine).</summary>
    /// <remarks>
    /// The search's one measure of distance (G5): Redis and SQL Server only find candidates, and each
    /// measures on its own model of the earth (Redis a sphere of another radius, SQL Server the
    /// ellipsoid), which differ by up to half a percent. Judged here, an institution is inside the
    /// circle or not whichever of them found it.
    /// </remarks>
    public double DistanceKmTo(GeoPoint other)
    {
        var latitude1 = Radians(Latitude);
        var latitude2 = Radians(other.Latitude);
        var sinLatitude = Math.Sin((latitude2 - latitude1) / 2);
        var sinLongitude = Math.Sin(Radians(other.Longitude - Longitude) / 2);

        var h = (sinLatitude * sinLatitude) + (Math.Cos(latitude1) * Math.Cos(latitude2) * sinLongitude * sinLongitude);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
