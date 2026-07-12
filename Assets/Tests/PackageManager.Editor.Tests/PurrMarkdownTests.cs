using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace PurrNet.Editor.Tests
{
    public class PurrMarkdownTests
    {
        private const string RawBase =
            "https://raw.githubusercontent.com/PurrNet/PurrNet/dev/";
        private const string SourceUrl =
            "https://github.com/PurrNet/PurrNet/blob/dev/README.md";

        [Test]
        public void Parse_BuildsCommonBlocksAndResolvesRelativeUrls()
        {
            const string markdown = "# PurrNet\n\nRead the [setup guide](docs/setup.md).\n\n" +
                                    "- Fast\n- Friendly\n\n```csharp\nList<int> values;\n```\n\n" +
                                    "![diagram](images/diagram.png)";

            var document = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl);

            Assert.That(document.Blocks.Select(block => block.Kind), Is.EqualTo(new[]
            {
                PurrMarkdownBlockKind.Heading,
                PurrMarkdownBlockKind.Paragraph,
                PurrMarkdownBlockKind.UnorderedList,
                PurrMarkdownBlockKind.Code,
                PurrMarkdownBlockKind.Image
            }));
            Assert.That(document.Blocks[3].Text, Does.Contain("List<int>"));
            Assert.That(Uri.UnescapeDataString(document.Blocks[4].Url),
                Does.EndWith("https://raw.githubusercontent.com/PurrNet/PurrNet/dev/images/diagram.png"));

            string paragraph = PurrMarkdownInline.ToRichText(document.Blocks[1].Text, SourceUrl);
            Assert.That(paragraph,
                Does.Contain("href=\"https://github.com/PurrNet/PurrNet/blob/dev/docs/setup.md\""));
        }

        [Test]
        public void Parse_HandlesHtmlImageInsideAnchor()
        {
            const string markdown = "<a href=\"https://discord.gg/purrnet\">\n" +
                                    "<img width=\"256\" height=\"128\" alt=\"Discord\" " +
                                    "src=\"https://discord.com/widget.png\">\n</a>";

            var image = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).Blocks.Single();

            Assert.That(image.Kind, Is.EqualTo(PurrMarkdownBlockKind.Image));
            Assert.That(image.Text, Is.EqualTo("Discord"));
            Assert.That(image.Url, Is.EqualTo("https://discord.com/widget.png"));
            Assert.That(image.LinkUrl, Is.EqualTo("https://discord.gg/purrnet"));
            Assert.That(image.RequestedWidth, Is.EqualTo(256));
            Assert.That(image.RequestedHeight, Is.EqualTo(128));
        }

        [Test]
        public void Parse_HandlesLinkedMarkdownBadge()
        {
            const string markdown =
                "Install [![openupm](https://img.shields.io/npm/v/purrnet)](https://openupm.com/purrnet/)";

            var blocks = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).Blocks;

            Assert.That(blocks.Count, Is.EqualTo(2));
            Assert.That(blocks[0].Text, Is.EqualTo("Install"));
            Assert.That(blocks[1].Kind, Is.EqualTo(PurrMarkdownBlockKind.Image));
            Assert.That(blocks[1].Url, Is.EqualTo("https://img.shields.io/npm/v/purrnet.png"));
            Assert.That(blocks[1].LinkUrl, Is.EqualTo("https://openupm.com/purrnet/"));
        }

        [Test]
        public void InlineRenderer_EscapesUntrustedRichTextAndRejectsUnsafeLinks()
        {
            string rendered = PurrMarkdownInline.ToRichText(
                "<color=red>owned</color> [bad](javascript:alert(1))", SourceUrl);

            Assert.That(rendered, Does.Contain("&lt;color=red&gt;"));
            Assert.That(rendered, Does.Not.Contain("href=\"javascript:"));
        }

        [Test]
        public void ResolveImage_UsesFirstPartyProxyForGithubAssets()
        {
            string url = PurrMarkdownUrl.ResolveImage(RawBase, "./images/logo.png");

            Assert.That(url, Does.StartWith("https://purrnet.dev/api/packages/image-proxy?url="));
            Assert.That(Uri.UnescapeDataString(url),
                Does.EndWith("https://raw.githubusercontent.com/PurrNet/PurrNet/dev/images/logo.png"));
        }

        [TestCase("https://localhost/image.png")]
        [TestCase("https://127.0.0.1/image.png")]
        [TestCase("file:///tmp/image.png")]
        [TestCase("data:image/png;base64,AAAA")]
        public void ResolveImage_RejectsUnsafeUrls(string url)
        {
            Assert.That(PurrMarkdownUrl.ResolveImage(RawBase, url), Is.Null);
        }

        [Test]
        public void ImageCache_DoesNotQueueOffscreenOrUnsafeImages()
        {
            using var cache = new PurrMarkdownImageCache();

            var offscreen = cache.GetOrRequest("https://example.com/image.png", false);
            var unsafeImage = cache.GetOrRequest("http://127.0.0.1/image.png", true);

            Assert.That(offscreen.Status, Is.EqualTo(PurrMarkdownImageCache.ImageStatus.Missing));
            Assert.That(unsafeImage.Status, Is.EqualTo(PurrMarkdownImageCache.ImageStatus.Unsupported));
        }

        [Test]
        public void ReadmeResponse_DeserializesApiContract()
        {
            const string json = "{" +
                                "\"readme_markdown\":\"# Hello\"," +
                                "\"readme_source_url\":\"https://github.com/o/r/blob/main/README.md\"," +
                                "\"readme_base_url\":\"https://raw.githubusercontent.com/o/r/main/\"," +
                                "\"readme_revision\":\"abc123\"}";

            var response = JsonConvert.DeserializeObject<PackageReadmeResponse>(json);

            Assert.That(response.Markdown, Is.EqualTo("# Hello"));
            Assert.That(response.SourceUrl, Does.Contain("github.com"));
            Assert.That(response.BaseUrl, Does.Contain("raw.githubusercontent.com"));
            Assert.That(response.Revision, Is.EqualTo("abc123"));
        }
    }
}
