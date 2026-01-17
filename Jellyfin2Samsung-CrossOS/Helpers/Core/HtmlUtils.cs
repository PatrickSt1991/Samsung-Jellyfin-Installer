namespace Jellyfin2Samsung.Helpers.Core
{
    public static class HtmlUtils
    {
        public static string EnsureBaseHref(string html)
        {
            if (html.Contains("<base", System.StringComparison.OrdinalIgnoreCase))
                return RegexPatterns.Html.BaseTag.Replace(html, "<base href=\".\">");

            return html.Replace("<head>", "<head><base href=\".\">");
        }

        public static string RewriteLocalPaths(string html)
        {
            return RegexPatterns.Html.LocalPaths.Replace(html, "$1=\"$2\"");
        }

        public static string CleanAndApplyCsp(string html)
        {
            html = RegexPatterns.Html.CspMeta.Replace(html, "");
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
        public static string EscapeJsString(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            return html
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
