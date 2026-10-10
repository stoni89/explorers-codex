using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using Serilog.Events;

namespace TheExplorersCodex.Windows;

/// <summary>
/// Neues Einstellungsmenü (siehe NewDesign/DESIGN_SPEC.md Abschnitt 6-8, hier Schritt 7 "Menü mit
/// Seitenleiste") - wird wie <see cref="CodexOverlayWindow"/> komplett eigenständig NEBEN dem
/// bisherigen <see cref="MainWindow"/> aufgebaut, dieses bleibt unverändert. Seitenleiste mit Logo,
/// EINER Gruppe ("SETTINGS") mit ZWEI Einträgen ("Allgemein"/"Overlay", Nutzeranforderung),
/// Versionsanzeige unten. "Allgemein" zeigt Sprache/Automation (links) und Quality of Life (rechts),
/// "Overlay" die eigene Overlay-Karte (vorher Teil von "Allgemein") - alle Karten dieselben
/// Configuration-Felder wie MainWindow.DrawGeneralTab, nur neu gestaltet (CodexTheme-Karten/
/// -Schalter statt ModernUi) und ohne die drei Schalter "Alle Gegenstände anzeigen"/"Nur aktive
/// Event-Gegenstände anzeigen"/"Overlay bei leerer Zone ausblenden" (Nutzeranforderung: entfernt).
/// Beide Menüs schreiben in dieselbe Configuration, Änderungen wirken also in beiden. Weitere Seiten
/// (Abschnitt 8, "Die Menüseiten nacheinander") noch nicht umgesetzt.
/// </summary>
public class CodexMenuWindow : Window
{
    private readonly Plugin plugin;
    private static readonly string VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private enum MenuPage
    {
        General,
        Overlay,
        Order,
        Database,
        Blacklist,
        Statistics,
        Plugins,
        About,
        Debug,
        Log,
        Changelog,
    }

    private MenuPage activePage = MenuPage.General;

    // Resize deaktiviert (Nutzeranforderung) - die feste Größe entspricht der zuletzt vom Nutzer
    // manuell eingestellten Fenstergröße (aus dalamudUI.ini: Size=1247,850), jetzt als fester Default
    // statt nur beim ersten Start übernommen.
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;

    private static readonly Vector2 ExpandedWindowSize = new(1247f, 850f);

    // Eingeklappt/ausgeklappt (Nutzeranforderung: eigene Einklappen/Schließen-Knöpfe statt der
    // nativen ImGui-Titelleiste, siehe PluginUiKit.UiWindowControls) - aus der Configuration
    // vorbelegt, damit der Zustand einen Neustart übersteht (siehe PreDraw für die Fenstergröße,
    // DrawInner/DrawCollapsedInner für die jeweilige Darstellung).
    private bool collapsed;

    public CodexMenuWindow(Plugin plugin) : base("##CodexNewDesignMenu", BaseFlags)
    {
        this.plugin = plugin;
        collapsed = plugin.Configuration.MenuWindowCollapsed;
        SizeCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions() =>
        Plugin.ClientState.IsLoggedIn;

    // Nutzer-Report "Position nach Neustart/Rebuild nicht gehalten": ImGuiCond.FirstUseEver greift
    // NUR, solange ImGui diesem Fenster (über die Lebensdauer des zugrundeliegenden ImGui-Kontexts,
    // der Dalamud-Neustarts/Plugin-Reloads überdauert) noch gar keine eigene, selbst in dalamudUI.ini
    // gemerkte Position zugewiesen hat - sobald dalamudUI.ini (unabhängig von unserer eigenen
    // Configuration) irgendeine Position für dieses Fenster kennt, wird unsere per Configuration
    // gespeicherte Position stillschweigend ignoriert. Fix: einmal pro Plugin-Ladevorgang (eigenes
    // bool-Flag statt ImGui's eigener Verfolgung) mit ImGuiCond.Always erzwingen, danach normal frei
    // verschiebbar (das bestehende Debounce-Save in Draw() hält Configuration weiterhin aktuell).
    private bool appliedSavedMenuPosition;

    public override void PreDraw()
    {
        // Bug (Nutzer-Report "Menü nicht mehr verschiebbar"): Dalamuds Window-Basisklasse ruft, solange
        // Position einen Wert hat, JEDEN Frame erneut ImGui.SetNextWindowPos(Position, PositionCondition)
        // auf - mit ImGuiCond.Always bedeutet das, die Position wird auch nach dem ersten Frame
        // unbegrenzt weiter erzwungen und jeder Ziehversuch des Nutzers sofort wieder zurückgesetzt.
        // Position MUSS deshalb direkt nach dem einen gewünschten Frame wieder auf null gesetzt werden,
        // damit Dalamud ab dann gar kein SetNextWindowPos mehr aufruft und ImGui/der Nutzer die
        // Position wieder frei bestimmen.
        if (!appliedSavedMenuPosition && plugin.Configuration.MenuWindowPosition is { } savedMenuPosition)
        {
            Position = savedMenuPosition;
            PositionCondition = ImGuiCond.Always;
            appliedSavedMenuPosition = true;
        }
        else if (appliedSavedMenuPosition)
        {
            Position = null;
        }

        // Eingeklappt (Abschnitt 3 der Nutzeranforderung): Fenster schrumpft auf die Mini-Leiste,
        // Position oben links bleibt unverändert - dasselbe ImGuiCond.Always-Prinzip wie oben bei
        // Position, nur für Size (Dalamuds Window-Basisklasse liest beides erst NACH PreDraw()).
        Size = collapsed
            ? new Vector2(PluginUiKit.UiWindowControls.CollapsedWidth, PluginUiKit.UiWindowControls.CollapsedHeight)
            : ExpandedWindowSize;

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
        // Bugfix (Nutzer-Report: "Menu language" ohne Wirkung) - Loc.MenuLanguageOverride wurde bisher
        // NIRGENDS anhand von config.MenuLanguage gesetzt (nur innerhalb von DrawOverlayPreview gezielt
        // auf null geschaltet, damit DIE Vorschau der Spielsprache folgt - siehe dortigen Kommentar).
        // Dadurch folgte das gesamte restliche Menü immer nur der Spielsprache, unabhängig von der
        // Dropdown-Auswahl. Wie MainWindow.Draw() (siehe dessen Vorbild) nur für die Dauer dieses einen
        // Draw()-Aufrufs gesetzt.
        var config = plugin.Configuration;
        Loc.MenuLanguageOverride = config.MenuLanguage == MenuLanguage.German;
        try
        {
            if (collapsed)
                DrawCollapsedInner();
            else
                DrawInner();
        }
        finally
        {
            Loc.MenuLanguageOverride = null;
        }
    }

    private void DrawInner()
    {
        var scale = ImGuiHelpers.GlobalScale;

        DrawSidebar(scale);
        ImGui.SameLine(0f, 0f);
        var controls = DrawContent(scale);

        // Nutzervorgabe: dünne Trennlinie zwischen Seitenleiste und Inhaltsbereich (bisher nur über
        // den Hintergrundfarbunterschied BgSidebar/BgWindow erkennbar, keine echte Linie) - auf dem
        // Fenster-eigenen Draw-Layer gezeichnet (nicht in einem der beiden Child-Fenster), damit sie
        // über die volle Fensterhöhe durchgeht, unabhängig vom jeweiligen Scroll-Zustand.
        var windowPos = ImGui.GetWindowPos();
        var sidebarLineX = windowPos.X + CodexTheme.SidebarWidth * scale;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(sidebarLineX, windowPos.Y),
            new Vector2(sidebarLineX, windowPos.Y + ImGui.GetWindowSize().Y),
            ImGui.GetColorU32(CodexTheme.LineCard));

        CodexTheme.DrawMenuCornerOrnaments(CodexTheme.CornerInsetMenu * scale);

        if (controls.ToggleCollapse)
        {
            collapsed = true;
            plugin.Configuration.MenuWindowCollapsed = true;
            plugin.Configuration.Save();
        }
        if (controls.Close)
            IsOpen = false;

        PersistWindowPosition(windowPos);
    }

    /// <summary>Eingeklappter Zustand (Abschnitt 3 der Nutzeranforderung) - Mini-Leiste statt Seitenleiste/
    /// Inhalt: Hintergrund/Eckverzierungen, Logo+Seitenname links, Ausklappen/Schließen rechts. Klick aufs
    /// Logo ODER Doppelklick auf die freie Fläche klappt aus, Ziehen auf der freien Fläche verschiebt das
    /// Fenster (siehe PluginUiKit.UiWindowControls.DrawDragArea).</summary>
    private void DrawCollapsedInner()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var config = plugin.Configuration;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        CodexTheme.DrawCollapsedMenuChrome(scale, CodexTheme.CornerInsetMenu * scale);

        var centerY = windowPos.Y + windowSize.Y / 2f;
        var rightEdge = windowPos.X + windowSize.X - 18f * scale;

        var controls = PluginUiKit.UiWindowControls.DrawCollapsedButtons(scale, rightEdge, centerY,
            expandTooltip: Loc.T("Ausklappen", "Expand"), closeTooltip: Loc.T("Schließen", "Close"));

        var brandAreaMin = windowPos + new Vector2(18f * scale, 0f);
        var brandClicked = DrawCollapsedBrandAndLabel(scale, brandAreaMin, windowSize.Y, out var brandAreaMaxX);

        var dragAreaMin = new Vector2(brandAreaMaxX, windowPos.Y);
        var dragAreaMax = new Vector2(rightEdge - PluginUiKit.UiWindowControls.IconButtonSize * scale * 2f - 20f * scale, windowPos.Y + windowSize.Y);
        var dragDoubleClicked = PluginUiKit.UiWindowControls.DrawDragArea("##CodexMenuCollapsedDrag", dragAreaMin, dragAreaMax);

        if (controls.ToggleCollapse || brandClicked || dragDoubleClicked)
        {
            collapsed = false;
            config.MenuWindowCollapsed = false;
            config.Save();
        }
        if (controls.Close)
            IsOpen = false;

