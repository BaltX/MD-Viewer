using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Markdig;

namespace MDViewer.Services;

public static class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ConvertToHtml(string markdown, string baseDirectory)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        body = FixImagePaths(body, baseDirectory);
        body = FixLinkPaths(body, baseDirectory);
        return Wrap(body);
    }

    public static string WrapRaw(string text)
    {
        var escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return Wrap($"<pre style=\"white-space:pre-wrap;word-break:break-all;font-size:13px\">{escaped}</pre>");
    }

    private static string FixImagePaths(string html, string baseDir)
    {
        return Regex.Replace(
            html,
            @"(<img[^>]+src="")(?!https?://|data:|file://)([^""]+)("")",
            m => ToAbsoluteFileRef(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, baseDir)
                 ?? m.Value);
    }

    // Converts all relative href links to absolute file:// paths so the WebBrowser
    // resolves them against the real project directory, not the temp HTML file location.
    private static string FixLinkPaths(string html, string baseDir)
    {
        return Regex.Replace(
            html,
            // href="..." — skip absolute URLs, pure anchors, and mailto
            @"(href="")(?!https?://|#|mailto:|file://)([^""]*?)([^""]*?)("")",
            m =>
            {
                var full = m.Groups[2].Value + m.Groups[3].Value; // full href value
                if (string.IsNullOrEmpty(full)) return m.Value;

                // Split anchor fragment
                var hashIdx = full.IndexOf('#');
                var fragment = hashIdx >= 0 ? full[hashIdx..] : "";
                var path     = hashIdx >= 0 ? full[..hashIdx] : full;

                if (string.IsNullOrEmpty(path)) return m.Value; // pure #anchor

                // URL-decode Cyrillic and other non-ASCII before building a real path
                var decoded = Uri.UnescapeDataString(path);

                var result = ToAbsoluteFileRef("href=\"", decoded, "\"", baseDir);
                if (result == null) return m.Value;

                // Re-inject fragment
                if (fragment.Length > 0)
                    result = result[..^1] + fragment + "\"";
                return result;
            });
    }

    private static string? ToAbsoluteFileRef(string prefix, string relativePath, string suffix, string baseDir)
    {
        try
        {
            var abs = Path.GetFullPath(Path.Combine(baseDir, relativePath)).Replace('\\', '/');
            return $"{prefix}file:///{abs}{suffix}";
        }
        catch { return null; }
    }

    private static string Wrap(string body) => $"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta http-equiv="X-UA-Compatible" content="IE=Edge">
        <style>{BuildCss()}</style>
        </head>
        <body>{body}<div id="csb"><div id="csb-t"></div></div>
        <script>{NavJs}</script>
        <script>{ScrollbarJs}</script>
        </body>
        </html>
        """;

    private static string C(string key, string fallback) =>
        Application.Current?.Resources[key] as string ?? fallback;

    private static string BuildCss()
    {
        var bg         = C("MdBg",         "#1e1e1e");
        var fg         = C("MdFg",         "#c9d1d9");
        var heading    = C("MdHeading",    "#f0f6fc");
        var code       = C("MdCode",       "#2d2d2d");
        var codeFg     = C("MdCodeFg",     "#e8912d");
        var codeBlock  = C("MdCodeBlockFg","#c9d1d9");
        var border     = C("MdBorder",     "#30363d");
        var link       = C("MdLink",       "#58a6ff");
        var quote      = C("MdQuote",      "#8b949e");
        var tableEven  = C("MdTableEven",  "#161b22");
        var thHead     = C("MdThHead",     "#161b22");
        var em         = C("MdEm",         "#d2a8ff");
        var strong      = C("MdStrong",           "#f0f6fc");
        var sThumb      = C("MdScrollThumb",      "#4e5157");
        var sThumbHover = C("MdScrollThumbHover", "#7a7d83");

        // $$ prefix: {{ var }} = interpolation, single { } = literal CSS braces
        return $$"""
            * { box-sizing: border-box; margin: 0; padding: 0; }
            html { -ms-overflow-style: none; }
            body {
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
                font-size: 15px;
                line-height: 1.65;
                color: {{fg}};
                background-color: {{bg}};
                padding: 28px 36px 60px;
                max-width: 860px;
                margin: 0 auto;
            }
            /* Custom scrollbar */
            #csb {
                display: none;
                position: fixed;
                top: 0; right: 0; bottom: 0;
                width: 10px;
                z-index: 99999;
                cursor: default;
            }
            #csb-t {
                position: absolute;
                right: 3px;
                width: 4px;
                background: {{sThumb}};
                border-radius: 3px;
                min-height: 24px;
                transition: width .12s, right .12s, background .12s;
            }
            #csb.hov #csb-t {
                width: 6px;
                right: 2px;
                background: {{sThumbHover}};
            }
            h1, h2, h3, h4, h5, h6 {
                color: {{heading}};
                margin-top: 28px;
                margin-bottom: 14px;
                font-weight: 600;
                line-height: 1.25;
            }
            h1 { font-size: 1.9em; border-bottom: 1px solid {{border}}; padding-bottom: .35em; }
            h2 { font-size: 1.5em; border-bottom: 1px solid {{border}}; padding-bottom: .3em; }
            h3 { font-size: 1.2em; }
            h4 { font-size: 1.05em; }
            p { margin: 12px 0; }
            a { color: {{link}}; text-decoration: none; }
            a:hover { text-decoration: underline; }
            strong { color: {{strong}}; }
            em { color: {{em}}; }
            code {
                background: {{code}};
                border-radius: 3px;
                padding: .15em .4em;
                font-family: Consolas, 'Liberation Mono', Menlo, monospace;
                font-size: 87%;
                color: {{codeFg}};
            }
            pre {
                background: {{code}};
                border: 1px solid {{border}};
                border-radius: 6px;
                padding: 16px;
                overflow: auto;
                margin: 16px 0;
            }
            pre code { background: transparent; padding: 0; font-size: 13px; color: {{codeBlock}}; }
            blockquote { margin: 14px 0; padding: 6px 1em; color: {{quote}}; border-left: 4px solid {{border}}; }
            table { border-collapse: collapse; width: 100%; margin: 16px 0; display: block; overflow-x: auto; }
            th, td { padding: 8px 14px; border: 1px solid {{border}}; }
            th { background: {{thHead}}; font-weight: 600; color: {{heading}}; }
            tr:nth-child(even) { background: {{tableEven}}; }
            img { max-width: 100%; height: auto; border-radius: 4px; display: block; margin: 12px 0; }
            ul, ol { padding-left: 2em; margin: 10px 0; }
            li { margin-top: 4px; }
            li > p { margin: 4px 0; }
            hr { border: none; border-top: 1px solid {{border}}; margin: 28px 0; }
            .task-list-item { list-style: none; margin-left: -1.5em; }
            .task-list-item input[type=checkbox] { margin-right: 6px; }
            details { margin: 10px 0; }
            summary { cursor: pointer; color: {{link}}; }
            @media print {
                #csb { display: none !important; }
                body { color: #000; background: #fff; padding: 0; max-width: none; }
                a { color: #000; text-decoration: underline; }
                code { background: #f4f4f4; color: #333; }
                pre { background: #f4f4f4; border: 1px solid #ccc; }
                pre code { color: #333; }
                blockquote { border-left: 3px solid #ccc; color: #555; }
                th { background: #eee; color: #000; }
                tr:nth-child(even) { background: #f9f9f9; }
                h1, h2, h3, h4, h5, h6 { color: #000; }
                strong { color: #000; }
                em { color: #333; }
            }
            """;
    }

    // Intercepts all link clicks and routes them through window.external.navigate()
    // so cross-zone file:// navigation works when content is loaded via NavigateToString.
    private const string NavJs = """
        (function () {
            function findAnchor(el) {
                while (el && el.tagName !== 'A') el = el.parentElement;
                return el;
            }
            document.addEventListener('click', function (e) {
                var a = findAnchor(e.target);
                if (!a || !a.href) return;
                e.preventDefault();
                try { window.external.navigate(a.href); } catch (ex) {}
            });
        })();
        """;

    // Vanilla JS custom scrollbar — no dependencies, works in IE11.
    // Raw string literal (no $) so JS { } braces are literal.
    private const string ScrollbarJs = """
        (function () {
            var bar = document.getElementById('csb');
            var thumb = document.getElementById('csb-t');
            if (!bar || !thumb) return;

            var dragging = false, startY = 0, startScroll = 0;

            function getScrollY() { return window.pageYOffset || document.documentElement.scrollTop || 0; }
            function getViewH()   { return window.innerHeight  || document.documentElement.clientHeight || 0; }
            function getDocH()    {
                return Math.max(document.body.scrollHeight,
                                document.documentElement.scrollHeight,
                                document.body.offsetHeight);
            }

            function update() {
                var v = getViewH(), d = getDocH(), s = getScrollY();
                if (d <= v) { bar.style.display = 'none'; return; }
                bar.style.display = 'block';
                var th = Math.max(24, v * v / d);
                thumb.style.height = th + 'px';
                thumb.style.top    = (s / (d - v) * (v - th)) + 'px';
            }

            thumb.onmousedown = function (e) {
                dragging = true; startY = e.clientY; startScroll = getScrollY();
                document.body.style.userSelect = 'none';
                e.preventDefault();
            };
            document.onmousemove = function (e) {
                if (!dragging) return;
                var v = getViewH(), d = getDocH(), th = Math.max(24, v * v / d);
                window.scrollTo(0, startScroll + (e.clientY - startY) / (v - th) * (d - v));
                e.preventDefault();
            };
            document.onmouseup = function () {
                dragging = false;
                document.body.style.userSelect = '';
            };

            bar.onclick = function (e) {
                if (e.target === thumb) return;
                var v = getViewH(), d = getDocH(), th = Math.max(24, v * v / d);
                window.scrollTo(0, Math.max(0, (e.clientY - th / 2) / (v - th) * (d - v)));
            };

            bar.onmouseover = function () { bar.className = 'hov'; };
            bar.onmouseout  = function () { if (!dragging) bar.className = ''; };

            window.onscroll = update;
            window.onresize = update;
            window.onload   = update;
            update();
        })();
        """;
}
