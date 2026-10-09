using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace TheExplorersCodex.Windows;

/// <summary>
/// Neues Overlay-Design (siehe NewDesign/DESIGN_SPEC.md) - wird schrittweise aufgebaut (Abschnitt 8,
/// "Reihenfolge der Umsetzung"). Aktueller Stand: Kopfzeile (2), Zone (2), Auto-Knöpfe für ALLE
/// sieben Automationen (3, erweitert über die drei "endgültigen" der Spezifikation hinaus),
/// Währungsübersicht (5.4), Reiter/Filter (4), Item-Liste für ALLE Sammelobjekt-Typen (6, erweitert
/// über Reittier/Begleiter hinaus - siehe Plugin.GetZoneOverlayItems-Kommentar), gesperrte Einträge
/// mit Tooltip (5.7), Transparenz und Textschatten (5.9, teilt sich Configuration.CompactTransparency
/// mit dem alten Overlay). Läuft komplett eigenständig NEBEN dem bisherigen
/// <see cref="CompactOverlayWindow"/> - dieses bleibt unverändert, solange das neue Design noch nicht
/// fertig ist (per Chat-Befehl "/exc newdesign" umschaltbar).
///
/// Noch NICHT umgesetzt (spätere Schritte): Kompakt-Modus (5.8), das Einstellungsmenü (6-8). Das
/// Fortschreiten der Automationen (Automation.Update()) läuft über Plugin.UpdateZoneAutomations,
/// das von diesem Fenster UND von CompactOverlayWindow aus aufgerufen wird (es genügt, dass eines
/// der beiden gerade zeichnet) - ursprünglich lief das ausschließlich über CompactOverlayWindow,
/// wodurch eine hier gestartete Automation stehen blieb, solange das alte Overlay nicht auch offen
/// war (Nutzer-Report).
/// </summary>
public class CodexOverlayWindow : Window
{
    private readonly Plugin plugin;

    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    public CodexOverlayWindow(Plugin plugin) : base("##CodexNewDesignOverlay", BaseFlags)
    {
        this.plugin = plugin;
        Size = new Vector2(CodexTheme.OverlayWidth, 0f);
        SizeCondition = ImGuiCond.Always;
    }

    // Abschnitt 5.9 - ab ca. 70% Overlay-Transparenz (dieselbe Einstellung wie beim alten Overlay,
    // siehe Configuration.CompactTransparency) werden Textschatten aktiv, Fensterrahmen und
    // Trennlinien ausgeblendet (Zierecken bleiben). Als Feld gehalten, da PreDraw() ihn einmal pro
    // Frame bestimmt und alle Draw-Methoden ihn danach lesen.
    private bool shadowActive;

    // Multiplikator (0-1) für die Alpha der Filter-/Auto-Knöpfe und Kategorie-Badges, aus
    // Configuration.CompactTransparency abgeleitet (siehe PreDraw) - 1 = voll deckend (0% Transparenz),
    // 0 = unsichtbar (100% Transparenz).
    private float overlayContentAlpha = 1f;

    // Abschnitt 5.1 - Einklappen (nur Kopfzeile sichtbar) und Positionssperre, analog zu
    // CompactOverlayWindow.collapsed/Configuration.CompactLocked. Die Sperre teilt sich bewusst
    // dieselbe Configuration.CompactLocked wie das alte Overlay (gleiche Grundidee wie
    // CompactTransparency/ShowCurrencyWallet) - "Einklappen" bleibt dagegen ein rein lokaler Zustand
    // dieses Fensters, wie im alten Overlay auch (dort ebenfalls kein Configuration-Feld).
    private bool collapsed;

    // Siehe DrawHeader - Debounce für den Einfachklick auf die Titelzeile (Menü öffnen), damit ein
    // Doppelklick (ein-/ausklappen) nicht zusätzlich das Menü aufpoppen lässt. -1 = kein ausstehender
    // Einfachklick.
    private double pendingTitleSingleClickTime = -1d;

    // Siehe DrawOverlayDebugInfo - wie CodexMenuWindow.debugCopyConfirmedUntil, eigener Zustand für
    // den Kopieren-Knopf dieses Fensters.
    private long overlayDebugCopyConfirmedUntil;

    public override bool DrawConditions() =>
        Plugin.ClientState.IsLoggedIn;

    // Siehe CodexMenuWindow.PreDraw-Kommentar (Nutzer-Report "Position nach Neustart/Rebuild nicht
    // gehalten") - gleicher Fix: einmal pro Plugin-Ladevorgang mit ImGuiCond.Always erzwingen statt
    // ImGui's eigener (von dalamudUI.ini überlagerter) FirstUseEver-Verfolgung zu vertrauen.
    private bool appliedSavedOverlayPosition;

    public override void PreDraw()
    {
        // Bug (Nutzer-Report "Overlay nicht mehr verschiebbar"): siehe CodexMenuWindow.PreDraw-
        // Kommentar - Dalamuds Window-Basisklasse ruft, solange Position einen Wert hat, JEDEN Frame
        // erneut ImGui.SetNextWindowPos(Position, PositionCondition) auf. Mit ImGuiCond.Always ohne
        // jemals Position wieder auf null zu setzen, wurde die Position dadurch dauerhaft erzwungen,
        // jeder Ziehversuch des Nutzers sofort wieder zurückgesetzt.
        if (!appliedSavedOverlayPosition && plugin.Configuration.OverlayWindowPosition is { } savedOverlayPosition)
        {
            Position = savedOverlayPosition;
            PositionCondition = ImGuiCond.Always;
            appliedSavedOverlayPosition = true;
        }
        else if (appliedSavedOverlayPosition)
        {
            Position = null;
        }

        var transparency = plugin.Configuration.CompactTransparency;
        shadowActive = transparency >= 0.7f;
        // Nutzeranforderung: Filter-/Auto-Knöpfe und Kategorie-Badges sollen mit demselben Regler wie
        // der Fensterhintergrund (CompactTransparency) ausbleichen, nicht erst binär ab der 70%-
        // Schattenschwelle (shadowActive) komplett unverändert bleiben.
        overlayContentAlpha = 1f - Math.Clamp(transparency, 0f, 1f);

        var flags = BaseFlags;
        if (plugin.Configuration.CompactLocked)
            flags |= ImGuiWindowFlags.NoMove;
        if (collapsed)
            flags |= ImGuiWindowFlags.NoResize;
        Flags = flags;

        CodexTheme.PushStyle();
        ImGui.PushStyleColor(ImGuiCol.WindowBg, CodexTheme.OverlayBg(transparency));
        ImGui.PushStyleColor(ImGuiCol.Border, shadowActive ? new Vector4(0f, 0f, 0f, 0f) : CodexTheme.LineFrame);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, shadowActive ? 0f : 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, CodexTheme.RoundingOverlay);
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
        var territoryId = Plugin.ClientState.TerritoryType;

        // Lässt gestartete Auto-Funktionen tatsächlich fortschreiten (Nutzer-Report: "Klick macht
        // nichts") - vorher lief Automation.Update() ausschließlich über CompactOverlayWindow, siehe
        // Plugin.UpdateZoneAutomations-Kommentar. Bewusst VOR dem collapsed-Zweig (wie im alten
        // Overlay) - eine laufende Automation soll beim Einklappen weiterlaufen.
        plugin.UpdateZoneAutomations(territoryId);

        DrawHeader(scale);

        // Eingeklappt (Nutzeranforderung, analog zu CompactOverlayWindow.collapsed) - nur die
        // Kopfzeile bleibt sichtbar, der Rest entfällt komplett statt nur ausgegraut zu werden.
        if (!collapsed)
        {
            DrawZoneRow(scale, territoryId);
            if (plugin.Configuration.ShowDebugInfo)
                DrawOverlayDebugInfo(scale);
            DrawAutoButtonsRow(scale, territoryId);
            DrawAutomationStatusText(scale);
            // Trennlinie unter dem Zonen-Block - bewusst HIER (immer gezeichnet), nicht mehr am Ende
            // von DrawAutoButtonsRow (das bei leerer Knopfreihe sofort zurückkehrt, ohne sie zu
            // zeichnen, siehe dort) - Abschnitt 5.4 setzt eine immer vorhandene Trennlinie über der
            // Währungsübersicht voraus.
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            if (!shadowActive)
                ImGui.Separator();
            DrawCurrencyWallet(scale, territoryId);
            DrawTabsAndFilters(scale, territoryId);
            DrawList(scale, territoryId);
        }

        CodexTheme.DrawOverlayCornerOrnaments(CodexTheme.CornerInsetOverlay * scale);

