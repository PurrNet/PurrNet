using System;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>What a row of the "ui" stream says.</summary>
    public enum UIOp : byte
    {
        /// <summary>The element appeared or changed: the row is its complete state.</summary>
        Update,
        /// <summary>The element is gone (destroyed, deactivated, or its canvas or panel stopped drawing).</summary>
        Remove,
        /// <summary>The screen's size in pixels (width, height). With the Snapshot flag every element is forgotten first.</summary>
        Screen
    }

    public enum UIKind : byte
    {
        /// <summary>A box without content of its own: a UI Toolkit element, a control without a graphic.</summary>
        Element,
        Canvas,
        /// <summary>A UI Toolkit panel.</summary>
        Panel,
        Image,
        RawImage,
        Text
    }

    public enum UIWidget : byte
    {
        None,
        Button,
        Toggle,
        Slider,
        Scrollbar,
        Dropdown,
        InputField,
        ProgressBar,
        Other
    }

    [Flags]
    public enum UIFlags : ushort
    {
        /// <summary>Drawn: not transparent, not culled, not hidden by its style.</summary>
        Visible = 1,
        /// <summary>A control that reacts to input right now.</summary>
        Interactable = 2,
        /// <summary>Catches the pointer (a raycast target, or a UI Toolkit element that can be picked).</summary>
        Pickable = 4,
        /// <summary>A root placed in the world: its rectangle is given by worldPosition, worldRotation and worldSize.</summary>
        WorldSpace = 8,
        /// <summary>The text was left out: a password or PIN field, or a text-entry field while those are redacted.</summary>
        Redacted = 16,
        /// <summary>On a Screen row: every element is forgotten, the rows that follow are the complete tree.</summary>
        Snapshot = 32,
        /// <summary>Comes from UI Toolkit; otherwise from uGUI.</summary>
        UIToolkit = 64
    }

    /// <summary>Where text sits in its rectangle; the values are those of <see cref="TextAnchor"/>.</summary>
    public enum UIAlign : byte
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        LowerLeft,
        LowerCenter,
        LowerRight
    }

    public enum UIFill : byte
    {
        None,
        Horizontal,
        Vertical,
        Radial
    }

    public struct UIColor
    {
        public byte r, g, b, a;
    }

    /// <summary>
    /// A row of the "ui" stream: one element of what the local player's UI showed (see recording/FORMAT.md,
    /// "UI stream"). Rows are written when an element changes and carry its complete state.
    /// </summary>
    public struct UISample
    {
        public UIOp op;
        public UIKind kind;
        public UIWidget widget;
        public UIFlags flags;
        /// <summary>The element's id, kept for as long as its object lives and never reused.</summary>
        public int id;
        /// <summary>The nearest recorded element above this one; 0 for a root (a canvas or panel).</summary>
        public int parent;
        public int root;
        /// <summary>Paint order within the root (higher is drawn later, on top); for a root, its order among roots.</summary>
        public int order;
        /// <summary>
        /// The rectangle before rotation, as fractions of the screen with the origin at the top left (of the root's
        /// rectangle when the root is in world space). Rotated by <see cref="rotation"/> around its centre.
        /// </summary>
        public float x, y, width, height;
        /// <summary>Degrees clockwise on screen, in pixels (not in the stretched 0..1 space).</summary>
        public float rotation;
        /// <summary>What fills the rectangle or tints the sprite; alpha 0 when nothing is drawn there.</summary>
        public UIColor color;
        public UIColor textColor;
        /// <summary>Text height as a fraction of the screen's (or world root's) height.</summary>
        public float fontSize;
        public UIAlign align;
        public UIFill fill;
        public byte fillOrigin;
        /// <summary>How much of a filled image is drawn, 0..1 (1 when it is not a filled image).</summary>
        public float fillAmount;
        /// <summary>A control's value: a slider, scrollbar or progress bar's position 0..1, a toggle's 0 or 1.</summary>
        public float value;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public Vector2 worldSize;
        public string name;
        /// <summary>The class of the component or element ("Image", "TextMeshProUGUI", "Label"...).</summary>
        public string type;
        /// <summary>Name of the sprite or texture drawn, if any.</summary>
        public string sprite;
        public string text;
    }
}