        PersistWindowPosition(windowPos);
    }

    /// <summary>Logo (Kompass, 36px) + "THE EXPLORER'S"/"Codex" + " · {Seitenname}" der Mini-Leiste - klickbar
    /// (gibt true bei Klick zurück), vertikal mittig über "barHeight". "brandAreaMaxX" (out) ist die
    /// Bildschirm-X direkt hinter dem gezeichneten Inhalt, für die freie Zieh-/Doppelklickfläche danach.</summary>
    private bool DrawCollapsedBrandAndLabel(float scale, Vector2 areaMin, float barHeight, out float brandAreaMaxX)
    {
        var iconSize = 36f * scale;
        var iconCursor = new Vector2(areaMin.X, areaMin.Y + (barHeight - iconSize) / 2f);
        ImGui.SetCursorScreenPos(iconCursor);
        CodexTheme.DrawCompassIcon(iconSize);

        var drawList = ImGui.GetWindowDrawList();
        var textX = iconCursor.X + iconSize + 10f * scale;

        float smallHeight, largeHeight;
        using (CodexTheme.FontCollapsedBrandSmall.Push())
            smallHeight = ImGui.GetFontSize();
        using (CodexTheme.FontCollapsedBrandLarge.Push())
            largeHeight = ImGui.GetFontSize();

        var startY = iconCursor.Y + (iconSize - (smallHeight + largeHeight)) / 2f;

        using (CodexTheme.FontCollapsedBrandSmall.Push())
            drawList.AddText(new Vector2(textX, startY), ImGui.GetColorU32(CodexTheme.TextSecondary), "THE EXPLORER'S");

        var codexText = Loc.T("Codex", "Codex");
        var codexY = startY + smallHeight;
        float codexWidth;
        using (CodexTheme.FontCollapsedBrandLarge.Push())
        {
            codexWidth = ImGui.CalcTextSize(codexText).X;
            drawList.AddText(new Vector2(textX, codexY), ImGui.GetColorU32(CodexTheme.TextHeading), codexText);
        }

        var pageLabel = " · " + GetPageHeader(activePage).Title;
        float pageLabelWidth, pageLabelHeight;
        using (CodexTheme.FontCollapsedPageLabel.Push())
        {
            var size = ImGui.CalcTextSize(pageLabel);
            pageLabelWidth = size.X;
            pageLabelHeight = size.Y;
        }
        var pageLabelX = textX + codexWidth;
        var pageLabelY = codexY + (largeHeight - pageLabelHeight) / 2f;
        using (CodexTheme.FontCollapsedPageLabel.Push())
            drawList.AddText(new Vector2(pageLabelX, pageLabelY), ImGui.GetColorU32(CodexTheme.TextMuted), pageLabel);

        brandAreaMaxX = pageLabelX + pageLabelWidth + 14f * scale;

        var clickAreaMax = new Vector2(brandAreaMaxX, areaMin.Y + barHeight);
        ImGui.SetCursorScreenPos(areaMin);
        var clicked = ImGui.InvisibleButton("##CodexMenuCollapsedBrand", clickAreaMax - areaMin);
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }

    /// <summary>Nutzeranforderung: Fensterposition persistieren, damit das Fenster beim nächsten Öffnen
    /// wieder dort erscheint, statt sich nur auf die eigene dalamudUI.ini zu verlassen (kein config.Save()
    /// bei jedem Frame während des Ziehens - dasselbe "database is locked"-Problem wie beim
    /// Deckkraft-Regler, siehe dortigen Kommentar). Von DrawInner UND DrawCollapsedInner genutzt, da die
    /// Fensterposition in beiden Zuständen gleich funktioniert (bei Letzterem per Freiflächen-Ziehen
    /// statt der nativen Titelleiste, siehe PluginUiKit.UiWindowControls.DrawDragArea).</summary>
    private void PersistWindowPosition(Vector2 windowPos)
    {
        var config = plugin.Configuration;
        if (config.MenuWindowPosition is not { } savedPos || Vector2.DistanceSquared(savedPos, windowPos) > 0.25f)
        {
            config.MenuWindowPosition = windowPos;
            menuPositionDirty = true;
        }
        if (menuPositionDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            config.Save();
            menuPositionDirty = false;
        }
    }

    private bool menuPositionDirty;

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
        DrawNavItem(scale, MenuPage.Overlay, FontAwesomeIcon.Desktop, Loc.T("Overlay", "Overlay"));
        DrawNavItem(scale, MenuPage.Order, FontAwesomeIcon.SortAmountDown, Loc.T("Reihenfolge", "Order"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        CodexTheme.SectionLabel(Loc.T("ARCHIV", "ARCHIVE"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, MenuPage.Database, FontAwesomeIcon.Database, Loc.T("Datenbank", "Database"));
        DrawNavItem(scale, MenuPage.Blacklist, FontAwesomeIcon.Ban, Loc.T("Blacklist", "Blacklist"));
        DrawNavItem(scale, MenuPage.Statistics, FontAwesomeIcon.ChartBar, Loc.T("Statistik", "Statistics"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        CodexTheme.SectionLabel(Loc.T("SYSTEM", "SYSTEM"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, MenuPage.Plugins, FontAwesomeIcon.Plug, Loc.T("Plugins", "Plugins"));

        // Nutzeranforderung: Debug-Seite nur in der Dev-Version (Entwickler-Werkzeug, geht niemanden
        // in der installierten Version etwas an) - daher schon der Menüpunkt selbst ausgeblendet,
        // nicht nur einzelne Karten darauf. Nutzervorgabe: Debug steht oberhalb von About.
        if (Plugin.PluginInterface.IsDev)
            DrawNavItem(scale, MenuPage.Debug, FontAwesomeIcon.Bug, Loc.T("Debug", "Debug"));

        DrawNavItem(scale, MenuPage.Log, FontAwesomeIcon.FileAlt, Loc.T("Log", "Log"));
        DrawNavItem(scale, MenuPage.Changelog, FontAwesomeIcon.FileAlt, Loc.T("Änderungen", "Changelog"),
            showNewBadge: ChangelogService.HasUnseenChangelog(plugin.Configuration));
        DrawNavItem(scale, MenuPage.About, FontAwesomeIcon.InfoCircle, Loc.T("Über", "About"));

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

        // Nutzervorgabe: Der Plugin-Name wird NIE übersetzt - im Deutschen und Englischen identisch.
        using (CodexTheme.FontSidebarBrandSmall.Push())
            drawList.AddText(new Vector2(textX, startY), ImGui.GetColorU32(CodexTheme.TextSecondary), "THE EXPLORER'S");

        var codexText = Loc.T("Codex", "Codex");
        var codexY = startY + smallHeight;
        float codexWidth;
        using (CodexTheme.FontSidebarBrandLarge.Push())
        {
            codexWidth = ImGui.CalcTextSize(codexText).X;
            drawList.AddText(new Vector2(textX, codexY), ImGui.GetColorU32(CodexTheme.TextHeading), codexText);
        }

        // Nutzeranforderung: Versionsnummer neben "Codex" in Klammern, in der kleinen Schriftgröße
        // (wie vorher unten in der Seitenleiste) - unten an der "Codex"-Zeile ausgerichtet, statt an
        // deren Oberkante (die große Schrift hat spürbar mehr Unterlängen-Freiraum als die kleine).
        var versionText = $"(v{VersionText})";
        using (CodexTheme.FontBodySmall.Push())
        {
            var versionHeight = ImGui.GetFontSize();
            // 3px nach oben versetzt (Nutzervorgabe), damit die Unterkante mit "Codex" auf einer Höhe ist.
            drawList.AddText(new Vector2(textX + codexWidth + 6f * scale, codexY + largeHeight - versionHeight - 3f * scale),
                ImGui.GetColorU32(CodexTheme.TextDim), versionText);
        }
    }

    private void DrawNavItem(float scale, MenuPage page, FontAwesomeIcon icon, string label, bool showNewBadge = false)
    {
        var selected = activePage == page;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;

        var height = 30f * scale;
        // Rechter Rand genauso groß wie der linke Einzug der Seitenleiste (18px, siehe
        // DrawSidebar/Indent) - symmetrischer Abstand zur Markierung (Nutzervorgabe).
        var width = ImGui.GetContentRegionAvail().X - 18f * scale;
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

        if (showNewBadge)
            DrawNavNewBadge(scale, cursor, width, height);

        if (clicked)
            activePage = page;
    }

    /// <summary>"◆ NEW"-Badge rechtsbündig in einem Menüpunkt (Changelog, Nutzeranforderung) - die
    /// Raute wird gezeichnet statt als Glyph (◆ fehlt im Ingame-Font, erscheint sonst als "?"), 12px
    /// Abstand zum rechten Rand des Menüeintrags.</summary>
    private static void DrawNavNewBadge(float scale, Vector2 itemCursor, float itemWidth, float itemHeight)
    {
        var label = Loc.T("NEU", "NEW");
        float labelWidth, textHeight;
        using (CodexTheme.FontChangelogTag.Push())
        {
            var size = ImGui.CalcTextSize(label);
            labelWidth = size.X;
            textHeight = size.Y;
        }

        var diamondSize = 6f * scale;
        var diamondGap = 5f * scale;
        var padding = new Vector2(7f * scale, 1f * scale);
        var badgeSize = new Vector2(diamondSize + diamondGap + labelWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var badgeCursor = new Vector2(itemCursor.X + itemWidth - 12f * scale - badgeSize.X, itemCursor.Y + (itemHeight - badgeSize.Y) / 2f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(CodexTheme.Accent), 3f * scale);

        var diamondCenter = new Vector2(badgeCursor.X + padding.X + diamondSize / 2f, badgeCursor.Y + badgeSize.Y / 2f);
        CodexWidgets.Diamond(diamondCenter, diamondSize, ImGui.GetColorU32(CodexTheme.TextOnAccent), ImGui.GetColorU32(CodexTheme.TextOnAccent));

        using (CodexTheme.FontChangelogTag.Push())
            drawList.AddText(new Vector2(badgeCursor.X + padding.X + diamondSize + diamondGap, badgeCursor.Y + padding.Y), ImGui.GetColorU32(CodexTheme.TextOnAccent), label);
    }

    /// <summary>Titel+Untertitel des gemeinsamen Seitenkopfs (siehe DrawContent) - je MenuPage, ohne About
    /// (die hat keinen gemeinsamen Seitenkopf). Auch von der eingeklappten Mini-Leiste genutzt (nur der
    /// Titel, siehe DrawCollapsedBrandAndLabel), damit der Seitenname dort nicht separat gepflegt werden muss.</summary>
    private static (string Title, string Subtitle) GetPageHeader(MenuPage page) => page switch
    {
        MenuPage.Overlay => (Loc.T("Overlay", "Overlay"), Loc.T("Passe das Overlay Fenster nach deinen Wünschen an.", "Adjust the overlay window to your liking.")),
        MenuPage.Order => (Loc.T("Reihenfolge", "Order"), Loc.T("Lege fest, in welcher Reihenfolge das Overlay die Kategorien zeigt.", "Arrange the categories in the order the overlay lists them.")),
        MenuPage.Database => (Loc.T("Datenbank", "Database"), Loc.T("Alle Sammelobjekte, die das Plugin in irgendeiner Zone oder einem Dungeon anzeigen würde - mit deinem Status.", "All collectibles this plugin would show in some zone or dungeon - with your status.")),
        MenuPage.Blacklist => (Loc.T("Blacklist", "Blacklist"), Loc.T("Ausgeblendete Einträge erscheinen nie im Overlay und werden von keiner Automation angelaufen.", "Hidden entries never show up in the overlay and are skipped by every automation.")),
        MenuPage.Statistics => (Loc.T("Statistik", "Statistics"), Loc.T("Zählt nur, was der Codex selbst verfolgt – dieselben Kategorien wie im Overlay.", "Counts only what the Codex itself tracks – the same categories you see in the overlay.")),
        MenuPage.Plugins => (Loc.T("Plugins", "Plugins"), Loc.T("Begleit-Plugins, auf die sich der Codex für Automationen stützt.", "Companion plugins the Codex relies on for automation.")),
        MenuPage.Debug => (Loc.T("Debug", "Debug"), Loc.T("Nur relevant, wenn im Overlay etwas nicht wie erwartet angezeigt wird.", "Only relevant if something in the overlay doesn't show as expected.")),
        MenuPage.Log => (Loc.T("Log", "Log"), Loc.T("Die eigenen Log-Zeilen dieses Plugins – durchsuchbar und nach Level filterbar, ohne /xllog zu öffnen.", "This plugin's own log lines – searchable and filterable by level, without opening /xllog.")),
        MenuPage.Changelog => (Loc.T("Änderungsprotokoll", "Changelog"), Loc.T("Was sich im Codex geändert hat – neueste Einträge zuerst.", "What changed in the Codex – newest entries first.")),
        MenuPage.About => (Loc.T("Über", "About"), string.Empty),
        _ => (Loc.T("Allgemein", "General"), Loc.T("Grundeinstellungen des Plugins.", "Basic plugin settings.")),
    };

    /// <summary>Abschnitt 6 - Inhaltsbereich: bisher nur der Seitenkopf (Titel + Untertitel + Trenn-Ornament) für "General", noch ohne eigentliche Einstellungen.
    /// Gibt zurück, ob gerade auf die zentral gezeichneten Einklappen/Schließen-Knöpfe geklickt wurde (siehe deren Kommentar weiter unten).</summary>
    private PluginUiKit.UiWindowControls.ButtonsResult DrawContent(float scale)
    {
        ImGui.BeginChild("##CodexMenuContent", Vector2.Zero, false);
        ImGui.Indent(32f * scale);
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        // Nutzeranforderung: die About-Seite hat KEINEN gemeinsamen Seitenkopf (Titel/Untertitel/
        // Trenn-Ornament) wie die übrigen Seiten - sie zentriert stattdessen ihr eigenes Siegel-Logo/
        // Name/Untertitel/Versions-Etikett/Ornament (siehe DrawAboutPage), daher hier komplett
        // übersprungen.
        if (activePage != MenuPage.About)
        {
            var (pageTitle, pageSubtitle) = GetPageHeader(activePage);

            using (CodexTheme.FontTitleMenu.Push())
                ImGui.TextColored(CodexTheme.TextHeading, pageTitle);

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
            using (CodexTheme.FontMenuSubtitle.Push())
                ImGui.TextColored(CodexTheme.TextSecondary, pageSubtitle);

            // Nochmal 3px nach oben (Nutzervorgabe), insgesamt also bündig ohne zusätzlichen Abstand.
            ImGui.Dummy(new Vector2(0f, 0f * scale));
            CodexTheme.DrawDividerOrnament();

            // Order-Seite enger (Nutzeranforderung: ohne Scrollbar bei inzwischen 17 statt 10 Zeilen).
            ImGui.Dummy(new Vector2(0f, (activePage == MenuPage.Order ? 8f : 16f) * scale));
        }

        switch (activePage)
        {
            case MenuPage.Overlay:
                DrawOverlayPage(scale);
                break;
            case MenuPage.Order:
                DrawOrderPage(scale);
                break;
            case MenuPage.Plugins:
                DrawPluginsPage(scale);
                break;
            case MenuPage.Database:
                DrawDatabasePage(scale);
                break;
            case MenuPage.Blacklist:
                DrawBlacklistPage(scale);
                break;
            case MenuPage.Statistics:
                DrawStatisticsPage(scale);
                break;
            case MenuPage.About:
                DrawAboutPage(scale);
                break;
            case MenuPage.Debug:
                if (Plugin.PluginInterface.IsDev)
                    DrawDebugPage(scale);
                break;
            case MenuPage.Log:
                DrawLogPage(scale);
                break;
            case MenuPage.Changelog:
                DrawChangelogPage(scale);
                break;
            default:
                DrawGeneralPage(scale);
                break;
        }

        ImGui.Unindent(32f * scale);

        // Einklappen/Schließen (Nutzeranforderung) - zentral vom Fenster gezeichnet, NICHT je Seite,
        // ganz rechts in der Kopfzeile, vertikal auf die Mitte des Seitentitels ausgerichtet (keine
        // der aktuellen Codex-Seiten hat eigene Reset/Save-Knöpfe, daher hasPageButtons: false - der
        // Trenner dazu entfällt dann laut PluginUiKit.UiWindowControls von selbst). WICHTIG: innerhalb
        // DIESES Childs gezeichnet (nicht erst danach im Fenster) - ein außerhalb eines Childs
        // gezeichnetes Item wird von ImGui für Hover/Klick NICHT als "vor" diesem Child liegend
        // erkannt, selbst wenn es zeitlich danach gezeichnet wird (das Child "gewinnt" die
        // Maus-Treffererkennung für seine eigene Fläche) - Knopf wäre sichtbar, aber nicht klickbar
        // gewesen (Nutzer-Report).
        var childPos = ImGui.GetWindowPos();
        var childSize = ImGui.GetWindowSize();
        float titleFontHeight;
        using (CodexTheme.FontTitleMenu.Push())
            titleFontHeight = ImGui.GetFontSize();
        var headerButtonsCenterY = childPos.Y + (28f + titleFontHeight / 2f) * scale;
        var headerButtonsRightEdge = childPos.X + childSize.X - 18f * scale;

        var controls = PluginUiKit.UiWindowControls.DrawExpandedButtons(scale, headerButtonsRightEdge, headerButtonsCenterY, hasPageButtons: false,
            collapseTooltip: Loc.T("Einklappen", "Collapse"), closeTooltip: Loc.T("Schließen", "Close"));

        ImGui.EndChild();
        return controls;
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
        // Nutzervorgabe: derselbe Abstand wie zwischen Seitenleiste und der linken Spalte (siehe
        // Indent(32px) in DrawContent) sowohl ZWISCHEN den beiden Spalten als auch RECHTS neben der
        // rechten Spalte (vorher lief die rechte Spalte bis zum Fensterrand durch, ohne passenden
        // Rand wie links).
        var gap = 32f * scale;
        var rightMargin = 32f * scale;
        var columnWidth = (availWidth - gap - rightMargin) / 2f;

        ImGui.BeginChild("##CodexGeneralLeft", new Vector2(columnWidth, 0f), false);
        DrawLanguageCard(scale, config);
        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawAutomationCard(scale, config);
        ImGui.EndChild();

        ImGui.SameLine(0f, gap);

        ImGui.BeginChild("##CodexGeneralRight", new Vector2(columnWidth, 0f), false);
        DrawQualityOfLifeCard(scale, config);
        ImGui.EndChild();
    }

    /// <summary>Seite "Overlay" (Nutzeranforderung) - die vorher auf "Allgemein" enthaltene Overlay-Karte, jetzt auf einer eigenen Seite. Gleiche Kartenbreite wie auf "Allgemein" (halbe Seite), statt die Karte auf die volle Breite zu strecken.</summary>
    private void DrawOverlayPage(float scale)
    {
        var config = plugin.Configuration;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var gap = 32f * scale;
        var rightMargin = 32f * scale;
        var columnWidth = (availWidth - gap - rightMargin) / 2f;

        ImGui.BeginChild("##CodexOverlayPageLeft", new Vector2(columnWidth, 0f), false);
        DrawOverlayCard(scale, config);
        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawDisplayCard(scale, config);
        ImGui.EndChild();

        ImGui.SameLine(0f, gap);

        ImGui.BeginChild("##CodexOverlayPageRight", new Vector2(columnWidth, 0f), false);
        CodexTheme.BeginCard(scale);
        DrawOverlayPreview(scale, config, ImGui.GetContentRegionAvail().X);
        CodexTheme.EndCard();
        ImGui.EndChild();
    }

    // Index der gerade per Drag gezogenen Zeile (siehe DrawOrderCategoryRow) - null, solange nicht gezogen wird.
    private int? orderDraggingIndex;

    // Pluralform je Kategorie NUR für den Zeilennamen dieser Seite (Nutzeranforderung, Referenzbild
    // zeigt "Mounts"/"Minions"/... statt der sonst überall verwendeten Singularform aus Loc.TypeName,
    // z.B. für Filter-Popups) - bewusst NICHT Loc.TypeName selbst geändert, das an vielen anderen
    // Stellen (Blacklist-Filter, altes Overlay) weiterhin die Singularform braucht.
    private static string GetOrderPageCategoryName(CollectibleType type) => type switch
    {
        CollectibleType.Mount => Loc.T("Mounts", "Mounts"),
        CollectibleType.Minion => Loc.T("Minions", "Minions"),
        CollectibleType.Orchestrion => Loc.T("Orchestrionrollen", "Orchestrion Rolls"),
        CollectibleType.TripleTriadCard => Loc.T("Triple-Triad-Karten", "Triple Triad Cards"),
        CollectibleType.Emote => Loc.T("Emotes", "Emotes"),
        CollectibleType.Hairstyle => Loc.T("Moderne Ästhetik", "Hairstyles"),
        CollectibleType.FashionAccessory => Loc.T("Accessoires", "Fashion Accessories"),
        CollectibleType.Barding => Loc.T("Bardierungen", "Bardings"),
        CollectibleType.Facewear => Loc.T("Brillen", "Glasses"),
        CollectibleType.FrameKit => Loc.T("Framer's Kits", "Framer's Kits"),
        CollectibleType.Quest => Loc.T("Quests", "Quests"),
        CollectibleType.AetherCurrent => Loc.T("Ätherströmungen", "Aether Currents"),
        CollectibleType.Sightseeing => Loc.T("Sightseeing-Punkte", "Sightseeing Spots"),
        CollectibleType.HuntingLog => Loc.T("Hunting Log", "Hunting Log"),
        CollectibleType.Aetheryte => Loc.T("Ätheryten", "Aetherytes"),
        CollectibleType.Chocobokeep => Loc.T("Chocobokeep", "Chocobokeep"),
        CollectibleType.Achievement => Loc.T("Errungenschaften", "Achievements"),
        _ => Loc.TypeName(type),
    };

    /// <summary>Seite "Reihenfolge" (Nutzeranforderung) - legt fest, in welcher Reihenfolge die Sammel-Kategorien im Overlay erscheinen (siehe Configuration.OrderPageCategories/SetOrderPageCategoryOrder).</summary>
    private void DrawOrderPage(float scale)
    {
        var width = ImGui.GetContentRegionAvail().X - 32f * scale;
        if (ImGui.BeginTable("##CodexOrderPageWidth", 1, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableNextColumn();
            DrawOrderCategoriesCard(scale, width);
            ImGui.EndTable();
        }
    }

    private void DrawOrderCategoriesCard(float scale, float width)
    {
        var config = plugin.Configuration;
        var order = config.GetOrderPageCategoryOrder();

        CodexTheme.BeginCard(scale, width: width);
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;
        var rowStartX = ImGui.GetCursorPosX();

        // Kartenkopf: Titel links, Hinweistext rechtsbündig auf derselben Zeile (Nutzeranforderung:
        // keine Trennlinie mehr darunter - die Zeilen selbst haben schon LineSubtle-Trenner).
        var titleY = ImGui.GetCursorPosY();
        using (CodexTheme.FontCardTitle.Push())
            ImGui.TextColored(CodexTheme.TextCardTitle, Loc.T("Kategorien", "Categories").ToUpperInvariant());

        var hint = Loc.T("Ziehen ⠿ oder Pfeile nutzen", "Drag ⠿ or use the arrows");
        float hintWidth;
        using (CodexTheme.FontOrderHint.Push())
            hintWidth = ImGui.CalcTextSize(hint).X;
        ImGui.SetCursorPos(new Vector2(rowStartX + contentWidth - hintWidth, titleY + 6f * scale));
        using (CodexTheme.FontOrderHint.Push())
            ImGui.TextColored(CodexTheme.TextDim, hint);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        for (var i = 0; i < order.Count; i++)
        {
            DrawOrderCategoryRow(scale, contentWidth, order, i);
            if (i < order.Count - 1)
            {
                var dividerCursor = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddLine(dividerCursor, dividerCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
            }
        }

        CodexTheme.EndCard();
    }

    private const string OrderDragDropPayloadId = "CodexOrderCategory";

    private void DrawOrderCategoryRow(float scale, float contentWidth, List<CollectibleType> order, int index)
    {
        var type = order[index];
        var rowHeight = 30f * scale;
        var rowStartScreenPos = ImGui.GetCursorScreenPos();
        var rowStartY = ImGui.GetCursorPosY();
        var rowStartX = ImGui.GetCursorPosX();

        // Pfeil-Spalte vorab berechnen, damit die Drag-Zeilen-Fläche (unten) exakt davor aufhört -
        // so überlappt sie die Pfeil-Buttons gar nicht erst (ImGui blockt Klicks für spätere Items,
        // die eine frühere überlappende Fläche treffen, ansonsten zuverlässig ab).
        var arrowSize = new Vector2(24f * scale, 24f * scale);
        var arrowsWidth = arrowSize.X * 2f + 6f * scale;
        var arrowsX = rowStartX + contentWidth - arrowsWidth;
        var dragAreaWidth = arrowsX - rowStartX - 4f * scale;

        // Zeile (bis kurz vor die Pfeile) als Drag-Quelle/-Ziel - der sichtbare Griff ist nur der
        // Hinweis, nicht die einzige ziehbare Fläche (großzügiger als "nur die 6 Punkte treffen").
        ImGui.SetCursorPos(new Vector2(rowStartX, rowStartY));
        ImGui.InvisibleButton($"##CodexOrderRow{type}", new Vector2(dragAreaWidth, rowHeight));
        var rowHovered = ImGui.IsItemHovered();
        if (rowHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.BeginDragDropSource())
        {
            orderDraggingIndex = index;
            ImGui.SetDragDropPayload(OrderDragDropPayloadId, ReadOnlySpan<byte>.Empty);
            using (CodexTheme.FontOrderName.Push())
                ImGui.TextColored(CodexTheme.TextPrimary, GetOrderPageCategoryName(type));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            // Während des Ziehens: 2px Accent-Linie an der Einfügeposition (oberhalb dieser Zeile).
            var insertLineCursor = rowStartScreenPos;
            ImGui.GetWindowDrawList().AddLine(insertLineCursor, insertLineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.Accent), 2f);

            var payload = ImGui.AcceptDragDropPayload(OrderDragDropPayloadId);
            if (!payload.IsNull && orderDraggingIndex is { } sourceIndex && sourceIndex != index)
            {
                var moved = order[sourceIndex];
                order.RemoveAt(sourceIndex);
                // Lag das gezogene Element VOR dem Ziel, rückt durch das RemoveAt alles danach um
                // einen Platz nach vorne - Zielindex entsprechend um 1 verringern.
                var insertIndex = sourceIndex < index ? index - 1 : index;
                order.Insert(insertIndex, moved);
                plugin.Configuration.SetOrderPageCategoryOrder(order);
                orderDraggingIndex = null;
            }

            ImGui.EndDragDropTarget();
        }

        // Gezogene Zeile selbst hervorgehoben (BgSelected).
        if (orderDraggingIndex == index)
            ImGui.GetWindowDrawList().AddRectFilled(rowStartScreenPos, rowStartScreenPos + new Vector2(contentWidth, rowHeight), ImGui.GetColorU32(CodexTheme.BgSelected));

        // Griff: 6 Punkte (2 Spalten x 3 Reihen), TextDisabled.
        var handleWidth = 14f * scale;
        var dotRadius = 1.6f * scale;
        var handleCenter = rowStartScreenPos + new Vector2(handleWidth / 2f, rowHeight / 2f);
        var dotColor = ImGui.GetColorU32(CodexTheme.TextDisabled);
        var drawList = ImGui.GetWindowDrawList();
        for (var col = -1; col <= 1; col += 2)
        {
            for (var row = -1; row <= 1; row++)
                drawList.AddCircleFilled(handleCenter + new Vector2(col * 3.5f * scale, row * 5f * scale), dotRadius, dotColor);
        }

        var cursorX = rowStartX + handleWidth + 10f * scale;

        // Position (rechtsbündig in einer 18px-Spalte), 1-basiert.
        var positionColumnWidth = 18f * scale;
        var positionText = (index + 1).ToString(CultureInfo.InvariantCulture);
        float positionTextWidth, positionTextHeight;
        using (CodexTheme.FontOrderPosition.Push())
        {
            var size = ImGui.CalcTextSize(positionText);
            positionTextWidth = size.X;
            positionTextHeight = size.Y;
        }
        ImGui.SetCursorPos(new Vector2(cursorX + positionColumnWidth - positionTextWidth, rowStartY + (rowHeight - positionTextHeight) / 2f));
        using (CodexTheme.FontOrderPosition.Push())
            ImGui.TextColored(CodexTheme.TextDim, positionText);
        cursorX += positionColumnWidth + 14f * scale;

        // Typ-Badge (92px, Farben/Label wie im Overlay).
        var (badgeBg, badgeFg) = CodexOverlayWindow.GetTypeBadgeColors(type);
        var badgeLabel = CodexOverlayWindow.GetBadgeLabel(type);
        float badgeFontHeight;
        using (CodexTheme.FontTypeBadge.Push())
            badgeFontHeight = ImGui.GetFontSize();
        var badgeSize = new Vector2(92f * scale, badgeFontHeight + 4f * scale);
        var badgeCursor = rowStartScreenPos + new Vector2(cursorX - rowStartX, (rowHeight - badgeSize.Y) / 2f);
        drawList.AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(badgeBg), CodexTheme.RoundingControl);
        ImGui.SetCursorScreenPos(badgeCursor);
        CodexOverlayWindow.CenteredText(badgeLabel, badgeSize, badgeFg, false);
        cursorX += badgeSize.X + 14f * scale;

        // Name (Stretch, bis kurz vor die Pfeile - arrowSize/arrowsX bereits oben berechnet).
        var nameWidth = arrowsX - 8f * scale - cursorX;
        using (CodexTheme.FontOrderName.Push())
        {
            ImGui.PushTextWrapPos(cursorX + MathF.Max(0f, nameWidth));
            ImGui.SetCursorPos(new Vector2(cursorX, rowStartY + (rowHeight - ImGui.GetFontSize()) / 2f));
            ImGui.TextColored(CodexTheme.TextPrimary, GetOrderPageCategoryName(type));
            ImGui.PopTextWrapPos();
        }

        DrawOrderArrowButton(scale, rowStartScreenPos + new Vector2(arrowsX - rowStartX, (rowHeight - arrowSize.Y) / 2f), arrowSize,
            FontAwesomeIcon.ChevronUp, index > 0, () => ReorderOrderCategory(order, index, index - 1));
        DrawOrderArrowButton(scale, rowStartScreenPos + new Vector2(arrowsX - rowStartX + arrowSize.X + 6f * scale, (rowHeight - arrowSize.Y) / 2f), arrowSize,
            FontAwesomeIcon.ChevronDown, index < order.Count - 1, () => ReorderOrderCategory(order, index, index + 1));
    }

    private void ReorderOrderCategory(List<CollectibleType> order, int fromIndex, int toIndex)
    {
        (order[fromIndex], order[toIndex]) = (order[toIndex], order[fromIndex]);
        plugin.Configuration.SetOrderPageCategoryOrder(order);
    }

    private void DrawOrderArrowButton(float scale, Vector2 screenPos, Vector2 size, FontAwesomeIcon icon, bool enabled, System.Action onClick)
    {
        ImGui.SetCursorScreenPos(screenPos);
        var id = $"##CodexOrderArrow{icon}{screenPos.X}{screenPos.Y}";
        if (!enabled)
            ImGui.BeginDisabled();

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var fg = enabled ? (hovered ? CodexTheme.TextHeading : CodexTheme.TextSecondary) : CodexTheme.TextDisabled;
        drawList.AddRect(screenPos, screenPos + size, ImGui.GetColorU32(CodexTheme.LineControl), CodexTheme.RoundingControl);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(screenPos + (size - glyphSize) / 2f, ImGui.GetColorU32(fg), glyph);
        }

        if (!enabled)
            ImGui.EndDisabled();
        else if (clicked)
            onClick();
    }

    /// <summary>
    /// Live-Vorschau des neuen Overlays (Nutzeranforderung) - MIT Dummy-Daten (keine echten
    /// Zonen-/Spielerdaten), damit sie unabhängig von der aktuellen Zone immer gleich aussieht und
    /// sich gefahrlos aktualisieren lässt. Reagiert auf dieselben Configuration-Schalter wie das
    /// echte Overlay (Auto-Knöpfe/Währungen ein-/ausblenden), zeigt aber bewusst KEINE echten
    /// Automationen/Items - das wäre angesichts von Live-Spielzustand (Zonenwechsel, Netzwerk-IPC zu
    /// Questionable usw.) in einer Menü-Vorschau weder sinnvoll noch sicher nachstellbar. Eigenständig
    /// per CodexTheme-Tokens nachgebaut (nicht CodexOverlayWindow.Draw() direkt wiederverwendet, das
    /// untrennbar an echte Plugin-/Zonendaten gekoppelt ist), mit eigenem, kleinerem Skalierungsfaktor
    /// (previewScale), damit die Vorschau in die Kartenspalte passt.
    /// </summary>
    private void DrawOverlayPreview(float scale, Configuration config, float columnWidth)
    {
        // Nutzeranforderung: Eindruck, als würde man "von weiter weg" auf das Overlay schauen - die
        // ganze Box (nicht nur der Inhalt, siehe ContentZoomOutFactor in DrawOverlayPreviewWindow)
        // wird dafür kleiner und mittig mit sichtbarem Freiraum drumherum dargestellt.
        // Nutzeranforderung: etwas breiter als zuvor (0.65 -> 0.75 -> 0.8), Zoom-Mechanik unverändert.
        const float OuterZoomOutFactor = 0.8f;
        var maxWidth = MathF.Min(columnWidth, CodexTheme.OverlayWidth * scale) * OuterZoomOutFactor;
        var previewScale = maxWidth / CodexTheme.OverlayWidth;
        var previewWidth = CodexTheme.OverlayWidth * previewScale;
        var centerOffset = MathF.Max(0f, (columnWidth - previewWidth) / 2f);

        // Nutzervorgabe: gleiche Titelgröße/-schrift wie die anderen Kartentitel (z.B. "OVERLAY" in
        // der linken Box), aber ohne die Trennlinie von CardGroupLabel (Nutzeranforderung: entfernt).
        using (CodexTheme.FontCardTitle.Push())
            CodexTheme.TextShadowed(Loc.T("VORSCHAU", "PREVIEW").ToUpperInvariant(), CodexTheme.TextTertiary, false);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        // Nutzeranforderung: keine eigene Sprachauswahl mehr für die Vorschau - sie folgt immer der
        // tatsächlich aktuell eingestellten Spielsprache (wie das echte Overlay, siehe
        // CodexOverlayWindow), unabhängig von der separat einstellbaren Menüsprache
        // (config.MenuLanguage). Dafür wird ein eventuell aktives Menüsprachen-Override hier gezielt
        // aufgehoben.
        var previousOverride = Loc.MenuLanguageOverride;
        Loc.MenuLanguageOverride = null;
        try
        {
            if (centerOffset > 0f)
                ImGui.Indent(centerOffset);
            DrawOverlayPreviewWindow(previewScale, config, previewWidth);
            if (centerOffset > 0f)
                ImGui.Unindent(centerOffset);
        }
        finally
        {
            Loc.MenuLanguageOverride = previousOverride;
        }
    }


    /// <summary>
    /// Gesamthöhe der Vorschau-Box. Feste Höhe statt automatischer Anpassung (dieses ImGui-Binding
    /// kennt kein ImGuiChildFlags.AutoResizeY, siehe CodexTheme.BeginCard-Kommentar) - unkritisch, da
    /// die Item-Liste selbst (anders als vorher) in einer eigenen, exakt wie im echten Overlay
    /// (CodexOverlayWindow.DrawList) intern scrollbaren Kindbox fester Höhe sitzt, der Rest der Höhe
    /// also unabhängig von der Dummy-Item-Anzahl konstant bleibt.
    /// </summary>
    private const float PreviewHeight = 640f;

    // Dummy-Automationsknöpfe für die Vorschau - echte Plugin.ZoneAutomationButton-Werte mit
    // No-Op-Start/Stop (die Vorschau soll nichts tatsächlich auslösen), aber ansonsten exakt wie im
    // echten Overlay gezeichnet (siehe CodexOverlayWindow.DrawAutoButton/MeasureAutoButton, jetzt
    // internal static und hier wiederverwendet).
    private static readonly Plugin.ZoneAutomationButton[] PreviewAutoButtons =
    {
        new("quest", 1, true, true, () => { }, () => { }),
        new("tripletriad", 1, false, true, () => { }, () => { }),
    };

    // Dummy-Währungen (Wert, Name, Retainer-/Satteltaschenbestand) für die Vorschau - genau die
    // Währungen, die die PreviewItems unten tatsächlich brauchen (Nutzeranforderung), "0" wird wie im
    // echten Overlay gedämpft/klein dargestellt (siehe CodexOverlayWindow.DrawCurrencyCell).
    // Der Retainer-Bestand (letzter Wert, 0 = keiner) erscheint nur bei aktiviertem Schalter "Show
    // retainer stock" - MGP, Gil und Allagan Tomestones werden (wie im echten Spiel) nie in
    // Retainern/Satteltaschen gelagert, daher dort bewusst 0; Special Brilliant Eggs dagegen schon.
    private static readonly (string Value, string Name, uint RetainerCount)[] PreviewCurrencies =
    {
        ("2,000,000", "MGP", 0u),
        ("0", "Gil", 0u),
        ("12", "Allagan Tomestone of Poetics", 0u),
        ("3", "Special Brilliant Eggs", 10u),
    };

    // Dummy-Items für die Vorschau (Typ, Name, Preis-Betrag, Preis-Währungsname, gesperrt, verlinkt) -
    // je ein Beispiel für Reittier, Begleiter, Orchestrionrolle, Quest und Triple-Triad-Karte
    // (Nutzeranforderung), aus echten Sammelobjekten der Datenbank ausgewählt, plus ein Beispiel mit
    // Special Brilliant Eggs als Preis-Währung. Betrag 0 = kein Preis (Quests haben wie im echten
    // Overlay keinen Währungspreis). "Linked" markiert Beispiele mit Kartenziel (Name erscheint
    // golden, siehe CodexOverlayWindow.DrawList/isLinked) - bei gesperrten Einträgen hat Gedämpft
    // (TextMuted) wie im echten Overlay Vorrang vor Golden.
    private static readonly (CollectibleType Type, string Name, uint Amount, string CurrencyName, bool Locked, bool Linked)[] PreviewItems =
    {
        (CollectibleType.Mount, "Adamantoise", 2_000_000u, "MGP", false, true),
        (CollectibleType.Minion, "Baby Bun", 40_000u, "Gil", false, false),
        (CollectibleType.Orchestrion, "Answers", 12u, "Allagan Tomestone of Poetics", true, false),
        (CollectibleType.Quest, "Hero in Waiting", 0u, "", false, false),
        (CollectibleType.TripleTriadCard, "Gilgamesh", 3_000u, "MGP", false, true),
        (CollectibleType.Minion, "Spring Chick", 3u, "Special Brilliant Eggs", false, false),
    };

    private static readonly Dictionary<string, uint> PreviewCurrencyIconCache = new();

    /// <summary>
    /// Löst den Icon einer Dummy-Währung über den NAMEN direkt aus den echten Spieldaten auf (Lumina
    /// Item-Sheet), statt eine geratene/feste Icon-Id zu hinterlegen - für eine reine Dummy-Vorschau
    /// die robustere Variante (funktioniert unabhängig von zukünftigen Icon-Id-Änderungen im Spiel,
    /// solange sich der Itemname nicht ändert). Pro Name einmalig aufgelöst und gecacht.
    /// </summary>
    private static uint GetPreviewCurrencyIconId(string currencyName)
    {
        if (PreviewCurrencyIconCache.TryGetValue(currencyName, out var cached))
            return cached;

        var iconId = 0u;
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        if (itemSheet != null)
        {
            foreach (var item in itemSheet)
            {
                if (string.Equals(item.Name.ToString(), currencyName, StringComparison.OrdinalIgnoreCase))
                {
                    iconId = item.Icon;
                    break;
                }
            }
        }

        PreviewCurrencyIconCache[currencyName] = iconId;
        return iconId;
    }

    /// <summary>
    /// Baut die Overlay-Vorschau JETZT aus denselben (jetzt internal static) Bausteinen wie das echte
    /// Overlay (siehe CodexOverlayWindow) zusammen, nur mit Dummy-Daten statt echter Plugin-/Zonendaten -
    /// Nutzeranforderung ("identisch wie das Overlay im Spiel, nur mit Dummy-Daten", vorher fehlten
    /// Textformatierungen und die Typ-/Währungs-Filter-Knöpfe, da die Vorschau eine eigenständige,
    /// vereinfachte Nachbildung war). Reagiert weiterhin auf dieselben Configuration-Schalter wie das
    /// echte Overlay (Auto-Knöpfe/Währungen ein-/ausblenden).
    /// </summary>
    /// <summary>
    /// Zoomfaktor für den INHALT der Vorschau (Nutzeranforderung: "ohne Scrollbar sehen können") -
    /// die Box selbst (layoutScale) behält ihre reguläre, am echten Overlay orientierte Größe, nur
    /// Schrift/Icons/Zeilenhöhen darin werden kleiner gezeichnet. Dadurch passt die Item-Liste
    /// (CodexTheme-Komponenten wie im echten Overlay) auch mit allen sechs Beispiel-Einträgen ohne
    /// internen Scrollbalken in die (gleich große) Box - reines Verkleinern von Box UND Inhalt
    /// zusammen hätte am Verhältnis nichts geändert (beides schrumpft sonst gleich stark).
    /// </summary>
    // Nutzeranforderung: nochmal weiter herausgezoomt (0.8 -> 0.65).
    private const float ContentZoomOutFactor = 0.65f;

    private static void DrawOverlayPreviewWindow(float layoutScale, Configuration config, float width)
    {
        var scale = layoutScale * ContentZoomOutFactor;
        var size = new Vector2(width, PreviewHeight * layoutScale);

        // Nutzeranforderung: die Vorschau soll die Auswirkung des Deckkraft-Reglers tatsächlich
        // zeigen - wie das echte Overlay (CodexOverlayWindow.PreDraw) wird der Fensterhintergrund
        // über CodexTheme.OverlayBg(transparency) eingefärbt statt eines festen, immer deckenden
        // Hintergrunds, und der Rahmen verschwindet ab derselben ~70%-Transparenzschwelle.
        var transparency = config.CompactTransparency;
        var shadowActive = transparency >= 0.7f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CodexTheme.OverlayBg(transparency));
        ImGui.PushStyleColor(ImGuiCol.Border, shadowActive ? new Vector4(0f, 0f, 0f, 0f) : CodexTheme.LineFrame);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, shadowActive ? 0f : 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, CodexTheme.RoundingOverlay);
        ImGui.BeginChild("##CodexOverlayPreviewWindow", size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawPreviewHeader(scale);
        DrawPreviewZoneRow(scale);
        DrawPreviewAutoButtonsRow(scale, config);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        ImGui.Separator();

        DrawPreviewCurrencyWallet(scale, config);
        DrawPreviewTabsAndFilters(scale, config);
        DrawPreviewList(scale, layoutScale, config);

        // Zierecken (wie beim echten Overlay) - innerhalb desselben Child-Fensters gezeichnet, per
        // ImGui.GetWindowPos()/GetWindowSize() (siehe CodexTheme.DrawCornerOrnaments-Kommentar), daher
        // VOR EndChild aufgerufen.
        CodexTheme.DrawOverlayCornerOrnaments(CodexTheme.CornerInsetOverlay * scale);

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    /// <summary>Kopfzeile wie CodexOverlayWindow.DrawHeader, aber ohne die echten Klick-/Doppelklick-Verhalten (reine Vorschau) - Icon-Knöpfe über den geteilten CodexOverlayWindow.DrawHeaderIconButton.</summary>
    private static void DrawPreviewHeader(float scale)
    {
        ImGui.Indent(14f * scale);

        var iconSize = 20f * scale;
        var buttonSize = new Vector2(17f * scale, 17f * scale);
        float textHeight;
        using (CodexTheme.FontTitleOverlay.Push())
            textHeight = ImGui.GetFontSize();

        var rowHeight = MathF.Max(iconSize, MathF.Max(buttonSize.Y, textHeight));
        var verticalPadding = 6f * scale;

        ImGui.Dummy(new Vector2(0f, verticalPadding));
        var rowStartY = ImGui.GetCursorPosY();

        ImGui.SetCursorPosY(rowStartY + (rowHeight - iconSize) / 2f);
        CodexTheme.DrawCompassIcon(iconSize);
        ImGui.SameLine(0f, 8f * scale);

        var textDescenderCompensation = textHeight * 0.14f;
        ImGui.SetCursorPosY(rowStartY + (rowHeight - textHeight) / 2f - textDescenderCompensation + 3f * scale);
        using (CodexTheme.FontTitleOverlay.Push())
            ImGui.TextColored(CodexTheme.TextHeading, "THE EXPLORER'S CODEX");

        const float buttonGap = 14f;
        const float rightMargin = 18f;
        var buttonsWidth = buttonSize.X * 3f + buttonGap * scale * 2f;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonsWidth - rightMargin * scale);
        ImGui.SetCursorPosY(rowStartY + (rowHeight - buttonSize.Y) / 2f);

        CodexOverlayWindow.DrawHeaderIconButton(FontAwesomeIcon.LockOpen, "##CodexPreviewLock", buttonSize);
        ImGui.SameLine(0f, buttonGap * scale);
        CodexOverlayWindow.DrawHeaderIconButton(FontAwesomeIcon.ChevronUp, "##CodexPreviewCollapse", buttonSize);
        ImGui.SameLine(0f, buttonGap * scale);
        CodexOverlayWindow.DrawHeaderIconButton(FontAwesomeIcon.Times, "##CodexPreviewClose", buttonSize);

        ImGui.SetCursorPosY(rowStartY + rowHeight);
        ImGui.Unindent(14f * scale);
        ImGui.Dummy(new Vector2(0f, verticalPadding));
        ImGui.Separator();
    }

    private static void DrawPreviewZoneRow(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 0f));
        ImGui.Indent(14f * scale);

        using (CodexTheme.FontZoneName.Push())
            ImGui.TextColored(CodexTheme.TextHeading, Loc.T("Alt-Gridania", "Old Gridania"));
        ImGui.SameLine(0f, 8f * scale);
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3f * scale);
        using (CodexTheme.FontBodySmall.Push())
            ImGui.TextColored(CodexTheme.TextMuted, "Zone 133");

        ImGui.Unindent(14f * scale);
    }

    private static void DrawPreviewAutoButtonsRow(float scale, Configuration config)
    {
        if (!config.ShowAutomationButtons)
            return;

        var anyRunning = PreviewAutoButtons.Any(b => b.IsActive);

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        ImGui.Indent(14f * scale);

        var startX = ImGui.GetCursorPosX();
        var contentMaxX = ImGui.GetWindowContentRegionMax().X - 14f * scale;
        var buttonHeight = CodexOverlayWindow.AutoButtonHeight * scale;
        var cursorX = startX;
        var cursorY = ImGui.GetCursorPosY();

        using (CodexTheme.FontAutoButtonLabel.Push())
        {
            foreach (var button in PreviewAutoButtons)
            {
                var label = CodexOverlayWindow.AutoButtonLabel(button.Key);
                var width = CodexOverlayWindow.MeasureAutoButton(label, button.Count, scale);

                if (cursorX > startX && cursorX + width > contentMaxX)
                {
                    cursorX = startX;
                    cursorY += buttonHeight + CodexOverlayWindow.AutoButtonGap * scale;
                }

                ImGui.SetCursorPos(new Vector2(cursorX, cursorY));
                CodexOverlayWindow.DrawAutoButton(button, label, new Vector2(width, buttonHeight), anyRunning, scale, shadow: false,
                    contentAlpha: 1f - Math.Clamp(config.CompactTransparency, 0f, 1f));

                cursorX += width + CodexOverlayWindow.AutoButtonGap * scale;
            }
        }

        ImGui.SetCursorPos(new Vector2(startX, cursorY + buttonHeight));
        ImGui.Unindent(14f * scale);
    }

    private static void DrawPreviewCurrencyWallet(float scale, Configuration config)
    {
        if (!config.ShowCurrencyWallet)
            return;

        ImGui.Dummy(new Vector2(0f, 5f * scale));
        ImGui.Indent(14f * scale);

        CodexTheme.SectionLabel(Loc.T("DEINE WÄHRUNGEN", "YOUR CURRENCIES"));
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        if (ImGui.BeginTable("##CodexPreviewCurrencyGrid", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("##CodexPreviewCurrencyCol0", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##CodexPreviewCurrencyCol1", ImGuiTableColumnFlags.WidthStretch, 1f);

            for (var i = 0; i < PreviewCurrencies.Length; i++)
            {
                if (i % 2 == 0)
                {
                    ImGui.TableNextRow();
                    if (i > 0)
                        ImGui.Dummy(new Vector2(0f, 5f * scale - ImGui.GetStyle().CellPadding.Y));
                }

                ImGui.TableNextColumn();
                DrawPreviewCurrencyCell(PreviewCurrencies[i], config, scale);
            }

            ImGui.EndTable();
        }

        ImGui.Unindent(14f * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
    }

    /// <summary>Wie CodexOverlayWindow.DrawCurrencyCell, aber mit festen Dummy-Werten statt plugin.GetCurrencyAmount - der Retainer-/Satteltaschenbestand reagiert dafür auf den echten Configuration.ShowRetainerItemCounts-Schalter (Nutzeranforderung "zeigen, wie es aussehen würde").</summary>
    private static void DrawPreviewCurrencyCell((string Value, string Name, uint RetainerCount) currency, Configuration config, float scale)
    {
        var (value, name, retainerCount) = currency;
        var valueColor = value == "0" ? CodexTheme.TextDim : CodexTheme.TextPrimary;

        ImGui.AlignTextToFramePadding();
        using (CodexTheme.FontCurrencyValue.Push())
            ImGui.TextColored(valueColor, value);

        ImGui.SameLine(0f, 6f * scale);
        ImGui.AlignTextToFramePadding();

        var availWidth = ImGui.GetContentRegionAvail().X;
        using (CodexTheme.FontCurrencyName.Push())
        {
            var displayName = name;
            if (ImGui.CalcTextSize(displayName).X > availWidth)
            {
                while (displayName.Length > 1 && ImGui.CalcTextSize(displayName + "…").X > availWidth)
                    displayName = displayName[..^1];
                displayName += "…";
            }

            ImGui.TextColored(CodexTheme.TextMuted, displayName);
            if (displayName != name && ImGui.IsItemHovered())
                ImGui.SetTooltip(name);
        }

        if (!config.ShowRetainerItemCounts || retainerCount == 0)
            return;

        ImGui.SameLine(0f, 2f * scale);
        using (CodexTheme.FontCurrencyName.Push())
            ImGui.TextColored(CodexTheme.TextDim, $"({retainerCount:N0})");
    }

    private static void DrawPreviewTabsAndFilters(float scale, Configuration config)
    {
        var contentAlpha = 1f - Math.Clamp(config.CompactTransparency, 0f, 1f);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.Indent(14f * scale);

        CodexOverlayWindow.DrawTab("zone", Loc.T("Zone", "Zone"), PreviewItems.Length, selected: true, scale, shadow: false, contentAlpha);
        ImGui.SameLine(0f, 18f * scale);
        CodexOverlayWindow.DrawTab("todo", Loc.T("ToDo-Liste", "To-do list"), 0, selected: false, scale, shadow: false, contentAlpha);

        var typeLabel = Loc.T("Typen", "Types");
        var currencyLabel = Loc.T("Währungen", "Currencies");

        float caretWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            caretWidth = ImGui.CalcTextSize(FontAwesomeIcon.CaretDown.ToIconString()).X;

        float typeLabelWidth, currencyLabelWidth;
        using (CodexTheme.FontFilterButton.Push())
        {
            typeLabelWidth = ImGui.CalcTextSize(typeLabel).X;
            currencyLabelWidth = ImGui.CalcTextSize(currencyLabel).X;
        }

        var typeWidth = typeLabelWidth + caretWidth + 18f * scale + 3f * scale;
        var currencyWidth = currencyLabelWidth + caretWidth + 18f * scale + 3f * scale;

        var rightEdge = ImGui.GetWindowContentRegionMax().X - 10f * scale;
        ImGui.SameLine(rightEdge - typeWidth - currencyWidth - 6f * scale);
        CodexOverlayWindow.DrawFilterButton("type", typeLabel, active: false, typeWidth, scale, shadow: false, contentAlpha);
        ImGui.SameLine(0f, 6f * scale);
        CodexOverlayWindow.DrawFilterButton("cur", currencyLabel, active: false, currencyWidth, scale, shadow: false, contentAlpha);

        ImGui.Unindent(14f * scale);
        ImGui.Separator();
    }

    private static void DrawPreviewList(float scale, float layoutScale, Configuration config)
    {
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        // Höhe bewusst an layoutScale (nicht am kleineren Inhalts-"scale") bemessen - siehe
        // ContentZoomOutFactor-Kommentar: der Listenbereich behält seine reguläre Größe, nur die
        // Zeilen darin werden kleiner gezeichnet, wodurch mehr davon hineinpassen.
        ImGui.BeginChild("##CodexPreviewList", new Vector2(0f, 290f * layoutScale), false);
        ImGui.Indent(8f * scale);

        // Wie CodexOverlayWindow.DrawList - reagiert auf dieselbe Zeilenhöhen-Einstellung. Nutzervorgabe:
        // dabei NICHT den Vorschau-Zoomfaktor (ContentZoomOutFactor/OuterZoomOutFactor, in "scale"
        // bereits enthalten) mit einberechnen - die 20-34px sollen unabhängig davon in echten
        // Pixeln (nur die normale UI-Skalierung) dargestellt werden, nicht zusätzlich herunterskaliert.
        // Dasselbe gilt laut Nutzervorgabe jetzt auch für Typ-Badge, Währungsicon und Schloss-Icon.
        var realScale = ImGuiHelpers.GlobalScale;
        float rowContentHeight;
        using (CodexTheme.FontTypeBadge.Push())
            rowContentHeight = ImGui.GetFontSize() + 4f * realScale;
        var rowGap = MathF.Max(0f, (config.OverlayRowHeight * realScale - rowContentHeight) / 2f);

        for (var itemIndex = 0; itemIndex < PreviewItems.Length; itemIndex++)
        {
            var (type, name, amount, currencyName, locked, linked) = PreviewItems[itemIndex];

            if (itemIndex == 0)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 8f * scale);
            else
                ImGui.Dummy(new Vector2(0f, rowGap));
            if (itemIndex > 0)
                ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, rowGap));

            var (badgeBg, badgeFg) = CodexOverlayWindow.GetTypeBadgeColors(type);
            if (locked)
            {
                badgeBg = badgeBg with { W = badgeBg.W * 0.55f };
                badgeFg = badgeFg with { W = badgeFg.W * 0.55f };
            }
            var previewContentAlpha = 1f - Math.Clamp(config.CompactTransparency, 0f, 1f);
            badgeBg = badgeBg with { W = badgeBg.W * previewContentAlpha };

            var badgeLabel = CodexOverlayWindow.GetBadgeLabel(type);

            float badgeFontHeight;
            using (CodexTheme.FontTypeBadge.Push())
                badgeFontHeight = ImGui.GetFontSize();
            var badgeSize = new Vector2(90f * realScale, badgeFontHeight + 4f * realScale);
            var badgeCursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(badgeBg), 3f);
            ImGui.SetCursorScreenPos(badgeCursor);
            CodexOverlayWindow.CenteredText(badgeLabel, badgeSize, badgeFg, shadow: false);

            ImGui.SameLine(0f, 8f * scale);
            ImGui.AlignTextToFramePadding();
            var nameColor = locked ? CodexTheme.TextMuted : linked ? CodexTheme.Accent : CodexTheme.TextPrimary;
            using (CodexTheme.FontBodyBold.Push())
                ImGui.TextColored(nameColor, name);

            if (locked)
            {
                var lockSize = new Vector2(22f * realScale, 22f * realScale);
                ImGui.SameLine(0f, 6f * scale);
                DrawPreviewGateLockButton(lockSize, realScale);
            }

            // Wie CodexOverlayWindow.DrawList (Nutzeranforderung: identisch im Preview abändern) -
            // Betrag + 16x16-Währungsicon statt ausgeschriebenem Namen, beide in einer Gruppe für
            // EINEN Hover-Bereich, Tooltip zeigt Betrag + vollen Währungsnamen.
            if (amount != 0)
            {
                var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
                var amountText = amount.ToString("N0", culture);
                var amountWidth = ImGui.CalcTextSize(amountText).X;
                var iconSize = 16f * realScale;
                var totalWidth = amountWidth + 4f * scale + iconSize;

                ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalWidth - 3f * scale);
                ImGui.AlignTextToFramePadding();
                ImGui.BeginGroup();
                ImGui.TextColored(locked ? CodexTheme.TextDim : CodexTheme.TextMuted, amountText);
                ImGui.SameLine(0f, 4f * scale);
                var iconId = GetPreviewCurrencyIconId(currencyName);
                if (iconId != 0)
                {
                    var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                    ImGui.Image(icon.Handle, new Vector2(iconSize));
                }
                ImGui.EndGroup();

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{amountText} {currencyName}");
            }
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.Unindent(8f * scale);
        ImGui.EndChild();
    }

    /// <summary>Wie CodexOverlayWindow.DrawGateLockButton, aber mit einem festen Beispieltext statt Plugin.GetAchievementOrRankGateReason (es gibt kein echtes CollectibleEntry für ein Dummy-Item).</summary>
    private static void DrawPreviewGateLockButton(Vector2 size, float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##CodexPreviewGateLock", size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(CodexTheme.WarnBg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(CodexTheme.WarnLine), CodexTheme.RoundingControl);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = FontAwesomeIcon.Lock.ToIconString();
            var nativeIconPx = ImGui.GetFontSize();
            ImGui.SetWindowFontScale((nativeIconPx - 2f * scale) / nativeIconPx);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(cursor + new Vector2((size.X - glyphSize.X) / 2f, (size.Y - glyphSize.Y) / 2f), ImGui.GetColorU32(CodexTheme.WarnFg), glyph);
            ImGui.SetWindowFontScale(1f);
        }

        if (hovered)
            ImGui.SetTooltip(Loc.T("Voraussetzung nicht erfüllt", "Requirement not met"));
    }

    private void DrawLanguageCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("SPRACHE", "LANGUAGE"), scale, CodexTheme.CardFieldLineWidth(scale));

        var languageLabels = new (MenuLanguage Language, string Label)[]
        {
            (MenuLanguage.German, "Deutsch"),
            (MenuLanguage.English, "English"),
        };
        var currentLanguageLabel = languageLabels.First(l => l.Language == config.MenuLanguage).Label;

        var open = CodexTheme.BeginDropdownRow(Loc.T("Menüsprache", "Menu language"), currentLanguageLabel, "##CodexMenuLanguage", scale,
            caption: Loc.T("Betrifft nur das Menü, nicht das Overlay.", "Only affects the menu, not the overlay."));
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
        CodexTheme.CardGroupLabel(Loc.T("MODUL", "MODULE"), scale, CodexTheme.CardFieldLineWidth(scale));

        var showOverlay = config.ShowCompactOverlay;
        if (CodexTheme.ToggleRow("##CodexShowOverlay", Loc.T("Overlay aktivieren", "Enable overlay"), ref showOverlay, scale))
        {
            config.ShowCompactOverlay = showOverlay;
            plugin.CodexOverlayWindow.IsOpen = showOverlay;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showAutomationButtons = config.ShowAutomationButtons;
        if (CodexTheme.ToggleRow("##CodexShowAutomationButtons", Loc.T("Automation-Knöpfe", "Automation buttons"), ref showAutomationButtons, scale))
        {
            config.ShowAutomationButtons = showAutomationButtons;
            config.Save();
        }

        CodexTheme.CardDivider(scale);

        var showWallet = config.ShowCurrencyWallet;
        if (CodexTheme.ToggleRow("##CodexShowCurrencyWallet", Loc.T("Währungen", "Currencies"), ref showWallet, scale))
        {
            config.ShowCurrencyWallet = showWallet;
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
        if (CodexTheme.ToggleRow("##CodexShowRetainerItemCounts", Loc.T("Retainer-Bestand", "Retainer stock"), ref showRetainerItemCounts, scale,
                // "Requires Allagan Tools." bewusst mit explizitem Zeilenumbruch davor (Nutzervorgabe:
                // soll immer zusammen stehen, notfalls in eigener Zeile) - ohne das erzwungene \n
                // könnte der automatische Textumbruch (siehe ToggleRow) die Phrase sonst mittendrin
                // trennen.
                caption: Loc.T("Zeigt an, ob die Retainer benötigte Währungen besitzen.\nRequires Allagan Tools.",
                    "Zeigt an, ob die Retainer benötigte Währungen besitzen.\nRequires Allagan Tools.")))
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

        CodexTheme.EndCard();
    }

    // Siehe DrawDisplayCard - true, solange der Deckkraft- bzw. Zeilenhöhen-Wert seit dem letzten
    // Save() geändert wurde, aber noch nicht persistiert ist (wird erst beim Loslassen des jeweiligen
    // Reglers gespeichert).
    private static bool opacityDirty;
    private static bool rowHeightDirty;

    /// <summary>
    /// Neue Karte unterhalb von "Modul" (Nutzeranforderung) - aktuell nur die Deckkraft des Overlays
    /// (config.CompactTransparency, geteilt mit dem alten Overlay). Die Konfiguration speichert eine
    /// TRANSPARENZ (0 = voll deckend, 1 = unsichtbar, siehe CodexTheme.OverlayBg), die Oberfläche
    /// zeigt laut Entwurfsbild aber eine DECKKRAFT in Prozent (100% = voll deckend) - exakt umgekehrt,
    /// daher die Umrechnung in beide Richtungen.
    /// </summary>
    private static void DrawDisplayCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);

        const float sliderWidthPx = 205f;
        const float rightMarginPx = 25f;
        const float gapPx = 8f;

        // Prozenttext bereits hier gemessen (statt erst weiter unten), damit die Trennlinie
        // (CardGroupLabel) bis zum %-Zeichen reicht (Nutzervorgabe) statt nur bis zum Slider-Ende.
        var opacity01 = 1f - config.CompactTransparency;
        var percentText = $"{opacity01 * 100f:0}%";
        float percentWidth, percentHeight;
        using (CodexTheme.FontMenuDropdownValue.Push())
        {
            var percentSize = ImGui.CalcTextSize(percentText);
            percentWidth = percentSize.X;
            percentHeight = percentSize.Y;
        }

        var gap = gapPx * scale;
        var sliderWidth = sliderWidthPx * scale;
        // Der Prozentwert endet am üblichen 25px-Randabstand (wie Dropdown/Toggle) - die Linie muss
        // also bis genau dorthin reichen, nicht nur bis zum Ende des (weiter links sitzenden)
        // Sliders. CardFieldLineWidth(scale) mit dem Standard-rightMargin trifft exakt diesen Punkt
        // (Bugfix: vorher wurde "sliderWidth + gap + percentWidth" als Linienlänge verwendet, die ab
        // dem CARD-Rand gemessen wird, nicht ab der weiter rechts liegenden Slider-Startposition -
        // dadurch war die Linie deutlich zu kurz).
        CodexTheme.CardGroupLabel(Loc.T("DARSTELLUNG", "DISPLAY"), scale, CodexTheme.CardFieldLineWidth(scale));

        // Nutzervorgabe: Label und Slider in derselben Zeile (wie bei den Dropdown-/Toggle-Zeilen),
        // Slider-Breite identisch zur Combobox-Breite (205px), Prozentwert sichtbar daneben (wie beim
        // vorherigen nativen ImGui-Regler) - der Slider rutscht dafür entsprechend weiter nach links,
        // während der Prozentwert selbst am üblichen 25px-Randabstand endet.
        var rowStartY = ImGui.GetCursorPosY();
        using (CodexTheme.FontMenuFieldLabel.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, Loc.T("Deckkraft", "Opacity"));
        var textBottomY = ImGui.GetCursorPosY();
        var textHeight = textBottomY - rowStartY;

        // 5px niedriger als zuvor (Nutzervorgabe).
        var sliderHeight = 17f * scale;
        var sliderY = rowStartY + (textHeight - sliderHeight) / 2f;
        var sliderX = ImGui.GetWindowContentRegionMax().X - rightMarginPx * scale - percentWidth - gap - sliderWidth;

        ImGui.SetCursorPos(new Vector2(sliderX, sliderY));
        // config.Save() NICHT bei jedem einzelnen Frame während des Ziehens aufrufen (Nutzer-Report:
        // "database is locked" - SQLiteException, da Dalamuds ReliableFileStorage jeden Save() als
        // eigenen asynchronen SQLite-Schreibvorgang behandelt; beim Ziehen über viele Werte hinweg
        // feuert das dutzende Male pro Sekunde und die Schreibvorgänge überlappen sich). Stattdessen
        // wird der Wert live im Speicher aktualisiert (für sofortiges visuelles Feedback), aber erst
        // EINMAL gespeichert, sobald der Schieberegler losgelassen wird (ImGui.IsItemDeactivated()
        // funktioniert auch für dieses selbst gezeichnete InvisibleButton-Steuerelement, unabhängig
        // von ImGuis interner "edited"-Markierung, die eigene Widgets wie SliderBehavior intern setzen
        // und die unser manueller Code nicht auslöst).
        if (CodexTheme.OpacitySlider("##CodexOverlayOpacity", ref opacity01, new Vector2(sliderWidth, sliderHeight)))
        {
            config.CompactTransparency = 1f - opacity01;
            opacityDirty = true;
        }
        if (opacityDirty && ImGui.IsItemDeactivated())
        {
            config.Save();
            opacityDirty = false;
        }

        var percentY = rowStartY + (textHeight - percentHeight) / 2f;
        ImGui.SetCursorPos(new Vector2(sliderX + sliderWidth + gap, percentY));
        using (CodexTheme.FontMenuDropdownValue.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, percentText);

        ImGui.SetCursorPosY(MathF.Max(textBottomY, sliderY + sliderHeight));

        CodexTheme.CardDivider(scale);

        // Nutzeranforderung: Zeilenhöhe der Item-Liste, 20-34px (34 = bisherige/aktuelle Höhe, siehe
        // Configuration.OverlayRowHeight) - sonst exakt dasselbe Zeilen-Layout wie "Deckkraft" oben.
        const float rowHeightMin = 20f;
        const float rowHeightMax = 34f;

        var rowHeightLabelText = $"{config.OverlayRowHeight:0} px";
        float rowHeightLabelWidth, rowHeightLabelHeight;
        using (CodexTheme.FontMenuDropdownValue.Push())
        {
            var labelSize = ImGui.CalcTextSize(rowHeightLabelText);
            rowHeightLabelWidth = labelSize.X;
            rowHeightLabelHeight = labelSize.Y;
        }

        var rowHeightRowStartY = ImGui.GetCursorPosY();
        using (CodexTheme.FontMenuFieldLabel.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, Loc.T("Zeilenhöhe", "Row height"));
        var rowHeightTextBottomY = ImGui.GetCursorPosY();
        var rowHeightTextHeight = rowHeightTextBottomY - rowHeightRowStartY;

        var rowHeightSliderY = rowHeightRowStartY + (rowHeightTextHeight - sliderHeight) / 2f;
        var rowHeightSliderX = ImGui.GetWindowContentRegionMax().X - rightMarginPx * scale - rowHeightLabelWidth - gap - sliderWidth;

        ImGui.SetCursorPos(new Vector2(rowHeightSliderX, rowHeightSliderY));
        var rowHeight01 = (config.OverlayRowHeight - rowHeightMin) / (rowHeightMax - rowHeightMin);
        if (CodexTheme.OpacitySlider("##CodexOverlayRowHeight", ref rowHeight01, new Vector2(sliderWidth, sliderHeight)))
        {
            config.OverlayRowHeight = rowHeightMin + rowHeight01 * (rowHeightMax - rowHeightMin);
            rowHeightDirty = true;
        }
        if (rowHeightDirty && ImGui.IsItemDeactivated())
        {
            config.Save();
            rowHeightDirty = false;
        }

        var rowHeightLabelY = rowHeightRowStartY + (rowHeightTextHeight - rowHeightLabelHeight) / 2f;
        ImGui.SetCursorPos(new Vector2(rowHeightSliderX + sliderWidth + gap, rowHeightLabelY));
        using (CodexTheme.FontMenuDropdownValue.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, rowHeightLabelText);

        ImGui.SetCursorPosY(MathF.Max(rowHeightTextBottomY, rowHeightSliderY + sliderHeight));

        CodexTheme.EndCard();
    }

    private static void DrawQualityOfLifeCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("LEBENSQUALITÄT", "QUALITY OF LIFE"), scale, CodexTheme.CardFieldLineWidth(scale));

        var showNavigationArrow = config.ShowNavigationArrow;
        if (CodexTheme.ToggleRow("##CodexShowNavigationArrow", Loc.T("Wegweiser-Pfeil anzeigen", "Show navigation arrow"), ref showNavigationArrow, scale,
                caption: Loc.T("Zeigt einen verschiebbaren Pfeil zum aktuellen Ziel.",
                    "Shows a movable arrow pointing to the current target.")))
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
                caption: Loc.T("Aktiviert die Möglichkeit, mehr Informationen zu Items/Währungen zu bekommen.",
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

        CodexTheme.CardDivider(scale);

        var notifyChangelog = config.NotifyChangelogInChat;
        if (CodexTheme.ToggleRow("##CodexNotifyChangelog", Loc.T("Nach Updates im Chat hinweisen", "Notify in chat after updates"), ref notifyChangelog, scale))
        {
            config.NotifyChangelogInChat = notifyChangelog;
            config.Save();
        }

        CodexTheme.EndCard();
    }

    private void DrawAutomationCard(float scale, Configuration config)
    {
        CodexTheme.BeginCard(scale);
        CodexTheme.CardGroupLabel(Loc.T("AUTOMATION", "AUTOMATION"), scale, CodexTheme.CardFieldLineWidth(scale));

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
        // Nutzeranforderung: nur ausgrauen, wenn GAR KEIN Kampf-Plugin installiert ist - bei genau
        // einem installierten Plugin soll die Auswahl trotzdem normal (nicht ausgegraut) angezeigt
        // werden, auch wenn es dort nichts zur Auswahl gibt.
        var pickerEnabled = installed.Count > 0;

        var currentLabel = effective is { } current
            ? CombatPluginBridge.DisplayName(current)
            : Loc.T("Keines installiert", "None installed");

        var open = CodexTheme.BeginDropdownRow(Loc.T("Kampf-Plugin", "Combat plugin"), currentLabel, "##CodexCombatPlugin", scale,
            Loc.T("Welches Plugin bei der Hunting-Log-Automation (und kampfpflichtigen Quest-Schritten) den Kampf übernimmt.",
                "Which plugin handles combat during the hunting log automation (and combat-required quest steps)."),
            disabled: !pickerEnabled);
        if (open)
        {
            // Nutzeranforderung: nur tatsächlich installierte Kampf-Plugins zur Auswahl anbieten,
            // statt alle bekannten Plugins zu listen und die nicht installierten nur auszugrauen.
            foreach (var kind in installed)
            {
                if (ImGui.Selectable(CombatPluginBridge.DisplayName(kind), effective == kind) && config.CombatPlugin != kind)
                {
                    config.CombatPlugin = kind;
                    config.Save();
                }
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
                caption: Loc.T("Lässt die Quest- und Hunting-Log-Automation den Chocobo-Begleiter beschwören und am Leben erhalten.",
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

    // ---- Seite "Plugins" (Nutzeranforderung) - zeigt dieselbe Prüf-/Install-Logik wie
    // MainWindow.DrawDependenciesPage (Plugin.Dependencies/IsPluginLoaded/IsDependencySatisfied/
    // HasMissingRequiredDependency, dafür dort auf internal gestellt), nur komplett neu im
    // Explorer-Codex-Theme gestaltet. "Check again" löst keine eigene Methode aus, da die Prüfung
    // ohnehin bei jedem Draw-Aufruf frisch ausgewertet wird (siehe DrawCheckAgainButton-Kommentar).

    /// <summary>Kurze, für diese Seite neu formulierte Beschreibungen (Nutzeranforderung) - bewusst NICHT die längeren, technischeren Plugin.Dependencies-Beschreibungen (die bleiben der alten Seite vorbehalten).</summary>
    private static (string De, string En) GetShortPluginDescription(string internalName) => internalName switch
    {
        "vnavmesh" => ("Navigation & Pfadfindung", "Navigation & pathing"),
        "Questionable" => ("Quest-Automation", "Quest automation"),
        "Lifestream" => ("Teleport & Reisen", "Teleport & travel"),
        "Saucy" => ("Triple-Triad-Automation", "Triple Triad Automation"),
        "TextAdvance" => ("Dialoge automatisch weiterklicken", "Auto-advance dialogue"),
        "InventoryTools" => ("Retainer-Bestände und mehr Informationen zu Items", "Retainer stock and more info on items"),
        _ => (string.Empty, string.Empty),
    };

    /// <summary>
    /// Rechter Randabstand der ganzen Plugins-Seite (Nutzeranforderung: derselbe Freiraum rechts wie
    /// links vor den Boxen, siehe DrawContent.Indent(32px)/DrawCloseButton.rightMargin) - gilt für
    /// Statusleiste UND alle drei Gruppen gleichermaßen, ersetzt die vorherige willkürliche
    /// 20%-Verschmälerung von Required/Optional.
    /// </summary>
    private const float PluginsPageRightMargin = 32f;

    private void DrawPluginsPage(float scale)
    {
        var width = ImGui.GetContentRegionAvail().X - PluginsPageRightMargin * scale;
        DrawPluginsStatusBanner(scale, width);
        ImGui.Dummy(new Vector2(0f, 18f * scale));
        DrawCombatPluginGroup(scale, width);
        ImGui.Dummy(new Vector2(0f, 18f * scale));
        DrawRequiredPluginsGroup(scale, width);
        ImGui.Dummy(new Vector2(0f, 18f * scale));
        DrawOptionalPluginsGroup(scale, width);
    }

    private static void DrawPluginsStatusBanner(float scale, float width)
    {
        var total = Plugin.Dependencies.Length;
        var installedCount = Plugin.Dependencies.Count(d => Plugin.IsPluginLoaded(d.InternalName));
        var ready = !Plugin.HasMissingRequiredDependency();

        var bg = ready ? CodexTheme.OkBg : CodexTheme.WarnBg;
        var border = ready ? CodexTheme.OkLine : CodexTheme.WarnLine;
        var fg = ready ? CodexTheme.OkFg : CodexTheme.WarnFg;

        string boldPart, restPart;
        if (ready)
        {
            boldPart = Loc.T("Alle Voraussetzungen erfüllt.", "All requirements met.");
            restPart = Loc.T($"{installedCount} von {total} Plugins installiert - Automation einsatzbereit.",
                $"{installedCount} of {total} plugins installed - automation is ready.");
        }
        else
        {
            var missingNames = Plugin.Dependencies
                .Where(d => d.Required && !Plugin.IsDependencySatisfied(d.InternalName, d.Group))
                .Select(d => d.Group ?? d.InternalName)
                .Distinct()
                .Select(key => key == Plugin.CombatDependencyGroup
                    ? Loc.T("Kampf-Plugin", "Combat plugin")
                    : Plugin.Dependencies.First(d => d.InternalName == key).DisplayName)
                .ToList();
            boldPart = Loc.T($"Fehlt: {string.Join(", ", missingNames)}.", $"Missing: {string.Join(", ", missingNames)}.");
            restPart = Loc.T("Automation ist deaktiviert, bis es installiert ist.", "Automation is disabled until it is installed.");
        }

        var availWidth = width;
        var paddingX = 16f * scale;
        var paddingY = 12f * scale;

        // FontBodyBold statt FontBody zur Höhenmessung - restPart ist jetzt genauso groß (siehe unten).
        float textHeight;
        using (CodexTheme.FontBodyBold.Push())
            textHeight = ImGui.GetFontSize();
        var iconSize = 20f * scale;
        var rowHeight = MathF.Max(iconSize, textHeight) + paddingY * 2f;

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + new Vector2(availWidth, rowHeight), ImGui.GetColorU32(bg), 6f * scale);
        drawList.AddRect(cursor, cursor + new Vector2(availWidth, rowHeight), ImGui.GetColorU32(border), 6f * scale);

        ImGui.SetCursorScreenPos(cursor + new Vector2(paddingX, (rowHeight - iconSize) / 2f));
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            ImGui.SetWindowFontScale(iconSize / ImGui.GetFontSize());
            ImGui.TextColored(fg, (ready ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.ExclamationTriangle).ToIconString());
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.SameLine(0f, 10f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (iconSize - textHeight) / 2f);
        using (CodexTheme.FontBodyBold.Push())
            ImGui.TextColored(fg, boldPart);
        // ~3px Abstand zum vorherigen Text, -1px Schriftgröße, dazu nochmal 1px nach unten und 3px
        // nach rechts (Nutzervorgabe).
        ImGui.SameLine(0f, 3f * scale + 3f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 1f * scale);
        using (CodexTheme.FontPluginStatusRest.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, restPart);

        if (!ready)
        {
            var firstMissing = Plugin.Dependencies.First(d => d.Required && !Plugin.IsDependencySatisfied(d.InternalName, d.Group));
            var buttonSize = MeasureInstallButton(scale);
            var buttonX = cursor.X + availWidth - paddingX - buttonSize.X;
            var buttonY = cursor.Y + (rowHeight - buttonSize.Y) / 2f;
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (DrawInstallButton(scale, "##CodexPluginsBannerInstall", buttonSize))
                Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, firstMissing.DisplayName);
        }

        ImGui.SetCursorScreenPos(cursor + new Vector2(0f, rowHeight));
    }

    private static void DrawCombatPluginGroup(float scale, float width)
    {
        DrawPluginGroupHeader(scale, Loc.T("Kampf-Plugin", "Combat plugin"),
            Loc.T("EINS ERFORDERLICH", "ONE REQUIRED"), CodexTheme.TextCardTitle, CodexTheme.LineFrame, CodexTheme.BgSelected);

        var effective = CombatPluginBridge.GetEffective();
        var combatEntries = Plugin.Dependencies.Where(d => d.Group == Plugin.CombatDependencyGroup).ToArray();

        // Nutzeranforderung: derselbe rechte Randabstand wie bei den anderen Gruppen auf dieser Seite
        // (siehe DrawPluginsPage.PluginsPageRightMargin) - über die feste Außenbreite der Tabelle.
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f * scale, 0f));
        if (ImGui.BeginTable("##CodexPluginsCombatTable", 2, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableSetupColumn("##CodexPluginsCombatCol0", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##CodexPluginsCombatCol1", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow();

            foreach (var entry in combatEntries)
            {
                ImGui.TableNextColumn();
                var installed = Plugin.IsPluginLoaded(entry.InternalName);
                var kind = entry.InternalName == CombatPluginBridge.RotationSolverInternalName
                    ? CombatPluginKind.RotationSolver
                    : CombatPluginKind.WrathCombo;
                var selected = effective == kind;
                var description = selected
                    ? Loc.T("Für die Automation ausgewählt", "Selected for automation")
                    : Loc.T("Alternatives Kampf-Plugin", "Alternative combat plugin");

                // Nutzeranforderung: der goldene Strich beim ausgewählten Kampf-Plugin geht jetzt bis
                // ganz nach links an den Kartenrand und über die volle Kartenhöhe (siehe
                // CodexTheme.BeginCard.accentBar), statt nur über die (schmalere, eingerückte)
                // Zeilenhöhe wie vorher.
                CodexTheme.BeginCard(scale, accentBar: selected);
                if (DrawPluginRow(scale, entry.DisplayName, description, installed))
                    Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, entry.DisplayName);
                CodexTheme.EndCard();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private static void DrawRequiredPluginsGroup(float scale, float width)
    {
        var requiredStandalone = Plugin.Dependencies.Where(d => d.Required && d.Group == null).ToArray();
        var installedCount = requiredStandalone.Count(d => Plugin.IsPluginLoaded(d.InternalName));
        var ok = installedCount == requiredStandalone.Length;

        DrawPluginGroupHeader(scale, Loc.T("Erforderlich", "Required"), $"{installedCount} / {requiredStandalone.Length}",
            ok ? CodexTheme.TextCardTitle : CodexTheme.WarnFg,
            ok ? CodexTheme.LineFrame : CodexTheme.WarnLine,
            ok ? CodexTheme.BgSelected : CodexTheme.WarnBg);

        // Derselbe rechte Randabstand wie bei den anderen Gruppen (siehe
        // DrawPluginsPage.PluginsPageRightMargin), über eine Tabelle mit fester Außenbreite statt
        // eines BeginChild mit Höhe 0 - Bugfix: ein Kind-Fenster mit Höhe 0 füllt in ImGui den
        // gesamten VERBLEIBENDEN Platz des äußeren scrollbaren Bereichs aus (nicht "an den Inhalt
        // anpassen", das bräuchte ImGuiChildFlags.AutoResizeY, das dieses Binding nicht kennt - siehe
        // CodexTheme.BeginCard-Kommentar) und blockierte dadurch den Scrollbalken der ganzen Seite
        // (Nutzer-Report: "der rote Rand/Scrollbalken fehlt bei Plugins"). Eine Tabelle mit
        // outer_size.Y=0 passt ihre Höhe dagegen wie gewünscht an den Inhalt an.
        if (ImGui.BeginTable("##CodexPluginsRequiredWidth", 1, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableNextColumn();
            CodexTheme.BeginCard(scale);
            for (var i = 0; i < requiredStandalone.Length; i++)
            {
                var entry = requiredStandalone[i];
                var installed = Plugin.IsPluginLoaded(entry.InternalName);
                var (de, en) = GetShortPluginDescription(entry.InternalName);
                if (DrawPluginRow(scale, entry.DisplayName, Loc.T(de, en), installed))
                    Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, entry.DisplayName);
                if (i < requiredStandalone.Length - 1)
                    CodexTheme.CardDivider(scale);
            }
            CodexTheme.EndCard();
            ImGui.EndTable();
        }
    }

    private static void DrawOptionalPluginsGroup(float scale, float width)
    {
        DrawPluginGroupHeader(scale, Loc.T("Optional", "Optional"), null, default, default, default);

        var optional = Plugin.Dependencies.Where(d => !d.Required).ToArray();
        // Derselbe rechte Randabstand wie bei den anderen Gruppen, über eine Tabelle statt BeginChild
        // - siehe DrawRequiredPluginsGroup-Kommentar (Bugfix: Scrollbalken der Seite fehlte).
        if (ImGui.BeginTable("##CodexPluginsOptionalWidth", 1, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableNextColumn();
            CodexTheme.BeginCard(scale);
            for (var i = 0; i < optional.Length; i++)
            {
                var entry = optional[i];
                var installed = Plugin.IsPluginLoaded(entry.InternalName);
                var (de, en) = GetShortPluginDescription(entry.InternalName);
                if (DrawPluginRow(scale, entry.DisplayName, Loc.T(de, en), installed))
                    Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, entry.DisplayName);
                if (i < optional.Length - 1)
                    CodexTheme.CardDivider(scale);
            }
            CodexTheme.EndCard();
            ImGui.EndTable();
        }
    }

    /// <summary>Gruppen-Überschrift (Cinzel 22px, siehe CodexTheme.FontPluginGroupLabel) + optionales Badge, vertikal mittig zur Überschrift ausgerichtet - 10px Abstand zur darunterliegenden Karte (Nutzervorgabe).</summary>
    private static void DrawPluginGroupHeader(float scale, string title, string? badgeText, Vector4 badgeFg, Vector4 badgeBorder, Vector4 badgeBg)
    {
        var rowStartY = ImGui.GetCursorPosY();
        float titleHeight;
        using (CodexTheme.FontPluginGroupLabel.Push())
        {
            titleHeight = ImGui.GetFontSize();
            ImGui.TextColored(CodexTheme.TextCardTitle, title);
        }

        if (!string.IsNullOrEmpty(badgeText))
        {
            ImGui.SameLine(0f, 10f * scale);
            var badgeSize = MeasurePluginBadge(scale, badgeText);
            ImGui.SetCursorPosY(rowStartY + (titleHeight - badgeSize.Y) / 2f);
            DrawPluginBadge(scale, badgeText, badgeFg, badgeBorder, badgeBg);
            ImGui.SetCursorPosY(rowStartY + titleHeight);
        }

        // -3px (Nutzervorgabe).
        ImGui.Dummy(new Vector2(0f, 7f * scale));
    }

    private static Vector2 MeasurePluginBadge(float scale, string text)
    {
        using (CodexTheme.FontBadgeSmall.Push())
        {
            var padding = new Vector2(7f * scale, 1f * scale);
            return ImGui.CalcTextSize(text) + padding * 2f;
        }
    }

    private static void DrawPluginBadge(float scale, string text, Vector4 fg, Vector4 border, Vector4 bg)
    {
        using (CodexTheme.FontBadgeSmall.Push())
        {
            var textSize = ImGui.CalcTextSize(text);
            var padding = new Vector2(7f * scale, 1f * scale);
            var size = textSize + padding * 2f;
            var cursor = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), 3f * scale);
            drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), 3f * scale);
            drawList.AddText(cursor + padding, ImGui.GetColorU32(fg), text);
            ImGui.Dummy(size);
        }
    }

    /// <summary>
    /// Eine Plugin-Zeile: Status-Punkt links, Name (fett)+Beschreibung direkt darunter, rechts
    /// "Installiert" oder ein Install-Knopf. Zeichnet KEINEN eigenen Kartenhintergrund (der kommt vom
    /// Aufrufer, per CodexTheme.BeginCard/EndCard - entweder eine eigene kleine Karte je Kampf-Plugin,
    /// oder eine gemeinsame Karte mit CardDivider zwischen den Zeilen). Die Höhe ergibt sich aus dem
    /// Inhalt (Name+Beschreibung bzw. Status-Punkt, je nachdem was höher ist) statt eines festen
    /// Werts. Rechtsbündige Elemente werden bewusst relativ zum bei Zeilenbeginn gemessenen
    /// GetContentRegionAvail() positioniert statt zu GetWindowContentRegionMax() - Letzteres bezieht
    /// sich auf das GANZE Fenster und ignoriert den aktuellen Einzug/die Tabellenspalte, wodurch
    /// "Installed"/der Install-Knopf vorher rechts aus der (schmaleren) Box herausragten
    /// (Nutzer-Report). Gibt true zurück, wenn der Install-Knopf gerade angeklickt wurde.
    /// </summary>
    private static bool DrawPluginRow(float scale, string name, string description, bool installed)
    {
        var rowStartY = ImGui.GetCursorPosY();
        var rowStartX = ImGui.GetCursorPosX();
        var rightEdge = rowStartX + ImGui.GetContentRegionAvail().X;

        var dotSize = 26f * scale;
        float nameHeight, descHeight;
        using (CodexTheme.FontPluginName.Push())
            nameHeight = ImGui.GetFontSize();
        using (CodexTheme.FontPluginDescription.Push())
            descHeight = ImGui.GetFontSize();
        var textBlockHeight = nameHeight + descHeight;

        var verticalPadding = 3f * scale;
        // Nochmal 10px, dann nochmal 7px kleiner (Nutzervorgabe).
        var height = MathF.Max(dotSize, textBlockHeight) + verticalPadding * 2f - 17f * scale;

        ImGui.SetCursorPosY(rowStartY + (height - dotSize) / 2f);
        DrawPluginStatusDot(scale, installed);

        ImGui.SameLine(0f, 12f * scale);
        var textX = ImGui.GetCursorPosX();

        ImGui.SetCursorPosY(rowStartY + (height - textBlockHeight) / 2f);
        using (CodexTheme.FontPluginName.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, name);
        ImGui.SetCursorPosX(textX);
        // 5px, dann 5px, dann 2px, dann nochmal 2px nach oben (Nutzervorgabe), näher an den Namen heran.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 14f * scale);
        using (CodexTheme.FontPluginDescription.Push())
            ImGui.TextColored(CodexTheme.TextMuted, description);

        var clicked = false;
        if (installed)
        {
            var label = Loc.T("Installiert", "Installed");
            float checkWidth, labelWidth, textHeight;
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                checkWidth = ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
            using (CodexTheme.FontPluginInstalledLabel.Push())
            {
                var size = ImGui.CalcTextSize(label);
                labelWidth = size.X;
                textHeight = size.Y;
            }

            var gap = 5f * scale;
            // 20px nach links (Nutzervorgabe).
            var rightX = rightEdge - checkWidth - gap - labelWidth - 20f * scale;
            ImGui.SetCursorPos(new Vector2(rightX, rowStartY + (height - textHeight) / 2f));
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                // +3px (Nutzervorgabe, wie der Haken im Status-Punkt).
                var glyph = FontAwesomeIcon.Check.ToIconString();
                var nativePx = ImGui.GetFontSize();
                ImGui.SetWindowFontScale((nativePx + 3f * scale) / nativePx);
                ImGui.TextColored(CodexTheme.OkFg, glyph);
                ImGui.SetWindowFontScale(1f);
            }
            ImGui.SameLine(0f, gap);
            ImGui.SetCursorPosY(rowStartY + (height - textHeight) / 2f);
            using (CodexTheme.FontPluginInstalledLabel.Push())
                ImGui.TextColored(CodexTheme.OkFg, label);
        }
        else
        {
            var buttonSize = MeasureInstallButton(scale);
            // 20px nach links (Nutzervorgabe).
            var rightX = rightEdge - buttonSize.X - 20f * scale;
            ImGui.SetCursorPos(new Vector2(rightX, rowStartY + (height - buttonSize.Y) / 2f));
            clicked = DrawInstallButton(scale, $"##CodexPluginInstall{name}", buttonSize);
        }

        ImGui.SetCursorPosY(rowStartY + height);
        return clicked;
    }

    private static void DrawPluginStatusDot(float scale, bool installed)
    {
        var size = new Vector2(26f * scale, 26f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var bg = installed ? CodexTheme.OkBg : CodexTheme.LineRow;
        var border = installed ? CodexTheme.OkLine : CodexTheme.LineControl;
        var fg = installed ? CodexTheme.OkFg : CodexTheme.TextMuted;

        drawList.AddCircleFilled(cursor + size / 2f, size.X / 2f, ImGui.GetColorU32(bg));
        drawList.AddCircle(cursor + size / 2f, size.X / 2f, ImGui.GetColorU32(border));

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = (installed ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString();
            var nativePx = ImGui.GetFontSize();
            // +3px (Nutzervorgabe).
            var desiredPx = 15f * scale;
            ImGui.SetWindowFontScale(desiredPx / nativePx);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(cursor + (size - glyphSize) / 2f, ImGui.GetColorU32(fg), glyph);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.Dummy(size);
    }

    private static Vector2 MeasureInstallButton(float scale)
    {
        var label = Loc.T("Installieren", "Install");
        var padding = new Vector2(12f * scale, 5f * scale);

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.Download.ToIconString()).X;

        float labelWidth, textHeight;
        using (CodexTheme.FontAutoButtonLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            labelWidth = size.X;
            textHeight = size.Y;
        }

        var iconGap = 5f * scale;
        return new Vector2(padding.X * 2f + iconWidth + iconGap + labelWidth, padding.Y * 2f + textHeight);
    }

    /// <summary>Zeichnet den per MeasureInstallButton bemessenen Install-Knopf AM AKTUELLEN Cursor (vom Aufrufer vorher per SetCursorPos positioniert) - manuell (InvisibleButton + Draw) statt natives ImGui.Button, da Icon- und Text-Schrift gemischt werden.</summary>
    private static bool DrawInstallButton(float scale, string id, Vector2 buttonSize)
    {
        var label = Loc.T("Installieren", "Install");
        var padding = new Vector2(12f * scale, 5f * scale);
        var iconGap = 5f * scale;

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, buttonSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = hovered ? CodexTheme.Accent with { W = 0.85f } : CodexTheme.Accent;
        drawList.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), 3f * scale);

        var contentCursor = cursor + new Vector2(padding.X, 0f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconStr = FontAwesomeIcon.Download.ToIconString();
            var iconSize = ImGui.CalcTextSize(iconStr);
            drawList.AddText(contentCursor + new Vector2(0f, (buttonSize.Y - iconSize.Y) / 2f), ImGui.GetColorU32(CodexTheme.TextOnAccent), iconStr);
            contentCursor.X += iconSize.X + iconGap;
        }

        using (CodexTheme.FontAutoButtonLabel.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(contentCursor + new Vector2(0f, (buttonSize.Y - labelSize.Y) / 2f), ImGui.GetColorU32(CodexTheme.TextOnAccent), label);
        }

        return clicked;
    }

    // ---- Seite "Datenbank" (Nutzeranforderung) - zeigt ALLE Sammelobjekte der ganzen Spielwelt (nicht
    // nur der aktuellen Zone, siehe Plugin.GetGlobalEntries), mit Status (Besessen/Fehlt). Reine
    // Neugestaltung der bestehenden MainWindow.DrawDatabasePage im Codex-Theme - Datenermittlung/
    // Besitzprüfung/Typlisten bleiben identisch (siehe Plugin.DatabaseTypes/
    // DatabaseTypesWithoutVendorInfo, dafür dort auf internal gestellt), nur die Darstellung ist neu.

    // Nur für die Datenbank-Seite - Sitzungszustand, nicht gespeichert (wie im alten Menü).
    private string databaseSearch = string.Empty;
    private CollectibleType databaseSelectedType = CollectibleType.Mount;

    // Nutzeranforderung: das "Sortiert nach"-Dropdown wieder entfernt - stattdessen direkt per Klick
    // auf eine beliebige Kopfzeile sortierbar (auf-/absteigend), siehe DrawDatabaseTable.
    private enum DatabaseSortKey
    {
        Name,
        Price,
        Vendor,
        Zone,
        Status,
    }

    private DatabaseSortKey databaseSortKey = DatabaseSortKey.Name;
    private bool databaseSortAscending = true;

    // Nutzer-Report "Rendern dauert extrem lange": dieser Block wertete GetGlobalEntries() (~3100
    // Einträge, inkl. Quest/Sightseeing) per IsOwned/Where/OrderBy bei JEDEM Draw-Aufruf neu aus -
    // bei 60 FPS summierte sich das spürbar, obwohl sich Suchtext/Filter/Sortierung meist über viele
    // Frames hinweg gar nicht ändern. Jetzt gecacht: sofortige Neuberechnung bei geänderter
    // Suche/Typ/Sortierung/"Erhaltene ausblenden" (bleibt responsiv), sonst höchstens alle 500ms (für
    // den sich sonst nur durch Spielaktionen ändernden Besitzstatus - völlig ausreichend für eine
    // Einstellungsseite).
    private List<CollectibleEntry>? databaseFilteredCache;
    private int databaseTypeEntriesCountCache;
    private List<CollectibleType>? databaseAvailableTypesCache;
    private (bool HideOwned, CollectibleType SelectedType, string Search, DatabaseSortKey SortKey, bool SortAscending) databaseCacheKey;
    private long databaseCacheTime;

    private void DrawDatabasePage(float scale)
    {
        var config = plugin.Configuration;

        DrawDatabaseSearchRow(scale, config);
        ImGui.Dummy(new Vector2(0f, 14f * scale));

        var now = Environment.TickCount64;
        var key = (config.DatabaseHideOwned, databaseSelectedType, databaseSearch, databaseSortKey, databaseSortAscending);
        if (databaseAvailableTypesCache == null || key != databaseCacheKey || now - databaseCacheTime >= 500)
        {
            RecomputeDatabaseView(config);
            databaseCacheTime = now;
            // databaseSelectedType kann sich in RecomputeDatabaseView korrigiert haben (ungültiger
            // Typ, siehe dort) - der Schlüssel muss den NEUEN Wert enthalten, sonst würde die nächste
            // Prüfung fälschlich wieder einen "Wechsel" erkennen und ständig neu rechnen.
            databaseCacheKey = (config.DatabaseHideOwned, databaseSelectedType, databaseSearch, databaseSortKey, databaseSortAscending);
        }

        var availableTypes = databaseAvailableTypesCache!;
        if (availableTypes.Count == 0)
        {
            using (CodexTheme.FontSubtitleItalic.Push())
                ImGui.TextColored(CodexTheme.TextTertiary,
                    Loc.T("Alles besessen/abgeschlossen - Glückwunsch!", "Everything owned/completed - congratulations!"));
            return;
        }

        // Nutzeranforderung: während der (typübergreifenden, siehe RecomputeDatabaseView) Suche eine
        // eigene "Suchergebnisse"-Pille statt der normalen Typ-Pillen zeigen - sonst wirkte es
        // irreführend, wenn z.B. der "Mount"-Tab markiert blieb, obwohl die Suche auch/nur ein
        // Minion gefunden hat. Verschwindet wieder, sobald das Suchfeld leer ist.
        var hasSearch = !string.IsNullOrWhiteSpace(databaseSearch);
        if (hasSearch)
            DrawDatabaseSearchResultsPill(scale);
        else
            DrawDatabaseTypePills(scale, availableTypes);
        ImGui.Dummy(new Vector2(0f, 14f * scale));

        var filtered = databaseFilteredCache!;
        DrawDatabaseCountRow(scale, filtered.Count, databaseTypeEntriesCountCache);
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (filtered.Count == 0)
        {
            using (CodexTheme.FontSubtitleItalic.Push())
                ImGui.TextColored(CodexTheme.TextTertiary, Loc.T("Nichts gefunden.", "Nothing found."));
            return;
        }

        DrawDatabaseTable(scale, filtered, showTypeBadge: hasSearch);
    }

    private void RecomputeDatabaseView(Configuration config)
    {
        // Nur Einträge mit echter Zonen-Zuordnung (TerritoryTypeId != 0) - exakt die Voraussetzung,
        // unter der das Overlay einen Eintrag JEMALS zeigen würde.
        var allEntries = plugin.GetGlobalEntries()
            .Where(e => e.TerritoryTypeId != 0)
            .Where(e => !config.DatabaseHideOwned || !plugin.IsOwned(e))
            .ToList();
        var availableTypes = Plugin.DatabaseTypes.Where(t => allEntries.Any(e => e.Type == t)).ToList();
        databaseAvailableTypesCache = availableTypes;

        if (availableTypes.Count == 0)
        {
            databaseFilteredCache = new List<CollectibleEntry>();
            databaseTypeEntriesCountCache = 0;
            return;
        }

        if (!availableTypes.Contains(databaseSelectedType))
            databaseSelectedType = availableTypes[0];

        // Nutzeranforderung: Suche gilt typübergreifend (Mount, Minion etc. zusammen), nicht mehr nur
        // innerhalb des aktuell per Pille gewählten Typs - der Typ-Filter greift deshalb nur noch,
        // solange das Suchfeld leer ist.
        var hasSearch = !string.IsNullOrWhiteSpace(databaseSearch);
        var typeEntries = hasSearch ? allEntries : allEntries.Where(e => e.Type == databaseSelectedType).ToList();
        databaseTypeEntriesCountCache = typeEntries.Count;

        // Nutzeranforderung: Suche gilt jetzt auch für Preis/Von/Zone/Status, nicht mehr nur den Namen.
        var searched = typeEntries
            .Where(e => !hasSearch || DatabaseEntryMatchesSearch(e, databaseSearch));
        // Nutzeranforderung: per Klick auf eine beliebige Kopfzeile sortierbar (auf-/absteigend),
        // siehe DrawDatabaseTable.
        var ordered = databaseSortKey switch
        {
            DatabaseSortKey.Price => searched.OrderBy(e => e.CurrencyAmount),
            DatabaseSortKey.Vendor => searched.OrderBy(e => string.IsNullOrEmpty(e.Vendor) ? string.Empty : e.Vendor, StringComparer.OrdinalIgnoreCase),
            DatabaseSortKey.Zone => searched.OrderBy(e => e.TerritoryTypeId != 0 ? Plugin.GetZoneName(e.TerritoryTypeId) : string.Empty, StringComparer.OrdinalIgnoreCase),
            DatabaseSortKey.Status => searched.OrderBy(e => plugin.IsOwned(e)),
            _ => searched.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        databaseFilteredCache = (databaseSortAscending ? ordered : ordered.Reverse()).ToList();
    }

    /// <summary>Sucht jetzt über alle sichtbaren Tabellenspalten (Nutzeranforderung), nicht mehr nur den Namen - Preis wird sowohl als formatierter Betrag als auch als voller Währungsname durchsucht (passend zur Preis-Zelle, die den Betrag als Icon+Zahl statt des Namens zeigt).</summary>
    private bool DatabaseEntryMatchesSearch(CollectibleEntry entry, string query)
    {
        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(entry.Vendor) && entry.Vendor.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var zone = entry.TerritoryTypeId != 0 ? Plugin.GetZoneName(entry.TerritoryTypeId) : string.Empty;
        if (zone.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var status = plugin.IsOwned(entry) ? Loc.T("Erhalten", "Owned") : Loc.T("Fehlt", "Missing");
        if (status.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(entry.Currency) && entry.Currency.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (entry.CurrencyAmount != 0)
        {
            var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
            if (entry.CurrencyAmount.ToString("N0", culture).Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Nutzeranforderung: Ätherströmungs-Einträge zeigen in der Tabelle nicht den vollen Namen
    /// "Aether Current - &lt;Zone&gt; (...)"/"... #N" (der Zonenname steht hier ohnehin schon in der
    /// eigenen ZONE-Spalte), sondern nur den unterscheidenden Teil danach - entweder den Inhalt der
    /// Klammer oder die "#N"-Nummer. Nur die ANZEIGE betrifft das (Name.Contains-Suche/Sortierung/
    /// Tooltip/Karten-Klick bleiben unverändert auf dem vollen entry.Name). Andere Typen unverändert.
    /// </summary>
    private static string GetDatabaseDisplayName(CollectibleEntry entry)
    {
        if (entry.Type != CollectibleType.AetherCurrent)
            return entry.Name;

        var parenMatch = Regex.Match(entry.Name, @"\(([^)]*)\)\s*$");
        if (parenMatch.Success)
            return parenMatch.Groups[1].Value;

        var hashMatch = Regex.Match(entry.Name, @"(#\d+)\s*$");
        if (hashMatch.Success)
            return hashMatch.Groups[1].Value;

        return entry.Name;
    }

    private void DrawDatabaseSearchRow(float scale, Configuration config)
    {
        var toggleLabel = Loc.T("Erhaltene ausblenden", "Hide owned");
        float toggleLabelWidth, toggleLabelHeight;
        using (CodexTheme.FontMenuFieldLabel.Push())
        {
            var labelSize = ImGui.CalcTextSize(toggleLabel);
            toggleLabelWidth = labelSize.X;
            toggleLabelHeight = labelSize.Y;
        }

        var toggleSize = new Vector2(42f * scale, 22f * scale);
        var innerGap = 10f * scale;
        var rightBlockWidth = toggleSize.X + innerGap + toggleLabelWidth;
        var searchGap = 16f * scale;
        // Nutzeranforderung: nicht breiter als der "Schließen"-Knopf-Rand (32px rechter Randabstand),
        // zusätzlich 15px, dann 20px, dann nochmal 30px kürzer (insgesamt 65px, Nutzervorgabe) -
        // statt der zuvor reduzierten Höhe, die wiederhergestellt wurde (siehe searchPadding/Schrift
        // unten).
        var searchWidth = ImGui.GetContentRegionAvail().X - 32f * scale - rightBlockWidth - searchGap - 65f * scale;
        // Höhe wiederhergestellt (Nutzervorgabe) - ursprüngliches FramePadding und FontBody statt der
        // zwischenzeitlich kleineren Schrift.
        var searchPadding = new Vector2(12f * scale, 10f * scale);
        var rowY = ImGui.GetCursorPosY();

        ImGui.PushStyleColor(ImGuiCol.FrameBg, CodexTheme.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, searchPadding);
        ImGui.SetNextItemWidth(searchWidth);
        // +2px (Nutzervorgabe): eigener Handle statt FontBody.
        using (CodexTheme.FontDatabaseSearchInput.Push())
            ImGui.InputTextWithHint("##CodexDatabaseSearch", Loc.T("Datenbank durchsuchen...", "Search database..."), ref databaseSearch, 100);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        var searchHeight = ImGui.GetItemRectSize().Y;

        // Nutzervorgabe: Suchbox, Toggle und Label alle auf derselben vertikalen Höhe - beide
        // relativ zur (jetzt kleineren) Suchbox-Höhe zentriert, keine separaten Verschiebungen mehr.
        ImGui.SameLine(0f, searchGap);
        var toggleY = rowY + (searchHeight - toggleSize.Y) / 2f;
        ImGui.SetCursorPosY(toggleY);
        var hideOwned = config.DatabaseHideOwned;
        if (CodexTheme.Toggle("##CodexDatabaseHideOwned", ref hideOwned, scale))
        {
            config.DatabaseHideOwned = hideOwned;
            config.Save();
        }

        // Nutzervorgabe: Label auf derselben Höhe wie der Toggle-Knopf zentriert, zusätzlich nochmal
        // 10px nach oben (separat vom Toggle selbst, der bleibt zur Suchbox zentriert).
        ImGui.SameLine(0f, innerGap);
        ImGui.SetCursorPosY(toggleY + (toggleSize.Y - toggleLabelHeight) / 2f - 10f * scale);
        using (CodexTheme.FontMenuFieldLabel.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, toggleLabel);

        ImGui.SetCursorPosY(rowY + searchHeight);
    }

    /// <summary>Wie MainWindow.DrawDatabaseTypeSelector, aber als goldumrandete Pillen statt schlichter Knöpfe, sonst dieselbe umbrechende Reihe.</summary>
    private void DrawDatabaseTypePills(float scale, IReadOnlyList<CollectibleType> availableTypes)
    {
        var startX = ImGui.GetCursorPosX();
        var startY = ImGui.GetCursorPosY();
        // Nutzeranforderung: nicht breiter als der "Schließen"-Knopf-Rand (32px rechter Randabstand).
        var contentMaxX = ImGui.GetWindowContentRegionMax().X - 32f * scale;
        var gap = 8f * scale;
        var rowGap = 8f * scale;

        var cursorX = startX;
        var cursorY = startY;
        var rowHeight = 0f;

        using (CodexTheme.FontMenuDropdownValue.Push())
        {
            foreach (var type in availableTypes)
            {
                var label = Loc.TypeName(type);
                var textSize = ImGui.CalcTextSize(label);
                var padding = new Vector2(14f * scale, 7f * scale);
                var pillSize = new Vector2(textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);

                if (cursorX > startX && cursorX + pillSize.X > contentMaxX)
                {
                    cursorX = startX;
                    cursorY += rowHeight + rowGap;
                    rowHeight = 0f;
                }

                ImGui.SetCursorPos(new Vector2(cursorX, cursorY));
                if (DrawDatabaseTypePill($"##CodexDbType{type}", label, databaseSelectedType == type, pillSize))
                    databaseSelectedType = type;

                cursorX += pillSize.X + gap;
                rowHeight = MathF.Max(rowHeight, pillSize.Y);
            }
        }

        ImGui.SetCursorPos(new Vector2(startX, cursorY + rowHeight));
    }

    /// <summary>Ersatz für DrawDatabaseTypePills während einer aktiven Suche - eine einzelne, immer
    /// markierte, nicht klickbare Pille statt der Typ-Auswahl (siehe DrawDatabasePage-Kommentar).</summary>
    private static void DrawDatabaseSearchResultsPill(float scale)
    {
        var label = Loc.T("Suchergebnisse", "Search results");
        using (CodexTheme.FontMenuDropdownValue.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            var padding = new Vector2(14f * scale, 7f * scale);
            var pillSize = new Vector2(textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);
            DrawDatabaseTypePill("##CodexDbTypeSearch", label, selected: true, pillSize);
        }
    }

    private static bool DrawDatabaseTypePill(string id, string label, bool selected, Vector2 size)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = selected ? CodexTheme.BgSelected : hovered ? CodexTheme.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var border = selected ? CodexTheme.Accent : CodexTheme.LineControl;
        var fg = selected ? CodexTheme.TextHeading : CodexTheme.TextSecondary;

        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        using (CodexTheme.FontMenuDropdownValue.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(cursor + (size - textSize) / 2f, ImGui.GetColorU32(fg), label);
        }

        return clicked;
    }

    private void DrawDatabaseCountRow(float scale, int filteredCount, int totalCount)
    {
        // Nutzeranforderung: "Sortiert nach"-Dropdown wieder entfernt (samt Label) - sortiert wird
        // jetzt per Klick auf die "NAME"-Kopfzeile (siehe DrawDatabaseTable).
        using (CodexTheme.FontDatabaseCountRow.Push())
            ImGui.TextColored(CodexTheme.TextTertiary,
                Loc.T($"{filteredCount} von {totalCount} Einträgen", $"{filteredCount} of {totalCount} entries"));
    }

    private void DrawDatabaseTable(float scale, List<CollectibleEntry> filtered, bool showTypeBadge)
    {
        // Bei typübergreifender Suche (siehe RecomputeDatabaseView) können die Treffer aus
        // verschiedenen Typen gemischt sein - die Preis-/Von-Spalten zeigen, sobald mindestens einer
        // der Treffer sie sinnvoll füllen könnte, statt sich allein auf den (dann nicht mehr
        // repräsentativen) per Pille gewählten Einzeltyp zu verlassen.
        var showVendorInfo = filtered.Any(e => !Plugin.DatabaseTypesWithoutVendorInfo.Contains(e.Type));
        // Nutzeranforderung: Kartenstift-Spalte entfernt - der Linkstatus ist weiterhin an der
        // goldenen Namensfarbe erkennbar, Klick auf den Namen öffnet unverändert die Karte.
        // Nutzeranforderung: bei der typübergreifenden Suche (siehe DrawDatabasePage) eine eigene,
        // überschriftslose Spalte VOR dem Namen fürs Typ-Badge statt es in die Namen-Spalte zu
        // zeichnen.
        var nameColumnIndex = showTypeBadge ? 1 : 0;
        var columnCount = (showTypeBadge ? 1 : 0) + (showVendorInfo ? 5 : 3);

        ImGui.PushStyleColor(ImGuiCol.TableRowBg, CodexTheme.BgCard with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, CodexTheme.LineSubtle);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, CodexTheme.LineCard);
        // 14px Innenabstand links/rechts, identisch für Kopf- UND Datenzeilen (eine gemeinsame
        // Tabellen-Einstellung, daher zwangsläufig gleich - Nutzervorgabe).
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(14f * scale, 4f * scale));

        // Nutzeranforderung: nicht breiter als die anderen Elemente dieser Seite (rechter Rand wie
        // beim "Schließen"-Knopf, 32px).
        var tableWidth = ImGui.GetContentRegionAvail().X - 32f * scale;

        // Nutzeranforderung: Tabelle bis 20px vor den unteren Rand des Menüs (des scrollbaren
        // Inhaltsbereichs) ausdehnen, statt einer festen Höhe - "Fenster" meint hier das umgebende
        // ##CodexMenuContent-Kindfenster, dessen Grenzen GetWindowPos()/GetWindowSize() hier liefern.
        var tableHeight = MathF.Max(100f * scale,
            ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetCursorScreenPos().Y - 20f * scale);

        // Nutzeranforderung: die Umrandung soll auch um das Kopfzeilen-Panel gehen - mit
        // ImGuiTableFlags.BordersOuter (statt eines manuell nachgezogenen Rechtecks) übernimmt ImGui
        // das selbst und zeichnet dabei zuverlässig um Kopf UND Zeilen zusammen, in der bereits
        // gepushten TableBorderStrong-Farbe (LineCard).
        if (ImGui.BeginTable("##CodexDatabaseTable", columnCount,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.ScrollY,
                new Vector2(tableWidth, tableHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            if (showTypeBadge)
                ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 98f * scale);
            ImGui.TableSetupColumn(Loc.T("NAME", "NAME"), ImGuiTableColumnFlags.WidthStretch, 1.7f);
            if (showVendorInfo)
            {
                ImGui.TableSetupColumn(Loc.T("PREIS", "PRICE"), ImGuiTableColumnFlags.WidthFixed, 120f * scale);
                ImGui.TableSetupColumn(Loc.T("VON", "FROM"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
            }
            ImGui.TableSetupColumn(Loc.T("ZONE", "ZONE"), ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn(Loc.T("STATUS", "STATUS"), ImGuiTableColumnFlags.WidthFixed, 100f * scale);

            // Nutzeranforderung: jede Spalte ist klickbar sortierbar, nicht mehr nur NAME - die
            // kopflose Badge-Spalte hat keinen Sortierknopf (siehe Header-Schleife unten, die leere
            // Labels überspringt), braucht aber trotzdem einen Platzhalter, damit die Indizes der
            // übrigen Spalten (columnSortKeys[c]) nicht verrutschen.
            var columnSortKeysList = new List<DatabaseSortKey>();
            if (showTypeBadge)
                columnSortKeysList.Add(DatabaseSortKey.Name);
            columnSortKeysList.AddRange(showVendorInfo
                ? new[] { DatabaseSortKey.Name, DatabaseSortKey.Price, DatabaseSortKey.Vendor, DatabaseSortKey.Zone, DatabaseSortKey.Status }
                : new[] { DatabaseSortKey.Name, DatabaseSortKey.Zone, DatabaseSortKey.Status });
            var columnSortKeys = columnSortKeysList.ToArray();

            // Kopfzeile: feste Höhe 34px, eigener (dunklerer) Hintergrund BgPopup, Cinzel SemiBold
            // 11px in Versalien mit Zeichenabstand, TextTertiary - von Hand gezeichnet statt
            // ImGui.TableHeadersRow() (das brächte den ImGui-Standard-Look, Nutzervorgabe). Text
            // senkrecht zentriert in der festen Zeilenhöhe.
            var headerHeight = 34f * scale;
            var headerTopScreenY = ImGui.GetCursorScreenPos().Y;
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CodexTheme.BgPopup));
            using (CodexTheme.FontDatabaseHeader.Push())
            {
                var textHeight = ImGui.GetTextLineHeight();
                for (var c = 0; c < columnCount; c++)
                {
                    ImGui.TableSetColumnIndex(c);
                    var label = ImGui.TableGetColumnName(c);
                    if (string.IsNullOrEmpty(label))
                        continue;
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerHeight - textHeight) / 2f);
                    // Nutzervorgabe: "NAME" nochmal 4px weiter vom linken Rand weg (insgesamt 8px),
                    // genau wie die Item-Namen darunter.
                    if (c == nameColumnIndex)
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);

                    // Nutzeranforderung: per Klick auf eine Kopfzeile sortierbar (auf-/absteigend) -
                    // golden hervorgehoben + kleiner Pfeil nur bei der aktuell aktiven Sortierspalte.
                    // Die eigentliche Trefferfläche ist ein InvisibleButton über denselben Bereich
                    // (Text+ggf. Pfeil), danach dorthin zurückgesetzt, damit der sichtbare Text
                    // unverändert bleibt.
                    var key = columnSortKeys[c];
                    var isActive = databaseSortKey == key;
                    var headerCellCursor = ImGui.GetCursorScreenPos();
                    CodexTheme.DrawSpacedText(label.ToUpperInvariant(), isActive ? CodexTheme.Accent : CodexTheme.TextTertiary, 1f * scale);
                    if (isActive)
                    {
                        ImGui.SameLine(0f, 4f * scale);
                        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                        {
                            var arrow = (databaseSortAscending ? FontAwesomeIcon.CaretUp : FontAwesomeIcon.CaretDown).ToIconString();
                            ImGui.TextColored(CodexTheme.Accent, arrow);
                        }
                    }
                    var headerCellEnd = ImGui.GetItemRectMax();

                    ImGui.SetCursorScreenPos(headerCellCursor);
                    if (ImGui.InvisibleButton($"##CodexDatabaseSortBy{key}", headerCellEnd - headerCellCursor))
                    {
                        if (isActive)
                            databaseSortAscending = !databaseSortAscending;
                        else
                        {
                            databaseSortKey = key;
                            databaseSortAscending = true;
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }
            }

            // Nutzeranforderung: eine durchgehende Linie über die VOLLE Tabellenbreite direkt unter
            // der Kopfzeile (nur dort, nicht darüber - der Kopf schließt direkt an die obere Kante der
            // Kartentabelle an), statt der normalen (nur je Spalte laufenden) Tabellen-Innenränder.
            var tableMinX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMin().X;
            var tableMaxX = tableMinX + tableWidth;
            var headerBottomY = headerTopScreenY + headerHeight;
            ImGui.GetWindowDrawList().AddLine(new Vector2(tableMinX, headerBottomY), new Vector2(tableMaxX, headerBottomY), ImGui.GetColorU32(CodexTheme.LineCard));

            // +10px, dann nochmal +20px Zeilenhöhe (Nutzervorgabe) über die Mindesthöhe von
            // TableNextRow, statt sich rein auf die natürliche Inhaltshöhe zu verlassen.
            var rowHeight = ImGui.GetFrameHeight() + 30f * scale;

            // Nutzer-Report: Zelleninhalte saßen oben in der (jetzt deutlich höheren) Zeile, da
            // AlignTextToFramePadding() sich nur auf die normale (kleine) Frame-Höhe bezieht, nicht
            // auf die hier manuell vergrößerte Zeilenhöhe. Stattdessen jede Zelle anhand derselben
            // (größten vorkommenden) Schrifthöhe explizit vertikal mittig positionieren.
            float cellContentHeight;
            using (CodexTheme.FontDatabaseItemName.Push())
                cellContentHeight = ImGui.GetTextLineHeight();

            foreach (var entry in filtered)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                var cellY = ImGui.GetCursorPosY() + (rowHeight - cellContentHeight) / 2f;

                // Nutzeranforderung: NUR bei der typübergreifenden Suche (siehe DrawDatabasePage) eine
                // eigene, überschriftslose Spalte VOR dem Namen mit dem Typ-Badge (Minion/
                // Orchestrionrolle/...) - sonst ist bei gemischten Treffern nicht erkennbar, zu
                // welchem Typ ein Eintrag gehört. Dieselben Typfarben + abgekürzten Beschriftungen
                // wie im Overlay (siehe CodexOverlayWindow.GetTypeBadgeColors/GetBadgeLabel).
                if (showTypeBadge)
                {
                    ImGui.TableNextColumn();
                    var badgeText = CodexOverlayWindow.GetBadgeLabel(entry.Type);
                    var (badgeBg, badgeFg) = CodexOverlayWindow.GetTypeBadgeColors(entry.Type);
                    var badgeSize = MeasurePluginBadge(scale, badgeText);
                    ImGui.SetCursorPosY(cellY + (cellContentHeight - badgeSize.Y) / 2f);
                    DrawPluginBadge(scale, badgeText, badgeFg, badgeBg, badgeBg);
                }

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                // Nutzervorgabe: nochmal 4px weiter vom linken Rand weg (insgesamt 8px, zusätzlich
                // zum gemeinsamen 14px-Innenabstand), genau wie die Kopfzeile "NAME".
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);

                var nameColor = entry.HasGoToTarget ? CodexTheme.Accent : CodexTheme.TextPrimary;
                using (CodexTheme.FontDatabaseItemName.Push())
                    ImGui.TextColored(nameColor, GetDatabaseDisplayName(entry));

                // Nutzeranforderung: STRG+SHIFT+Klick auf den Namen schaltet die Blacklist um (wie im
                // Overlay, siehe CodexOverlayWindow.DrawList-Kommentar) - in der Datenbank zusätzlich
                // togglend (bereits geblacklistete Einträge werden damit wieder entfernt), da die
                // Datenbank (anders als das Overlay) auch geblacklistete Einträge weiter anzeigt.
                var isBlacklistShortcut = ImGui.IsItemClicked() && ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                if (isBlacklistShortcut)
                {
                    if (Plugin.IsBlacklisted(entry))
                        Plugin.RemoveFromBlacklist(entry.Type, entry.Id);
                    else
                        Plugin.AddToBlacklist(entry);
                }

                if (entry.HasGoToTarget)
                {
                    if (ImGui.IsItemHovered())
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsItemClicked() && !isBlacklistShortcut)
                        Plugin.OpenEntryMap(entry);
                }

                // Nutzeranforderung: dasselbe Rechtsklick-Menü wie im Overlay (Mehr Informationen/
                // ToDo-Liste/Blacklist) - muss direkt NACH dem Namen-Widget aufgerufen werden (siehe
                // CodexOverlayWindow.DrawEntryContextMenu-Kommentar), also VOR dem Blacklist-Icon
                // unten - sonst öffnet sich das Menü beim Rechtsklick auf das Icon statt auf den Namen
                // (Nutzer-Report).
                plugin.CodexOverlayWindow.DrawEntryContextMenu(entry);

                // Rotes Blacklist-Icon neben dem Namen für geblacklistete Einträge (die Datenbank-
                // Seite zeigt, anders als das Overlay, auch geblacklistete Einträge).
                if (Plugin.IsBlacklisted(entry))
                {
                    ImGui.SameLine(0f, 6f * scale);
                    // 2px nach unten (Nutzervorgabe).
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * scale);
                    using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                        ImGui.TextColored(CodexTheme.ErrFg, FontAwesomeIcon.Ban.ToIconString());
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(Loc.T("Auf der Blacklist", "Blacklisted"));
                }

                if (showVendorInfo)
                {
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(cellY);
                    DrawDatabasePriceCell(entry, scale);

                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(cellY);
                    using (CodexTheme.FontDatabaseItemText.Push())
                        ImGui.TextColored(CodexTheme.TextMuted, string.IsNullOrEmpty(entry.Vendor) ? "—" : entry.Vendor);
                }

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                using (CodexTheme.FontDatabaseItemText.Push())
                    ImGui.TextColored(CodexTheme.TextMuted, entry.TerritoryTypeId != 0 ? Plugin.GetZoneName(entry.TerritoryTypeId) : "—");

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                DrawDatabaseStatusBadge(plugin.IsOwned(entry), scale);
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }

    private static void DrawDatabasePriceCell(CollectibleEntry entry, float scale)
    {
        if (string.IsNullOrEmpty(entry.Currency))
        {
            using (CodexTheme.FontDatabaseItemText.Push())
                ImGui.TextColored(CodexTheme.TextDim, "—");
            return;
        }

        // Rein textuelle "Währung" ohne Betrag/Icon (z.B. "Dungeon Drop" bei Noten ohne Händler,
        // siehe Data/orchestrions.json "Alienus") - einfach den Text statt eines Betrags anzeigen.
        if (entry.CurrencyAmount == 0)
        {
            using (CodexTheme.FontDatabaseItemText.Push())
                ImGui.TextColored(CodexTheme.TextMuted, entry.Currency);
            return;
        }

        var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
        using (CodexTheme.FontDatabaseItemText.Push())
            ImGui.TextColored(CodexTheme.TextMuted, entry.CurrencyAmount.ToString("N0", culture));

        if (entry.CurrencyIconId == 0)
            return;

        ImGui.SameLine(0f, 4f * scale);
        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(entry.CurrencyIconId)).GetWrapOrEmpty();
        // +3px (Nutzervorgabe): 16px -> 19px.
        ImGui.Image(icon.Handle, new Vector2(19f * scale));
    }

    private static void DrawDatabaseStatusBadge(bool owned, float scale)
    {
        var label = owned ? Loc.T("Erhalten", "Owned") : Loc.T("Fehlt", "Missing");
        var fg = owned ? CodexTheme.OkFg : CodexTheme.ErrFg;
        var bg = owned ? CodexTheme.OkBg : CodexTheme.ErrBg;
        var border = fg with { W = 0.6f };

        // Nochmal 3px kleiner (Nutzervorgabe) - FontDatabaseStatusLabel (14px) statt
        // FontPluginInstalledLabel (17px, das bleibt für die Plugins-Seite). Icon-Bonus von +3px
        // dafür wieder entfernt (zurück auf native Icon-Schriftgröße), passend zum kleineren Label.
        float iconWidth, labelWidth, contentHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize((owned ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString()).X;
        using (CodexTheme.FontDatabaseStatusLabel.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            labelWidth = labelSize.X;
            contentHeight = labelSize.Y;
        }

        var padding = new Vector2(8f * scale, 3f * scale);
        var innerGap = 4f * scale;
        var size = new Vector2(iconWidth + innerGap + labelWidth + padding.X * 2f, contentHeight + padding.Y * 2f);

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        var contentCursor = cursor + new Vector2(padding.X, (size.Y - contentHeight) / 2f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var icon = (owned ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString();
            var iconSize = ImGui.CalcTextSize(icon);
            drawList.AddText(contentCursor + new Vector2(0f, (contentHeight - iconSize.Y) / 2f), ImGui.GetColorU32(fg), icon);
        }
        contentCursor.X += iconWidth + innerGap;
        using (CodexTheme.FontDatabaseStatusLabel.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(fg), label);

        ImGui.Dummy(size);
    }

    // ---- Seite "Blacklist" (Nutzeranforderung, Vorbild referenz/codex-blacklist.png) - komplette
    // Neugestaltung der bestehenden MainWindow.DrawBlacklistPage im Codex-Theme. Datengrundlage bleibt
    // identisch (Configuration.Blacklist/Plugin.IsBlacklisted/AddToBlacklist/RemoveFromBlacklist).
    // STRG+SHIFT+Klick im Overlay gab es entgegen der ursprünglichen Spezifikation bisher nirgends im
    // Code (auch nicht im alten CompactOverlayWindow, das stattdessen nur ein Rechtsklick-Menü kennt) -
    // jetzt in CodexOverlayWindow.DrawList ergänzt (siehe dortigen Kommentar), damit die hier beworbene
    // Tastenkombination tatsächlich funktioniert. BlacklistedEntry speichert keine Zone (nur Type/Id/
    // Name, siehe Configuration.cs) - die ZONE-Spalte löst sie deshalb pro Zeichenaufruf gegen
    // Plugin.GetGlobalEntries() auf (wie GetDatabaseDisplayName/DatabaseEntryMatchesSearch das für die
    // Datenbank-Seite tun), "—" wenn kein passender, noch zonengebundener Eintrag (mehr) existiert.

    // Nur für die Blacklist-Seite - Sitzungszustand, nicht gespeichert (wie im alten Menü).
    private string blacklistSearch = string.Empty;
    private readonly HashSet<CollectibleType> blacklistHiddenTypes = new();

    private void DrawBlacklistPage(float scale)
    {
        var config = plugin.Configuration;
        var blacklist = config.Blacklist;

        // Nutzervorgabe: kein zusätzlicher Abstand vor der Box - direkt im selben Abstand zur
        // goldenen Trennlinie wie der erste Inhalt der anderen Seiten (der schon aus DrawContent
        // kommende 16px-Dummy vor dem switch-case reicht).
        DrawBlacklistShortcutHint(scale, leadingGap: false);

        if (blacklist.Count == 0)
        {
            DrawBlacklistEmptyState(scale);
            return;
        }

        DrawBlacklistToolbar(scale, config);
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var zoneLookup = GetBlacklistZoneLookup();

        var filtered = blacklist
            .Where(b => !blacklistHiddenTypes.Contains(b.Type))
            .Where(b => string.IsNullOrWhiteSpace(blacklistSearch) || b.Name.Contains(blacklistSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => config.TypeOrder.IndexOf(b.Type))
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using (CodexTheme.FontDatabaseCountRow.Push())
            ImGui.TextColored(CodexTheme.TextMuted,
                Loc.T($"{filtered.Count} von {blacklist.Count} Einträgen", $"{filtered.Count} of {blacklist.Count} entries"));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (filtered.Count == 0)
        {
            using (CodexTheme.FontSubtitleItalic.Push())
                ImGui.TextColored(CodexTheme.TextTertiary, Loc.T("Nichts gefunden.", "Nothing found."));
            return;
        }

        DrawBlacklistTable(scale, filtered, zoneLookup);
    }

    // Nutzer-Report "Rendern dauert extrem lange": Zone je (Type, Id) ist statische Spieldaten, die
    // sich zur Laufzeit nie ändert - eine GroupBy über alle ~3100 globalen Einträge bei JEDEM
    // Draw-Aufruf war daher reine Verschwendung. Einmalig gebaut, nie invalidiert (wie
    // CollectionData.GetAllEntries() selbst).
    private Dictionary<(CollectibleType Type, uint Id), string>? blacklistZoneLookupCache;

    private Dictionary<(CollectibleType Type, uint Id), string> GetBlacklistZoneLookup() =>
        blacklistZoneLookupCache ??= plugin.GetGlobalEntries()
            .Where(e => e.TerritoryTypeId != 0)
            .GroupBy(e => (e.Type, e.Id))
            .ToDictionary(g => g.Key, g => Plugin.GetZoneName(g.First().TerritoryTypeId));

    /// <summary>
    /// Ruft CodexWidgets.ShortcutHint mit den lokalisierten Tastennamen/dem Begleittext der Blacklist-
    /// Seite auf, mit 18px Abstand danach (DESIGN_SPEC) - von DrawBlacklistPage direkt unter der
    /// goldenen Trennlinie verwendet (einmalig, auch im leeren Zustand - DrawBlacklistEmptyState
    /// zeigt darunter nur noch die "Nothing hidden yet"-Box, OHNE das Tastenkürzel ein zweites Mal zu
    /// wiederholen, siehe Nutzer-Report "Box erscheint zweimal"). Breite auf denselben rechten
    /// Randabstand begrenzt wie der "Schließen"-Knopf (Nutzervorgabe), statt der vollen Fensterbreite.
    /// </summary>
    private static void DrawBlacklistShortcutHint(float scale, bool leadingGap = true)
    {
        if (leadingGap)
            ImGui.Dummy(new Vector2(0f, 18f * scale));
        CodexWidgets.ShortcutHint(
            new[]
            {
                Loc.T("Strg", "Ctrl"),
                Loc.T("Umschalt", "Shift"),
                Loc.T("Klick", "Click"),
            },
            Loc.T("auf einen Eintrag im Overlay setzt ihn auf die Blacklist.", "on an entry in the overlay adds it to the blacklist."),
            ImGui.GetContentRegionAvail().X - 32f * scale);
        ImGui.Dummy(new Vector2(0f, 18f * scale));
    }

    /// <summary>Leerer Zustand (DESIGN_SPEC) - ersetzt Werkzeugzeile und Tabelle komplett, wenn die Blacklist leer ist.</summary>
    private void DrawBlacklistEmptyState(float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - 32f * scale;
        var height = 220f * scale;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(CodexTheme.BgCard), CodexTheme.RoundingCard);
        drawList.AddRect(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(CodexTheme.LineCard), CodexTheme.RoundingCard);

        float iconHeight, labelHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            // Bug (Nutzer-Report "Box ist leer"): SetWindowFontScale wurde hier gesetzt, aber vor
            // Verlassen des using-Blocks NIE wieder auf 1 zurückgesetzt - blieb dadurch für den Rest
            // dieses Fensters in diesem Frame aktiv und verzerrte alle nachfolgenden Positionen/Größen
            // auf der Seite, wodurch der restliche Inhalt effektiv unsichtbar/falsch platziert wurde.
            ImGui.SetWindowFontScale(24f / ImGui.GetFontSize());
            iconHeight = ImGui.CalcTextSize(FontAwesomeIcon.Ban.ToIconString()).Y;
            ImGui.SetWindowFontScale(1f);
        }
        using (CodexTheme.FontSubtitleItalic.Push())
            labelHeight = ImGui.GetTextLineHeight();

        var contentY = cursor.Y + (height - (iconHeight + 8f * scale + labelHeight)) / 2f;

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = FontAwesomeIcon.Ban.ToIconString();
            ImGui.SetWindowFontScale(24f / ImGui.GetFontSize());
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(new Vector2(cursor.X + (width - glyphSize.X) / 2f, contentY), ImGui.GetColorU32(CodexTheme.TextDim), glyph);
            ImGui.SetWindowFontScale(1f);
        }

        var emptyLabel = Loc.T("Noch nichts ausgeblendet.", "Nothing hidden yet.");
        using (CodexTheme.FontSubtitleItalic.Push())
        {
            var labelSize = ImGui.CalcTextSize(emptyLabel);
            drawList.AddText(new Vector2(cursor.X + (width - labelSize.X) / 2f, contentY + iconHeight + 8f * scale), ImGui.GetColorU32(CodexTheme.TextTertiary), emptyLabel);
        }

        // Nutzer-Report: bei leerer Blacklist erschien das Tastenkürzel-Hinweisfeld zweimal (einmal
        // oben auf der Seite, siehe DrawBlacklistPage, und hier nochmal darunter) - der zweite,
        // redundante Aufruf wurde entfernt. ImGui.Dummy registriert die Box-Höhe trotzdem im Layout
        // (wichtig für die Scroll-Höhe des umgebenden Inhaltsbereichs), statt nur per
        // SetCursorScreenPos "unsichtbar" weiterzuspringen.
        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>Werkzeugzeile (DESIGN_SPEC): Suchfeld + "Types ▾"-Filter, wie MainWindow.DrawBlacklistPage, nur neu gestaltet.</summary>
    private void DrawBlacklistToolbar(float scale, Configuration config)
    {
        var filterLabel = Loc.T("Typen ▾", "Types ▾");
        float filterTextWidth;
        using (CodexTheme.FontFilterButton.Push())
            filterTextWidth = ImGui.CalcTextSize(filterLabel).X;

        var filterPadding = new Vector2(12f * scale, 7f * scale);
        var gap = 10f * scale;
        var searchPadding = new Vector2(14f * scale, 9f * scale);
        // Derselbe rechte Randabstand wie beim "Schließen"-Knopf (Nutzervorgabe: der Filter-Knopf soll
        // bis dorthin reichen, statt mit Leerraum davor zu enden).
        var rightMargin = 32f * scale;

        ImGui.PushStyleColor(ImGuiCol.FrameBg, CodexTheme.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineControl);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, searchPadding);
        // Breite erst berechnet, NACHDEM die Mindestbreite des Filter-Knopfs feststeht (Lupen-Symbol
        // entfernt, Nutzervorgabe) - die Höhe übernimmt der Filter-Knopf unten 1:1 von diesem Suchfeld.
        // Nutzervorgabe: Suchleiste 30px schmaler als ursprünglich.
        var filterMinWidth = filterTextWidth + filterPadding.X * 2f;
        var searchWidth = ImGui.GetContentRegionAvail().X - rightMargin - filterMinWidth - gap - 30f * scale;
        ImGui.SetNextItemWidth(searchWidth);
        using (CodexTheme.FontDatabaseSearchInput.Push())
            ImGui.InputTextWithHint("##CodexBlacklistSearch", Loc.T("Blacklist durchsuchen...", "Search blacklist..."), ref blacklistSearch, 100);
        var searchHeight = ImGui.GetItemRectSize().Y;
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // Nutzervorgabe: Filter-Knopf reicht bis zum rechten Rand (wie der "Schließen"-Knopf), statt
        // nur inhaltsbreit zu sein - füllt den nach der schmaleren Suchleiste übrigen Platz komplett.
        // Höhe weiterhin identisch zur Suchleiste statt einer eigenen (kleineren) Höhe.
        var filterWidth = ImGui.GetContentRegionAvail().X - rightMargin - gap - searchWidth;
        var filterSize = new Vector2(filterWidth, searchHeight);

        var anyFilterActive = blacklistHiddenTypes.Count > 0;
        ImGui.SameLine(0f, gap);
        var filterCursor = ImGui.GetCursorScreenPos();
        var filterClicked = ImGui.InvisibleButton("##CodexBlacklistTypeFilter", filterSize);
        var filterHovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var filterBg = anyFilterActive ? CodexTheme.BgSelectedStrong : filterHovered ? CodexTheme.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var filterBorder = anyFilterActive ? CodexTheme.Accent : CodexTheme.LineControl;
        var filterFg = anyFilterActive ? CodexTheme.TextHeading : CodexTheme.TextSecondary;
        drawList.AddRectFilled(filterCursor, filterCursor + filterSize, ImGui.GetColorU32(filterBg), CodexTheme.RoundingControl);
        drawList.AddRect(filterCursor, filterCursor + filterSize, ImGui.GetColorU32(filterBorder), CodexTheme.RoundingControl);
        using (CodexTheme.FontFilterButton.Push())
        {
            var displayLabel = anyFilterActive ? $"◆ {filterLabel}" : filterLabel;
            var textSize = ImGui.CalcTextSize(displayLabel);
            drawList.AddText(filterCursor + (filterSize - textSize) / 2f, ImGui.GetColorU32(filterFg), displayLabel);
        }

        if (filterClicked)
            ImGui.OpenPopup("##CodexBlacklistTypeFilterPopup");

        if (ImGui.BeginPopup("##CodexBlacklistTypeFilterPopup"))
        {
            foreach (var type in config.TypeOrder)
            {
                var enabled = !blacklistHiddenTypes.Contains(type);
                if (ImGui.Checkbox($"{Loc.TypeName(type)}##CodexBlacklistTypeFilterEntry", ref enabled))
                {
                    if (enabled)
                        blacklistHiddenTypes.Remove(type);
                    else
                        blacklistHiddenTypes.Add(type);
                }
            }
            ImGui.EndPopup();
        }
    }

    private void DrawBlacklistTable(float scale, List<BlacklistedEntry> filtered, Dictionary<(CollectibleType Type, uint Id), string> zoneLookup)
    {
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, CodexTheme.BgCard with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0f, 0f, 0f, 0f));
        // DESIGN_SPEC: Trennlinie zwischen den Datenzeilen in LineRow (statt des sonst üblichen
        // LineSubtle, siehe DrawDatabaseTable) - eigene Zeilenfarbe speziell für die Blacklist-Tabelle.
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, CodexTheme.LineRow);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, CodexTheme.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(16f * scale, 4f * scale));

        var tableWidth = ImGui.GetContentRegionAvail().X - 32f * scale;
        var tableHeight = MathF.Max(100f * scale,
            ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetCursorScreenPos().Y - 20f * scale);

        if (ImGui.BeginTable("##CodexBlacklistTable", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.ScrollY,
                new Vector2(tableWidth, tableHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(Loc.T("TYP", "TYPE"), ImGuiTableColumnFlags.WidthFixed, 110f * scale);
            ImGui.TableSetupColumn(Loc.T("NAME", "NAME"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T("ZONE", "ZONE"), ImGuiTableColumnFlags.WidthFixed, 150f * scale);
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 120f * scale);

            var headerHeight = 34f * scale;
            var headerTopScreenY = ImGui.GetCursorScreenPos().Y;
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CodexTheme.BgPopup));
            // Nutzervorgabe: dieselbe Textgröße wie die Datenbank-Kopfzeile, statt des ursprünglich
            // nach DESIGN_SPEC vorgesehenen eigenen, kleineren 11px-Handles.
            using (CodexTheme.FontDatabaseHeader.Push())
            {
                var textHeight = ImGui.GetTextLineHeight();
                for (var c = 0; c < 4; c++)
                {
                    ImGui.TableSetColumnIndex(c);
                    var label = ImGui.TableGetColumnName(c);
                    if (string.IsNullOrEmpty(label))
                        continue;
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerHeight - textHeight) / 2f);
                    CodexTheme.DrawSpacedText(label.ToUpperInvariant(), CodexTheme.TextTertiary, 1f * scale);
                }
            }

            var tableMinX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMin().X;
            var tableMaxX = tableMinX + tableWidth;
            var headerBottomY = headerTopScreenY + headerHeight;
            ImGui.GetWindowDrawList().AddLine(new Vector2(tableMinX, headerBottomY), new Vector2(tableMaxX, headerBottomY), ImGui.GetColorU32(CodexTheme.LineCard));

            var rowHeight = 44f * scale;
            float cellContentHeight;
            using (CodexTheme.FontDatabaseItemName.Push())
                cellContentHeight = ImGui.GetTextLineHeight();

            BlacklistedEntry? toRemove = null;

            foreach (var item in filtered)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                var rowTopY = ImGui.GetCursorPosY();
                var cellY = rowTopY + (rowHeight - cellContentHeight) / 2f;

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                var (badgeBg, badgeFg) = CodexOverlayWindow.GetTypeBadgeColors(item.Type);
                var badgeLabel = CodexOverlayWindow.GetBadgeLabel(item.Type);
                var badgeSize = new Vector2(62f * scale, cellContentHeight + 4f * scale);
                var badgeCursor = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(badgeBg), 3f * scale);
                using (CodexTheme.FontBlacklistTypeBadge.Push())
                {
                    var labelSize = ImGui.CalcTextSize(badgeLabel);
                    ImGui.GetWindowDrawList().AddText(badgeCursor + (badgeSize - labelSize) / 2f, ImGui.GetColorU32(badgeFg), badgeLabel);
                }
                ImGui.Dummy(badgeSize);

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                // Nutzervorgabe: dieselbe Textgröße wie der Item-Name in der Datenbank-Spalte.
                using (CodexTheme.FontDatabaseItemName.Push())
                    ImGui.TextColored(CodexTheme.TextPrimary, item.Name);

                // Nutzervorgabe: Icon+Label 2px nach unten versetzt (unabhängig vom Namen) - SameLine
                // setzt die Y-Position sonst wieder auf den Zeilenanfang zurück, daher vor JEDEM der
                // beiden Widgets erneut gesetzt.
                var hiddenLabelY = cellY + 2f * scale;
                ImGui.SameLine(0f, 8f * scale);
                ImGui.SetCursorPosY(hiddenLabelY);
                using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                {
                    ImGui.SetWindowFontScale(13f / ImGui.GetFontSize());
                    ImGui.TextColored(CodexTheme.TextMuted, FontAwesomeIcon.EyeSlash.ToIconString());
                    ImGui.SetWindowFontScale(1f);
                }
                ImGui.SameLine(0f, 4f * scale);
                ImGui.SetCursorPosY(hiddenLabelY);
                using (CodexTheme.FontBlacklistBody.Push())
                    ImGui.TextColored(CodexTheme.TextMuted, Loc.T("ausgeblendet", "hidden"));

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(cellY);
                var zone = zoneLookup.GetValueOrDefault((item.Type, item.Id));
                // Nutzervorgabe: dieselbe Textgröße wie die Zone in der Datenbank-Spalte.
                using (CodexTheme.FontDatabaseItemText.Push())
                    ImGui.TextColored(string.IsNullOrEmpty(zone) ? CodexTheme.TextDim : CodexTheme.TextSecondary, string.IsNullOrEmpty(zone) ? "—" : zone);

                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(rowTopY);
                if (DrawBlacklistRestoreButton(scale, item, rowHeight))
                    toRemove = item;
            }

            ImGui.EndTable();

            if (toRemove != null)
                Plugin.RemoveFromBlacklist(toRemove.Type, toRemove.Id);
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        DrawBlacklistTableFooter(scale);
    }

    /// <summary>"↺ Restore"-Knopf, vertikal mittig zur VOLLEN Zeilenhöhe (Nutzervorgabe) statt nur zur Namens-Zeilenhöhe - erwartet daher rowTopY (Zeilenanfang, siehe DrawBlacklistTable) statt eines bereits zentrierten cellY. Icon und Label in getrennten AddText-Aufrufen (jeweils eigene Schrift) statt eines gemeinsamen Strings - ein FontAwesome-Glyph im Fließtext der normalen Schrift würde sonst als fehlendes Zeichen dargestellt.</summary>
    private static bool DrawBlacklistRestoreButton(float scale, BlacklistedEntry item, float rowHeight)
    {
        var label = Loc.T("Zurücksetzen", "Restore");
        var padding = new Vector2(10f * scale, 4f * scale);
        var iconGap = 5f * scale;

        float iconWidth, textWidth, textHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.Undo.ToIconString()).X;
        using (CodexTheme.FontBlacklistButtonLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var buttonSize = new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        // Rechtsbündig in der Spalte (DESIGN_SPEC: Spalte ohne Titel, rechtsbündig), vertikal mittig
        // zur vollen Zeilenhöhe (Nutzervorgabe).
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonSize.X);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - buttonSize.Y) / 2f);
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##CodexBlacklistRestore{item.Type}{item.Id}", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = hovered ? CodexTheme.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        var border = hovered ? CodexTheme.Accent : CodexTheme.LineControl;
        drawList.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        var contentCursor = cursor + new Vector2(padding.X, (buttonSize.Y - textHeight) / 2f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextHeading), FontAwesomeIcon.Undo.ToIconString());
        contentCursor.X += iconWidth + iconGap;
        using (CodexTheme.FontBlacklistButtonLabel.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextHeading), label);

        return clicked;
    }

    private static void DrawBlacklistTableFooter(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 26f * scale));

        var drawList = ImGui.GetWindowDrawList();
        var lineY = ImGui.GetCursorScreenPos().Y - 13f * scale;
        var min = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMin().X, lineY);
        var max = new Vector2(min.X + ImGui.GetContentRegionAvail().X - 32f * scale, lineY);
        drawList.AddLine(min, max, ImGui.GetColorU32(CodexTheme.LineRow));

        var line1 = Loc.T("Das war's - alles Ausgeblendete.", "That's everything you've hidden.");
        var line2 = Loc.T("Wiederhergestellte Einträge erscheinen sofort wieder im Overlay.", "Restored entries reappear in the overlay right away.");

        var contentWidth = ImGui.GetContentRegionAvail().X - 32f * scale;
        using (CodexTheme.FontSubtitleItalic.Push())
        {
            var size = ImGui.CalcTextSize(line1);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (contentWidth - size.X) / 2f);
            ImGui.TextColored(CodexTheme.TextTertiary, line1);
        }
        using (CodexTheme.FontBlacklistBody.Push())
        {
            var size = ImGui.CalcTextSize(line2);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (contentWidth - size.X) / 2f);
            ImGui.TextColored(CodexTheme.TextMuted, line2);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    // ---- Seite "About" (Nutzeranforderung, Vorbild referenz/codex-about.png) - komplette
    // Neugestaltung der bestehenden MainWindow.DrawAboutPage im Codex-Theme. Links (Ko-fi/GitHub-
    // Issues) und die Versionsermittlung bleiben unverändert (siehe MainWindow.VersionText/IconPath-
    // Vorbild), nur Aufbau und Darstellung sind neu - alles horizontal zentriert, OHNE den sonst
    // gemeinsamen Seitenkopf aus Titel/Untertitel/Trenn-Ornament (siehe DrawContent-Kommentar).

    private const string RepoUrl = "https://github.com/stoni89/explorers-codex";

    private void DrawAboutPage(float scale)
    {
        // Nutzervorgabe: NICHT mehr symmetrisch - 60px Rand links (80px minus 20px), 80px Rand
        // rechts. Die "Charted by Hand"-Box selbst sitzt dadurch direkt an diesen beiden Rändern
        // (siehe DrawAboutCard/DrawAboutFollowSection), alle übrigen (schmaleren) Elemente bleiben
        // weiterhin zu dieser Box-Breite zentriert (Nutzervorgabe: "Icon horizontal mittig zur Box").
        var (boxX, boxWidth) = GetAboutBoxBounds(scale);

        ImGui.SetCursorPosX(boxX);
        CodexWidgets.SealLogo(150f, boxWidth);

        ImGui.Dummy(new Vector2(0f, 24f * scale));

        const string pluginName = "The Explorer's Codex";
        using (CodexTheme.FontAboutTitle.Push())
        {
            ImGui.SetCursorPosX(boxX);
            CodexWidgets.CenterNext(ImGui.CalcTextSize(pluginName).X, boxWidth);
            ImGui.TextColored(CodexTheme.TextHeading, pluginName);
        }

        // Nutzervorgabe: insgesamt 13px näher an den Titel (5px + weitere 8px, ursprünglich 6px
        // Abstand) - negativer Versatz statt Dummy, da der verbleibende Abstand sonst nicht mehr
        // ausreicht, um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 7f * scale);
        var tagline = Loc.T("Dein Begleiter für Sammelobjekte in ganz Eorzea.", "Your companion for collectibles across Eorzea.");
        using (CodexTheme.FontAboutSubtitle.Push())
        {
            ImGui.SetCursorPosX(boxX);
            CodexWidgets.CenterNext(ImGui.CalcTextSize(tagline).X, boxWidth);
            ImGui.TextColored(CodexTheme.TextSecondary, tagline);
        }

        // Nutzervorgabe: 5px näher an den Untertitel (ursprünglich 8px Abstand).
        ImGui.Dummy(new Vector2(0f, 3f * scale));
        DrawAboutVersionBadge(scale, boxX, boxWidth);

        // Nutzervorgabe: insgesamt 8px näher an die Version (5px + 5px + 5px, dann wieder 4px + 3px
        // nach unten, ursprünglich 12px Abstand) - negativer Versatz statt Dummy, der verbleibende
        // Abstand reicht nicht mehr aus, um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f * scale);
        ImGui.SetCursorPosX(boxX);
        CodexWidgets.CenterNext(CodexTheme.DividerOrnamentWidth, boxWidth);
        CodexTheme.DrawDividerOrnament();

        // Nutzervorgabe: Box nochmal ca. 6px nach unten versetzt.
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        DrawAboutCard(scale);

        ImGui.Dummy(new Vector2(0f, 24f * scale));
        DrawAboutFollowSection(scale);
    }

    /// <summary>
    /// Linke lokale X-Position und Breite der "Charted by Hand"-Box - Nutzervorgabe: NICHT mehr
    /// symmetrisch zentriert, sondern 60px Rand links / 80px Rand rechts. Alle übrigen Elemente der
    /// Seite (Logo/Titel/Untertitel/Version/Trenn-Ornament/Follow-Abschnitt) zentrieren sich
    /// weiterhin ZU dieser Breite (siehe Aufrufer), sitzen also selbst nicht zwangsläufig an den
    /// exakt gleichen 60px/80px-Rändern wie die Box, sondern innerhalb von deren Spannweite mittig.
    /// </summary>
    private static (float LocalX, float Width) GetAboutBoxBounds(float scale)
    {
        const float leftMargin = 60f;
        const float rightMargin = 80f;
        var localX = ImGui.GetCursorPosX() + leftMargin * scale;
        var width = ImGui.GetContentRegionAvail().X - (leftMargin + rightMargin) * scale;
        return (localX, width);
    }

    private static void DrawAboutVersionBadge(float scale, float localX, float availableWidth)
    {
        var text = string.Format(Loc.T("Version {0}", "Version {0}"), VersionText);
        var padding = new Vector2(10f * scale, 2f * scale);

        float textWidth, textHeight;
        using (CodexTheme.FontAboutVersionBadge.Push())
        {
            var size = ImGui.CalcTextSize(text);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        ImGui.SetCursorPosX(localX);
        CodexWidgets.CenterNext(badgeSize.X, availableWidth);
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(CodexTheme.LineControl), 3f * scale);
        using (CodexTheme.FontAboutVersionBadge.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(CodexTheme.TextSecondary), text);

        ImGui.Dummy(badgeSize);
    }

    /// <summary>
    /// "Charted by Hand"-Karte (DESIGN_SPEC) - Hintergrund wie CodexTheme.BeginCard/EndCard erst
    /// NACH dem Inhalt über ChannelsSplit/ChannelsMerge gezeichnet (passt sich so der tatsächlichen
    /// Texthöhe an), aber mit eigenem, asymmetrischem Innenabstand (22 oben/30 seitlich/24 unten)
    /// statt BeginCard/EndCards einheitlichem Innenabstand - daher hier eigenständig statt über
    /// BeginCard/EndCard umgesetzt. Die Karte selbst sitzt NICHT zentriert, sondern direkt an den in
    /// GetAboutBoxBounds festgelegten (asymmetrischen) Rändern - ihr Inhalt zentriert sich DARIN
    /// gegen die lokale Innenbreite (contentWidth), nicht gegen die volle Seitenbreite - siehe
    /// CodexWidgets.CenterNext/CenteredWrappedText-Kommentar.
    /// </summary>
    private void DrawAboutCard(float scale)
    {
        var paddingTop = 22f * scale;
        var paddingSide = 30f * scale;
        var paddingBottom = 24f * scale;

        var (boxX, cardWidth) = GetAboutBoxBounds(scale);
        var contentWidth = cardWidth - paddingSide * 2f;

        ImGui.SetCursorPosX(boxX);
        var cardOrigin = ImGui.GetCursorScreenPos();
        var contentLocalX = boxX + paddingSide;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorPosX(contentLocalX);
        ImGui.Dummy(new Vector2(0f, paddingTop));

        // Raute mit Herz-Symbol (DESIGN_SPEC: Quadrat 38x38px, um 45° gedreht, Rahmen Accent, Füllung
        // BgSelected). Ein um 45° gedrehtes Quadrat der Kantenlänge "diamondEdge" hat eine (axis-
        // aligned) Spitze-zu-Spitze-Spannweite von diamondEdge*sqrt(2) - das reservierte Layout
        // (CenterNext/Dummy) muss daher diese größere Spannweite nutzen, nicht die Kantenlänge selbst,
        // sonst würde die Raute über den dafür vorgesehenen Platz hinausragen.
        var diamondEdge = 38f * scale;
        var diamondBounds = diamondEdge * 1.41421356f;
        ImGui.SetCursorPosX(contentLocalX);
        CodexWidgets.CenterNext(diamondBounds, contentWidth);
        var diamondCursor = ImGui.GetCursorScreenPos();
        var diamondCenter = diamondCursor + new Vector2(diamondBounds / 2f);
        CodexWidgets.Diamond(diamondCenter, diamondEdge, ImGui.GetColorU32(CodexTheme.BgSelected), ImGui.GetColorU32(CodexTheme.Accent));
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = FontAwesomeIcon.Heart.ToIconString();
            var nativeIconPx = ImGui.GetFontSize();
            ImGui.SetWindowFontScale(16f * scale / nativeIconPx);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(diamondCenter - glyphSize / 2f, ImGui.GetColorU32(CodexTheme.Accent), glyph);
            ImGui.SetWindowFontScale(1f);
        }
        ImGui.Dummy(new Vector2(diamondBounds, diamondBounds));

        // Titel - 8px Abstand unter der Raute (DESIGN_SPEC, abweichend vom sonstigen 10px-Abstand
        // zwischen den übrigen Teilen).
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        var cardTitle = Loc.T("Von Hand kartiert", "Charted by Hand");
        using (CodexTheme.FontAboutCardTitle.Push())
        {
            ImGui.SetCursorPosX(contentLocalX);
            CodexWidgets.CenterNext(ImGui.CalcTextSize(cardTitle).X, contentWidth);
            ImGui.TextColored(CodexTheme.TextCardTitle, cardTitle);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        var cardText = Loc.T(
            "Jede Seite dieses Codex entsteht in meiner Freizeit, Update für Update. Wenn er dir auf deinen Reisen ein paar Wege erspart hat, hält ein Kaffee auf Ko-fi die Tinte am Fließen – ganz freiwillig. Danke, dass du mit mir auf Entdeckungsreise gehst!",
            "Every page of this Codex is mapped out in my spare time, one update at a time. If it has saved you a few steps on your travels, a coffee on Ko-fi keeps the ink flowing – entirely optional. Thank you for exploring with me!");
        using (CodexTheme.FontAboutBody.Push())
            CodexWidgets.CenteredWrappedText(cardText, 520f * scale, ImGui.GetColorU32(CodexTheme.TextSecondary), contentLocalX, contentWidth);

        // Ko-fi-Knopf - 6px Abstand nach oben (DESIGN_SPEC, abweichend vom sonstigen 10px-Abstand),
        // nur inhaltsbreit statt über die volle Kartenbreite.
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.SetCursorPosX(contentLocalX);
        CodexWidgets.CenterNext(MeasureAboutKofiButtonSize(scale).X, contentWidth);
        DrawAboutKofiButton(scale);

        ImGui.Dummy(new Vector2(0f, paddingBottom));

        var cardMax = new Vector2(cardOrigin.X + cardWidth, ImGui.GetCursorScreenPos().Y);
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(cardOrigin, cardMax, ImGui.GetColorU32(CodexTheme.BgCard), CodexTheme.RoundingCard);
        drawList.AddRect(cardOrigin, cardMax, ImGui.GetColorU32(CodexTheme.LineCard), CodexTheme.RoundingCard);
        drawList.ChannelsMerge();
    }

    private static Vector2 MeasureAboutKofiButtonSize(float scale)
    {
        var label = Loc.T("Auf Ko-fi unterstützen", "Support on Ko-fi");
        var padding = new Vector2(20f * scale, 9f * scale);
        var iconGap = 8f * scale;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.MugHot.ToIconString()).X;
        float textWidth, textHeight;
        using (CodexTheme.FontAboutKofiLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
    }

    private static void DrawAboutKofiButton(float scale)
    {
        var label = Loc.T("Auf Ko-fi unterstützen", "Support on Ko-fi");
        var padding = new Vector2(20f * scale, 9f * scale);
        var iconGap = 8f * scale;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.MugHot.ToIconString()).X;
        float textWidth, textHeight;
        using (CodexTheme.FontAboutKofiLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var buttonSize = new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##CodexAboutKofi", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(CodexTheme.Accent with { W = hovered ? 0.85f : 1f }), CodexTheme.RoundingControl);

        var contentCursor = cursor + new Vector2(padding.X, (buttonSize.Y - textHeight) / 2f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextOnAccent), FontAwesomeIcon.MugHot.ToIconString());
        contentCursor.X += iconWidth + iconGap;
        using (CodexTheme.FontAboutKofiLabel.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextOnAccent), label);

        if (clicked)
            Util.OpenLink("https://ko-fi.com/horstbrot");
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    /// <summary>"Follow the Journey"-Abschnitt (DESIGN_SPEC) - Trennlinie mit Beschriftung, Hinweistext, zwei Rahmen-Knöpfe ("Report a bug"/"Suggest a feature") zu den GitHub-Issues, dieselbe Breite wie die Karte darüber.</summary>
    private void DrawAboutFollowSection(float scale)
    {
        var (localX, cardWidth) = GetAboutBoxBounds(scale);

        ImGui.SetCursorPosX(localX);
        using (CodexTheme.FontAboutDividerLabel.Push())
            CodexWidgets.LabeledDivider(Loc.T("Folge der Reise", "Follow the Journey").ToUpperInvariant(), cardWidth, cardWidth);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        var hint = Loc.T(
            "Einen Fehler gefunden oder eine Idee fürs nächste Update? Sag mir auf GitHub Bescheid.",
            "Found a bug or have an idea for the next update? Let me know on GitHub.");
        using (CodexTheme.FontAboutHint.Push())
        {
            ImGui.SetCursorPosX(localX);
            CodexWidgets.CenterNext(ImGui.CalcTextSize(hint).X, cardWidth);
            ImGui.TextColored(CodexTheme.TextSecondary, hint);
        }

        // Nutzervorgabe: 2px näher an das Label darüber (ursprünglich 10px Abstand).
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        var gap = 10f * scale;
        var bugSize = MeasureAboutLinkButtonSize(FontAwesomeIcon.Bug, Loc.T("Fehler melden", "Report a bug"), scale);
        var featureSize = MeasureAboutLinkButtonSize(FontAwesomeIcon.Lightbulb, Loc.T("Feature vorschlagen", "Suggest a feature"), scale);
        var totalWidth = bugSize.X + gap + featureSize.X;

        ImGui.SetCursorPosX(localX);
        CodexWidgets.CenterNext(totalWidth, cardWidth);
        DrawAboutLinkButton("##CodexAboutBug", FontAwesomeIcon.Bug, Loc.T("Fehler melden", "Report a bug"), bugSize, $"{RepoUrl}/issues/new?labels=bug");
        ImGui.SameLine(0f, gap);
        DrawAboutLinkButton("##CodexAboutFeature", FontAwesomeIcon.Lightbulb, Loc.T("Feature vorschlagen", "Suggest a feature"), featureSize, $"{RepoUrl}/issues/new?labels=enhancement");
    }

    private static Vector2 MeasureAboutLinkButtonSize(FontAwesomeIcon icon, string label, float scale)
    {
        var padding = new Vector2(16f * scale, 7f * scale);
        var iconGap = 6f * scale;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        float textWidth, textHeight;
        using (CodexTheme.FontAboutLinkButtonLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
    }

    /// <summary>Nutzervorgabe "Icon + Text zentral setzen": Icon und Label werden JEWEILS gegen die eigene Glyphen-/Texthöhe vertikal zentriert, statt beide (wie zuvor) gegen die Label-Texthöhe - FontAwesome-Icons haben eine andere native Höhe als der Fließtext, wodurch das Icon sonst leicht über/unter der echten Mitte saß.</summary>
    private static void DrawAboutLinkButton(string id, FontAwesomeIcon icon, string label, Vector2 buttonSize, string url)
    {
        var iconGap = 6f * ImGuiHelpers.GlobalScale;

        float iconWidth, iconHeight, textWidth, textHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconSize = ImGui.CalcTextSize(icon.ToIconString());
            iconWidth = iconSize.X;
            iconHeight = iconSize.Y;
        }
        using (CodexTheme.FontAboutLinkButtonLabel.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            textWidth = labelSize.X;
            textHeight = labelSize.Y;
        }

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, buttonSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = hovered ? CodexTheme.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        var border = hovered ? CodexTheme.Accent : CodexTheme.LineControl;
        drawList.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        var contentWidth = iconWidth + iconGap + textWidth;
        var contentLeft = cursor.X + (buttonSize.X - contentWidth) / 2f;
        var contentCursor = new Vector2(contentLeft, cursor.Y + (buttonSize.Y - iconHeight) / 2f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextHeading), icon.ToIconString());
        contentCursor = new Vector2(contentLeft + iconWidth + iconGap, cursor.Y + (buttonSize.Y - textHeight) / 2f);
        using (CodexTheme.FontAboutLinkButtonLabel.Push())
            drawList.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextHeading), label);

        if (clicked)
            Util.OpenLink(url);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    // ---- Seite "Statistics" (Nutzeranforderung, Vorbild referenz/codex-statistik.png) - komplette
    // Neugestaltung der bestehenden MainWindow.DrawStatisticsPage im Codex-Theme. Zähl-Logik bleibt
    // identisch (Plugin.StatisticsTypes + Plugin.GetAllTrackedQuestIds, dafür dort auf internal
    // gestellt), nur Aufbau und Darstellung sind neu. Die Gruppen-Zuordnung (Collections/Progress)
    // liegt als feste Liste in Plugin.GetStatisticsGroup (Nutzeranforderung: "in der Plugin-Logik").

    private void DrawStatisticsPage(float scale)
    {
        var rows = GetStatisticsRows();
        if (rows.Count == 0)
        {
            using (CodexTheme.FontSubtitleItalic.Push())
                ImGui.TextColored(CodexTheme.TextTertiary, Loc.T("Nichts gefunden.", "Nothing found."));
            return;
        }

        var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
        var totalOwned = rows.Sum(r => r.Owned);
        var totalCount = rows.Sum(r => r.Total);
        var furthest = rows.OrderByDescending(r => r.Total == 0 ? 0f : r.Owned / (float)r.Total).First();

        DrawStatisticsTiles(scale, rows.Count, totalOwned, totalCount, furthest, culture);
        // Nutzervorgabe: Abstand zum oberen Rand der Box um 10px kleiner (ursprünglich 18px).
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var collectionsRows = rows.Where(r => Plugin.GetStatisticsGroup(r.Type) == Plugin.StatisticsGroup.Collections).ToList();
        var progressRows = rows.Where(r => Plugin.GetStatisticsGroup(r.Type) == Plugin.StatisticsGroup.Progress).ToList();

        if (collectionsRows.Count > 0)
        {
            DrawStatisticsGroup(scale, "##CodexStatsCollections", Loc.T("Sammlungen", "Collections"), collectionsRows, culture);
            // Nutzervorgabe: Abstand zum unteren Rand der Box (zur nächsten Box) um 10px kleiner
            // (ursprünglich 18px).
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        if (progressRows.Count > 0)
            DrawStatisticsGroup(scale, "##CodexStatsProgress", Loc.T("Fortschritt", "Progress"), progressRows, culture);
    }

    /// <summary>Dieselbe Zähl-Logik wie MainWindow.DrawStatisticsPage (nur zonengebundene, deduplizierte Einträge + die live berechnete, zonenunabhängige Quest-Liste) - hier einmalig als Liste statt direkt gezeichnet, damit sowohl die Kacheln als auch die Gruppen-Tabellen darauf zugreifen können.</summary>
    // Nutzer-Report "Rendern dauert extrem lange": GetStatisticsRows wertet pro Aufruf GroupBy/Where/
    // IsOwned über alle ~3100 globalen Einträge aus - bei 60 FPS summierte sich das spürbar, obwohl
    // sich der Besitzstatus nur selten (durch Spielaktionen, nicht durch UI-Interaktion) ändert.
    // Daher wie GetDatabaseRows/GetBlacklistZoneLookup zeitlich gedrosselt statt bei jedem Draw-Aufruf
    // neu berechnet - alle 500ms reicht für eine "Statistik"-Seite völlig aus.
    private List<(CollectibleType Type, int Owned, int Total)>? statisticsRowsCache;
    private long statisticsRowsCacheTime;

    private List<(CollectibleType Type, int Owned, int Total)> GetStatisticsRows()
    {
        var now = Environment.TickCount64;
        if (statisticsRowsCache != null && now - statisticsRowsCacheTime < 500)
            return statisticsRowsCache;

        statisticsRowsCache = ComputeStatisticsRows();
        statisticsRowsCacheTime = now;
        return statisticsRowsCache;
    }

    private List<(CollectibleType Type, int Owned, int Total)> ComputeStatisticsRows()
    {
        var entries = CollectionData.GetAllEntries()
            .Where(e => e.TerritoryTypeId != 0)
            .GroupBy(e => (e.Type, e.Id))
            .Select(g => g.First())
            .ToList();

        var rows = new List<(CollectibleType, int, int)>();
        foreach (var type in Plugin.StatisticsTypes)
        {
            var typeEntries = entries.Where(e => e.Type == type).ToList();
            if (typeEntries.Count == 0)
                continue;
            rows.Add((type, typeEntries.Count(plugin.IsOwned), typeEntries.Count));
        }

        var questIds = plugin.GetAllTrackedQuestIds();
        if (questIds.Count > 0)
        {
            var questsOwned = questIds.Count(id => plugin.IsOwned(new CollectibleEntry { Id = id, Type = CollectibleType.Quest }));
            rows.Add((CollectibleType.Quest, questsOwned, questIds.Count));
        }

        // Nutzeranforderung: Sightseeing in der Progress-Gruppe (siehe Plugin.StatisticsGroupByType) -
        // wie Quest zonenunabhängig live berechnet (GetSightseeingEntries), nicht über
        // CollectionData.GetAllEntries() (siehe Plugin.StatisticsTypes-Kommentar).
        var sightseeingEntries = Plugin.GetSightseeingEntries();
        if (sightseeingEntries.Count > 0)
        {
            var sightseeingOwned = sightseeingEntries.Count(plugin.IsOwned);
            rows.Add((CollectibleType.Sightseeing, sightseeingOwned, sightseeingEntries.Count));
        }

        return rows;
    }

    private static void DrawStatisticsTiles(float scale, int categoryCount, int totalOwned, int totalCount,
        (CollectibleType Type, int Owned, int Total) furthest, CultureInfo culture)
    {
        var gap = 14f * scale;
        // Nutzervorgabe: derselbe rechte Randabstand wie bei den Karten auf der "Allgemein"-Seite
        // (siehe DrawGeneralPage), daher explizite Tabellenbreite statt der vollen verfügbaren Breite.
        var tableWidth = ImGui.GetContentRegionAvail().X - 32f * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(gap / 2f, 0f));
        if (ImGui.BeginTable("##CodexStatsTiles", 3, ImGuiTableFlags.None, new Vector2(tableWidth, 0f)))
        {
            ImGui.TableSetupColumn("##CodexStatsTileTotal", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("##CodexStatsTileFurthest", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##CodexStatsTileRemaining", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            // Nutzervorgabe: alle drei Kacheln gleich hoch - die "Total discovered"-Kachel hat den
            // meisten Inhalt (Zahlenzeile + Balken) und ist daher die größte; ihre tatsächliche
            // Inhaltshöhe wird gemessen und den anderen beiden als Mindesthöhe vorgegeben (siehe
            // dortige Füll-Dummy vor EndCard).
            var totalTileContentHeight = DrawStatisticsTotalTile(scale, totalOwned, totalCount, culture);
            // Nutzervorgabe: "Furthest along"/"Still to find" insgesamt 5px niedriger als "Total
            // discovered" (2px + 2px + weitere 1px).
            var smallTileContentHeight = totalTileContentHeight - 5f * scale;

            ImGui.TableSetColumnIndex(1);
            DrawStatisticsFurthestTile(scale, furthest, culture, smallTileContentHeight);

            ImGui.TableSetColumnIndex(2);
            DrawStatisticsRemainingTile(scale, totalCount - totalOwned, categoryCount, culture, smallTileContentHeight);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    /// <summary>Gibt die tatsächliche Inhaltshöhe zurück (siehe DrawStatisticsTiles) - die anderen beiden Kacheln füllen bis zu dieser Höhe auf, damit alle drei gleich hoch werden.</summary>
    private static float DrawStatisticsTotalTile(float scale, int owned, int total, CultureInfo culture)
    {
        var origin = ImGui.GetCursorScreenPos();
        CodexTheme.BeginCard(scale);
        using (CodexTheme.FontStatsLabel.Push())
            CodexTheme.DrawSpacedText(Loc.T("INSGESAMT ENTDECKT", "TOTAL DISCOVERED"), CodexTheme.TextTertiary, 1f * scale);

        // Nutzervorgabe: insgesamt 13px näher ans Label (7px + weitere 6px, ursprünglich 8px
        // Abstand) - negativer Versatz statt Dummy, der verbleibende Abstand reicht nicht mehr aus,
        // um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5f * scale);

        var fraction = total == 0 ? 0f : owned / (float)total;
        var percentText = (fraction * 100f).ToString("0.0", culture) + " %";
        var numberText = owned.ToString("N0", culture);
        var suffixText = $"/ {total.ToString("N0", culture)}";

        float numberHeight, suffixHeight, percentHeight, numberWidth, suffixWidth, percentWidth;
        using (CodexTheme.FontStatsBigNumber.Push())
        {
            var size = ImGui.CalcTextSize(numberText);
            numberWidth = size.X;
            numberHeight = size.Y;
        }
        using (CodexTheme.FontStatsSuffix.Push())
        {
            var size = ImGui.CalcTextSize(suffixText);
            suffixWidth = size.X;
            suffixHeight = size.Y;
        }
        using (CodexTheme.FontStatsPercentBig.Push())
        {
            var size = ImGui.CalcTextSize(percentText);
            percentWidth = size.X;
            percentHeight = size.Y;
        }

        var rowHeight = MathF.Max(numberHeight, MathF.Max(suffixHeight, percentHeight));
        var rowOrigin = ImGui.GetCursorScreenPos();
        // Nutzervorgabe: rechts denselben Abstand zum Kartenrand wie links - BeginCard rückt den
        // Inhalt nur links per Indent ein, ImGui.GetContentRegionAvail() reicht aber bis zum echten
        // (ungepolsterten) rechten Kartenrand, daher hier zusätzlich um CardPaddingX gekürzt.
        var avail = ImGui.GetContentRegionAvail().X - CodexTheme.CardPaddingX;
        var drawList = ImGui.GetWindowDrawList();

        using (CodexTheme.FontStatsBigNumber.Push())
            drawList.AddText(new Vector2(rowOrigin.X, rowOrigin.Y + rowHeight - numberHeight), ImGui.GetColorU32(CodexTheme.TextHeading), numberText);
        // Nutzervorgabe: "/ 3,137" ca. 5px nach oben versetzt (4px + weitere 1px), statt wie Zahl/
        // Prozent an der gemeinsamen Grundlinie (rowHeight) auszurichten.
        using (CodexTheme.FontStatsSuffix.Push())
            drawList.AddText(new Vector2(rowOrigin.X + numberWidth + 6f * scale, rowOrigin.Y + rowHeight - suffixHeight - 5f * scale), ImGui.GetColorU32(CodexTheme.TextMuted), suffixText);
        using (CodexTheme.FontStatsPercentBig.Push())
            drawList.AddText(new Vector2(rowOrigin.X + avail - percentWidth, rowOrigin.Y + rowHeight - percentHeight), ImGui.GetColorU32(CodexTheme.Accent), percentText);

        ImGui.Dummy(new Vector2(avail, rowHeight));
        // Nutzervorgabe: Balken 5px nach oben (ursprünglich 12px Abstand).
        ImGui.Dummy(new Vector2(0f, 7f * scale));
        CodexWidgets.ProgressBar(fraction, 10f, width: avail);

        var contentHeight = ImGui.GetCursorScreenPos().Y - origin.Y;
        CodexTheme.EndCard();
        return contentHeight;
    }

    private static void DrawStatisticsFurthestTile(float scale, (CollectibleType Type, int Owned, int Total) furthest, CultureInfo culture, float minContentHeight)
    {
        var origin = ImGui.GetCursorScreenPos();
        CodexTheme.BeginCard(scale);
        using (CodexTheme.FontStatsLabel.Push())
            CodexTheme.DrawSpacedText(Loc.T("AM WEITESTEN", "FURTHEST ALONG"), CodexTheme.TextTertiary, 1f * scale);

        // Nutzervorgabe: insgesamt 13px näher ans Label (7px + weitere 6px, ursprünglich 8px
        // Abstand) - negativer Versatz statt Dummy, der verbleibende Abstand reicht nicht mehr aus,
        // um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5f * scale);
        using (CodexTheme.FontStatsMediumNumber.Push())
            ImGui.TextColored(CodexTheme.TextHeading, Loc.TypeName(furthest.Type));

        var fraction = furthest.Total == 0 ? 0f : furthest.Owned / (float)furthest.Total;
        var detail = string.Format(Loc.T("{0} von {1} · {2}", "{0} of {1} · {2}"),
            furthest.Owned.ToString("N0", culture), furthest.Total.ToString("N0", culture), (fraction * 100f).ToString("0.0", culture) + " %");
        using (CodexTheme.FontStatsTileDetail.Push())
            ImGui.TextColored(CodexTheme.TextMuted, detail);

        DrawStatisticsTileFiller(origin, minContentHeight);
        CodexTheme.EndCard();
    }

    private static void DrawStatisticsRemainingTile(float scale, int remaining, int categoryCount, CultureInfo culture, float minContentHeight)
    {
        var origin = ImGui.GetCursorScreenPos();
        CodexTheme.BeginCard(scale);
        using (CodexTheme.FontStatsLabel.Push())
            CodexTheme.DrawSpacedText(Loc.T("NOCH ZU FINDEN", "STILL TO FIND"), CodexTheme.TextTertiary, 1f * scale);

        // Nutzervorgabe: insgesamt 13px näher ans Label (7px + weitere 6px, ursprünglich 8px
        // Abstand) - negativer Versatz statt Dummy, der verbleibende Abstand reicht nicht mehr aus,
        // um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5f * scale);
        using (CodexTheme.FontStatsMediumNumber.Push())
            ImGui.TextColored(CodexTheme.TextHeading, remaining.ToString("N0", culture));

        var detail = string.Format(Loc.T("Einträge in {0} Kategorien", "entries across {0} categories"), categoryCount);
        using (CodexTheme.FontStatsTileDetail.Push())
            ImGui.TextColored(CodexTheme.TextMuted, detail);

        DrawStatisticsTileFiller(origin, minContentHeight);
        CodexTheme.EndCard();
    }

    /// <summary>Füllt bis zur Mindesthöhe (Nutzervorgabe: alle drei Kacheln gleich hoch) auf - VOR dem EndCard()-Aufruf des Aufrufers, damit die dort gezeichnete Kartenhöhe die Auffüllung mit einschließt.</summary>
    private static void DrawStatisticsTileFiller(Vector2 origin, float minContentHeight)
    {
        var contentHeight = ImGui.GetCursorScreenPos().Y - origin.Y;
        if (contentHeight < minContentHeight)
            ImGui.Dummy(new Vector2(0f, minContentHeight - contentHeight));
    }

    private void DrawStatisticsGroup(float scale, string tableId, string title, List<(CollectibleType Type, int Owned, int Total)> rows, CultureInfo culture)
    {
        // Nutzervorgabe: Überschrift + Badge 5px nach unten versetzt.
        ImGui.Dummy(new Vector2(0f, 5f * scale));
        DrawStatisticsGroupHeader(scale, title, rows.Sum(r => r.Owned), rows.Sum(r => r.Total), culture);
        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawStatisticsGroupCard(scale, tableId, rows, culture);
    }

    private static void DrawStatisticsGroupHeader(float scale, string title, int owned, int total, CultureInfo culture)
    {
        float titleHeight;
        using (CodexTheme.FontStatsGroupTitle.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(CodexTheme.TextCardTitle, title);
        }

        var badgeText = $"{owned.ToString("N0", culture)} / {total.ToString("N0", culture)}";
        var padding = new Vector2(7f * scale, 1f * scale);
        float badgeTextWidth, badgeTextHeight;
        using (CodexTheme.FontStatsBadge.Push())
        {
            var size = ImGui.CalcTextSize(badgeText);
            badgeTextWidth = size.X;
            badgeTextHeight = size.Y;
        }
        var badgeSize = new Vector2(badgeTextWidth + padding.X * 2f, badgeTextHeight + padding.Y * 2f);

        ImGui.SameLine(0f, 10f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (titleHeight - badgeSize.Y) / 2f);
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(CodexTheme.LineControl), 3f * scale);
        using (CodexTheme.FontStatsBadge.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(CodexTheme.TextMuted), badgeText);

        ImGui.Dummy(badgeSize);
    }

    /// <summary>
    /// Gruppen-Karte (DESIGN_SPEC: 6px oben/20px seitlich/8px unten Innenabstand, dadurch
    /// asymmetrisch statt über CodexTheme.BeginCard/EndCard, dessen Innenabstand einheitlich ist) -
    /// Hintergrund wie dort erst NACH dem Inhalt über ChannelsSplit/ChannelsMerge gezeichnet.
    /// </summary>
    private void DrawStatisticsGroupCard(float scale, string tableId, List<(CollectibleType Type, int Owned, int Total)> rows, CultureInfo culture)
    {
        var paddingTop = 6f * scale;
        var paddingSide = 20f * scale;
        var paddingBottom = 8f * scale;

        var cardOrigin = ImGui.GetCursorScreenPos();
        // Nutzervorgabe: derselbe rechte Randabstand wie bei den Karten auf der "Allgemein"-Seite
        // (siehe DrawGeneralPage), statt die volle verfügbare Breite auszunutzen.
        var cardWidth = ImGui.GetContentRegionAvail().X - 32f * scale;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.Indent(paddingSide);
        ImGui.Dummy(new Vector2(0f, paddingTop));

        // Nutzervorgabe: rechts denselben Abstand zur Karte wie links (paddingSide) - ohne explizite
        // Breite würde die Tabelle (per ImGui.GetContentRegionAvail() an der per Indent
        // verschobenen Cursor-Position) bis zum echten rechten Kartenrand reichen und dort über den
        // gezeichneten Kartenhintergrund hinausragen.
        var tableWidth = cardWidth - paddingSide * 2f;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(14f * scale, 0f));
        if (ImGui.BeginTable(tableId, 2, ImGuiTableFlags.None, new Vector2(tableWidth, 0f)))
        {
            ImGui.TableSetupColumn("##CodexStatsGroupCol0", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##CodexStatsGroupCol1", ImGuiTableColumnFlags.WidthStretch, 1f);

            for (var i = 0; i < rows.Count; i += 2)
            {
                ImGui.TableNextRow();
                var isFirstRow = i == 0;

                ImGui.TableSetColumnIndex(0);
                DrawStatisticsCategoryRow(scale, rows[i], isFirstRow, culture);

                if (i + 1 < rows.Count)
                {
                    ImGui.TableSetColumnIndex(1);
                    DrawStatisticsCategoryRow(scale, rows[i + 1], isFirstRow, culture);
                }
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Dummy(new Vector2(0f, paddingBottom));
        ImGui.Unindent(paddingSide);

        var cardMax = new Vector2(cardOrigin.X + cardWidth, ImGui.GetCursorScreenPos().Y);
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(cardOrigin, cardMax, ImGui.GetColorU32(CodexTheme.BgCard), CodexTheme.RoundingCard);
        drawList.AddRect(cardOrigin, cardMax, ImGui.GetColorU32(CodexTheme.LineCard), CodexTheme.RoundingCard);
        drawList.ChannelsMerge();
    }

    /// <summary>
    /// Eine Kategorie-Zeile (DESIGN_SPEC) - Klick auf die Namenszeile wechselt zur Datenbank-Seite mit
    /// gesetztem Typ-Filter und eingeschaltetem "Erhaltene ausblenden" (Nutzeranforderung). Hover-
    /// Trefferfläche bewusst nur auf die Namenszeile beschränkt, nicht auch auf den Balken darunter
    /// (der behält seinen eigenen, unabhängigen Tooltip über CodexWidgets.ProgressBar) - vereinfacht
    /// die Trefferflächen-Berechnung, DESIGN_SPEC erlaubt explizit, die Klick-Funktion bei Bedarf zu
    /// vereinfachen.
    /// </summary>
    private void DrawStatisticsCategoryRow(float scale, (CollectibleType Type, int Owned, int Total) row, bool isFirstRow, CultureInfo culture)
    {
        const float percentColumnWidth = 52f;
        var avail = ImGui.GetContentRegionAvail().X;

        if (!isFirstRow)
        {
            var lineCursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(avail, 0f), ImGui.GetColorU32(CodexTheme.LineRow));
        }
        // Nutzervorgabe: Zeilenabstände weiter verkleinert, damit die ganze Box niedriger wird
        // (ursprünglich 9px oben).
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        var (type, owned, total) = row;
        var fraction = total == 0 ? 0f : owned / (float)total;
        var percentText = (fraction * 100f).ToString("0.0", culture) + " %";
        var nameText = Loc.TypeName(type);
        var countValueText = owned.ToString("N0", culture);
        var countSuffixText = $" / {total.ToString("N0", culture)}";

        float nameHeight, countHeight, percentHeight, countValueWidth, countSuffixWidth, percentWidth;
        using (CodexTheme.FontStatsCategoryName.Push())
            nameHeight = ImGui.CalcTextSize(nameText).Y;
        using (CodexTheme.FontStatsCategoryCount.Push())
        {
            countValueWidth = ImGui.CalcTextSize(countValueText).X;
            countSuffixWidth = ImGui.CalcTextSize(countSuffixText).X;
            countHeight = ImGui.CalcTextSize(countValueText).Y;
        }
        using (CodexTheme.FontStatsDetail.Push())
        {
            percentWidth = ImGui.CalcTextSize(percentText).X;
            percentHeight = ImGui.CalcTextSize(percentText).Y;
        }

        var rowHeight = MathF.Max(nameHeight, MathF.Max(countHeight, percentHeight));
        var rowOrigin = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##CodexStatsRow{type}", new Vector2(avail, rowHeight));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var nameColor = owned == 0 ? CodexTheme.TextSecondary : hovered ? CodexTheme.TextHeading : CodexTheme.TextPrimary;
        using (CodexTheme.FontStatsCategoryName.Push())
            drawList.AddText(new Vector2(rowOrigin.X, rowOrigin.Y + (rowHeight - nameHeight) / 2f), ImGui.GetColorU32(nameColor), nameText);

        using (CodexTheme.FontStatsDetail.Push())
            drawList.AddText(new Vector2(rowOrigin.X + avail - percentWidth, rowOrigin.Y + (rowHeight - percentHeight) / 2f), ImGui.GetColorU32(CodexTheme.TextMuted), percentText);

        var percentColumnX = rowOrigin.X + avail - percentColumnWidth * scale;
        var countGap = 10f * scale;
        var countValueColor = owned == 0 ? CodexTheme.TextDim : CodexTheme.TextValue;
        using (CodexTheme.FontStatsCategoryCount.Push())
        {
            var countY = rowOrigin.Y + (rowHeight - countHeight) / 2f;
            drawList.AddText(new Vector2(percentColumnX - countGap - countSuffixWidth, countY), ImGui.GetColorU32(CodexTheme.TextDim), countSuffixText);
            drawList.AddText(new Vector2(percentColumnX - countGap - countSuffixWidth - countValueWidth, countY), ImGui.GetColorU32(countValueColor), countValueText);
        }

        // Nutzervorgabe: Balken insgesamt 10px nach oben (5px + weitere 5px, ursprünglich 8px
        // Abstand) - negativer Versatz statt Dummy, der verbleibende Abstand reicht nicht mehr aus,
        // um ihn per Dummy-Höhe weiter zu verkleinern.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
        var remaining = total - owned;
        var tooltip = string.Format(Loc.T("noch {0} offen", "{0} still to find"), remaining.ToString("N0", culture));
        CodexWidgets.ProgressBar(fraction, 6f, tooltip);

        // Nutzervorgabe: Zeilenabstände weiter verkleinert, damit die ganze Box niedriger wird
        // (ursprünglich 10px unten).
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        if (clicked)
        {
            activePage = MenuPage.Database;
            databaseSelectedType = type;
            plugin.Configuration.DatabaseHideOwned = true;
            plugin.Configuration.Save();
        }
    }

    // ---- Seite "Debug" (Nutzeranforderung, Vorbild referenz/codex-debug.png) - komplette
    // Neugestaltung der bestehenden MainWindow.DrawDebugTab im Codex-Theme. Logik bleibt identisch
    // (Configuration.ShowDebugInfo/Simulate*Automation, Zonen-/Positionsabfrage, alle Dump-Methoden in
    // Plugin.cs), nur Aufbau und Darstellung sind neu. Nutzeranforderung: die ganze Seite (schon der
    // Menüpunkt selbst, siehe DrawSidebar) nur in der Dev-Version sichtbar, nicht nur Simulation/Dumps
    // wie im alten Menü.

    private long debugCopyConfirmedUntil;
    private readonly Dictionary<string, long> debugDumpConfirmedUntil = new();

    private void DrawDebugPage(float scale)
    {
        var config = plugin.Configuration;
        var rightMargin = 32f * scale;
        var gap = 16f * scale;

        // Nutzer-Report "General/Current Status-Boxen leer, Simulation zeichnet außerhalb des
        // Fensters": BEIDE ImGui.BeginTable-Aufrufe auf dieser Seite (Kopfzeile General|Status UND
        // die vier Simulations-Schalter) wurden komplett entfernt. ImGui-Tabellen teilen die Draw-List
        // für korrektes Spalten-Clipping intern selbst per ChannelsSplit/ChannelsMerge auf - das
        // verschachtelt sich mit CodexTheme.BeginCard/EndCard (das für die automatische Kartenhöhe
        // ebenfalls ChannelsSplit nutzt) und reißt dessen Hintergrund-/Reihenfolge-Tracking durcheinander,
        // sobald eine Karte eine Tabelle enthält ODER selbst in einer Tabellenspalte sitzt. Beide
        // Zwei-Spalten-Layouts unten sind daher manuell (SetCursorPos) mit explizit durchgereichter
        // Breite umgesetzt, statt sich auf ImGui.GetContentRegionAvail()/GetWindowContentRegionMax()
        // zu verlassen - beide kennen eine so manuell aufgeteilte Spalte nicht von sich aus.
        var rowWidth = ImGui.GetContentRegionAvail().X - rightMargin;
        var colWidth = (rowWidth - gap) / 2f;
        var rowStartX = ImGui.GetCursorPosX();
        var rowStartY = ImGui.GetCursorPosY();

        // Nutzervorgabe: General und Current Status sollen identisch hoch sein. Current Status hat
        // deutlich mehr Inhalt (Titel+Trennlinie+Zone+Weltposition vs. nur ein Schalter in General),
        // ist also so gut wie immer die höhere Karte - deshalb zuerst Status zeichnen, ihre Inhaltshöhe
        // messen, und General danach mit genau dieser Höhe als Untergrenze stretchen.
        // Bug (Nutzer-Report "Status-Inhalt landet weiterhin in der linken Karte"): ImGui.SetCursorPos
        // positioniert nur das EINE nächste Widget - sobald irgendein echtes ImGui-Element gezeichnet
        // wird (z.B. BeginCard's eigenes Dummy), springt die X-Position für die nächste Zeile
        // automatisch auf den aktuellen Einzug (ImGui.Indent) zurück, nicht auf unsere manuell
        // gesetzte Spalten-X. Bei einer Karte mit mehreren Zeilen (Titel, Trennlinie, Zone, Position)
        // betrifft das jede Zeile außer der allerersten. Fix: die rechte Spalte über einen echten,
        // dauerhaften ImGui.Indent-Versatz erzeugen statt per SetCursorPos - der bleibt über mehrere
        // Zeilen hinweg bestehen, bis er wieder per Unindent zurückgenommen wird.
        ImGui.SetCursorPos(new Vector2(rowStartX, rowStartY));
        var colIndent = colWidth + gap;
        ImGui.Indent(colIndent);
        var statusContentHeight = DrawDebugStatusCard(scale, colWidth);
        ImGui.Unindent(colIndent);
        var statusBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(rowStartX, rowStartY));
        // Nutzervorgabe: General insgesamt 6px niedriger als Current Status (2px + 2px + weitere 2px).
        DrawDebugGeneralCard(scale, config, colWidth, minContentHeight: statusContentHeight - 6f * scale);
        var generalBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(rowStartX, MathF.Max(generalBottom, statusBottom)));

        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawDebugSimulationCard(scale, config, rowWidth);

        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawDebugDumpsCard(scale, rowWidth);
    }

    /// <summary>Zeile mit Schalter (DESIGN_SPEC: Mindesthöhe 44px, Trennlinie oben außer in der ersten Zeile einer Karte, Text 15px Medium, optionale 12px-Beschreibung darunter). "width" ist die EXPLIZITE Innenbreite der umgebenden Karte (bzw. halbe Kartenbreite bei den Simulations-Schaltern) - bewusst NICHT über ImGui.GetContentRegionAvail()/GetWindowContentRegionMax() ermittelt, siehe DrawDebugPage-Kommentar.</summary>
    private static bool DrawDebugToggleRow(string id, string label, ref bool value, float scale, float width, bool isFirstRow, string? description = null)
    {
        var rowLeftX = ImGui.GetCursorPosX();

        if (!isFirstRow)
        {
            var lineCursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(width, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        }

        var rowTop = ImGui.GetCursorPosY();
        var toggleSize = new Vector2(42f * scale, 22f * scale);

        float labelHeight;
        using (CodexTheme.FontDebugRowLabel.Push())
            labelHeight = ImGui.GetTextLineHeight();
        var descGap = 2f * scale;
        float descHeight = 0f;
        if (description != null)
            using (CodexTheme.FontDebugDescription.Push())
                descHeight = ImGui.GetTextLineHeight();

        var textBlockHeight = labelHeight + (description != null ? descGap + descHeight : 0f);
        var rowHeight = MathF.Max(44f * scale, textBlockHeight);

        ImGui.SetCursorPosY(rowTop + (rowHeight - textBlockHeight) / 2f);
        using (CodexTheme.FontDebugRowLabel.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, label);
        if (description != null)
        {
            // Nutzervorgabe: Beschreibungstext insgesamt 8px nach oben verschieben (5px + weitere 3px).
            ImGui.Dummy(new Vector2(0f, MathF.Max(0f, descGap - 8f * scale)));
            using (CodexTheme.FontDebugDescription.Push())
                ImGui.TextColored(CodexTheme.TextMuted, description);
        }

        var afterTextY = ImGui.GetCursorPosY();
        var toggleY = rowTop + (rowHeight - toggleSize.Y) / 2f;
        var toggleX = rowLeftX + width - toggleSize.X;
        ImGui.SetCursorPos(new Vector2(toggleX, toggleY));
        var changed = CodexTheme.Toggle(id, ref value, scale);

        ImGui.SetCursorPos(new Vector2(rowLeftX, MathF.Max(afterTextY, rowTop + rowHeight)));
        return changed;
    }

    private static void DrawDebugGeneralCard(float scale, Configuration config, float width, float minContentHeight)
    {
        CodexTheme.BeginCard(scale, width: width);
        var contentStartY = ImGui.GetCursorPosY();
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;
        using (CodexTheme.FontDebugCardTitle.Push())
            ImGui.TextColored(CodexTheme.TextCardTitle, Loc.T("Allgemein", "General"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var showDebug = config.ShowDebugInfo;
        if (DrawDebugToggleRow("##CodexDebugShowInfo", Loc.T("Debug-Infos im Overlay anzeigen", "Show debug info in overlay"), ref showDebug, scale, contentWidth, isFirstRow: true,
                Loc.T("Weltposition im Overlay anzeigen.", "Show world position in overlay.")))
        {
            config.ShowDebugInfo = showDebug;
            config.Save();
        }

        // Nutzervorgabe: General und Current Status identisch hoch - fehlende Höhe wird hier als
        // unsichtbarer Füller ergänzt, bevor EndCard() die tatsächliche (jetzt angeglichene) Höhe
        // für den Kartenhintergrund verwendet.
        var contentHeight = ImGui.GetCursorPosY() - contentStartY;
        if (contentHeight < minContentHeight)
            ImGui.Dummy(new Vector2(0f, minContentHeight - contentHeight));

        CodexTheme.EndCard();
    }

    private float DrawDebugStatusCard(float scale, float width)
    {
        CodexTheme.BeginCard(scale, width: width);
        var contentStartY = ImGui.GetCursorPosY();
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;

        var titleRowY = ImGui.GetCursorPosY();
        var title = Loc.T("Aktueller Status", "Current Status");
        float titleHeight;
        using (CodexTheme.FontDebugCardTitle.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(CodexTheme.TextCardTitle, title);
        }
        DrawDebugCopyButton(scale, titleRowY, titleHeight, contentWidth);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        // Nutzervorgabe: Zonen-Name-/Weltposition-Werte um 6px nach rechts (war 96px) - beide Zeilen
        // teilen sich dieselbe Spaltenbreite, die Verschiebung gilt also automatisch für beide.
        const float labelColumnWidth = 102f;

        var territoryId = Plugin.ClientState.TerritoryType;
        var zoneName = Plugin.GetZoneName(territoryId);
        DrawDebugStatusLabel(scale, labelColumnWidth, Loc.T("Zone", "Zone"));
        using (CodexTheme.FontDebugStatusText.Push())
            ImGui.TextColored(CodexTheme.TextPrimary, zoneName);
        ImGui.SameLine(0f, 4f * scale);
        // Nutzervorgabe: Zonen-ID-Etikett ("#397") um 2px nach unten.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * scale);
        using (CodexTheme.FontMono12.Push())
        {
            ImGui.TextColored(CodexTheme.TextDim, "#");
            ImGui.SameLine(0f, 0f);
            ImGui.TextColored(CodexTheme.TextValue, territoryId.ToString());
        }

        // Nutzervorgabe: "World position"-Zeile insgesamt 8px nach oben verschieben (4px + weitere 4px) -
        // der ursprüngliche 8px-Abstand ist damit komplett aufgebraucht.
        ImGui.Dummy(Vector2.Zero);

        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        DrawDebugStatusLabel(scale, labelColumnWidth, Loc.T("Weltposition", "World position"));
        // Nutzervorgabe: X/Y/Z-Werte um 2px nach unten.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * scale);
        if (playerPos.HasValue)
        {
            using (CodexTheme.FontMono12.Push())
            {
                DrawDebugCoordinate("X", playerPos.Value.X, scale);
                ImGui.SameLine(0f, 8f * scale);
                DrawDebugCoordinate("Y", playerPos.Value.Y, scale);
                ImGui.SameLine(0f, 8f * scale);
                DrawDebugCoordinate("Z", playerPos.Value.Z, scale);
            }
        }
        else
        {
            using (CodexTheme.FontDebugStatusText.Push())
                ImGui.TextColored(CodexTheme.TextDim, Loc.T("nicht verfügbar", "not available"));
        }

        // Nutzervorgabe: Current Status insgesamt 28px höher (10px + 3px + 5px + weitere 10px) - fließt
        // über minContentHeight auch in General ein, damit beide Karten weiterhin identisch hoch bleiben.
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        var contentHeight = ImGui.GetCursorPosY() - contentStartY;
        CodexTheme.EndCard();
        return contentHeight;
    }

    private static void DrawDebugStatusLabel(float scale, float columnWidth, string label)
    {
        // Bug (Nutzer-Report "General/Status-Inhalt landet in der linken Karte"): ImGui.SameLine(x)
        // interpretiert x als Versatz ab dem ABSOLUTEN linken Fensterrand, nicht relativ zum aktuellen
        // Einzug/zur manuell positionierten Spalte - bei einer rechten Spalte landet der Folgeinhalt
        // dadurch in der linken Spalte. Fix: Zeilenanfang vorher merken und per SetCursorPos absolut
        // weiterpositionieren statt SameLine(x) mit x != 0 zu verwenden.
        var rowX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();
        using (CodexTheme.FontDebugStatusText.Push())
            ImGui.TextColored(CodexTheme.TextMuted, label);
        ImGui.SetCursorPos(new Vector2(rowX + columnWidth * scale, rowY));
    }

    private static void DrawDebugCoordinate(string axis, float value, float scale)
    {
        ImGui.TextColored(CodexTheme.TextDim, axis);
        ImGui.SameLine(0f, 4f * scale);
        ImGui.TextColored(CodexTheme.TextValue, value.ToString("F3", CultureInfo.InvariantCulture));
    }

    /// <summary>"Copy to clipboard"-Knopf rechts neben dem Kartentitel - kopiert dieselbe "x f, y f, z f"-Zwischenablageformatierung wie bisher (DESIGN_SPEC: Logik bleibt unverändert), zeigt danach 1,5 Sekunden ein Häkchen statt des Kopiersymbols.</summary>
    private void DrawDebugCopyButton(float scale, float titleRowY, float titleHeight, float contentWidth)
    {
        var confirmed = Environment.TickCount64 < debugCopyConfirmedUntil;
        var label = confirmed ? Loc.T("Kopiert", "Copied") : Loc.T("In Zwischenablage kopieren", "Copy to clipboard");
        var icon = confirmed ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy;
        var fg = confirmed ? CodexTheme.OkFg : CodexTheme.TextHeading;
        var border = confirmed ? CodexTheme.OkFg : CodexTheme.LineControl;

        var padding = new Vector2(12f * scale, 5f * scale);
        var iconGap = 6f * scale;

        float iconWidth, iconHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var size = ImGui.CalcTextSize(icon.ToIconString());
            iconWidth = size.X;
            iconHeight = size.Y;
        }
        float textWidth, textHeight;
        using (CodexTheme.FontDebugButtonLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var buttonSize = new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var leftX = ImGui.GetCursorPosX();
        var rightX = leftX + contentWidth - buttonSize.X;
        var buttonY = titleRowY + (titleHeight - buttonSize.Y) / 2f;

        // Bug (Nutzer-Report "General/Status-Inhalt landet in der linken Karte"): ImGui.SameLine(x)
        // interpretiert x relativ zum absoluten Fensterrand statt zur aktuellen Spalte - deshalb hier
        // ein absolutes SetCursorPos statt SameLine(rightX).
        ImGui.SetCursorPos(new Vector2(rightX, buttonY));

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##CodexDebugCopyStatus", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(hovered ? CodexTheme.BgSelected : new Vector4(0f, 0f, 0f, 0f)), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(new Vector2(cursor.X + padding.X, cursor.Y + (buttonSize.Y - iconHeight) / 2f), ImGui.GetColorU32(fg), icon.ToIconString());
        using (CodexTheme.FontDebugButtonLabel.Push())
            drawList.AddText(new Vector2(cursor.X + padding.X + iconWidth + iconGap, cursor.Y + (buttonSize.Y - textHeight) / 2f), ImGui.GetColorU32(fg), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (clicked)
        {
            var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
            if (playerPos.HasValue)
            {
                ImGui.SetClipboardText($"{playerPos.Value.X.ToString(CultureInfo.InvariantCulture)}f, " +
                                        $"{playerPos.Value.Y.ToString(CultureInfo.InvariantCulture)}f, " +
                                        $"{playerPos.Value.Z.ToString(CultureInfo.InvariantCulture)}f");
                debugCopyConfirmedUntil = Environment.TickCount64 + 1500;
            }
        }

        ImGui.SetCursorPos(new Vector2(leftX, MathF.Max(titleRowY + titleHeight, buttonY + buttonSize.Y)));
    }

    private void DrawDebugSimulationCard(float scale, Configuration config, float width)
    {
        CodexTheme.BeginCard(scale, width: width);
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;

        var title = Loc.T("Simulation", "Simulation");
        float titleHeight;
        using (CodexTheme.FontDebugCardTitle.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(CodexTheme.TextCardTitle, title);
        }

        var anyActive = config.SimulateAetheryteAutomation || config.SimulateChocobokeepAutomation
                                                             || config.SimulateSightseeingAutomation || config.SimulateAetherCurrentAutomation;
        DrawDebugBadge(scale, titleHeight, anyActive);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawDebugWarningBox(scale, contentWidth);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        // Nutzer-Report "Simulation zeichnet außerhalb des Fensters": keine ImGui.BeginTable mehr
        // (siehe DrawDebugPage-Kommentar) - die vier Schalter werden manuell in zwei Spalten
        // positioniert, mit derselben expliziten Breite wie die übrigen Zeilen dieser Karte.
        var columnGap = 28f * scale;
        var colWidth = (contentWidth - columnGap) / 2f;
        var colLeftX = ImGui.GetCursorPosX();
        var colRightX = colLeftX + colWidth + columnGap;
        var rowStartY = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(colLeftX, rowStartY));
        var simAetheryte = config.SimulateAetheryteAutomation;
        if (DrawDebugToggleRow("##CodexSimAetheryte", Loc.T("Auto Aetheryte simulieren", "Simulate Auto Aetheryte"), ref simAetheryte, scale, colWidth, isFirstRow: true))
        {
            config.SimulateAetheryteAutomation = simAetheryte;
            config.Save();
        }
        var row0LeftBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(colRightX, rowStartY));
        var simChocobokeep = config.SimulateChocobokeepAutomation;
        if (DrawDebugToggleRow("##CodexSimChocobokeep", Loc.T("Auto Chocobokeep simulieren", "Simulate Auto Chocobokeep"), ref simChocobokeep, scale, colWidth, isFirstRow: true))
        {
            config.SimulateChocobokeepAutomation = simChocobokeep;
            config.Save();
        }
        var row0Bottom = MathF.Max(row0LeftBottom, ImGui.GetCursorPosY());

        ImGui.SetCursorPos(new Vector2(colLeftX, row0Bottom));
        var simSightseeing = config.SimulateSightseeingAutomation;
        if (DrawDebugToggleRow("##CodexSimSightseeing", Loc.T("Auto Sightseeing simulieren", "Simulate Auto Sightseeing"), ref simSightseeing, scale, colWidth, isFirstRow: false))
        {
            config.SimulateSightseeingAutomation = simSightseeing;
            config.Save();
        }
        var row1LeftBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(colRightX, row0Bottom));
        var simAetherCurrent = config.SimulateAetherCurrentAutomation;
        if (DrawDebugToggleRow("##CodexSimAetherCurrent", Loc.T("Auto Ätherströmung simulieren", "Simulate Auto Aether Current"), ref simAetherCurrent, scale, colWidth, isFirstRow: false))
        {
            config.SimulateAetherCurrentAutomation = simAetherCurrent;
            config.Save();
        }
        var row1Bottom = MathF.Max(row1LeftBottom, ImGui.GetCursorPosY());

        ImGui.SetCursorPos(new Vector2(colLeftX, row1Bottom));

        CodexTheme.EndCard();
    }

    private static void DrawDebugBadge(float scale, float titleHeight, bool active)
    {
        var text = active ? Loc.T("AKTIV", "ACTIVE") : Loc.T("NUR ZUM TESTEN", "TESTING ONLY");
        var bg = active ? CodexTheme.WarnFg : CodexTheme.WarnBg;
        var fg = active ? CodexTheme.TextOnAccent : CodexTheme.WarnFg;

        var padding = new Vector2(7f * scale, 1f * scale);
        float fontSize, baseWidth, textHeight;
        using (CodexTheme.FontDebugBadge.Push())
        {
            fontSize = ImGui.GetFontSize();
            var size = ImGui.CalcTextSize(text);
            baseWidth = size.X;
            textHeight = size.Y;
        }
        // 0.06em Zeichenabstand (DESIGN_SPEC) - dieselbe Breitenkorrektur wie CodexTheme.DrawSpacedText.
        var spacing = fontSize * 0.06f;
        var textWidth = baseWidth + spacing * MathF.Max(0f, text.Length - 1);
        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);

        ImGui.SameLine(0f, 10f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (titleHeight - badgeSize.Y) / 2f);

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + badgeSize, ImGui.GetColorU32(bg), 3f * scale);
        drawList.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(CodexTheme.WarnLine), 3f * scale);

        ImGui.SetCursorScreenPos(cursor + padding);
        using (CodexTheme.FontDebugBadge.Push())
            CodexTheme.DrawSpacedText(text, fg, spacing);

        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(badgeSize);
    }

    private static void DrawDebugWarningBox(float scale, float width)
    {
        var boldLead = Loc.T("Betrifft nur die Automatik.", "Affects automation only.");
        var rest = Loc.T(
            "Die Automatik steuert auch bereits freigeschaltete Ziele erneut an, um Wegfindung und Interaktion zu testen. Die Anzeige im Overlay bleibt unverändert.",
            "The automation revisits targets you've already unlocked, to test pathing and interaction. The overlay display stays unchanged.");

        var padding = new Vector2(12f * scale, 10f * scale);
        var iconGap = 10f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // Root cause of "General/Current Status/Simulation render blank": this box used to open its
        // own drawList.ChannelsSplit(2) for the auto-height background trick, same as CodexTheme.
        // BeginCard - but this box is drawn WHILE the Simulation card's own BeginCard split is still
        // open (channel 1 current). ImGui's ImDrawListSplitter explicitly does not support nested
        // splits ("IM_ASSERT(_Current == 0 ...)" in imgui's source) - in a Release build without
        // asserts this silently corrupts the splitter's channel bookkeeping for the rest of the frame,
        // which is why everything drawn before the (separately, cleanly split) Debug Dumps card came
        // out blank. Fix: measure the wrapped text height up front (ImGui.CalcTextSize supports a
        // wrap-width argument) so the background rect can be drawn BEFORE the text, in the card's
        // existing channel, with no nested split at all.
        float iconWidth, iconHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconSize = ImGui.CalcTextSize(FontAwesomeIcon.ExclamationTriangle.ToIconString());
            iconWidth = iconSize.X;
            iconHeight = iconSize.Y;
        }

        var textIndent = iconWidth + iconGap;
        var wrapWidth = width - padding.X * 2f - textIndent;

        float boldHeight;
        using (CodexTheme.FontDebugWarningBold.Push())
            boldHeight = ImGui.CalcTextSize(boldLead, false, wrapWidth).Y;
        float restHeight;
        using (CodexTheme.FontDebugWarningText.Push())
            restHeight = ImGui.CalcTextSize(rest, false, wrapWidth).Y;

        // Nutzer-Report "Text nicht vertikal zentriert": boldHeight+restHeight allein unterschätzte
        // die tatsächliche Blockhöhe, da die beiden TextColored-Aufrufe zwei getrennte ImGui-Items
        // sind und ImGui zwischen ihnen automatisch ImGui.GetStyle().ItemSpacing.Y einfügt - die Box
        // wurde dadurch etwas zu kurz berechnet (unten knapper Rand als oben statt symmetrisch).
        var textBlockSpacing = ImGui.GetStyle().ItemSpacing.Y;
        var contentHeight = MathF.Max(iconHeight, boldHeight + textBlockSpacing + restHeight);
        var boxMax = new Vector2(origin.X + width, origin.Y + padding.Y * 2f + contentHeight);
        drawList.AddRectFilled(origin, boxMax, ImGui.GetColorU32(CodexTheme.WarnBg), 4f * scale);
        drawList.AddRect(origin, boxMax, ImGui.GetColorU32(CodexTheme.WarnLine), 4f * scale);

        ImGui.Indent(padding.X);
        // Nutzervorgabe: Text insgesamt 6px nach oben verschieben (3px + weitere 3px) - Boxhöhe bleibt
        // unverändert, nur der obere Innenabstand wird kleiner, der untere bleibt bei padding.Y
        // (asymmetrisch mit Absicht).
        ImGui.Dummy(new Vector2(0f, MathF.Max(0f, padding.Y - 6f * scale)));

        // Dreieck am oberen Rand des (unten ggf. zweizeiligen) Textblocks ausgerichtet, statt zur
        // vollen Blockhöhe zentriert - deutlich einfacher, da die tatsächliche Texthöhe vom Umbruch
        // abhängt und erst nach dem Zeichnen bekannt wäre.
        var iconCursor = ImGui.GetCursorScreenPos();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(iconCursor, ImGui.GetColorU32(CodexTheme.WarnFg), FontAwesomeIcon.ExclamationTriangle.ToIconString());

        ImGui.Indent(textIndent);
        var wrapPos = ImGui.GetCursorPosX() + wrapWidth;

        using (CodexTheme.FontDebugWarningBold.Push())
        {
            ImGui.PushTextWrapPos(wrapPos);
            ImGui.TextColored(CodexTheme.WarnFg, boldLead);
            ImGui.PopTextWrapPos();
        }
        using (CodexTheme.FontDebugWarningText.Push())
        {
            ImGui.PushTextWrapPos(wrapPos);
            ImGui.TextColored(CodexTheme.TextValue, rest);
            ImGui.PopTextWrapPos();
        }
        ImGui.Unindent(textIndent);

        ImGui.Dummy(new Vector2(0f, padding.Y));
        ImGui.Unindent(padding.X);
    }

    private void DrawDebugDumpsCard(float scale, float width)
    {
        CodexTheme.BeginCard(scale, width: width);
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;

        var titleRowY = ImGui.GetCursorPosY();
        var rowLeftX = ImGui.GetCursorPosX();
        var title = Loc.T("Debug-Ausgaben", "Debug Dumps");
        float titleHeight;
        using (CodexTheme.FontDebugCardTitle.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(CodexTheme.TextCardTitle, title);
        }

        var hint = Loc.T("Jeder Knopf schreibt seine Daten ins Dalamud-Log (/xllog).", "Each button writes its data to the Dalamud log (/xllog).");
        float hintWidth, hintHeight;
        using (CodexTheme.FontDebugDescription.Push())
        {
            var size = ImGui.CalcTextSize(hint);
            hintWidth = size.X;
            hintHeight = size.Y;
        }
        // Bug (Nutzer-Report "Inhalt landet in der falschen Spalte"): ImGui.SameLine(x) interpretiert x
        // relativ zum absoluten Fensterrand, nicht zur aktuellen Karte - deshalb absolutes SetCursorPos.
        var hintX = rowLeftX + contentWidth - hintWidth;
        var hintY = titleRowY + (titleHeight - hintHeight) / 2f;
        ImGui.SetCursorPos(new Vector2(hintX, hintY));
        using (CodexTheme.FontDebugDescription.Push())
            ImGui.TextColored(CodexTheme.TextMuted, hint);

        ImGui.SetCursorPos(new Vector2(rowLeftX, MathF.Max(titleRowY + titleHeight, hintY + hintHeight)));
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 12f * scale));

        var rawItems = new (string Label, System.Action OnClick)[]
        {
            (Loc.T("Aetheryten", "Aetherytes"), () => plugin.DumpAetheryteDebugInfo()),
            (Loc.T("Hunting Log", "Hunting log"), Plugin.DumpHuntingLogDebugInfo),
            (Loc.T("Sightseeing", "Sightseeing"), () => plugin.DumpSightseeingDebugInfo()),
            (Loc.T("Eigene Position", "My position"), () => plugin.DumpPlayerPositionDebugInfo()),
            (Loc.T("Framer's Kit", "Framer's kit"), Plugin.DumpFrameKitDebugInfo),
            (Loc.T("Moderne Ästhetik", "Modern Aesthetics"), Plugin.DumpHairstyleDebugInfo),
            (Loc.T("Chocobokeep", "Chocobokeep"), Plugin.DumpChocobokeepDebugInfo),
            (Loc.T("Händler-Positionen", "Vendor positions"), Plugin.DumpVendorPositionEnrichmentDebugInfo),
            (Loc.T("GK-Bardinghändler", "GC barding vendors"), Plugin.DumpGrandCompanyBardingVendorDebugInfo),
            (Loc.T("Dungeon-Zonen", "Dungeon zones"), Plugin.DumpZoneEnrichmentDebugInfo),
            (Loc.T("Saisonevent", "Seasonal event"), Plugin.DumpSeasonalEventDebugInfo),
            (Loc.T("Ätherströmungen (aktuelle Zone)", "Aether currents (current zone)"), Plugin.DumpAetherCurrentDebugInfo),
            (Loc.T("Ätherströmungen (alle Zonen)", "Aether currents (all zones)"), Plugin.DumpAetherCurrentDebugInfoAllZones),
            ("\"Protecting What's Important\"", () => plugin.DumpQuestAcceptabilityDebugInfo("Protecting What's Important")),
            ("\"Open and Inviting\"", () => plugin.DumpQuestAcceptabilityDebugInfo("Open and Inviting")),
            ("\"Cat on a Cold Stone Roof\"", () => plugin.DumpQuestAcceptabilityDebugInfo("Cat on a Cold Stone Roof")),
            ("\"Grandfather's Belongings\"", () => plugin.DumpQuestAcceptabilityDebugInfo("Grandfather's Belongings")),
            (Loc.T("Kugane Tower Jump testen", "Test Kugane Tower jump"), () => plugin.SightseeingAutomation.StartKuganeTowerJumpTest()),
            (Loc.T("ToDo-Liste aufräumen", "Clean up ToDo list"), plugin.CleanUpToDoList),
        };

        // Jeder Knopf bekommt 1,5 Sekunden lang ein Häkchen statt des Terminal-Symbols, sobald er
        // geklickt wurde (siehe CodexWidgets.FlowButtons) - die eigentliche Dump-Methode wird dabei
        // unverändert mit aufgerufen (DESIGN_SPEC: Logik bleibt unverändert).
        var items = rawItems
            .Select(item => (item.Label, OnClick: (System.Action)(() =>
            {
                debugDumpConfirmedUntil[item.Label] = Environment.TickCount64 + 1500;
                item.OnClick();
            })))
            .ToArray();

        var now = Environment.TickCount64;
        CodexWidgets.FlowButtons(items, 8f, label =>
            debugDumpConfirmedUntil.TryGetValue(label, out var until) && now < until);

        CodexTheme.EndCard();
    }

    // ---- Seite "Log" (Nutzeranforderung, Vorbild Anhang-Screenshot) - Neugestaltung der alten
    // MainWindow.DrawLogPage im Codex-Theme. Logik (Puffer PluginLogStore, Quelle via SplitLogSource,
    // Level, Clear) bleibt erhalten, nur Darstellung/Bedienung sind neu. Durchgehend Bildschirm-
    // Koordinaten und kein ChannelsSplit (siehe DrawDebugPage-Kommentar weiter oben) - diese Seite
    // sitzt nicht in einer BeginCard, braucht also ohnehin keinen Kartentrick.

    private string logSearch = string.Empty;

    private readonly HashSet<LogEventLevel> logLevelFilter = new()
    {
        LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information,
        LogEventLevel.Warning, LogEventLevel.Error, LogEventLevel.Fatal,
    };

    private string? logSelectedSource;
    private bool logSelectMode;
    private readonly HashSet<long> logSelectedIds = new();
    private int? logLastClickedRowIndex;

    // Bei neuen Zeilen automatisch ans Ende scrollen, SOLANGE der Nutzer nicht von Hand hochgescrollt
    // hat (wie bei tail -f) - true, sobald die Scroll-Position beim letzten Frame nah am Ende war.
    private bool logFollowNewLines = true;

    // Dirty-Flag-Cache (Performance-Vorgabe): Quellenliste/Level-Zähler/gefilterte Liste werden nur
    // neu gebaut, wenn sich Suche, Quelle, Level-Auswahl oder der Puffer selbst (Größe oder neueste
    // Id) seit dem letzten Frame geändert haben.
    private List<LogEntry>? logFilteredCache;
    private List<string>? logSourcesCache;
    private Dictionary<LogEventLevel, int>? logLevelCountsCache;
    private int logTotalCountCache;
    private (string Search, string? Source, int LevelsMask, int BufferCount, long LastId) logCacheKey;

    private static readonly Regex LogTokenRegex = new(@"#\d+|\b\d+/\d+\b|\b[A-Za-z_][A-Za-z0-9_]*=[^\s,;]+", RegexOptions.Compiled);

    private void DrawLogPage(float scale)
    {
        var rightMargin = 32f * scale;

        var entries = PluginLogStore.Snapshot();
        EnsureLogFilterCache(entries);

        DrawLogToolbarRow(scale, rightMargin);
        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawLogLevelChipsRow(scale);
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawLogMetaRow(scale, rightMargin, logFilteredCache!.Count, logTotalCountCache);
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        DrawLogConsole(scale, rightMargin, logFilteredCache!);
    }

    private void EnsureLogFilterCache(List<LogEntry> entries)
    {
        var levelsMask = 0;
        foreach (var level in logLevelFilter)
            levelsMask |= 1 << (int)level;

        var lastId = entries.Count > 0 ? entries[^1].Id : 0L;
        var key = (logSearch, logSelectedSource, levelsMask, entries.Count, lastId);
        if (logFilteredCache != null && key == logCacheKey)
            return;

        logCacheKey = key;
        logTotalCountCache = entries.Count;

        // Quellen (Abschnitt "Source: All") unabhängig von Level/Suche/aktueller Auswahl - alle
        // jemals im aktuellen Puffer vorkommenden Quellen.
        logSourcesCache = entries
            .Select(e => SplitLogSource(e.Message).Source)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Zähler je Level (Abschnitt 4): respektieren Quelle+Suche, aber NICHT den Level-Filter selbst -
        // sonst würde ein deaktivierter Chip sofort auf 0 springen, sobald man ihn abwählt.
        IEnumerable<LogEntry> baseFiltered = entries;
        if (!string.IsNullOrEmpty(logSelectedSource))
            baseFiltered = baseFiltered.Where(e => SplitLogSource(e.Message).Source == logSelectedSource);
        if (!string.IsNullOrWhiteSpace(logSearch))
        {
            baseFiltered = baseFiltered.Where(e =>
                e.Message.Contains(logSearch, StringComparison.OrdinalIgnoreCase) ||
                e.Timestamp.ToString("HH:mm:ss").Contains(logSearch, StringComparison.OrdinalIgnoreCase));
        }
        var baseFilteredList = baseFiltered.ToList();

        logLevelCountsCache = baseFilteredList
            .GroupBy(e => e.Level)
            .ToDictionary(g => g.Key, g => g.Count());

        logFilteredCache = baseFilteredList.Where(e => logLevelFilter.Contains(e.Level)).ToList();
    }

    /// <summary>Werkzeugleiste Zeile 1: Suchfeld (flexible Breite) + "Source: ..."-Dropdown.</summary>
    private void DrawLogToolbarRow(float scale, float rightMargin)
    {
        // Nutzer-Report "Fragezeichen": das vorherige "▾"-Zeichen fehlte in der Alegreya-Schrift und
        // wurde als Tofu-Glyph ("?") dargestellt - daher entfernt, statt ein Fallback-Zeichen zu suchen.
        var sourceLabel = Loc.T($"Quelle: {logSelectedSource ?? Loc.T("Alle", "All")}", $"Source: {logSelectedSource ?? "All"}");
        float sourceTextWidth;
        using (CodexTheme.FontLogFilterLabel.Push())
            sourceTextWidth = ImGui.CalcTextSize(sourceLabel).X;

        var sourcePadding = new Vector2(12f * scale, 7f * scale);
        var gap = 10f * scale;
        var searchPadding = new Vector2(12f * scale, 9f * scale);
        var sourceMinWidth = sourceTextWidth + sourcePadding.X * 2f;

        var searchWidth = ImGui.GetContentRegionAvail().X - rightMargin - sourceMinWidth - gap;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CodexTheme.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineControl);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, searchPadding);
        ImGui.SetNextItemWidth(searchWidth);
        using (CodexTheme.FontLogSearchInput.Push())
            ImGui.InputTextWithHint("##CodexLogSearch", Loc.T("Log durchsuchen …", "Search log …"), ref logSearch, 200);
        var searchHeight = ImGui.GetItemRectSize().Y;
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        var sourceSize = new Vector2(sourceMinWidth, searchHeight);
        ImGui.SameLine(0f, gap);
        var sourceCursor = ImGui.GetCursorScreenPos();
        var sourceClicked = ImGui.InvisibleButton("##CodexLogSourceFilter", sourceSize);
        var sourceHovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var sourceActive = !string.IsNullOrEmpty(logSelectedSource);
        var sourceBg = sourceActive ? CodexTheme.BgSelectedStrong : sourceHovered ? CodexTheme.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var sourceBorder = sourceActive ? CodexTheme.Accent : CodexTheme.LineControl;
        var sourceFg = sourceActive ? CodexTheme.TextHeading : CodexTheme.TextSecondary;
        drawList.AddRectFilled(sourceCursor, sourceCursor + sourceSize, ImGui.GetColorU32(sourceBg), CodexTheme.RoundingControl);
        drawList.AddRect(sourceCursor, sourceCursor + sourceSize, ImGui.GetColorU32(sourceBorder), CodexTheme.RoundingControl);
        using (CodexTheme.FontLogFilterLabel.Push())
        {
            var textSize = ImGui.CalcTextSize(sourceLabel);
            drawList.AddText(sourceCursor + (sourceSize - textSize) / 2f, ImGui.GetColorU32(sourceFg), sourceLabel);
        }

        if (sourceClicked)
            ImGui.OpenPopup("##CodexLogSourcePopup");

        // Nutzervorgabe: 3px Abstand zu allen vier Seiten, wie bei den anderen Dropdown-Fenstern -
        // muss VOR BeginPopup gepusht werden, da es auf das dabei ggf. neu erzeugte Popup-Fenster wirkt.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3f * scale, 3f * scale));
        if (ImGui.BeginPopup("##CodexLogSourcePopup"))
        {
            if (ImGui.Selectable(Loc.T("Alle", "All") + "##CodexLogSourceAll", logSelectedSource == null))
                logSelectedSource = null;
            foreach (var source in logSourcesCache!)
            {
                if (ImGui.Selectable(source + "##CodexLogSourceEntry_" + source, logSelectedSource == source))
                    logSelectedSource = source;
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
    }

    /// <summary>Werkzeugleiste Zeile 2: Level-Chips (Toggle+Zähler) - die Select lines/Copy all/Clear-
    /// Knöpfe sitzen seit Nutzervorgabe stattdessen rechtsbündig in der Meta-Zeile, siehe DrawLogMetaRow.</summary>
    private void DrawLogLevelChipsRow(float scale)
    {
        var chipGap = 6f * scale;

        DrawLogLevelChip(scale, LogEventLevel.Verbose, Loc.T("Verbose", "Verbose"), CodexTheme.TextDisabled, isFirst: true);
        DrawLogLevelChip(scale, LogEventLevel.Debug, Loc.T("Debug", "Debug"), CodexTheme.TextMuted, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Information, Loc.T("Info", "Info"), PluginUiKit.UiTheme.Active.InfoFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Warning, Loc.T("Warnung", "Warning"), CodexTheme.WarnFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Error, Loc.T("Fehler", "Error"), CodexTheme.ErrFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Fatal, Loc.T("Kritisch", "Critical"), CodexTheme.LogCritFg, isFirst: false, gap: chipGap);
    }

    private void DrawLogLevelChip(float scale, LogEventLevel level, string label, Vector4 color, bool isFirst, float gap = 0f)
    {
        var active = logLevelFilter.Contains(level);
        logLevelCountsCache!.TryGetValue(level, out var count);
        var countText = count.ToString(CultureInfo.InvariantCulture);

        float labelWidth, countWidth, labelHeight;
        using (CodexTheme.FontLogFilterLabel.Push())
        {
            var size = ImGui.CalcTextSize("◆ " + label);
            labelWidth = size.X;
            labelHeight = size.Y;
        }
        using (CodexTheme.FontLogChipCount.Push())
            countWidth = ImGui.CalcTextSize(countText).X;

        // Nutzervorgabe: Chip 3px höher - zusätzlich zur ohnehin größeren Schrift (FontLogFilterLabel),
        // daher 1,5px mehr Innenabstand oben/unten statt der ursprünglichen 5px.
        var padding = new Vector2(10f * scale, 6.5f * scale);
        var innerGap = 6f * scale;
        var chipSize = new Vector2(labelWidth + innerGap + countWidth + padding.X * 2f, labelHeight + padding.Y * 2f);

        if (!isFirst)
            ImGui.SameLine(0f, gap);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##CodexLogChip_" + level, chipSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = active ? color with { W = 0.12f } : hovered ? CodexTheme.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var border = active ? color with { W = 0.45f } : CodexTheme.LineDisabled;
        var fg = active ? color : CodexTheme.TextDisabled;
        drawList.AddRectFilled(cursor, cursor + chipSize, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + chipSize, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        var textY = cursor.Y + padding.Y;
        using (CodexTheme.FontLogFilterLabel.Push())
            drawList.AddText(new Vector2(cursor.X + padding.X, textY), ImGui.GetColorU32(fg), "◆ " + label);
        using (CodexTheme.FontLogChipCount.Push())
        {
            var countY = cursor.Y + (chipSize.Y - ImGui.CalcTextSize(countText).Y) / 2f;
            drawList.AddText(new Vector2(cursor.X + padding.X + labelWidth + innerGap, countY), ImGui.GetColorU32(fg), countText);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked)
        {
            if (!logLevelFilter.Remove(level))
                logLevelFilter.Add(level);
        }
    }

    /// <summary>Transparenter Knopf mit Rahmen (Select lines/Copy all/Clear) - Hover BgSelected, aktiv (Select-Modus) Accent-Rahmen statt LineControl.</summary>
    private static bool DrawLogGhostButton(float scale, string id, string label, float width, bool active, Vector4? textColor = null)
    {
        // Nutzervorgabe: 5px höher als ursprünglich (26px).
        var size = new Vector2(width, 31f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = hovered ? CodexTheme.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        var border = active ? CodexTheme.Accent : CodexTheme.LineControl;
        var fg = textColor ?? (active ? CodexTheme.TextHeading : CodexTheme.TextSecondary);
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), CodexTheme.RoundingControl);
        using (CodexTheme.FontLogGhostButton.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(cursor + (size - textSize) / 2f, ImGui.GetColorU32(fg), label);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>"{n} of {m} lines" links, Select lines/Copy all/Clear rechtsbündig (Nutzervorgabe: an
    /// die Stelle des entfernten "Follow new lines"-Schalters gewandert - das automatische Mitscrollen
    /// selbst bleibt als reines Hintergrundverhalten erhalten, nur der sichtbare Schalter fällt weg).</summary>
    private void DrawLogMetaRow(float scale, float rightMargin, int filteredCount, int totalCount)
    {
        var rowY = ImGui.GetCursorPosY();
        var buttonHeight = 31f * scale;

        var lineCountText = Loc.T($"{filteredCount} von {totalCount} Zeilen", $"{filteredCount} of {totalCount} lines");
        float textHeight;
        using (CodexTheme.FontLogMeta.Push())
            textHeight = ImGui.CalcTextSize(lineCountText).Y;

        // Nutzervorgabe: Text an der UNTEREN Kante der drei Knöpfe ausgerichtet statt zentriert - daher
        // erst (ohne zu zeichnen) gemessen, dann an der passenden Y-Position gezeichnet, statt wie
        // zuvor sofort am Zeilenanfang.
        ImGui.SetCursorPosY(rowY + buttonHeight - textHeight);
        using (CodexTheme.FontLogMeta.Push())
            ImGui.TextColored(CodexTheme.TextDim, lineCountText);

        var selectLabel = Loc.T("Zeilen auswählen", "Select lines");
        var copyLabel = logSelectMode
            ? Loc.T($"Auswahl kopieren ({logSelectedIds.Count})", $"Copy selected ({logSelectedIds.Count})")
            : Loc.T("Alles kopieren", "Copy all");
        var clearLabel = Loc.T("Leeren", "Clear");

        float selectWidth, copyWidth, clearWidth;
        using (CodexTheme.FontLogGhostButton.Push())
        {
            var padX = 12f * scale * 2f;
            selectWidth = ImGui.CalcTextSize(selectLabel).X + padX;
            copyWidth = ImGui.CalcTextSize(copyLabel).X + padX;
            clearWidth = ImGui.CalcTextSize(clearLabel).X + padX;
        }
        var buttonGap = 8f * scale;
        var totalButtonsWidth = selectWidth + copyWidth + clearWidth + buttonGap * 2f;

        // Bug (derselbe wie beim Debug-Fix): ImGui.SameLine(x) erwartet x als FENSTER-relative
        // Position, nicht als reine "verbleibende Breite" - ohne das hier fehlende GetCursorPosX()
        // (das den aktuellen Einzug/die Zeilenbasis mit einrechnet) landeten Elemente immer um genau
        // diesen Einzug zu weit links, nie wirklich am rechten Rand.
        var rowStartX = ImGui.GetCursorPosX();
        // Oben ausgerichtet (nicht zentriert) - die Knöpfe sind die höheren Elemente dieser Zeile,
        // daher endet der Zeilentext "{n} of {m} lines" an genau dieser unteren Kante (siehe oben).
        var buttonY = rowY;

        ImGui.SameLine(MathF.Max(0f, rowStartX + ImGui.GetContentRegionAvail().X - rightMargin - totalButtonsWidth));
        // Y für jeden der drei Knöpfe explizit gesetzt statt sich auf ImGui.SameLine()'s implizite
        // "gleiche Zeile"-Y-Wiederherstellung zu verlassen (Nutzer-Report: "Select lines" nicht auf
        // gleicher Höhe wie "Copy all") - robuster gegen jede Abweichung zwischen den drei Aufrufen.
        ImGui.SetCursorPosY(buttonY);
        if (DrawLogGhostButton(scale, "##CodexLogSelectMode", selectLabel, selectWidth, active: logSelectMode))
        {
            logSelectMode = !logSelectMode;
            if (!logSelectMode)
            {
                logSelectedIds.Clear();
                logLastClickedRowIndex = null;
            }
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(buttonY);
        if (DrawLogGhostButton(scale, "##CodexLogCopy", copyLabel, copyWidth, active: false))
        {
            var toCopy = logSelectMode
                ? logFilteredCache!.Where(e => logSelectedIds.Contains(e.Id))
                : logFilteredCache!;
            CopyLogLines(toCopy);
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(buttonY);
        // Nutzervorgabe: kein Bestätigungs-Popup mehr - Clear löscht sofort.
        if (DrawLogGhostButton(scale, "##CodexLogClear", clearLabel, clearWidth, active: false))
        {
            PluginLogStore.Clear();
            logSelectedIds.Clear();
            logLastClickedRowIndex = null;
        }

        ImGui.SetCursorPosY(rowY + MathF.Max(textHeight, buttonHeight));
    }

    private static void SetupLogTableColumns(float scale)
    {
        ImGui.TableSetupColumn("##CodexLogTime", ImGuiTableColumnFlags.WidthFixed, 78f * scale);
        ImGui.TableSetupColumn("##CodexLogLevel", ImGuiTableColumnFlags.WidthFixed, 52f * scale);
        ImGui.TableSetupColumn("##CodexLogSource", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
        ImGui.TableSetupColumn("##CodexLogMessage", ImGuiTableColumnFlags.WidthStretch, 1f);
    }

    private void DrawLogConsole(float scale, float rightMargin, List<LogEntry> filtered)
    {
        var width = ImGui.GetContentRegionAvail().X - rightMargin;
        // Nutzervorgabe: insgesamt niedriger, damit die letzte Zeile nicht vom unteren Fensterrand
        // abgeschnitten wird (vorher 20px Abstand zum Fensterende, jetzt 60px).
        var height = MathF.Max(100f * scale,
            ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetCursorScreenPos().Y - 60f * scale);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, CodexTheme.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);
        ImGui.BeginChild("##CodexLogConsole", new Vector2(width, height), true);

        var innerWidth = ImGui.GetContentRegionAvail().X;
        var headerHeight = 32f * scale;

        // Nutzervorgabe: der Scrollbalken soll erst UNTER der Kopfzeile erscheinen, nicht über deren
        // volle Höhe mitlaufen - daher die Kopfzeile als eigene, nicht scrollende Mini-Tabelle, und
        // die eigentlichen Zeilen in einem separaten, darunterliegenden Scroll-Kindfenster (dessen
        // eigener Scrollbalken dadurch erst ab dessen eigener Oberkante gezeichnet wird).
        if (ImGui.BeginTable("##CodexLogHeaderTable", 4, ImGuiTableFlags.None, new Vector2(innerWidth, headerHeight)))
        {
            SetupLogTableColumns(scale);
            DrawLogTableHeader(scale, headerHeight);
            ImGui.EndTable();
        }

        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##CodexLogBody", new Vector2(innerWidth, bodyHeight), false);

        if (filtered.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, bodyHeight / 2f - 12f * scale));
            var emptyText = Loc.T("Keine Log-Zeilen passen zu den Filtern.", "No log lines match the current filters.");
            using (CodexTheme.FontLogMeta.Push())
                CodexWidgets.CenterNext(ImGui.CalcTextSize(emptyText).X, ImGui.GetContentRegionAvail().X);
            using (CodexTheme.FontLogMeta.Push())
                ImGui.TextColored(CodexTheme.TextDim, emptyText);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, CodexTheme.LineRow);
            if (ImGui.BeginTable("##CodexLogBodyTable", 4, ImGuiTableFlags.BordersInnerH))
            {
                SetupLogTableColumns(scale);

                var rowHeight = 24f * scale;
                var clipper = new ImGuiListClipper();
                clipper.Begin(filtered.Count, rowHeight);
                while (clipper.Step())
                {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        DrawLogRow(scale, filtered, i, rowHeight);
                }
                clipper.End();

                ImGui.EndTable();
            }
            ImGui.PopStyleColor();

            // Scrollt (im eigenen Body-Kindfenster statt über die Tabelle) ans Ende, solange der
            // Nutzer zuletzt schon (ungefähr) am Ende war.
            if (logFollowNewLines)
                ImGui.SetScrollY(ImGui.GetScrollMaxY());
            logFollowNewLines = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2f;
        }

        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawLogTableHeader(float scale, float headerHeight)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CodexTheme.BgCard));

        string[] labels = { Loc.T("ZEIT", "TIME"), Loc.T("LEVEL", "LEVEL"), Loc.T("QUELLE", "SOURCE"), Loc.T("NACHRICHT", "MESSAGE") };
        using (CodexTheme.FontLogTableHeader.Push())
        {
            var textHeight = ImGui.GetTextLineHeight();
            for (var c = 0; c < 4; c++)
            {
                ImGui.TableSetColumnIndex(c);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerHeight - textHeight) / 2f);
                CodexTheme.DrawSpacedText(labels[c], CodexTheme.TextTertiary, 1f * scale);
            }
        }
        // Nutzervorgabe: keine Trennlinie zwischen Kopfzeile und der obersten Log-Zeile.
    }

    private void DrawLogRow(float scale, List<LogEntry> filtered, int rowIndex, float rowHeight)
    {
        var entry = filtered[rowIndex];
        var (source, message) = SplitLogSource(entry.Message);
        var isSelected = logSelectMode && logSelectedIds.Contains(entry.Id);
        var levelColor = LogLevelColor(entry.Level);
        var isDimmed = entry.Level is LogEventLevel.Debug or LogEventLevel.Verbose;
        var isTinted = entry.Level is LogEventLevel.Warning or LogEventLevel.Error or LogEventLevel.Fatal;
        var messageColor = isDimmed ? CodexTheme.TextMuted : CodexTheme.TextPrimary;

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

        var rowBg = isSelected ? CodexTheme.BgSelectedStrong
            : isTinted ? levelColor with { W = 0.05f }
            : rowIndex % 2 == 1 ? CodexTheme.BgInput with { W = 1f } // Zebra wird unten separat aufgehellt
            : new Vector4(0f, 0f, 0f, 0f);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(rowBg));

        // Zeile per unsichtbarem Selectable über alle Spalten anklickbar machen (nur im Auswahlmodus
        // interaktiv) - liefert außerdem den Hover-Status für die ganze Zeile.
        ImGui.TableSetColumnIndex(0);
        var rowCursor = ImGui.GetCursorScreenPos();
        ImGui.Selectable($"##CodexLogRow_{entry.Id}", false, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight));
        var rowHovered = ImGui.IsItemHovered();
        // SpanAllColumns macht die Trefferfläche selbst schon zeilenbreit (nicht nur Spalte 0) - darum
        // hier ItemRectMin/Max statt GetContentRegionAvail() verwenden, das an dieser Stelle nur die
        // Breite von Spalte 0 (78px) liefern würde, nicht die ganze Zeile.
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        if (logSelectMode && rowHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            HandleLogRowClick(rowIndex, entry.Id, filtered);
        if (!isSelected && rowHovered)
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(CodexTheme.BgSelected with { W = 0.4f }));
        if (isSelected)
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, new Vector2(rowMin.X + 2f * scale, rowMax.Y), ImGui.GetColorU32(CodexTheme.Accent));

        ImGui.SetCursorScreenPos(rowCursor);
        using (CodexTheme.FontMono12.Push())
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
            ImGui.TextColored(CodexTheme.TextDim, entry.Timestamp.ToString("HH:mm:ss"));
        }

        ImGui.TableSetColumnIndex(1);
        var badgeText = LogLevelBadge(entry.Level);
        var badgeSize = new Vector2(36f * scale, 16f * scale);
        var badgeCursor = ImGui.GetCursorScreenPos() + new Vector2(0f, (rowHeight - badgeSize.Y) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(levelColor with { W = 0.14f }), 3f * scale);
        using (CodexTheme.FontLogBadge.Push())
        {
            var textSize = ImGui.CalcTextSize(badgeText);
            drawList.AddText(badgeCursor + (badgeSize - textSize) / 2f, ImGui.GetColorU32(levelColor), badgeText);
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
        using (CodexTheme.FontLogRow.Push())
        {
            // Avail ist hier bereits um die 8px von SetCursorPosX oben verkleinert - kein zweiter Abzug.
            var maxWidth = ImGui.GetContentRegionAvail().X;
            var truncatedSource = TruncateToWidth(source, maxWidth);
            ImGui.TextColored(CodexTheme.TextSecondary, truncatedSource);
        }

        ImGui.TableSetColumnIndex(3);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        var messageCellPos = ImGui.GetCursorScreenPos() + new Vector2(0f, (rowHeight - 13f * scale) / 2f);
        float maxMessageWidth;
        using (CodexTheme.FontLogRow.Push())
            maxMessageWidth = ImGui.GetContentRegionAvail().X;
        string truncatedMessage;
        using (CodexTheme.FontLogRow.Push())
            truncatedMessage = TruncateToWidth(message, maxMessageWidth);
        DrawLogHighlightedText(messageCellPos, truncatedMessage, messageColor);
        if (rowHovered && truncatedMessage != message)
        {
            ImGui.PushStyleColor(ImGuiCol.PopupBg, CodexTheme.BgPopup);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 40f);
            using (CodexTheme.FontLogRow.Push())
                ImGui.TextColored(CodexTheme.TextPrimary, message);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.PopStyleColor();
        }
    }

    private void HandleLogRowClick(int rowIndex, long entryId, List<LogEntry> filteredList)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift && logLastClickedRowIndex.HasValue)
        {
            var start = Math.Min(logLastClickedRowIndex.Value, rowIndex);
            var end = Math.Max(logLastClickedRowIndex.Value, rowIndex);
            for (var i = start; i <= end; i++)
                logSelectedIds.Add(filteredList[i].Id);
        }
        else if (io.KeyCtrl)
        {
            if (!logSelectedIds.Remove(entryId))
                logSelectedIds.Add(entryId);
        }
        else
        {
            logSelectedIds.Clear();
            logSelectedIds.Add(entryId);
        }

        logLastClickedRowIndex = rowIndex;
    }

    private static void DrawLogHighlightedText(Vector2 screenPos, string text, Vector4 baseColor)
    {
        var drawList = ImGui.GetWindowDrawList();
        var x = screenPos.X;
        var lastIndex = 0;

        void DrawPlain(string segment)
        {
            if (segment.Length == 0)
                return;
            using (CodexTheme.FontLogRow.Push())
            {
                drawList.AddText(new Vector2(x, screenPos.Y), ImGui.GetColorU32(baseColor), segment);
                x += ImGui.CalcTextSize(segment).X;
            }
        }

        foreach (Match m in LogTokenRegex.Matches(text))
        {
            DrawPlain(text[lastIndex..m.Index]);
            using (CodexTheme.FontMono12.Push())
            {
                drawList.AddText(new Vector2(x, screenPos.Y), ImGui.GetColorU32(CodexTheme.Accent), m.Value);
                x += ImGui.CalcTextSize(m.Value).X;
            }
            lastIndex = m.Index + m.Length;
        }
        DrawPlain(text[lastIndex..]);
    }

    /// <summary>Kürzt per CalcTextSize auf maxWidth und hängt "…" an, falls nötig - erwartet die passende Schrift bereits gepusht.</summary>
    private static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "…";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisWidth <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return text[..low] + ellipsis;
    }

    private void CopyLogLines(IEnumerable<LogEntry> lines)
    {
        var text = new StringBuilder();
        foreach (var entry in lines)
        {
            var (source, message) = SplitLogSource(entry.Message);
            var sourcePrefix = string.IsNullOrEmpty(source) ? string.Empty : $"{source}: ";
            text.Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append(" [").Append(LevelLabel(entry.Level)).Append("] ")
                .Append(sourcePrefix).Append(message).Append('\n');
        }
        ImGui.SetClipboardText(text.ToString());
    }

    private static Vector4 LogLevelColor(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => CodexTheme.TextDisabled,
        LogEventLevel.Debug => CodexTheme.TextMuted,
        LogEventLevel.Information => PluginUiKit.UiTheme.Active.InfoFg,
        LogEventLevel.Warning => CodexTheme.WarnFg,
        LogEventLevel.Error => CodexTheme.ErrFg,
        LogEventLevel.Fatal => CodexTheme.LogCritFg,
        _ => CodexTheme.TextMuted,
    };

    private static string LogLevelBadge(LogEventLevel level) => LevelLabel(level);

    private static string LevelLabel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "CRT",
        _ => "???",
    };

    /// <summary>
    /// Fast jede Log-Zeile in diesem Projekt beginnt mit einer eigenen Quellenangabe in eckigen
    /// Klammern (z.B. "[SightseeingAutomation] ..."), die bisher einfach Teil des Nachrichtentexts war -
    /// für die Log-Seite hier als eigener Wert herausgelöst, Rest bleibt die eigentliche Nachricht ohne
    /// das Tag.
    /// </summary>
    private static (string Source, string Message) SplitLogSource(string message)
    {
        if (message.Length > 0 && message[0] == '[')
        {
            var close = message.IndexOf(']');
            if (close > 1)
                return (message[1..close], message[(close + 1)..].TrimStart());
        }

        return (string.Empty, message);
    }

    // ---- Seite "Changelog" (Nutzeranforderung) - Zeitleiste mit auf-/zuklappbaren Versionskarten.
    // Auf-/Zu-Zustand nur zur Laufzeit gehalten (Dictionary), nicht gespeichert (DESIGN_SPEC). Rauten
    // und Chevron werden gezeichnet statt als Glyph (◆/› fehlen im Ingame-Font, erscheinen sonst als
    // "?") - Rauten über den bereits vorhandenen CodexWidgets.Diamond-Helfer, Chevron über die
    // FontAwesome-Icon-Schrift (die kennt ›-ähnliche Pfeile sehr wohl, nur die normale Textschrift
    // nicht - anders als ◆/▾, die auch der Icon-Schrift fehlen).

    private readonly Dictionary<string, bool> changelogExpanded = new();
    private Version? changelogUnseenThreshold;
    private bool changelogMarkedSeen;

    private void DrawChangelogPage(float scale)
    {
        var config = plugin.Configuration;
        var entries = ChangelogService.Entries;

        // Beim ersten Anzeigen dieser Seite in dieser Sitzung: merken, welche Version vorher als
        // gesehen galt (die NEW-Badges an den Versionskarten sollen laut Nutzervorgabe für den Rest
        // der Sitzung sichtbar bleiben, auch nachdem unten sofort als gesehen gespeichert wird), dann
        // genau einmal speichern (nicht jeden Frame).
        if (!changelogMarkedSeen)
        {
            changelogMarkedSeen = true;
            Version.TryParse(config.LastSeenChangelogVersion, out var previouslySeen);
            changelogUnseenThreshold = previouslySeen;
            if (ChangelogService.LatestVersion is { } latest && (previouslySeen == null || latest > previouslySeen))
            {
                config.LastSeenChangelogVersion = latest.ToString();
                config.Save();
            }
        }

        if (entries.Count == 0)
        {
            using (CodexTheme.FontChangelogMeta.Push())
                ImGui.TextColored(CodexTheme.TextDim, Loc.T("Noch keine Änderungen eingetragen.", "No changes recorded yet."));
            return;
        }

        // Nutzervorgabe: höchstens 3 Versionskarten anzeigen (statt aller bekannten Versionen).
        var displayedEntries = entries.Count > 3 ? entries.GetRange(0, 3) : entries;

        var rightMargin = 32f * scale;
        var timelineWidth = 34f * scale;
        var lineX = ImGui.GetCursorScreenPos().X + 9f * scale;
        var cardWidth = ImGui.GetContentRegionAvail().X - timelineWidth - rightMargin;
        var rowStartX = ImGui.GetCursorPosX();

        float? firstDiamondY = null;
        var lastDiamondY = 0f;

        for (var i = 0; i < displayedEntries.Count; i++)
        {
            var entry = displayedEntries[i];
            var isLatest = i == 0;
            var cardTopScreenY = ImGui.GetCursorScreenPos().Y;
            var cardTopPosY = ImGui.GetCursorPosY();

            var expanded = GetChangelogExpanded(entry, isLatest);
            float headerLineHeight;
            using ((expanded ? CodexTheme.FontChangelogVersionTitle : CodexTheme.FontChangelogCollapsedTitle).Push())
                headerLineHeight = ImGui.GetTextLineHeight();
            var headerPaddingY = expanded ? CodexTheme.CardPaddingY : 12f * scale;
            var diamondY = cardTopScreenY + headerPaddingY + headerLineHeight / 2f;

            firstDiamondY ??= diamondY;
            lastDiamondY = diamondY;

            var diamondCenter = new Vector2(lineX, diamondY);
            if (isLatest)
            {
                ImGui.GetWindowDrawList().AddCircleFilled(diamondCenter, 10f * scale, ImGui.GetColorU32(CodexTheme.Accent with { W = 0.15f }));
                CodexWidgets.Diamond(diamondCenter, 8.5f * scale, ImGui.GetColorU32(CodexTheme.Accent), ImGui.GetColorU32(CodexTheme.Accent));
            }
            else
            {
                CodexWidgets.Diamond(diamondCenter, 8.5f * scale, ImGui.GetColorU32(CodexTheme.BgWindow), ImGui.GetColorU32(CodexTheme.LineFrame));
            }

            ImGui.SetCursorPos(new Vector2(rowStartX + timelineWidth, cardTopPosY));
            DrawChangelogVersionCard(scale, entry, expanded, cardWidth);

            if (i < displayedEntries.Count - 1)
                ImGui.Dummy(new Vector2(0f, 14f * scale));
        }

        if (firstDiamondY.HasValue && firstDiamondY.Value < lastDiamondY)
            ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, firstDiamondY.Value), new Vector2(lineX, lastDiamondY), ImGui.GetColorU32(CodexTheme.LineCard));
    }

    private bool GetChangelogExpanded(ChangelogEntry entry, bool isLatest)
    {
        if (!changelogExpanded.TryGetValue(entry.Version, out var expanded))
        {
            expanded = isLatest;
            changelogExpanded[entry.Version] = expanded;
        }
        return expanded;
    }

    private void DrawChangelogVersionCard(float scale, ChangelogEntry entry, bool expanded, float width)
    {
        var showNewBadge = Version.TryParse(entry.Version, out var entryVersion) &&
            (changelogUnseenThreshold == null || entryVersion > changelogUnseenThreshold);

        if (!expanded)
        {
            DrawChangelogCollapsedRow(scale, entry, width, showNewBadge);
            return;
        }

        CodexTheme.BeginCard(scale, width: width);
        // Nutzervorgabe: Titel/Untertitel/Badges/Änderungsliste insgesamt 40px weiter nach rechts
        // (20px + weitere 20px).
        var extraIndent = 40f * scale;
        ImGui.Indent(extraIndent);
        var contentWidth = width - CodexTheme.CardPaddingX * 2f;
        var rowX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();

        var titleText = string.Format(Loc.T("Version {0}", "Version {0}"), entry.Version);
        float titleWidth, titleHeight;
        using (CodexTheme.FontChangelogVersionTitle.Push())
        {
            var size = ImGui.CalcTextSize(titleText);
            titleWidth = size.X;
            titleHeight = size.Y;
            ImGui.TextColored(CodexTheme.TextHeading, titleText);
        }

        if (showNewBadge)
        {
            ImGui.SameLine(0f, 8f * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (titleHeight - ChangelogBadgeHeight(scale)) / 2f);
            var badgeCursor = ImGui.GetCursorScreenPos();
            var badgeSize = DrawChangelogNewBadgeAt(scale, badgeCursor);
            ImGui.Dummy(badgeSize);
        }

        var dateText = FormatChangelogDate(entry.Date);
        float dateWidth, dateHeight;
        using (CodexTheme.FontChangelogMeta.Push())
        {
            var size = ImGui.CalcTextSize(dateText);
            dateWidth = size.X;
            dateHeight = size.Y;
        }
        // Nutzervorgabe: 15px Abstand zum rechten Kartenrand statt bündig mit contentWidth.
        ImGui.SetCursorPos(new Vector2(rowX + contentWidth - dateWidth - 15f * scale, rowY + (titleHeight - dateHeight) / 2f));
        using (CodexTheme.FontChangelogMeta.Push())
            ImGui.TextColored(CodexTheme.TextMuted, dateText);

        ImGui.SetCursorPos(new Vector2(rowX, rowY + titleHeight));
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(CodexTheme.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawChangelogChangeList(scale, entry, contentWidth);

        ImGui.Unindent(extraIndent);
        CodexTheme.EndCard();
    }

    private static void DrawChangelogChangeList(float scale, ChangelogEntry entry, float contentWidth)
    {
        var tagColumnWidth = ChangelogTagColumnWidth(scale);
        var tagBadgeHeight = ChangelogBadgeHeight(scale);
        var rowGap = 8f * scale;

        for (var i = 0; i < entry.Changes.Count; i++)
        {
            var change = entry.Changes[i];
            var rowTopY = ImGui.GetCursorPosY();
            var tagCursor = ImGui.GetCursorScreenPos();
            DrawChangelogTagBadge(scale, tagCursor, change.Type);

            // Hängender Einzug über ImGui.Indent/Unindent statt manueller SetCursorPos-Y-Buchführung -
            // derselbe robuste Ansatz wie DrawDebugWarningBox (dauerhafter Einzug statt Einzelpositionen,
            // damit auch mehrzeilig umgebrochener Text zuverlässig unter dem Textanfang bündig bleibt).
            ImGui.Indent(tagColumnWidth);
            using (CodexTheme.FontChangelogChangeText.Push())
            {
                var wrapPos = ImGui.GetCursorPosX() + (contentWidth - tagColumnWidth);
                ImGui.PushTextWrapPos(wrapPos);
                ImGui.TextColored(CodexTheme.TextValue, ChangelogService.ResolveText(change));
                ImGui.PopTextWrapPos();
            }
            ImGui.Unindent(tagColumnWidth);

            // Absicherung, falls das Tag-Badge (fester Höhe) höher wäre als eine einzeilige, kurze
            // Änderung - die Zeile reserviert dann trotzdem mindestens die Badge-Höhe.
            var minRowBottomY = rowTopY + tagBadgeHeight;
            if (ImGui.GetCursorPosY() < minRowBottomY)
                ImGui.Dummy(new Vector2(0f, minRowBottomY - ImGui.GetCursorPosY()));

            if (i < entry.Changes.Count - 1)
                ImGui.Dummy(new Vector2(0f, rowGap));
        }
    }

    /// <summary>Einzeilige, zugeklappte Versionskarte - Klick auf die Zeile klappt sie auf.</summary>
    private void DrawChangelogCollapsedRow(float scale, ChangelogEntry entry, float width, bool showNewBadge)
    {
        var padding = new Vector2(20f * scale, 12f * scale);
        float titleHeight;
        using (CodexTheme.FontChangelogCollapsedTitle.Push())
            titleHeight = ImGui.GetTextLineHeight();
        var rowHeight = titleHeight + padding.Y * 2f;
        var rowSize = new Vector2(width, rowHeight);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##CodexChangelogRow_" + entry.Version, rowSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + rowSize, ImGui.GetColorU32(hovered ? CodexTheme.BgSelected : CodexTheme.BgCard), CodexTheme.RoundingCard);
        drawList.AddRect(cursor, cursor + rowSize, ImGui.GetColorU32(CodexTheme.LineCard), CodexTheme.RoundingCard);

        var titleText = string.Format(Loc.T("Version {0}", "Version {0}"), entry.Version);
        float titleWidth;
        using (CodexTheme.FontChangelogCollapsedTitle.Push())
        {
            titleWidth = ImGui.CalcTextSize(titleText).X;
            drawList.AddText(new Vector2(cursor.X + padding.X, cursor.Y + padding.Y), ImGui.GetColorU32(CodexTheme.TextHeading), titleText);
        }

        var dateText = FormatChangelogDate(entry.Date);
        float dateWidth;
        using (CodexTheme.FontChangelogMeta.Push())
        {
            var size = ImGui.CalcTextSize(dateText);
            dateWidth = size.X;
            drawList.AddText(new Vector2(cursor.X + padding.X + titleWidth + 10f * scale, cursor.Y + (rowHeight - size.Y) / 2f), ImGui.GetColorU32(CodexTheme.TextMuted), dateText);
        }

        if (showNewBadge)
        {
            var badgeX = cursor.X + padding.X + titleWidth + 10f * scale + dateWidth + 12f * scale;
            DrawChangelogNewBadgeAt(scale, new Vector2(badgeX, cursor.Y + (rowHeight - ChangelogBadgeHeight(scale)) / 2f));
        }

        var changesLabel = entry.Changes.Count == 1
            ? Loc.T("1 Änderung", "1 change")
            : string.Format(Loc.T("{0} Änderungen", "{0} changes"), entry.Changes.Count);
        float changesWidth, changesHeight;
        using (CodexTheme.FontChangelogMeta.Push())
        {
            var size = ImGui.CalcTextSize(changesLabel);
            changesWidth = size.X;
            changesHeight = size.Y;
        }

        string chevronGlyph;
        float chevronWidth, chevronHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            chevronGlyph = FontAwesomeIcon.ChevronRight.ToIconString();
            var size = ImGui.CalcTextSize(chevronGlyph);
            chevronWidth = size.X;
            chevronHeight = size.Y;
        }

        var chevronX = cursor.X + width - padding.X - chevronWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(new Vector2(chevronX, cursor.Y + (rowHeight - chevronHeight) / 2f), ImGui.GetColorU32(CodexTheme.TextMuted), chevronGlyph);

        var changesX = chevronX - 8f * scale - changesWidth;
        using (CodexTheme.FontChangelogMeta.Push())
            drawList.AddText(new Vector2(changesX, cursor.Y + (rowHeight - changesHeight) / 2f), ImGui.GetColorU32(CodexTheme.TextMuted), changesLabel);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked)
            changelogExpanded[entry.Version] = true;

        ImGui.Dummy(rowSize);
    }

    private static Vector2 DrawChangelogNewBadgeAt(float scale, Vector2 cursor)
    {
        var label = Loc.T("NEU", "NEW");
        var padding = new Vector2(7f * scale, 1f * scale);
        float textWidth, textHeight;
        using (CodexTheme.FontChangelogTagBadge.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + badgeSize, ImGui.GetColorU32(CodexTheme.Accent), 3f * scale);
        using (CodexTheme.FontChangelogTagBadge.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(CodexTheme.TextOnAccent), label);
        return badgeSize;
    }

    private static float ChangelogBadgeHeight(float scale)
    {
        using (CodexTheme.FontChangelogTagBadge.Push())
            return ImGui.GetTextLineHeight() + 2f * scale;
    }

    private static Vector2 DrawChangelogTagBadge(float scale, Vector2 cursor, string type)
    {
        var label = ChangelogTagLabel(type);
        var (fg, bg, line) = ChangelogTagColors(type);
        var padding = new Vector2(7f * scale, 1f * scale);
        float textWidth, textHeight;
        using (CodexTheme.FontChangelogTagBadge.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + badgeSize, ImGui.GetColorU32(bg), 3f * scale);
        drawList.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(line), 3f * scale);
        using (CodexTheme.FontChangelogTagBadge.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(fg), label);
        return badgeSize;
    }

    /// <summary>Nutzervorgabe: Tag-Spalte so breit wie der längste Tag in der aktuellen Menüsprache (für Deutsch "VERBESSERT" deutlich breiter als "NEW"), statt einer festen Breite.</summary>
    private static float ChangelogTagColumnWidth(float scale)
    {
        var maxWidth = 0f;
        using (CodexTheme.FontChangelogTagBadge.Push())
        {
            foreach (var type in new[] { "new", "improved", "fixed", "removed" })
                maxWidth = MathF.Max(maxWidth, ImGui.CalcTextSize(ChangelogTagLabel(type)).X);
        }
        return maxWidth + 7f * scale * 2f + 10f * scale;
    }

    private static string ChangelogTagLabel(string type) => type switch
    {
        "new" => Loc.T("NEU", "NEW"),
        "fixed" => Loc.T("BEHOBEN", "FIXED"),
        "removed" => Loc.T("ENTFERNT", "REMOVED"),
        _ => Loc.T("VERBESSERT", "IMPROVED"),
    };

    private static (Vector4 Fg, Vector4 Bg, Vector4 Line) ChangelogTagColors(string type) => type switch
    {
        "new" => (CodexTheme.OkFg, CodexTheme.OkBg, CodexTheme.OkLine),
        "fixed" => (PluginUiKit.UiTheme.Active.InfoFg, PluginUiKit.UiTheme.Active.InfoBg, PluginUiKit.UiTheme.Active.InfoLine),
        "removed" => (CodexTheme.ErrFg, CodexTheme.ErrBg, CodexTheme.ErrLine),
        _ => (CodexTheme.WarnFg, CodexTheme.WarnBg, CodexTheme.WarnLine),
    };

    private static string FormatChangelogDate(string date)
    {
        if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            try
            {
                return parsed.ToString("d", new CultureInfo(Loc.T("de-DE", "en-US")));
            }
            catch (CultureNotFoundException)
            {
                return parsed.ToString("d", CultureInfo.InvariantCulture);
            }
        }
        return date;
    }
}
