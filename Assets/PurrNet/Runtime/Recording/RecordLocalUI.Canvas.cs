#if PURRNET_UGUI
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if PURRNET_TMP
using TMPro;
#endif

namespace PurrNet.Recording
{
    // uGUI: canvases, their graphics (Image, RawImage, Text, TextMeshPro) and controls.
    public sealed partial class RecordLocalUI
    {
        sealed partial class Element
        {
            public RecordLocalUI owner;
            public Transform transform;
            public RectTransform rect;
            public Canvas canvas;
            public Graphic graphic;
            public CanvasRenderer renderer;
            public Selectable selectable;
            public Mask mask;
            /// <summary>The input field whose typed text this graphic shows.</summary>
            public Selectable input;
            public UnityAction onDirty;
            public Matrix4x4 worldToRoot;
            public Color tint;
            public float textSize;
            public int graphicCount = -1;
            public bool redacted;
            /// <summary>Never written: under a <see cref="DontRecordUI"/>, or a part of another graphic.</summary>
            public bool excluded;
            public bool part;

            public void MarkDirty()
            {
                content = true;
                owner.Queue(this);
            }
        }

        // The registry's canvases are read through its private dictionary: there is no public list of canvases,
        // and searching the scene allocates. Without the field (another uGUI version) the scene is searched instead.
        static readonly FieldInfo RegistryGraphics =
            typeof(GraphicRegistry).GetField("m_Graphics", BindingFlags.Instance | BindingFlags.NonPublic);

        const float CanvasSearchInterval = 0.25f;

        readonly Dictionary<Transform, Element> _byTransform = new Dictionary<Transform, Element>(ReferenceComparer<Transform>.instance);
        readonly Dictionary<Canvas, Element> _byCanvas = new Dictionary<Canvas, Element>(ReferenceComparer<Canvas>.instance);
        readonly Dictionary<Graphic, Element> _byGraphic = new Dictionary<Graphic, Element>(ReferenceComparer<Graphic>.instance);
        readonly Dictionary<Selectable, Element> _bySelectable = new Dictionary<Selectable, Element>(ReferenceComparer<Selectable>.instance);
        ICollection<Canvas> _registryCanvases;
        Canvas[] _canvases = new Canvas[8];
        Selectable[] _selectables = new Selectable[32];
        int _canvasCount;
        int _selectableCount = -1;
        float _nextCanvasSearch;
        bool _rescan;

        void EnableCanvases()
        {
            // The registry lays canvases out from the same event; created first, it runs before this does.
            _ = CanvasUpdateRegistry.instance;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            try
            {
                if (RegistryGraphics?.GetValue(GraphicRegistry.instance) is System.Collections.IDictionary graphics)
                    _registryCanvases = graphics.Keys as ICollection<Canvas>;
            }
            catch
            {
                _registryCanvases = null;
            }
        }

        void DisableCanvases() => Canvas.willRenderCanvases -= OnWillRenderCanvases;

        void OnWillRenderCanvases()
        {
            _lastRenderFrame = Time.frameCount;
            Pass();
        }

        void ClearCanvases()
        {
            foreach (var pair in _byGraphic)
                Unhook(pair.Value);
            _byTransform.Clear();
            _byCanvas.Clear();
            _byGraphic.Clear();
            _bySelectable.Clear();
            _selectableCount = -1;
            _canvasCount = 0;
        }

        static void Unhook(Element element)
        {
            if (element.graphic == null || element.onDirty == null)
                return;
            element.graphic.UnregisterDirtyVerticesCallback(element.onDirty);
            element.graphic.UnregisterDirtyMaterialCallback(element.onDirty);
        }

        // ---- Finding canvases, graphics and controls ----------------------------------------------------

