using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Right-side HUD panel showing a status card per drone (name + energy bar).
/// Built entirely in code — no prefabs required.
/// </summary>
public class DroneStatusUI : MonoBehaviour
{
    struct StepRow
    {
        public GameObject root;
        public Text label;
        public Image barBg;
        public Image barFill;
        public Text time;
    }

    struct DroneCard
    {
        public DroneController drone;
        public Text nameLabel;
        public Image energyFill;
        public Text energyText;
        public Image cardBg;
        public LayoutElement layoutElem;
        public Outline cardOutline;
        // Journey step rows
        public RectTransform stepsContainer;
        public List<StepRow> stepRows;
        public int lastStepCount;
    }

    readonly List<DroneCard> cards = new List<DroneCard>();
    List<DroneController> allDrones;

    // Palette (matches hex-map sci-fi theme)
    static readonly Color panelColor      = new Color(0.02f, 0.02f, 0.04f, 0.80f);
    static readonly Color cardColor       = new Color(0.05f, 0.05f, 0.08f, 0.90f);
    static readonly Color cardSelectedCol = new Color(0.04f, 0.12f, 0.18f, 0.95f);
    static readonly Color cardSelectedBorderCol = new Color(0f, 0.85f, 1f, 0.6f);
    static readonly Color nameSelectedCol = new Color(0.3f, 1f, 1f, 1f);
    static readonly Color accentColor     = new Color(0f, 0.85f, 1f, 1f);
    static readonly Color dimTextColor    = new Color(0.45f, 0.50f, 0.55f, 1f);
    static readonly Color barBgColor      = new Color(0.08f, 0.08f, 0.10f, 1f);
    static readonly Color energyFullCol   = new Color(0f, 0.85f, 1f, 1f);
    static readonly Color energyLowCol    = new Color(1f, 0.30f, 0.10f, 1f);

    // Journey step colors
    static readonly Color stepBarBgCol      = new Color(0.06f, 0.06f, 0.08f, 1f);
    static readonly Color stepTravelCol     = new Color(0f, 0.65f, 0.85f, 0.85f);
    static readonly Color stepScanCol       = new Color(0.10f, 0.75f, 0.45f, 0.85f);
    static readonly Color stepCompletedCol  = new Color(0.15f, 0.35f, 0.25f, 0.55f);
    static readonly Color stepFutureBarCol  = new Color(0.10f, 0.10f, 0.12f, 0.40f);
    static readonly Color stepTextActiveCol = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color stepTextDimCol    = new Color(0.45f, 0.48f, 0.50f, 0.65f);

    const float baseCardH = 48f;
    const float stepRowH  = 16f;
    const float stepGap   = 2f;

    Font uiFont;

    // ── public API ───────────────────────────

    public void Init(List<DroneController> drones)
    {
        allDrones = drones;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont("Arial", 14);

        BuildCanvas(drones);
    }

    // ── build ────────────────────────────────

    void BuildCanvas(List<DroneController> drones)
    {
        // Canvas
        var canvasGO = new GameObject("StatusCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // EventSystem is required for UI button clicks to work
        if (FindObjectOfType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        // Right-side panel
        var panel = MakeImage(canvasGO.transform, "DronePanel", panelColor);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1, 1);
        panelRT.anchorMax = new Vector2(1, 1);
        panelRT.pivot = new Vector2(1, 1);
        panelRT.anchoredPosition = new Vector2(-12, -12);
        panelRT.sizeDelta = new Vector2(190, 0); // width fixed, height auto

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 4;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panel.transform, false);
        var titleText = titleGO.AddComponent<Text>();
        titleText.text = "DRONES";
        titleText.font = uiFont;
        titleText.fontSize = 12;
        titleText.color = dimTextColor;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontStyle = FontStyle.Bold;
        var titleLE = titleGO.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 18;

        // Drone cards
        foreach (var drone in drones)
            cards.Add(CreateCard(panel.transform, drone));
    }

