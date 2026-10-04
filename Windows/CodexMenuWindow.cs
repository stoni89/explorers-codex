using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

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
        Database,
        Blacklist,
        Plugins,
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
        DrawNavItem(scale, MenuPage.Overlay, FontAwesomeIcon.Desktop, Loc.T("Overlay", "Overlay"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        CodexTheme.SectionLabel(Loc.T("ARCHIV", "ARCHIVE"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, MenuPage.Database, FontAwesomeIcon.Database, Loc.T("Datenbank", "Database"));
        DrawNavItem(scale, MenuPage.Blacklist, FontAwesomeIcon.Ban, Loc.T("Blacklist", "Blacklist"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        CodexTheme.SectionLabel(Loc.T("SYSTEM", "SYSTEM"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, MenuPage.Plugins, FontAwesomeIcon.Plug, Loc.T("Plugins", "Plugins"));

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

    private void DrawNavItem(float scale, MenuPage page, FontAwesomeIcon icon, string label)
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

        if (clicked)
            activePage = page;
    }

    /// <summary>Abschnitt 6 - Inhaltsbereich: bisher nur der Seitenkopf (Titel + Untertitel + Trenn-Ornament) für "General", noch ohne eigentliche Einstellungen.</summary>
    private void DrawContent(float scale)
    {
        ImGui.BeginChild("##CodexMenuContent", Vector2.Zero, false);
        ImGui.Indent(32f * scale);
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        DrawCloseButton(scale);

        var (pageTitle, pageSubtitle) = activePage switch
        {
            MenuPage.Overlay => (Loc.T("Overlay", "Overlay"), Loc.T("Passe das Overlay Fenster nach deinen Wünschen an.", "Adjust the overlay window to your liking.")),
            MenuPage.Database => (Loc.T("Datenbank", "Database"), Loc.T("Durchsuche alle bekannten Sammelobjekte.", "Browse all known collectibles.")),
            MenuPage.Blacklist => (Loc.T("Blacklist", "Blacklist"), Loc.T("Verwalte dauerhaft ausgeblendete Einträge.", "Manage permanently hidden entries.")),
            MenuPage.Plugins => (Loc.T("Plugins", "Plugins"), Loc.T("Begleit-Plugins, auf die sich der Codex für Automationen stützt.", "Companion plugins the Codex relies on for automation.")),
            _ => (Loc.T("Allgemein", "General"), Loc.T("Grundeinstellungen des Plugins.", "Basic plugin settings.")),
        };

        using (CodexTheme.FontTitleMenu.Push())
            ImGui.TextColored(CodexTheme.TextHeading, pageTitle);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
        using (CodexTheme.FontMenuSubtitle.Push())
            ImGui.TextColored(CodexTheme.TextSecondary, pageSubtitle);

        ImGui.Dummy(new Vector2(0f, 3f * scale));
        CodexTheme.DrawDividerOrnament();

        ImGui.Dummy(new Vector2(0f, 16f * scale));

        switch (activePage)
        {
            case MenuPage.Overlay:
                DrawOverlayPage(scale);
                break;
            case MenuPage.Plugins:
                DrawPluginsPage(scale);
                break;
            case MenuPage.Database:
            case MenuPage.Blacklist:
                // Noch nicht umgesetzt (Nutzeranforderung bisher nur: Menüpunkte + Icons anlegen) -
                // Platzhalter statt stillschweigend die "Allgemein"-Seite anzuzeigen.
                using (CodexTheme.FontSubtitleItalic.Push())
                    ImGui.TextColored(CodexTheme.TextTertiary, Loc.T("Noch nicht umgesetzt.", "Not implemented yet."));
                break;
            default:
                DrawGeneralPage(scale);
                break;
        }

        ImGui.Unindent(32f * scale);
        ImGui.EndChild();
    }

    /// <summary>
    /// "Schließen"-Knopf oben rechts auf JEDER Menüseite (Nutzeranforderung, Vorbild Entwurfsbild mit
    /// einem "Speichern"-Knopf dort - hier aber bewusst zu "Schließen" umbenannt und schließt beim
    /// Klick nur das Menü, da dieses Plugin ohnehin sofort bei jeder Änderung speichert, siehe
    /// DrawGeneralPage-Kommentar - ein "Speichern"-Knopf ohne echten Zweck wäre irreführend).
    /// </summary>
    private void DrawCloseButton(float scale)
    {
        var label = Loc.T("Schließen", "Close");
        // +8px, dann nochmal +20px Breite (insgesamt +28px), +5px Höhe (Nutzervorgabe) - Padding
        // wirkt auf beide Seiten, daher halbiert.
        var padding = new Vector2(30f * scale, 10.5f * scale);

        float textWidth, textHeight;
        using (CodexTheme.FontBodyBold.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            textWidth = textSize.X;
            textHeight = textSize.Y;
        }

        var buttonSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        // Derselbe rechte Randabstand wie der linke Indent dieses Inhaltsbereichs (Nutzeranforderung:
        // optisch symmetrisch zum linken Rand).
        var rightMargin = 32f * scale;
        var buttonX = ImGui.GetWindowContentRegionMax().X - buttonSize.X - rightMargin;
        var rowY = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(buttonX, rowY));

        ImGui.PushStyleColor(ImGuiCol.Button, CodexTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CodexTheme.Accent with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, CodexTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, CodexTheme.TextOnAccent);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        using (CodexTheme.FontBodyBold.Push())
        {
            if (ImGui.Button(label))
                IsOpen = false;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        ImGui.SetCursorPosY(rowY);
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
        // Nutzeranforderung: die Vorschau-Box wieder in voller (Original-)Breite zeigen - nur der
        // INHALT (siehe ContentZoomOutFactor in DrawOverlayPreviewWindow) bleibt kleiner gezeichnet,
        // damit alle sechs Beispiel-Items ohne Scrollbalken hineinpassen.
        var maxWidth = MathF.Min(columnWidth, CodexTheme.OverlayWidth * scale);
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
        DrawPreviewTabsAndFilters(scale);
        DrawPreviewList(scale, layoutScale);

        // Zierecken (wie beim echten Overlay) - innerhalb desselben Child-Fensters gezeichnet, per
        // ImGui.GetWindowPos()/GetWindowSize() (siehe CodexTheme.DrawCornerOrnaments-Kommentar), daher
        // VOR EndChild aufgerufen.
        CodexTheme.DrawCornerOrnaments(CodexTheme.CornerLenOverlay * scale, CodexTheme.CornerThickOverlay * scale, CodexTheme.CornerInsetOverlay * scale);

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
                CodexOverlayWindow.DrawAutoButton(button, label, new Vector2(width, buttonHeight), anyRunning, scale, shadow: false);

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
        ImGui.Separator();
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

    private static void DrawPreviewTabsAndFilters(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.Indent(14f * scale);

        CodexOverlayWindow.DrawTab("zone", Loc.T("Zone", "Zone"), PreviewItems.Length, selected: true, scale, shadow: false);
        ImGui.SameLine(0f, 18f * scale);
        CodexOverlayWindow.DrawTab("todo", Loc.T("ToDo-Liste", "To-do list"), 0, selected: false, scale, shadow: false);

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
        CodexOverlayWindow.DrawFilterButton("type", typeLabel, active: false, typeWidth, scale, shadow: false);
        ImGui.SameLine(0f, 6f * scale);
        CodexOverlayWindow.DrawFilterButton("cur", currencyLabel, active: false, currencyWidth, scale, shadow: false);

        ImGui.Unindent(14f * scale);
        ImGui.Separator();
    }

    private static void DrawPreviewList(float scale, float layoutScale)
    {
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        // Höhe bewusst an layoutScale (nicht am kleineren Inhalts-"scale") bemessen - siehe
        // ContentZoomOutFactor-Kommentar: der Listenbereich behält seine reguläre Größe, nur die
        // Zeilen darin werden kleiner gezeichnet, wodurch mehr davon hineinpassen.
        ImGui.BeginChild("##CodexPreviewList", new Vector2(0f, 290f * layoutScale), false);
        ImGui.Indent(8f * scale);

        for (var itemIndex = 0; itemIndex < PreviewItems.Length; itemIndex++)
        {
            var (type, name, amount, currencyName, locked, linked) = PreviewItems[itemIndex];

            if (itemIndex == 0)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 8f * scale);
            else
                ImGui.Dummy(new Vector2(0f, CodexTheme.OverlayRowGap * scale));
            if (itemIndex > 0)
                ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, CodexTheme.OverlayRowGap * scale));

            var (badgeBg, badgeFg) = CodexOverlayWindow.GetTypeBadgeColors(type);
            if (locked)
            {
                badgeBg = badgeBg with { W = badgeBg.W * 0.55f };
                badgeFg = badgeFg with { W = badgeFg.W * 0.55f };
            }

            var badgeLabel = CodexOverlayWindow.GetBadgeLabel(type);

            float badgeFontHeight;
            using (CodexTheme.FontTypeBadge.Push())
                badgeFontHeight = ImGui.GetFontSize();
            var badgeSize = new Vector2(90f * scale, badgeFontHeight + 4f * scale);
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
                var lockSize = new Vector2(22f * scale, 22f * scale);
                ImGui.SameLine(0f, 6f * scale);
                DrawPreviewGateLockButton(lockSize, scale);
            }

            // Wie CodexOverlayWindow.DrawList (Nutzeranforderung: identisch im Preview abändern) -
            // Betrag + 16x16-Währungsicon statt ausgeschriebenem Namen, beide in einer Gruppe für
            // EINEN Hover-Bereich, Tooltip zeigt Betrag + vollen Währungsnamen.
            if (amount != 0)
            {
                var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
                var amountText = amount.ToString("N0", culture);
                var amountWidth = ImGui.CalcTextSize(amountText).X;
                var iconSize = 16f * scale;
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
            plugin.CompactOverlayWindow.IsOpen = showOverlay;
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
                caption: Loc.T("Zeigt an, ob die Retainer benötigte Currencies besitzen.\nRequires Allagan Tools.",
                    "Zeigt an, ob die Retainer benötigte Currencies besitzen.\nRequires Allagan Tools.")))
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

    // Siehe DrawDisplayCard - true, solange der Deckkraft-Wert seit dem letzten Save() geändert
    // wurde, aber noch nicht persistiert ist (wird erst beim Loslassen des Reglers gespeichert).
    private static bool opacityDirty;

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
    // MainWindow.DrawDependenciesPage (MainWindow.Dependencies/IsPluginLoaded/IsDependencySatisfied/
    // HasMissingRequiredDependency, dafür dort auf internal gestellt), nur komplett neu im
    // Explorer-Codex-Theme gestaltet. "Check again" löst keine eigene Methode aus, da die Prüfung
    // ohnehin bei jedem Draw-Aufruf frisch ausgewertet wird (siehe DrawCheckAgainButton-Kommentar).

    /// <summary>Kurze, für diese Seite neu formulierte Beschreibungen (Nutzeranforderung) - bewusst NICHT die längeren, technischeren MainWindow.Dependencies-Beschreibungen (die bleiben der alten Seite vorbehalten).</summary>
    private static (string De, string En) GetShortPluginDescription(string internalName) => internalName switch
    {
        "vnavmesh" => ("Navigation & Pfadfindung", "Navigation & pathing"),
        "Questionable" => ("Quest-Automation", "Quest automation"),
        "Lifestream" => ("Teleport & Reisen", "Teleport & travel"),
        "Saucy" => ("Gold-Saucer-Minispiele", "Gold Saucer minigames"),
        "TextAdvance" => ("Dialoge automatisch weiterklicken", "Auto-advance dialogue"),
        "InventoryTools" => ("Retainer-Bestände neben Währungen", "Retainer stock next to currencies"),
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
        var total = MainWindow.Dependencies.Length;
        var installedCount = MainWindow.Dependencies.Count(d => MainWindow.IsPluginLoaded(d.InternalName));
        var ready = !MainWindow.HasMissingRequiredDependency();

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
            var missingNames = MainWindow.Dependencies
                .Where(d => d.Required && !MainWindow.IsDependencySatisfied(d.InternalName, d.Group))
                .Select(d => d.Group ?? d.InternalName)
                .Distinct()
                .Select(key => key == MainWindow.CombatDependencyGroup
                    ? Loc.T("Kampf-Plugin", "Combat plugin")
                    : MainWindow.Dependencies.First(d => d.InternalName == key).DisplayName)
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
            var firstMissing = MainWindow.Dependencies.First(d => d.Required && !MainWindow.IsDependencySatisfied(d.InternalName, d.Group));
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
        var combatEntries = MainWindow.Dependencies.Where(d => d.Group == MainWindow.CombatDependencyGroup).ToArray();

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
                var installed = MainWindow.IsPluginLoaded(entry.InternalName);
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
        var requiredStandalone = MainWindow.Dependencies.Where(d => d.Required && d.Group == null).ToArray();
        var installedCount = requiredStandalone.Count(d => MainWindow.IsPluginLoaded(d.InternalName));
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
                var installed = MainWindow.IsPluginLoaded(entry.InternalName);
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

        var optional = MainWindow.Dependencies.Where(d => !d.Required).ToArray();
        // Derselbe rechte Randabstand wie bei den anderen Gruppen, über eine Tabelle statt BeginChild
        // - siehe DrawRequiredPluginsGroup-Kommentar (Bugfix: Scrollbalken der Seite fehlte).
        if (ImGui.BeginTable("##CodexPluginsOptionalWidth", 1, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableNextColumn();
            CodexTheme.BeginCard(scale);
            for (var i = 0; i < optional.Length; i++)
            {
                var entry = optional[i];
                var installed = MainWindow.IsPluginLoaded(entry.InternalName);
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
}
