#if PURRNET_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
#define PURRREC_INPUT_SYSTEM
#elif ENABLE_LEGACY_INPUT_MANAGER
#define PURRREC_LEGACY_INPUT
#endif
using UnityEngine;
#if PURRREC_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
#if PURRREC_INPUT_SYSTEM && !PURRNET_UITK
using System.Collections.Generic;
#endif
#if PURRNET_UGUI
using UnityEngine.EventSystems;
#endif
#if PURRNET_UITK
using System.Collections.Generic;
using UnityEngine.UIElements;
#endif

namespace PurrNet.Recording
{
    /// <summary>
    /// What the player's devices did during one tick (the format spec, "The input stream"). Fixed size and
    /// unmanaged: a row costs nothing to write.
    /// </summary>
    public struct RawInputSample
    {
        public const ushort Typing = 1, HasKeyboard = 2, HasPointer = 4, HasGamepad = 8, Focused = 16,
            FromInputSystem = 32, FromLegacyInput = 64, CursorLocked = 128, CursorConfined = 256, CursorHidden = 512;

        public ushort flags;
        public byte mouseButtons;
        public Vector2 mouseDelta;
        public Vector2 pointer;
        public Vector2 scroll;
        public ulong keys0, keys1;
        public Vector2 leftStick, rightStick;
        public float leftTrigger, rightTrigger;
        public uint gamepadButtons;
        public uint joystickButtons;
    }

    /// <summary>
    /// One row of the "actions" stream: an Input System action by name, written when it is first seen (and again at
    /// the start of every chunk) and whenever its value or phase changes. Unlike raw keys it means the same thing
    /// for every player, whatever they bound it to.
    /// </summary>
    public struct InputActionSample
    {
        public const byte Declare = 1, Value = 2;
        public const byte Performed = 1, Disabled = 2;

        public byte op;
        public byte phase;
        public byte flags;
        public int id;
        public float x, y, z;
        public string name;
        public string type;
    }

    /// <summary>
    /// Writes what the player's devices did to the "input" stream, one row per tick: pointer movement, scroll, mouse
    /// buttons, held keys and the gamepad. Recorders add one next to themselves unless input recording is turned off.
    /// A key or button pressed and released between two rows is still in the next row. While a text field has
    /// keyboard focus no keys are recorded; the row says the player was typing instead.
    /// On a client the rows go into the client's own part of the recording, never over the game's connection.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class RecordLocalInput : MonoBehaviour
    {
        public const string Stream = "input";

        RawInputSample _row;
        bool _typing;
        ulong _tick = ulong.MaxValue;

#if PURRREC_INPUT_SYSTEM
        void OnEnable() => InputSystem.onActionChange += OnActionChange;
#endif

        void OnDisable()
        {
#if PURRREC_INPUT_SYSTEM
            InputSystem.onActionChange -= OnActionChange;
#endif
            _row = default;
            _typing = false;
        }

        void Update()
        {
            var session = RecordingSession.active;
            if (session == null || !session.wantsSamples)
            {
                _row = default;
                _typing = false;
                return;
            }

            Poll();

            ulong tick = session.localTick;
            if (tick == _tick)
                return;
            _tick = tick;
#if PURRREC_INPUT_SYSTEM
            PollActions();
#endif

            if (_typing)
            {
                _row.keys0 = _row.keys1 = 0;
                _row.flags |= RawInputSample.Typing;
            }
            if (Application.isFocused)
                _row.flags |= RawInputSample.Focused;
            var lockState = UnityEngine.Cursor.lockState;
            if (lockState == CursorLockMode.Locked)
                _row.flags |= RawInputSample.CursorLocked;
            else if (lockState == CursorLockMode.Confined)
                _row.flags |= RawInputSample.CursorConfined;
            if (!UnityEngine.Cursor.visible)
                _row.flags |= RawInputSample.CursorHidden;
            session.WriteSample(Stream, _row);
#if PURRREC_INPUT_SYSTEM
            WriteActions(session);
#endif
            _row = default;
            _typing = false;
        }

        void SetKey(int key)
        {
            if (key < 64)
                _row.keys0 |= 1ul << key;
            else if (key < 128)
                _row.keys1 |= 1ul << (key - 64);
        }

#if PURRREC_INPUT_SYSTEM
        static readonly GamepadButton[] GamepadButtons =
        {
            GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadRight,
            GamepadButton.North, GamepadButton.East, GamepadButton.South, GamepadButton.West,
            GamepadButton.LeftStick, GamepadButton.RightStick, GamepadButton.LeftShoulder, GamepadButton.RightShoulder,
            GamepadButton.Start, GamepadButton.Select, GamepadButton.LeftTrigger, GamepadButton.RightTrigger
        };

        void Poll()
        {
            _row.flags |= RawInputSample.FromInputSystem;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                _row.flags |= RawInputSample.HasKeyboard;
                bool active = keyboard.wasUpdatedThisFrame || keyboard.anyKey.isPressed;
                if (IsTyping(active))
                    _typing = true;
                else if (active)
                {
                    var keys = keyboard.allKeys;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var key = keys[i];
                        if (key != null && (key.isPressed || key.wasPressedThisFrame))
                            SetKey((int)key.keyCode);
                    }
                }
            }

