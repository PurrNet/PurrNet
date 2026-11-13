using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PurrNet.Recording;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

/// <summary>
/// What recording a busy HUD costs: a shooter-style screen of about 200 elements (bars, counters, cooldowns, a
/// minimap, chat, a scoreboard that opens now and then, nameplates in the world, damage numbers, a UI Toolkit quest
/// tracker), animated for half a minute with and without the UI recorder. Explicit, since it only measures:
/// run it with -testFilter "RecordLocalUITests.HudCost".
/// </summary>
public partial class RecordLocalUITests
{
    sealed class Hud
    {
        public Image health;
        public TextMeshProUGUI ammo, timer;
        public Text score;
        public readonly List<Image> cooldowns = new List<Image>();
        public readonly List<TextMeshProUGUI> cooldownLabels = new List<TextMeshProUGUI>();
        public readonly List<RectTransform> blips = new List<RectTransform>();
        public readonly List<TextMeshProUGUI> chat = new List<TextMeshProUGUI>();
        public GameObject scoreboard;
        public readonly List<RectTransform> nameplates = new List<RectTransform>();
        public readonly List<TextMeshProUGUI> damage = new List<TextMeshProUGUI>();
        public Transform canvas;
        public Label quest;
        public ProgressBar questBar;
        public int elements;
    }

