using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Position anchor for an overlay step bar.</summary>
public struct StepAnchor
{
    public Vector3 worldPos;
    public Vector2Int roomA, roomB;
    public int layer;
    public bool overBudget;
}

/// <summary>
/// Central overlay that aggregates step bars from all drones.
/// Groups bars at the same anchor (passage/station) so multiple drones
/// show one shared label with stacked per-drone progress bars.
/// Energy is displayed to the left of each bar.
/// </summary>
public class OverlayManager : MonoBehaviour
{
    List<DroneController> drones;
    Canvas canvas;
    CanvasScaler scaler;

    const float barWidth = 110f;
    const float infoWidth = 50f;
    const float rowHeight = 20f;
    const float labelHeight = 16f;
    const float doneFlashDuration = 0.4f;

    // tracks when each step completed: (droneIndex, groupKey hash, stepLabel) → Time.time
    readonly Dictionary<long, float> completionTimes = new Dictionary<long, float>();

    // ── per-frame scratch (reused, zero alloc) ──
    readonly List<StepEntry> entries = new List<StepEntry>();
    readonly Dictionary<GroupKey, int> keyToGroup = new Dictionary<GroupKey, int>();
    readonly List<List<StepEntry>> groups = new List<List<StepEntry>>();
    readonly List<Vector3> groupPositions = new List<Vector3>();
    int activeGroupCount;

    // ── pooled UI ──
    readonly List<GroupUI> groupPool = new List<GroupUI>();
    Font font;

    struct StepEntry
    {
        public StepAnchor anchor;
        public string label;
        public int energyCost;
        public int stepIndex;
        public float progress, elapsed, total;
        public bool isDone, isActive, isPreview;
        public bool overBudget;
        public int droneIndex;
    }

    struct GroupKey : System.IEquatable<GroupKey>
    {
        public Vector2Int roomA, roomB;
        public int layer;

        public override int GetHashCode()
            => roomA.GetHashCode() ^ (roomB.GetHashCode() * 397) ^ (layer * 7919);
        public bool Equals(GroupKey other)
            => roomA == other.roomA && roomB == other.roomB && layer == other.layer;
        public override bool Equals(object obj) => obj is GroupKey k && Equals(k);
    }

    struct BarUI
    {
        public GameObject root;
        public RectTransform rect;
        public Text infoText;
        public Image bgImage, fillImage;
        public RectTransform fillRect;
        public Text timeText;
    }

    struct GroupUI
    {
        public GameObject root;
        public RectTransform rect;
        public Text label;
        public List<BarUI> bars;
    }

    public void Init(List<DroneController> droneList)
    {
        drones = droneList;
        EnsureCanvas();
    }

    void EnsureCanvas()
    {
        if (canvas != null) return;
        var go = new GameObject("OverlayCanvas");
        DontDestroyOnLoad(go);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
    }

    void LateUpdate()
    {
        if (drones == null) return;
        CollectEntries();
        GroupEntries();
        RenderGroups();
    }

    // ── collection ──────────────────────────