        // Nutzeranforderung: Position persistieren - kein config.Save() bei jedem Frame während des
        // Ziehens (siehe CodexMenuWindow.Draw-Kommentar, dasselbe "database is locked"-Problem).
        var windowPos = ImGui.GetWindowPos();
        var config = plugin.Configuration;
        if (config.OverlayWindowPosition is not { } savedPos || Vector2.DistanceSquared(savedPos, windowPos) > 0.25f)
        {
            config.OverlayWindowPosition = windowPos;
            overlayPositionDirty = true;
        }
        if (overlayPositionDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            config.Save();
            overlayPositionDirty = false;
        }
    }

    private bool overlayPositionDirty;

    /// <summary>
    /// Abschnitt 5.1 - Kompass-Symbol, Titel, drei Symbol-Knöpfe, Trennlinie. Icon/Titel/Knöpfe haben
    /// unterschiedliche Eigenhöhen (Icon 20px, Knöpfe 22px, Titelschrift ~18px) - werden daher über
    /// SetCursorPosY je Element einzeln auf eine gemeinsame Zeilenhöhe zentriert, statt sich auf
    /// ImGuis automatische (oben ausgerichtete) SameLine-Zeile zu verlassen (Nutzeranforderung:
    /// vertikal mittig vom oberen Rand bis zur Trennlinie).
    /// </summary>
    private void DrawHeader(float scale)
    {
        ImGui.Indent(14f * scale);

        var iconSize = 20f * scale;
        var buttonSize = new Vector2(17f * scale, 17f * scale);
        float textHeight;
        using (CodexTheme.FontTitleOverlay.Push())
            textHeight = ImGui.GetFontSize();

        var rowHeight = MathF.Max(iconSize, MathF.Max(buttonSize.Y, textHeight));
        var verticalPadding = 6f * scale; // oben/unten gleich groß -> Zeile vertikal mittig im Kopfbereich.

        ImGui.Dummy(new Vector2(0f, verticalPadding));
        var rowStartY = ImGui.GetCursorPosY();

        // Klickbereich über Icon + Titeltext (Nutzeranforderung) - Rechteck wird VOR dem Zeichnen als
        // Fensterkoordinate gemerkt, der rechte Rand danach aus dem tatsächlich gezeichneten Titeltext
        // übernommen (GetItemRectMax), damit die Trefferfläche genau bis zum Textende reicht.
        var titleAreaMin = ImGui.GetCursorScreenPos();

        ImGui.SetCursorPosY(rowStartY + (rowHeight - iconSize) / 2f);
        CodexTheme.DrawCompassIcon(iconSize);
        ImGui.SameLine(0f, 8f * scale);

        // Schriftzeilenhöhe reserviert unten Platz für Unterlängen (g/y/...) - bei einem reinen
        // Versalien-Titel ("THE EXPLORER'S CODEX") bleibt dieser Platz leer, wodurch die eigentliche
        // Tinte (Buchstaben) beim Zentrieren an der VOLLEN Zeilenhöhe optisch zu tief wirkt. Deshalb
        // ein kleiner, empirischer Ausgleich nach oben, damit der Titel wirklich mit Icon/Knöpfen auf
        // einer Höhe erscheint (Nutzeranforderung: vertikal mittig).
        var textDescenderCompensation = textHeight * 0.14f;
        ImGui.SetCursorPosY(rowStartY + (rowHeight - textHeight) / 2f - textDescenderCompensation + 3f * scale);
        using (CodexTheme.FontTitleOverlay.Push())
            CodexTheme.TextShadowed("THE EXPLORER'S CODEX", CodexTheme.TextHeading, shadowActive);

        // Einfachklick öffnet das Einstellungsmenü, Doppelklick klappt stattdessen ein/aus
        // (Nutzeranforderung) - unsichtbarer Button über Icon+Titeltext, NACH dem Zeichnen platziert,
        // damit er die Klicks über dieser Fläche abfängt, ohne das Aussehen zu verändern.
        //
        // Der Einfachklick darf NICHT sofort beim ersten Klick auslösen: Ein Doppelklick besteht aus
        // zwei Einzelklicks hintereinander, ImGui.IsItemClicked() feuert also auch beim ersten Klick
        // eines Doppelklicks. Ohne Verzögerung öffnete ein Doppelklick deshalb KURZ das Menü (erster
        // Klick), bevor beim zweiten Klick zusätzlich noch ein-/ausgeklappt wurde (Nutzer-Report:
        // Doppelklick klappt nicht zuverlässig ein/aus). Der erste Klick merkt sich daher nur den
        // Zeitpunkt; die Menü-Aktion feuert erst, wenn bis zum Ablauf von ImGui's eigenem
        // Doppelklick-Zeitfenster KEIN zweiter Klick (= Doppelklick) folgte.
        var titleAreaMax = new Vector2(ImGui.GetItemRectMax().X, titleAreaMin.Y + rowHeight);
        var cursorBeforeTitleButton = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(titleAreaMin);
        ImGui.InvisibleButton("##CodexTitleArea", titleAreaMax - titleAreaMin);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            pendingTitleSingleClickTime = -1d;
            collapsed = !collapsed;
        }
        else if (ImGui.IsItemClicked())
        {
            pendingTitleSingleClickTime = ImGui.GetTime();
        }
        ImGui.SetCursorScreenPos(cursorBeforeTitleButton);

        if (pendingTitleSingleClickTime >= 0d && ImGui.GetTime() - pendingTitleSingleClickTime > ImGui.GetIO().MouseDoubleClickTime)
        {
            pendingTitleSingleClickTime = -1d;
            plugin.OpenOptions();
        }

        // Rechts: Sperren, Einklappen, Schließen - mit echtem Abstand zueinander UND zum rechten
        // Fensterrand, absolut vom tatsächlichen rechten Fensterrand aus positioniert
        // (GetWindowContentRegionMax), nicht vom aktuellen Cursor aus - robuster, siehe derselbe Fix
        // bei den Filter-Knöpfen.
        // Einheitlicher Abstand zwischen allen drei Knöpfen (Nutzeranforderung).
        const float buttonGap = 14f;
        const float rightMargin = 18f;
        var buttonsWidth = buttonSize.X * 3f + buttonGap * scale * 2f;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonsWidth - rightMargin * scale);
        ImGui.SetCursorPosY(rowStartY + (rowHeight - buttonSize.Y) / 2f);

        // Sperren/Einklappen/Schließen - jetzt mit echter Wirkung (Nutzeranforderung), analog zu
        // CompactOverlayWindow.DrawLockButtonTopRight/DrawCollapseButtonTopRight/
        // DrawCloseButtonTopRight (Lock teilt sich dieselbe Configuration.CompactLocked).
        var locked = plugin.Configuration.CompactLocked;
        if (DrawHeaderIconButton(locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen, "##CodexLock", buttonSize,
                locked ? Loc.T("Fenster entsperren", "Unlock window") : Loc.T("Fenster sperren (Position fixieren)", "Lock window (fix position)"),
                locked ? CodexTheme.Accent : null))
        {
            plugin.Configuration.CompactLocked = !locked;
            plugin.Configuration.Save();
        }

        ImGui.SameLine(0f, buttonGap * scale);
        if (DrawHeaderIconButton(collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp, "##CodexCollapse", buttonSize,
                collapsed ? Loc.T("Ausklappen", "Expand") : Loc.T("Einklappen", "Collapse")))
            collapsed = !collapsed;

        ImGui.SameLine(0f, buttonGap * scale);
        if (DrawHeaderIconButton(FontAwesomeIcon.Times, "##CodexClose", buttonSize, Loc.T("Schließen", "Close")))
            IsOpen = false;

        ImGui.SetCursorPosY(rowStartY + rowHeight);
        ImGui.Unindent(14f * scale);
        ImGui.Dummy(new Vector2(0f, verticalPadding));
        if (!shadowActive)
            ImGui.Separator();
    }

    /// <summary>
    /// Kopfzeilen-Icon-Knopf (Sperren/Einklappen/Schließen) - manuell über InvisibleButton +
    /// zentriert gezeichnetes Icon statt eines nativen ImGui.Button(icon, size): dessen interne
    /// Text-Beschneidung auf die Knopfgröße schnitt breitere Glyphen (z.B. LockOpen, dessen
    /// geöffneter Bügel seitlich übersteht) sichtbar ab (Nutzer-Report). Das Icon selbst darf hier
    /// über den Knopfrand hinausragen, bleibt aber mittig zentriert.
    /// </summary>
    internal static bool DrawHeaderIconButton(FontAwesomeIcon icon, string id, Vector2 size, string? tooltip = null, Vector4? iconColor = null)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        if (hovered)
        {
            // Nutzeranforderung: Hover-Hintergrund etwas größer als der eigentliche Knopf, nicht exakt
            // knopfgroß - wirkt sonst zu knapp um das Icon herum.
            var hoverPadding = new Vector2(3f, 3f);
            drawList.AddRectFilled(cursor - hoverPadding, cursor + size + hoverPadding, ImGui.GetColorU32(CodexTheme.BgSelected), CodexTheme.RoundingControl);
            if (!string.IsNullOrEmpty(tooltip))
                ShowTooltip(tooltip);
        }

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            var glyphWidth = ImGui.CalcTextSize(glyph).X;
            // Vertikal an der gemeinsamen Zeilenhöhe der Icon-Schrift zentriert (GetFontSize()),
            // NICHT an der individuellen Glyphen-Bounding-Box - die unterscheidet sich je Icon (z.B.
            // ChevronUp ist "kürzer" als Times) und ließ die Icons sonst auf unterschiedlicher Höhe
            // sitzen (Nutzeranforderung: einheitliche vertikale Höhe).
            var lineHeight = ImGui.GetFontSize();
            drawList.AddText(
                cursor + new Vector2((size.X - glyphWidth) / 2f, (size.Y - lineHeight) / 2f),
                ImGui.GetColorU32(iconColor ?? CodexTheme.TextSecondary),
                glyph);
        }

        return clicked;
    }

    /// <summary>Abschnitt 5.2 - Zonenname + Zonen-Id.</summary>
    private void DrawZoneRow(float scale, uint territoryId)
    {
        ImGui.Dummy(new Vector2(0f, 0f));
        ImGui.Indent(14f * scale);

        var zoneName = Plugin.GetZoneName(territoryId);

        using (CodexTheme.FontZoneName.Push())
            CodexTheme.TextShadowed(zoneName, CodexTheme.TextHeading, shadowActive);
        ImGui.SameLine(0f, 8f * scale);
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3f * scale);
        using (CodexTheme.FontBodySmall.Push())
            CodexTheme.TextShadowed($"Zone {territoryId}", CodexTheme.TextMuted, shadowActive);

        ImGui.Unindent(14f * scale);
    }

    /// <summary>Siehe Configuration.ShowDebugInfo - Zonen-Name/-Id und Weltposition
    /// direkt im Overlay unterhalb der Zonen-Zeile, mit Kopieren-Knopf für die Weltposition daneben
    /// (dasselbe "x f, y f, z f"-Format wie CodexMenuWindow.DrawDebugCopyButton) - erspart das Öffnen
    /// des Hauptmenüs nur für diese Werte, z.B. beim Ermitteln von Sightseeing-/Angel-Positionen.</summary>
    private void DrawOverlayDebugInfo(float scale)
    {
        ImGui.Indent(14f * scale);
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        var posText = playerPos.HasValue
            ? $"X {playerPos.Value.X.ToString("F3", CultureInfo.InvariantCulture)}  " +
              $"Y {playerPos.Value.Y.ToString("F3", CultureInfo.InvariantCulture)}  " +
              $"Z {playerPos.Value.Z.ToString("F3", CultureInfo.InvariantCulture)}"
            : Loc.T("Weltposition nicht verfügbar", "World position not available");

        var rowStartX = ImGui.GetCursorPosX();
        float maxPosTextWidth;
        using (CodexTheme.FontBodySmall.Push())
        {
            // Feste Breite für den längsten realistisch vorkommenden Text reserviert (negative,
            // vierstellige Koordinaten) statt die tatsächliche Textbreite zu nehmen (Nutzervorgabe:
            // Kopieren-Knopf soll nicht wandern, wenn sich die Ziffernbreite der Koordinaten durch
            // Vorzeichen/Stellenzahl ändert).
            maxPosTextWidth = ImGui.CalcTextSize("X -9999.999  Y -9999.999  Z -9999.999").X;
            CodexTheme.TextShadowed(posText, CodexTheme.TextTertiary, shadowActive);
        }

        if (playerPos.HasValue)
        {
            ImGui.SameLine(rowStartX + maxPosTextWidth + 6f * scale, 0f);
            DrawOverlayDebugCopyButton(scale, playerPos.Value);
        }

        ImGui.Unindent(14f * scale);
        ImGui.Dummy(new Vector2(0f, 2f * scale));
    }

    /// <summary>Kleiner Kopieren-Knopf neben der Weltposition (siehe DrawOverlayDebugInfo) - bewusst nur
    /// ein Icon statt des vollen, beschrifteten Knopfs aus CodexMenuWindow.DrawDebugCopyButton, dafür
    /// ist im schmalen Overlay kein Platz.</summary>
    private void DrawOverlayDebugCopyButton(float scale, Vector3 position)
    {
        var confirmed = Environment.TickCount64 < overlayDebugCopyConfirmedUntil;
        var icon = confirmed ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy;
        var fg = confirmed ? CodexTheme.OkFg : CodexTheme.TextHeading;
        var border = confirmed ? CodexTheme.OkFg : CodexTheme.LineControl;

        var size = new Vector2(18f * scale, 18f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##CodexOverlayDebugCopy", size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(hovered ? CodexTheme.BgSelected : new Vector4(0f, 0f, 0f, 0f)), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            var nativeIconPx = ImGui.GetFontSize();
            var desiredIconPx = 11f * scale;
            ImGui.SetWindowFontScale(desiredIconPx / nativeIconPx);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(cursor + (size - glyphSize) / 2f, ImGui.GetColorU32(fg), glyph);
            ImGui.SetWindowFontScale(1f);
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ShowTooltip(confirmed ? Loc.T("Kopiert", "Copied") : Loc.T("Weltposition kopieren", "Copy world position"));
        }

        if (clicked)
        {
            ImGui.SetClipboardText($"{position.X.ToString(CultureInfo.InvariantCulture)}f, " +
                                    $"{position.Y.ToString(CultureInfo.InvariantCulture)}f, " +
                                    $"{position.Z.ToString(CultureInfo.InvariantCulture)}f");
            overlayDebugCopyConfirmedUntil = Environment.TickCount64 + 1500;
        }
    }

    // ---- Abschnitt 5.2: Auto-Knöpfe ----

    internal static string AutoButtonLabel(string key) => key switch
    {
        "quest" => Loc.T("Auto-Quest", "Auto Quest"),
        "sight" => Loc.T("Auto-Erkundung", "Auto Sightseeing"),
        "aeth" => Loc.T("Auto-Ätheryt", "Auto Aetheryte"),
        "huntinglog" => Loc.T("Auto-Hunting Log", "Auto Hunting Log"),
        "aethercurrent" => Loc.T("Auto-Ätherströmung", "Auto Aether Current"),
        "chocobokeep" => Loc.T("Auto-Chocobokeep", "Auto Chocobokeep"),
        "tripletriad" => Loc.T("Auto-Triple Triad", "Auto Triple Triad"),
        _ => key,
    };

    internal static readonly Vector4 RunningCountFg = new(0.227f, 0.165f, 0.063f, 1f); // #3A2A10

    // Exakte Maße laut Nutzervorgabe (Pixel, vor Skalierung): 8px Innenabstand links, 10px rechts,
    // 9x9px Symbol mit 6px Abstand zum Text, 6px Abstand Text-zu-Zahl, 6px Abstand zwischen Knöpfen
    // (waagerecht wie senkrecht beim Umbruch), 28px feste Höhe, 3px Eckenrundung.
    // Internal statt private: wird auch von CodexMenuWindow.DrawOverlayPreviewWindow wiederverwendet,
    // damit die Overlay-Vorschau die exakt gleichen Maße wie das echte Overlay benutzt.
    internal const float AutoButtonPaddingLeft = 8f;
    internal const float AutoButtonPaddingRight = 10f;
    internal const float AutoButtonIconSize = 9f;
    internal const float AutoButtonIconGap = 6f;
    internal const float AutoButtonCountGap = 6f;
    internal const float AutoButtonGap = 6f;
    internal const float AutoButtonHeight = 28f;
    internal const float AutoButtonRounding = 3f;

    /// <summary>Abschnitt 5.2 - Knopfreihe mit den drei "endgültigen" Auto-Funktionen (siehe Plugin.GetZoneAutomationButtons).</summary>
    private void DrawAutoButtonsRow(float scale, uint territoryId)
    {
        // Nutzeranforderung: im ToDo-Tab nur die Auto-Knöpfe zeigen, die für ToDo-Einträge auch
        // etwas zu tun haben, mit einer auf die ToDo-Liste bezogenen Anzahl statt der Zonen-Anzahl
        // (siehe Plugin.GetZoneAutomationButtons-Kommentar zum restrictToToDo-Parameter).
        var buttons = plugin.GetZoneAutomationButtons(territoryId, restrictToToDo: activeView == OverlayView.ToDo);
        if (buttons.Count == 0)
            return;

        var anyRunning = buttons.Any(b => b.IsActive);
        // Nutzeranforderung: Auto-Knöpfe während MSQ-Quest-Kämpfen (solo-instanzierte Bosskämpfe wie
        // bei "Containment Bay" o.ä.) deaktivieren - Questionable/die Automation soll dort nicht
        // hineinfunken. Condition[InCombat] deckt jeden Kampf ab, nicht nur MSQ-spezifische - bewusst
        // so (jeder Kampf ist ein Grund, die Automation nicht gerade jetzt starten zu lassen).
        var inCombat = Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        ImGui.Indent(14f * scale);

        var startX = ImGui.GetCursorPosX();
        var contentMaxX = ImGui.GetWindowContentRegionMax().X - 14f * scale;
        var buttonHeight = AutoButtonHeight * scale;
        var cursorX = startX;
        var cursorY = ImGui.GetCursorPosY();

        using (CodexTheme.FontAutoButtonLabel.Push())
        {
            foreach (var button in buttons)
            {
                var label = AutoButtonLabel(button.Key);
                var width = MeasureAutoButton(label, button.Count, scale);

                if (cursorX > startX && cursorX + width > contentMaxX)
                {
                    cursorX = startX;
                    cursorY += buttonHeight + AutoButtonGap * scale;
                }

                ImGui.SetCursorPos(new Vector2(cursorX, cursorY));
                DrawAutoButton(button, label, new Vector2(width, buttonHeight), anyRunning, scale, shadowActive, inCombat, overlayContentAlpha);

                cursorX += width + AutoButtonGap * scale;
            }
        }

        ImGui.SetCursorPos(new Vector2(startX, cursorY + buttonHeight));
        ImGui.Unindent(14f * scale);
    }

    internal static float MeasureAutoButton(string label, int count, float scale)
    {
        var labelWidth = ImGui.CalcTextSize(label).X;
        float countWidth;
        using (CodexTheme.FontAutoButtonCount.Push())
            countWidth = ImGui.CalcTextSize(count.ToString()).X;

        return (AutoButtonPaddingLeft + AutoButtonIconSize + AutoButtonIconGap + AutoButtonCountGap + AutoButtonPaddingRight) * scale
               + labelWidth + countWidth;
    }

    /// <summary>
    /// Zeichnet EINEN Auto-Knopf im passenden Zustand (Abschnitt 5.2-Tabelle: Bereit/Läuft/Gesperrt).
    /// Internal statt private und mit explizitem "shadow"-Parameter statt des Instanzfelds
    /// shadowActive: wird auch von CodexMenuWindow.DrawOverlayPreviewWindow (Dummy-Vorschau im neuen
    /// Einstellungsmenü) wiederverwendet, damit die Vorschau exakt wie das echte Overlay aussieht.
    /// </summary>
    internal static void DrawAutoButton(Plugin.ZoneAutomationButton button, string label, Vector2 size, bool anyRunning, float scale, bool shadow, bool inCombat = false, float contentAlpha = 1f)
    {
        var locked = (anyRunning && !button.IsActive) || (inCombat && !button.IsActive);

        Vector4 bg, border, fg, countFg, iconColor;
        if (button.IsActive)
        {
            bg = CodexTheme.Accent;
            border = CodexTheme.Accent;
            fg = CodexTheme.TextOnAccent;
            countFg = RunningCountFg;
            iconColor = CodexTheme.TextOnAccent;
        }
        else if (locked)
        {
            // Abschnitt 5.9: Knöpfe "ohne eigene Füllung" (hier: ganz ohne Hintergrund) bekommen ab
            // ~70% Transparenz einen leicht deckenden Hintergrund, sonst wären sie auf dem fast
            // unsichtbaren Fensterhintergrund kaum noch lesbar.
            bg = shadow ? CodexTheme.TranslucentButtonBg : new Vector4(0f, 0f, 0f, 0f);
            border = CodexTheme.LineDisabled;
            fg = CodexTheme.TextDisabled;
            countFg = CodexTheme.TextDisabled;
            iconColor = CodexTheme.TextDisabled;
        }
        else
        {
            bg = shadow ? CodexTheme.TranslucentButtonBg : CodexTheme.BgSelected;
            border = CodexTheme.LineControl;
            fg = CodexTheme.TextPrimary;
            countFg = CodexTheme.TextTertiary;
            iconColor = CodexTheme.Accent;
        }

        // Mit der Overlay-Deckkraft ausbleichen (Nutzeranforderung) - aber NUR die Füllung, nicht
        // Umriss/Text/Icon (Nutzervorgabe: die sollen auch bei 0% Opacity noch lesbar bleiben).
        bg = bg with { W = bg.W * contentAlpha };

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##CodexAuto_{button.Key}", size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), AutoButtonRounding * scale);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), AutoButtonRounding * scale);

        // Hover-Effekt (Nutzeranforderung) - dezente helle Überlagerung, nur bei tatsächlich
        // klickbaren Zuständen (Bereit/Läuft), nicht beim gesperrten Knopf ohne Wirkung.
        if (hovered && !locked)
            drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), AutoButtonRounding * scale);

        var contentCursor = cursor + new Vector2(AutoButtonPaddingLeft * scale, 0f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var icon = button.IsActive ? FontAwesomeIcon.Pause.ToIconString() : FontAwesomeIcon.Play.ToIconString();
            // Fest auf 9x9px (vor Skalierung) gezeichnet, statt der nativen (größeren) Glyphen-Größe
            // der Icon-Schrift - SetWindowFontScale wirkt auch auf den einfachen AddText-Aufruf unten
            // (beide lesen dieselbe effektive Schriftgröße), danach sofort wieder zurückgesetzt.
            var nativeIconPx = ImGui.GetFontSize();
            var desiredIconPx = AutoButtonIconSize * scale;
            ImGui.SetWindowFontScale(desiredIconPx / nativeIconPx);
            var iconSize = ImGui.CalcTextSize(icon);
            drawList.AddText(contentCursor + new Vector2(0f, (size.Y - iconSize.Y) / 2f), ImGui.GetColorU32(iconColor), icon);
            ImGui.SetWindowFontScale(1f);
            contentCursor.X += desiredIconPx + AutoButtonIconGap * scale;
        }

        var labelSize = ImGui.CalcTextSize(label);
        CodexTheme.DrawTextShadowed(drawList, contentCursor + new Vector2(0f, (size.Y - labelSize.Y) / 2f), fg, label, shadow);
        contentCursor.X += labelSize.X + AutoButtonCountGap * scale;

        // Zahl: gleiche Schriftgröße wie das Label, aber normale Stärke statt Bold (Nutzervorgabe).
        using (CodexTheme.FontAutoButtonCount.Push())
        {
            var countText = button.Count.ToString();
            var countSize = ImGui.CalcTextSize(countText);
            CodexTheme.DrawTextShadowed(drawList, contentCursor + new Vector2(0f, (size.Y - countSize.Y) / 2f), countFg, countText, shadow);
        }

        if (locked)
        {
            if (hovered)
            {
                ShowTooltip(inCombat
                    ? Loc.T("Im Kampf nicht verfügbar", "Not available during combat")
                    : Loc.T("Es läuft bereits eine andere Auto-Funktion", "Another auto function is already running"));
            }
            return;
        }

        if (clicked)
        {
            if (button.IsActive)
                button.Stop();
            else
                button.Start();
        }
    }

    /// <summary>
    /// Statustext der aktiven (oder gerade erst gestoppten, siehe ShouldShowStatusText) Automation,
    /// z.B. "Starte: Quest XY...", "Nicht unterstützt: Quest XY" oder "Keine Quests mehr übrig." -
    /// identisches Vorbild zu CompactOverlayWindow.DrawAutomationStatusTexts, das im neuen Overlay
    /// bisher komplett fehlte (Nutzer-Report: Klick auf Auto-Quest schien "nichts zu tun", dabei
    /// hatte Questionable die einzige verbleibende Quest nur abgelehnt/als nicht unterstützt
    /// markiert - ohne diesen Text war das nicht erkennbar, nur im Log sichtbar).
    /// </summary>
    private void DrawAutomationStatusText(float scale)
    {
        string? text = null;
        bool active = false;

        void Check(bool shouldShow, string statusText, bool isActive)
        {
            if (text != null || !shouldShow)
                return;
            text = statusText;
            active = isActive;
        }

        Check(plugin.QuestAutomation.ShouldShowStatusText, plugin.QuestAutomation.StatusText, plugin.QuestAutomation.IsActive);
        Check(plugin.AetheryteAutomation.ShouldShowStatusText, plugin.AetheryteAutomation.StatusText, plugin.AetheryteAutomation.IsActive);
        Check(plugin.HuntingLogAutomation.ShouldShowStatusText, plugin.HuntingLogAutomation.StatusText, plugin.HuntingLogAutomation.IsActive);
        Check(plugin.AetherCurrentAutomation.ShouldShowStatusText, plugin.AetherCurrentAutomation.StatusText, plugin.AetherCurrentAutomation.IsActive);
        Check(plugin.SightseeingAutomation.ShouldShowStatusText, plugin.SightseeingAutomation.StatusText, plugin.SightseeingAutomation.IsActive);
        Check(plugin.ChocobokeepAutomation.ShouldShowStatusText, plugin.ChocobokeepAutomation.StatusText, plugin.ChocobokeepAutomation.IsActive);
        Check(plugin.TripleTriadAutomation.ShouldShowStatusText, plugin.TripleTriadAutomation.StatusText, plugin.TripleTriadAutomation.IsActive);
        Check(plugin.NoFlyAreaExit.IsBusy, plugin.NoFlyAreaExit.StatusText, true);
        Check(plugin.ToDoCrossZoneTraveler.IsBusy, plugin.ToDoCrossZoneTraveler.StatusText, true);

        if (text == null)
            return;

        ImGui.Indent(14f * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        using (CodexTheme.FontOverlayStatusText.Push())
            CodexTheme.TextShadowed(text, active ? CodexTheme.Accent : CodexTheme.TextTertiary, shadowActive);
        ImGui.Unindent(14f * scale);
    }

    // ---- Abschnitt 5.4: Währungsübersicht ----

    /// <summary>
    /// Abschnitt 5.4 - "DEINE WÄHRUNGEN": 2-spaltiges Raster mit Wert+Name je noch benötigter
    /// Währung. Datenquelle (welche Währungen, woher die Bestände kommen) bleibt die bisherige
    /// Plugin-Logik (siehe Plugin.GetCurrencyAmount) - nur die Darstellung ist neu, bewusst OHNE den
    /// "Benötigte Währung"-Umschalt-Knopf und die Retainer-/Satteltaschen-Zusätze des alten Overlays,
    /// die die neue Spezifikation für diesen Abschnitt nicht vorsieht.
    /// </summary>
    private void DrawCurrencyWallet(float scale, uint territoryId)
    {
        var config = plugin.Configuration;
        if (!config.ShowCurrencyWallet)
            return;

        // Dieselbe Grundmenge wie die aktuell sichtbare Liste (siehe GetZoneOverlayItems/
        // GetToDoOverlayItems) - jeder Eintrag mit Hauptwährung UND etwaigen Zusatzwährungen
        // (CollectibleEntry.AdditionalCurrencies), auf die jeweilige Item-Id dedupliziert (der alte
        // Vorbild-Wortlaut in CompactOverlayWindow.DrawCurrencyWallet macht dasselbe). Im ToDo-Tab
        // (Nutzeranforderung) die Währungen der ToDo-Liste statt der aktuellen Zone zeigen.
        var items = activeView == OverlayView.ToDo ? plugin.GetToDoOverlayItems() : plugin.GetZoneOverlayItems(territoryId);
        var currencies = items
            .SelectMany(e => new[] { new CollectibleCurrency { Currency = e.Currency, CurrencyIconId = e.CurrencyIconId, CurrencyItemId = e.CurrencyItemId, CurrencyAmount = e.CurrencyAmount } }
                .Concat(e.AdditionalCurrencies ?? Enumerable.Empty<CollectibleCurrency>()))
            .Where(c => c.CurrencyItemId != 0)
            .GroupBy(c => c.CurrencyItemId)
            .Select(g => g.First())
            .ToList();

        if (currencies.Count == 0)
            return;

        ImGui.Dummy(new Vector2(0f, 5f * scale));
        ImGui.Indent(14f * scale);

        CodexTheme.SectionLabel(Loc.T("DEINE WÄHRUNGEN", "YOUR CURRENCIES"), shadowActive);
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));

        // Rechter Randabstand identisch zum linken Indent (14px) - ohne explizite Tabellenbreite nutzte
        // die Tabelle die volle verfügbare Breite bis zum Fensterrand, wodurch rechts außen (anders als
        // links) kein Randabstand blieb und Inhalt dort abgeschnitten wirkte (Nutzer-Report).
        var tableWidth = ImGui.GetContentRegionAvail().X - 14f * scale;
        if (ImGui.BeginTable("##CodexCurrencyGrid", 2, ImGuiTableFlags.None, new Vector2(tableWidth, 0f)))
        {
            ImGui.TableSetupColumn("##CodexCurrencyCol0", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##CodexCurrencyCol1", ImGuiTableColumnFlags.WidthStretch, 1f);

            for (var i = 0; i < currencies.Count; i++)
            {
                if (i % 2 == 0)
                {
                    ImGui.TableNextRow();
                    if (i > 0)
                        ImGui.Dummy(new Vector2(0f, 5f * scale - ImGui.GetStyle().CellPadding.Y));
                }

                ImGui.TableNextColumn();
                DrawCurrencyCell(currencies[i], culture, scale);
            }

            ImGui.EndTable();
        }

        ImGui.Unindent(14f * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
    }

    private void DrawCurrencyCell(CollectibleCurrency currency, CultureInfo culture, float scale)
    {
        var amount = plugin.GetCurrencyAmount(currency.CurrencyItemId);
        var name = GetCurrencyLabel(currency.Currency);
        var valueText = amount.ToString("N0", culture);
        var valueColor = amount == 0 ? CodexTheme.TextDim : CodexTheme.TextPrimary;

        ImGui.AlignTextToFramePadding();
        using (CodexTheme.FontCurrencyValue.Push())
            CodexTheme.TextShadowed(valueText, valueColor, shadowActive);

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

            CodexTheme.TextShadowed(displayName, CodexTheme.TextMuted, shadowActive);
            if (displayName != name && ImGui.IsItemHovered())
                ShowTooltip(name);
        }

        // Retainer-/Satteltaschen-Bestand als "(<Anzahl>)" dahinter (Nutzeranforderung, identisch zum
        // alten Overlay - siehe CompactOverlayWindow.DrawCurrencyWallet) - nur bei aktiviertem
        // Schalter, und nur wenn tatsächlich irgendwo etwas liegt (sonst entfällt die Klammer
        // komplett, statt "(0)" zu zeigen).
        if (!plugin.Configuration.ShowRetainerItemCounts)
            return;

        var retainerCounts = Plugin.GetRetainerItemCounts(currency.CurrencyItemId);
        var retainerTotal = retainerCounts.Count == 0 ? 0u : (uint)retainerCounts.Values.Sum(v => (long)v);
        var saddlebagCount = Plugin.GetSaddlebagItemCount(currency.CurrencyItemId);
        var combinedTotal = retainerTotal + saddlebagCount;
        if (combinedTotal == 0)
            return;

        ImGui.SameLine(0f, 2f * scale);
        using (CodexTheme.FontCurrencyName.Push())
            CodexTheme.TextShadowed($"({combinedTotal.ToString("N0", culture)})", CodexTheme.TextDim, shadowActive);

        if (!ImGui.IsItemHovered())
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f * scale, 2f * scale));
        ImGui.BeginTooltip();
        foreach (var kv in retainerCounts.OrderByDescending(kv => kv.Value))
            ImGui.TextUnformatted($"{kv.Key}: {kv.Value.ToString("N0", culture)}");
        if (saddlebagCount > 0)
        {
            if (retainerCounts.Count > 0)
                ImGui.Separator();
            ImGui.TextUnformatted($"{Loc.T("Satteltasche", "Saddlebag")}: {saddlebagCount.ToString("N0", culture)}");
        }

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Wie ImGui.SetTooltip, aber mit 2px Innenabstand zu allen Rändern statt des deutlich größeren
    /// ImGui-Standard-WindowPadding (Nutzeranforderung, gilt jetzt einheitlich für alle einfachen
    /// Tooltips im neuen Overlay - der Schloss-Tooltip in DrawGateLockButton hatte das bereits).
    /// </summary>
    private static void ShowTooltip(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f * scale, 2f * scale));
        ImGui.SetTooltip(text);
        ImGui.PopStyleVar();
    }

    // ---- Abschnitt 5.5: Reiter und Filter ----

    private enum OverlayView
    {
        Zone,
        ToDo,
    }

    private OverlayView activeView = OverlayView.Zone;
    private string? openFilterPopup;

    // Bleibt über mehrere Frames erhalten (Popup öffnen, tippen, wieder schließen) - wird beim
    // erneuten Öffnen bewusst NICHT zurückgesetzt (gleiches Vorbild wie CompactOverlayWindow.
    // currencyFilterSearch).
    private string currencyFilterSearch = string.Empty;

    private List<CollectibleEntry> CurrentRawItems(uint territoryId) =>
        activeView == OverlayView.Zone ? plugin.GetZoneOverlayItems(territoryId) : plugin.GetToDoOverlayItems();

    /// <summary>Abschnitt 5.5 - Links die beiden Reiter (Zone/ToDo-Liste) mit Anzahl, rechts die Filter-Knöpfe.</summary>
    private void DrawTabsAndFilters(float scale, uint territoryId)
    {
        // 8px Abstand zur Währungsübersicht darüber (Abschnitt 5.4: "Darunter folgt die Reiter-Zeile
        // mit 8px Abstand") - on top of deren eigenem 4px Innenabstand unten.
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.Indent(14f * scale);

        var zoneCount = plugin.GetZoneOverlayItems(territoryId).Count;
        var todoCount = plugin.GetToDoOverlayItems().Count;

        if (DrawTab("zone", Loc.T("Zone", "Zone"), zoneCount, activeView == OverlayView.Zone, scale, shadowActive, overlayContentAlpha))
            activeView = OverlayView.Zone;
        ImGui.SameLine(0f, 18f * scale);
        if (DrawTab("todo", Loc.T("ToDo-Liste", "To-do list"), todoCount, activeView == OverlayView.ToDo, scale, shadowActive, overlayContentAlpha))
            activeView = OverlayView.ToDo;

        var config = plugin.Configuration;
        var rawItems = CurrentRawItems(territoryId);
        var typesInList = rawItems.Select(e => e.Type).Distinct().OrderBy(Loc.TypeName).ToList();
        var typeActive = typesInList.Any(t => !config.ShowType.GetValueOrDefault(t, true));

        // Nur der Währungs-NAME (per CompactOverlayWindow.GetAllCurrencies, dieselbe Kanonisierung
        // wie im alten Overlay) statt des vollen Preis+Name-Strings, und dedupliziert - vorher stand
        // hier jeder volle Preistext (z.B. "500,000 Gil") als eigener, unkanonisierter Checkbox-
        // Eintrag, wodurch dieselbe Währung in unterschiedlichen Beträgen mehrfach auftauchte
        // (Nutzer-Report "keine Duplikate"). Die geteilte Kanonisierung sorgt außerdem dafür, dass
        // Configuration.HiddenCurrencies zwischen altem und neuem Overlay konsistent bleibt. Die
        // IconId wird mitgeführt, um im Filter-Popup das Währungssymbol hinter dem Namen zu zeigen
        // (Nutzeranforderung) - pro Label bevorzugt ein Vertreter MIT bekanntem Icon, da mehrere
        // Einträge derselben Währung unterschiedlich vollständige Icon-Daten haben können (gleiches
        // Vorbild wie CompactOverlayWindow.DrawCurrencyFilterPopupContent).
        var currenciesInList = rawItems
            .Where(e => config.ShowType.GetValueOrDefault(e.Type, true))
            .SelectMany(GetAllCurrencies)
            .Where(c => !string.IsNullOrEmpty(c.Label))
            .GroupBy(c => c.Label)
            .Select(g => (Label: g.Key, IconId: g.Select(c => c.IconId).FirstOrDefault(id => id != 0)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currencyActive = currenciesInList.Any(c => config.HiddenCurrencies.Contains(c.Label));

        var typeLabel = Loc.T("Typen", "Types");
        var currencyLabel = Loc.T("Währungen", "Currencies");

        // WICHTIG: Label-Breite mit der Schrift messen, mit der es auch gezeichnet wird (siehe
        // DrawFilterButton - FontTabRow, NICHT die Icon-Schrift) - nur der Pfeil-Glyph selbst braucht
        // die Icon-Schrift. Beides in DERSELBEN Messung zu mischen lieferte vorher falsche (viel zu
        // kleine/große) Breiten und dadurch überlappende Knöpfe (Nutzer-Report).
        float caretWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            caretWidth = ImGui.CalcTextSize(FontAwesomeIcon.CaretDown.ToIconString()).X;

        float typeLabelWidth, currencyLabelWidth;
        using (CodexTheme.FontFilterButton.Push())
        {
            typeLabelWidth = ImGui.CalcTextSize(typeLabel).X;
            currencyLabelWidth = ImGui.CalcTextSize(currencyLabel).X;
        }

        // Innenabstand 1px x 6px (Nutzerspezifikation) - 6px links vor dem Label, 6px zwischen Label
        // und Pfeil, 6px rechts nach dem Pfeil. +3px zusätzliche Breite (Nutzer-Report: Label und
        // Pfeil überlappten sich bei aktivem Filter, wo zusätzlich die Diamant-Markierung Platz
        // braucht, siehe DrawFilterButton).
        var typeWidth = typeLabelWidth + caretWidth + 18f * scale + 3f * scale;
        var currencyWidth = currencyLabelWidth + caretWidth + 18f * scale + 3f * scale;

        // Rechtsbündig ab dem tatsächlichen rechten Fensterrand (Abschnitt 5.5), mit 10px rechtem
        // Innenabstand der Reiter-Zeile (Nutzerspezifikation), damit "Currencies" nicht am
        // Fensterrand klebt - bewusst NICHT über GetContentRegionAvail()+GetCursorPosX() vom
        // aktuellen (variablen) Cursor aus berechnet, das ist fehleranfälliger und war Teil
        // desselben Überlappungs-Bugs.
        var rightEdge = ImGui.GetWindowContentRegionMax().X - 10f * scale;
        ImGui.SameLine(rightEdge - typeWidth - currencyWidth - 6f * scale);
        var (typeButtonMin, typeButtonSize, typeClicked) = DrawFilterButton("type", typeLabel, typeActive, typeWidth, scale, shadowActive, overlayContentAlpha);
        if (typeClicked)
            openFilterPopup = openFilterPopup == "type" ? null : "type";
        ImGui.SameLine(0f, 6f * scale);
        var (currencyButtonMin, currencyButtonSize, currencyClicked) = DrawFilterButton("cur", currencyLabel, currencyActive, currencyWidth, scale, shadowActive, overlayContentAlpha);
        if (currencyClicked)
            openFilterPopup = openFilterPopup == "cur" ? null : "cur";

        if (openFilterPopup == "type")
            DrawTypeFilterPopup(config, typesInList, (typeButtonMin, typeButtonSize));
        else if (openFilterPopup == "cur")
            DrawCurrencyFilterPopup(config, currenciesInList, (currencyButtonMin, currencyButtonSize));

        ImGui.Unindent(14f * scale);
        if (!shadowActive)
            ImGui.Separator();
    }

    /// <summary>
    /// Internal statt private, "selected" als Parameter statt des Vergleichs mit dem Instanzfeld
    /// activeView: wird auch von CodexMenuWindow.DrawOverlayPreviewWindow wiederverwendet. Gibt true
    /// zurück, wenn der Reiter gerade angeklickt wurde - der Aufrufer entscheidet selbst, was das
    /// bedeutet (im echten Overlay: activeView umschalten, in der Vorschau: nichts).
    /// </summary>
    internal static bool DrawTab(string id, string label, int count, bool selected, float scale, bool shadow, float contentAlpha = 1f)
    {
        var countText = count.ToString();

        float labelWidth, labelHeight, countWidth, countHeight;
        using (CodexTheme.FontTabLabel.Push())
        {
            labelWidth = ImGui.CalcTextSize(label + " ").X;
            labelHeight = ImGui.GetFontSize();
        }
        using (CodexTheme.FontTabRow.Push())
        {
            countWidth = ImGui.CalcTextSize(countText).X;
            countHeight = ImGui.GetFontSize();
        }

        var totalWidth = labelWidth + countWidth;
        var size = new Vector2(totalWidth + 4f * scale, MathF.Max(labelHeight, ImGui.GetFrameHeight()));
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##CodexTab_{id}", size);

        // Kein Hintergrund hier, nur Text/Linie - bleibt unabhängig von contentAlpha immer voll
        // sichtbar (Nutzervorgabe: nur Füllungen sollen mit der Opacity ausbleichen).
        var drawList = ImGui.GetWindowDrawList();
        var labelColor = selected ? CodexTheme.TextHeading : CodexTheme.TextTertiary;
        var countColor = selected ? CodexTheme.Accent : CodexTheme.TextDim;
        using (CodexTheme.FontTabLabel.Push())
            CodexTheme.DrawTextShadowed(drawList, cursor, labelColor, label + " ", shadow);
        // Auf der Grundlinie des (größeren) Label-Texts ausgerichtet, statt oben - sonst wirkt die
        // kleinere Zahl bei unterschiedlichen Schriftgrößen visuell zu hoch.
        using (CodexTheme.FontTabRow.Push())
            CodexTheme.DrawTextShadowed(drawList, cursor + new Vector2(labelWidth, labelHeight - countHeight), countColor, countText, shadow);

        if (selected)
        {
            var lineY = cursor.Y + size.Y - 1f;
            drawList.AddLine(new Vector2(cursor.X, lineY), new Vector2(cursor.X + totalWidth, lineY), ImGui.GetColorU32(CodexTheme.Accent), 2f);
        }

        return clicked;
    }

    /// <summary>
    /// Internal statt private, ohne Zugriff auf das Instanzfeld openFilterPopup (der Aufrufer
    /// entscheidet selbst anhand von "Clicked", was ein Klick bedeutet) - wird auch von
    /// CodexMenuWindow.DrawOverlayPreviewWindow wiederverwendet, damit die Vorschau exakt dieselben
    /// Filter-Knöpfe wie das echte Overlay zeigt.
    /// </summary>
    internal static (Vector2 Min, Vector2 Size, bool Clicked) DrawFilterButton(string key, string label, bool active, float width, float scale, bool shadow, float contentAlpha = 1f)
    {
        float labelHeight;
        using (CodexTheme.FontFilterButton.Push())
            labelHeight = ImGui.GetFontSize();

        var paddingY = 1f * scale;
        // +3px Höhe (Nutzerspezifikation), zusätzlich zum regulären 1px-Innenabstand oben/unten.
        var size = new Vector2(width, labelHeight + paddingY * 2f + 3f * scale);
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##CodexFilter_{key}", size);

        // Nutzerspezifikation: 1px Rahmen #5A4A33 (CodexTheme.LineControl) IMMER, keine Füllung ohne
        // aktiven Filter - nur bei aktivem Filter eine Füllung (BgSelectedStrong). Der Popup-"offen"-
        // Zustand bekommt bewusst keine eigene Hervorhebung mehr (vorher Accent-Rahmen/BgSelected).
        // Abschnitt 5.9: ab ~70% Overlay-Transparenz bekommt der sonst leere Hintergrund einen leicht
        // deckenden Ton, damit der Knopf auf dem fast unsichtbaren Fenster lesbar bleibt.
        var bg = active ? CodexTheme.BgSelectedStrong : shadow ? CodexTheme.TranslucentButtonBg : new Vector4(0f, 0f, 0f, 0f);
        var fg = active ? CodexTheme.TextHeading : CodexTheme.TextPrimary;
        // Nur die Füllung blasst mit der Opacity aus, nicht Rahmen/Text/Diamant (Nutzervorgabe: die
        // sollen auch bei 0% Opacity noch zu sehen sein).
        bg = bg with { W = bg.W * contentAlpha };

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(CodexTheme.LineControl), CodexTheme.RoundingControl);

        var contentCursor = cursor + new Vector2(6f * scale, (size.Y - labelHeight) / 2f);
        if (active)
        {
            var diamond = 6f * scale;
            var diamondCenter = contentCursor + new Vector2(diamond / 2f, labelHeight / 2f);
            drawList.AddCircleFilled(diamondCenter, diamond / 2f, ImGui.GetColorU32(CodexTheme.Accent), 4);
            contentCursor.X += diamond + 4f * scale;
        }

        using (CodexTheme.FontFilterButton.Push())
            CodexTheme.DrawTextShadowed(drawList, contentCursor, fg, label, shadow);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var caret = FontAwesomeIcon.CaretDown.ToIconString();
            var caretSize = ImGui.CalcTextSize(caret);
            drawList.AddText(cursor + new Vector2(size.X - caretSize.X - 6f * scale, (size.Y - caretSize.Y) / 2f), ImGui.GetColorU32(fg), caret);
        }

        return (cursor, size, clicked);
    }

    /// <summary>Dynamisch aus den Typen der aktuellen Liste aufgebaut (Nutzeranforderung "alle Items integrieren") - analog zum Währungsfilter, statt der vorher fest auf Reittier/Begleiter beschränkten Checkboxen.</summary>
    private void DrawTypeFilterPopup(Configuration config, List<CollectibleType> types, (Vector2 Min, Vector2 Size) anchor)
    {
        ImGui.SetNextWindowPos(anchor.Min + new Vector2(0f, anchor.Size.Y + 2f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, CodexTheme.BgPopup);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineFrame);
        if (ImGui.Begin("##CodexTypeFilterPopup", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize))
        {
            foreach (var type in types)
                DrawTypeCheckbox(config, type, Loc.TypeName(type));

            CloseFilterPopupOnOutsideClick();
        }
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// Schließt das gerade offene Filter-Popup bei einem Linksklick AUSSERHALB seines tatsächlichen
    /// Fenster-Rechtecks - per Mausposition statt (wie vorher) ImGui.IsWindowFocused() geprüft. Beim
    /// Währungsfilter (mit Suchfeld, daher deutlich breiter/höher als der Typfilter) blieb das Popup
    /// sonst offen, wenn man auf eine scheinbar leere Stelle klickte, die aber noch innerhalb des
    /// (durch die Suchleiste breiteren) Popup-Fensters lag, da ein Klick INNERHALB des eigenen
    /// Fensters den Fokus nicht entzieht (Nutzer-Report: "geht nicht zu, wenn ich in die leere klicke").
    /// Muss noch VOR ImGui.End() aufgerufen werden, solange das Popup-Fenster das aktuelle ist.
    /// </summary>
    private void CloseFilterPopupOnOutsideClick()
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var mouse = ImGui.GetMousePos();
        if (mouse.X < min.X || mouse.X > max.X || mouse.Y < min.Y || mouse.Y > max.Y)
            openFilterPopup = null;
    }

    private void DrawTypeCheckbox(Configuration config, CollectibleType type, string label)
    {
        var shown = config.ShowType.GetValueOrDefault(type, true);
        if (ImGui.Checkbox(label, ref shown))
        {
            config.ShowType[type] = shown;
            config.Save();
        }
    }

    private void DrawCurrencyFilterPopup(Configuration config, List<(string Label, uint IconId)> currencies, (Vector2 Min, Vector2 Size) anchor)
    {
        ImGui.SetNextWindowPos(anchor.Min + new Vector2(0f, anchor.Size.Y + 2f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, CodexTheme.BgPopup);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.LineFrame);
        if (ImGui.Begin("##CodexCurrencyFilterPopup", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint("##CodexCurrencyFilterSearch", Loc.T("Suchen...", "Search..."), ref currencyFilterSearch, 64);

            var filtered = currencies
                .Where(c => string.IsNullOrEmpty(currencyFilterSearch) || c.Label.Contains(currencyFilterSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var iconSize = ImGui.GetTextLineHeight();
            foreach (var (label, iconId) in filtered)
            {
                var shown = !config.HiddenCurrencies.Contains(label);
                if (ImGui.Checkbox(label, ref shown))
                {
                    if (shown)
                        config.HiddenCurrencies.Remove(label);
                    else
                        config.HiddenCurrencies.Add(label);
                    config.Save();
                }

                // Währungssymbol HINTER dem Namen (Nutzeranforderung) - anders als im alten Overlay,
                // das es vor dem Namen zeigt.
                if (iconId != 0)
                {
                    ImGui.SameLine();
                    var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                    ImGui.Image(icon.Handle, new Vector2(iconSize));
                }
            }

            if (filtered.Count == 0)
                ImGui.TextColored(CodexTheme.TextTertiary, Loc.T("Keine Treffer.", "No matches."));

            CloseFilterPopupOnOutsideClick();
        }
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    // ---- Abschnitt 5.6: Item-Liste ----

    /// <summary>
    /// Hintergrund-/Textfarbe des Typ-Etiketts (Abschnitt 2 "Typ-Etiketten" definiert nur Reittier/
    /// Begleiter als eigenes Bg/Fg-Paar). Für alle übrigen Typen (Nutzeranforderung "alle Items
    /// integrieren") wird ein zur bereits im alten Overlay gepflegten Typfarbe
    /// (CompactOverlayWindow.TypeColors, bleibt unverändert die Textfarbe) passender, abgedunkelter
    /// Hintergrund abgeleitet - Nutzeranforderung: jedes Etikett soll wie bei Reittier/Begleiter einen
    /// zur Farbe passenden eigenen Hintergrund bekommen, statt sich alle außer diesen beiden einen
    /// einzigen generischen dunklen Hintergrund zu teilen. Der Skalierungsfaktor (~22%) entspricht in
    /// etwa dem Verhältnis von MinionBadgeBg zu dessen TypeColors-Wert.
    /// </summary>
    internal static (Vector4 Bg, Vector4 Fg) GetTypeBadgeColors(CollectibleType type)
    {
        if (type == CollectibleType.Mount)
            return (CodexTheme.MountBadgeBg, CodexTheme.MountBadgeFg);
        if (type == CollectibleType.Minion)
            return (CodexTheme.MinionBadgeBg, CodexTheme.MinionBadgeFg);

        var fg = TypeColors.GetValueOrDefault(type, CodexTheme.TextSecondary);
        var bg = new Vector4(fg.X * 0.22f, fg.Y * 0.22f, fg.Z * 0.22f, 1f);
        return (bg, fg);
    }

    // ---- Währungs-/Typfarben-Hilfsmittel (Nutzeranforderung: altes Overlay vollständig entfernt -
    // diese Mitglieder lebten vorher in Windows.CompactOverlayWindow, werden aber auch hier
    // gebraucht, daher hierher verschoben statt dupliziert).

    internal static readonly Dictionary<CollectibleType, Vector4> TypeColors = new()
    {
        [CollectibleType.Mount] = new(0.85f, 0.45f, 0.05f, 1f),
        [CollectibleType.Minion] = new(0.75f, 0.6f, 1f, 1f),
        [CollectibleType.Orchestrion] = new(0.4f, 0.9f, 0.85f, 1f),
        [CollectibleType.Barding] = new(0.85f, 0.65f, 0.45f, 1f),
        [CollectibleType.Emote] = new(1f, 0.55f, 0.75f, 1f),
        [CollectibleType.Facewear] = new(0.55f, 0.75f, 1f, 1f),
        [CollectibleType.FashionAccessory] = new(0.75f, 0.9f, 0.45f, 1f),
        [CollectibleType.TripleTriadCard] = new(0.25f, 0.6f, 1f, 1f),
        [CollectibleType.FrameKit] = new(0.55f, 0.55f, 0.95f, 1f),
        [CollectibleType.Hairstyle] = new(0.9f, 0.7f, 0.9f, 1f),
        [CollectibleType.Aetheryte] = new(0.6f, 1f, 0.75f, 1f),
        [CollectibleType.Quest] = new(1f, 0.9f, 0.5f, 1f),
        [CollectibleType.HuntingLog] = new(0.68f, 0.45f, 0.95f, 1f),
        [CollectibleType.AetherCurrent] = new(0.65f, 0.95f, 1f, 1f),
        [CollectibleType.Sightseeing] = new(0.3f, 0.8f, 0.45f, 1f),
        [CollectibleType.Chocobokeep] = new(0.95f, 0.85f, 0.2f, 1f),
        [CollectibleType.Achievement] = new(0.95f, 0.6f, 0.3f, 1f),
    };

    // Typen, deren Name tatsächlich einem echten Item-Sheet-Eintrag entspricht, den Allagan Tools'
    // "/moreinfo"-Befehl (siehe Plugin.OpenAllaganToolsItemInfo) per Namenssuche finden kann - für
    // SHIFT + Linksklick. Quest/Sightseeing/Aetheryte/HuntingLog/AetherCurrent/Chocobokeep sind keine
    // Items. FrameKit/Hairstyle tragen zwar nur den Namen des Rahmens/der Frisur, werden aber über
    // Plugin.ResolveUnlockItemId auf das freischaltende Item (Framer's Kit bzw. "Modern Aesthetics"-
    // Buch) aufgelöst.
    internal static readonly HashSet<CollectibleType> AllaganToolsEligibleTypes = new()
    {
        CollectibleType.Mount,
        CollectibleType.Minion,
        CollectibleType.Orchestrion,
        CollectibleType.Barding,
        CollectibleType.Emote,
        CollectibleType.Facewear,
        CollectibleType.FashionAccessory,
        CollectibleType.TripleTriadCard,
        CollectibleType.FrameKit,
        CollectibleType.Hairstyle,
    };

    internal static string GetCurrencyLabel(string currencyText)
    {
        var t = Regex.Replace(currencyText, @"^\s*[\d,]+\s*", "");
        t = Regex.Replace(t, @"\s*\([^)]*\)\s*$", "");
        return t.Trim();
    }

    /// <summary>Triple-Triad-NPC-Kampf (siehe Plugin.GetTripleTriadNpcEntries) - Currency enthält dort den Gegner-Namen statt eines Preises.</summary>
    private static bool IsTripleTriadNpcFight(CollectibleEntry entry) =>
        entry.Category == Plugin.TripleTriadNpcCategory && entry.CurrencyItemId == 0 && !string.IsNullOrEmpty(entry.Currency);

    // Manche Roh-Quelldaten schreiben dieselbe Währung uneinheitlich mal im Singular, mal im Plural
    // (z.B. "Bicolor Gemstone" vs. "Bicolor Gemstones") - ohne Abgleich taucht sie im "Currencys
    // filtern"-Popup fälschlich zweimal auf UND ein Ausblenden über die eine Schreibweise würde
    // Einträge mit der jeweils anderen gar nicht erfassen (siehe CanonicalizeCurrencyLabel). Reiner
    // Vergleichsschlüssel (nicht die Anzeige) - entfernt ein einzelnes anhängendes "s" (aber nicht
    // "ss", z.B. bei "Skybuilders' Scrips" oder generell Wörtern, die schon auf "ss" enden).
    private static string NormalizeCurrencyLabelKey(string label)
    {
        var lower = label.ToLowerInvariant();
        return lower.Length > 1 && lower.EndsWith('s') && !lower.EndsWith("ss") ? lower[..^1] : lower;
    }

    // Je Vergleichsschlüssel (siehe NormalizeCurrencyLabelKey) DIE Schreibweise, die unter allen
    // bekannten Einträgen am häufigsten vorkommt (bei Gleichstand die kürzere, meist die
    // Singular-Form) - einmalig aus der kompletten Sammlung aufgebaut, da Spielinhalte sich zur
    // Laufzeit nicht ändern.
    private static Dictionary<string, string>? currencyLabelCanonicalCache;

    private static string CanonicalizeCurrencyLabel(string rawLabel)
    {
        if (string.IsNullOrEmpty(rawLabel))
            return rawLabel;

        currencyLabelCanonicalCache ??= CollectionData.GetAllEntries()
            .SelectMany(GetRawCurrencyLabels)
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(NormalizeCurrencyLabelKey)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(l => l, StringComparer.Ordinal)
                    .OrderByDescending(gg => gg.Count())
                    .ThenBy(gg => gg.Key.Length)
                    .First().Key);

        var key = NormalizeCurrencyLabelKey(rawLabel);
        return currencyLabelCanonicalCache.TryGetValue(key, out var canonical) ? canonical : rawLabel;
    }

    private static IEnumerable<string> GetRawCurrencyLabels(CollectibleEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Currency))
            yield return GetCurrencyLabel(entry.Currency);

        if (entry.AdditionalCurrencies != null)
        {
            foreach (var additional in entry.AdditionalCurrencies)
            {
                if (!string.IsNullOrEmpty(additional.Currency))
                    yield return GetCurrencyLabel(additional.Currency);
            }
        }
    }

    internal static IEnumerable<(string Label, uint IconId)> GetAllCurrencies(CollectibleEntry entry)
    {
        // Triple-Triad-NPC-Kämpfe tragen statt eines Preises den Gegner-Namen im Currency-Feld (siehe
        // Plugin.GetTripleTriadNpcEntries) - für den Currency-Filter alle zu EINEM Eintrag
        // zusammenfassen, statt jeden der über 100 Gegner einzeln aufzulisten.
        if (IsTripleTriadNpcFight(entry))
        {
            yield return (Loc.T("NPC-Kampf", "NPC Fight"), 0);
            yield break;
        }

        if (!string.IsNullOrEmpty(entry.Currency))
            yield return (CanonicalizeCurrencyLabel(GetCurrencyLabel(entry.Currency)), entry.CurrencyIconId);

        if (entry.AdditionalCurrencies != null)
        {
            foreach (var additional in entry.AdditionalCurrencies)
            {
                if (!string.IsNullOrEmpty(additional.Currency))
                    yield return (CanonicalizeCurrencyLabel(GetCurrencyLabel(additional.Currency)), additional.CurrencyIconId);
            }
        }
    }

    internal static IEnumerable<string> GetAllCurrencyLabels(CollectibleEntry entry) => GetAllCurrencies(entry).Select(c => c.Label);

    /// <summary>
    /// Kurzform für das Typ-Etikett in der Item-Liste (Nutzeranforderung) - NUR für dieses Etikett,
    /// nicht für Loc.TypeName allgemein (Filter-Popups, altes Overlay, Menü zeigen weiterhin den
    /// vollen Namen).
    /// </summary>
    internal static string GetBadgeLabel(CollectibleType type) => type switch
    {
        CollectibleType.Orchestrion => Loc.T("Orchestrion", "Orchestrion"),
        CollectibleType.TripleTriadCard => Loc.T("TT-Karte", "TT Card"),
        CollectibleType.Hairstyle => Loc.T("Ästhetik", "Aesthetics"),
        _ => Loc.TypeName(type),
    };

    /// <summary>Abschnitt 5.6 - Scrollbereich, Zeile: Typ-Etikett, Name, Preis+Währung, Karten-Knopf.</summary>
    private void DrawList(float scale, uint territoryId)
    {
        var config = plugin.Configuration;
        var list = CurrentRawItems(territoryId)
            .Where(e => config.ShowType.GetValueOrDefault(e.Type, true))
            .Where(e => !GetAllCurrencyLabels(e).Any(config.HiddenCurrencies.Contains))
            .ToList();

        ImGui.Dummy(new Vector2(0f, 2f * scale));

        if (list.Count == 0)
        {
            ImGui.Indent(14f * scale);
            using (CodexTheme.FontSubtitleItalic.Push())
            {
                ImGui.TextColored(CodexTheme.TextTertiary,
                    activeView == OverlayView.ToDo
                        ? Loc.T("Noch nichts auf deiner Liste.", "Nothing on your list yet.")
                        : Loc.T("Nichts offen in dieser Zone.", "Nothing left in this zone."));
            }
            ImGui.Unindent(14f * scale);
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            return;
        }

        ImGui.BeginChild("##CodexOverlayList", new Vector2(0f, 290f * scale), false);
        // Etwas Luft ÜBER der ersten Zeile (Nutzer-Report: Schloss-Symbol in der ersten Zeile oben
        // etwas abgeschnitten) - das Schloss-Icon (22px hoch) reicht bei Zeile 0 sonst bis an den
        // oberen Rand des Scroll-Childs, dessen Clip-Rect exakt dort beginnt.
        ImGui.Dummy(new Vector2(0f, 3f * scale));
        ImGui.Indent(8f * scale);

        // Zeilenhöhe (Nutzeranforderung, Display-Karte im neuen Menü) - die Badge-Höhe (fester
        // Schriftbestandteil, immer gleich groß) definiert die MINDESTENS sichtbare Zeilenhöhe; der
        // Rest von config.OverlayRowHeight wird als Innenabstand oben/unten um diesen Inhalt verteilt,
        // sodass sich die Gesamthöhe einer Zeile (Abstand + Inhalt + Abstand) tatsächlich sichtbar
        // ändert, statt nur der (vorher sehr kleinen, kaum wahrnehmbaren) Trennlinien-Lücke.
        float rowContentHeight;
        using (CodexTheme.FontTypeBadge.Push())
            rowContentHeight = ImGui.GetFontSize() + 4f * scale;
        var rowGap = MathF.Max(0f, (config.OverlayRowHeight * scale - rowContentHeight) / 2f);

        for (var itemIndex = 0; itemIndex < list.Count; itemIndex++)
        {
            var item = list[itemIndex];

            if (itemIndex == 0)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 8f * scale);
            else
                ImGui.Dummy(new Vector2(0f, rowGap));
            if (itemIndex > 0 && !shadowActive)
                ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, rowGap));

            // Abschnitt 5.7 - Voraussetzung nicht erfüllt (z.B. Errungenschaft/Rang fehlt): Etikett
            // abgedunkelt, Name und Preis gedämpft, zusätzliches Schloss-Symbol mit Tooltip. Nur
            // sichtbar, wenn Configuration.ShowAllItems aktiviert ist - sonst tauchen diese Einträge
            // laut Plugin.GetZoneOverlayItems erst gar nicht in der Liste auf (siehe dessen Kommentar).
            // Zusätzlich (Nutzeranforderung): Quests, die Questionable selbst als nicht unterstützt
            // meldet (siehe QuestAutomation.IsKnownUnsupported) - die stehen weiterhin in der Liste
            // (seit der Entfernung des entsprechenden Filters aus ComputeZoneOverlayItems), bekommen
            // aber dasselbe Schloss-Symbol, nur mit eigenem Hinweistext statt des generischen
            // Achievement-/Rang-Grundes.
            var isQuestUnsupportedByQuestionable = item.Type == CollectibleType.Quest && plugin.QuestAutomation.IsKnownUnsupported(item.Id);
            // Wie oben bei Quests, nur für Ätherströmungen mit bekanntermaßen nicht automatisierbarer
            // Handroute (siehe AetherCurrentAutomation.KnownUnsupportedIds-Kommentar, z.B. "The Ruby
            // Sea #1" - der Tauchgang ab der Wasseroberfläche).
            var isAetherCurrentUnsupported = item.Type == CollectibleType.AetherCurrent && AetherCurrentAutomation.IsKnownUnsupported(item.Id);
            var isGated = Plugin.IsAchievementOrRankGated(item) || isQuestUnsupportedByQuestionable || isAetherCurrentUnsupported;

            var (badgeBg, badgeFg) = GetTypeBadgeColors(item.Type);
            if (isGated)
            {
                badgeBg = badgeBg with { W = badgeBg.W * 0.55f };
                badgeFg = badgeFg with { W = badgeFg.W * 0.55f };
            }
            // Nutzervorgabe: nur die Füllung blasst mit der Overlay-Deckkraft aus, der Beschriftungstext
            // bleibt auch bei 0% Opacity lesbar.
            badgeBg = badgeBg with { W = badgeBg.W * overlayContentAlpha };

            var badgeLabel = GetBadgeLabel(item.Type);

            // 1. Typ-Etikett - auf 90px statt der im Entwurf für "Reittier"/"Begleiter" bemessenen
            // 62px verbreitert (Nutzeranforderung "alle Items integrieren" - andere Typnamen wie
            // "Ätherströmung" oder "Triple-Triad-Karte" sind deutlich länger).
            float badgeFontHeight;
            using (CodexTheme.FontTypeBadge.Push())
                badgeFontHeight = ImGui.GetFontSize();
            var badgeSize = new Vector2(90f * scale, badgeFontHeight + 4f * scale);
            var badgeCursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(badgeBg), 3f);
            ImGui.SetCursorScreenPos(badgeCursor);
            CenteredText(badgeLabel, badgeSize, badgeFg, shadowActive);

            // 2. Name - kein eigenes Karten-Icon mehr (Nutzeranforderung), stattdessen wie im alten
            // Overlay (siehe CompactOverlayWindow.DrawClickableName) direkt klickbar: Linksklick öffnet
            // bei vorhandenem Kartenziel die Karte, Namensfarbe wird dann golden (Accent) statt der
            // normalen Textfarbe, Hover zeigt Handcursor + Vendor-Tooltip. Bei nicht erfüllter
            // Voraussetzung (Abschnitt 5.7) immer TextMuted, unabhängig vom Kartenziel. Errungenschaften
            // haben kein Kartenziel, bekommen aber denselben Linksklick-Effekt wie im alten Overlay:
            // Klick öffnet das native Achievement-Fenster mit vorausgefülltem Suchfeld (siehe
            // Plugin.OpenAchievementWindow) statt einer Karte.
            ImGui.SameLine(0f, 8f * scale);
            ImGui.AlignTextToFramePadding();
            var hasGoToTarget = item.HasGoToTarget;
            var isAchievementLink = item.Type == CollectibleType.Achievement;
            var isLinked = hasGoToTarget || isAchievementLink;
            var nameColor = isGated ? CodexTheme.TextMuted : isLinked ? CodexTheme.Accent : CodexTheme.TextPrimary;
            using (CodexTheme.FontBodyBold.Push())
                CodexTheme.TextShadowed(item.Name, nameColor, shadowActive);
            // Strg+Shift+Klick auf den Namen setzt den Eintrag direkt auf die Blacklist (Nutzeranforderung
            // für die neue Blacklist-Seite, die dieses Tastenkürzel in einer Hinweis-Karte bewirbt - gab
            // es vorher nirgends im Code, weder hier noch im alten Overlay, nur den Rechtsklick-Menüpunkt
            // unten in DrawEntryContextMenu). Hat Vorrang vor dem normalen Linksklick (Karte/Achievement).
            var isBlacklistShortcut = ImGui.IsItemClicked() && ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
            if (isBlacklistShortcut)
                Plugin.AddToBlacklist(item);

            if (hasGoToTarget)
            {
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (!string.IsNullOrEmpty(item.Vendor))
                        ShowTooltip(Loc.T($"Bei {item.Vendor}", $"From {item.Vendor}"));
                }

                if (ImGui.IsItemClicked() && !isBlacklistShortcut)
                    Plugin.OpenEntryMap(item);
            }
            else if (isAchievementLink)
            {
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsItemClicked() && !isBlacklistShortcut)
                    Plugin.OpenAchievementWindow(item.Name);
            }

            DrawEntryContextMenu(item);

            // Nutzeranforderung: Quests, deren Abschluss automatisch eine Ätherströmung freischaltet
            // (siehe Plugin.QuestGrantsAetherCurrent-Kommentar), bekommen "(Aether Current)" hinter dem
            // Namen - in derselben Farbe wie die Ätherströmungs-Badges/-Kategorie, damit sofort
            // erkennbar ist, welche Quests das mitbringen.
            if (item.Type == CollectibleType.Quest && Plugin.QuestGrantsAetherCurrent(item.Id))
            {
                ImGui.SameLine(0f, 4f * scale);
                CodexTheme.TextShadowed(Loc.T("(Ätherströmung)", "(Aether Current)"), TypeColors[CollectibleType.AetherCurrent], shadowActive);
            }

            // 3. Schloss-Knopf (Abschnitt 5.7) - nur bei nicht erfüllter Voraussetzung, direkt neben
            // dem Namen (Nutzeranforderung), nicht mehr vor dem Preis.
            if (isGated)
            {
                var lockSize = new Vector2(22f * scale, 22f * scale);
                ImGui.SameLine(0f, 6f * scale);
                var gateReason = isQuestUnsupportedByQuestionable
                    ? Loc.T("Unsupported Quest by Questionable", "Unsupported Quest by Questionable")
                    : isAetherCurrentUnsupported
                        ? Loc.T("Currently unsupported", "Currently unsupported")
                        : Plugin.GetAchievementOrRankGateReason(item);
                DrawGateLockButton(item, lockSize, scale, gateReason);
            }

            // 4. Währung - Nutzeranforderung: statt des ausgeschriebenen Namens (z.B. "500,000 Gil")
            // nur noch Betrag + 16x16-Icon aus den Spieldaten (ITextureProvider.GetFromGameIcon,
            // dasselbe Vorbild wie CompactOverlayWindow.DrawCurrencyRequirement), 4px Abstand
            // dazwischen, rechtsbündig. Beide in EINER Gruppe (BeginGroup/EndGroup), damit EIN
            // zusammenhängender Hover-Bereich den Tooltip mit Betrag + vollem Währungsnamen
            // (item.Currency) auslöst - auch bei aktivem Textschatten (Abschnitt 5.9), wo TextShadowed
            // sonst keinen eigenen, per IsItemHovered() abfragbaren Treffer hinterlässt.
            if (item.CurrencyItemId != 0)
            {
                var culture = CultureInfo.GetCultureInfo(Loc.T("de-DE", "en-US"));
                var amountText = item.CurrencyAmount.ToString("N0", culture);
                var amountWidth = ImGui.CalcTextSize(amountText).X;
                var iconSize = 16f * scale;
                var totalWidth = amountWidth + 4f * scale + iconSize;

                // Nutzeranforderung: 2px weiter nach links als zuvor (war 3px Randabstand).
                ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalWidth - 5f * scale);
                ImGui.AlignTextToFramePadding();
                ImGui.BeginGroup();
                CodexTheme.TextShadowed(amountText, isGated ? CodexTheme.TextDim : CodexTheme.TextMuted, shadowActive);
                ImGui.SameLine(0f, 4f * scale);
                var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.CurrencyIconId)).GetWrapOrEmpty();
                ImGui.Image(icon.Handle, new Vector2(iconSize));
                ImGui.EndGroup();

                if (ImGui.IsItemHovered())
                    ShowTooltip(item.Currency);
            }
        }

        // Nutzer-Report: die letzte Zeile wirkte unten abgeschnitten, da sich bis ganz nach unten
        // scrollen ließ, ohne dass ihr unterer Rand je vollständig sichtbar wurde - ein kleiner
        // Nachlauf-Abstand hinter der letzten Zeile sorgt dafür, dass ImGui sie beim Scrollen bis zum
        // Ende (GetScrollMaxY) komplett ins Bild lässt, statt exakt an ihrer unteren Kante zu enden.
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        ImGui.Unindent(8f * scale);
        ImGui.EndChild();
    }

    /// <summary>
    /// Rechtsklick-Menü am Item-Namen (Nutzeranforderung) - zwei per Trennlinie abgesetzte
    /// Abschnitte: "Mehr Informationen", dann "Auf die ToDo-Liste setzen"/"Auf die Blacklist setzen"
    /// (farblich hervorgehoben), je mit einem Icon davor. Muss direkt NACH dem Namen-Widget
    /// aufgerufen werden, da ImGui.OpenPopupOnItemClick sich auf das zuletzt gezeichnete Item
    /// bezieht - siehe CompactOverlayWindow.DrawEntryContextMenu-Vorbild. Ein "GoTo"-Eintrag (echte
    /// Hinlaufen-Automation, siehe CompactOverlayWindow.DrawGoToIcon/plugin.GoToAutomation) war hier
    /// kurz enthalten, wurde aber auf Nutzerwunsch vorerst wieder entfernt. Die Karte bleibt
    /// weiterhin per Linksklick auf den Namen selbst erreichbar (Plugin.OpenEntryMap).
    /// </summary>
    /// <summary>Internal statt private: wird auch von Windows.CodexMenuWindow.DrawDatabaseTable wiederverwendet (Nutzeranforderung: "dasselbe Rechtsklick-Menü wie im Overlay" für Datenbank-Einträge).</summary>
    internal void DrawEntryContextMenu(CollectibleEntry item)
    {
        var popupId = $"##CodexEntryMenu{item.Type}{item.Id}";
        ImGui.OpenPopupOnItemClick(popupId, ImGuiPopupFlags.MouseButtonRight);

        // Nutzervorgabe: 4px Abstand zu allen Rändern statt des deutlich größeren ImGui-Standard-
        // WindowPadding - muss VOR BeginPopup gepusht werden (wirkt auf das dabei ggf. neu erzeugte
        // Fenster) und in jedem Fall (auch bei geschlossenem Popup) wieder gepoppt werden.
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f * scale, 4f * scale));
        if (!ImGui.BeginPopup(popupId))
        {
            ImGui.PopStyleVar();
            return;
        }

        var allaganToolsEnabled = plugin.Configuration.EnableAllaganToolsIntegration
                                   && Plugin.IsAllaganToolsAvailable()
                                   && AllaganToolsEligibleTypes.Contains(item.Type)
                                   && (item.Type is not (CollectibleType.FrameKit or CollectibleType.Hairstyle) || Plugin.HasUnlockItem(item));

        if (allaganToolsEnabled && DrawEntryMenuItem(FontAwesomeIcon.InfoCircle, Loc.T("Mehr Informationen", "More information")))
            Plugin.OpenAllaganToolsItemInfo(item);

        // GoTo (Hinlaufen-Automation) vorerst wieder entfernt (Nutzeranforderung) - war hier als
        // plugin.GoToAutomation.GoTo(item)/Cancel() verdrahtet, siehe Git-Historie bei Bedarf.

        ImGui.Separator();

        if (Plugin.IsOnToDoList(item))
        {
            if (DrawEntryMenuItem(FontAwesomeIcon.Times, Loc.T("Von der ToDo-Liste entfernen", "Remove from ToDo list")))
                Plugin.RemoveFromToDoList(item.Type, item.Id);
        }
        else if (DrawEntryMenuItem(FontAwesomeIcon.ClipboardList, Loc.T("Auf die ToDo-Liste setzen", "Add to ToDo list")))
        {
            Plugin.AddToToDoList(item);
        }

        // Bugfix (Nutzer-Report): zeigte bisher IMMER "Auf die Blacklist setzen" und rief immer
        // AddToBlacklist auf, auch für bereits geblacklistete Einträge - jetzt wie beim ToDo-Eintrag
        // darüber je nach aktuellem Status umgeschaltet.
        var isBlacklisted = Plugin.IsBlacklisted(item);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.35f, 0.35f, 1f));
        var blacklistClicked = DrawEntryMenuItem(FontAwesomeIcon.Ban,
            isBlacklisted ? Loc.T("Von der Blacklist entfernen", "Remove from blacklist") : Loc.T("Auf die Blacklist setzen", "Add to blacklist"));
        ImGui.PopStyleColor();
        if (blacklistClicked)
        {
            if (isBlacklisted)
                Plugin.RemoveFromBlacklist(item.Type, item.Id);
            else
                Plugin.AddToBlacklist(item);
        }

        ImGui.EndPopup();
        ImGui.PopStyleVar();
    }

    /// <summary>Ein Menüeintrag mit vorangestelltem Icon (Nutzeranforderung) - Icon in der Icon-Schrift, Beschriftung danach als normales Selectable.</summary>
    private static bool DrawEntryMenuItem(FontAwesomeIcon icon, string label)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SameLine();
        return ImGui.Selectable(label);
    }

    internal static void CenteredText(string text, Vector2 areaSize, Vector4 color, bool shadow)
    {
        using (CodexTheme.FontTypeBadge.Push())
        {
            var textSize = ImGui.CalcTextSize(text);
            var start = ImGui.GetCursorScreenPos();
            CodexTheme.DrawTextShadowed(
                ImGui.GetWindowDrawList(),
                start + new Vector2((areaSize.X - textSize.X) / 2f, (areaSize.Y - textSize.Y) / 2f),
                color,
                text,
                shadow);
        }

        ImGui.Dummy(areaSize);
    }

    /// <summary>
    /// Abschnitt 5.7 - Schloss-Knopf vor dem Preis eines Eintrags mit nicht erfüllter Voraussetzung
    /// (Achievement/Rang) ODER einer von Questionable als nicht unterstützt gemeldeten Quest (siehe
    /// Aufrufer) - der Tooltip-Grund wird dafür vom Aufrufer übergeben statt hier fest
    /// Plugin.GetAchievementOrRankGateReason aufzurufen, damit beide Fälle dasselbe Schloss-Symbol
    /// nutzen können.
    /// </summary>
    private static void DrawGateLockButton(CollectibleEntry item, Vector2 size, float scale, string reason)
    {
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##CodexGateLock{item.Type}{item.Id}", size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(CodexTheme.WarnBg), CodexTheme.RoundingControl);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(CodexTheme.WarnLine), CodexTheme.RoundingControl);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            // Nutzervorgabe: Schloss-Symbol um 2px kleiner als die native Icon-Schriftgröße.
            var glyph = FontAwesomeIcon.Lock.ToIconString();
            var nativeIconPx = ImGui.GetFontSize();
            ImGui.SetWindowFontScale((nativeIconPx - 2f * scale) / nativeIconPx);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(cursor + new Vector2((size.X - glyphSize.X) / 2f, (size.Y - glyphSize.Y) / 2f), ImGui.GetColorU32(CodexTheme.WarnFg), glyph);
            ImGui.SetWindowFontScale(1f);
        }

        if (!hovered)
            return;

        ImGui.PushStyleColor(ImGuiCol.PopupBg, CodexTheme.BgPopup);
        ImGui.PushStyleColor(ImGuiCol.Border, CodexTheme.TooltipLine);
        // Nutzervorgabe: 2px Abstand zu allen Rändern (Titelzeile oben/links, Text darunter
        // rundherum) statt des deutlich größeren ImGui-Standard-WindowPadding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f * scale, 2f * scale));
        ImGui.BeginTooltip();

        // Titelzeile: Schloss-Symbol ebenfalls 2px kleiner, zusätzlich weitere 2px Abstand zum
        // oberen Rand (oben auf das bereits per WindowPadding gesetzte 2px) - Titeltext bekommt
        // davon abweichend weitere 3px (Nutzervorgabe).
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2f * scale);
        float titleIconNativePx;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            titleIconNativePx = ImGui.GetFontSize();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            ImGui.SetWindowFontScale((titleIconNativePx - 2f * scale) / titleIconNativePx);
            ImGui.TextColored(CodexTheme.WarnFg, FontAwesomeIcon.Lock.ToIconString());
            ImGui.SetWindowFontScale(1f);
        }
        ImGui.SameLine();
        ImGui.TextColored(CodexTheme.WarnFg, Loc.T("Voraussetzung nicht erfüllt", "Requirement not met"));
        ImGui.Separator();

        // Grund: weitere 2px Abstand an allen Seiten (oben auf das WindowPadding).
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        ImGui.Indent(2f * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 250f * scale - 4f * scale);
        // Keine "◆"-Aufzählung mehr davor (Nutzer-Report: wird im Standard-ImGui-Font als
        // Fragezeichen/Tofu-Glyph dargestellt, da dessen Zeichensatz diesen Unicode-Punkt nicht
        // enthält).
        ImGui.TextColored(CodexTheme.TextValue, reason);
        ImGui.PopTextWrapPos();
        ImGui.Unindent(2f * scale);
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }
}
