using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace TheExplorersCodex.Windows;

/// <summary>
/// Neues Einstellungsmenü (siehe NewDesign/DESIGN_SPEC.md Abschnitt 6-8, hier Schritt 7 "Menü mit
/// Seitenleiste") - wird wie <see cref="CodexOverlayWindow"/> komplett eigenständig NEBEN dem
/// bisherigen <see cref="MainWindow"/> aufgebaut, dieses bleibt unverändert. Aktueller Stand
/// (Nutzeranforderung "vorerst nur Settings/General"): Seitenleiste mit Logo, EINER Gruppe
/// ("SETTINGS") mit EINEM Eintrag ("General"), Versionsanzeige unten. Die Seite "Allgemein" zeigt
/// bereits dieselben Einstellungen wie MainWindow.DrawGeneralTab (Sprache/Overlay/Quality of
/// Life/Automation), neu gestaltet mit CodexTheme-Karten/-Schaltern statt ModernUi - beide Menüs
/// schreiben in dieselbe Configuration, Änderungen wirken also in beiden. Weitere Seiten (Abschnitt
/// 8, "Die Menüseiten nacheinander") noch nicht umgesetzt.
/// </summary>
public class CodexMenuWindow : Window
{
    private readonly Plugin plugin;
    private static readonly string VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private enum MenuPage
    {
        General,
    }

    private MenuPage activePage = MenuPage.General;

    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;