    void CollectEntries()
    {
        entries.Clear();
        foreach (var drone in drones)
        {
            if (drone == null) continue;

            // Active journey steps
            var journey = drone.Journey;
            var anchors = drone.JourneyAnchors;
            int idx = drone.JourneyCurrentIndex;
            if (journey != null && anchors != null && idx >= 0)
            {
                for (int i = 0; i < journey.Count && i < anchors.Count; i++)
                {
                    entries.Add(new StepEntry
                    {
                        anchor = anchors[i],
                        label = journey[i].label,
                        energyCost = journey[i].energyCost,
                        stepIndex = i,
                        progress = drone.GetJourneyStepProgress(i),
                        elapsed = drone.GetJourneyStepElapsed(i),
                        total = journey[i].duration,
                        isDone = i < idx,
                        isActive = i == idx,
                        isPreview = false,
                        overBudget = false,
                        droneIndex = drone.DroneIndex,
                    });
                }
            }

            // Hover preview steps
            var pvPlan = drone.PreviewJourney;
            var pvAnchors = drone.PreviewAnchors;
            if (drone.IsShowingPreview && pvPlan != null && pvAnchors != null)
            {
                for (int i = 0; i < pvPlan.Count && i < pvAnchors.Count; i++)
                {
                    entries.Add(new StepEntry
                    {
                        anchor = pvAnchors[i],
                        label = pvPlan[i].label,
                        energyCost = pvPlan[i].energyCost,
                        stepIndex = i,
                        progress = 0f,
                        elapsed = 0f,
                        total = pvPlan[i].duration,
                        isDone = false,
                        isActive = false,
                        isPreview = true,
                        overBudget = pvAnchors[i].overBudget,
                        droneIndex = drone.DroneIndex,
                    });
                }
            }
        }
    }

    // ── grouping ────────────────────────────

    static GroupKey KeyFromAnchor(StepAnchor a)
    {
        var key = new GroupKey { roomA = a.roomA, roomB = a.roomB, layer = a.layer };
        // Canonical ordering so A→B and B→A map to the same passage
        if (key.roomA.x > key.roomB.x
            || (key.roomA.x == key.roomB.x && key.roomA.y > key.roomB.y))
        {
            var tmp = key.roomA;
            key.roomA = key.roomB;
            key.roomB = tmp;
        }
        return key;
    }

    void GroupEntries()
    {
        keyToGroup.Clear();
        activeGroupCount = 0;
        for (int i = 0; i < groups.Count; i++) groups[i].Clear();
        groupPositions.Clear();

        foreach (var e in entries)
        {
            var key = KeyFromAnchor(e.anchor);
            if (!keyToGroup.TryGetValue(key, out int gi))
            {
                gi = activeGroupCount++;
                keyToGroup[key] = gi;
                while (groups.Count <= gi) groups.Add(new List<StepEntry>());
                groupPositions.Add(e.anchor.worldPos);
            }
            groups[gi].Add(e);
        }

        // Stable sort: droneIndex, then journey before preview
        for (int gi = 0; gi < activeGroupCount; gi++)
        {
            groups[gi].Sort((a, b) =>
            {
                int c = a.droneIndex.CompareTo(b.droneIndex);
                if (c != 0) return c;
                return a.isPreview.CompareTo(b.isPreview);
            });
        }
    }

    // ── rendering ───────────────────────────

    void RenderGroups()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        for (int gi = 0; gi < activeGroupCount; gi++)
        {
            var group = GetOrCreateGroup(gi);
            var items = groups[gi];
            if (items.Count == 0) { group.root.SetActive(false); continue; }

            Vector3 screen = cam.WorldToScreenPoint(groupPositions[gi]);
            if (screen.z <= 0) { group.root.SetActive(false); continue; }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, screen, null, out var canvasPos);
            group.rect.localPosition = canvasPos;
            group.root.SetActive(true);

            // Shared label from first item
            var first = items[0];
            group.label.text = first.label;
            group.label.color = first.isPreview
                ? (first.overBudget
                    ? new Color(1f, 0.30f, 0.25f, 1f)
                    : new Color(1f, 0.85f, 0.3f, 1f))
                : new Color(0f, 0.85f, 1f, 1f);

            for (int bi = 0; bi < items.Count; bi++)
            {
                var bar = EnsureBar(group, bi);
                UpdateBar(bar, items[bi], bi);
            }
            for (int bi = items.Count; bi < group.bars.Count; bi++)
                group.bars[bi].root.SetActive(false);

