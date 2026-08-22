// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using ShellyEmMcp.Devices;
using ShellyEmMcp.Tests.TestSupport;

namespace ShellyEmMcp.Tests;

[TestFixture]
public class ShellyRpcClientTests
{
    [Test]
    public async Task CallAsync_BuildsRpcUrl_WithoutParameters()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"model":"SPEM-003CEBEU"}""", Encoding.UTF8, "application/json"),
        });
        var client = new ShellyRpcClient(new HttpClient(handler));

        var result = await client.CallAsync("192.168.1.100", "Shelly.GetDeviceInfo");

        Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("http://192.168.1.100/rpc/Shelly.GetDeviceInfo"));
        Assert.That(result, Does.Contain("SPEM-003CEBEU"));
    }

    [Test]
    public async Task CallAsync_BuildsRpcUrl_WithEscapedParameters()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = new ShellyRpcClient(new HttpClient(handler));

        await client.CallAsync("192.168.1.100", "EM.GetStatus", new Dictionary<string, string> { ["id"] = "0" });

        Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("http://192.168.1.100/rpc/EM.GetStatus?id=0"));
    }

    [Test]
    public void CallAsync_ThrowsOnErrorResponse()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"not found"}""", Encoding.UTF8, "application/json"),
        });
        var client = new ShellyRpcClient(new HttpClient(handler));

        Assert.That(async () => await client.CallAsync("192.168.1.100", "Unknown.Method"), Throws.TypeOf<HttpRequestException>());
    }
}
