// SPDX-License-Identifier: MIT

namespace ShellyEmMcp.Tests.TestSupport;

/// <summary>
/// Captures the last outgoing request and returns a canned response, so RPC client tests can assert
/// on exactly what was sent (URL/method) without hitting a real Shelly device.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(responder(request));
    }
}