        void DiscoverCanvases()
        {
            int count = _canvasCount;
            if (_registryCanvases != null)
            {
                count = _registryCanvases.Count;
                if (_canvases.Length < count)
                    _canvases = new Canvas[Mathf.NextPowerOfTwo(count)];
                _registryCanvases.CopyTo(_canvases, 0);
            }
            else if (Time.unscaledTime >= _nextCanvasSearch)
            {
                _nextCanvasSearch = Time.unscaledTime + CanvasSearchInterval;
#if UNITY_2023_1_OR_NEWER
                _canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
#else
                _canvases = FindObjectsOfType<Canvas>();
#endif
                count = _canvases.Length;
            }
            _canvasCount = count;

            bool rescan = _rescan;
            _rescan = false;
            for (int i = 0; i < count; i++)
            {
                var canvas = _canvases[i];
                if (canvas == null)
                    continue;
                if (!_byCanvas.TryGetValue(canvas, out var element))
                    element = TrackCanvas(canvas);
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                int graphicCount = graphics.Count;
                if (graphicCount == element.graphicCount && !rescan)
                    continue;
                element.graphicCount = graphicCount;
                for (int g = 0; g < graphicCount; g++)
                {
                    var graphic = graphics[g];
                    if (graphic != null && !_byGraphic.ContainsKey(graphic))
                        TrackGraphic(graphic);
                }
            }

            // Controls without a graphic of their own (a slider, a toggle) are elements too.
            int selectableCount = Selectable.allSelectableCount;
            if (selectableCount == _selectableCount && !rescan)
                return;
            _selectableCount = selectableCount;
            if (_selectables.Length < selectableCount)
                _selectables = new Selectable[Mathf.NextPowerOfTwo(selectableCount)];
            int found = Selectable.AllSelectablesNoAlloc(_selectables);
            for (int i = 0; i < found; i++)
            {
                var selectable = _selectables[i];
                _selectables[i] = null;
                if (selectable == null || _bySelectable.ContainsKey(selectable))
                    continue;
                var element = ElementFor(selectable.transform);
                element.selectable = selectable;
                element.content = true;
                _bySelectable[selectable] = element;
            }
        }

        Element ElementFor(Transform transform)
        {
            if (_byTransform.TryGetValue(transform, out var element))
            {
                Queue(element);
                return element;
            }
            element = Add(false);
            element.owner = this;
            element.transform = transform;
            element.rect = transform as RectTransform;
            element.excluded = Excluded(transform);
            _byTransform[transform] = element;
            return element;
        }

        Element TrackCanvas(Canvas canvas)
        {
            var element = ElementFor(canvas.transform);
            element.canvas = canvas;
            element.content = true;
            _byCanvas[canvas] = element;
            // The root draws its nested canvases' graphics but may have none of its own, so the registry has not
            // heard of it.
            var root = canvas.rootCanvas;
            if (root != null && !ReferenceEquals(root, canvas) && !_byCanvas.ContainsKey(root))
                TrackCanvas(root);
            return element;
        }

        void TrackGraphic(Graphic graphic)
        {
            var element = ElementFor(graphic.transform);
            element.graphic = graphic;
            element.rect = graphic.rectTransform;
            element.renderer = graphic.canvasRenderer;
            element.content = true;
            _byGraphic[graphic] = element;
#if PURRNET_TMP
            // The pieces a TextMeshPro text or input field draws itself with.
            element.part = graphic is TMP_SubMeshUI || graphic is TMP_SelectionCaret;
#endif
            if (element.part)
            {
                element.excluded = true;
                return;
            }

            element.onDirty ??= element.MarkDirty;
            graphic.RegisterDirtyVerticesCallback(element.onDirty);
            graphic.RegisterDirtyMaterialCallback(element.onDirty);
            graphic.TryGetComponent(out element.selectable);
            graphic.TryGetComponent(out element.mask);
            element.input = InputOf(graphic);
        }

        /// <summary>The input field that shows what was typed into it through <paramref name="graphic"/>, if any.</summary>
        static Selectable InputOf(Graphic graphic)
        {
            if (graphic is Text)
            {
                var field = graphic.GetComponentInParent<InputField>(true);
                return field != null && ReferenceEquals(field.textComponent, graphic) ? field : null;
            }
#if PURRNET_TMP
            if (graphic is TMP_Text)
            {
                var field = graphic.GetComponentInParent<TMP_InputField>(true);
                return field != null && ReferenceEquals(field.textComponent, graphic) ? field : null;
            }
#endif
            return null;
        }

