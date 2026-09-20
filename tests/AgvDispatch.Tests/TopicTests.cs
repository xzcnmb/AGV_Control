namespace AgvDispatch.Tests;

using AgvDispatch.Vda5050.Topics;

public class TopicTests
{
    [Fact]
    public void Constants_MatchInterfaceProfileContract()
    {
        Assert.Equal("uagv", Vda5050Topic.InterfaceName);
        Assert.Equal("v2", Vda5050Topic.MajorVersion);
        Assert.Equal("connection", Vda5050Topic.Connection);
        Assert.Equal("factsheet", Vda5050Topic.Factsheet);
        Assert.Equal("state", Vda5050Topic.State);
        Assert.Equal("order", Vda5050Topic.Order);
        Assert.Equal("instantActions", Vda5050Topic.InstantActions);
        Assert.Equal("visualization", Vda5050Topic.Visualization);
        Assert.Equal("uagv/v2/#", Vda5050Topic.Wildcard);
    }

    [Fact]
    public void Build_ConstructsCorrectVda5050Topic()
    {
        var topic = Vda5050Topic.Build("AgvSim", "AGV0001", Vda5050Topic.State);
        Assert.Equal("uagv/v2/AgvSim/AGV0001/state", topic);
    }

    [Fact]
    public void TryParse_RoundTripsSuccessfully()
    {
        var original = Vda5050Topic.Build("AgvSim", "AGV0001", Vda5050Topic.State);
        bool success = Vda5050Topic.TryParse(original, out var info);

        Assert.True(success);
        Assert.Equal("AgvSim", info.Manufacturer);
        Assert.Equal("AGV0001", info.SerialNumber);
        Assert.Equal("state", info.Topic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("uagv")]
    [InlineData("uagv/v2")]
    [InlineData("uagv/v2/AgvSim")]
    [InlineData("uagv/v2/AgvSim/AGV0001")]
    [InlineData("uagv/v2/AgvSim/AGV0001/state/extra")]
    [InlineData("other/v2/AgvSim/AGV0001/state")]
    [InlineData("uagv/v1/AgvSim/AGV0001/state")]
    [InlineData("uagv/v2//AGV0001/state")]
    [InlineData("uagv/v2/AgvSim//state")]
    [InlineData("uagv/v2/AgvSim/AGV0001/")]
    public void TryParse_MalformedOrInvalidTopics_ReturnsFalse(string? topic)
    {
        bool success = Vda5050Topic.TryParse(topic, out var info);

        Assert.False(success);
        Assert.Equal(string.Empty, info.Manufacturer);
        Assert.Equal(string.Empty, info.SerialNumber);
        Assert.Equal(string.Empty, info.Topic);
    }
}
