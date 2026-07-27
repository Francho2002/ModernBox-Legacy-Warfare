using System;
using NCMS.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ModernBox
{
    /// <summary>Manual, paginated diplomacy report; it never refreshes per frame.</summary>
    internal static class ModernDiplomacyWindow
    {
        private const string WindowId = "ModernDiplomacyWindow";
        private static ScrollWindow window;
        private static Text report;
        private static Text pageLabel;
        private static Button previous;
        private static Button next;
        private static RectTransform contentRect;
        private static int page;
        private static bool initialized;

        internal static void init()
        {
            if (initialized) return;
            try
            {
                window = Windows.CreateNewWindow(WindowId, "ModernBox");
                Transform background = window?.transform.Find("Background");
                Transform scroll = background?.Find("Scroll View");
                Transform content = scroll?.Find("Viewport")?.Find("Content");
                if (background == null || content == null) return;
                RectTransform windowRect = window.GetComponent<RectTransform>();
                if (windowRect != null)
                    windowRect.sizeDelta = new Vector2(560f, 510f);
                scroll.gameObject.SetActive(true);
                contentRect = content.GetComponent<RectTransform>();
                Text title = background.Find("Name")?.GetComponent<Text>();
                if (title != null)
                {
                    title.text = "Diplomacia moderna";
                    title.color = new Color(0.94f, 0.84f, 0.55f, 1f);
                }
                GameObject textObject = new GameObject("ModernDiplomacyReport");
                textObject.transform.SetParent(content, false);
                report = textObject.AddComponent<Text>();
                report.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); report.fontSize = 11;
                report.alignment = TextAnchor.UpperLeft; report.supportRichText = true;
                report.horizontalOverflow = HorizontalWrapMode.Wrap; report.verticalOverflow = VerticalWrapMode.Overflow;
                RectTransform rect = report.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(8, -8); rect.sizeDelta = new Vector2(-16, 460);
                previous = CreateButton(background, "ModernDiplomacyPrevious", "Anterior", new Vector2(-225, 11), Previous);
                next = CreateButton(background, "ModernDiplomacyNext", "Siguiente", new Vector2(-120, 11), Next);
                pageLabel = Label(background, new Vector2(18, 12));
                CreateButton(background, "ModernDiplomacyRefresh", "Actualizar", new Vector2(-18, 11), Refresh);
                initialized = true; Refresh();
            }
            catch (Exception ex) { ModernBoxLogger.Error("[MX.Diplomacy] UI: " + ex.Message); }
        }

        internal static void Show() { init(); Refresh(); if (window != null) Windows.ShowWindow(WindowId); }
        private static void Previous() { if (page > 0) { page--; Refresh(); } }
        private static void Next() { if (page + 1 < ModernDiplomacyController.PageCount) { page++; Refresh(); } }
        private static void Refresh()
        {
            if (!initialized || report == null) return;
            int count = ModernDiplomacyController.PageCount; page = Mathf.Clamp(page, 0, count - 1);
            report.text = ModernDiplomacyController.BuildReport(page);
            pageLabel.text = "Página " + (page + 1) + " de " + count;
            previous.interactable = page > 0; next.interactable = page + 1 < count;
            RectTransform reportRect = report.GetComponent<RectTransform>();
            float height = Mathf.Max(460f, report.preferredHeight + 24f);
            if (reportRect != null)
                reportRect.sizeDelta = new Vector2(-16f, height);
            if (contentRect != null)
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, height + 12f);
        }
        private static Text Label(Transform parent, Vector2 position)
        {
            GameObject obj = new GameObject("ModernDiplomacyPage"); obj.transform.SetParent(parent, false);
            Text label = obj.AddComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); label.fontSize = 10; label.alignment = TextAnchor.MiddleCenter;
            RectTransform rect = label.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(180, 23);
            return label;
        }
        private static Button CreateButton(Transform parent, string name, string text, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject(name); obj.transform.SetParent(parent, false);
            Image image = obj.AddComponent<Image>(); image.color = new Color(.20f, .34f, .45f, .95f);
            Button button = obj.AddComponent<Button>(); button.onClick.AddListener(action);
            RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(84, 25);
            GameObject labelObject = new GameObject("Label"); labelObject.transform.SetParent(obj.transform, false);
            Text label = labelObject.AddComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); label.fontSize = 10; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.text = text;
            RectTransform labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            return button;
        }
    }
}