        static bool Excluded(Transform transform)
        {
            var markers = DontRecordUI.all;
            for (int i = 0; i < markers.Count; i++)
                if (transform.IsChildOf(markers[i].transform))
                    return true;
            return false;
        }

        void ExclusionsChanged()
        {
            foreach (var pair in _byTransform)
            {
                var element = pair.Value;
                if (element.transform != null)
                    element.excluded = element.part || Excluded(element.transform);
            }
        }

        /// <summary>The object is gone, or has nothing left to record.</summary>
        void Release(Element element)
        {
            Drop(element);
            Unhook(element);
            if (element.graphic is not null)
                _byGraphic.Remove(element.graphic);
            if (element.canvas is not null)
                _byCanvas.Remove(element.canvas);
            if (element.selectable is not null)
                _bySelectable.Remove(element.selectable);
            _byTransform.Remove(element.transform);
            _rescan = true;
        }

        // ---- Reading an element -----------------------------------------------------------------------

        void CaptureCanvasElement(Element element)
        {
            var transform = element.transform;
            if (transform == null)
            {
                Release(element);
                return;
            }

            // A component removed from an object that lives on.
            var graphic = element.graphic;
            if (graphic is not null && graphic == null)
            {
                _byGraphic.Remove(graphic);
                element.graphic = graphic = null;
                element.renderer = null;
                element.content = true;
            }
            var canvas = element.canvas;
            if (canvas is not null && canvas == null)
            {
                _byCanvas.Remove(canvas);
                element.canvas = canvas = null;
                element.content = true;
            }
            var selectable = element.selectable;
            if (selectable is not null && selectable == null)
            {
                _bySelectable.Remove(selectable);
                element.selectable = selectable = null;
                element.content = true;
            }
            if (graphic is null && canvas is null && selectable is null)
            {
                Release(element);
                return;
            }

            bool shown = !element.excluded &&
                         (graphic is not null ? graphic.isActiveAndEnabled && graphic.canvas != null
                             : canvas is not null ? canvas.isActiveAndEnabled
                             : selectable.isActiveAndEnabled);
            Element parent = null;
            if (shown)
            {
                // The nearest recorded element above; parents are read first so a new subtree arrives top-down.
                for (var above = transform.parent; above is not null; above = above.parent)
                {
                    if (!_byTransform.TryGetValue(above, out var candidate))
                        continue;
                    CaptureFirst(candidate);
                    if (!candidate.live)
                        continue;
                    parent = candidate;
                    break;
                }
                // Only a canvas can be a root.
                shown = parent != null || canvas is not null;
            }
            if (!shown)
            {
                if (element.live)
                {
                    Hide(element);
                    _rescan = true;
                }
                return;
            }

            var root = parent != null ? parent.root : element;
            element.parent = parent;
            element.root = root;

            var sample = element.last;
            var flags = UIFlags.Visible;
            var rect = element.rect;
            Rect local = default;
            if (parent == null)
            {
                CaptureRoot(element, ref sample, ref flags);
                local = element.rootRect;
            }
            else if (rect is not null)
            {
                var toRoot = root.worldToRoot * transform.localToWorldMatrix;
                local = rect.rect;
                var bounds = root.rootRect;
                SetRect(root,
                    ToRoot(toRoot, bounds, local.xMin, local.yMax),
                    ToRoot(toRoot, bounds, local.xMax, local.yMax),
                    ToRoot(toRoot, bounds, local.xMin, local.yMin), ref sample);
                sample.order = 0;
                sample.worldPosition = default;
                sample.worldRotation = default;
                sample.worldSize = default;
            }

            if (element.content)
            {
                element.content = false;
                ReadContent(element, ref sample);
            }

            sample.color = default;
            sample.textColor = default;
            if (graphic is not null)
            {
                var renderer = element.renderer;
                var colour = element.tint * renderer.GetColor();
                var drawn = ToColor(colour, renderer.GetInheritedAlpha());
                if (sample.kind == UIKind.Text)
                    sample.textColor = drawn;
                else
                    sample.color = drawn;
                if (parent != null)
                    sample.order = renderer.absoluteDepth;
                if (drawn.a == 0 || renderer.cull)
                    flags &= ~UIFlags.Visible;
                var mask = element.mask;
                if (mask is not null && mask != null && mask.enabled && !mask.showMaskGraphic)
                    flags &= ~UIFlags.Visible;
                if (graphic.raycastTarget)
                    flags |= UIFlags.Pickable;
                sample.fontSize = local.height > 0f ? element.textSize * sample.height / local.height : 0f;
            }
            else
                sample.fontSize = 0f;

            sample.value = 0f;
            if (selectable is not null)
            {
                if (selectable.IsInteractable() && selectable.isActiveAndEnabled)
                    flags |= UIFlags.Interactable;
                switch (sample.widget)
                {
                    case UIWidget.Slider:
                        sample.value = ((Slider)selectable).normalizedValue;
                        break;
                    case UIWidget.Toggle:
                        sample.value = ((Toggle)selectable).isOn ? 1f : 0f;
                        break;
                    case UIWidget.Scrollbar:
                        sample.value = ((Scrollbar)selectable).value;
                        break;
                }
            }
            if (element.redacted)
                flags |= UIFlags.Redacted;
            sample.flags = flags;
            Commit(element, ref sample);
        }