    public CodexMenuWindow(Plugin plugin) : base("##CodexNewDesignMenu", BaseFlags)
    {
        this.plugin = plugin;
        Size = new Vector2(860f, 600f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640f, 420f), MaximumSize = new Vector2(4000f, 4000f) };
    }

    public override bool DrawConditions() =>
        Plugin.ClientState.IsLoggedIn;

    public override void PreDraw()
    {
        CodexTheme.PushStyle();
        ImGui.PushStyleColor(ImGuiCol.WindowBg, CodexTheme.BgWindow);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineControl);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, CodexTheme.RoundingWindow);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        CodexTheme.PopStyle();
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        DrawSidebar(scale);
        ImGui.SameLine(0f, 0f);
        DrawContent(scale);

        CodexTheme.DrawCornerOrnaments(
            CodexTheme.CornerLenMenu * scale,
            CodexTheme.CornerThickMenu * scale,
            CodexTheme.CornerInsetMenu * scale);
    }

    /// <summary>Abschnitt 6 - Seitenleiste: Logo, Navigationsgruppen, Versionsanzeige unten.</summary>
    private void DrawSidebar(float scale)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CodexTheme.BgSidebar);
        ImGui.BeginChild("##CodexMenuSidebar", new Vector2(CodexTheme.SidebarWidth * scale, 0f), false);
        ImGui.Indent(18f * scale);
        ImGui.Dummy(new Vector2(0f, 20f * scale));

        DrawLogo(scale);

        ImGui.Dummy(new Vector2(0f, 24f * scale));
        CodexTheme.SectionLabel(Loc.T("EINSTELLUNGEN", "SETTINGS"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, MenuPage.General, FontAwesomeIcon.ArrowsAltH, Loc.T("Allgemein", "General"));

        // Versionsanzeige ganz unten - ersetzt vorerst die Statuskarte aus dem Entwurf
        // ("Archiv synchronisiert"), die ein echtes Synchronisierungs-Konzept voraussetzt, das es in
        // diesem Plugin nicht gibt - erfundene Stand-Angaben wären hier irreführend.
        var footerY = ImGui.GetWindowHeight() - 32f * scale;
        if (ImGui.GetCursorPosY() < footerY)
            ImGui.SetCursorPosY(footerY);
        using (CodexTheme.FontBodySmall.Push())
            ImGui.TextColored(CodexTheme.TextDim, $"v{VersionText}");

        ImGui.Unindent(18f * scale);
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Logo-Zeile (Abschnitt 6) - Kompass-Symbol links, rechts daneben zwei übereinanderstehende
    /// Textzeilen ("THE EXPLORER'S" klein über "Codex" groß), beide vertikal mittig zur Icon-Höhe.
    /// Per drawList.AddText statt ImGui-Cursor-Fluss positioniert (siehe CodexOverlayWindow-Vorbild
    /// an vielen Stellen) - zuverlässiger für dieses zweizeilige Nebeneinander als SameLine-Ketten.
    /// </summary>
    private void DrawLogo(float scale)
    {
        var iconSize = 40f * scale;
        var cursor = ImGui.GetCursorScreenPos();

        CodexTheme.DrawCompassIcon(iconSize);

        var drawList = ImGui.GetWindowDrawList();
        var textX = cursor.X + iconSize + 10f * scale;

        float smallHeight, largeHeight;
        using (CodexTheme.FontSidebarBrandSmall.Push())
            smallHeight = ImGui.GetFontSize();
        using (CodexTheme.FontSidebarBrandLarge.Push())
            largeHeight = ImGui.GetFontSize();

        var startY = cursor.Y + (iconSize - (smallHeight + largeHeight)) / 2f;

        using (CodexTheme.FontSidebarBrandSmall.Push())
            drawList.AddText(new Vector2(textX, startY), ImGui.GetColorU32(CodexTheme.TextSecondary), Loc.T("DER FORSCHER", "THE EXPLORER'S"));
        using (CodexTheme.FontSidebarBrandLarge.Push())
            drawList.AddText(new Vector2(textX, startY + smallHeight), ImGui.GetColorU32(CodexTheme.TextHeading), Loc.T("Codex", "Codex"));
    }

    private void DrawNavItem(float scale, MenuPage page, FontAwesomeIcon icon, string label)
    {
        var selected = activePage == page;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;

        var height = 30f * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##CodexNav_{page}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        if (selected)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(CodexTheme.BgSelected), CodexTheme.RoundingControl);
            drawList.AddRectFilled(cursor, cursor + new Vector2(2f * scale, height), ImGui.GetColorU32(CodexTheme.Accent));
        }
        else if (hovered)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(CodexTheme.BgSelected with { W = 0.5f }), CodexTheme.RoundingControl);
        }

        var fg = selected ? CodexTheme.TextHeading : CodexTheme.TextSecondary;
        var contentX = cursor.X + 10f * scale;

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(new Vector2(contentX, cursor.Y + (height - ImGui.GetFontSize()) / 2f), ImGui.GetColorU32(selected ? CodexTheme.Accent : fg), icon.ToIconString());

        using (CodexTheme.FontBodyMedium.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(contentX + iconWidth + 10f * scale, cursor.Y + (height - labelSize.Y) / 2f), ImGui.GetColorU32(fg), label);
        }

        if (clicked)
            activePage = page;
    }

    /// <summary>Abschnitt 6 - Inhaltsbereich: bisher nur der Seitenkopf (Titel + Untertitel + Trenn-Ornament) für "General", noch ohne eigentliche Einstellungen.</summary>
    private void DrawContent(float scale)
    {
        ImGui.BeginChild("##CodexMenuContent", Vector2.Zero, false);
        ImGui.Indent(32f * scale);
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        using (CodexTheme.FontTitleMenu.Push())
            ImGui.TextColored(CodexTheme.TextHeading, Loc.T("Allgemein", "General"));

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
        using (CodexTheme.FontMenuSubtitle.Push())
            ImGui.TextColored(CodexTheme.TextSecondary, Loc.T("Grundeinstellungen des Plugins.", "Basic plugin settings."));

        ImGui.Dummy(new Vector2(0f, 5f * scale));
        CodexTheme.DrawDividerOrnament();

        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawGeneralPage(scale);

        ImGui.Unindent(32f * scale);
        ImGui.EndChild();
    }

    // ---- Seite "Allgemein" (Abschnitt 8) - Karten "Sprache", "Overlay", "Quality of Life",
    // "Automation", 1:1 dieselben Configuration-Felder wie MainWindow.DrawGeneralTab, nur neu
    // gestaltet (Karten/Schalter im neuen Theme statt ModernUi). Speichert wie das alte Menü sofort
    // bei jeder Änderung (config.Save()) - es gibt bewusst keine Reset-/Speichern-Knöpfe aus dem
    // Entwurf, da dieses Plugin kein Entwurf-/Staging-Konzept kennt und ein "Speichern"-Knopf ohne
    // echten Zweck hier irreführend wäre.
    private string mountFilter = string.Empty;

    private void DrawGeneralPage(float scale)
    {
        var config = plugin.Configuration;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var gap = 20f * scale;
        var columnWidth = (availWidth - gap) / 2f;

        ImGui.BeginChild("##CodexGeneralLeft", new Vector2(columnWidth, 0f), false);
        DrawLanguageCard(scale, config);
        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawOverlayCard(scale, config);
        ImGui.EndChild();

        ImGui.SameLine(0f, gap);

        ImGui.BeginChild("##CodexGeneralRight", new Vector2(columnWidth, 0f), false);
        DrawQualityOfLifeCard(scale, config);
        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawAutomationCard(scale, config);
        ImGui.EndChild();
    }

    private void DrawLanguageCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("SPRACHE", "LANGUAGE"), scale, CodexTheme.DropdownRowLineWidth(scale));

        var languageLabels = new (MenuLanguage Language, string Label)[]
        {
            (MenuLanguage.German, "Deutsch"),
            (MenuLanguage.English, "English"),
        };
        var currentLanguageLabel = languageLabels.First(l => l.Language == config.MenuLanguage).Label;

        var open = CodexTheme.BeginDropdownRow(Loc.T("Menüsprache", "Menu language"), currentLanguageLabel, "##CodexMenuLanguage", scale,
            Loc.T("Gilt nur für dieses Menü - das kompakte Overlay folgt weiterhin der Spielsprache.",
                "Only affects this menu - the compact overlay keeps following the game language."));
        if (open)
        {
            foreach (var (language, label) in languageLabels)
            {
                if (ImGui.Selectable(label, config.MenuLanguage == language))
                {
                    config.MenuLanguage = language;
                    config.Save();
                }
            }
        }
        CodexTheme.EndDropdownRow(open, disabled: false);

        CodexTheme.EndCard();
    }

    private void DrawOverlayCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("OVERLAY", "OVERLAY"), scale);

        var showOverlay = config.ShowCompactOverlay;
        if (CodexTheme.ToggleRow("##CodexShowOverlay", Loc.T("Overlay aktivieren", "Enable overlay"), ref showOverlay, scale))
        {
            config.ShowCompactOverlay = showOverlay;
            plugin.CompactOverlayWindow.IsOpen = showOverlay;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showAutomationButtons = config.ShowAutomationButtons;
        if (CodexTheme.ToggleRow("##CodexShowAutomationButtons", Loc.T("Automation-Knöpfe anzeigen", "Show automation buttons"), ref showAutomationButtons, scale,
                Loc.T("Blendet nur die Start-/Stopp-Knöpfe im Overlay aus - laufende Automationen werden dadurch nicht gestoppt.",
                    "Only hides the start/stop buttons in the overlay - running automations keep running.")))
        {
            config.ShowAutomationButtons = showAutomationButtons;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showWallet = config.ShowCurrencyWallet;
        if (CodexTheme.ToggleRow("##CodexShowCurrencyWallet", Loc.T("Währungen anzeigen", "Show currencies"), ref showWallet, scale))
        {
            config.ShowCurrencyWallet = showWallet;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showGoToIcon = config.ShowGoToIcon;
        if (CodexTheme.ToggleRow("##CodexShowGoToIcon", Loc.T("\"Hinlaufen\"-Icon anzeigen", "Show \"go to\" icon"), ref showGoToIcon, scale))
        {
            config.ShowGoToIcon = showGoToIcon;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var hideOverlayWhenEmpty = config.HideOverlayWhenEmpty;
        if (CodexTheme.ToggleRow("##CodexHideOverlayWhenEmpty", Loc.T("Overlay bei leerer Zone ausblenden", "Hide overlay when zone is empty"), ref hideOverlayWhenEmpty, scale,
                Loc.T("Blendet das Overlay komplett aus, solange es in der aktuellen Zone (nach allen aktiven Filtern) nichts Fehlendes gibt - laufende Automationen laufen davon unbeeinflusst weiter.",
                    "Completely hides the overlay while there's nothing missing in the current zone (after all active filters) - running automations keep running unaffected.")))
        {
            config.HideOverlayWhenEmpty = hideOverlayWhenEmpty;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var allaganToolsAvailableForRetainerCounts = Plugin.IsAllaganToolsAvailable();
        if (config.ShowRetainerItemCounts && !allaganToolsAvailableForRetainerCounts)
        {
            config.ShowRetainerItemCounts = false;
            config.Save();
        }

        if (!allaganToolsAvailableForRetainerCounts)
            ImGui.BeginDisabled();
        var showRetainerItemCounts = config.ShowRetainerItemCounts;
        if (CodexTheme.ToggleRow("##CodexShowRetainerItemCounts", Loc.T("Retainer-Bestände bei Währungen anzeigen", "Show retainer stock next to currencies"), ref showRetainerItemCounts, scale,
                Loc.T("Zeigt hinter jeder Währung unter \"Deine Währungen\" zusätzlich \"(<Anzahl>)\" mit der auf den eigenen Retainern und in der Chocobo-Satteltasche liegenden Menge. Erfordert Allagan Tools.",
                    "Shows \"(<count>)\" after each currency under \"Your currencies\" with how much of it sits on your retainers and in your Chocobo Saddlebag. Requires Allagan Tools.")))
        {
            config.ShowRetainerItemCounts = showRetainerItemCounts;
            config.Save();
        }
        if (!allaganToolsAvailableForRetainerCounts)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.T("Allagan Tools ist nicht installiert.", "Allagan Tools is not installed."));
        }

        CodexTheme.CardDivider(scale);

        var showAllItems = config.ShowAllItems;
        if (CodexTheme.ToggleRow("##CodexShowAllItems", Loc.T("Alle Gegenstände anzeigen", "Show all items"), ref showAllItems, scale,
                Loc.T("Zeige alle Items/Daten, auch wenn sie durch ein nicht erreichtes Achievement oder nicht freigeschaltete Ränge aktuell nicht erreichbar sind.",
                    "Show all items/data, even if they're currently unreachable due to a not-yet-completed achievement or unlocked rank.")))
        {
            config.ShowAllItems = showAllItems;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showOnlyActiveEventItems = config.ShowOnlyActiveEventItems;
        if (CodexTheme.ToggleRow("##CodexShowOnlyActiveEventItems", Loc.T("Nur aktive Event-Gegenstände anzeigen", "Show only active event items"), ref showOnlyActiveEventItems, scale,
                Loc.T("Blendet nur Saisonevent-Gegenstände aus, deren Event gerade nicht läuft - alle anderen Gegenstände bleiben sichtbar.",
                    "Only hides seasonal event items whose event isn't currently running - all other items stay visible.")))
        {
            config.ShowOnlyActiveEventItems = showOnlyActiveEventItems;
            config.Save();
        }

        CodexTheme.EndCard();
    }

    private static void DrawQualityOfLifeCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("LEBENSQUALITÄT", "QUALITY OF LIFE"), scale);

        var showNavigationArrow = config.ShowNavigationArrow;
        if (CodexTheme.ToggleRow("##CodexShowNavigationArrow", Loc.T("Wegweiser-Pfeil anzeigen", "Show navigation arrow"), ref showNavigationArrow, scale,
                Loc.T("Zeigt einen verschiebbaren Pfeil zum aktuellen Ziel - verschwindet bei Ankunft oder per Rechtsklick.",
                    "Shows a movable arrow pointing to the current target - disappears on arrival or right-click.")))
        {
            config.ShowNavigationArrow = showNavigationArrow;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var useSprint = config.UseSprintOnCooldown;
        if (CodexTheme.ToggleRow("##CodexUseSprint", Loc.T("Sprint auf Cooldown nutzen", "Use Sprint on cooldown"), ref useSprint, scale))
        {
            config.UseSprintOnCooldown = useSprint;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var allaganToolsAvailable = Plugin.IsAllaganToolsAvailable();
        if (config.EnableAllaganToolsIntegration && !allaganToolsAvailable)
        {
            config.EnableAllaganToolsIntegration = false;
            config.Save();
        }

        if (!allaganToolsAvailable)
            ImGui.BeginDisabled();
        var enableAllaganTools = config.EnableAllaganToolsIntegration;
        if (CodexTheme.ToggleRow("##CodexEnableAllaganTools", Loc.T("Allagan-Tools-Integration aktivieren", "Enable Allagan Tools integration"), ref enableAllaganTools, scale,
                Loc.T("Aktiviert die Möglichkeit, mehr Informationen zu Items/Währungen zu bekommen.",
                    "Enables the ability to get more information about items or currencies.")))
        {
            config.EnableAllaganToolsIntegration = enableAllaganTools;
            config.Save();
        }
        if (!allaganToolsAvailable)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.T("Allagan Tools ist nicht installiert.", "Allagan Tools is not installed."));
        }

        CodexTheme.EndCard();
    }

    private void DrawAutomationCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("AUTOMATION", "AUTOMATION"), scale);

        DrawCombatPluginPicker(scale, config);
        CodexTheme.CardDivider(scale);
        DrawMountPicker(scale, config);
        CodexTheme.CardDivider(scale);
        DrawChocoboCompanionSettings(scale, config);

        CodexTheme.EndCard();
    }

    private static void DrawCombatPluginPicker(float scale, Configuration config)
    {
        Plugin.EnsureCombatPluginDefault();
        var installed = CombatPluginBridge.GetInstalled();
        var effective = CombatPluginBridge.GetEffective();
        var pickerEnabled = installed.Count > 1;

        var currentLabel = effective is { } current
            ? CombatPluginBridge.DisplayName(current)
            : Loc.T("Keines installiert", "None installed");

        var open = CodexTheme.BeginDropdownRow(Loc.T("Kampf-Plugin", "Combat plugin"), currentLabel, "##CodexCombatPlugin", scale,
            Loc.T("Welches Plugin bei der Hunting-Log-Automation (und kampfpflichtigen Quest-Schritten) den Kampf übernimmt.",
                "Which plugin handles combat during the hunting log automation (and combat-required quest steps)."),
            disabled: !pickerEnabled);
        if (open)
        {
            foreach (var kind in Enum.GetValues<CombatPluginKind>())
            {
                var isInstalled = installed.Contains(kind);
                if (!isInstalled)
                    ImGui.BeginDisabled();

                if (ImGui.Selectable(CombatPluginBridge.DisplayName(kind), effective == kind) && isInstalled && config.CombatPlugin != kind)
                {
                    config.CombatPlugin = kind;
                    config.Save();
                }

                if (!isInstalled)
                    ImGui.EndDisabled();
            }
        }
        CodexTheme.EndDropdownRow(open, disabled: !pickerEnabled);
    }

    private void DrawMountPicker(float scale, Configuration config)
    {
        plugin.EnsureAetheryteMountAutoDefault();

        var unlockedMounts = plugin.GetUnlockedMounts();
        var noMountsUnlocked = unlockedMounts.Count == 0;
        var rouletteAvailable = unlockedMounts.Count >= 2;

        var noneLabel = Loc.T("Kein Mount (zu Fuß)", "No mount (on foot)");
        var rouletteLabel = Loc.T("Mount Roulette", "Mount Roulette");
        var currentLabel = config.AetheryteMountId switch
        {
            null => noneLabel,
            0 when rouletteAvailable => rouletteLabel,
            0 => noneLabel,
            var id => unlockedMounts.FirstOrDefault(m => m.Id == (uint)id.Value)?.Name ?? noneLabel,
        };

        var open = CodexTheme.BeginDropdownRow(Loc.T("Mount", "Mount"), currentLabel, "##CodexAetheryteMount", scale,
            noMountsUnlocked ? Loc.T("Noch kein Mount freigeschaltet.", "No mount unlocked yet.") : null,
            disabled: noMountsUnlocked);
        if (open)
        {
            if (ImGui.Selectable(noneLabel, config.AetheryteMountId == null))
            {
                config.AetheryteMountId = null;
                config.Save();
            }

            if (rouletteAvailable && ImGui.Selectable(rouletteLabel, config.AetheryteMountId == 0))
            {
                config.AetheryteMountId = 0;
                config.Save();
            }

            ImGui.Separator();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##CodexAetheryteMountFilter", Loc.T("Mounts durchsuchen...", "Search mounts..."), ref mountFilter, 100);

            var filtered = string.IsNullOrWhiteSpace(mountFilter)
                ? unlockedMounts
                : unlockedMounts.Where(m => m.Name.Contains(mountFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            ImGui.BeginChild("##CodexAetheryteMountList", new Vector2(0, 150f * scale));
            foreach (var mount in filtered)
            {
                var isSelected = config.AetheryteMountId == (int)mount.Id;
                if (ImGui.Selectable(mount.Name, isSelected))
                {
                    config.AetheryteMountId = (int)mount.Id;
                    config.Save();
                }
            }
            ImGui.EndChild();
        }
        CodexTheme.EndDropdownRow(open, disabled: noMountsUnlocked);
    }

    private static void DrawChocoboCompanionSettings(float scale, Configuration config)
    {
        var unlocked = Plugin.IsChocoboCompanionUnlocked();

        if (!unlocked)
            ImGui.BeginDisabled();
        var useChocobo = config.UseChocoboCompanion;
        if (CodexTheme.ToggleRow("##CodexUseChocoboCompanion", Loc.T("Chocobo-Begleiter nutzen", "Use Chocobo Companion"), ref useChocobo, scale,
                Loc.T("Lässt die Quest- und Hunting-Log-Automation den Chocobo-Begleiter beschwören und am Leben erhalten.",
                    "Lets the quest and hunting log automation summon and keep the Chocobo Companion alive.")))
        {
            config.UseChocoboCompanion = useChocobo;
            config.Save();
        }
        if (!unlocked)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.T("Erfordert die abgeschlossene Quest \"My Feisty Little Chocobo\".", "Requires the completed quest \"My Feisty Little Chocobo\"."));
        }

        CodexTheme.CardDivider(scale);

        var stances = new (ChocoboStance Stance, string Label)[]
        {
            (ChocoboStance.Attacker, Loc.T("Angreifer", "Attacker")),
            (ChocoboStance.Defender, Loc.T("Verteidiger", "Defender")),
            (ChocoboStance.Healer, Loc.T("Heiler", "Healer")),
            (ChocoboStance.FreeStance, Loc.T("Freie Haltung", "Free Stance")),
        };

        var stanceComboEnabled = unlocked && config.UseChocoboCompanion;
        var currentStanceLabel = stances.Where(s => s.Stance == config.ChocoboStance).Select(s => s.Label).FirstOrDefault(string.Empty);

        var open = CodexTheme.BeginDropdownRow(Loc.T("Chocobo-Haltung", "Chocobo stance"), currentStanceLabel, "##CodexChocoboStance", scale,
            disabled: !stanceComboEnabled);
        if (open)
        {
            foreach (var (stance, label) in stances)
            {
                var stanceUnlocked = Plugin.IsChocoboStanceUnlocked(stance);
                if (!stanceUnlocked)
                    ImGui.BeginDisabled();

                if (ImGui.Selectable(label, config.ChocoboStance == stance) && stanceUnlocked)
                {
                    config.ChocoboStance = stance;
                    config.Save();
                }

                if (!stanceUnlocked)
                    ImGui.EndDisabled();
            }
        }
        CodexTheme.EndDropdownRow(open, disabled: !stanceComboEnabled);
    }
}
