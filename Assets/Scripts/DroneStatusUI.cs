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
        // Equipment slots (rectangular icon boxes)
        public List<Image> slotBgs;
        public List<Text> slotIcons;
        public List<Text> slotLabels;
        public List<Button> slotButtons;
        // Journey display (step bar + overall bar)
        public GameObject journeyRow;       // parent container
        public Image stepBarBg;
        public Image stepBarFill;
        public Text stepLabel;
        public Text stepTime;
        public Image journeyBarBg;
        public Image journeyBarFill;
        public Text journeyTime;
    }

    readonly List<DroneCard> cards = new List<DroneCard>();
    List<DroneController> allDrones;
    GameManager gm;

    // Palette (matches hex-map sci-fi theme)
    static readonly Color panelColor      = new Color(0.02f, 0.02f, 0.04f, 0.80f);
    static readonly Color cardColor       = new Color(0.05f, 0.05f, 0.08f, 0.90f);
    static readonly Color cardSelectedCol = new Color(0.04f, 0.12f, 0.18f, 0.95f);
    static readonly Color cardSelectedBorderCol = Palette.WithAlpha(Palette.SelectionRing, 0.6f);
    static readonly Color nameSelectedCol = Color.white;
    static readonly Color accentColor     = Palette.DroneIdle;
    static readonly Color dimTextColor    = new Color(0.45f, 0.50f, 0.55f, 1f);
    static readonly Color barBgColor      = new Color(0.08f, 0.08f, 0.10f, 1f);
    static readonly Color energyFullCol   = Palette.DroneMoving;
    static readonly Color energyLowCol    = Palette.DroneDepleted;

    // Energy segment colors
    static readonly Color segFullCol     = Palette.WithAlpha(Palette.DroneMoving, 0.9f);
    static readonly Color segMidCol      = new Color(1f, 0.75f, 0f, 0.9f);
    static readonly Color segLowCol      = Palette.WithAlpha(Palette.DroneDepleted, 0.9f);
    static readonly Color segEmptyCol    = new Color(0.10f, 0.10f, 0.12f, 0.5f);
    static readonly Color segPreviewCol  = new Color(1f, 0.55f, 0f, 0.7f);

    // Equipment slot colors
    static readonly Color slotEmptyCol    = new Color(0.06f, 0.06f, 0.09f, 0.8f);
    static readonly Color slotFilledCol   = new Color(0.04f, 0.14f, 0.20f, 0.9f);
    static readonly Color slotLockedCol   = new Color(0.04f, 0.04f, 0.06f, 0.5f);
    static readonly Color slotTextCol     = new Color(0.55f, 0.60f, 0.65f, 1f);
    static readonly Color slotGearCol     = Palette.DroneIdle;
    static readonly Color slotHoverCol    = new Color(0.08f, 0.18f, 0.25f, 0.9f);

    // Shop popup colors
    static readonly Color shopBgCol       = new Color(0.02f, 0.02f, 0.04f, 0.95f);
    static readonly Color shopItemCol     = new Color(0.06f, 0.06f, 0.09f, 0.9f);
    static readonly Color shopItemHoverCol= new Color(0.04f, 0.14f, 0.20f, 0.95f);
    static readonly Color pointsCol       = new Color(1f, 0.85f, 0.2f, 1f);

    // Journey bar colors
    static readonly Color journeyBarBgCol   = new Color(0.06f, 0.06f, 0.08f, 0.9f);
    static readonly Color stepBarFillCol    = Palette.WithAlpha(Palette.DroneMoving, 0.85f);
    static readonly Color journeyBarFillCol = Palette.WithAlpha(Palette.DroneMoving, 0.45f);
    static readonly Color journeyTextCol    = new Color(0.75f, 0.85f, 0.90f, 0.95f);

    const float baseCardH = 58f;
    const float slotSize  = 22f;
    const float slotGap   = 3f;
    const float cardPad   = 5f;

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
        panelRT.sizeDelta = new Vector2(200, 0);

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

        // Touch/mouse drag to issue move orders
        var drag = cardGO.AddComponent<DroneCardDrag>();
        drag.Drone = drone;
        drag.GM = gm;

        // Selection border (hidden by default)
        var outline = cardGO.AddComponent<Outline>();
        outline.effectColor = cardSelectedBorderCol;
        outline.effectDistance = new Vector2(1.5f, 1.5f);
        outline.enabled = false;

        // ════════════════════════════════════
        //  VERTICAL LAYOUT (full width rows)
        // ════════════════════════════════════
        float L = cardPad, R = -cardPad; // left/right insets

        // ── Row 1: Name + corridor-size dots ──
        float nY0 = -2f, nY1 = -17f;

        var nameGO = MakeText(cardGO.transform, "Name", drone.DroneName, 12, accentColor,
                              TextAnchor.MiddleLeft);
        nameGO.GetComponent<Text>().fontStyle = FontStyle.Bold;
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 1); nameRT.anchorMax = new Vector2(1, 1);
        nameRT.offsetMin = new Vector2(L, nY1); nameRT.offsetMax = new Vector2(-40, nY0);

        PassageType[] sizes = { PassageType.Corridor, PassageType.Duct, PassageType.Vent };
        Color[] dotColors = { Palette.CorridorGlow, Palette.DuctGlow, Palette.VentGlow };
        float dotSize = 5f, dotGap2 = 3f;
        for (int d = 0; d < sizes.Length; d++)
        {
            var dotGO = MakeImage(cardGO.transform, $"Dot_{sizes[d]}", dotColors[d]);
            var dotRT = dotGO.GetComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(1, 1); dotRT.anchorMax = new Vector2(1, 1);
            dotRT.pivot = new Vector2(1, 0.5f);
            float dx = -cardPad - (sizes.Length - 1 - d) * (dotSize + dotGap2);
            dotRT.anchoredPosition = new Vector2(dx, (nY0 + nY1) * 0.5f);
            dotRT.sizeDelta = new Vector2(dotSize, dotSize);
        }

        // ── Row 2: Energy segments + count ──
        float eY0 = -19f, eY1 = -32f;

        var energyContGO = new GameObject("EnergyBar");
        energyContGO.transform.SetParent(cardGO.transform, false);
        var energyContRT = energyContGO.AddComponent<RectTransform>();
        energyContRT.anchorMin = new Vector2(0, 1); energyContRT.anchorMax = new Vector2(1, 1);
        energyContRT.offsetMin = new Vector2(L, eY1); energyContRT.offsetMax = new Vector2(-38, eY0);

        int maxE = drone.MaxEnergy;
        var segments = new List<Image>();
        float segGap = 1.5f;
        for (int s = 0; s < maxE; s++)
        {
            var segGO = MakeImage(energyContGO.transform, $"Seg_{s}", segFullCol);
            var segRT = segGO.GetComponent<RectTransform>();
            float xMin = (float)s / maxE;
            float xMax = (float)(s + 1) / maxE;
            segRT.anchorMin = new Vector2(xMin, 0); segRT.anchorMax = new Vector2(xMax, 1);
            float halfGap = segGap * 0.5f;
            segRT.offsetMin = new Vector2(s == 0 ? 0 : halfGap, 1);
            segRT.offsetMax = new Vector2(s == maxE - 1 ? 0 : -halfGap, -1);
            segments.Add(segGO.GetComponent<Image>());
        }

        var pctGO = MakeText(cardGO.transform, "Pct", $"{maxE}/{maxE}", 10, accentColor,
                             TextAnchor.MiddleRight);
        var pctRT = pctGO.GetComponent<RectTransform>();
        pctRT.anchorMin = new Vector2(1, 1); pctRT.anchorMax = new Vector2(1, 1);
        pctRT.offsetMin = new Vector2(-36, eY1); pctRT.offsetMax = new Vector2(R, eY0);

        // ── Row 3: Equipment slots (inline) ──
        float slY = -34f;
        int maxSlots = drone.Model != null ? drone.Model.MaxSlots : 2;
        var slotBgs = new List<Image>();
        var slotIcons = new List<Text>();
        var slotLabels = new List<Text>();
        var slotButtons = new List<Button>();

        for (int s = 0; s < maxSlots; s++)
        {
            float slotX = L + s * (slotSize + slotGap);

            var slotGO = MakeImage(cardGO.transform, $"Slot_{s}", slotEmptyCol);
            var slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.anchorMin = new Vector2(0, 1); slotRT.anchorMax = new Vector2(0, 1);
            slotRT.pivot = new Vector2(0, 1);
            slotRT.anchoredPosition = new Vector2(slotX, slY);
            slotRT.sizeDelta = new Vector2(slotSize, slotSize);

            var slotOutline = slotGO.AddComponent<Outline>();
            slotOutline.effectColor = new Color(0.25f, 0.35f, 0.40f, 0.6f);
            slotOutline.effectDistance = new Vector2(1, -1);

            var iconGO = MakeText(slotGO.transform, "Icon", "", 11, slotTextCol,
                                  TextAnchor.MiddleCenter);
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0, 0.30f); iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(1, 0); iconRT.offsetMax = new Vector2(-1, -1);

            var lblGO = MakeText(slotGO.transform, "Label", "EMPTY", 5, slotTextCol,
                                 TextAnchor.MiddleCenter);
            var lblRT = lblGO.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = new Vector2(1, 0.30f);
            lblRT.offsetMin = new Vector2(1, 0); lblRT.offsetMax = new Vector2(-1, 0);

            var slotBtn = slotGO.AddComponent<Button>();
            var slotNav = slotBtn.navigation;
            slotNav.mode = Navigation.Mode.None;
            slotBtn.navigation = slotNav;
            slotBtn.transition = Selectable.Transition.None;
            int slotIdx = s;
            slotBtn.onClick.AddListener(() => OnSlotClicked(capturedDrone, slotIdx));

            slotBgs.Add(slotGO.GetComponent<Image>());
            slotIcons.Add(iconGO.GetComponent<Text>());
            slotLabels.Add(lblGO.GetComponent<Text>());
            slotButtons.Add(slotBtn);
        }

        // ── Journey bars (below slots, full width) ──
        float jBase = slY - slotSize - 2f;         // below slots
        float barH = 10f, barGap = 2f;
        float sBarY0 = jBase, sBarY1 = jBase - barH;
        float oBarY0 = sBarY1 - barGap, oBarY1 = oBarY0 - barH;

        var jContGO = new GameObject("JourneyContainer");
        jContGO.transform.SetParent(cardGO.transform, false);
        var jContRT = jContGO.AddComponent<RectTransform>();
        jContRT.anchorMin = Vector2.zero; jContRT.anchorMax = Vector2.one;
        jContRT.offsetMin = Vector2.zero; jContRT.offsetMax = Vector2.zero;

        // Step bar
        var sBgGO = MakeImage(jContGO.transform, "StepBg", journeyBarBgCol);
        var sBgRT = sBgGO.GetComponent<RectTransform>();
        sBgRT.anchorMin = new Vector2(0, 1); sBgRT.anchorMax = new Vector2(1, 1);
        sBgRT.offsetMin = new Vector2(L, sBarY1); sBgRT.offsetMax = new Vector2(R, sBarY0);

        var sFillGO = MakeImage(sBgGO.transform, "Fill", stepBarFillCol);
        var sFillRT = sFillGO.GetComponent<RectTransform>();
        sFillRT.anchorMin = Vector2.zero; sFillRT.anchorMax = new Vector2(0, 1);
        sFillRT.offsetMin = Vector2.zero; sFillRT.offsetMax = Vector2.zero;

        var sLabelGO = MakeText(sBgGO.transform, "SLabel", "", 7, journeyTextCol, TextAnchor.MiddleLeft);
        var sLabelRT = sLabelGO.GetComponent<RectTransform>();
        sLabelRT.anchorMin = Vector2.zero; sLabelRT.anchorMax = Vector2.one;
        sLabelRT.offsetMin = new Vector2(3, 0); sLabelRT.offsetMax = new Vector2(-3, 0);

        var sTimeGO = MakeText(sBgGO.transform, "STime", "", 7, journeyTextCol, TextAnchor.MiddleRight);
        var sTimeRT = sTimeGO.GetComponent<RectTransform>();
        sTimeRT.anchorMin = Vector2.zero; sTimeRT.anchorMax = Vector2.one;
        sTimeRT.offsetMin = new Vector2(3, 0); sTimeRT.offsetMax = new Vector2(-3, 0);

        // Overall journey bar
        var oBgGO = MakeImage(jContGO.transform, "JourneyBg", journeyBarBgCol);
        var oBgRT = oBgGO.GetComponent<RectTransform>();
        oBgRT.anchorMin = new Vector2(0, 1); oBgRT.anchorMax = new Vector2(1, 1);
        oBgRT.offsetMin = new Vector2(L, oBarY1); oBgRT.offsetMax = new Vector2(R, oBarY0);

        var oFillGO = MakeImage(oBgGO.transform, "Fill", journeyBarFillCol);
        var oFillRT = oFillGO.GetComponent<RectTransform>();
        oFillRT.anchorMin = Vector2.zero; oFillRT.anchorMax = new Vector2(0, 1);
        oFillRT.offsetMin = Vector2.zero; oFillRT.offsetMax = Vector2.zero;

        var oTimeGO = MakeText(oBgGO.transform, "OTime", "", 7, journeyTextCol, TextAnchor.MiddleRight);
        var oTimeRT = oTimeGO.GetComponent<RectTransform>();
        oTimeRT.anchorMin = Vector2.zero; oTimeRT.anchorMax = Vector2.one;
        oTimeRT.offsetMin = new Vector2(3, 0); oTimeRT.offsetMax = new Vector2(-3, 0);

        jContGO.SetActive(false);

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
            slotIcons = slotIcons,
            slotLabels = slotLabels,
            slotButtons = slotButtons,
            journeyRow = jContGO,
            stepBarBg = sBgGO.GetComponent<Image>(),
            stepBarFill = sFillGO.GetComponent<Image>(),
            stepLabel = sLabelGO.GetComponent<Text>(),
            stepTime = sTimeGO.GetComponent<Text>(),
            journeyBarBg = oBgGO.GetComponent<Image>(),
            journeyBarFill = oFillGO.GetComponent<Image>(),
            journeyTime = oTimeGO.GetComponent<Text>(),
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
                    c.slotIcons[s].text = equip.Icon;
                    c.slotIcons[s].color = slotGearCol;
                    c.slotLabels[s].text = equip.Name.ToUpper();
                    c.slotLabels[s].color = slotGearCol;
                    c.slotBgs[s].color = atRefitStation ? slotFilledCol : slotFilledCol * 0.7f;
                }
                else
                {
                    c.slotIcons[s].text = atRefitStation ? "+" : "—";
                    c.slotIcons[s].color = atRefitStation ? accentColor : slotTextCol;
                    c.slotLabels[s].text = atRefitStation ? "EQUIP" : "EMPTY";
                    c.slotLabels[s].color = atRefitStation ? accentColor : slotTextCol;
                    c.slotBgs[s].color = atRefitStation ? slotEmptyCol : slotLockedCol;
                }
            }

            // ── Journey display (step bar + overall bar) ──
            var journey = c.drone.Journey;
            int stepCount = journey.Count;
            int activeIdx = c.drone.JourneyCurrentIndex;
            bool hasJourney = stepCount > 0 && activeIdx >= 0 && activeIdx < stepCount;

            c.journeyRow.SetActive(hasJourney);
            float targetH = hasJourney ? baseCardH + 24f : baseCardH;
            c.layoutElem.preferredHeight = targetH;
            c.layoutElem.minHeight = targetH;
            if (hasJourney)
            {
                var step = journey[activeIdx];

                // Step bar: current action progress
                float stepProg = c.drone.GetJourneyStepProgress(activeIdx);
                c.stepBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(stepProg), 1);
                string costTag = step.energyCost > 0 ? $" ⚡{step.energyCost}" : "";
                c.stepLabel.text = "\u25B8 " + step.label + costTag;
                float stepRemain = step.duration * (1f - stepProg);
                c.stepTime.text = $"{Mathf.Max(0, stepRemain):F1}s";

                // Overall journey bar
                float overallProg = c.drone.JourneyOverallProgress;
                c.journeyBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(overallProg), 1);
                float totalRemain = c.drone.JourneyTotalTime - c.drone.JourneyElapsedTime;
                c.journeyTime.text = $"{Mathf.Max(0, totalRemain):F1}s total";
            }
        }

        // ── Points display ──
        if (pointsText != null && gm != null && gm.Player != null)
            pointsText.text = $"⬡ {gm.Player.Points} POINTS";

        // ── Bottom hover tooltip ──
        UpdateHoverTooltip();
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
        panelOutl.effectColor = Palette.WithAlpha(Palette.DroneIdle, 0.4f);
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
        outl.effectColor = Palette.WithAlpha(Palette.DroneIdle, 0.3f);
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
