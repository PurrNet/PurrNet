#if PURRNET_UITK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace PurrNet.Recording
{
    // UI Toolkit: the runtime panels (UIDocuments) and every element in them.
    public sealed partial class RecordLocalUI
    {
        sealed partial class Element
        {
            public VisualElement visual;
            public TextElement textElement;
            public UIWidget control;
            public bool panel;
            public int paintOrder;
            /// <summary>The element's opacity times that of everything above it.</summary>
            public float opacity = 1f;
        }

        // UI Toolkit has no public list of its runtime panels; its own sorted list is read instead. Without it
        // (another Unity version) the scene is searched for UIDocuments.
        static readonly Func<IList> PlayerPanels = FindPlayerPanels();

        static Func<IList> FindPlayerPanels()
        {
            try
            {
                var method = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
                    ?.GetMethod("GetSortedPlayerPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                if (method == null || !typeof(IList).IsAssignableFrom(method.ReturnType))
                    return null;
                return (Func<IList>)Delegate.CreateDelegate(typeof(Func<IList>), method);
            }
            catch
            {
                return null;
            }
        }

        readonly Dictionary<VisualElement, Element> _byVisual = new Dictionary<VisualElement, Element>(ReferenceComparer<VisualElement>.instance);
        readonly List<Element> _panels = new List<Element>(4);
        EventCallback<ChangeEvent<string>> _onTextChanged;
        float _nextDocumentSearch;
        bool _orderChanged;

        void ClearPanels()
        {
            foreach (var panel in _panels)
                panel.visual.UnregisterCallback(_onTextChanged, TrickleDown.TrickleDown);
            _panels.Clear();
            _byVisual.Clear();
        }

        // ---- Finding panels and elements ----------------------------------------------------------------

        void DiscoverPanels()
        {
            IList panels = null;
            if (PlayerPanels != null)
            {
                try { panels = PlayerPanels(); }
                catch { panels = null; }
            }

            if (panels != null)
            {
                for (int i = 0; i < panels.Count; i++)
                    if (panels[i] is IPanel panel)
                        TrackPanel(panel);
            }
            else if (Time.unscaledTime >= _nextDocumentSearch)
            {
                _nextDocumentSearch = Time.unscaledTime + 0.25f;
#if UNITY_2023_1_OR_NEWER
                var documents = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
#else
                var documents = FindObjectsOfType<UIDocument>();
#endif
                foreach (var document in documents)
                {
                    var panel = document.rootVisualElement?.panel;
                    if (panel != null)
                        TrackPanel(panel);
                }
            }
        }

        void TrackPanel(IPanel panel)
        {
            var tree = panel.visualTree;
            if (tree == null || _byVisual.ContainsKey(tree))
                return;
            var element = AddVisual(tree);
            element.panel = true;
            _panels.Add(element);
            // Text changes are announced by an event that passes through the panel's root.
            _onTextChanged ??= OnTextChanged;
            tree.RegisterCallback(_onTextChanged, TrickleDown.TrickleDown);
        }

        void OnTextChanged(ChangeEvent<string> change)
        {
            if (change.target is VisualElement visual && _byVisual.TryGetValue(visual, out var element))
                Queue(element);
        }

        /// <summary>Starts recording an element and everything in it.</summary>
        Element AddVisual(VisualElement visual)
        {
            var element = Add(true);
            element.visual = visual;
            element.textElement = visual as TextElement;
            element.control = ControlOf(visual);
            _byVisual[visual] = element;
            _orderChanged = true;
            var children = visual.hierarchy;
            int count = children.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = children[i];
                if (!_byVisual.ContainsKey(child))
                    AddVisual(child);
            }
            return element;
        }

        static UIWidget ControlOf(VisualElement visual)
        {
            switch (visual)
            {
                case Button _: return UIWidget.Button;
                case Toggle _:
                case RadioButton _: return UIWidget.Toggle;
                case Slider _:
                case SliderInt _: return UIWidget.Slider;
                case Scroller _: return UIWidget.Scrollbar;
                case AbstractProgressBar _: return UIWidget.ProgressBar;
                case DropdownField _: return UIWidget.Dropdown;
                case TextField _: return UIWidget.InputField;
                default: return UIWidget.None;
            }
        }

        /// <summary>The element left its panel: it and everything in it is forgotten.</summary>
        void DropTree(Element element)
        {
            var children = element.visual.hierarchy;
            int count = children.childCount;
            for (int i = 0; i < count; i++)
                if (_byVisual.TryGetValue(children[i], out var child))
                    DropTree(child);
            Drop(element);
            _byVisual.Remove(element.visual);
            _orderChanged = true;
            if (element.panel)
            {
                element.visual.UnregisterCallback(_onTextChanged, TrickleDown.TrickleDown);
                _panels.Remove(element);
            }
        }

        /// <summary>Hides everything in the element (it stopped being shown), or queues it (it is shown again).</summary>
        void SetTreeShown(Element element, bool shown)
        {
            var children = element.visual.hierarchy;
            int count = children.childCount;
            for (int i = 0; i < count; i++)
            {
                if (!_byVisual.TryGetValue(children[i], out var child))
                    continue;
                if (shown)
                    Queue(child);
                else if (child.live)
                {
                    Hide(child);
                    SetTreeShown(child, false);
                }
            }
        }

        // ---- Reading an element -----------------------------------------------------------------------

        void CaptureToolkit(Element element)
        {
            var visual = element.visual;
            var hierarchy = visual.hierarchy;
            Element parent = null;
            if (!element.panel)
            {
                var above = hierarchy.parent;
                if (above == null || visual.panel == null || !_byVisual.TryGetValue(above, out parent) || parent.dropped)
                {
                    DropTree(element);
                    return;
                }
                if (!ReferenceEquals(element.parent, parent))
                    _orderChanged = true;
                element.parent = parent;
                element.root = parent.root;
                CaptureFirst(parent);
            }
            else
            {
                if (visual.panel == null)
                {
                    DropTree(element);
                    return;
                }
                element.root = element;
            }

            int childCount = hierarchy.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = hierarchy[i];
                if (!_byVisual.ContainsKey(child))
                    AddVisual(child);
            }

            var style = visual.resolvedStyle;
            var layout = visual.layout;
            bool shown = !float.IsNaN(layout.width) && !float.IsNaN(layout.height) &&
                         style.display != DisplayStyle.None && !visual.ClassListContains(DontRecordUI.UssClass);
            if (element.panel)
                shown &= childCount > 0 && !InWorld(visual);
            else
                shown &= parent.live && !(parent.panel && MarkedDocument(visual));
            if (!shown)
            {
                if (element.live)
                {
                    Hide(element);
                    SetTreeShown(element, false);
                }
                return;
            }
            if (!element.live)
                SetTreeShown(element, true);
            if (_orderChanged)
                RenumberPanels();

            var sample = element.last;
            var flags = UIFlags.UIToolkit;
            var root = element.root;
            sample.worldPosition = default;
            sample.worldRotation = default;
            sample.worldSize = default;
            sample.fill = UIFill.None;
            sample.fillOrigin = 0;
            sample.fillAmount = 1f;
            if (element.panel)
            {
                element.rootRect = new Rect(0f, 0f, layout.width, layout.height);
                element.opacity = style.opacity;
                sample.x = sample.y = sample.rotation = 0f;
                sample.width = sample.height = 1f;
                sample.order = PanelOrder(visual);
            }
            else
            {
                var toPanel = visual.worldTransform;
                SetRect(root,
                    toPanel.MultiplyPoint3x4(Vector3.zero),
                    toPanel.MultiplyPoint3x4(new Vector3(layout.width, 0f, 0f)),
                    toPanel.MultiplyPoint3x4(new Vector3(0f, layout.height, 0f)), ref sample);
                element.opacity = style.opacity * parent.opacity;
                sample.order = element.paintOrder;
            }

            if (style.visibility == Visibility.Visible && element.opacity > 0f)
                flags |= UIFlags.Visible;
            if (visual.pickingMode == PickingMode.Position)
                flags |= UIFlags.Pickable;

            sample.name = visual.name;
            sample.type = TypeName(visual.GetType());
            sample.widget = element.control;
            sample.value = 0f;
            if (element.control != UIWidget.None)
            {
                if (visual.enabledInHierarchy)
                    flags |= UIFlags.Interactable;
                sample.value = ValueOf(visual);
            }

            // What fills the rectangle: a background image (or an Image element's picture), else the background colour.
            UnityEngine.Object picture;
            Color fill;
            if (visual is Image image)
            {
                picture = image.sprite;
                if (picture == null)
                    picture = image.image;
                if (picture == null)
                    picture = image.vectorImage;
                fill = image.tintColor;
            }
            else
            {
                var background = style.backgroundImage;
                picture = background.sprite;
                if (picture == null)
                    picture = background.texture;
                if (picture == null)
                    picture = background.renderTexture;
                if (picture == null)
                    picture = background.vectorImage;
                fill = picture != null ? style.unityBackgroundImageTintColor : style.backgroundColor;
            }
            sample.sprite = picture != null ? _session.Name(picture) : null;
            sample.color = ToColor(fill, element.opacity);

            sample.text = null;
            sample.textColor = default;
            sample.fontSize = 0f;
            sample.align = UIAlign.UpperLeft;
            var text = element.textElement;
            if (text != null)
            {
                // What is typed into a field is its inner text element's text; a password is never read.
                ITextEdition edition = text;
                if (edition.isPassword || (_redactTextInput && !edition.isReadOnly))
                    flags |= UIFlags.Redacted;
                else
                    sample.text = text.text;
                sample.textColor = ToColor(style.color, element.opacity);
                sample.fontSize = layout.height > 0f ? style.fontSize * sample.height / layout.height : 0f;
                sample.align = (UIAlign)style.unityTextAlign;
            }

            sample.kind = element.panel ? UIKind.Panel
                : !string.IsNullOrEmpty(sample.text) || (flags & UIFlags.Redacted) != 0 ? UIKind.Text
                : picture != null ? UIKind.Image
                : UIKind.Element;
            sample.flags = flags;
            Commit(element, ref sample);
        }

        static float ValueOf(VisualElement visual)
        {
            switch (visual)
            {
                case Slider slider: return Mathf.InverseLerp(slider.lowValue, slider.highValue, slider.value);
                case SliderInt slider: return Mathf.InverseLerp(slider.lowValue, slider.highValue, slider.value);
                case Toggle toggle: return toggle.value ? 1f : 0f;
                case RadioButton radio: return radio.value ? 1f : 0f;
                case Scroller scroller: return Mathf.InverseLerp(scroller.lowValue, scroller.highValue, scroller.value);
                case AbstractProgressBar bar: return Mathf.InverseLerp(bar.lowValue, bar.highValue, bar.value);
                default: return 0f;
            }
        }

        /// <summary>Whether <paramref name="visual"/> is the root of a document that has a <see cref="DontRecordUI"/>.</summary>
        static bool MarkedDocument(VisualElement visual)
        {
            var markers = DontRecordUI.all;
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].TryGetComponent<UIDocument>(out var document) && ReferenceEquals(document.rootVisualElement, visual))
                    return true;
            return false;
        }

        static int PanelOrder(VisualElement tree)
        {
#if UNITY_6000_0_OR_NEWER
            if (tree.panel is IRuntimePanel runtime && runtime.panelSettings != null)
                return (int)runtime.panelSettings.sortingOrder;
#endif
            return 0;
        }

        /// <summary>World-space panels are not recorded: their place in the world is not readable from here.</summary>
        static bool InWorld(VisualElement tree)
        {
#if UNITY_6000_4_OR_NEWER
            if (tree.panel is IRuntimePanel runtime && runtime.panelSettings != null)
                return runtime.panelSettings.renderMode == PanelRenderMode.WorldSpace;
#endif
            return false;
        }

        // ---- Paint order ------------------------------------------------------------------------------

        /// <summary>
        /// Numbers every element in the order its panel paints them (depth first), after elements were added, removed
        /// or moved. Elements whose number changed are written again.
        /// </summary>
        void RenumberPanels()
        {
            if (!_orderChanged)
                return;
            _orderChanged = false;
            for (int i = 0; i < _panels.Count; i++)
            {
                int next = 0;
                Renumber(_panels[i], ref next);
            }
        }

        void Renumber(Element element, ref int next)
        {
            if (!element.panel)
            {
                element.paintOrder = next++;
                if (element.live && element.last.order != element.paintOrder)
                {
                    element.last.order = element.paintOrder;
                    Write(ref element.last);
                }
            }
            var children = element.visual.hierarchy;
            int count = children.childCount;
            for (int i = 0; i < count; i++)
                if (_byVisual.TryGetValue(children[i], out var child) && !child.dropped)
                    Renumber(child, ref next);
        }
    }
}
#endif
