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
        public Text energyText;
        public Image cardBg;
        public LayoutElement layoutElem;
        public Outline cardOutline;
        // Discrete energy segments
        public RectTransform energyContainer;
        public List<Image> energySegments;
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

    // Energy segment colors
    static readonly Color segFullCol     = new Color(0f, 0.85f, 1f, 0.9f);
    static readonly Color segMidCol      = new Color(1f, 0.75f, 0f, 0.9f);
    static readonly Color segLowCol      = new Color(1f, 0.25f, 0.10f, 0.9f);
    static readonly Color segEmptyCol    = new Color(0.10f, 0.10f, 0.12f, 0.5f);
    static readonly Color segPreviewCol  = new Color(1f, 0.55f, 0f, 0.7f);

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

        // ── Row 2: discrete energy segments + text ──
        var energyContGO = new GameObject("EnergyBar");
        energyContGO.transform.SetParent(cardGO.transform, false);
        var energyContRT = energyContGO.AddComponent<RectTransform>();
        energyContRT.anchorMin = new Vector2(0, 1);
        energyContRT.anchorMax = new Vector2(1, 1);
        energyContRT.offsetMin = new Vector2(8, -44);
        energyContRT.offsetMax = new Vector2(-42, -26);

        // Create individual segment images
        int maxE = drone.MaxEnergy;
        var segments = new List<Image>();
        float segGap = 1.5f;
        for (int s = 0; s < maxE; s++)
        {
            var segGO = MakeImage(energyContGO.transform, $"Seg_{s}", segFullCol);
            var segRT = segGO.GetComponent<RectTransform>();
            float xMin = (float)s / maxE;
            float xMax = (float)(s + 1) / maxE;
            segRT.anchorMin = new Vector2(xMin, 0);
            segRT.anchorMax = new Vector2(xMax, 1);
            float halfGap = segGap * 0.5f;
            segRT.offsetMin = new Vector2(s == 0 ? 0 : halfGap, 1);
            segRT.offsetMax = new Vector2(s == maxE - 1 ? 0 : -halfGap, -1);
            segments.Add(segGO.GetComponent<Image>());
        }

        var pctGO = MakeText(cardGO.transform, "Pct", $"{maxE}/{maxE}", 11, accentColor,
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
            energyText = pctGO.GetComponent<Text>(),
            cardBg = cardImg,
            layoutElem = le,
            cardOutline = outline,
            energyContainer = energyContRT,
            energySegments = segments,
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

            int curE = c.drone.CurrentEnergy;
            int maxE = c.drone.MaxEnergy;

            // Determine how many segments are "threatened" by journey or preview
            int journeyCost = c.drone.JourneyEnergyCost;
            int previewCost = c.drone.PreviewEnergyCost;
            int totalPending = journeyCost + previewCost;
            bool overBudget = c.drone.PreviewExceedsEnergy;
            Color insufficientCol = new Color(1f, 0.15f, 0.10f, 0.85f);

            // Update discrete energy segments
            for (int s = 0; s < c.energySegments.Count && s < maxE; s++)
            {
                Color col;
                if (s >= curE)
                {
                    // Empty — flash red when over budget to show deficit
                    col = (overBudget && s < curE + (totalPending - curE))
                        ? insufficientCol * (0.5f + 0.2f * Mathf.Sin(Time.time * 6f))
                        : segEmptyCol;
                }
                else if (s >= curE - totalPending)
                {
                    if (overBudget)
                    {
                        col = insufficientCol; // all pending segments red
                    }
                    else if (previewCost > 0 && s >= curE - previewCost)
                    {
                        col = segPreviewCol; // preview (hover) cost — orange
                    }
                    else
                    {
                        col = segPreviewCol * 0.7f; // committed journey cost — dimmer orange
                    }
                }
                else
                {
                    // Safe segment — color based on fill level
                    float frac = (float)curE / maxE;
                    if (frac > 0.5f) col = segFullCol;
                    else if (frac > 0.25f) col = segMidCol;
                    else col = segLowCol;
                }
                c.energySegments[s].color = col;
            }

            // Energy text — red when preview would exceed available energy
            if (overBudget)
            {
                int available = curE - journeyCost;
                c.energyText.text = $"<color=#FF2222>{curE}/{maxE} (need {previewCost}, have {available})</color>";
            }
            else
            {
                c.energyText.text = $"{curE}/{maxE}";
            }

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

                string costTag = step.energyCost > 0 ? $" ⚡{step.energyCost}" : "";
                string prefix = isActive ? "\u25B8 " : isCompleted ? "\u2713 " : "  ";
                row.label.text = prefix + step.label + costTag;
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
