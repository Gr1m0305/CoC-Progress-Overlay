using System.Net;
using System.Text;

namespace ClashOverlay.Tests.Support;

/// <summary>Serves canned responses by URL (exact match first, then prefix) and records every request.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(string Url, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public FakeHttpHandler On(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((url, respond));
        return this;
    }

    public FakeHttpHandler OnJson(string url, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(url, _ => Json(json, status));

    public FakeHttpHandler OnBytes(string url, byte[] bytes) =>
        On(url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public int Count(string urlPart) => Requests.Count(r => r.RequestUri!.ToString().Contains(urlPart, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        var url = request.RequestUri!.ToString();
        foreach (var (u, respond) in _routes)
        {
            if (string.Equals(u, url, StringComparison.Ordinal))
            {
                return respond(request);
            }
        }

        foreach (var (u, respond) in _routes)
        {
            if (url.StartsWith(u, StringComparison.Ordinal))
            {
                return respond(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"reason\":\"notFound\"}") };
    }
}