        static Vector2 ToRoot(in Matrix4x4 toRoot, in Rect bounds, float x, float y)
        {
            var point = toRoot.MultiplyPoint3x4(new Vector3(x, y, 0f));
            return new Vector2(point.x - bounds.xMin, bounds.yMax - point.y);
        }

        /// <summary>A root canvas: where it is on screen, or in the world.</summary>
        void CaptureRoot(Element element, ref UISample sample, ref UIFlags flags)
        {
            var canvas = element.canvas;
            var transform = element.transform;
            var local = element.rect is not null ? element.rect.rect : default;
            element.rootRect = local;
            element.worldToRoot = transform.worldToLocalMatrix;
            sample.rotation = 0f;
            sample.order = canvas.sortingOrder;
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                flags |= UIFlags.WorldSpace;
                element.screenX = element.screenY = 0f;
                element.screenWidth = element.screenHeight = 1f;
                var scale = transform.lossyScale;
                sample.worldPosition = transform.TransformPoint(local.center);
                sample.worldRotation = transform.rotation;
                sample.worldSize = new Vector2(local.width * scale.x, local.height * scale.y);
            }
            else
            {
                // Overlay and camera canvases cover their pixel rectangle whatever the camera does.
                var pixels = canvas.pixelRect;
                float width = _screenWidth > 0 ? _screenWidth : 1f, height = _screenHeight > 0 ? _screenHeight : 1f;
                element.screenX = pixels.x / width;
                element.screenY = 1f - (pixels.y + pixels.height) / height;
                element.screenWidth = pixels.width / width;
                element.screenHeight = pixels.height / height;
                sample.worldPosition = default;
                sample.worldRotation = default;
                sample.worldSize = default;
            }
            sample.x = element.screenX;
            sample.y = element.screenY;
            sample.width = element.screenWidth;
            sample.height = element.screenHeight;
        }

