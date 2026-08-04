using System.Net;

namespace NotifyHub.Tests;

/// <summary>Minimal fake HttpMessageHandler: returns the next prepared response from a queue for
/// each incoming request - handler-based because every channel can have an HttpClient injected,
/// making it testable without a real network connection or provider account.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string content = "")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(content) });
        return this;
    }

    public FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("Keine weitere Fake-Antwort in der Queue.");
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