            var mouse = Mouse.current;
            var pointer = (Pointer)mouse ?? Pointer.current;
            if (pointer != null)
            {
                _row.flags |= RawInputSample.HasPointer;
                _row.mouseDelta += pointer.delta.ReadValue();
                SetPointer(pointer.position.ReadValue());
                if (mouse != null)
                {
                    _row.scroll += mouse.scroll.ReadValue();
                    Button(mouse.leftButton, 0);
                    Button(mouse.rightButton, 1);
                    Button(mouse.middleButton, 2);
                    Button(mouse.backButton, 3);
                    Button(mouse.forwardButton, 4);
                }
                else
                    Button(pointer.press, 0);
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                _row.flags |= RawInputSample.HasGamepad;
                _row.leftStick = pad.leftStick.ReadValue();
                _row.rightStick = pad.rightStick.ReadValue();
                _row.leftTrigger = pad.leftTrigger.ReadValue();
                _row.rightTrigger = pad.rightTrigger.ReadValue();
                for (int i = 0; i < GamepadButtons.Length; i++)
                {
                    var button = pad[GamepadButtons[i]];
                    if (button.isPressed || button.wasPressedThisFrame)
                        _row.gamepadButtons |= 1u << i;
                }
            }

            AccumulateActions();
        }

        void Button(UnityEngine.InputSystem.Controls.ButtonControl button, int bit)
        {
            if (button.isPressed || button.wasPressedThisFrame)
                _row.mouseButtons |= (byte)(1 << bit);
        }

        public const string ActionsStream = "actions";

        sealed class ActionState
        {
            public int id;
            public InputAction action;
            public string name, type;
            public byte kind;
            public bool unreadable;
            public int epoch;
            public int frame;
            public Vector3 value, sum, written;
            public bool accumulates;
            public bool summed;
            public int summedFrame = -1;
            public byte phase, writtenPhase;
            public bool performed;
            public bool disabled;
        }

        readonly Dictionary<InputAction, ActionState> _actions = new Dictionary<InputAction, ActionState>();
        readonly List<ActionState> _actionList = new List<ActionState>();
        readonly List<InputAction> _enabledActions = new List<InputAction>();
        readonly List<ActionState> _summedActions = new List<ActionState>();

        ActionState StateOf(InputAction action)
        {
            if (_actions.TryGetValue(action, out var state))
                return state;
            var map = action.actionMap;
            state = new ActionState
            {
                id = _actionList.Count + 1,
                name = map != null && !string.IsNullOrEmpty(map.name) ? map.name + "/" + action.name : action.name,
                type = action.expectedControlType ?? "",
                kind = ValueKind(action.expectedControlType)
            };
            _actions[action] = state;
            _actionList.Add(state);
            return state;
        }

        void OnActionChange(object changed, InputActionChange change)
        {
            if (change == InputActionChange.ActionPerformed && changed is InputAction action &&
                RecordingSession.active != null && RecordingSession.active.wantsSamples)
                StateOf(action).performed = true;
        }

        bool ReadAction(InputAction action, ActionState state, out Vector3 value)
        {
            value = default;
            if (state.unreadable || state.kind == 0)
                return false;
            try
            {
                switch (state.kind)
                {
                    case 1: value.x = action.ReadValue<float>(); break;
                    case 2: value = action.ReadValue<Vector2>(); break;
                    default: value = action.ReadValue<Vector3>(); break;
                }
                return true;
            }
            catch (System.Exception)
            {
                state.unreadable = true;
                return false;
            }
        }

        /// <summary>
        /// Actions bound to a delta (mouse movement) are summed every frame, so a tick's row holds all the movement
        /// since the last one; every other action is read once per tick.
        /// </summary>
        void AccumulateActions()
        {
            if (_typing)
                return;
            int frame = Time.frameCount;
            for (int i = 0; i < _summedActions.Count; i++)
            {
                var state = _summedActions[i];
                if (ReadAction(state.action, state, out var value))
                    state.sum += value;
                state.summedFrame = frame;
            }
        }

        static byte ValueKind(string controlType)
        {
            switch (controlType)
            {
                case "Button":
                case "Axis":
                case "Analog":
                case "Key":
                    return 1;
                case "Vector2":
                case "Stick":
                case "Dpad":
                case "Delta":
                    return 2;
                case "Vector3":
                    return 3;
                default:
                    return 0;
            }
        }

        void PollActions()
        {
            _enabledActions.Clear();
            InputSystem.ListEnabledActions(_enabledActions);
            int frame = Time.frameCount;
            for (int i = 0; i < _enabledActions.Count; i++)
            {
                var action = _enabledActions[i];
                var state = StateOf(action);

                state.frame = frame;
                if (_typing)
                {
                    state.value = default;
                    state.phase = (byte)InputActionPhase.Waiting;
                    continue;
                }

                ReadAction(action, state, out var value);
                var control = action.activeControl;
                state.accumulates = control != null && control.name == "delta";
                if (state.accumulates && !state.summed)
                {
                    state.summed = true;
                    state.action = action;
                    _summedActions.Add(state);
                }
                state.value = value;
                if (state.summedFrame != frame)
                    state.sum += value;
                state.phase = (byte)action.phase;
                state.performed |= action.triggered;
            }
        }

        void WriteActions(RecordingSession session)
        {
            int epoch = session.sampleEpoch;
            if (epoch == 0)
                return;

            int frame = Time.frameCount;
            for (int i = 0; i < _actionList.Count; i++)
            {
                var state = _actionList[i];
                bool enabled = state.frame == frame;
                var value = !enabled ? default : state.accumulates ? state.sum : state.value;
                byte phase = enabled ? state.phase : (byte)InputActionPhase.Disabled;
                bool snapshot = state.epoch != epoch;
                state.sum = default;

                if (snapshot)
                {
                    if (!enabled)
                    {
                        state.performed = false;
                        continue;
                    }
                    state.epoch = epoch;
                    session.WriteSample(ActionsStream, new InputActionSample
                    {
                        op = InputActionSample.Declare, id = state.id, name = state.name, type = state.type
                    });
                }
                else if (!state.performed && phase == state.writtenPhase && value == state.written)
                    continue;

                byte flags = state.performed ? InputActionSample.Performed : (byte)0;
                if (!enabled)
                    flags |= InputActionSample.Disabled;
                session.WriteSample(ActionsStream, new InputActionSample
                {
                    op = InputActionSample.Value, id = state.id, phase = phase, flags = flags,
                    x = value.x, y = value.y, z = value.z
                });
                state.written = value;
                state.writtenPhase = phase;
                state.performed = false;
            }
        }