        /// <summary>What the element shows: read when it appears and when uGUI says its graphic changed.</summary>
        void ReadContent(Element element, ref UISample sample)
        {
            var graphic = element.graphic;
            var selectable = element.selectable;
            sample.name = _session.Name(element.transform.gameObject);
            sample.sprite = null;
            sample.text = null;
            sample.fill = UIFill.None;
            sample.fillOrigin = 0;
            sample.fillAmount = 1f;
            sample.align = UIAlign.UpperLeft;
            element.textSize = 0f;
            element.redacted = false;
            element.tint = Color.white;

            sample.widget = UIWidget.None;
            if (selectable is not null)
            {
                sample.widget = selectable switch
                {
                    Button _ => UIWidget.Button,
                    Toggle _ => UIWidget.Toggle,
                    Slider _ => UIWidget.Slider,
                    Scrollbar _ => UIWidget.Scrollbar,
                    Dropdown _ => UIWidget.Dropdown,
                    InputField _ => UIWidget.InputField,
#if PURRNET_TMP
                    TMP_Dropdown _ => UIWidget.Dropdown,
                    TMP_InputField _ => UIWidget.InputField,
#endif
                    _ => UIWidget.Other
                };
            }

            if (graphic is null)
            {
                sample.kind = element.canvas is not null ? UIKind.Canvas : UIKind.Element;
                sample.type = TypeName(element.canvas is not null ? typeof(Canvas) : selectable.GetType());
                return;
            }

            sample.type = TypeName(graphic.GetType());
            element.tint = graphic.color;
            switch (graphic)
            {
                case Image image:
                {
                    sample.kind = UIKind.Image;
                    var sprite = image.overrideSprite;
                    if (sprite != null)
                        sample.sprite = _session.Name(sprite);
                    if (image.type == Image.Type.Filled)
                    {
                        sample.fillAmount = image.fillAmount;
                        sample.fillOrigin = (byte)image.fillOrigin;
                        sample.fill = image.fillMethod == Image.FillMethod.Horizontal ? UIFill.Horizontal
                            : image.fillMethod == Image.FillMethod.Vertical ? UIFill.Vertical
                            : UIFill.Radial;
                    }
                    break;
                }
                case RawImage raw:
                {
                    sample.kind = UIKind.RawImage;
                    var texture = raw.texture;
                    if (texture != null)
                        sample.sprite = _session.Name(texture);
                    break;
                }
                case Text text:
                    sample.kind = UIKind.Text;
                    sample.align = (UIAlign)text.alignment;
                    element.textSize = text.fontSize;
                    element.redacted = Redacts(element.input);
                    if (!element.redacted)
                        sample.text = text.text;
                    break;
#if PURRNET_TMP
                case TMP_Text text:
                    sample.kind = UIKind.Text;
                    sample.align = Align(text.horizontalAlignment, text.verticalAlignment);
                    element.textSize = text.fontSize;
                    element.redacted = Redacts(element.input);
                    if (!element.redacted)
                        sample.text = text.text;
                    break;
#endif
                default:
                    sample.kind = UIKind.Element;
                    break;
            }
        }

        /// <summary>Whether what was typed into <paramref name="input"/> stays out of the recording.</summary>
        bool Redacts(Selectable input)
        {
            if (input is null || input == null)
                return false;
            if (_redactTextInput)
                return true;
            if (input is InputField field)
                return field.inputType == InputField.InputType.Password ||
                       field.contentType == InputField.ContentType.Password ||
                       field.contentType == InputField.ContentType.Pin;
#if PURRNET_TMP
            if (input is TMP_InputField tmp)
                return tmp.inputType == TMP_InputField.InputType.Password ||
                       tmp.contentType == TMP_InputField.ContentType.Password ||
                       tmp.contentType == TMP_InputField.ContentType.Pin;
#endif
            return true;
        }

#if PURRNET_TMP
        static UIAlign Align(HorizontalAlignmentOptions horizontal, VerticalAlignmentOptions vertical)
        {
            int column = horizontal == HorizontalAlignmentOptions.Right ? 2
                : horizontal == HorizontalAlignmentOptions.Center || horizontal == HorizontalAlignmentOptions.Geometry ? 1
                : 0;
            int row = vertical == VerticalAlignmentOptions.Top ? 0
                : vertical == VerticalAlignmentOptions.Bottom || vertical == VerticalAlignmentOptions.Baseline ? 2
                : 1;
            return (UIAlign)(row * 3 + column);
        }
#endif
    }
}
#endif
