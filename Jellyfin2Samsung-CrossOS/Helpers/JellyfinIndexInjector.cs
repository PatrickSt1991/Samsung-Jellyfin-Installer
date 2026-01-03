using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class JellyfinIndexInjector
{
    public static async Task DownloadAndPatchIndexHtmlAsync(
        string jellyfinBaseUrl,
        string wwwFolderPath)
    {
        jellyfinBaseUrl = jellyfinBaseUrl.TrimEnd('/');

        if (!Directory.Exists(wwwFolderPath))
            throw new DirectoryNotFoundException(wwwFolderPath);

        var indexUrl = $"{jellyfinBaseUrl}/web/index.html";

        using var http = new HttpClient();
        var html = await http.GetStringAsync(indexUrl);

        const string injection = @"
<script src=""$WEBAPIS/webapis/webapis.js""></script>
<script>window.appMode='cordova';</script>
<script src=""../tizen.js"" defer></script>
";

        // Find main.jellyfin.bundle.js (hash changes per build)
        var regex = new Regex(
            @"<script\s+defer[^>]+src=""main\.jellyfin\.bundle\.js[^""]*""></script>",
            RegexOptions.IgnoreCase);

        var match = regex.Match(html);
        if (!match.Success)
            throw new InvalidOperationException("main.jellyfin.bundle.js not found");

        // Inject BEFORE main.jellyfin.bundle.js
        html = html.Insert(match.Index, injection + "\n");

        var outputPath = Path.Combine(wwwFolderPath, "index.html");
        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8);
    }
}
