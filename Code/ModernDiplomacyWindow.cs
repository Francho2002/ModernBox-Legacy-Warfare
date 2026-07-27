using System;
using NCMS.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ModernBox
{
    /// <summary>
    /// Manual, paginated diplomacy report. It never refreshes per frame.
    /// The presentation intentionally differs from military intelligence: this
    /// is a relationship dashboard rather than a production report.
    /// </summary>
    internal static class ModernDiplomacyWindow
    {
        private const string WindowId = "ModernDiplomacyWindow";
        private const float WindowWidth = 560f;
        private const float WindowHeight = 510f;
        private const float FooterHeight = 36f;
        private const float FooterBottomInset = 44f;

        private static readonly Color AccentColor = new Color(0.70f, 0.58f, 0.96f, 1f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.17f, 0.39f, 0.96f);

        private static ScrollWindow window;
        private static Text report;
        private static Text pageLabel;
        private static Button previous;
        private static Button next;
        private static RectTransform contentRect;
        private static RectTransform viewportRect;
        private static ScrollRect reportScroll;
        private static int page;
        private static bool initialized;

        internal static void init()
        {
            if (initialized)
                return;

            try
            {
                if (window == null)
                    window = Windows.CreateNewWindow(WindowId, "Centro diplomático");
                if (window == null)
                    return;

                Transform background = window.transform.Find("Background");
                Transform scroll = background?.Find("Scroll View");
                Transform viewport = scroll?.Find("Viewport");
                Transform content = viewport?.Find("Content");
                if (background == null || scroll == null || viewport == null || content == null)
                    return;

                scroll.gameObject.SetActive(true);
                ConfigureWindowGeometry(background, scroll, viewport, content);
                contentRect = content.GetComponent<RectTransform>();
                ConfigureTitle(background);
                CreateReport(content);

                Transform footer = CreateFooter(background);
                pageLabel = CreatePageLabel(footer);
                previous = CreateButton(footer, "ModernDiplomacyPrevious", "Anterior", 78f, Previous);
                next = CreateButton(footer, "ModernDiplomacyNext", "Siguiente", 78f, Next);
                CreateButton(footer, "ModernDiplomacyRefresh", "Actualizar", 94f, Refresh);

                initialized = true;
                Refresh();
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.Diplomacy] UI: " + ex.Message);
            }
        }

        internal static void Show()
        {
            init();
            Refresh();
            if (window != null)
                Windows.ShowWindow(WindowId);
        }

        private static void ConfigureTitle(Transform background)
        {
            Text title = background.Find("Title")?.GetComponent<Text>() ??
                background.Find("Name")?.GetComponent<Text>();
            if (title == null)
                return;

            title.text = "Centro diplomático";
            title.color = AccentColor;
            title.fontSize = 15;
            title.alignment = TextAnchor.MiddleCenter;
            title.supportRichText = true;
        }

        private static void CreateReport(Transform content)
        {
            GameObject textObject = new GameObject("ModernDiplomacyReport");
            textObject.transform.SetParent(content, false);

            report = textObject.AddComponent<Text>();
            report.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            report.fontSize = 11;
            report.lineSpacing = 1.08f;
            report.alignment = TextAnchor.UpperLeft;
            report.color = new Color(0.95f, 0.93f, 1f, 1f);
            report.supportRichText = true;
            report.raycastTarget = false;
            report.horizontalOverflow = HorizontalWrapMode.Wrap;
            report.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rect = report.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(-16f, 360f);
        }

        private static void Previous()
        {
            if (page <= 0)
                return;

            page--;
            Refresh();
        }

        private static void Next()
        {
            if (page + 1 >= ModernDiplomacyController.PageCount)
                return;

            page++;
            Refresh();
        }

        private static void Refresh()
        {
            if (!initialized || report == null)
                return;

            int count = ModernDiplomacyController.PageCount;
            const string introduction =
                "<color=#C7B5FF><b>CENTRO DIPLOMÁTICO — SISTEMA AUTOMÁTICO</b></color>\n" +
                "<color=#B7C9DD>No tienes que activar cada pacto manualmente. Los reinos negocian según su poder, opinión y situación; este panel permite consultar los acuerdos y sus efectos.</color>\n\n";
            if (count <= 0)
            {
                page = 0;
                report.text = introduction +
                    "<color=#FFCC77>No hay civilizaciones activas. Crea o carga reinos y vuelve a pulsar Actualizar.</color>";
                if (pageLabel != null)
                    pageLabel.text = "Sin civilizaciones";
                if (previous != null)
                    previous.interactable = false;
                if (next != null)
                    next.interactable = false;
                ResizeContent();
                return;
            }

            page = Mathf.Clamp(page, 0, count - 1);
            report.text = introduction + ModernDiplomacyController.BuildReport(page);

            if (pageLabel != null)
                pageLabel.text = "Reino " + (page + 1) + " de " + count + " · automático";
            if (previous != null)
                previous.interactable = page > 0;
            if (next != null)
                next.interactable = page + 1 < count;

            ResizeContent();
        }

        private static void ResizeContent()
        {
            if (report == null || contentRect == null)
                return;

            RectTransform reportRect = report.GetComponent<RectTransform>();
            if (reportRect == null)
                return;

            float viewportHeight = viewportRect == null ? 350f : Mathf.Max(250f, viewportRect.rect.height);
            float height = Mathf.Max(viewportHeight, report.preferredHeight + 24f);
            reportRect.sizeDelta = new Vector2(-16f, height);
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, height + 12f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            if (reportScroll != null)
                reportScroll.verticalNormalizedPosition = 1f;
        }

        private static Transform CreateFooter(Transform background)
        {
            GameObject footer = new GameObject("ModernDiplomacyFooter");
            footer.transform.SetParent(background, false);

            Image image = footer.AddComponent<Image>();
            image.color = new Color(0.12f, 0.08f, 0.20f, 0.98f);

            RectTransform rect = footer.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 8f);
            rect.sizeDelta = new Vector2(-30f, FooterHeight);

            HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 5, 5);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return footer.transform;
        }

        private static Text CreatePageLabel(Transform parent)
        {
            GameObject obj = new GameObject("ModernDiplomacyPage");
            obj.transform.SetParent(parent, false);

            Text label = obj.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 9;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.91f, 0.88f, 1f, 1f);

            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.minWidth = 80f;
            layout.flexibleWidth = 1f;
            return label;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string text,
            float width,
            UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            Image image = obj.AddComponent<Image>();
            image.color = ButtonColor;

            Button button = obj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.94f, 1f, 1f);
            colors.pressedColor = new Color(0.80f, 0.70f, 1f, 1f);
            colors.disabledColor = new Color(0.52f, 0.48f, 0.58f, 0.65f);
            button.colors = colors;
            button.onClick.AddListener(action);

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, 25f);
            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = 25f;

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(obj.transform, false);
            Text label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = text;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void ConfigureWindowGeometry(
            Transform background,
            Transform scrollTransform,
            Transform viewport,
            Transform content)
        {
            RectTransform windowRect = window.GetComponent<RectTransform>();
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            RectTransform scrollRect = scrollTransform.GetComponent<RectTransform>();
            viewportRect = viewport.GetComponent<RectTransform>();
            RectTransform innerContentRect = content.GetComponent<RectTransform>();

            if (windowRect != null)
                windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            if (backgroundRect != null)
                backgroundRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            if (scrollRect != null)
            {
                scrollRect.anchorMin = Vector2.zero;
                scrollRect.anchorMax = Vector2.one;
                scrollRect.pivot = new Vector2(0.5f, 0.5f);
                scrollRect.offsetMin = new Vector2(14f, FooterBottomInset + 4f);
                scrollRect.offsetMax = new Vector2(-14f, -40f);
            }

            reportScroll = scrollTransform.GetComponent<ScrollRect>();
            if (reportScroll != null)
            {
                reportScroll.horizontal = false;
                reportScroll.vertical = true;
            }

            if (viewportRect != null)
            {
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.pivot = new Vector2(0.5f, 0.5f);
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = new Vector2(
                    reportScroll != null && reportScroll.verticalScrollbar != null ? -14f : 0f,
                    0f);
            }

            if (innerContentRect != null)
            {
                innerContentRect.anchorMin = new Vector2(0f, 1f);
                innerContentRect.anchorMax = new Vector2(1f, 1f);
                innerContentRect.pivot = new Vector2(0.5f, 1f);
                innerContentRect.anchoredPosition = Vector2.zero;
            }
            if (backgroundRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(backgroundRect);
        }
    }
}
