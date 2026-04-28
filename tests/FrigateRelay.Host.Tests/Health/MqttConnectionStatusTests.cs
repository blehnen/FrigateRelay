using FrigateRelay.Host.Health;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Health;

[TestClass]
public sealed class MqttConnectionStatusTests
{
    [TestMethod]
    public void Default_IsConnected_IsFalse()
    {
        var status = new MqttConnectionStatus();
        Assert.IsFalse(status.IsConnected);
    }

    [TestMethod]
    public void SetConnected_True_IsConnected_ReturnsTrue()
    {
        var status = new MqttConnectionStatus();
        status.SetConnected(true);
        Assert.IsTrue(status.IsConnected);
    }

    [TestMethod]
    public void SetConnected_False_AfterTrue_IsConnected_ReturnsFalse()
    {
        var status = new MqttConnectionStatus();
        status.SetConnected(true);
        status.SetConnected(false);
        Assert.IsFalse(status.IsConnected);
    }

    [TestMethod]
    public void ConcurrentReadWrite_DoesNotThrow()
    {
        var status = new MqttConnectionStatus();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
        {
            try
            {
                for (var j = 0; j < 1000; j++)
                {
                    status.SetConnected(j % 2 == 0);
                    _ = status.IsConnected;
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.AreEqual(0, exceptions.Count, $"Concurrent access threw: {string.Join("; ", exceptions.Select(e => e.Message))}");
    }
}
