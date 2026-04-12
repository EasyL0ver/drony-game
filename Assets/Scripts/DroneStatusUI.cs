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
        // Equipment slots
        public List<Image> slotBgs;
        public List<Text> slotLabels;
        public List<Button> slotButtons;
        // Total journey progress bar
        public Image journeyBarBg;
        public Image journeyBarFill;
        public Text journeyText;
        // Journey step rows
        public RectTransform stepsContainer;
        public List<StepRow> stepRows;
        public int lastStepCount;
    }

    readonly List<DroneCard> cards = new List<DroneCard>();
    List<DroneController> allDrones;
    GameManager gm;

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

    // Equipment slot colors
    static readonly Color slotEmptyCol    = new Color(0.06f, 0.06f, 0.09f, 0.8f);
    static readonly Color slotFilledCol   = new Color(0.04f, 0.14f, 0.20f, 0.9f);
    static readonly Color slotLockedCol   = new Color(0.04f, 0.04f, 0.06f, 0.5f);
    static readonly Color slotTextCol     = new Color(0.55f, 0.60f, 0.65f, 1f);
    static readonly Color slotGearCol     = new Color(0f, 0.85f, 1f, 0.95f);
    static readonly Color slotHoverCol    = new Color(0.08f, 0.18f, 0.25f, 0.9f);

    // Shop popup colors
    static readonly Color shopBgCol       = new Color(0.02f, 0.02f, 0.04f, 0.95f);
    static readonly Color shopItemCol     = new Color(0.06f, 0.06f, 0.09f, 0.9f);
    static readonly Color shopItemHoverCol= new Color(0.04f, 0.14f, 0.20f, 0.95f);
    static readonly Color pointsCol       = new Color(1f, 0.85f, 0.2f, 1f);

    // Journey total bar colors
    static readonly Color journeyBarBgCol   = new Color(0.06f, 0.06f, 0.08f, 0.9f);
    static readonly Color journeyBarFillCol = new Color(0f, 0.65f, 0.85f, 0.75f);
    static readonly Color journeyTextCol    = new Color(0.75f, 0.85f, 0.90f, 0.95f);

    const float baseCardH = 68f;  // increased to fit gear slots
    const float slotRowH  = 14f;
    const float journeyBarH = 12f;
    const float stepRowH  = 16f;
    const float stepGap   = 2f;

    Font uiFont;

    // Bottom-center hover tooltip
    GameObject hoverTooltipGO;
    Text hoverTooltipText;
    Image hoverTooltipBg;

    // Points display (top of panel)
    Text pointsText;

    // Gear shop popup
    GameObject shopPopupGO;
    Transform shopItemsParent;
    Text shopTitleText;
    Text shopPointsText;
    DroneController shopTargetDrone;
    int shopTargetSlot;
    Canvas statusCanvas;

    // ── public API ───────────────────────────

    public void Init(GameManager gameManager)
    {
        gm = gameManager;
        allDrones = gameManager.Drones;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont("Arial", 14);

        BuildCanvas(allDrones);
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
        statusCanvas = canvas;

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

        // Points display
        var ptsGO = new GameObject("Points");
        ptsGO.transform.SetParent(panel.transform, false);
        pointsText = ptsGO.AddComponent<Text>();
        pointsText.text = "";
        pointsText.font = uiFont;
        pointsText.fontSize = 11;
        pointsText.color = pointsCol;
        pointsText.alignment = TextAnchor.MiddleCenter;
        pointsText.fontStyle = FontStyle.Bold;
        var ptsLE = ptsGO.AddComponent<LayoutElement>();
        ptsLE.preferredHeight = 16;

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
        nameRT.offsetMin = new Vector2(8, -22);
        nameRT.offsetMax = new Vector2(-8, -2);

        // ── Row 2: equipment slots ──
        int maxSlots = drone.Model != null ? drone.Model.MaxSlots : 2;
        var slotBgs = new List<Image>();
        var slotLabels = new List<Text>();
        var slotButtons = new List<Button>();

        float slotY0 = -24f;
        float slotY1 = slotY0 - slotRowH;
        float slotWidth = 1f / maxSlots;
        for (int s = 0; s < maxSlots; s++)
        {
            var slotGO = MakeImage(cardGO.transform, $"Slot_{s}", slotEmptyCol);
            var slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.anchorMin = new Vector2(slotWidth * s, 1);
            slotRT.anchorMax = new Vector2(slotWidth * (s + 1), 1);
            slotRT.offsetMin = new Vector2(s == 0 ? 8 : 2, slotY1);
            slotRT.offsetMax = new Vector2(s == maxSlots - 1 ? -8 : -2, slotY0);

            var slotTxtGO = MakeText(slotGO.transform, "SlotText", "EMPTY", 9, slotTextCol,
                                     TextAnchor.MiddleCenter);
            var slotTxtRT = slotTxtGO.GetComponent<RectTransform>();
            slotTxtRT.anchorMin = Vector2.zero;
            slotTxtRT.anchorMax = Vector2.one;
            slotTxtRT.offsetMin = new Vector2(2, 0);
            slotTxtRT.offsetMax = new Vector2(-2, 0);

            var slotBtn = slotGO.AddComponent<Button>();
            var slotNav = slotBtn.navigation;
            slotNav.mode = Navigation.Mode.None;
            slotBtn.navigation = slotNav;
            slotBtn.transition = Selectable.Transition.None;
            int slotIdx = s;
            slotBtn.onClick.AddListener(() => OnSlotClicked(capturedDrone, slotIdx));

            slotBgs.Add(slotGO.GetComponent<Image>());
            slotLabels.Add(slotTxtGO.GetComponent<Text>());
            slotButtons.Add(slotBtn);
        }

        // ── Row 3: discrete energy segments + text ──
        float eY0 = -40f;
        float eY1 = -58f;
        var energyContGO = new GameObject("EnergyBar");
        energyContGO.transform.SetParent(cardGO.transform, false);
        var energyContRT = energyContGO.AddComponent<RectTransform>();
        energyContRT.anchorMin = new Vector2(0, 1);
        energyContRT.anchorMax = new Vector2(1, 1);
        energyContRT.offsetMin = new Vector2(8, eY1);
        energyContRT.offsetMax = new Vector2(-42, eY0);

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
        pctRT.offsetMin = new Vector2(-40, eY1);
        pctRT.offsetMax = new Vector2(-8, eY0);

        // ── Row 4: total journey progress bar ──
        var jBarGO = MakeImage(cardGO.transform, "JourneyBar", journeyBarBgCol);
        var jBarRT = jBarGO.GetComponent<RectTransform>();
        jBarRT.anchorMin = new Vector2(0, 1);
        jBarRT.anchorMax = new Vector2(1, 1);
        jBarRT.offsetMin = new Vector2(8, -(baseCardH - 2 + journeyBarH));
        jBarRT.offsetMax = new Vector2(-8, -(baseCardH - 2));

        var jFillGO = MakeImage(jBarGO.transform, "Fill", journeyBarFillCol);
        var jFillRT = jFillGO.GetComponent<RectTransform>();
        jFillRT.anchorMin = Vector2.zero;
        jFillRT.anchorMax = new Vector2(0, 1);
        jFillRT.offsetMin = Vector2.zero;
        jFillRT.offsetMax = Vector2.zero;

        var jTextGO = MakeText(jBarGO.transform, "JText", "", 9, journeyTextCol, TextAnchor.MiddleCenter);
        var jTextRT = jTextGO.GetComponent<RectTransform>();
        jTextRT.anchorMin = Vector2.zero;
        jTextRT.anchorMax = Vector2.one;
        jTextRT.offsetMin = Vector2.zero;
        jTextRT.offsetMax = Vector2.zero;

        jBarGO.SetActive(false);

        // ── Steps container — below journey bar, populated dynamically ──
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
            slotBgs = slotBgs,
            slotLabels = slotLabels,
            slotButtons = slotButtons,
            journeyBarBg = jBarGO.GetComponent<Image>(),
            journeyBarFill = jFillGO.GetComponent<Image>(),
            journeyText = jTextGO.GetComponent<Text>(),
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

            // ── Equipment slots ──
            bool atRefitStation = c.drone.IsRefitting;

            for (int s = 0; s < c.slotBgs.Count; s++)
            {
                var equip = c.drone.Model != null && c.drone.Model.Equipment != null
                    ? c.drone.Model.Equipment[s] : null;

                if (equip != null)
                {
                    c.slotLabels[s].text = equip.Icon + " " + equip.Name.ToUpper();
                    c.slotLabels[s].color = slotGearCol;
                    c.slotBgs[s].color = atRefitStation ? slotFilledCol : slotFilledCol * 0.7f;
                }
                else
                {
                    c.slotLabels[s].text = atRefitStation ? "+ EQUIP" : "EMPTY";
                    c.slotLabels[s].color = atRefitStation ? accentColor : slotTextCol;
                    c.slotBgs[s].color = atRefitStation ? slotEmptyCol : slotLockedCol;
                }
            }

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

            // ── Total journey progress bar ──
            bool hasJourney = stepCount > 0;
            c.journeyBarBg.gameObject.SetActive(hasJourney);
            if (hasJourney)
            {
                float jProg = c.drone.JourneyOverallProgress;
                c.journeyBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(jProg), 1);
                float jElapsed = c.drone.JourneyElapsedTime;
                float jTotal = c.drone.JourneyTotalTime;
                c.journeyText.text = $"{jElapsed:F1}s / {jTotal:F1}s  ⚡{c.drone.JourneyEnergyCost}";
            }
        }

        // ── Points display ──
        if (pointsText != null && gm != null && gm.Player != null)
            pointsText.text = $"⬡ {gm.Player.Points} POINTS";

        // ── Bottom hover tooltip ──
        UpdateHoverTooltip();
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
        float jBarExtra = count > 0 ? journeyBarH + 4f : 0f;
        float stepsH = count > 0 ? count * stepRowH + (count - 1) * stepGap + stepGap : 0;
        c.stepsContainer.sizeDelta = new Vector2(0, stepsH);
        c.stepsContainer.anchoredPosition = new Vector2(0, -(baseCardH + jBarExtra));
        float totalH = baseCardH + jBarExtra + stepsH;
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

    // ── equipment slot click ─────────────────

    void OnSlotClicked(DroneController drone, int slotIdx)
    {
        if (drone.Model == null || drone.Model.Equipment == null) return;

        // Must have completed a refit action
        if (!drone.IsRefitting) return;

        var equipped = drone.Model.Equipment[slotIdx];
        if (equipped != null)
        {
            // Unequip — refund points
            drone.Model.Unequip(slotIdx);
            gm.Player.Refund(equipped);
        }
        else
        {
            // Open shop popup for this slot
            OpenShop(drone, slotIdx);
        }
    }

    // ── gear shop popup ─────────────────────

    void OpenShop(DroneController drone, int slotIdx)
    {
        shopTargetDrone = drone;
        shopTargetSlot = slotIdx;

        if (shopPopupGO != null)
            Destroy(shopPopupGO);

        if (statusCanvas == null) return;

        // Full-screen overlay to catch outside clicks
        shopPopupGO = new GameObject("ShopOverlay");
        shopPopupGO.transform.SetParent(statusCanvas.transform, false);
        var overlayRT = shopPopupGO.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        // Dim background — click to close
        var dimImg = shopPopupGO.AddComponent<Image>();
        dimImg.color = new Color(0, 0, 0, 0.4f);
        var dimBtn = shopPopupGO.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        var dimNav = dimBtn.navigation;
        dimNav.mode = Navigation.Mode.None;
        dimBtn.navigation = dimNav;
        dimBtn.onClick.AddListener(CloseShop);

        // Shop panel — centered
        var panelGO = MakeImage(shopPopupGO.transform, "ShopPanel", shopBgCol);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        float panelW = 260f;
        float itemH = 44f;
        float headerH = 50f;
        float panelH = headerH + GearCatalog.All.Length * (itemH + 4) + 12;
        panelRT.sizeDelta = new Vector2(panelW, panelH);

        // Stop clicks on panel from closing
        var panelBtn = panelGO.AddComponent<Button>();
        panelBtn.transition = Selectable.Transition.None;
        var pNav = panelBtn.navigation;
        pNav.mode = Navigation.Mode.None;
        panelBtn.navigation = pNav;

        var panelOutl = panelGO.AddComponent<Outline>();
        panelOutl.effectColor = new Color(0f, 0.85f, 1f, 0.4f);
        panelOutl.effectDistance = new Vector2(2, -2);

        // Header
        var titleGO = MakeText(panelGO.transform, "Title", $"EQUIP {drone.DroneName}", 14, accentColor,
                               TextAnchor.MiddleCenter);
        titleGO.GetComponent<Text>().fontStyle = FontStyle.Bold;
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.offsetMin = new Vector2(8, -28);
        titleRT.offsetMax = new Vector2(-8, -4);

        var ptsGO = MakeText(panelGO.transform, "Points", $"⬡ {gm.Player.Points} POINTS", 12,
                             pointsCol, TextAnchor.MiddleCenter);
        ptsGO.GetComponent<Text>().fontStyle = FontStyle.Bold;
        shopPointsText = ptsGO.GetComponent<Text>();
        var ptsRT = ptsGO.GetComponent<RectTransform>();
        ptsRT.anchorMin = new Vector2(0, 1);
        ptsRT.anchorMax = new Vector2(1, 1);
        ptsRT.offsetMin = new Vector2(8, -48);
        ptsRT.offsetMax = new Vector2(-8, -28);

        // Gear items
        for (int g = 0; g < GearCatalog.All.Length; g++)
        {
            var gear = GearCatalog.All[g];
            float yTop = -(headerH + g * (itemH + 4));
            float yBot = yTop - itemH;

            bool alreadyEquipped = drone.Model.HasGear(gear.Type);
            bool canAfford = gm.Player.Points >= gear.Cost;
            bool canBuy = canAfford && !alreadyEquipped;

            var itemGO = MakeImage(panelGO.transform, "Item_" + gear.Name,
                                   canBuy ? shopItemCol : shopItemCol * 0.5f);
            var itemRT = itemGO.GetComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0, 1);
            itemRT.anchorMax = new Vector2(1, 1);
            itemRT.offsetMin = new Vector2(8, yBot);
            itemRT.offsetMax = new Vector2(-8, yTop);

            // Gear name + icon
            var nameTxt = MakeText(itemGO.transform, "Name", $"{gear.Icon} {gear.Name}", 12,
                                   canBuy ? accentColor : dimTextColor, TextAnchor.MiddleLeft);
            var nameRT2 = nameTxt.GetComponent<RectTransform>();
            nameRT2.anchorMin = Vector2.zero;
            nameRT2.anchorMax = new Vector2(0.6f, 0.55f);
            nameRT2.offsetMin = new Vector2(8, 0);
            nameRT2.offsetMax = new Vector2(0, 0);

            // Description
            var descTxt = MakeText(itemGO.transform, "Desc", gear.Description, 9,
                                   dimTextColor, TextAnchor.UpperLeft);
            var descRT = descTxt.GetComponent<RectTransform>();
            descRT.anchorMin = new Vector2(0, 0.55f);
            descRT.anchorMax = Vector2.one;
            descRT.offsetMin = new Vector2(8, 0);
            descRT.offsetMax = new Vector2(-8, -2);

            // Cost + buy button
            string costStr = alreadyEquipped ? "EQUIPPED" : $"⬡ {gear.Cost}";
            Color costColor = alreadyEquipped ? dimTextColor : (canAfford ? pointsCol : energyLowCol);
            var costTxt = MakeText(itemGO.transform, "Cost", costStr, 11,
                                   costColor, TextAnchor.MiddleRight);
            costTxt.GetComponent<Text>().fontStyle = FontStyle.Bold;
            var costRT = costTxt.GetComponent<RectTransform>();
            costRT.anchorMin = new Vector2(0.6f, 0);
            costRT.anchorMax = new Vector2(1, 0.55f);
            costRT.offsetMin = new Vector2(0, 0);
            costRT.offsetMax = new Vector2(-8, 0);

            if (canBuy)
            {
                var itemBtn = itemGO.AddComponent<Button>();
                itemBtn.transition = Selectable.Transition.ColorTint;
                var colors = itemBtn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.6f, 1f, 1f, 1f);
                colors.pressedColor = new Color(0.4f, 0.8f, 0.8f, 1f);
                itemBtn.colors = colors;
                var iNav = itemBtn.navigation;
                iNav.mode = Navigation.Mode.None;
                itemBtn.navigation = iNav;

                var capturedGear = gear;
                itemBtn.onClick.AddListener(() => OnBuyGear(capturedGear));
            }
        }
    }

    void OnBuyGear(GearItem gear)
    {
        if (shopTargetDrone == null || shopTargetDrone.Model == null) return;
        if (gm == null || gm.Player == null) return;

        if (!gm.Player.TryPurchase(gear)) return;

        int result = shopTargetDrone.Model.Equip(gear);
        if (result < 0)
        {
            // Slots full — refund
            gm.Player.Refund(gear);
            return;
        }

        CloseShop();
    }

    void CloseShop()
    {
        if (shopPopupGO != null)
            Destroy(shopPopupGO);
        shopPopupGO = null;
        shopTargetDrone = null;
    }

    // ── UI helpers ───────────────────────────

    void EnsureHoverTooltip()
    {
        if (hoverTooltipGO != null) return;

        // Find the status canvas (first canvas child)
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;

        hoverTooltipGO = new GameObject("HoverTooltip");
        hoverTooltipGO.transform.SetParent(canvas.transform, false);
        var rt = hoverTooltipGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 16);
        rt.sizeDelta = new Vector2(400, 36);

        hoverTooltipBg = hoverTooltipGO.AddComponent<Image>();
        hoverTooltipBg.color = new Color(0.03f, 0.03f, 0.06f, 0.88f);

        var outl = hoverTooltipGO.AddComponent<Outline>();
        outl.effectColor = new Color(0f, 0.85f, 1f, 0.3f);
        outl.effectDistance = new Vector2(1, -1);

        var txtGO = MakeText(hoverTooltipGO.transform, "Text", "", 14, Color.white, TextAnchor.MiddleCenter);
        hoverTooltipText = txtGO.GetComponent<Text>();
        hoverTooltipText.fontStyle = FontStyle.Bold;
        hoverTooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(8, 0);
        txtRT.offsetMax = new Vector2(-8, 0);

        hoverTooltipGO.SetActive(false);
    }

    void UpdateHoverTooltip()
    {
        EnsureHoverTooltip();
        if (hoverTooltipGO == null) return;

        // Gather preview info from all selected drones
        float totalTime = 0f;
        int totalEnergy = 0;
        bool anyPreview = false;
        bool anyOverBudget = false;

        foreach (var c in cards)
        {
            if (c.drone == null || !c.drone.IsSelected) continue;
            if (!c.drone.IsShowingPreview) continue;

            anyPreview = true;
            totalTime += c.drone.PreviewTotalTime;
            totalEnergy += c.drone.PreviewEnergyCost;
            if (c.drone.PreviewExceedsEnergy) anyOverBudget = true;
        }

        if (!anyPreview)
        {
            hoverTooltipGO.SetActive(false);
            return;
        }

        hoverTooltipGO.SetActive(true);

        if (anyOverBudget)
        {
            hoverTooltipText.text = $"<color=#FF3333>NOT ENOUGH ENERGY</color>   ⏱ {totalTime:F1}s   ⚡{totalEnergy}";
            hoverTooltipBg.color = new Color(0.12f, 0.02f, 0.02f, 0.90f);
        }
        else
        {
            hoverTooltipText.text = $"⏱ {totalTime:F1}s   ⚡{totalEnergy}";
            hoverTooltipBg.color = new Color(0.03f, 0.03f, 0.06f, 0.88f);
        }
    }

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
