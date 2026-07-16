using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

            Assert.That(document.blocks.Select(block => block.kind), Is.EqualTo(new[]
            {
                PurrMarkdownBlockKind.Heading,
                PurrMarkdownBlockKind.Paragraph,
                PurrMarkdownBlockKind.UnorderedList,
                PurrMarkdownBlockKind.Code,
                PurrMarkdownBlockKind.Image
            }));
            Assert.That(document.blocks[3].text, Does.Contain("List<int>"));
            Assert.That(Uri.UnescapeDataString(document.blocks[4].url),
                Does.EndWith("https://raw.githubusercontent.com/PurrNet/PurrNet/dev/images/diagram.png"));

            string paragraph = PurrMarkdownInline.ToRichText(document.blocks[1].text, SourceUrl);
            Assert.That(paragraph,
                Does.Contain("href=\"https://github.com/PurrNet/PurrNet/blob/dev/docs/setup.md\""));
        }

        [Test]
        public void Parse_HandlesHtmlImageInsideAnchor()
        {
            const string markdown = "<a href=\"https://discord.gg/purrnet\">\n" +
                                    "<img width=\"256\" height=\"128\" alt=\"Discord\" " +
                                    "src=\"https://discord.com/widget.png\">\n</a>";

            var image = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).blocks.Single();

            Assert.That(image.kind, Is.EqualTo(PurrMarkdownBlockKind.Image));
            Assert.That(image.text, Is.EqualTo("Discord"));
            Assert.That(image.url, Is.EqualTo("https://discord.com/widget.png"));
            Assert.That(image.linkUrl, Is.EqualTo("https://discord.gg/purrnet"));
            Assert.That(image.requestedWidth, Is.EqualTo(256));
            Assert.That(image.requestedHeight, Is.EqualTo(128));
        }

        [Test]
        public void Parse_HandlesLinkedMarkdownBadge()
        {
            const string markdown =
                "Install [![openupm](https://img.shields.io/npm/v/purrnet)](https://openupm.com/purrnet/)";

            var blocks = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).blocks;

            Assert.That(blocks.Count, Is.EqualTo(2));
            Assert.That(blocks[0].text, Is.EqualTo("Install"));
            Assert.That(blocks[1].kind, Is.EqualTo(PurrMarkdownBlockKind.Image));
            Assert.That(blocks[1].url, Is.EqualTo("https://img.shields.io/npm/v/purrnet.png"));
            Assert.That(blocks[1].linkUrl, Is.EqualTo("https://openupm.com/purrnet/"));
        }

        [Test]
        public void Parse_OverlappingMarkdownAndHtmlImageMatches_DoNotThrow()
        {
            const string markdown =
                "![<img src=\"https://example.com/inner.png\">](https://example.com/outer.png)";

            PurrMarkdownDocument document = null;
            Assert.DoesNotThrow(() => document = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl));

            var image = document.blocks.Single();
            Assert.That(image.kind, Is.EqualTo(PurrMarkdownBlockKind.Image));
            Assert.That(image.url, Is.EqualTo("https://example.com/outer.png"));
        }

        [Test]
        public void Parse_ClosedInlineHtmlAnchor_DoesNotLeakToFollowingImage()
        {
            const string markdown = "<a href=\"https://example.com/wrong\">text</a>\n" +
                                    "<img src=\"https://example.com/image.png\">";

            var image = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).blocks
                .Single(block => block.kind == PurrMarkdownBlockKind.Image);

            Assert.That(image.linkUrl, Is.Null);
        }

        [Test]
        public void Parse_HtmlAnchorsApplyOnlyToImagesInsideTheirSourceRange()
        {
            const string markdown = "<img src=\"https://example.com/before.png\">" +
                                    "<a href=\"https://example.com/target\">" +
                                    "<img src=\"https://example.com/inside.png\"></a>";

            var images = PurrMarkdownParser.Parse(markdown, RawBase, SourceUrl).blocks
                .Where(block => block.kind == PurrMarkdownBlockKind.Image).ToArray();

            Assert.That(images, Has.Length.EqualTo(2));
            Assert.That(images[0].linkUrl, Is.Null);
            Assert.That(images[1].linkUrl, Is.EqualTo("https://example.com/target"));
        }

        [Test]
        public void InlineRenderer_EscapesUntrustedRichTextAndRejectsUnsafeLinks()
        {
            string rendered = PurrMarkdownInline.ToRichText(
                "<color=red>owned</color> [bad](javascript:alert(1))", SourceUrl);

            Assert.That(rendered, Does.Contain("&lt;color=red&gt;"));
            Assert.That(rendered, Does.Not.Contain("href=\"javascript:"));
        }

        [Test, Timeout(5000)]
        public void InlineRenderer_CodeHeavyInput_IsLinearAndPreservesPrivateUseText()
        {
            const string privateUseText = "\uE0000\uE001";
            string markdown = privateUseText + " " +
                              string.Join(" ", Enumerable.Repeat("`x`", 16000));

            var watch = Stopwatch.StartNew();
            string rendered = PurrMarkdownInline.ToRichText(markdown, SourceUrl);
            watch.Stop();

            Assert.That(rendered, Does.StartWith(privateUseText));
            Assert.That(rendered, Does.Not.Contain("PurrCode"));
            Assert.That(rendered.Split(new[] { "<color=#88cccc>" },
                StringSplitOptions.None).Length - 1, Is.EqualTo(16000));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
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
        [TestCase("https://localhost./image.png")]
        [TestCase("https://127.0.0.1/image.png")]
        [TestCase("https://[::]/image.png")]
        [TestCase("https://[::ffff:127.0.0.1]/image.png")]
        [TestCase("file:///tmp/image.png")]
        [TestCase("data:image/png;base64,AAAA")]
        public void ResolveImage_RejectsUnsafeUrls(string url)
        {
            Assert.That(PurrMarkdownUrl.ResolveImage(RawBase, url), Is.Null);
        }

        [Test]
        public void ImageRedirectValidation_NormalizesRelativeTargetsAndRejectsUnsafeTargets()
        {
            Assert.That(PurrMarkdownUrl.TryResolveImageRedirect(
                "https://example.com/images/old.png", "../new.png",
                out string normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("https://example.com/new.png"));

            Assert.That(PurrMarkdownUrl.TryResolveImageRedirect(
                "https://example.com/image.png", "http://example.com/insecure.png",
                out _, out _), Is.False);
            Assert.That(PurrMarkdownUrl.TryResolveImageRedirect(
                "https://example.com/image.png", "https://127.0.0.1/private.png",
                out _, out _), Is.False);
        }

        [Test]
        public void ImageDnsValidation_RejectsMixedAndReservedAddressSets()
        {
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.Parse("93.184.216.34")
            }, out _), Is.True);
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Loopback
            }, out _), Is.False);
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.IPv6Any
            }, out _), Is.False);
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.Parse("2001:db8::1")
            }, out _), Is.False);
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.Parse("100::1")
            }, out _), Is.False);
            Assert.That(PurrMarkdownUrl.TryValidatePublicAddresses(new[]
            {
                IPAddress.Parse("4000::1")
            }, out _), Is.False);
        }

        [Test]
        public void ImageCache_DoesNotQueueOffscreenOrUnsafeImages()
        {
            using var cache = new PurrMarkdownImageCache();

            var offscreen = cache.GetOrRequest("https://example.com/image.png", false);
            var unsafeImage = cache.GetOrRequest("http://127.0.0.1/image.png", true);

            Assert.That(offscreen.status, Is.EqualTo(PurrMarkdownImageCache.ImageStatus.Missing));
            Assert.That(unsafeImage.status, Is.EqualTo(PurrMarkdownImageCache.ImageStatus.Unsupported));
        }

        [Test]
        public void Renderer_RetriesTransientFailuresButNotUnsupportedImages()
        {
            Assert.That(PurrMarkdownRenderer.ShouldRequestNearViewport(
                PurrMarkdownImageCache.ImageStatus.Missing), Is.True);
            Assert.That(PurrMarkdownRenderer.ShouldRequestNearViewport(
                PurrMarkdownImageCache.ImageStatus.Failed), Is.True);
            Assert.That(PurrMarkdownRenderer.ShouldRequestNearViewport(
                PurrMarkdownImageCache.ImageStatus.Unsupported), Is.False);
            Assert.That(PurrMarkdownRenderer.ShouldRequestNearViewport(
                PurrMarkdownImageCache.ImageStatus.Ready), Is.False);
        }

        [Test]
        public void ImageCache_EnforcesDiskLimitAfterEveryWrite()
        {
            string root = Path.Combine(Path.GetTempPath(), "PurrMarkdownDiskCacheTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var cache = new PurrMarkdownImageCache(root, 10, false);
                cache.WriteDiskEntrySafe("https://example.com/one.png", new byte[6]);
                cache.WriteDiskEntrySafe("https://example.com/two.png", new byte[6]);

                var files = new DirectoryInfo(root).GetFiles("*.img");
                Assert.That(files.Sum(file => file.Length), Is.LessThanOrEqualTo(10));
                Assert.That(files, Has.Length.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void ReadmeCache_OldEpochCannotRepopulateAfterInvalidation()
        {
            var readme = JsonConvert.DeserializeObject<PackageReadmeResponse>(
                "{\"readme_markdown\":\"private\"}");
            PurrPackageManagerCache.Invalidate();
            int oldEpoch = PurrPackageManagerCache.Epoch;
            Assert.That(PurrPackageManagerCache.TrySetPackageReadme("package", readme, oldEpoch), Is.True);

            PurrPackageManagerCache.Invalidate();

            Assert.That(PurrPackageManagerCache.TrySetPackageReadme("package", readme, oldEpoch), Is.False);
            Assert.That(PurrPackageManagerCache.TryGetPackageReadme("package", out _), Is.False);
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

            Assert.That(response.markdown, Is.EqualTo("# Hello"));
            Assert.That(response.sourceUrl, Does.Contain("github.com"));
            Assert.That(response.baseUrl, Does.Contain("raw.githubusercontent.com"));
            Assert.That(response.revision, Is.EqualTo("abc123"));
        }
    }
}