#elif PURRREC_LEGACY_INPUT
        static readonly (KeyCode code, byte key)[] Keys =
        {
            (KeyCode.Space, 1), (KeyCode.Return, 2), (KeyCode.Tab, 3), (KeyCode.BackQuote, 4), (KeyCode.Quote, 5),
            (KeyCode.Semicolon, 6), (KeyCode.Comma, 7), (KeyCode.Period, 8), (KeyCode.Slash, 9), (KeyCode.Backslash, 10),
            (KeyCode.LeftBracket, 11), (KeyCode.RightBracket, 12), (KeyCode.Minus, 13), (KeyCode.Equals, 14),
            (KeyCode.A, 15), (KeyCode.B, 16), (KeyCode.C, 17), (KeyCode.D, 18), (KeyCode.E, 19), (KeyCode.F, 20),
            (KeyCode.G, 21), (KeyCode.H, 22), (KeyCode.I, 23), (KeyCode.J, 24), (KeyCode.K, 25), (KeyCode.L, 26),
            (KeyCode.M, 27), (KeyCode.N, 28), (KeyCode.O, 29), (KeyCode.P, 30), (KeyCode.Q, 31), (KeyCode.R, 32),
            (KeyCode.S, 33), (KeyCode.T, 34), (KeyCode.U, 35), (KeyCode.V, 36), (KeyCode.W, 37), (KeyCode.X, 38),
            (KeyCode.Y, 39), (KeyCode.Z, 40),
            (KeyCode.Alpha1, 41), (KeyCode.Alpha2, 42), (KeyCode.Alpha3, 43), (KeyCode.Alpha4, 44), (KeyCode.Alpha5, 45),
            (KeyCode.Alpha6, 46), (KeyCode.Alpha7, 47), (KeyCode.Alpha8, 48), (KeyCode.Alpha9, 49), (KeyCode.Alpha0, 50),
            (KeyCode.LeftShift, 51), (KeyCode.RightShift, 52), (KeyCode.LeftAlt, 53), (KeyCode.RightAlt, 54),
            (KeyCode.AltGr, 54), (KeyCode.LeftControl, 55), (KeyCode.RightControl, 56),
            (KeyCode.LeftCommand, 57), (KeyCode.LeftWindows, 57), (KeyCode.RightCommand, 58), (KeyCode.RightWindows, 58),
            (KeyCode.Menu, 59), (KeyCode.Escape, 60), (KeyCode.LeftArrow, 61), (KeyCode.RightArrow, 62),
            (KeyCode.UpArrow, 63), (KeyCode.DownArrow, 64), (KeyCode.Backspace, 65), (KeyCode.PageDown, 66),
            (KeyCode.PageUp, 67), (KeyCode.Home, 68), (KeyCode.End, 69), (KeyCode.Insert, 70), (KeyCode.Delete, 71),
            (KeyCode.CapsLock, 72), (KeyCode.Numlock, 73), (KeyCode.Print, 74), (KeyCode.ScrollLock, 75),
            (KeyCode.Pause, 76), (KeyCode.KeypadEnter, 77), (KeyCode.KeypadDivide, 78), (KeyCode.KeypadMultiply, 79),
            (KeyCode.KeypadPlus, 80), (KeyCode.KeypadMinus, 81), (KeyCode.KeypadPeriod, 82), (KeyCode.KeypadEquals, 83),
            (KeyCode.Keypad0, 84), (KeyCode.Keypad1, 85), (KeyCode.Keypad2, 86), (KeyCode.Keypad3, 87),
            (KeyCode.Keypad4, 88), (KeyCode.Keypad5, 89), (KeyCode.Keypad6, 90), (KeyCode.Keypad7, 91),
            (KeyCode.Keypad8, 92), (KeyCode.Keypad9, 93),
            (KeyCode.F1, 94), (KeyCode.F2, 95), (KeyCode.F3, 96), (KeyCode.F4, 97), (KeyCode.F5, 98), (KeyCode.F6, 99),
            (KeyCode.F7, 100), (KeyCode.F8, 101), (KeyCode.F9, 102), (KeyCode.F10, 103), (KeyCode.F11, 104),
            (KeyCode.F12, 105), (KeyCode.F13, 112), (KeyCode.F14, 113), (KeyCode.F15, 114)
        };

        const int MouseButtons = 5, JoystickButtons = 20;
