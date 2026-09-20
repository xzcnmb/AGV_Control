namespace AgvDispatch.Tests;

using System.Collections.Concurrent;
using AgvDispatch.Vda5050;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Topics;

public class HeaderIdCounterTests
{
    [Fact]
    public void Next_StartsAtZeroAndIncrementsMonotonicallyPerTopic()
    {
        var counter = new HeaderIdCounter();

        Assert.Equal(0u, counter.Next("state"));
        Assert.Equal(1u, counter.Next("state"));
        Assert.Equal(2u, counter.Next("state"));
    }

    [Fact]
    public void Next_CountersAreIndependentAcrossTopics()
    {
        var counter = new HeaderIdCounter();

        Assert.Equal(0u, counter.Next("state"));
        Assert.Equal(1u, counter.Next("state"));

        Assert.Equal(0u, counter.Next("order"));
        Assert.Equal(0u, counter.Next("connection"));

        Assert.Equal(2u, counter.Next("state"));
        Assert.Equal(1u, counter.Next("order"));
        Assert.Equal(1u, counter.Next("connection"));
    }

    [Fact]
    public void GetCurrent_ReturnsCurrentValueWithoutIncrementing()
    {
        var counter = new HeaderIdCounter();

        Assert.Equal(0u, counter.GetCurrent("state"));

        counter.Next("state"); // returns 0, next will be 1
        Assert.Equal(1u, counter.GetCurrent("state"));

        counter.Next("state"); // returns 1, next will be 2
        Assert.Equal(2u, counter.GetCurrent("state"));
    }

    [Fact]
    public void Reset_ResetsSpecificTopicOnly()
    {
        var counter = new HeaderIdCounter();

        counter.Next("state");
        counter.Next("state");
        counter.Next("order");

        counter.Reset("state");

        Assert.Equal(0u, counter.Next("state"));
        Assert.Equal(1u, counter.Next("order"));
    }

    [Fact]
    public void ResetAll_ResetsAllTopicCounters()
    {
        var counter = new HeaderIdCounter();

        counter.Next("state");
        counter.Next("order");
        counter.Next("connection");

        counter.ResetAll();

        Assert.Equal(0u, counter.Next("state"));
        Assert.Equal(0u, counter.Next("order"));
        Assert.Equal(0u, counter.Next("connection"));
    }

    [Fact]
    public void Stamp_SetsAllHeaderFieldsCorrectly()
    {
        var counter = new HeaderIdCounter();
        var msg = new StateMessage();

        var before = DateTime.UtcNow;
        counter.Stamp(msg, "state", "AgvSim", "AGV0001");
        var after = DateTime.UtcNow;

        Assert.Equal(0u, msg.HeaderId);
        Assert.Equal(Vda5050Constants.ProtocolVersion, msg.Version);
        Assert.Equal("AgvSim", msg.Manufacturer);
        Assert.Equal("AGV0001", msg.SerialNumber);
        Assert.True(msg.Timestamp >= before.AddMilliseconds(-100) && msg.Timestamp <= after.AddMilliseconds(100));
    }

    [Fact]
    public void Next_IsThreadSafeUnderHighConcurrency()
    {
        var counter = new HeaderIdCounter();
        const int iterations = 5000;
        var results = new ConcurrentBag<uint>();

        Parallel.For(0, iterations, _ =>
        {
            var val = counter.Next("concurrent-topic");
            results.Add(val);
        });

        Assert.Equal(iterations, results.Count);

        var distinctCount = results.Distinct().Count();
        Assert.Equal(iterations, distinctCount);

        Assert.Equal((uint)(iterations - 1), results.Max());
        Assert.Equal(0u, results.Min());
    }
}
