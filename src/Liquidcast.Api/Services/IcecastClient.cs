using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Liquidcast.Api.Services;

/// <summary>Reads listener stats from the Icecast admin API.</summary>
public class IcecastClient
{
    private readonly IHttpClientFactory _http;
    private readonly RuntimeConfig _cfg;
    private readonly ILogger<IcecastClient> _log;

    public IcecastClient(IHttpClientFactory http, RuntimeConfig cfg, ILogger<IcecastClient> log)
    {
        _http = http;
        _cfg = cfg;
        _log = log;
    }

    public record Stats(bool MountConnected, int Listeners);

    public async Task<Stats> FetchAsync(CancellationToken ct)
    {
        var s = _cfg.Settings;
        var url = $"http://{s.IcecastHost}:{s.IcecastPort}/admin/stats.xml";
        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{s.IcecastAdminUser}:{s.IcecastAdminPassword}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogDebug("Icecast admin stats returned {Status} from {Url} (check admin user/password)",
                    (int)resp.StatusCode, url);
                return new Stats(false, 0);
            }

            var xml = await resp.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var mount = s.IcecastMount;

            var source = doc.Descendants("source")
                .FirstOrDefault(e => (string?)e.Attribute("mount") == mount);
            if (source is null)
            {
                _log.LogDebug("Icecast reachable but mount {Mount} has no source (Liquidsoap not connected)", mount);
                return new Stats(false, 0);
            }

            int listeners = int.TryParse((string?)source.Element("listeners"), out var l) ? l : 0;
            return new Stats(true, listeners);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Icecast stats fetch failed for {Url} (unreachable?)", url);
            return new Stats(false, 0);
        }
    }
}