#if !UNITY_6000_0_OR_NEWER
        Vector3 _lastMouse;
        bool _hasLastMouse;
#endif

        void Poll()
        {
            _row.flags |= RawInputSample.FromLegacyInput | RawInputSample.HasKeyboard;

            bool active = Input.anyKey || Input.anyKeyDown;
            if (IsTyping(active))
                _typing = true;
            else if (active)
            {
                for (int i = 0; i < Keys.Length; i++)
                {
                    var code = Keys[i].code;
                    if (Input.GetKey(code) || Input.GetKeyDown(code))
                        SetKey(Keys[i].key);
                }
            }

            if (active)
            {
                for (int i = 0; i < JoystickButtons; i++)
                {
                    var code = KeyCode.JoystickButton0 + i;
                    if (Input.GetKey(code) || Input.GetKeyDown(code))
                        _row.joystickButtons |= 1u << i;
                }
            }

            if (!Input.mousePresent)
                return;
            _row.flags |= RawInputSample.HasPointer;
            var position = Input.mousePosition;
#if UNITY_6000_0_OR_NEWER
            _row.mouseDelta += (Vector2)Input.mousePositionDelta;
#else
            if (_hasLastMouse)
                _row.mouseDelta += (Vector2)(position - _lastMouse);
            _lastMouse = position;
            _hasLastMouse = true;
#endif
            _row.scroll += Input.mouseScrollDelta;
            SetPointer(position);
            if (active)
            {
                for (int i = 0; i < MouseButtons; i++)
                    if (Input.GetMouseButton(i) || Input.GetMouseButtonDown(i))
                        _row.mouseButtons |= (byte)(1 << i);
            }
        }