            float totalH = labelHeight + items.Count * rowHeight;
            group.rect.sizeDelta = new Vector2(infoWidth + barWidth + 8f, totalH);
        }

        for (int gi = activeGroupCount; gi < groupPool.Count; gi++)
            groupPool[gi].root.SetActive(false);
    }

    void UpdateBar(BarUI bar, StepEntry step, int rowIndex)
    {
        // Completed steps: brief flash then hide
        if (step.isDone)
        {
            long key = ((long)step.droneIndex << 32) | (uint)step.stepIndex;
            if (!completionTimes.TryGetValue(key, out float t0))
            {
                t0 = Time.time;
                completionTimes[key] = t0;
            }
            float age = Time.time - t0;
            if (age >= doneFlashDuration)
            {
                bar.root.SetActive(false);
                return;
            }
            // flash: brief white pulse that fades out
            float alpha = 1f - (age / doneFlashDuration);
            float flash = alpha * 0.6f;
            bar.root.SetActive(true);
            bar.rect.anchoredPosition = new Vector2(0, -(labelHeight + rowIndex * rowHeight));
            bar.fillImage.color = new Color(1f, 1f, 1f, flash);
            bar.bgImage.color = new Color(1f, 1f, 1f, flash * 0.3f);
            bar.infoText.text = "";
            bar.timeText.text = "";
            return;
        }

        bar.root.SetActive(true);
        bar.rect.anchoredPosition = new Vector2(0, -(labelHeight + rowIndex * rowHeight));

        bar.infoText.text = step.energyCost != 0 ? $"\u26A1{step.energyCost}" : "";

        float fillW = Mathf.Clamp01(step.progress) * (barWidth - 4f);
        bar.fillRect.offsetMax = new Vector2(2f + fillW, -2f);

        Color fillCol, bgCol, infoCol;
        if (step.isPreview)
        {
            fillCol = step.overBudget
                ? new Color(1f, 0.15f, 0.10f, 0.5f)
                : new Color(1f, 0.75f, 0f, 0.45f);
            bgCol = step.overBudget
                ? new Color(0.12f, 0.02f, 0.02f, 0.80f)
                : new Color(0.08f, 0.06f, 0.01f, 0.75f);
            infoCol = step.overBudget
                ? new Color(1f, 0.30f, 0.25f, 0.8f)
                : new Color(1f, 0.7f, 0.3f, 0.8f);
        }
        else if (step.isActive)
        {
            fillCol = new Color(0f, 0.85f, 1f, 0.9f);
            bgCol = new Color(0.02f, 0.04f, 0.08f, 0.88f);
            infoCol = new Color(0f, 0.85f, 1f, 1f);
        }
        else
        {
            fillCol = new Color(0.25f, 0.35f, 0.45f, 0.55f);
            bgCol = new Color(0.02f, 0.04f, 0.08f, 0.55f);
            infoCol = new Color(0.5f, 0.6f, 0.7f, 0.6f);
        }

        bar.fillImage.color = fillCol;
        bar.bgImage.color = bgCol;
        bar.infoText.color = infoCol;

        if (step.isActive)
            bar.timeText.text = $"{step.elapsed:F1}s/{step.total:F1}s";
        else
            bar.timeText.text = $"{step.total:F1}s";
        bar.timeText.color = Color.white;
    }

    // ── UI creation (lazy pool) ─────────────

    Font GetFont()
    {
        if (font != null) return font;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    GroupUI GetOrCreateGroup(int index)
    {
        while (groupPool.Count <= index)
            groupPool.Add(CreateGroup());
        return groupPool[index];
    }

    GroupUI CreateGroup()
    {
        var g = new GroupUI();
        g.root = new GameObject("OverlayGroup");
        g.root.transform.SetParent(canvas.transform, false);
        g.rect = g.root.AddComponent<RectTransform>();
        g.rect.pivot = new Vector2(0.5f, 1f);

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(g.root.transform, false);
        g.label = lblGO.AddComponent<Text>();
        g.label.font = GetFont();
        g.label.fontSize = 13;
        g.label.fontStyle = FontStyle.Bold;
        g.label.alignment = TextAnchor.MiddleCenter;
        g.label.horizontalOverflow = HorizontalWrapMode.Overflow;
        g.label.verticalOverflow = VerticalWrapMode.Overflow;
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0, 1);
        lblRT.anchorMax = new Vector2(1, 1);
        lblRT.pivot = new Vector2(0.5f, 1);
        lblRT.offsetMin = new Vector2(0, -labelHeight);
        lblRT.offsetMax = new Vector2(0, 0);

        g.bars = new List<BarUI>();
        return g;
    }

    BarUI EnsureBar(GroupUI group, int index)
    {
        while (group.bars.Count <= index)
            group.bars.Add(CreateBar(group.root.transform));
        return group.bars[index];
    }

    BarUI CreateBar(Transform parent)
    {
        var bar = new BarUI();

        var rowGO = new GameObject("BarRow");
        rowGO.transform.SetParent(parent, false);
        bar.root = rowGO;
        bar.rect = rowGO.AddComponent<RectTransform>();
        bar.rect.anchorMin = new Vector2(0, 1);
        bar.rect.anchorMax = new Vector2(1, 1);
        bar.rect.pivot = new Vector2(0, 1);
        bar.rect.sizeDelta = new Vector2(0, rowHeight);

        // Energy text (left side)
        var infoGO = new GameObject("Info");
        infoGO.transform.SetParent(rowGO.transform, false);
        bar.infoText = infoGO.AddComponent<Text>();
        bar.infoText.font = GetFont();
        bar.infoText.fontSize = 11;
        bar.infoText.fontStyle = FontStyle.Bold;
        bar.infoText.alignment = TextAnchor.MiddleRight;
        bar.infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var infoRT = infoGO.GetComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0, 0);
        infoRT.anchorMax = new Vector2(0, 1);
        infoRT.pivot = new Vector2(0, 0.5f);
        infoRT.offsetMin = new Vector2(0, 0);
        infoRT.offsetMax = new Vector2(infoWidth - 4f, 0);

        // Background bar
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(rowGO.transform, false);
        bar.bgImage = bgGO.AddComponent<Image>();
        bar.bgImage.color = new Color(0.02f, 0.04f, 0.08f, 0.88f);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(0, 1);
        bgRT.pivot = new Vector2(0, 0.5f);
        bgRT.offsetMin = new Vector2(infoWidth, 1f);
        bgRT.offsetMax = new Vector2(infoWidth + barWidth, -1f);

        // Fill bar
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bgGO.transform, false);
        bar.fillImage = fillGO.AddComponent<Image>();
        bar.fillImage.color = new Color(0f, 0.85f, 1f, 0.9f);
        bar.fillRect = fillGO.GetComponent<RectTransform>();
        bar.fillRect.anchorMin = new Vector2(0, 0);
        bar.fillRect.anchorMax = new Vector2(0, 1);
        bar.fillRect.pivot = new Vector2(0, 0.5f);
        bar.fillRect.offsetMin = new Vector2(2f, 2f);
        bar.fillRect.offsetMax = new Vector2(2f, -2f);

        // Time text (inside bar)
        var timeGO = new GameObject("Time");
        timeGO.transform.SetParent(bgGO.transform, false);
        bar.timeText = timeGO.AddComponent<Text>();
        bar.timeText.font = GetFont();
        bar.timeText.fontSize = 10;
        bar.timeText.fontStyle = FontStyle.Bold;
        bar.timeText.alignment = TextAnchor.MiddleCenter;
        bar.timeText.color = Color.white;
        bar.timeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var timeRT = timeGO.GetComponent<RectTransform>();
        timeRT.anchorMin = Vector2.zero;
        timeRT.anchorMax = Vector2.one;
        timeRT.offsetMin = Vector2.zero;
        timeRT.offsetMax = Vector2.zero;

        bgGO.AddComponent<Outline>().effectColor = new Color(0f, 0.6f, 0.9f, 0.3f);

        return bar;
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
