using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lower-left panel showing a description of whatever the player is hovering over.
/// </summary>
public class HoverInfoPanel : MonoBehaviour
{
    Text label;
    CanvasGroup canvasGroup;
    string currentText = "";

    public void Init()
    {
        var canvasGO = new GameObject("HoverInfoCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var panelGO = new GameObject("HoverPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        var rt = panelGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(12f, 12f);
        rt.sizeDelta = new Vector2(500f, 50f);

        canvasGroup = panelGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var textGO = new GameObject("Label");
        textGO.transform.SetParent(panelGO.transform, false);
        label = textGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 14;
        label.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        label.alignment = TextAnchor.LowerLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;

        var shadow = textGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1f, -1f);

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
    }

    public void SetDescription(string text)
    {
        if (text == currentText) return;
        currentText = text;

        if (string.IsNullOrEmpty(text))
        {
            canvasGroup.alpha = 0f;
            label.text = "";
        }
        else
        {
            canvasGroup.alpha = 1f;
            label.text = text;
        }
    }
}