    DroneCard CreateCard(Transform parent, DroneController drone)
    {
        // Card root
        var cardGO = MakeImage(parent, "Card_" + drone.DroneName, cardColor);
        var cardImg = cardGO.GetComponent<Image>();
        var le = cardGO.AddComponent<LayoutElement>();
        le.preferredHeight = baseCardH;
        le.minHeight = baseCardH;

        // Click-to-select button (invisible, covers entire card)
        var btn = cardGO.AddComponent<Button>();
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        btn.transition = Selectable.Transition.None;
        var capturedDrone = drone;
        btn.onClick.AddListener(() => OnCardClicked(capturedDrone));

        // Selection border (hidden by default)
        var outline = cardGO.AddComponent<Outline>();
        outline.effectColor = cardSelectedBorderCol;
        outline.effectDistance = new Vector2(1.5f, 1.5f);
        outline.enabled = false;

        // ── Row 1: name — top-anchored, fixed 22px ──
        var nameGO = MakeText(cardGO.transform, "Name", drone.DroneName, 13, accentColor,
                              TextAnchor.MiddleLeft);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 1);
        nameRT.anchorMax = new Vector2(1, 1);
        nameRT.offsetMin = new Vector2(8, -24);
        nameRT.offsetMax = new Vector2(-8, -2);

