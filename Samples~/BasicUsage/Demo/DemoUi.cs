using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Builds the demo's uGUI in code. Plumbing only; nothing here is about Janitor.</summary>
    internal static class DemoUi
    {
        internal static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        internal static readonly Vector2 TopRight = new Vector2(1f, 1f);
        internal static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private static readonly Color ButtonColor = new Color(0.22f, 0.42f, 0.72f, 1f);

        // True when the Game view is taller than wide; the demo then uses its portrait positions.
        internal static bool Portrait { get; private set; }

        internal static Vector2 Pick(Vector2 landscape, Vector2 portrait) => Portrait ? portrait : landscape;

        // Returns a fixed-size stage centered on the screen, so the layout fits any aspect ratio.
        internal static RectTransform CreateCanvas()
        {
            Portrait = Screen.height > Screen.width;
            var stageSize = Portrait ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);

            var go = new GameObject("DemoCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // Expand never makes the canvas smaller than the stage, whatever the screen's shape.
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = stageSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var stage = CreateRect(go.transform, "Stage");
            Place(stage, Center, Vector2.zero, stageSize);
            return stage;
        }

        internal static RectTransform CreateRect(Transform parent, string name, bool active = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(active);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // The pivot sits on the anchor, so the position is an offset from that point.
        internal static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static Image CreateImage(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = CreateRect(parent, name);
            Place(rect, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static TMP_Text CreateLabel(Transform parent, string name, string text, float fontSize, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = CreateRect(parent, name);
            Place(rect, anchor, position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        // Two columns of buttons at the left edge, below the HUD in portrait; the grid positions its children.
        internal static RectTransform CreateButtonGrid(Transform parent)
        {
            var rect = CreateRect(parent, "Buttons");
            Place(rect, TopLeft, Pick(new Vector2(30f, -30f), new Vector2(30f, -420f)), new Vector2(790f, 1000f));

            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(380f, 56f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            return rect;
        }

        internal static Button CreateButton(Transform parent, string text)
        {
            var rect = CreateRect(parent, text);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var label = CreateLabel(rect, "Text", text, 26f, Center, Vector2.zero, Vector2.zero);
            Stretch(label.rectTransform);
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 26f;
            return button;
        }

        internal static void SetText(Button button, string text)
        {
            button.GetComponentInChildren<TMP_Text>().text = text;
        }
    }
}