#else
        void Poll()
        {
            if (IsTyping(false))
                _typing = true;
        }
#endif

        void SetPointer(Vector2 position)
        {
            int width = Screen.width, height = Screen.height;
            _row.pointer = width > 0 && height > 0 ? new Vector2(position.x / width, position.y / height) : default;
        }

#if PURRNET_UITK
        readonly List<UIDocument> _documents = new List<UIDocument>();
        float _documentsAt = float.NegativeInfinity;
#endif

        bool IsTyping(bool keysActive)
        {
#if PURRNET_UGUI
            var events = EventSystem.current;
            if (events)
            {
                var selected = events.currentSelectedGameObject;
                if (selected)
                {
                    if (selected.TryGetComponent<UnityEngine.UI.InputField>(out var field) && field.isFocused)
                        return true;
#if PURRNET_TMP
                    if (selected.TryGetComponent<TMPro.TMP_InputField>(out var tmp) && tmp.isFocused)
                        return true;
#endif
                }
            }
#endif
#if PURRNET_IMGUI
            if (GUIUtility.keyboardControl != 0)
                return true;
#endif
            if (TouchScreenKeyboard.visible)
                return true;
#if PURRNET_UITK
            if (keysActive && Time.unscaledTime - _documentsAt > 1f)
            {
                _documentsAt = Time.unscaledTime;
                _documents.Clear();
                _documents.AddRange(Resources.FindObjectsOfTypeAll<UIDocument>());
            }
            for (int i = 0; i < _documents.Count; i++)
            {
                var document = _documents[i];
                if (!document || !document.isActiveAndEnabled)
                    continue;
                var focused = document.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
                for (; focused != null; focused = focused.hierarchy.parent)
                {
                    if (focused is TextInputBaseField<string> || focused is TextInputBaseField<int> ||
                        focused is TextInputBaseField<long> || focused is TextInputBaseField<float> ||
                        focused is TextInputBaseField<double>)
                        return true;
                }
            }
#endif
            return false;
        }
    }
}