        // ── Row 2: energy bar + percentage — below name ──
        var barBg = MakeImage(cardGO.transform, "BarBg", barBgColor);
        var barBgRT = barBg.GetComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0, 1);
        barBgRT.anchorMax = new Vector2(1, 1);
        barBgRT.offsetMin = new Vector2(8, -44);
        barBgRT.offsetMax = new Vector2(-42, -26);

        var fill = MakeImage(barBg.transform, "Fill", energyFullCol);
        var fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(1, 1);
        fillRT.offsetMax = new Vector2(-1, -1);
        var fillImg = fill.GetComponent<Image>();

        var pctGO = MakeText(cardGO.transform, "Pct", "100%", 11, accentColor,
                             TextAnchor.MiddleRight);
        var pctRT = pctGO.GetComponent<RectTransform>();
        pctRT.anchorMin = new Vector2(1, 1);
        pctRT.anchorMax = new Vector2(1, 1);
        pctRT.offsetMin = new Vector2(-40, -44);
        pctRT.offsetMax = new Vector2(-8, -26);

        // ── Steps container — below energy, populated dynamically ──
        var stepsGO = new GameObject("Steps");
        stepsGO.transform.SetParent(cardGO.transform, false);
        var stepsRT = stepsGO.AddComponent<RectTransform>();
        stepsRT.anchorMin = new Vector2(0, 1);
        stepsRT.anchorMax = new Vector2(1, 1);
        stepsRT.pivot = new Vector2(0.5f, 1f);
        stepsRT.anchoredPosition = new Vector2(0, -baseCardH);
        stepsRT.sizeDelta = new Vector2(0, 0);

        return new DroneCard
        {
            drone = drone,
            nameLabel = nameGO.GetComponent<Text>(),
            energyFill = fillImg,
            energyText = pctGO.GetComponent<Text>(),
            cardBg = cardImg,
            layoutElem = le,
            cardOutline = outline,
            stepsContainer = stepsRT,
            stepRows = new List<StepRow>(),
            lastStepCount = 0,
        };
    }

    // ── per-frame update ─────────────────────

    void Update()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            if (c.drone == null) continue;

            float e = Mathf.Clamp01(c.drone.Energy);

            // Energy fill
            var fillRT = c.energyFill.rectTransform;
            fillRT.anchorMax = new Vector2(e, 1);
            c.energyFill.color = Color.Lerp(energyLowCol, energyFullCol, e);
            c.energyText.text = Mathf.RoundToInt(e * 100f) + "%";

            // Selection highlight
            bool sel = c.drone.IsSelected;
            c.cardBg.color = sel ? cardSelectedCol : cardColor;
            c.nameLabel.color = sel ? nameSelectedCol : accentColor;
            c.cardOutline.enabled = sel;

            // ── Journey step rows ──
            var journey = c.drone.Journey;
            int stepCount = journey.Count;

            // Rebuild step rows if count changed
            if (stepCount != c.lastStepCount)
            {
                RebuildStepRows(ref c, stepCount);
                cards[i] = c;
            }

            // Update each step row
            int activeIdx = c.drone.JourneyCurrentIndex;
            for (int s = 0; s < c.stepRows.Count; s++)
            {
                var row = c.stepRows[s];
                var step = journey[s];
                float progress = c.drone.GetJourneyStepProgress(s);
                float elapsed = c.drone.GetJourneyStepElapsed(s);

                bool isActive = (s == activeIdx);
                bool isCompleted = (s < activeIdx);

                // Bar fill width
                row.barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);

                // Colors based on state
                if (isCompleted)
                {
                    row.barFill.color = stepCompletedCol;
                    row.barBg.color = stepBarBgCol;
                    row.label.color = stepTextDimCol;
                    row.time.color = stepTextDimCol;
                }
                else if (isActive)
                {
                    row.barFill.color = step.isScan ? stepScanCol : stepTravelCol;
                    row.barBg.color = stepBarBgCol;
                    row.label.color = stepTextActiveCol;
                    row.time.color = stepTextActiveCol;
                }
                else
                {
                    row.barFill.color = stepFutureBarCol;
                    row.barBg.color = stepBarBgCol;
                    row.label.color = stepTextDimCol;
                    row.time.color = stepTextDimCol;
                }

                string prefix = isActive ? "\u25B8 " : isCompleted ? "\u2713 " : "  ";
                row.label.text = prefix + step.label;
                row.time.text = $"{elapsed:F1}s / {step.duration:F1}s";
            }
        }
    }

    void RebuildStepRows(ref DroneCard c, int count)
    {
        // Destroy old rows
        foreach (var row in c.stepRows)
            if (row.root != null)
                Destroy(row.root);
        c.stepRows.Clear();

        // Create new rows
        for (int s = 0; s < count; s++)
        {
            float yTop = -(s * (stepRowH + stepGap));
            float yBot = yTop - stepRowH;

            // Row background (doubles as bar bg)
            var rowGO = MakeImage(c.stepsContainer, "Step_" + s, stepBarBgCol);
            var rowRT = rowGO.GetComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0, 1);
            rowRT.anchorMax = new Vector2(1, 1);
            rowRT.offsetMin = new Vector2(4, yBot);
            rowRT.offsetMax = new Vector2(-4, yTop);

            // Fill bar
            var fillGO = MakeImage(rowGO.transform, "Fill", stepTravelCol);
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(0, 1);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            // Label (overlaid, left)
            var labelGO = MakeText(rowGO.transform, "Label", "", 9, stepTextActiveCol,
                                   TextAnchor.MiddleLeft);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(4, 0);
            labelRT.offsetMax = new Vector2(-4, 0);

            // Time (overlaid, right)
            var timeGO = MakeText(rowGO.transform, "Time", "", 9, stepTextActiveCol,
                                  TextAnchor.MiddleRight);
            var timeRT = timeGO.GetComponent<RectTransform>();
            timeRT.anchorMin = Vector2.zero;
            timeRT.anchorMax = Vector2.one;
            timeRT.offsetMin = new Vector2(4, 0);
            timeRT.offsetMax = new Vector2(-4, 0);

            c.stepRows.Add(new StepRow
            {
                root = rowGO,
                label = labelGO.GetComponent<Text>(),
                barBg = rowGO.GetComponent<Image>(),
                barFill = fillGO.GetComponent<Image>(),
                time = timeGO.GetComponent<Text>(),
            });
        }

        // Resize container and card
        float stepsH = count > 0 ? count * stepRowH + (count - 1) * stepGap + stepGap : 0;
        c.stepsContainer.sizeDelta = new Vector2(0, stepsH);
        float totalH = baseCardH + stepsH;
        c.layoutElem.preferredHeight = totalH;
        c.layoutElem.minHeight = totalH;
        c.lastStepCount = count;
    }

    // ── card click ───────────────────────────

    void OnCardClicked(DroneController drone)
    {
        bool additive = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (!additive)
        {
            foreach (var d in allDrones)
                d.IsSelected = false;
        }

        drone.IsSelected = !drone.IsSelected || !additive;
    }

    // ── UI helpers ───────────────────────────

    GameObject MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    GameObject MakeText(Transform parent, string name, string content,
                        int fontSize, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.text = content;
        txt.font = uiFont;
        txt.fontSize = fontSize;
        txt.color = color;
        txt.alignment = alignment;
        return go;
    }
}
