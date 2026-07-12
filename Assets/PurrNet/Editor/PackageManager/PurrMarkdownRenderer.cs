using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    internal sealed class PurrMarkdownRenderer
    {
        private const float ImagePlaceholderHeight = 42f;
        private const float MaxImageHeight = 480f;
        private const float ImagePrefetchViewports = 1f;

        private readonly PurrMarkdownImageCache _images;
        private readonly Dictionary<PurrMarkdownBlock, string> _richText = new();
        private readonly Dictionary<HeightKey, float> _heightCache = new();

        private GUIStyle _bodyStyle;
        private GUIStyle _listStyle;
        private GUIStyle _quoteStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _imageStatusStyle;
        private GUIStyle[] _headingStyles;
        private bool _lastProSkin;
        private float _lastPixelsPerPoint;

        private readonly struct HeightKey : IEquatable<HeightKey>
        {
            private readonly PurrMarkdownBlock _block;
            private readonly int _width;
            private readonly int _style;

            public HeightKey(PurrMarkdownBlock block, int width, int style)
            {
                _block = block;
                _width = width;
                _style = style;
            }

            public bool Equals(HeightKey other) =>
                ReferenceEquals(_block, other._block) && _width == other._width && _style == other._style;

            public override bool Equals(object obj) => obj is HeightKey other && Equals(other);
            public override int GetHashCode() =>
                ((RuntimeHelpers.GetHashCode(_block) * 397) ^ _width) * 397 ^ _style;
        }

        public PurrMarkdownRenderer(PurrMarkdownImageCache images)
        {
            _images = images ?? throw new ArgumentNullException(nameof(images));
        }

        public void ClearLayoutCache()
        {
            _richText.Clear();
            _heightCache.Clear();
        }

        public void Draw(PurrMarkdownDocument document, float availableWidth,
            float scrollY, float viewportHeight)
        {
            if (document?.Blocks == null)
                return;

            EnsureStyles();
            availableWidth = Mathf.Max(40f, availableWidth);
            foreach (var block in document.Blocks)
            {
                switch (block.Kind)
                {
                    case PurrMarkdownBlockKind.Heading:
                        DrawTextBlock(block, HeadingStyle(block.Level), availableWidth,
                            100 + Mathf.Clamp(block.Level, 1, 6), document.LinkBaseUrl);
                        break;
                    case PurrMarkdownBlockKind.Paragraph:
                        DrawTextBlock(block, _bodyStyle, availableWidth, 1, document.LinkBaseUrl);
                        break;
                    case PurrMarkdownBlockKind.UnorderedList:
                    case PurrMarkdownBlockKind.OrderedList:
                        DrawList(block, document.LinkBaseUrl, availableWidth);
                        break;
                    case PurrMarkdownBlockKind.Code:
                        DrawCode(block, availableWidth);
                        break;
                    case PurrMarkdownBlockKind.Quote:
                        DrawQuote(block, availableWidth, document.LinkBaseUrl);
                        break;
                    case PurrMarkdownBlockKind.Rule:
                        DrawRule();
                        break;
                    case PurrMarkdownBlockKind.Image:
                        DrawImage(block, availableWidth, scrollY, viewportHeight);
                        break;
                }

                if (block.Kind != PurrMarkdownBlockKind.Rule)
                    GUILayout.Space(block.Kind == PurrMarkdownBlockKind.Heading ? 5f : 3f);
            }
        }

        private void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            if (_bodyStyle != null && _lastProSkin == proSkin &&
                Mathf.Approximately(_lastPixelsPerPoint, pixelsPerPoint))
                return;

            _lastProSkin = proSkin;
            _lastPixelsPerPoint = pixelsPerPoint;
            _heightCache.Clear();

            _bodyStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 11,
                margin = new RectOffset(0, 0, 1, 1)
            };
            _listStyle = new GUIStyle(_bodyStyle)
            {
                padding = new RectOffset(12, 0, 0, 0)
            };
            _quoteStyle = new GUIStyle(_bodyStyle)
            {
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(12, 6, 5, 5),
                normal = { textColor = proSkin
                    ? new Color(0.72f, 0.76f, 0.82f)
                    : new Color(0.25f, 0.29f, 0.35f) }
            };
            _codeStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false,
                fontSize = 10,
                padding = new RectOffset(8, 8, 6, 6)
            };
            _imageStatusStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 5, 5)
            };

            _headingStyles = new GUIStyle[6];
            int[] fontSizes = { 19, 16, 14, 12, 11, 11 };
            for (int i = 0; i < _headingStyles.Length; i++)
            {
                _headingStyles[i] = new GUIStyle(EditorStyles.boldLabel)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = fontSizes[i],
                    margin = new RectOffset(0, 0, i == 0 ? 8 : 5, 1)
                };
            }
        }

        private GUIStyle HeadingStyle(int level) => _headingStyles[Mathf.Clamp(level, 1, 6) - 1];

        private void DrawTextBlock(PurrMarkdownBlock block, GUIStyle style, float width, int styleId,
            string linkBaseUrl)
        {
            string text = RichText(block, linkBaseUrl);
            var content = new GUIContent(text);
            float height = GetHeight(block, content, style, width, styleId);
            var rect = GUILayoutUtility.GetRect(content, style,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));
            EditorGUI.LabelField(rect, content, style);
        }

        private void DrawList(PurrMarkdownBlock block, string linkBaseUrl, float width)
        {
            if (!_richText.TryGetValue(block, out string rendered))
            {
                var builder = new StringBuilder();
                for (int i = 0; i < block.Items.Count; i++)
                {
                    if (i > 0)
                        builder.AppendLine();
                    string prefix = block.Kind == PurrMarkdownBlockKind.OrderedList
                        ? $"{i + 1}.  "
                        : "•  ";
                    builder.Append(prefix);
                    builder.Append(PurrMarkdownInline.ToRichText(block.Items[i], linkBaseUrl));
                }
                rendered = builder.ToString();
                _richText[block] = rendered;
            }

            var content = new GUIContent(rendered);
            float height = GetHeight(block, content, _listStyle, width, 2);
            var rect = GUILayoutUtility.GetRect(content, _listStyle,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));
            EditorGUI.LabelField(rect, content, _listStyle);
        }

        private void DrawCode(PurrMarkdownBlock block, float width)
        {
            var content = new GUIContent(block.Text);
            float height = GetHeight(block, content, _codeStyle, width, 3);
            var rect = GUILayoutUtility.GetRect(content, _codeStyle,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));
            EditorGUI.SelectableLabel(rect, block.Text, _codeStyle);
        }

        private void DrawQuote(PurrMarkdownBlock block, float width, string linkBaseUrl)
        {
            string text = RichText(block, linkBaseUrl);
            var content = new GUIContent(text);
            float height = GetHeight(block, content, _quoteStyle, width, 4);
            var rect = GUILayoutUtility.GetRect(content, _quoteStyle,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.35f, 0.55f, 0.8f, 0.08f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height),
                    new Color(0.4f, 0.7f, 1f, 0.7f));
            }
            EditorGUI.LabelField(rect, content, _quoteStyle);
        }

        private static void DrawRule()
        {
            GUILayout.Space(3f);
            var rect = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 0.35f));
            GUILayout.Space(4f);
        }

        private void DrawImage(PurrMarkdownBlock block, float availableWidth,
            float scrollY, float viewportHeight)
        {
            var snapshot = _images.GetOrRequest(block.Url, false);
            CalculateImageSize(block, snapshot, availableWidth, out float width, out float height);
            var rowRect = GUILayoutUtility.GetRect(availableWidth, height,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));

            float prefetch = viewportHeight * ImagePrefetchViewports;
            bool nearViewport = rowRect.yMax >= scrollY - prefetch &&
                                rowRect.yMin <= scrollY + viewportHeight + prefetch;
            if (nearViewport && Event.current.type == EventType.Repaint &&
                snapshot.Status == PurrMarkdownImageCache.ImageStatus.Missing)
            {
                snapshot = _images.GetOrRequest(block.Url, true);
            }

            var imageRect = new Rect(rowRect.x + (rowRect.width - width) * 0.5f,
                rowRect.y, width, height);
            if (snapshot.Status == PurrMarkdownImageCache.ImageStatus.Ready && snapshot.Texture != null)
            {
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(imageRect, snapshot.Texture, ScaleMode.ScaleToFit, true);

                string target = block.LinkUrl ?? block.Url;
                if (!string.IsNullOrEmpty(target))
                {
                    EditorGUIUtility.AddCursorRect(imageRect, MouseCursor.Link);
                    if (GUI.Button(imageRect, GUIContent.none, GUIStyle.none))
                        Application.OpenURL(target);
                }
                return;
            }

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(imageRect, new Color(0.3f, 0.3f, 0.3f, 0.12f));

            string label;
            switch (snapshot.Status)
            {
                case PurrMarkdownImageCache.ImageStatus.Failed:
                    label = string.IsNullOrEmpty(block.Text) ? "Image unavailable" : block.Text;
                    break;
                case PurrMarkdownImageCache.ImageStatus.Unsupported:
                    label = string.IsNullOrEmpty(block.Text) ? "Unsupported image format" : block.Text;
                    break;
                default:
                    label = string.IsNullOrEmpty(block.Text) ? "Loading image…" : $"Loading {block.Text}…";
                    break;
            }

            GUI.Label(imageRect, new GUIContent(label, snapshot.Error), _imageStatusStyle);
            if (snapshot.Status == PurrMarkdownImageCache.ImageStatus.Failed ||
                snapshot.Status == PurrMarkdownImageCache.ImageStatus.Unsupported)
            {
                string target = block.LinkUrl ?? block.Url;
                if (!string.IsNullOrEmpty(target))
                {
                    EditorGUIUtility.AddCursorRect(imageRect, MouseCursor.Link);
                    if (GUI.Button(imageRect, GUIContent.none, GUIStyle.none))
                        Application.OpenURL(target);
                }
            }
        }

        private static void CalculateImageSize(PurrMarkdownBlock block,
            PurrMarkdownImageCache.ImageSnapshot snapshot, float availableWidth,
            out float width, out float height)
        {
            float naturalWidth = snapshot.Width > 0 ? snapshot.Width : block.RequestedWidth;
            float naturalHeight = snapshot.Height > 0 ? snapshot.Height : block.RequestedHeight;
            if (naturalWidth <= 0 || naturalHeight <= 0)
            {
                width = availableWidth;
                height = ImagePlaceholderHeight;
                return;
            }

            float maxWidth = block.RequestedWidth > 0
                ? Mathf.Min(availableWidth, block.RequestedWidth)
                : availableWidth;
            float maxHeight = block.RequestedHeight > 0
                ? Mathf.Min(MaxImageHeight, block.RequestedHeight)
                : MaxImageHeight;
            float scale = Mathf.Min(1f, Mathf.Min(maxWidth / naturalWidth, maxHeight / naturalHeight));
            width = Mathf.Max(1f, naturalWidth * scale);
            height = Mathf.Max(1f, naturalHeight * scale);
        }

        private string RichText(PurrMarkdownBlock block, string linkBaseUrl)
        {
            if (!_richText.TryGetValue(block, out string rendered))
            {
                rendered = PurrMarkdownInline.ToRichText(block.Text, linkBaseUrl);
                _richText[block] = rendered;
            }
            return rendered;
        }

        private float GetHeight(PurrMarkdownBlock block, GUIContent content,
            GUIStyle style, float width, int styleId)
        {
            int quantizedWidth = Mathf.Max(40, Mathf.FloorToInt(width / 8f) * 8);
            var key = new HeightKey(block, quantizedWidth, styleId);
            if (_heightCache.TryGetValue(key, out float height))
                return height;

            height = Mathf.Max(style.lineHeight, style.CalcHeight(content, quantizedWidth));
            _heightCache[key] = height;
            return height;
        }
    }
}
