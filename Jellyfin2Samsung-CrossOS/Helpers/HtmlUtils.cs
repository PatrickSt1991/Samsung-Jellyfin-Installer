using System.Text.RegularExpressions;

namespace Jellyfin2Samsung.Helpers
{
    public static class HtmlUtils
    {
        public static string EnsureBaseHref(string html)
        {
            if (html.Contains("<base", System.StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(html, @"<base[^>]+>", "<base href=\".\">");

            return html.Replace("<head>", "<head><base href=\".\">");
        }

        public static string RewriteLocalPaths(string html)
        {
            html = Regex.Replace(html, @"(src|href)=""[^""]*/web/([^""]+)""", "$1=\"$2\"");
            return html;
        }

        public static string CleanAndApplyCsp(string html)
        {
            html = Regex.Replace(html, @"<meta[^>]*Content-Security-Policy[^>]*>", "");
            return html.Replace("</head>",
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;\">\n</head>");
        }

        public static string EnsurePublicJsIsLast(string html)
        {
            const string tag = "<script src=\"plugin_cache/public.js\"></script>";
            if (!html.Contains(tag)) return html;

            html = html.Replace(tag, "");
            return html.Replace("</body>", tag + "\n</body>");
        }
    }
}
