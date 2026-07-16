using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PurrNet.Editor
{
    internal enum PurrMarkdownBlockKind
    {
        Paragraph,
        Heading,
        UnorderedList,
        OrderedList,
        Code,
        Quote,
        Rule,
        Image
    }

    internal sealed class PurrMarkdownBlock
    {
        public PurrMarkdownBlockKind kind { get; }
        public string text { get; }
        public IReadOnlyList<string> items { get; }
        public int level { get; }
        public string url { get; }
        public string linkUrl { get; }
        public float requestedWidth { get; }
        public float requestedHeight { get; }

        private PurrMarkdownBlock(PurrMarkdownBlockKind kind, string text = null,
            IReadOnlyList<string> items = null, int level = 0, string url = null,
            string linkUrl = null, float requestedWidth = 0, float requestedHeight = 0)
        {
            this.kind = kind;
            this.text = text ?? string.Empty;
            this.items = items;
            this.level = level;
            this.url = url;
            this.linkUrl = linkUrl;
            this.requestedWidth = requestedWidth;
            this.requestedHeight = requestedHeight;
        }

        public static PurrMarkdownBlock Paragraph(string text) =>
            new(PurrMarkdownBlockKind.Paragraph, text);

        public static PurrMarkdownBlock Heading(string text, int level) =>
            new(PurrMarkdownBlockKind.Heading, text, level: level);

        public static PurrMarkdownBlock List(IReadOnlyList<string> items, bool ordered) =>
            new(ordered ? PurrMarkdownBlockKind.OrderedList : PurrMarkdownBlockKind.UnorderedList,
                items: items);

        public static PurrMarkdownBlock Code(string text) =>
            new(PurrMarkdownBlockKind.Code, text);

        public static PurrMarkdownBlock Quote(string text) =>
            new(PurrMarkdownBlockKind.Quote, text);

        public static PurrMarkdownBlock Rule() => new(PurrMarkdownBlockKind.Rule);

        public static PurrMarkdownBlock Image(string alt, string url, string linkUrl,
            float requestedWidth, float requestedHeight) =>
            new(PurrMarkdownBlockKind.Image, alt, url: url, linkUrl: linkUrl,
                requestedWidth: requestedWidth, requestedHeight: requestedHeight);
    }

    internal sealed class PurrMarkdownDocument
    {
        public IReadOnlyList<PurrMarkdownBlock> blocks { get; }
        public string linkBaseUrl { get; }

        internal PurrMarkdownDocument(IReadOnlyList<PurrMarkdownBlock> blocks, string linkBaseUrl)
        {
            this.blocks = blocks;
            this.linkBaseUrl = linkBaseUrl;
        }
    }

    internal static class PurrMarkdownParser
    {
        internal const int MaxMarkdownCharacters = 512 * 1024;
        internal const int MaxBlocks = 2048;
        internal const int MaxImages = 48;
        internal const int MaxBlockCharacters = 64 * 1024;
        internal const int MaxListItems = 512;

        private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+?)\s*#*\s*$",
            RegexOptions.Compiled);
        private static readonly Regex ListRegex = new(@"^\s*(?:([-+*])|(\d+)[.)])\s+(.+)$",
            RegexOptions.Compiled);
        private static readonly Regex MarkdownImageRegex = new(
            @"(?:\[)?!\[(?<alt>[^\]]*)\]\((?<src><[^>]+>|[^\s\)]+)(?:\s+[""'].*?[""'])?\)(?:\]\((?<link><[^>]+>|[^\s\)]+)(?:\s+[""'].*?[""'])?\))?",
            RegexOptions.Compiled);
        private static readonly Regex HtmlImageRegex = new(@"<img\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(
            @"</?(?:a|img|picture|source|div|span|p|br|details|summary|table|thead|tbody|tr|th|td|h[1-6]|code|pre|kbd|sup|sub|em|strong|ul|ol|li|blockquote|hr)\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AttributeRegex = new(
            @"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+))",
            RegexOptions.Compiled);
        private static readonly Regex AnchorOpenRegex = new(@"<a\b[^>]*\bhref\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+))[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AnchorCloseRegex = new(@"</a\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly struct ImageMatch
        {
            public readonly int Index;
            public readonly int Length;
            public readonly string Alt;
            public readonly string Source;
            public readonly string Link;
            public readonly float Width;
            public readonly float Height;

            public ImageMatch(int index, int length, string alt, string source, string link,
                float width, float height)
            {
                Index = index;
                Length = length;
                Alt = alt;
                Source = source;
                Link = link;
                Width = width;
                Height = height;
            }
        }

        private readonly struct AnchorMatch
        {
            public readonly int Index;
            public readonly string Link;
            public readonly bool IsOpen;

            public AnchorMatch(int index, string link, bool isOpen)
            {
                Index = index;
                Link = link;
                IsOpen = isOpen;
            }
        }

        public static PurrMarkdownDocument Parse(string markdown, string contentBaseUrl, string sourceUrl)
        {
            markdown ??= string.Empty;
            bool truncated = markdown.Length > MaxMarkdownCharacters;
            if (truncated)
                markdown = markdown.Substring(0, MaxMarkdownCharacters);

            var blocks = new List<PurrMarkdownBlock>();
            var paragraph = new StringBuilder();
            var listItems = new List<string>();
            bool? orderedList = null;
            var code = new StringBuilder();
            bool inCode = false;
            int imageCount = 0;
            string htmlLink = null;

            void Add(PurrMarkdownBlock block)
            {
                if (block != null && blocks.Count < MaxBlocks)
                    blocks.Add(block);
            }

            void FlushParagraph()
            {
                if (paragraph.Length == 0)
                    return;
                Add(PurrMarkdownBlock.Paragraph(paragraph.ToString().Trim()));
                paragraph.Clear();
            }

            void FlushList()
            {
                if (listItems.Count == 0)
                    return;
                Add(PurrMarkdownBlock.List(listItems.ToArray(), orderedList == true));
                listItems.Clear();
                orderedList = null;
            }

            void AppendParagraph(string value)
            {
                value = StripUnsafeHtml(value).Trim();
                if (value.Length == 0)
                    return;

                while (value.Length > MaxBlockCharacters)
                {
                    FlushParagraph();
                    Add(PurrMarkdownBlock.Paragraph(value.Substring(0, MaxBlockCharacters)));
                    value = value.Substring(MaxBlockCharacters);
                }

                if (paragraph.Length > 0 && paragraph.Length + value.Length + 1 > MaxBlockCharacters)
                    FlushParagraph();
                if (paragraph.Length > 0)
                    paragraph.Append(' ');
                paragraph.Append(value);
            }

            void AddCodeBlocks()
            {
                string value = code.ToString().TrimEnd('\n');
                for (int offset = 0; offset < value.Length && blocks.Count < MaxBlocks;
                     offset += MaxBlockCharacters)
                {
                    Add(PurrMarkdownBlock.Code(value.Substring(offset,
                        Math.Min(MaxBlockCharacters, value.Length - offset))));
                }
                code.Clear();
            }

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var rawLine in lines)
            {
                if (blocks.Count >= MaxBlocks)
                    break;

                string line = rawLine;
                string trimmed = line.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                    trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    if (inCode)
                    {
                        AddCodeBlocks();
                        inCode = false;
                    }
                    else
                    {
                        FlushParagraph();
                        FlushList();
                        inCode = true;
                    }
                    continue;
                }

                if (inCode)
                {
                    code.AppendLine(line);
                    continue;
                }

                var anchors = FindAnchors(line);

                if (string.IsNullOrWhiteSpace(line))
                {
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    FlushParagraph();
                    FlushList();
                    continue;
                }

                var heading = HeadingRegex.Match(trimmed);
                if (heading.Success)
                {
                    FlushParagraph();
                    FlushList();
                    string headingText = heading.Groups[2].Value;
                    if (headingText.Length > 8192)
                        headingText = headingText.Substring(0, 8192) + "…";
                    Add(PurrMarkdownBlock.Heading(headingText, heading.Groups[1].Value.Length));
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    continue;
                }

                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    FlushParagraph();
                    FlushList();
                    Add(PurrMarkdownBlock.Rule());
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    continue;
                }

                var list = ListRegex.Match(line);
                if (list.Success)
                {
                    FlushParagraph();
                    bool ordered = list.Groups[2].Success;
                    if (orderedList.HasValue && orderedList.Value != ordered)
                        FlushList();
                    orderedList = ordered;
                    if (listItems.Count < MaxListItems)
                    {
                        string item = list.Groups[3].Value.Trim();
                        listItems.Add(item.Length <= 8192 ? item : item.Substring(0, 8192) + "…");
                    }
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    continue;
                }

                FlushList();

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    string quote = trimmed.TrimStart('>').TrimStart();
                    if (quote.Length > MaxBlockCharacters)
                        quote = quote.Substring(0, MaxBlockCharacters) + "…";
                    Add(PurrMarkdownBlock.Quote(quote));
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    continue;
                }

                var images = FindImages(line);
                if (images.Count == 0)
                {
                    AppendParagraph(line);
                    ApplyRemainingAnchors(anchors, 0, 0, ref htmlLink);
                    continue;
                }

                int offset = 0;
                int anchorOffset = 0;
                foreach (var image in images)
                {
                    // Markdown and HTML image regexes can both match the same source range
                    // (for example, an HTML image in Markdown alt text). Prefer the earliest,
                    // longest match and ignore any nested/overlapping match.
                    if (image.Index < offset)
                        continue;

                    ApplyAnchorsBefore(anchors, image.Index, offset, ref anchorOffset, ref htmlLink);
                    AppendParagraph(line.Substring(offset, image.Index - offset));
                    FlushParagraph();

                    if (imageCount < MaxImages)
                    {
                        string imageUrl = PurrMarkdownUrl.ResolveImage(contentBaseUrl, image.Source);
                        string linkUrl = PurrMarkdownUrl.ResolveLink(sourceUrl, image.Link ?? htmlLink);
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            Add(PurrMarkdownBlock.Image(WebUtility.HtmlDecode(image.Alt ?? string.Empty),
                                imageUrl, linkUrl, image.Width, image.Height));
                            imageCount++;
                        }
                        else if (!string.IsNullOrEmpty(image.Alt))
                        {
                            AppendParagraph(image.Alt);
                        }
                    }
                    offset = image.Index + image.Length;
                }
                AppendParagraph(line.Substring(offset));
                ApplyRemainingAnchors(anchors, anchorOffset, offset, ref htmlLink);
            }

            if (inCode && code.Length > 0)
                AddCodeBlocks();
            FlushList();
            FlushParagraph();

            if (truncated || blocks.Count >= MaxBlocks)
                Add(PurrMarkdownBlock.Quote("README truncated for editor performance."));

            return new PurrMarkdownDocument(blocks, sourceUrl);
        }

        private static List<ImageMatch> FindImages(string line)
        {
            var matches = new List<ImageMatch>();

            foreach (Match match in MarkdownImageRegex.Matches(line))
            {
                matches.Add(new ImageMatch(match.Index, match.Length,
                    match.Groups["alt"].Value,
                    TrimUrl(match.Groups["src"].Value),
                    TrimUrl(match.Groups["link"].Value), 0, 0));
            }

            foreach (Match match in HtmlImageRegex.Matches(line))
            {
                string source = null;
                string alt = null;
                float width = 0;
                float height = 0;

                foreach (Match attribute in AttributeRegex.Matches(match.Value))
                {
                    string name = attribute.Groups["name"].Value;
                    string value = WebUtility.HtmlDecode(GetCapture(attribute, "dq", "sq", "bare"));
                    if (name.Equals("src", StringComparison.OrdinalIgnoreCase)) source = value;
                    else if (name.Equals("alt", StringComparison.OrdinalIgnoreCase)) alt = value;
                    else if (name.Equals("width", StringComparison.OrdinalIgnoreCase)) TryParseDimension(value, out width);
                    else if (name.Equals("height", StringComparison.OrdinalIgnoreCase)) TryParseDimension(value, out height);
                }

                if (!string.IsNullOrEmpty(source))
                    matches.Add(new ImageMatch(match.Index, match.Length, alt, source, null, width, height));
            }

            return matches.OrderBy(m => m.Index).ThenByDescending(m => m.Length).ToList();
        }

        private static List<AnchorMatch> FindAnchors(string line)
        {
            var matches = new List<AnchorMatch>();
            foreach (Match match in AnchorOpenRegex.Matches(line))
            {
                matches.Add(new AnchorMatch(match.Index,
                    WebUtility.HtmlDecode(GetCapture(match, "dq", "sq", "bare")), true));
            }

            foreach (Match match in AnchorCloseRegex.Matches(line))
                matches.Add(new AnchorMatch(match.Index, null, false));

            return matches.OrderBy(match => match.Index).ThenBy(match => match.IsOpen ? 0 : 1).ToList();
        }

        private static void ApplyAnchorsBefore(IReadOnlyList<AnchorMatch> anchors, int beforeIndex,
            int consumedUntil, ref int anchorOffset, ref string htmlLink)
        {
            while (anchorOffset < anchors.Count && anchors[anchorOffset].Index < beforeIndex)
            {
                var anchor = anchors[anchorOffset++];
                if (anchor.Index < consumedUntil)
                    continue;
                htmlLink = anchor.IsOpen ? anchor.Link : null;
            }
        }

        private static void ApplyRemainingAnchors(IReadOnlyList<AnchorMatch> anchors, int anchorOffset,
            int consumedUntil, ref string htmlLink)
        {
            while (anchorOffset < anchors.Count)
            {
                var anchor = anchors[anchorOffset++];
                if (anchor.Index < consumedUntil)
                    continue;
                htmlLink = anchor.IsOpen ? anchor.Link : null;
            }
        }

        private static void TryParseDimension(string value, out float dimension)
        {
            value = value?.Trim().TrimEnd('p', 'x', '%');
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dimension) ||
                dimension < 1 || dimension > 4096)
                dimension = 0;
        }

        private static string GetCapture(Match match, params string[] names)
        {
            foreach (string name in names)
                if (match.Groups[name].Success)
                    return match.Groups[name].Value;
            return string.Empty;
        }

        private static string TrimUrl(string value)
        {
            value = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            if (value.Length > 1 && value[0] == '<' && value[^1] == '>')
                value = value.Substring(1, value.Length - 2);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string StripUnsafeHtml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return WebUtility.HtmlDecode(HtmlTagRegex.Replace(value, string.Empty));
        }
    }

    internal static class PurrMarkdownInline
    {
        private static readonly Regex LinkRegex = new(@"(?<!!)\[(?<label>[^\]]+)\]\((?<url><[^>]+>|[^\s\)]+)(?:\s+[""'].*?[""'])?\)",
            RegexOptions.Compiled);
        private static readonly Regex AutoLinkRegex = new(@"(?<![=""'])\bhttps?://[^\s<>]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex BoldAsteriskRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldUnderscoreRegex = new(@"__(.+?)__", RegexOptions.Compiled);
        private static readonly Regex ItalicAsteriskRegex = new(@"(?<!\*)\*([^*]+?)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex ItalicUnderscoreRegex = new(@"(?<!\w)_([^_]+?)_(?!\w)", RegexOptions.Compiled);

        public static string ToRichText(string text, string linkBaseUrl)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = new StringBuilder(text.Length + 32);
            int offset = 0;
            foreach (Match match in LinkRegex.Matches(text))
            {
                result.Append(FormatPlain(text.Substring(offset, match.Index - offset), linkBaseUrl));
                string url = PurrMarkdownUrl.ResolveLink(linkBaseUrl, match.Groups["url"].Value);
                string label = FormatPlain(match.Groups["label"].Value, null);
                result.Append(string.IsNullOrEmpty(url) ? label : MakeLink(label, url));
                offset = match.Index + match.Length;
            }
            result.Append(FormatPlain(text.Substring(offset), linkBaseUrl));
            return result.ToString();
        }

        private static string FormatPlain(string value, string autoLinkBase)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var autoLinked = new StringBuilder();
            int offset = 0;
            foreach (Match match in AutoLinkRegex.Matches(value))
            {
                autoLinked.Append(FormatMarkup(value.Substring(offset, match.Index - offset)));
                string display = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
                string suffix = match.Value.Substring(display.Length);
                string url = PurrMarkdownUrl.ResolveLink(autoLinkBase, display);
                autoLinked.Append(string.IsNullOrEmpty(url)
                    ? FormatMarkup(display)
                    : MakeLink(Escape(display), url));
                autoLinked.Append(Escape(suffix));
                offset = match.Index + match.Length;
            }
            autoLinked.Append(FormatMarkup(value.Substring(offset)));
            return autoLinked.ToString();
        }

        private static string FormatMarkup(string value)
        {
            value = Escape(value);
            var codeTokens = new List<string>();
            string tokenPrefix = null;
            const char tokenSuffix = '\uE001';
            value = CodeRegex.Replace(value, match =>
            {
                if (tokenPrefix == null)
                {
                    // A per-call marker avoids corrupting literal private-use characters in
                    // untrusted README text. The marker is removed before returning.
                    do
                    {
                        tokenPrefix = $"\uE000PurrCode{Guid.NewGuid():N}:";
                    } while (value.IndexOf(tokenPrefix, StringComparison.Ordinal) >= 0);
                }

                int index = codeTokens.Count;
                codeTokens.Add($"<color=#88cccc>{match.Groups[1].Value}</color>");
                return $"{tokenPrefix}{index}{tokenSuffix}";
            });
            value = BoldAsteriskRegex.Replace(value, "<b>$1</b>");
            value = BoldUnderscoreRegex.Replace(value, "<b>$1</b>");
            value = ItalicAsteriskRegex.Replace(value, "<i>$1</i>");
            value = ItalicUnderscoreRegex.Replace(value, "<i>$1</i>");

            if (codeTokens.Count == 0)
                return value;

            // Restore all code spans in one forward pass. Replacing once per token made a
            // code-heavy maximum-size block quadratic on Unity's IMGUI thread.
            var restored = new StringBuilder(value.Length + codeTokens.Count * 16);
            int offset = 0;
            while (offset < value.Length)
            {
                int marker = value.IndexOf(tokenPrefix, offset, StringComparison.Ordinal);
                if (marker < 0)
                {
                    restored.Append(value, offset, value.Length - offset);
                    break;
                }

                restored.Append(value, offset, marker - offset);
                int digitsStart = marker + tokenPrefix.Length;
                int suffix = value.IndexOf(tokenSuffix, digitsStart);
                if (suffix < 0 || !int.TryParse(value.Substring(digitsStart, suffix - digitsStart),
                        NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 || index >= codeTokens.Count)
                {
                    // This cannot occur for markers emitted above, but preserve text safely if
                    // future formatting changes ever split a marker.
                    restored.Append(value, marker, value.Length - marker);
                    break;
                }

                restored.Append(codeTokens[index]);
                offset = suffix + 1;
            }

            return restored.ToString();
        }

        private static string MakeLink(string label, string url)
        {
            return $"<a href=\"{EscapeAttribute(url)}\"><color=#66aaff>{label}</color></a>";
        }

        internal static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string EscapeAttribute(string value)
        {
            return Escape(value).Replace("\"", "&quot;");
        }
    }

    internal static class PurrMarkdownUrl
    {
        private const string ImageProxyBase = "https://purrnet.dev/api/packages/image-proxy?url=";
        private static readonly HashSet<string> GithubImageHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "raw.githubusercontent.com",
            "private-user-images.githubusercontent.com",
            "github.githubassets.com",
            "user-images.githubusercontent.com"
        };

        public static string ResolveImage(string baseUrl, string candidate)
        {
            string resolved = Resolve(baseUrl, candidate, false);
            if (string.IsNullOrEmpty(resolved) || !Uri.TryCreate(resolved, UriKind.Absolute, out var uri))
                return null;

            if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                (uri.AbsolutePath.Contains("/blob/") || uri.AbsolutePath.Contains("/raw/")))
            {
                var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 5 && (segments[2] == "blob" || segments[2] == "raw"))
                {
                    string path = string.Join("/", segments.Skip(4));
                    uri = new Uri($"https://raw.githubusercontent.com/{segments[0]}/{segments[1]}/{segments[3]}/{path}{uri.Query}");
                }
            }

            // Shields defaults to SVG, which Texture2D cannot decode. Its documented
            // raster endpoint is the same badge path with a .png suffix.
            if (uri.Host.Equals("img.shields.io", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(uri.AbsolutePath, @"\.(?:png|jpe?g)$", RegexOptions.IgnoreCase))
            {
                var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + ".png" };
                uri = builder.Uri;
            }

            string result = GithubImageHosts.Contains(uri.Host)
                ? ImageProxyBase + Uri.EscapeDataString(uri.AbsoluteUri)
                : uri.AbsoluteUri;
            return TryNormalizeImageUrl(result, out string normalized, out _) ? normalized : null;
        }

        public static string ResolveLink(string baseUrl, string candidate)
        {
            return Resolve(baseUrl, candidate, true);
        }

        private static string Resolve(string baseUrl, string candidate, bool allowFragment)
        {
            candidate = WebUtility.HtmlDecode(candidate ?? string.Empty).Trim();
            if (candidate.Length > 4096)
                return null;
            if (candidate.Length > 1 && candidate[0] == '<' && candidate[^1] == '>')
                candidate = candidate.Substring(1, candidate.Length - 2);
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            if (allowFragment && candidate[0] == '#' &&
                Uri.TryCreate(baseUrl, UriKind.Absolute, out var fragmentBase))
                return new Uri(fragmentBase.GetLeftPart(UriPartial.Path) + candidate).AbsoluteUri;

            Uri uri;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
            }
            else if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                if (candidate.StartsWith("/", StringComparison.Ordinal) && TryResolveRepoRoot(baseUri, candidate, out var repoRoot))
                    uri = repoRoot;
                else if (!Uri.TryCreate(baseUri, candidate, out uri))
                    return null;
            }
            else
            {
                return null;
            }

            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                return null;
            if (!string.IsNullOrEmpty(uri.UserInfo) || IsObviouslyLocal(uri.DnsSafeHost))
                return null;
            return uri.AbsoluteUri;
        }

        internal static bool TryNormalizeImageUrl(string absoluteUrl, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(absoluteUrl) ||
                !Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
            {
                error = "Image URL is not an absolute URL.";
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Only HTTPS image URLs are supported.";
                return false;
            }

            if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Image URL has an invalid host or embedded credentials.";
                return false;
            }

            string host = uri.DnsSafeHost.TrimEnd('.');
            if (IsObviouslyLocal(host))
            {
                error = "Private or local image hosts are not allowed.";
                return false;
            }

            try
            {
                // Strip fragments (which never identify different response bytes) and a
                // trailing DNS root dot so every redirect is checked in one canonical form.
                var builder = new UriBuilder(uri) { Host = host, Fragment = string.Empty };
                normalized = builder.Uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
                return true;
            }
            catch (UriFormatException)
            {
                error = "Image URL could not be normalized.";
                return false;
            }
        }

        internal static bool TryResolveImageRedirect(string currentUrl, string location,
            out string normalized, out string error)
        {
            normalized = null;
            error = null;
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var current) ||
                string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(current, location, out var redirect))
            {
                error = "Image redirect has an invalid Location header.";
                return false;
            }

            return TryNormalizeImageUrl(redirect.AbsoluteUri, out normalized, out error);
        }

        internal static bool TryValidatePublicAddresses(IEnumerable<IPAddress> addresses, out string error)
        {
            error = null;
            bool found = false;
            if (addresses != null)
            {
                foreach (var address in addresses)
                {
                    found = true;
                    // Reject mixed public/private DNS answers too: a client cannot control
                    // which answer UnityWebRequest ultimately chooses.
                    if (address == null || IsPrivateOrLocalAddress(address))
                    {
                        error = "Image host resolves to a private, local, or reserved address.";
                        return false;
                    }
                }
            }

            if (found)
                return true;
            error = "Image host did not resolve to an address.";
            return false;
        }

        private static bool TryResolveRepoRoot(Uri baseUri, string candidate, out Uri resolved)
        {
            resolved = null;
            var segments = baseUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (baseUri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 3)
            {
                resolved = new Uri($"https://raw.githubusercontent.com/{segments[0]}/{segments[1]}/{segments[2]}/{candidate.TrimStart('/')}");
                return true;
            }
            if (baseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                segments.Length >= 4 && segments[2] == "blob")
            {
                resolved = new Uri($"https://github.com/{segments[0]}/{segments[1]}/blob/{segments[3]}/{candidate.TrimStart('/')}");
                return true;
            }
            return false;
        }

        private static bool IsObviouslyLocal(string host)
        {
            host = host?.TrimEnd('.');
            if (string.IsNullOrWhiteSpace(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!IPAddress.TryParse(host, out var address))
                return host.IndexOf('.') < 0;
            return IsPrivateOrLocalAddress(address);
        }

        internal static bool IsPrivateOrLocalAddress(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
                return true;

            if (address.IsIPv4MappedToIPv6)
                return IsPrivateOrLocalAddress(address.MapToIPv4());

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127 ||
                       (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                       (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
                       (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) ||
                       (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                       (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
                       bytes[0] >= 224;
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6 || bytes.Length != 16)
                return true;

            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes[0] == 0xff ||
                (bytes[0] & 0xfe) == 0xfc)
                return true;

            // Today, publicly routed IPv6 unicast space is within 2000::/3. Treat every
            // other allocation as reserved rather than attempting a request to it.
            if ((bytes[0] & 0xe0) != 0x20)
                return true;

            // IETF protocol assignments (2001::/23), documentation (2001:db8::/32 and
            // 3fff::/20), and deprecated 6to4 (2002::/16) are not public image endpoints.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] & 0xfe) == 0)
                return true;
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
                return true;
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
                return true;
            return bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0;
        }
    }
}