    Hud BuildHud()
    {
        var hud = new Hud();
        var canvas = NewCanvas("HUD", RenderMode.ScreenSpaceOverlay).transform;
        hud.canvas = canvas;

        // Frames, icons and captions that never change.
        for (int i = 0; i < 30; i++)
            NewImage("Frame" + i, canvas, new Vector2(10 + i % 10 * 60, 400 + i / 10 * 22), new Vector2(56, 20), Color.gray);
        for (int i = 0; i < 20; i++)
            NewTmp("Caption" + i, canvas, new Vector2(10 + i % 10 * 60, 400 + i / 10 * 22), new Vector2(56, 20), "Slot " + i);
        for (int i = 0; i < 4; i++)
            NewImage("Crosshair" + i, canvas, new Vector2(316 + i * 2, 238), new Vector2(2, 4), Color.white);

        hud.health = NewImage("HealthBar", canvas, new Vector2(20, 20), new Vector2(200, 16), Color.red);
        hud.health.sprite = NewSprite("bar");
        hud.health.type = Image.Type.Filled;
        hud.health.fillMethod = Image.FillMethod.Horizontal;
        hud.ammo = NewTmp("Ammo", canvas, new Vector2(20, 40), new Vector2(120, 24), "30 / 90");
        hud.timer = NewTmp("Timer", canvas, new Vector2(280, 10), new Vector2(80, 24), "10:00");
        hud.score = NewText("Score", canvas, new Vector2(520, 10), new Vector2(100, 24), "0");

        for (int i = 0; i < 8; i++)
        {
            var icon = NewImage("Ability" + i, canvas, new Vector2(200 + i * 34, 350), new Vector2(32, 32), Color.white);
            var cooldown = NewImage("Cooldown" + i, icon.transform, Vector2.zero, new Vector2(32, 32), new Color(0, 0, 0, 0.6f));
            cooldown.sprite = hud.health.sprite;
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            hud.cooldowns.Add(cooldown);
            hud.cooldownLabels.Add(NewTmp("CooldownText" + i, icon.transform, Vector2.zero, new Vector2(32, 32), ""));
        }

        var minimap = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage));
        Place(minimap, canvas, new Vector2(500, 40), new Vector2(128, 128));
        for (int i = 0; i < 16; i++)
            hud.blips.Add(NewImage("Blip" + i, minimap.transform, new Vector2(60, 60), new Vector2(6, 6), Color.green).rectTransform);

        for (int i = 0; i < 8; i++)
            hud.chat.Add(NewTmp("ChatLine" + i, canvas, new Vector2(10, 250 + i * 16), new Vector2(240, 16), ""));

        hud.scoreboard = new GameObject("Scoreboard", typeof(RectTransform), typeof(Image));
        Place(hud.scoreboard, canvas, new Vector2(120, 80), new Vector2(400, 240));
        for (int row = 0; row < 10; row++)
            for (int column = 0; column < 5; column++)
                NewTmp($"Cell{row}-{column}", hud.scoreboard.transform, new Vector2(10 + column * 78, 10 + row * 22), new Vector2(74, 20),
                    column == 0 ? "Player" + row : (row * column).ToString());
        hud.scoreboard.SetActive(false);

        for (int i = 0; i < 6; i++)
        {
            var plate = NewCanvas("Nameplate" + i, RenderMode.WorldSpace);
            var rect = (RectTransform)plate.transform;
            rect.sizeDelta = new Vector2(200, 40);
            rect.localScale = Vector3.one * 0.01f;
            NewImage("PlateBar", rect, new Vector2(0, 24), new Vector2(200, 16), Color.red);
            NewTmp("PlateName", rect, Vector2.zero, new Vector2(200, 24), "Enemy " + i);
            hud.nameplates.Add(rect);
        }

        // UI Toolkit quest tracker.
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        _created.Add(settings);
        var documentObject = new GameObject("QuestDocument");
        documentObject.SetActive(false);
        _created.Add(documentObject);
        var document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        documentObject.SetActive(true);
        var root = document.rootVisualElement;
        for (int i = 0; i < 5; i++)
        {
            var row = new VisualElement { name = "quest-row" + i };
            row.style.flexDirection = FlexDirection.Row;
            for (int k = 0; k < 3; k++)
                row.Add(new Label("Objective " + i + "." + k));
            root.Add(row);
        }
        hud.quest = new Label("Find the key") { name = "quest" };
        root.Add(hud.quest);
        hud.questBar = new ProgressBar { name = "quest-bar", lowValue = 0, highValue = 100 };
        root.Add(hud.questBar);
        return hud;
    }

    /// <summary>One frame of a match: what a HUD does while people play.</summary>
    void AnimateHud(Hud hud, float time, bool calm, ref int second, ref int tenth)
    {
        // Things that move every frame: radial cooldowns, minimap blips, nameplates following their owners.
        for (int i = 0; i < hud.cooldowns.Count && !calm; i++)
        {
            float remaining = Mathf.Repeat(4f + i - time, 4f + i);
            bool active = i < 4;
            hud.cooldowns[i].fillAmount = active ? remaining / (4f + i) : 0f;
        }
        for (int i = 0; i < hud.blips.Count && !calm; i++)
            hud.blips[i].anchoredPosition = new Vector2(60 + Mathf.Sin(time * 0.7f + i) * 50, -60 + Mathf.Cos(time * 0.5f + i * 2) * 50);
        for (int i = 0; i < hud.nameplates.Count && !calm; i++)
            hud.nameplates[i].position = new Vector3(Mathf.Sin(time * 0.3f + i) * 8, 2, 10 + Mathf.Cos(time * 0.2f + i) * 5);
        for (int i = hud.damage.Count - 1; i >= 0; i--)
        {
            var number = hud.damage[i];
            number.rectTransform.anchoredPosition += new Vector2(0, 40 * Time.deltaTime);
            number.alpha -= Time.deltaTime;
            if (number.alpha <= 0f)
            {
                Object.Destroy(number.gameObject);
                hud.damage.RemoveAt(i);
            }
        }

        int nowTenth = (int)(time * 10);
        if (nowTenth != tenth)
        {
            tenth = nowTenth;
            for (int i = 0; i < 4; i++)
                hud.cooldownLabels[i].SetText("{0:1}", Mathf.Repeat(4f + i - time, 4f + i));
            if (nowTenth % 3 == 0)
                hud.ammo.SetText("{0} / 90", 30 - nowTenth / 3 % 30);
            if (nowTenth % 5 == 0)
            {
                hud.health.fillAmount = 0.5f + 0.5f * Mathf.Sin(time);
                hud.questBar.value = nowTenth / 5 % 100;
            }
        }

        int nowSecond = (int)time;
        if (nowSecond == second)
            return;
        second = nowSecond;
        hud.timer.SetText("{0:00}:{1:00}", (float)(9 - nowSecond / 60), (float)(59 - nowSecond % 60));
        hud.damage.Add(NewTmp("Damage", hud.canvas, new Vector2(300 + nowSecond % 5 * 10, 200), new Vector2(60, 20), (17 * nowSecond % 90).ToString()));
        if (nowSecond % 2 == 0)
        {
            hud.score.text = (nowSecond * 50).ToString();
            hud.quest.text = "Find the key (" + nowSecond / 2 % 4 + "/3)";
        }
        if (nowSecond % 3 == 0)
        {
            for (int i = 0; i < hud.chat.Count - 1; i++)
                hud.chat[i].text = hud.chat[i + 1].text;
            hud.chat[hud.chat.Count - 1].text = "Player" + nowSecond % 7 + ": message number " + nowSecond;
        }
        hud.scoreboard.SetActive(nowSecond % 10 >= 8);
    }

    [UnityTest, Explicit]
    public IEnumerator HudCost()
    {
        const float seconds = 30f;
        const int tickRate = 30;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        float budget = 0.1f;
        var arguments = System.Environment.GetCommandLineArgs();
        bool calm = System.Array.IndexOf(arguments, "-uiCalm") >= 0;
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-uiBudgetMs")
                budget = float.Parse(arguments[i + 1], System.Globalization.CultureInfo.InvariantCulture);

        var report = new System.Text.StringBuilder();
        foreach (bool recordUI in new[] { true, false })
        {
            yield return StartHost(recordUI, 10f, true, tickRate);
            Application.targetFrameRate = 60;
            var ui = _recorder.GetComponent<RecordLocalUI>();
            var hud = BuildHud();
            for (int i = 0; i < 3; i++)
                yield return null;
            if (ui)
            {
                ui.checkBudgetMs = budget;
                RecordLocalUI.measureAllocations = true;
            }
            string file = recordUI ? "hud-ui.purrrec" : "hud-none.purrrec";
            _recorder.StartRecording(file);
            string path = _recorder.currentPath;

            int second = -1, tenth = -1, frames = 0;
            float start = Time.realtimeSinceStartup;
            bool warm = false;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float time = Time.realtimeSinceStartup - start;
                AnimateHud(hud, time, calm, ref second, ref tenth);
                frames++;
                // The first two seconds warm up pools, caches and the JIT.
                if (!warm && time > 2f && ui)
                {
                    warm = true;
                    ui.ResetStats();
                }
                yield return null;
            }

            if (ui)
            {
                report.AppendLine($"budget {budget} ms, {tickRate} ticks/s, {frames / seconds:0} frames/s{(calm ? ", calm" : "")}");
                report.AppendLine(ui.Describe());
            }
            _recorder.StopRecording();
            yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
            yield return new WaitForSecondsRealtime(0.5f);
            report.AppendLine($"{file}: {new FileInfo(path).Length} bytes for {seconds} s");
            yield return TearDown();
        }
        File.WriteAllText(Path.Combine(OutputDirectory, "hud-cost.txt"), report.ToString());
        Debug.Log(report.ToString());
    }
}
