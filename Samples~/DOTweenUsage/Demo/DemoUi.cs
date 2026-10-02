using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Builds the demo's uGUI in code. Plumbing only; nothing here is about Janitor.</summary>
    internal static class DemoUi
    {
        internal static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        internal static readonly Vector2 TopRight = new Vector2(1f, 1f);
        internal static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private static readonly Color ButtonColor = new Color(0.22f, 0.42f, 0.72f, 1f);
        private static readonly Color CardColor = new Color(0.1f, 0.12f, 0.16f, 0.92f);

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

        // A container whose layout group positions its children in rows.
        internal static RectTransform CreateGrid(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2 cell, Vector2 spacing)
        {
            var rect = CreateRect(parent, name);
            Place(rect, anchor, position, size);

            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            return rect;
        }

        // A titled panel; the demo places its content with Center anchors.
        internal static RectTransform CreateCard(Transform parent, string title)
        {
            var rect = CreateRect(parent, title + "Card");
            var image = rect.gameObject.AddComponent<Image>();
            image.color = CardColor;
            image.raycastTarget = false;

            var label = CreateLabel(rect, "Title", title, 26f, TopLeft, new Vector2(16f, -10f), new Vector2(300f, 36f));
            label.alignment = TextAlignmentOptions.Left;
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
    }
}
