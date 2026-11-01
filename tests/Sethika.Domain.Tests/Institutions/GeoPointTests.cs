using Sethika.Domain.Institutions;

namespace Sethika.Domain.Tests.Institutions;

/// <summary>Plan 06, G5: the one measure of distance, and the box a searched point must be in.</summary>
public class GeoPointTests
{
    /// <summary>One degree along a great circle: 2πR / 360.</summary>
    private const double OneDegreeKm = 2 * Math.PI * GeoPoint.EarthRadiusKm / 360;

    [Fact]
    public void ADegreeOfLatitudeIsADegreeOfTheGreatCircle()
    {
        Assert.Equal(OneDegreeKm, new GeoPoint(6, 80).DistanceKmTo(new GeoPoint(7, 80)), 6);
        Assert.Equal(111.195, OneDegreeKm, 3);
    }

    [Fact]
    public void ADegreeOfLongitudeShrinksWithTheCosineOfTheLatitude()
    {
        Assert.Equal(OneDegreeKm, new GeoPoint(0, 80).DistanceKmTo(new GeoPoint(0, 81)), 6);

        // At 60° a parallel is half the equator; over one degree the great circle is a hair shorter.
        var atSixty = new GeoPoint(60, 80).DistanceKmTo(new GeoPoint(60, 81));
        Assert.InRange(atSixty, (OneDegreeKm / 2) - 0.01, OneDegreeKm / 2);
    }

    [Fact]
    public void TheDistanceIsTheSameBothWays_AndNothingToItself()
    {
        var colombo = new GeoPoint(6.9271, 79.8612);
        var galle = new GeoPoint(6.0329, 80.2168);

        Assert.Equal(colombo.DistanceKmTo(galle), galle.DistanceKmTo(colombo), 9);
        Assert.Equal(0, colombo.DistanceKmTo(colombo), 9);

        // Colombo to Galle as the crow flies, about 107 km.
        Assert.InRange(colombo.DistanceKmTo(galle), 106, 108);
    }

    [Theory]
    [InlineData(6.9271, 79.8612, true)]
    [InlineData(5.5, 79.3, true)]
    [InlineData(10.0, 82.1, true)]
    [InlineData(5.49, 80.0, false)]
    [InlineData(10.01, 80.0, false)]
    [InlineData(7.0, 79.29, false)]
    [InlineData(7.0, 82.11, false)]
    [InlineData(0, 0, false)]
    public void OnlyAPointInSriLankasBoxIsInSriLanka(double latitude, double longitude, bool expected) =>
        Assert.Equal(expected, new GeoPoint(latitude, longitude).InSriLanka);
}
