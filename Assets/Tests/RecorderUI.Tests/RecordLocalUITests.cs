using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PurrNet;
using PurrNet.Recording;
using PurrNet.Transports;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;
using Slider = UnityEngine.UI.Slider;
using Toggle = UnityEngine.UI.Toggle;

/// <summary>
/// Records a host's UI (uGUI, TextMeshPro, UI Toolkit) while it changes and writes what the scene set, tick by tick,
/// next to the recording. "python recording/purrrec.py ui-check file.expect.json" compares the two.
/// </summary>
public partial class RecordLocalUITests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    readonly List<Object> _created = new List<Object>();
    NetworkManager _manager;
    NetworkRecorder _recorder;
    Font _font;

    static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../test-local/ui/rec"));

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        RecordLocalUI.measureAllocations = false;
        if (_recorder && _recorder.isRecording)
            _recorder.StopRecording();
        if (_manager)
        {
            _manager.StopClient();
            _manager.StopServer();
            for (int i = 0; i < 10; i++)
                yield return null;
        }
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Fields)!.SetValue(target, value);

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    IEnumerator Ticks(int count)
    {
        var tickModule = _manager.tickModule;
        uint until = tickModule.localTick + (uint)count;
        yield return WaitUntil(() => tickModule.localTick >= until);
    }

    IEnumerator StartHost(bool recordUI, float chunkSeconds, bool redactTextInput, int tickRate = 20)
    {
        var go = new GameObject("RecorderNetworkManager");
        go.SetActive(false);
        _created.Add(go);
        var transport = go.AddComponent<LocalTransport>();
        _manager = go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;
        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        _manager.SetNetworkRules(rules);
        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        _manager.SetPrefabProvider(provider);
        _manager.transport = transport;
        Set(_manager, "_tickRate", tickRate);

        _recorder = go.AddComponent<NetworkRecorder>();
        Set(_recorder, "_recordOnServer", false);
        Set(_recorder, "_recordCamera", false);
        Set(_recorder, "_recordUI", recordUI);
        Set(_recorder, "_redactTextInput", redactTextInput);
        Set(_recorder, "_upload", false);
        Set(_recorder, "_chunkSeconds", chunkSeconds);
        Set(_recorder, "_directory", OutputDirectory);
        Set(_recorder, "_networkManager", _manager);
        go.SetActive(true);

        _manager.StartHost();
        yield return WaitUntil(() => _manager.isServer && _manager.isClient && _manager.players.Count == 1);
        yield return null;
    }

    // ---- Building UI ----------------------------------------------------------------------------------

    GameObject NewCanvas(string name, RenderMode mode, int order = 0)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(GraphicRaycaster));
        _created.Add(go);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = mode;
        canvas.sortingOrder = order;
        return go;
    }

    /// <summary>A rectangle of <paramref name="size"/> pixels whose top left is <paramref name="at"/> pixels from the parent's.</summary>
    static RectTransform Place(GameObject go, Transform parent, Vector2 at, Vector2 size)
    {
        var rect = go.GetComponent<RectTransform>();
        if (!rect)
            rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = size;
        return rect;
    }

    static Image NewImage(string name, Transform parent, Vector2 at, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        Place(go, parent, at, size);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    Text NewText(string name, Transform parent, Vector2 at, Vector2 size, string value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        Place(go, parent, at, size);
        var text = go.GetComponent<Text>();
        text.font = _font;
        text.fontSize = 24;
        text.color = Color.black;
        text.text = value;
        return text;
    }

    static TextMeshProUGUI NewTmp(string name, Transform parent, Vector2 at, Vector2 size, string value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        Place(go, parent, at, size);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = 24;
        text.text = value;
        return text;
    }

    Sprite NewSprite(string name)
    {
        var texture = new Texture2D(4, 4) { name = name + "-texture" };
        _created.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        sprite.name = name;
        _created.Add(sprite);
        return sprite;
    }

    // ---- What the scene set ---------------------------------------------------------------------------

    sealed class Expectations
    {
        readonly StringBuilder _json = new StringBuilder("{\"phases\":[");
        bool _anyPhase, _anyElement;

        public void Phase(ulong tick, string label)
        {
            if (_anyPhase)
                _json.Append("]},");
            _anyPhase = true;
            _anyElement = false;
            _json.Append("\n{\"tick\":").Append(tick).Append(",\"label\":\"").Append(label).Append("\",\"elements\":[");
        }

        /// <summary>An element and what it should show: name, value, name, value...</summary>
        public void Element(string name, params object[] values)
        {
            if (_anyElement)
                _json.Append(',');
            _anyElement = true;
            _json.Append("\n {\"name\":\"").Append(name).Append('"');
            for (int i = 0; i < values.Length; i += 2)
            {
                _json.Append(",\"").Append(values[i]).Append("\":");
                switch (values[i + 1])
                {
                    case null: _json.Append("null"); break;
                    case string s: _json.Append('"').Append(s).Append('"'); break;
                    case bool b: _json.Append(b ? "true" : "false"); break;
                    case float f: _json.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                    case int n: _json.Append(n); break;
                    case float[] list:
                        _json.Append('[');
                        for (int k = 0; k < list.Length; k++)
                            _json.Append(k > 0 ? "," : "").Append(list[k].ToString("R", CultureInfo.InvariantCulture));
                        _json.Append(']');
                        break;
                    default: throw new System.ArgumentException(values[i + 1].GetType().Name);
                }
            }
            _json.Append('}');
        }

        public void Absent(string name) => Element(name, "absent", true);

        /// <summary>Whether any element shows <paramref name="text"/>.</summary>
        public void Shows(string text, bool shown) => Element("", "shows", text, "shown", shown);

        public string Finish(string recording, int width, int height, params string[] forbidden)
        {
            _json.Append(_anyPhase ? "]}" : "").Append("],\n\"recording\":\"").Append(recording.Replace("\\", "/"))
                .Append("\",\"screen\":[").Append(width).Append(',').Append(height).Append("],\"forbidden\":[");
            for (int i = 0; i < forbidden.Length; i++)
                _json.Append(i > 0 ? "," : "").Append('"').Append(forbidden[i]).Append('"');
            return _json.Append("]}").ToString();
        }
    }

    static float[] Box(float x, float y, float width, float height) =>
        new[] { x / Screen.width, y / Screen.height, width / Screen.width, height / Screen.height };

    static float[] Rgba(float r, float g, float b, float a) => new[] { r, g, b, a };

    [UnityTest]
    public IEnumerator RecordsCanvasTextMeshProAndToolkitUI()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        yield return StartHost(true, 1f, false);
        var ui = _recorder.GetComponent<RecordLocalUI>();
        Assert.That(ui, Is.Not.Null, "the recorder adds the UI recorder");
        float sw = Screen.width, sh = Screen.height;

        // uGUI, screen space overlay.
        var hud = NewCanvas("HUD", RenderMode.ScreenSpaceOverlay, 5).transform;
        var health = NewImage("HealthBar", hud, new Vector2(20, 20), new Vector2(200, 20), Color.red);
        health.sprite = NewSprite("bar-sprite");
        health.type = Image.Type.Filled;
        health.fillMethod = Image.FillMethod.Horizontal;
        health.fillAmount = 1f;
        var score = NewText("Score", hud, new Vector2(20, 50), new Vector2(200, 30), "Score: 0");
        var ammo = NewTmp("Ammo", hud, new Vector2(20, 90), new Vector2(200, 30), "30 / 90");
        var minimapObject = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage));
        Place(minimapObject, hud, new Vector2(500, 20), new Vector2(120, 120));
        var minimapTexture = new Texture2D(8, 8) { name = "minimap-tex" };
        _created.Add(minimapTexture);
        minimapObject.GetComponent<RawImage>().texture = minimapTexture;
        var blip = NewImage("Blip", minimapObject.transform, new Vector2(10, 10), new Vector2(8, 8), Color.green);
        var fire = NewImage("Fire", hud, new Vector2(20, 130), new Vector2(100, 40), Color.white);
        fire.gameObject.AddComponent<Button>();
        NewText("FireLabel", fire.transform, Vector2.zero, new Vector2(100, 40), "FIRE");
        var sliderObject = new GameObject("Volume", typeof(RectTransform), typeof(Slider));
        Place(sliderObject, hud, new Vector2(20, 180), new Vector2(160, 20));
        var slider = sliderObject.GetComponent<Slider>();
        slider.value = 0.25f;
        var toggleObject = new GameObject("Mute", typeof(RectTransform), typeof(Toggle));
        Place(toggleObject, hud, new Vector2(20, 210), new Vector2(20, 20));
        toggleObject.GetComponent<Toggle>().isOn = true;
        var needle = NewImage("Needle", hud, new Vector2(300, 300), new Vector2(60, 10), Color.yellow);
        needle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        needle.rectTransform.anchoredPosition = new Vector2(330, -305);
        var popup = new GameObject("Popup", typeof(RectTransform), typeof(CanvasGroup));
        Place(popup, hud, new Vector2(200, 200), new Vector2(100, 50));
        NewImage("PopupImage", popup.transform, Vector2.zero, new Vector2(100, 50), Color.blue);

        // Text entry: a password field, and a chat field.
        var passwordObject = new GameObject("Password", typeof(RectTransform), typeof(Image), typeof(InputField));
        passwordObject.SetActive(false);
        Place(passwordObject, hud, new Vector2(20, 240), new Vector2(160, 30));
        var passwordText = NewText("PasswordText", passwordObject.transform, Vector2.zero, new Vector2(160, 30), "");
        var password = passwordObject.GetComponent<InputField>();
        password.textComponent = passwordText;
        password.contentType = InputField.ContentType.Password;
        passwordObject.SetActive(true);
        password.text = "hunter2";
        var chatObject = new GameObject("Chat", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        chatObject.SetActive(false);
        Place(chatObject, hud, new Vector2(20, 280), new Vector2(160, 30));
        var chatText = NewTmp("ChatText", chatObject.transform, Vector2.zero, new Vector2(160, 30), "");
        var chat = chatObject.GetComponent<TMP_InputField>();
        chat.textViewport = (RectTransform)chatObject.transform;
        chat.textComponent = chatText;
        chatObject.SetActive(true);
        chat.text = "hello chat";

        // Kept out by the marker.
        var secret = new GameObject("Secret", typeof(RectTransform), typeof(DontRecordUI));
        Place(secret, hud, new Vector2(300, 20), new Vector2(100, 30));
        NewText("SecretText", secret.transform, Vector2.zero, new Vector2(100, 30), "TOPSECRET");

        // World space and screen space camera.
        var nameplate = NewCanvas("Nameplate", RenderMode.WorldSpace);
        var nameplateRect = (RectTransform)nameplate.transform;
        nameplateRect.sizeDelta = new Vector2(200, 50);
        nameplateRect.localScale = Vector3.one * 0.01f;
        nameplateRect.position = new Vector3(1, 2, 3);
        var nameText = NewTmp("NameText", nameplate.transform, Vector2.zero, new Vector2(200, 50), "Bob");
        var cameraObject = new GameObject("UICamera", typeof(Camera));
        _created.Add(cameraObject);
        var cameraCanvas = NewCanvas("CameraCanvas", RenderMode.ScreenSpaceCamera);
        cameraCanvas.GetComponent<Canvas>().worldCamera = cameraObject.GetComponent<Camera>();
        NewImage("Crosshair", cameraCanvas.transform, new Vector2(sw / 2 - 16, sh / 2 - 16), new Vector2(32, 32), Color.white);

        // UI Toolkit.
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        _created.Add(settings);
        settings.sortingOrder = 3;
        var documentObject = new GameObject("Document");
        documentObject.SetActive(false);
        _created.Add(documentObject);
        var document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        documentObject.SetActive(true);
        var root = document.rootVisualElement;
        root.name = "doc-root";
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.width = sw;
        root.style.height = sh;
        var status = new Label("Ready") { name = "status" };
        status.style.position = Position.Absolute;
        status.style.left = 10;
        status.style.top = 300;
        status.style.width = 120;
        status.style.height = 30;
        status.style.color = new Color(0, 1, 0, 1);
        root.Add(status);
        var box = new VisualElement { name = "box" };
        box.style.position = Position.Absolute;
        box.style.left = 400;
        box.style.top = 300;
        box.style.width = 80;
        box.style.height = 40;
        box.style.backgroundColor = new Color(1, 0, 0, 1);
        root.Add(box);
        var bar = new ProgressBar { name = "hp-bar", lowValue = 0, highValue = 100, value = 50 };
        root.Add(bar);
        var pin = new TextField { name = "pin", isPasswordField = true, value = "1234" };
        root.Add(pin);
        var typed = new TextField { name = "typed", value = "uitk typed" };
        root.Add(typed);
        var hidden = new VisualElement { name = "hidden" };
        hidden.AddToClassList(DontRecordUI.UssClass);
        hidden.Add(new Label("hidden-uitk") { name = "hidden-label" });
        root.Add(hidden);

        for (int i = 0; i < 3; i++)
            yield return null;
        _recorder.StartRecording("ui-test.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        string path = _recorder.currentPath;
        var expect = new Expectations();
        var tickModule = _manager.tickModule;

        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "initial");
        expect.Element("HUD", "kind", "Canvas", "parent", 0, "rect", new float[] { 0, 0, 1, 1 }, "order", 5, "visible", true, "toolkit", false);
        expect.Element("HealthBar", "kind", "Image", "type", "Image", "sprite", "bar-sprite", "fill", "Horizontal", "fillAmount", 1f,
            "rect", Box(20, 20, 200, 20), "color", Rgba(255, 0, 0, 255), "parentName", "HUD", "rootName", "HUD", "visible", true);
        expect.Element("Score", "kind", "Text", "type", "Text", "text", "Score: 0", "rect", Box(20, 50, 200, 30), "fontSize", 24f / sh,
            "textColor", Rgba(0, 0, 0, 255));
        expect.Element("Ammo", "kind", "Text", "type", "TextMeshProUGUI", "text", "30 / 90", "rect", Box(20, 90, 200, 30), "fontSize", 24f / sh);
        expect.Element("Minimap", "kind", "RawImage", "sprite", "minimap-tex", "rect", Box(500, 20, 120, 120));
        expect.Element("Blip", "kind", "Image", "rect", Box(510, 30, 8, 8), "parentName", "Minimap", "color", Rgba(0, 255, 0, 255));
        expect.Element("Fire", "kind", "Image", "widget", "Button", "interactable", true, "pickable", true);
        expect.Element("FireLabel", "kind", "Text", "text", "FIRE", "parentName", "Fire");
        expect.Element("Volume", "kind", "Element", "widget", "Slider", "value", 0.25f, "type", "Slider", "rect", Box(20, 180, 160, 20));
        expect.Element("Mute", "widget", "Toggle", "value", 1f);
        expect.Element("Needle", "rect", Box(300, 300, 60, 10), "rotation", 0f);
        expect.Element("PopupImage", "visible", true, "color", Rgba(0, 0, 255, 255));
        expect.Element("Password", "kind", "Image", "widget", "InputField");
        expect.Element("PasswordText", "kind", "Text", "text", null, "redacted", true);
        expect.Element("ChatText", "kind", "Text", "text", "hello chat​", "redacted", false, "parentName", "Chat");
        expect.Absent("Secret");
        expect.Absent("SecretText");
        expect.Element("Nameplate", "kind", "Canvas", "world", true, "worldPosition", new float[] { 1, 2, 3 }, "worldSize", new float[] { 2, 0.5f },
            "worldRotation", new float[] { 0, 0, 0, 1 }, "rect", new float[] { 0, 0, 1, 1 });
        expect.Element("NameText", "text", "Bob", "rect", new float[] { 0, 0, 1, 1 }, "rootName", "Nameplate", "fontSize", 24f / 50f);
        expect.Element("CameraCanvas", "kind", "Canvas", "world", false, "rect", new float[] { 0, 0, 1, 1 });
        expect.Element("Crosshair", "rect", Box(sw / 2 - 16, sh / 2 - 16, 32, 32), "rootName", "CameraCanvas");
        expect.Element("status", "kind", "Text", "type", "Label", "text", "Ready", "rect", Box(10, 300, 120, 30), "toolkit", true,
            "textColor", Rgba(0, 255, 0, 255), "parentName", "doc-root");
        expect.Element("box", "kind", "Element", "rect", Box(400, 300, 80, 40), "color", Rgba(255, 0, 0, 255), "toolkit", true);
        expect.Element("hp-bar", "widget", "ProgressBar", "value", 0.5f, "type", "ProgressBar");
        expect.Element("pin", "widget", "InputField");
        expect.Element("typed", "widget", "InputField");
        expect.Absent("hidden");
        expect.Absent("hidden-label");
        expect.Shows("uitk typed", true);

        // Changes the UI systems announce.
        health.fillAmount = 0.4f;
        score.text = "Score: 150";
        ammo.text = "29 / 90";
        status.text = "Reloading";
        bar.value = 80;
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "announced");
        expect.Element("HealthBar", "fillAmount", 0.4f, "rect", Box(20, 20, 200, 20));
        expect.Element("Score", "text", "Score: 150");
        expect.Element("Ammo", "text", "29 / 90");
        expect.Element("status", "text", "Reloading");
        expect.Element("hp-bar", "value", 0.8f);

        // Changes nothing announces.
        blip.rectTransform.anchoredPosition = new Vector2(50, -60);
        popup.GetComponent<CanvasGroup>().alpha = 0f;
        needle.rectTransform.localRotation = Quaternion.Euler(0, 0, 90);
        slider.value = 0.75f;
        nameplateRect.position = new Vector3(4, 5, 6);
        status.style.left = 50;
        box.style.backgroundColor = new Color(0, 1, 0, 0.5f);
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "unannounced");
        expect.Element("Blip", "rect", Box(550, 80, 8, 8));
        expect.Element("PopupImage", "visible", false);
        expect.Element("Needle", "rect", Box(300, 300, 60, 10), "rotation", -90f);
        expect.Element("Volume", "value", 0.75f);
        expect.Element("Nameplate", "worldPosition", new float[] { 4, 5, 6 });
        expect.Element("NameText", "rect", new float[] { 0, 0, 1, 1 }, "text", "Bob");
        expect.Element("status", "rect", Box(50, 300, 120, 30), "text", "Reloading");
        expect.Element("box", "color", Rgba(0, 255, 0, 128));

        // Elements come and go; typed text is redacted from here on.
        minimapObject.SetActive(false);
        Object.Destroy(needle.gameObject);
        NewImage("Hit", hud, new Vector2(310, 230), new Vector2(20, 20), Color.white);
        root.Remove(bar);
        var toast = new Label("Saved") { name = "toast" };
        root.Add(toast);
        box.style.display = DisplayStyle.None;
        ui.redactTextInput = true;
        nameText.text = "Alice";
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "structure");
        expect.Absent("Minimap");
        expect.Absent("Blip");
        expect.Absent("Needle");
        expect.Element("Hit", "kind", "Image", "rect", Box(310, 230, 20, 20), "parentName", "HUD");
        expect.Absent("hp-bar");
        expect.Absent("box");
        expect.Element("toast", "kind", "Text", "text", "Saved");
        expect.Element("ChatText", "text", null, "redacted", true);
        expect.Element("PasswordText", "text", null, "redacted", true);
        expect.Element("NameText", "text", "Alice");
        expect.Shows("uitk typed", false);

        // Back again, with the ids they had; then long enough for a few more chunks.
        minimapObject.SetActive(true);
        box.style.display = DisplayStyle.Flex;
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "back");
        expect.Element("Minimap", "kind", "RawImage", "sprite", "minimap-tex", "rect", Box(500, 20, 120, 120));
        expect.Element("Blip", "rect", Box(550, 80, 8, 8), "parentName", "Minimap");
        expect.Element("box", "rect", Box(400, 300, 80, 40));
        yield return new WaitForSecondsRealtime(2.5f);
        score.text = "Score: 151";
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "later");
        expect.Element("Score", "text", "Score: 151");
        expect.Element("HealthBar", "fillAmount", 0.4f, "sprite", "bar-sprite");
        expect.Element("status", "text", "Reloading");
        expect.Element("Hit", "kind", "Image");
        expect.Absent("Needle");
        expect.Absent("SecretText");

        // Markers added while recording: a uGUI subtree, and a whole document.
        fire.gameObject.AddComponent<DontRecordUI>();
        documentObject.AddComponent<DontRecordUI>();
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "markers");
        expect.Absent("Fire");
        expect.Absent("FireLabel");
        expect.Absent("doc-root");
        expect.Absent("status");
        expect.Absent("toast");
        expect.Element("Score", "text", "Score: 151");
        expect.Element("Hit", "kind", "Image");

        // And taken away again.
        Object.Destroy(fire.GetComponent<DontRecordUI>());
        yield return Ticks(4);
        expect.Phase(tickModule.localTick - 1, "unmarked");
        expect.Element("Fire", "kind", "Image", "widget", "Button");
        expect.Element("FireLabel", "text", "FIRE", "parentName", "Fire");
        expect.Absent("status");

        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        File.WriteAllText(Path.ChangeExtension(path, ".expect.json"),
            expect.Finish(path, Screen.width, Screen.height, "hunter2", "1234", "TOPSECRET", "hidden-uitk", "*******"));
        Debug.Log(ui.Describe());
        Assert.That(ui.enabled, Is.True, "the UI recorder did not stop itself");
        Assert.That(ui.rows, Is.GreaterThan(40));
    }
}
