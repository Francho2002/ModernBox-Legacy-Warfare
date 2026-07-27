using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NCMS.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ModernBox
{
    /// <summary>
    /// Read-only intelligence report.  It deliberately refreshes only when the
    /// player opens the window or presses its button; it never polls the world
    /// every frame.
    /// </summary>
    internal static class MilitaryStatusWindow
    {
        private const string WindowId = "MilitaryStatusWindow";
        private const float WindowWidth = 560f;
        private const float WindowHeight = 510f;
        // The scroll template fills almost all of a stock WorldBox window. A
        // dedicated footer keeps the report from ever drawing behind controls.
        private const float FooterHeight = 36f;
        private const float FooterBottomInset = 44f;
        // A city report has substantial production detail. Keeping one city on a
        // page prevents Unity's Text mesh from exceeding its vertex limit while
        // still making every city available through the page controls.
        private const int MaximumUnitGroupsPerCity = 10;
        private const int MaximumCandidatesPerLine = 8;
        private const int MaximumDockLinesPerCity = 4;

        private static ScrollWindow window;
        private static GameObject content;
        private static Text reportText;
        private static Text pageLabel;
        private static RectTransform viewportRect;
        private static ScrollRect reportScroll;
        private static Button previousPageButton;
        private static Button nextPageButton;
        private static int currentPage;
        private static int pageCount;
        private static bool initialized;

        private sealed class ProductionCandidate
        {
            internal string id;
            internal string tier;
            internal string role;
            internal ConstructionCost cost;
            internal string costLabel;
        }

        private sealed class ReportPage
        {
            internal Kingdom kingdom;
            internal City city;
            internal int cityNumber;
            internal int cityCount;
        }

        internal static void init()
        {
            if (initialized)
                return;

            try
            {
                if (window == null)
                    window = Windows.CreateNewWindow(WindowId, "Estado militar");
                if (window == null)
                {
                    ModernBoxLogger.Warning("[MX.Intel] No se pudo crear la ventana de estado militar.");
                    return;
                }

                Transform background = window.transform.Find("Background");
                Transform scrollTransform = background?.Find("Scroll View");
                Transform viewport = scrollTransform?.Find("Viewport");
                Transform contentTransform = viewport?.Find("Content");
                if (background == null || scrollTransform == null || viewport == null || contentTransform == null)
                {
                    ModernBoxLogger.Warning("[MX.Intel] La plantilla de ventana de WorldBox no tiene la estructura esperada.");
                    return;
                }

                GameObject scrollObject = scrollTransform.gameObject;
                scrollObject.SetActive(true);
                ConfigureWindowGeometry(background, scrollTransform, viewport, contentTransform);

                content = contentTransform.gameObject;
                ConfigureTitle(background);
                CreateReportText(contentTransform);
                Transform footer = CreateFooter(background);
                CreatePageLabel(footer);
                previousPageButton = CreateControlButton(footer, "MilitaryStatusPrevious", "Anterior", 78f, PreviousPage);
                nextPageButton = CreateControlButton(footer, "MilitaryStatusNext", "Siguiente", 78f, NextPage);
                CreateRefreshButton(footer);
                initialized = true;
                Refresh();
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.Intel] Error al inicializar el estado militar: " + ex.Message);
            }
        }

        internal static void Show()
        {
            init();
            Refresh();
            if (window != null)
                Windows.ShowWindow(WindowId);
        }

        internal static void Refresh()
        {
            if (!initialized || reportText == null)
                return;

            try
            {
                reportText.text = BuildReport();
                UpdatePageControls();
                ResizeContent();
            }
            catch (Exception ex)
            {
                reportText.text = "<color=#FF7777>No se pudo actualizar el informe militar.</color>\n" +
                    Escape(ex.Message);
                pageCount = 0;
                UpdatePageControls();
                if (pageLabel != null)
                    pageLabel.text = "Error del informe";
                ResizeContent();
                ModernBoxLogger.Error("[MX.Intel] Error al actualizar estado militar: " + ex);
            }
        }

        private static void ConfigureTitle(Transform background)
        {
            Text titleText = background.Find("Title")?.GetComponent<Text>() ??
                background.Find("Name")?.GetComponent<Text>();
            if (titleText == null)
                return;

            titleText.text = "Estado militar";
            titleText.color = new Color(0.94f, 0.84f, 0.55f, 1f);
            titleText.fontSize = 15;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.supportRichText = true;
        }

        private static void CreateReportText(Transform contentTransform)
        {
            GameObject reportObject = new GameObject("MilitaryStatusReport");
            reportObject.transform.SetParent(contentTransform, false);

            reportText = reportObject.AddComponent<Text>();
            reportText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            reportText.fontSize = 11;
            reportText.lineSpacing = 1.08f;
            reportText.alignment = TextAnchor.UpperLeft;
            reportText.color = new Color(0.95f, 0.95f, 0.90f, 1f);
            reportText.supportRichText = true;
            reportText.raycastTarget = false;
            reportText.horizontalOverflow = HorizontalWrapMode.Wrap;
            reportText.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform reportRect = reportObject.GetComponent<RectTransform>();
            reportRect.anchorMin = new Vector2(0f, 1f);
            reportRect.anchorMax = new Vector2(1f, 1f);
            reportRect.pivot = new Vector2(0.5f, 1f);
            reportRect.anchoredPosition = new Vector2(9f, -8f);
            reportRect.sizeDelta = new Vector2(-18f, 360f);
        }

        private static void CreateRefreshButton(Transform background)
        {
            GameObject buttonObject = new GameObject("MilitaryStatusRefresh");
            buttonObject.transform.SetParent(background, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.34f, 0.45f, 0.95f);
            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.78f, 1f);
            colors.pressedColor = new Color(0.72f, 0.88f, 1f, 1f);
            button.colors = colors;
            button.onClick.AddListener(Refresh);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(94f, 25f);
            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 94f;
            layout.minHeight = layout.preferredHeight = 25f;

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            Text label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 11;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "Actualizar";

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private static void CreatePageLabel(Transform background)
        {
            GameObject labelObject = new GameObject("MilitaryStatusPageLabel");
            labelObject.transform.SetParent(background, false);
            pageLabel = labelObject.AddComponent<Text>();
            pageLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            pageLabel.fontSize = 10;
            pageLabel.alignment = TextAnchor.MiddleCenter;
            pageLabel.color = new Color(0.87f, 0.91f, 0.95f, 1f);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            LayoutElement layout = labelObject.AddComponent<LayoutElement>();
            layout.minWidth = 80f;
            layout.flexibleWidth = 1f;
        }

        private static Button CreateControlButton(Transform background, string name, string labelText, float width, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(background, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.34f, 0.45f, 0.95f);
            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.78f, 1f);
            colors.pressedColor = new Color(0.72f, 0.88f, 1f, 1f);
            colors.disabledColor = new Color(0.52f, 0.52f, 0.52f, 0.65f);
            button.colors = colors;
            button.onClick.AddListener(action);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(width, 25f);
            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = 25f;

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            Text label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = labelText;

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        private static Transform CreateFooter(Transform background)
        {
            GameObject footerObject = new GameObject("MilitaryStatusFooter");
            footerObject.transform.SetParent(background, false);

            Image footerImage = footerObject.AddComponent<Image>();
            footerImage.color = new Color(0.08f, 0.14f, 0.19f, 0.97f);

            RectTransform footerRect = footerObject.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 8f);
            footerRect.sizeDelta = new Vector2(-30f, FooterHeight);

            HorizontalLayoutGroup layout = footerObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 5, 5);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return footerObject.transform;
        }

        private static void ConfigureWindowGeometry(
            Transform background,
            Transform scrollTransform,
            Transform viewport,
            Transform contentTransform)
        {
            RectTransform windowRect = window.GetComponent<RectTransform>();
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            RectTransform scrollRect = scrollTransform.GetComponent<RectTransform>();
            viewportRect = viewport.GetComponent<RectTransform>();
            RectTransform contentRect = contentTransform.GetComponent<RectTransform>();

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

            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
            }
            if (backgroundRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(backgroundRect);
        }

        private static void PreviousPage()
        {
            if (currentPage <= 0)
                return;

            currentPage--;
            Refresh();
        }

        private static void NextPage()
        {
            if (currentPage >= pageCount - 1)
                return;

            currentPage++;
            Refresh();
        }

        private static void UpdatePageControls()
        {
            if (pageLabel != null)
                pageLabel.text = pageCount > 0
                    ? "Página " + (currentPage + 1) + " de " + pageCount
                    : "Sin civilizaciones";
            if (previousPageButton != null)
                previousPageButton.interactable = currentPage > 0;
            if (nextPageButton != null)
                nextPageButton.interactable = currentPage + 1 < pageCount;
        }

        private static void ResizeContent()
        {
            if (content == null || reportText == null)
                return;

            RectTransform contentRect = content.GetComponent<RectTransform>();
            RectTransform reportRect = reportText.GetComponent<RectTransform>();
            if (contentRect == null || reportRect == null)
                return;

            float viewportHeight = viewportRect == null ? 350f : Mathf.Max(250f, viewportRect.rect.height);
            float height = Mathf.Max(viewportHeight, reportText.preferredHeight + 20f);
            reportRect.sizeDelta = new Vector2(-18f, height);
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, height + 12f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            if (reportScroll != null)
                reportScroll.verticalNormalizedPosition = 1f;
        }

        private static string BuildReport()
        {
            StringBuilder result = new StringBuilder();
            result.AppendLine("<color=#F5D66D><b>Informe de inteligencia militar</b></color>");
            result.AppendLine("Actualización manual. El informe no consume recursos ni modifica a los reinos.");
            result.AppendLine("<color=#AFC7D6>La disponibilidad indica los requisitos actuales; la IA sigue construyendo en ciclos, no de inmediato.</color>");
            result.AppendLine();

            if (World.world == null || World.world.kingdoms == null)
            {
                pageCount = 0;
                result.AppendLine("<color=#FFCC77>No hay un mundo de civilizaciones activo todavía.</color>");
                return result.ToString();
            }

            List<ReportPage> pages = GetReportPages();
            pageCount = pages.Count;
            if (pageCount == 0)
            {
                result.AppendLine("<color=#FFCC77>No hay civilizaciones activas para informar.</color>");
                return result.ToString();
            }

            currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
            ReportPage page = pages[currentPage];
            result.AppendLine("<color=#AFC7D6>Página " + (currentPage + 1) + " de " + pageCount +
                " - ciudad " + page.cityNumber + " de " + page.cityCount + " del reino seleccionado.</color>");
            result.AppendLine();
            AppendKingdomReport(result, page);

            return result.ToString();
        }

        private static List<ReportPage> GetReportPages()
        {
            List<Kingdom> kingdoms = new List<Kingdom>();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom != null && kingdom.isCiv())
                    kingdoms.Add(kingdom);
            }

            List<ReportPage> pages = new List<ReportPage>();
            foreach (Kingdom kingdom in kingdoms.OrderBy(k => SafeName(k.name, "Reino sin nombre")))
            {
                List<City> cities = kingdom.cities == null
                    ? new List<City>()
                    : kingdom.cities.Where(city => city != null && city.isAlive())
                        .OrderBy(city => SafeName(GetCityName(city), "Ciudad sin nombre"))
                        .ToList();

                if (cities.Count == 0)
                {
                    pages.Add(new ReportPage { kingdom = kingdom, cityNumber = 0, cityCount = 0 });
                    continue;
                }

                for (int index = 0; index < cities.Count; index++)
                {
                    pages.Add(new ReportPage
                    {
                        kingdom = kingdom,
                        city = cities[index],
                        cityNumber = index + 1,
                        cityCount = cities.Count
                    });
                }
            }
            return pages;
        }

        private static void AppendKingdomReport(StringBuilder result, ReportPage page)
        {
            Kingdom kingdom = page.kingdom;
            List<City> cities = kingdom.cities == null
                ? new List<City>()
                : kingdom.cities.Where(city => city != null && city.isAlive()).ToList();

            result.AppendLine("<color=#80D6FF><b>REINO: " + Escape(SafeName(kingdom.name, "Sin nombre")) + "</b></color>");
            result.AppendLine("Doctrina: <color=#FFE08A>" + Escape(GetDoctrineLabel(kingdom)) + "</color>  |  Ciudades: " + cities.Count);
            result.AppendLine("Inventario vinculado a ciudades: " + DescribeKingdomInventory(cities));

            if (cities.Count == 0)
            {
                result.AppendLine("  <color=#FFCC77>Sin ciudades vivas: no puede fabricar unidades de ciudad.</color>");
                result.AppendLine();
                return;
            }

            result.AppendLine("  <color=#BBBBBB>Mostrando ciudad " + page.cityNumber + " de " + page.cityCount +
                ". Usa Anterior/Siguiente para revisar todas las ciudades y reinos.</color>");
            AppendCityReport(result, page.city);

            result.AppendLine();
        }

        private static void AppendCityReport(StringBuilder result, City city)
        {
            int population = SafePopulation(city);
            string progression = GetProgressionLabel(city);
            string cityName = SafeName(GetCityName(city), "Ciudad sin nombre");
            result.AppendLine("  <color=#FFFFFF><b>" + Escape(cityName) + "</b></color> — población " + population + " | progreso: " + Escape(progression));

            AppendOwnedUnits(result, city);
            AppendLandProduction(result, city, population);
            AppendLauncherStatus(result, city, population);
            AppendNavalProduction(result, city);
        }

        private static void AppendOwnedUnits(StringBuilder result, City city)
        {
            Dictionary<string, int> groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (city.units != null)
            {
                foreach (Actor unit in city.units)
                {
                    if (unit == null || !unit.isAlive() || string.IsNullOrEmpty(unit.asset?.id))
                        continue;

                    string group = GetActorCategory(unit.asset.id) + ": " + unit.asset.id;
                    groups[group] = groups.TryGetValue(group, out int count) ? count + 1 : 1;
                }
            }

            if (groups.Count == 0)
            {
                result.AppendLine("    Posee: <color=#BBBBBB>sin unidades registradas</color>");
                return;
            }

            result.AppendLine("    Posee:");
            List<KeyValuePair<string, int>> orderedGroups = groups.OrderBy(pair => pair.Key).ToList();
            int shownGroups = Math.Min(MaximumUnitGroupsPerCity, orderedGroups.Count);
            for (int index = 0; index < shownGroups; index++)
            {
                KeyValuePair<string, int> group = orderedGroups[index];
                result.AppendLine("      • " + Escape(group.Key) + " ×" + group.Value);
            }
            if (orderedGroups.Count > shownGroups)
            {
                int remainingUnits = orderedGroups.Skip(shownGroups).Sum(group => group.Value);
                result.AppendLine("      <color=#BBBBBB>+ " + (orderedGroups.Count - shownGroups) +
                    " tipos más (" + remainingUnits + " unidades).</color>");
            }
        }

        private static void AppendLandProduction(StringBuilder result, City city, int population)
        {
            int currentLand = CountLandMilitary(city);
            int landCap = MilitaryQuotaService.GetLandUnitCap(city);
            int currentArtillery = CountArtillery(city);
            int artilleryCap = MilitaryQuotaService.GetArtilleryCap(city);

            string gate = GetLandGate(city, population, currentLand, landCap);
            result.AppendLine("    Tierra: " + gate + " | capacidad terrestre " + currentLand + "/" + landCap +
                ", artillería convencional " + currentArtillery + "/" + artilleryCap + ".");
            result.AppendLine("      Perfil de ciudad: " + MilitaryQuotaService.GetCityQuotaLabel(city) + ".");

            List<ProductionCandidate> candidates = GetLandCandidates(city);
            if (candidates.Count == 0)
            {
                result.AppendLine("      Catálogo terrestre: <color=#FFCC77>no hay activos válidos para la especie o faltan assets.</color>");
                return;
            }

            List<ProductionCandidate> unlocked = candidates
                .Where(candidate => MilitaryProgressionController.IsRoleUnlocked(city, candidate.tier, candidate.role, candidate.id))
                .ToList();
            List<ProductionCandidate> progressionBlocked = candidates
                .Where(candidate => !MilitaryProgressionController.IsRoleUnlocked(city, candidate.tier, candidate.role, candidate.id))
                .ToList();
            List<ProductionCandidate> artilleryBlocked = unlocked
                .Where(candidate => ModernCapPolicy.IsConventionalArtillery(candidate.id) &&
                    currentArtillery >= artilleryCap)
                .ToList();
            List<ProductionCandidate> productionEligible = unlocked
                .Where(candidate => !ModernCapPolicy.IsConventionalArtillery(candidate.id) ||
                    currentArtillery < artilleryCap)
                .ToList();
            List<ProductionCandidate> affordable = productionEligible
                .Where(candidate => city.hasEnoughResourcesFor(candidate.cost))
                .ToList();

            result.AppendLine("      Catálogo desbloqueado (cuando WorldBox cree una unidad-base):");
            foreach (IGrouping<string, ProductionCandidate> group in unlocked
                .GroupBy(candidate => candidate.tier + " / " + candidate.role)
                .OrderBy(group => group.Key))
            {
                result.AppendLine("        " + Escape(group.Key) + ": " + DescribeCandidates(group));
            }

            if (unlocked.Count == 0)
                result.AppendLine("        <color=#FFCC77>Ninguno: la ciudad aún no alcanzó el nivel militar requerido.</color>");
            if (progressionBlocked.Count > 0)
                result.AppendLine("      Bloqueado por progreso militar: " + DescribeCandidates(progressionBlocked) + ".");
            if (artilleryBlocked.Count > 0)
                result.AppendLine("      Bloqueado por cupo de artillería: " + DescribeCandidates(artilleryBlocked) + ".");

            if (!Traits.vehiclesAllowed)
            {
                result.AppendLine("      Bloqueo: la opción <b>Permitir vehículos</b> está desactivada.");
            }
            else if (currentLand >= landCap &&
                !(MilitaryProgressionController.GetLevel(city) >= 3 && currentArtillery == 0))
            {
                result.AppendLine("      Bloqueo: la capacidad terrestre de esta ciudad está completa.");
            }
            else if (unlocked.Count == 0)
            {
                result.AppendLine("      Bloqueo: faltan progreso militar e infraestructura para desbloquear el catálogo.");
            }
            else if (productionEligible.Count == 0)
            {
                result.AppendLine("      Bloqueo: el cupo de artillería de esta ciudad está completo.");
            }
            else if (affordable.Count == 0)
            {
                result.AppendLine("      Bloqueo: faltan recursos para todos los candidatos del catálogo actual.");
            }
            else
            {
                result.AppendLine("      Con recursos ahora: " + DescribeCandidates(affordable) + ".");
            }
        }

        private static void AppendLauncherStatus(StringBuilder result, City city, int population)
        {
            ProductionCandidate launcher = GetLandCandidates(city)
                .FirstOrDefault(candidate => candidate.id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase));

            if (HasMissileLauncher(city))
            {
                result.AppendLine("    Lanzamisiles terrestre: <color=#8FF0A4>ya posee uno</color> (máximo actual: 1 por ciudad).");
                return;
            }

            if (launcher == null)
            {
                result.AppendLine("    Lanzamisiles terrestre: <color=#FFCC77>no hay plataforma válida registrada para su especie.</color>");
                return;
            }

            if (!Traits.vehiclesAllowed)
            {
                result.AppendLine("    Lanzamisiles terrestre: bloqueado — vehículos desactivados.");
            }
            else if (population < 75)
            {
                result.AppendLine("    Lanzamisiles terrestre: bloqueado — requiere 75 habitantes (actual: " + population + ").");
            }
            else if (!MilitaryProgressionController.CanBuildDefensiveLauncher(city))
            {
                result.AppendLine("    Lanzamisiles terrestre: bloqueado — requiere nivel militar 3 e infraestructura pesada.");
            }
            else if (!city.hasLeader())
            {
                result.AppendLine("    Lanzamisiles terrestre: bloqueado — la ciudad no tiene líder.");
            }
            else if (!city.hasEnoughResourcesFor(launcher.cost))
            {
                result.AppendLine("    Lanzamisiles terrestre: bloqueado — necesita " + launcher.costLabel + ".");
            }
            else
            {
                result.AppendLine("    Lanzamisiles terrestre: <color=#8FF0A4>elegible</color> — " + Escape(launcher.id) +
                    " se construirá en el próximo ciclo de producción disponible (coste: " + launcher.costLabel + ").");
            }
        }

        private static void AppendNavalProduction(StringBuilder result, City city)
        {
            List<Building> docks = GetDockBuildings(city);
            int dockCount = docks.Count;
            int currentBoats = CountBoats(city);
            string faction = GetNavalFaction(city);
            List<ProductionCandidate> candidates = GetNavalCandidates(faction);

            if (!Traits.vehiclesAllowed)
            {
                result.AppendLine("    Naval: bloqueado — vehículos desactivados. Puertos: " + dockCount + ", embarcaciones vinculadas: " + currentBoats + ".");
                return;
            }

            if (dockCount == 0)
            {
                result.AppendLine("    Naval: bloqueado — no hay puerto. Embarcaciones vinculadas: " + currentBoats + ".");
                return;
            }

            result.AppendLine("    Naval: " + dockCount + " puerto(s), " + currentBoats + " embarcación(es) vinculada(s), flota " + Escape(faction) + ".");
            int shownDocks = Math.Min(MaximumDockLinesPerCity, docks.Count);
            for (int index = 0; index < shownDocks; index++)
            {
                MilitaryQuotaService.DockQuota quota = MilitaryQuotaService.GetDockQuota(docks[index], city);
                int militaryLimit = Math.Max(2, quota.MilitaryBoats);
                result.AppendLine("      Puerto " + (index + 1) + ": " +
                    MilitaryQuotaService.GetDockQuotaLabel(docks[index], city) +
                    "; capacidad militar operativa " + militaryLimit +
                    " (escolta + submarino especial).");
            }
            if (docks.Count > shownDocks)
                result.AppendLine("      + " + (docks.Count - shownDocks) + " puertos más (cupo aplicado individualmente).");
            int kingdomStrategic = MilitaryQuotaService.CountKingdomStrategicAssets(city.kingdom);
            int kingdomStrategicCap = MilitaryQuotaService.GetKingdomStrategicCap(city.kingdom);
            result.AppendLine("      Estratégicos del reino: " + kingdomStrategic + "/" + kingdomStrategicCap + ".");
            if (candidates.Count == 0)
            {
                result.AppendLine("      Catálogo naval: <color=#FFCC77>faltan assets navales registrados.</color>");
                return;
            }

            List<ProductionCandidate> affordable = candidates
                .Where(candidate => city.hasEnoughResourcesFor(candidate.cost))
                .ToList();
            result.AppendLine("      Catálogo naval por puerto: " + DescribeCandidates(candidates) + ".");
            result.AppendLine("      Los submarinos estratégicos requieren antes una nave militar normal y espacio tanto en el puerto como en el reino.");

            if (affordable.Count == 0)
            {
                result.AppendLine("      Bloqueo: faltan recursos para las embarcaciones disponibles.");
            }
            else
            {
                result.AppendLine("      Con recursos ahora: " + DescribeCandidates(affordable) +
                    ". La IA los intentará fabricar en sus ciclos normales si queda cupo.");
            }
        }

        private static string DescribeKingdomInventory(IEnumerable<City> cities)
        {
            int land = 0;
            int launchers = 0;
            int boats = 0;
            int submarines = 0;
            foreach (City city in cities)
            {
                if (city?.units == null)
                    continue;
                foreach (Actor unit in city.units)
                {
                    if (unit == null || !unit.isAlive())
                        continue;
                    string id = unit.asset?.id;
                    if (string.IsNullOrEmpty(id))
                        continue;
                    if (id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase)) launchers++;
                    if (IsSubmarine(id)) submarines++;
                    if (IsBoat(id)) boats++;
                    if (ModernCapPolicy.IsLandMilitaryActor(id)) land++;
                }
            }
            return "tierra " + land + ", lanzamisiles " + launchers + ", barcos " + boats + ", submarinos " + submarines;
        }

        private static string GetLandGate(City city, int population, int currentLand, int landCap)
        {
            if (!Traits.vehiclesAllowed) return "<color=#FF8888>bloqueada: vehículos desactivados</color>";
            if (city == null || !city.isAlive()) return "<color=#FF8888>bloqueada: ciudad no válida</color>";
            if (city.kingdom == null || !city.kingdom.isCiv()) return "<color=#FF8888>bloqueada: no pertenece a una civilización</color>";
            if (!city.hasLeader()) return "<color=#FF8888>bloqueada: sin líder</color>";
            if (!city.hasBuildingType("type_hall")) return "<color=#FF8888>bloqueada: sin ayuntamiento</color>";
            if (population < 20) return "<color=#FF8888>bloqueada: requiere 20 habitantes</color>";
            if (currentLand >= landCap &&
                !(MilitaryProgressionController.GetLevel(city) >= 3 && CountArtillery(city) == 0))
                return "<color=#FF8888>bloqueada: cupo completo</color>";
            return "<color=#8FF0A4>habilitada</color>";
        }

        private static List<ProductionCandidate> GetLandCandidates(City city)
        {
            List<ProductionCandidate> result = new List<ProductionCandidate>();
            string species = GetSpecies(city);
            if (string.IsNullOrEmpty(species))
                return result;

            AddLandCandidates(result, Traits.CartTransformations.CartTransformationsModernRoles, "moderno", species);
            AddLandCandidates(result, Traits.CartTransformations.CartTransformationsRenaissanceRoles, "renacimiento", species);
            AddLandCandidates(result, Traits.CartTransformations.CartTransformationsMedievalRoles, "medieval", species);

            return result
                .GroupBy(candidate => candidate.tier + "|" + candidate.role + "|" + candidate.id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.tier)
                .ThenBy(candidate => candidate.role)
                .ThenBy(candidate => candidate.id)
                .ToList();
        }

        private static void AddLandCandidates(
            List<ProductionCandidate> result,
            Dictionary<string, Dictionary<string, List<string>>> table,
            string tier,
            string species)
        {
            if (table == null || !table.TryGetValue(species, out Dictionary<string, List<string>> roles) || roles == null)
                return;

            foreach (KeyValuePair<string, List<string>> role in roles)
            {
                if (role.Value == null)
                    continue;
                foreach (string id in role.Value.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ActorAsset asset = AssetManager.actor_library.get(id);
                    if (asset == null || !ModernCapPolicy.IsLandMilitaryActor(id) ||
                        string.IsNullOrEmpty(asset.default_attack) || AssetManager.items.get(asset.default_attack) == null)
                        continue;

                    ProductionCandidate candidate = GetLandCandidate(id, tier, role.Key);
                    result.Add(candidate);
                }
            }
        }

        private static ProductionCandidate GetLandCandidate(string id, string tier, string role)
        {
            ConstructionCost cost;
            string label;
            if (id.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) ||
                id == "F55FighterJet" || id == "americanbomberww" || id == "biplane" ||
                id == "fighterww" || id == "Zeppelin" || id == "EliteZeppelin" ||
                id.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase) || id == "AbramTank")
            {
                cost = new ConstructionCost(7, 6, 4, 2);
                label = "7 madera, 6 piedra, 4 metal, 2 oro";
            }
            else if (tier == "renacimiento")
            {
                cost = new ConstructionCost(5, 4, 2, 1);
                label = "5 madera, 4 piedra, 2 metal, 1 oro";
            }
            else if (tier == "medieval")
            {
                cost = new ConstructionCost(4, 3, 0, 0);
                label = "4 madera, 3 piedra";
            }
            else
            {
                cost = new ConstructionCost(6, 5, 3, 2);
                label = "6 madera, 5 piedra, 3 metal, 2 oro";
            }

            return new ProductionCandidate { id = id, tier = tier, role = role, cost = cost, costLabel = label };
        }

        private static List<ProductionCandidate> GetNavalCandidates(string faction)
        {
            List<string> ids = new List<string>
            {
                "aDestroyer_" + faction,
                "bDestroyer_" + faction,
                "Submarine_" + faction,
                "SalvoSubmarine_" + faction
            };
            foreach (string roleId in NavalRoles.GetRoleIds())
            {
                if (roleId.EndsWith("_" + faction, StringComparison.OrdinalIgnoreCase))
                    ids.Add(roleId);
            }

            List<ProductionCandidate> result = new List<ProductionCandidate>();
            foreach (string id in ids)
            {
                if (AssetManager.actor_library.get(id) == null)
                    continue;

                ConstructionCost cost;
                string label;
                if (id.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(15, 13, 11, 6);
                    label = "15 madera, 13 piedra, 11 metal, 6 oro";
                }
                else if (id.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(6, 5, 3, 1);
                    label = "6 madera, 5 piedra, 3 metal, 1 oro";
                }
                else if (id.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(8, 7, 5, 2);
                    label = "8 madera, 7 piedra, 5 metal, 2 oro";
                }
                else if (id.StartsWith("TridentSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(12, 10, 8, 4);
                    label = "12 madera, 10 piedra, 8 metal, 4 oro";
                }
                else if (id.StartsWith("NeutronSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(9, 8, 6, 3);
                    label = "9 madera, 8 piedra, 6 metal, 3 oro";
                }
                else if (id.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(9, 8, 6, 3);
                    label = "9 madera, 8 piedra, 6 metal, 3 oro";
                }
                else if (id.StartsWith("HammerSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(13, 11, 9, 5);
                    label = "13 madera, 11 piedra, 9 metal, 5 oro";
                }
                else if (id.StartsWith("RuinSubmarine_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(9, 8, 6, 3);
                    label = "9 madera, 8 piedra, 6 metal, 3 oro";
                }
                else if (IsSubmarine(id))
                {
                    cost = new ConstructionCost(8, 7, 5, 2);
                    label = "8 madera, 7 piedra, 5 metal, 2 oro";
                }
                else if (IsMilitaryBoat(id))
                {
                    cost = new ConstructionCost(6, 5, 4, 2);
                    label = "6 madera, 5 piedra, 4 metal, 2 oro";
                }
                else if (id.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase))
                {
                    cost = new ConstructionCost(7, 5, 3, 2);
                    label = "7 madera, 5 piedra, 3 metal, 2 oro";
                }
                else
                {
                    cost = new ConstructionCost(4, 3, 1, 1);
                    label = "4 madera, 3 piedra, 1 metal, 1 oro";
                }

                result.Add(new ProductionCandidate { id = id, tier = "naval", role = GetActorCategory(id), cost = cost, costLabel = label });
            }
            return result;
        }

        private static string DescribeCandidates(IEnumerable<ProductionCandidate> candidates)
        {
            List<ProductionCandidate> list = candidates.ToList();
            if (list.Count == 0)
                return "ninguno";

            int shown = Math.Min(MaximumCandidatesPerLine, list.Count);
            string description = string.Join(", ", list.Take(shown).Select(candidate =>
                Escape(candidate.id) + " <color=#B9C9D8>(" + Escape(candidate.costLabel) + ")</color>"));
            if (list.Count > shown)
                description += " <color=#BBBBBB>+ " + (list.Count - shown) + " más</color>";
            return description;
        }

        private static int CountLandMilitary(City city)
        {
            if (city?.units == null)
                return 0;

            int count = 0;
            foreach (Actor unit in city.units)
            {
                if (unit == null || !unit.isAlive())
                    continue;
                string id = unit.asset?.id;
                if (id == "baseWarUnit" || ModernCapPolicy.IsLandMilitaryActor(id))
                    count++;
            }
            return count;
        }

        private static int CountArtillery(City city)
        {
            if (city?.units == null)
                return 0;

            int count = 0;
            foreach (Actor unit in city.units)
            {
                if (unit != null && unit.isAlive() &&
                    ModernCapPolicy.IsConventionalArtillery(unit.asset?.id))
                    count++;
            }
            return count;
        }

        private static bool HasMissileLauncher(City city)
        {
            if (city?.units == null)
                return false;
            return city.units.Any(unit => unit != null && unit.isAlive() &&
                unit.asset?.id?.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) == true);
        }

        private static List<Building> GetDockBuildings(City city)
        {
            if (city?.buildings == null)
                return new List<Building>();
            return city.buildings.Where(building =>
                    building?.asset?.id?.IndexOf("docks", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private static int CountBoats(City city)
        {
            if (city?.units == null)
                return 0;
            return city.units.Count(unit => unit != null && unit.isAlive() && IsBoat(unit.asset?.id));
        }

        private static string GetSpecies(City city)
        {
            Actor leader = city?.leader;
            string species = leader?.subspecies?.data?.species_id;
            return string.IsNullOrEmpty(species) ? leader?.asset?.id : species;
        }

        private static string GetNavalFaction(City city)
        {
            string leader = city?.leader?.asset?.id ?? string.Empty;
            if (leader == "dwarf" || leader.Contains("cold") || leader.Contains("penguin")) return "harden";
            if (leader == "elf" || leader.Contains("druid") || leader.Contains("fairy")) return "gaia";
            if (leader == "orc" || leader.Contains("necromancer") || leader.Contains("wolf")) return "horde";
            return "alliance";
        }

        private static bool IsBoat(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            return id.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("FishingBoat_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Transporter_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase) ||
                NavalRoles.IsAnyModernSubmarine(id);
        }

        private static bool IsMilitaryBoat(string id)
        {
            return id != null && (id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase) ||
                NavalRoles.IsAnyModernSubmarine(id));
        }

        private static bool IsSubmarine(string id)
        {
            return id != null && NavalRoles.IsAnyModernSubmarine(id);
        }

        private static string GetActorCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Desconocida";
            if (id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase)) return "Lanzamisiles terrestre";
            string submarineRole = NavalRoles.GetRoleLabel(id);
            if (!string.IsNullOrEmpty(submarineRole)) return submarineRole;
            if (id.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase)) return "Submarino estratégico de salva";
            if (id.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase)) return "Submarino";
            if (id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) || id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase)) return "Destructor";
            if (id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase)) return "Portaaviones";
            if (id.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase) || id.StartsWith("FishingBoat_", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Transporter_", StringComparison.OrdinalIgnoreCase)) return "Buque";
            if (ModernCapPolicy.IsAllowedAircraft(id)) return "Aeronave";
            if (ModernCapPolicy.IsArtillery(id)) return "Artillería";
            if (ModernCapPolicy.IsLandMilitaryActor(id)) return "Vehículo terrestre";
            return "Unidad";
        }

        private static string GetDoctrineLabel(Kingdom kingdom)
        {
            try
            {
                string display = MilitaryDoctrineService.GetDisplayName(kingdom);
                return string.IsNullOrEmpty(display) ? "En evaluación" : display;
            }
            catch
            {
                return "En evaluación";
            }
        }

        private static string GetProgressionLabel(City city)
        {
            try
            {
                MilitaryProgressionStatus status = MilitaryProgressionController.GetStatus(city);
                if (status == null)
                    return "En evaluación";

                StringBuilder label = new StringBuilder();
                label.Append("nivel ").Append(status.Level);
                label.Append(" — infraestructura ").Append(status.RelevantBuildings)
                    .Append("/logística ").Append(status.AdvancedBuildings);
                label.Append("; financiación renacimiento ").Append(FormatBoolean(status.CanFundRenaissance))
                    .Append(", pesada ").Append(FormatBoolean(status.CanFundHeavy));
                if (!IsNoBlockingReason(status.BlockingReason))
                    label.Append("; ").Append(status.BlockingReason);
                return label.ToString();
            }
            catch
            {
                return "En evaluación";
            }
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "sí" : "no";
        }

        private static bool IsNoBlockingReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ||
                string.Equals(reason, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "ninguno", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static int SafePopulation(City city)
        {
            try { return city?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static string GetCityName(City city)
        {
            if (city == null)
                return null;
            try
            {
                Type type = city.GetType();
                PropertyInfo property = type.GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    type.GetProperty("city_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property?.GetValue(city, null);
                if (value != null)
                    return value.ToString();

                FieldInfo field = type.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    type.GetField("city_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                value = field?.GetValue(city);
                if (value != null)
                    return value.ToString();

                PropertyInfo dataProperty = type.GetProperty("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object data = dataProperty?.GetValue(city, null);
                if (data == null)
                {
                    FieldInfo dataField = type.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    data = dataField?.GetValue(city);
                }
                if (data == null)
                    return null;

                Type dataType = data.GetType();
                PropertyInfo nestedName = dataType.GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    dataType.GetProperty("city_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                value = nestedName?.GetValue(data, null);
                if (value != null)
                    return value.ToString();
                FieldInfo nestedField = dataType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    dataType.GetField("city_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return nestedField?.GetValue(data)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string SafeName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
