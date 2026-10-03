using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;

namespace TheExplorersCodex.Windows;

/// <summary>
/// Schlankes, randloses Overlay-Fenster - zeigt nur Sammelobjekte der aktuellen Zone.
/// Gedacht zum permanenten Offenlassen neben der Action-Leiste, daher bewusst kompakt gehalten.
/// </summary>
public class CompactOverlayWindow : Window
{
    private readonly Plugin plugin;

    // NoBringToFrontOnFocus ist hier der entscheidende Teil: ohne das rutscht das Overlay bei
    // JEDER Interaktion (auch nur Hovern/Scrollen) wieder an die Spitze des ImGui-Z-Stapels - lag
    // ein anderes Fenster (Spiel-eigenes UI oder ein anderes Plugin) optisch DARÜBER, fing das
    // Overlay dessen Klicks trotzdem ab (es galt für die Eingabe-Ermittlung weiterhin als "vorn"),
    // wodurch das andere Fenster an dieser Stelle nicht mehr klickbar war. Da dieses Fenster ohnehin
    // dauerhaft offen bleiben soll (kein Grund, es je "nach vorne" zu holen), kostet das nichts.
    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    public CompactOverlayWindow(Plugin plugin) : base("##TheExplorersCodexCompact", BaseFlags)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;

        // Startgröße nur beim allerersten Öffnen - danach darf der Spieler frei skalieren
        // (z.B. wenn ein Mount-Name nicht in die Standardbreite passt).
        Size = new Vector2(260, 200);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(180, 100),
            MaximumSize = new Vector2(800, 2000),
        };
    }

    public void Dispose() { }

    // Position/Größe vom LETZTEN Frame (siehe Draw, ganz unten aktualisiert) - für die
    // Überlappungsprüfung in PreDraw. Erst NACH Begin() (also in Draw) wüsste man die aktuelle
    // Position zwar noch genauer, aber Fenster-Flags wie NoMouseInputs (siehe PreDraw) wirken nur,
    // wenn sie VOR Begin() gesetzt werden - eine Frame Verzögerung ist dafür unmerklich, da sich
    // die Fensterposition normalerweise nicht jeden Frame ändert.
    private Vector2? lastWindowMin;
    private Vector2? lastWindowMax;

// Vom aktuellen Frame - einmal in PreDraw ermittelt (auf Basis der Fensterposition vom LETZTEN
    // Frame, siehe lastWindowMin/Max), dann in Draw benutzt, um dort per ImGuiP.SetWindowHitTestHole
    // gezielt Mausklicks ans darunterliegende native Fenster durchzureichen, und über IsOccluded in
    // praktisch jedem Zeichenaufruf in DrawContent, um NUR die davon betroffenen Zeilen/Knöpfe/Icons
    // unsichtbar zu machen - der Rest des Overlays bleibt normal sichtbar/klickbar, auch wenn
    // irgendwo ein natives Fenster überlappt.
    private List<(Vector2 Min, Vector2 Max)> nativeOverlapRects = new();

    // Eigener ImGuiWindow-Zeiger (siehe Draw, dort gesetzt) - für den Selbstausschluss in
    // Plugin.GetOverlappingNativeWindowRects: ein Namensabgleich war unzuverlässig (Dalamuds Window-
    // Basisklasse hängt an die in Begin() übergebene ID intern noch weitere Zeichen an, siehe Nutzer-
    // Report "Overlay wird immer ausgeblendet" - der Namensvergleich traf dadurch nie, das Overlay
    // hat sich selbst als "anderes Fenster" erkannt und sich dadurch komplett durchlöchert/versteckt).
    // Ein Zeigervergleich ist dagegen unabhängig vom genauen internen Namensformat.
    private nint ownWindowHandle;

    // Eingeklappt = nur die Kopfzeile (Titel + Schloss-/Einklapp-/Schließen-Knopf) sichtbar, der
    // Rest (Zonenname, Währungen, Sammelobjekt-Liste) wird ausgeblendet - identisches Prinzip wie
    // beim Optionsfenster (siehe MainWindow.collapsed), hier aber die Zielhöhe NICHT fest verdrahtet,
    // sondern jeden Frame aus der tatsächlich gezeichneten Kopfzeile gemessen (siehe DrawContent) -
    // die Schriftgröße dieses Fensters ist über config.CompactFontScale frei einstellbar, eine feste
    // Höhe wie bei MainWindow würde dort also nicht zu jeder Skalierung passen.
    private bool collapsed;
    private bool collapsedLastFrame;
    private Vector2 expandedSize = new(260, 200);

    // Automatisches Einklappen, sobald ein ECHTES natives Fenster (mit Titelleiste, z.B. die
    // Crucible-/Beastmaster-Tafel) das Overlay überlappt (Nutzer-Report: "Das Fenster vom Beastmaster
    // ist nicht verschiebbar/klickbar, weil es auf dem Overlay liegt") - NoInputs (weder über die
    // Dalamud-Flags-Eigenschaft noch direkt am rohen ImGui-Fenster gesetzt, siehe Git-Historie) hat
    // sich als wirkungslos erwiesen; einzig TATSÄCHLICHES Einklappen (das Fenster existiert an der
    // Stelle danach schlicht nicht mehr) hat im Test geholfen. Bewusst GETRENNT vom manuell vom
    // Spieler gesetzten "collapsed" (das bleibt unverändert) - EffectiveCollapsed() ist überall dort
    // zu verwenden, wo bisher "collapsed" direkt die Größe/den Inhalt steuerte, damit nach dem
    // Verschwinden des nativen Fensters wieder genau der vom Spieler zuletzt gewählte Zustand gilt.
    private bool autoCollapsedForNativeOverlap;

    // Milderer Fall von autoCollapsedForNativeOverlap (Nutzeranforderung: "nicht alles ausblenden,
    // wenn möglich") - überlappt das native Fenster NICHT bis ganz nach oben, bleibt genug Platz, um
    // nur auf den freien oberen Teil (Kopfzeile + Anfang der Liste) zu schrumpfen, statt komplett auf
    // Kopfzeilenhöhe einzuklappen. null = kein Teil-Schrumpfen nötig/möglich diesen Frame.
    private float? autoShrinkToHeight;

    private const float AutoShrinkMinimumHeight = 80f;
    private const float AutoShrinkSafetyMargin = 4f;

    private bool EffectiveCollapsed() => collapsed || autoCollapsedForNativeOverlap;

    // Wie collapsedLastFrame, aber für Configuration.HideOverlayWhenEmpty (siehe DrawContent, ganz
    // am Anfang gesetzt/gelesen) - ohne diese Wiederherstellung in PreDraw würde das Fenster nach
    // dem Schrumpfen auf 0x0 dauerhaft winzig bleiben, auch nachdem wieder etwas fehlt.
    private bool hiddenDueToEmptyLastFrame;

    // Umschaltet zwischen "Deine Währungen:" (besessene Menge) und "Benötigte Währung:" (Summe der
    // noch fehlenden Menge über alle aktuell angezeigten, noch nicht besessenen Einträge hinweg) -
    // siehe DrawCurrencyWallet. Bewusst kein Configuration-Feld, da es sich nur um eine
    // Sitzungs-Ansicht handelt, kein dauerhaft zu speichernder Zustand.
    private bool showCurrencyCostMode;

    // Wiederverwendetes leeres Dictionary statt bei jeder Währung im "Benötigte Währung"-Modus neu
    // zu allozieren (siehe DrawCurrencyWallet) - dort wird gar nicht erst nach Retainer-Beständen
    // gefragt.
    private static readonly Dictionary<string, uint> EmptyRetainerCounts = new();

    /// <summary>
    /// Ob das Element, das man an der AKTUELLEN Cursor-Position mit der übergebenen Größe zeichnen
    /// würde, unter einem nativen Fenster liegen würde (siehe nativeOverlapRects) - jede
    /// zeichnende Methode in dieser Klasse ruft das VOR dem eigentlichen Zeichnen auf und
    /// zeichnet bei true stattdessen einen gleich großen ImGui.Dummy (siehe z.B. OutlineText),
    /// damit Layout/SameLine-Reihenfolge unverändert bleiben, aber nichts Sichtbares/Klickbares
    /// an dieser Stelle entsteht.
    /// </summary>
    private bool IsOccluded(Vector2 size)
    {
        if (nativeOverlapRects.Count == 0)
            return false;

        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        return nativeOverlapRects.Any(r => r.Min.X < max.X && r.Max.X > min.X && r.Min.Y < max.Y && r.Max.Y > min.Y);
    }

    /// <summary>
    /// Verhindert, dass das Overlay schon am Titelbildschirm (vor dem Einloggen) oder während des
    /// Lade-/Zonenwechsel-Übergangs (BetweenAreas/BetweenAreas51 - Ladebildschirm, Zone noch nicht
    /// fertig geladen) mit ggf. veralteten Daten der letzten Sitzung angezeigt wird - wird von
    /// Dalamuds WindowSystem VOR PreDraw/Draw/PostDraw geprüft, das Fenster erscheint also gar
    /// nicht erst statt nur mit falschem Inhalt.
    /// </summary>
    /// <summary>
    /// Zusätzlich (Nutzeranforderung): während MSQ-Solo-Duties (siehe Plugin.IsInMsqSoloDuty)
    /// ausgeblendet, danach automatisch wieder eingeblendet - wird jeden Frame neu geprüft, kein
    /// eigener An-/Aus-Zustand nötig.
    /// </summary>
    public override bool DrawConditions() =>
        Plugin.ClientState.IsLoggedIn && !Plugin.Condition[ConditionFlag.BetweenAreas] && !Plugin.Condition[ConditionFlag.BetweenAreas51]
        && !Plugin.IsInMsqSoloDuty();

    // Dezentes Weiß-Grau statt der vorherigen lila Farbe - klein und unauffällig, zeigt aber
    // weiterhin an, wo sich das Fenster zum Skalieren greifen lässt.
    private static readonly Vector4 ResizeGripColor = new(1f, 1f, 1f, 0.25f);
    private static readonly Vector4 ResizeGripHoveredColor = new(1f, 1f, 1f, 0.5f);
    private static readonly Vector4 ResizeGripActiveColor = new(1f, 1f, 1f, 0.7f);
    private static readonly Vector4 ResizeGripHiddenColor = new(0f, 0f, 0f, 0f);

    // Ungefähre sichtbare Kantenlänge (Yalm... äh, Pixel) des von ImGui selbst in die untere rechte
    // Fensterecke gezeichneten Greifdreiecks - ImGui legt die genaue Größe intern fest (u.a. an der
    // Schriftgröße orientiert), ein fester, großzügig bemessener Wert reicht hier aber, da es nur um
    // die grobe Überlappungsprüfung geht, nicht um pixelgenaues Clipping.
    private const float ResizeGripVisualSize = 20f;

    public override void PreDraw()
    {
        var config = plugin.Configuration;

        // Auf Basis der Fensterposition vom LETZTEN Frame (siehe Draw, ganz unten aktualisiert) -
        // erst NACH Begin() (also in Draw) wüsste man die aktuelle Position zwar noch genauer, eine
        // Frame Verzögerung ist dafür unmerklich, da sich die Fensterposition normalerweise nicht
        // jeden Frame ändert.
        nativeOverlapRects = lastWindowMin.HasValue && lastWindowMax.HasValue
            ? Plugin.GetOverlappingNativeWindowRects(lastWindowMin.Value, lastWindowMax.Value, ownWindowHandle)
            : new List<(Vector2 Min, Vector2 Max)>();

        // Automatisches Einklappen, solange ein ECHTES natives Fenster das Overlay überlappt (siehe
        // autoCollapsedForNativeOverlap-Kommentar) - mit den Grenzen vom LETZTEN Frame geprüft, wie
        // nativeOverlapRects oben auch (ein Frame Verzögerung ist unmerklich). BEWUSST gegen die volle
        // AUSGEKLAPPTE Größe (expandedSize) geprüft, nicht gegen die aktuellen (ggf. schon wegen des
        // Auto-Einklappens geschrumpften) lastWindowMax - sonst würde das geschrumpfte Fenster das
        // native nicht mehr überlappen, "ausklappen" auslösen, das wieder überlappt, wieder
        // einklappt, usw. (Endlos-Geflacker zwischen ein-/ausgeklappt).
        //
        // Bevorzugt NUR auf den nicht überlappten oberen Teil schrumpfen (autoShrinkToHeight,
        // Nutzeranforderung: "nicht alles ausblenden, wenn möglich") - überlappt das native Fenster
        // z.B. nur die untere Hälfte, bleiben Zonenname/obere Listeneinträge weiterhin sichtbar und
        // bedienbar. Komplettes Einklappen (autoCollapsedForNativeOverlap) nur noch als Rückfall,
        // wenn für den oberen Teil nicht einmal mehr AutoShrinkMinimumHeight übrig bleibt (natives
        // Fenster reicht bis (fast) an die eigene Kopfzeile heran).
        autoCollapsedForNativeOverlap = false;
        autoShrinkToHeight = null;
        if (lastWindowMin.HasValue
            && Plugin.TryGetOverlappingDraggableNativeWindowRect(lastWindowMin.Value, lastWindowMin.Value + expandedSize, out var nativeMin, out _))
        {
            var availableHeight = nativeMin.Y - lastWindowMin.Value.Y - AutoShrinkSafetyMargin;
            if (availableHeight >= AutoShrinkMinimumHeight)
                autoShrinkToHeight = availableHeight;
            else
                autoCollapsedForNativeOverlap = true;
        }

        var effectiveCollapsed = EffectiveCollapsed();

        // Gesperrt = nur die Position fixiert, nicht die Größe - das Fenster bleibt also auch im
        // gesperrten Zustand an der Ecke skalierbar (z.B. wenn ein Mount-Name nicht mehr in die
        // aktuelle Breite passt), nur das versehentliche Verschieben wird verhindert. Eingeklappt
        // zusätzlich NoResize - bei der dann sehr knappen Höhe würde ImGuis eigene Rahmen-Zieh-
        // Trefferzone sonst praktisch das ganze Fenster überlappen und Klicks (auch den Klick auf
        // den Ausklapp-Knopf) abfangen, statt sie durchzulassen (identisches Problem/Lösung wie im
        // Optionsfenster, siehe MainWindow.PreDraw).
        var flags = BaseFlags;
        if (config.CompactLocked)
            flags |= ImGuiWindowFlags.NoMove;
        // Während des (vollen ODER teilweisen) automatischen Schrumpfens ebenfalls NoResize - siehe
        // Begründung oben beim normalen Einklappen, gilt für den Teil-Fall genauso.
        var adjustedThisFrame = effectiveCollapsed || autoShrinkToHeight.HasValue;
        if (adjustedThisFrame)
            flags |= ImGuiWindowFlags.NoResize;

        Flags = flags;

        // Siehe MainWindow.PreDraw (identisches Problem/Lösung): ImGuis Stil-Standard WindowMinSize
        // (32x32) würde ein Schrumpfen auf die (deutlich kleinere) eingeklappte Kopfzeilenhöhe sonst
        // verhindern, egal was wir per Größe vorgeben.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1f, 1f));

        if (autoShrinkToHeight.HasValue)
        {
            // Zielhöhe schon VOR Begin() bekannt (aus der Position des nativen Fensters berechnet,
            // kein Messen nach dem Zeichnen nötig wie beim vollen Einklappen unten) - wirkt dadurch
            // sofort in diesem Frame.
            Size = new Vector2(expandedSize.X, autoShrinkToHeight.Value);
            SizeCondition = ImGuiCond.Always;
        }
        // Zurück zur zuletzt bekannten ausgeklappten Größe - muss VOR Begin() passieren (siehe
        // MainWindow.PreDraw), sonst kommt die Wiederherstellung erst einen Frame zu spät sichtbar
        // an. Das Schrumpfen beim (vollen) EINklappen passiert dagegen bewusst NICHT hier, sondern
        // erst in DrawContent (nach Begin()) - dort ist die tatsächlich benötigte Höhe der Kopfzeile
        // bekannt (abhängig von config.CompactFontScale), hier vorher noch nicht.
        else if ((!adjustedThisFrame && collapsedLastFrame) || hiddenDueToEmptyLastFrame)
        {
            Size = expandedSize;
            SizeCondition = ImGuiCond.Always;
        }
        else
        {
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        collapsedLastFrame = adjustedThisFrame;

        var alpha = 1f - System.Math.Clamp(config.CompactTransparency, 0f, 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
        // Dieselbe Grundfarbe wie das Optionsfenster (siehe ModernUi.PushStyle) - bei Transparenz=0
        // (voll undurchsichtig) sehen beide Fenster damit identisch aus.
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.055f, 0.063f, 0.098f, alpha));

        // Genau wie bei der Scrollbar (siehe DrawContent) wird auch das Greifdreieck unten rechts
        // von ImGui selbst gezeichnet, nicht über die zeilenweise IsOccluded-Prüfung - läge ein
        // natives Fenster genau unter dieser Ecke, würde es trotzdem weiter sichtbar darüber
        // gezeichnet. Deshalb hier komplett durchsichtig machen, statt in den normalen Farben, wenn
        // die (grob abgeschätzte) Greif-Ecke ein natives Fenster überlappt.
        var gripOverlapped = false;
        if (lastWindowMax.HasValue)
        {
            var gripMin = lastWindowMax.Value - new Vector2(ResizeGripVisualSize, ResizeGripVisualSize);
            var gripMax = lastWindowMax.Value;
            gripOverlapped = nativeOverlapRects.Any(r =>
                r.Min.X < gripMax.X && r.Max.X > gripMin.X && r.Min.Y < gripMax.Y && r.Max.Y > gripMin.Y);
        }

        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, gripOverlapped ? ResizeGripHiddenColor : ResizeGripColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, gripOverlapped ? ResizeGripHiddenColor : ResizeGripHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, gripOverlapped ? ResizeGripHiddenColor : ResizeGripActiveColor);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(4);
    }

    public override unsafe void Draw()
    {
        var window = ImGuiP.GetCurrentWindow();
        ownWindowHandle = (nint)window.Handle;

        // Erzwingt JEDEN Frame aufs Neue, dass dieses Fenster ganz hinten im Anzeige-Stapel sitzt -
        // NoBringToFrontOnFocus (siehe BaseFlags) verhindert nur, dass es bei eigener Interaktion
        // wieder nach vorn rutscht, garantiert aber nicht, dass es ÜBERHAUPT hinten bleibt (z.B.
        // wenn ein anderes Fenster geschlossen und neu geöffnet wird). BringWindowToDisplayBack ist
        // intern in ImGui, aber über ImGuiP öffentlich zugänglich - genau dafür gedacht.
        ImGuiP.BringWindowToDisplayBack(window);

        // Für die Überlappungsprüfung im NÄCHSTEN Frame merken (siehe PreDraw/nativeOverlapRects).
        lastWindowMin = ImGui.GetWindowPos();
        lastWindowMax = lastWindowMin + ImGui.GetWindowSize();

        // Nur merken, solange NICHT (voll ODER teilweise, auch automatisch wegen überlappendem
        // nativen Fenster) geschrumpft - sonst würde die künstlich verkleinerte Größe versehentlich
        // als "neue ausgeklappte Normalgröße" gespeichert und beim Zurückkehren zur Normalgröße
        // fälschlich wiederhergestellt (identisches Problem/Lösung wie in MainWindow.Draw).
        if (!EffectiveCollapsed() && !autoShrinkToHeight.HasValue)
            expandedSize = ImGui.GetWindowSize();

        // Dalamud/ImGui zeichnet grundsätzlich IMMER über dem nativen Spiel-UI (keine echte Z-Order
        // zwischen beiden möglich, siehe Plugin.GetOverlappingNativeWindowRects-Kommentar) - echte
        // Mausklicks würden ein darunter liegendes natives Fenster (Währung, Inventar, ...) sonst
        // nie erreichen, obwohl es dort sichtbar ist. SetWindowHitTestHole "durchlöchert" unser
        // Fenster gezielt an dieser Stelle, statt (wie früher) für den GESAMTEN Frame sämtliche
        // Mauseingaben zu deaktivieren - der Rest des Overlays bleibt normal klickbar. ImGui
        // unterstützt intern nur EIN Loch pro Fenster und Frame - bei mehreren gleichzeitig
        // überlappenden nativen Fenstern (seltener Fall) wird deshalb die umschließende Hülle aller
        // Überlappungen als ein einziges Loch benutzt, statt nur die letzte zu berücksichtigen.
        if (nativeOverlapRects.Count > 0)
        {
            var holeMin = nativeOverlapRects[0].Min;
            var holeMax = nativeOverlapRects[0].Max;
            foreach (var (min, max) in nativeOverlapRects.Skip(1))
            {
                holeMin = Vector2.Min(holeMin, min);
                holeMax = Vector2.Max(holeMax, max);
            }

            ImGuiP.SetWindowHitTestHole(window, holeMin, holeMax - holeMin);
        }

        // Direkt am rohen ImGui-Fenster NACH Begin() gesetzt, nicht über die Dalamud-Window.Flags-
        // Eigenschaft (siehe PreDraw-Kommentar) - mehrere Versuche darüber haben trotz korrekt
        // erkannter Überlappung nichts bewirkt (Nutzer-Report: Ziehen/Klicken am nativen Fenster ging
        // weiterhin nicht, nur komplettes Einklappen unseres Fensters half - das bestätigte zumindest,
        // dass UNSER Fenster tatsächlich die Ursache war, nicht ein anderes Plugin). Mit den JETZT
        // aktuellen (nicht erst nächsten Frame bekannten) Fenstergrenzen geprüft, nicht den Werten vom
        // letzten Frame.
        // NoInputs (weder über die Dalamud-Flags-Eigenschaft noch direkt am rohen ImGui-Fenster
        // gesetzt, siehe Git-Historie) hat sich im Live-Test als wirkungslos erwiesen - automatisches
        // Einklappen (siehe autoCollapsedForNativeOverlap/EffectiveCollapsed, oben in PreDraw berechnet
        // und unten in DrawContent angewendet) ersetzt das jetzt, da NUR das im Test tatsächlich half.

        DrawContent();
    }

    private void DrawContent()
    {
        var config = plugin.Configuration;
        using var fontScope = PushCompactFont(config);
        ImGui.SetWindowFontScale(config.CompactFontScale);

        var currentTerritoryId = Plugin.ClientState.TerritoryType;

        // Manche Zonen gehören zu einer Stadt, haben aber ein eigenes TerritoryType (z.B. "Heart of
        // the Sworn" -> Ul'dah) - dort sollen dieselben Daten wie in der zugeordneten Stadtzone
        // verwendet werden. Nur für Datenabfragen, nicht für die angezeigte Zonenüberschrift unten.
        var effectiveTerritoryId = Plugin.ResolveEffectiveTerritoryId(currentTerritoryId);

        // In geteilten Hauptstädten (Ul'dah, Limsa, Gridania, Ishgard) sollen Sammelobjekte aus
        // JEDEM Bezirk angezeigt werden, egal in welchem man gerade steht - jeder Eintrag verlinkt
        // trotzdem auf seinen tatsächlichen Bezirk (siehe FlagTerritoryTypeId/MapId je Eintrag).
        var siblingTerritories = Plugin.GetSplitCityTerritories(effectiveTerritoryId);
        var allForZone = CollectionData.GetAllEntries()
            .Concat(plugin.GetLiveZoneEntries(effectiveTerritoryId))
            // Hunting-Log-Einträge sind bewusst an die TATSÄCHLICHE Zone (nicht die für geteilte
            // Hauptstädte "aufgelöste" effectiveTerritoryId) gebunden - roamende Monster gibt es
            // nur in genau dieser einen Zone, nicht stadtweit wie Aetheryten/Quest-NPCs.
            .Concat(plugin.GetHuntingLogEntries(currentTerritoryId))
            .Where(e => siblingTerritories.Contains(e.TerritoryTypeId))
            // Blacklist (siehe Plugin.IsBlacklisted) ganz vorne - ALLES Weitere (Anzeige UND alle
            // Automationen, die ihre Listen hieraus ableiten) sieht diese Einträge gar nicht erst.
            .Where(e => !Plugin.IsBlacklisted(e))
            // Bei deaktiviertem "Alle Gegenstände anzeigen" (Configuration.ShowAllItems, siehe
            // MainWindow-Einstellungen) Einträge ausblenden, die nur durch eine noch nicht erreichte
            // Errungenschaft/einen noch nicht freigeschalteten Rang ODER (siehe
            // ComputeGrandCompanyOrTribeGateReason) ein gerade nicht laufendes Saisonevent erreichbar
            // sind (siehe Plugin.AchievementOrRankGatedItems) - bei aktiviertem Schalter bleiben sie
            // sichtbar, aber mit der "Bedingung nicht erfüllt"-Markierung (siehe weiter unten).
            // Hunting-Log-Ziele höherer, noch nicht erreichter Rang-Stufen (siehe
            // CollectibleEntry.HuntingLogRequiredRank) bleiben IMMER sichtbar (mit Markierung) -
            // ausdrücklicher Nutzerwunsch, als Vorschau auf das, was noch kommt.
            .Where(e => config.ShowAllItems || e.Type == CollectibleType.HuntingLog || !Plugin.IsAchievementOrRankGated(e))
            .ToList();

        // Manche Triple-Triad-Karten stehen doppelt in CollectionData.GetAllEntries() - einmal
        // generisch (aus triadcards.json, oft MIT eigenem, von Hand gepflegtem Vendor/Fundort) UND
        // einmal live aus Lumina berechnet pro NPC-Gegner (siehe Plugin.GetTripleTriadNpcEntries,
        // erkennbar an EventNpcId != 0) - Nutzer-Report: z.B. "Coeurlregina" wurde im Overlay
        // zweimal angezeigt, EIN Eintrag prominent mit NPC-Namen, der andere ohne, aber mit NPC-Namen
        // im Tooltip (dessen eigener Vendor-Name aus der JSON-Datei). Ein Abgleich allein über
        // HasGoToTarget reicht nicht, da auch die JSON-Variante oft schon Vendor+Koordinaten trägt.
        // Die generische JSON-Variante wird daher ausgeblendet, sobald für dieselbe Karte mindestens
        // ein live berechneter NPC-Gegner (EventNpcId != 0) existiert - der ist aktueller/zuverlässiger.
        // Zwei verschiedene NPC-Gegner für dieselbe Karte (beide mit EventNpcId != 0) bleiben dagegen
        // beide sichtbar (jeder ein eigenes, tatsächlich nutzbares Ziel) - anders als
        // Plugin.GetGlobalEntries (Datenbank-Seite/ToDo-Liste), das komplett auf einen Eintrag pro
        // Karte dedupliziert.
        var tripleTriadIdsWithLiveNpc = allForZone
            .Where(e => e.Type == CollectibleType.TripleTriadCard && e.EventNpcId != 0)
            .Select(e => e.Id)
            .ToHashSet();
        allForZone = allForZone
            .Where(e => e.Type != CollectibleType.TripleTriadCard || e.EventNpcId != 0 || !tripleTriadIdsWithLiveNpc.Contains(e.Id))
            .ToList();

        var afterTypeFilter = allForZone
            .Where(e => config.ShowType.GetValueOrDefault(e.Type, true))
            .ToList();

        // Siehe "Currencys filtern" weiter unten - blendet ALLE Einträge einer vom Nutzer
        // ausgewählten Währung aus, unabhängig vom Typ. Prüft auch AdditionalCurrencies (siehe
        // GetAllCurrencyLabels) - ein Eintrag mit mehreren Währungen (z.B. Triple-Triad-Karte
        // "G-Warrior") verschwindet also auch dann, wenn nur EINE seiner mehreren Währungen
        // ausgeblendet wurde, nicht nur bei der ersten.
        var afterCurrencyFilter = afterTypeFilter
            .Where(e => !GetAllCurrencyLabels(e).Any(config.HiddenCurrencies.Contains))
            .ToList();

        var entries = afterCurrencyFilter
            .Where(e => !plugin.IsOwned(e))
            // Siehe Configuration.ShowOnlyActiveEventItems-Kommentar - blendet bei aktiviertem
            // Schalter NUR die Saisonevent-Einträge aus, deren Event gerade NICHT läuft; alle
            // anderen Einträge (auch alle normalen, nicht event-gebundenen) bleiben unverändert.
            .Where(e => !config.ShowOnlyActiveEventItems || e.Category != "Saisonevent" || Plugin.IsSeasonalEventEntryCurrentlyActive(e))
            .OrderBy(e => config.TypeOrder.IndexOf(e.Type))
            .ThenBy(e => e.Vendor)
            .ThenBy(e => e.Name)
            .ToList();

        // Bewusst aus "allForZone" (nicht "entries") - die Automation soll unabhängig vom
        // Typen-Filter laufen, auch wenn Quests im Overlay z.B. ausgeblendet sind. IsAchievementOrRankGated
        // aber IMMER zusätzlich ausgeschlossen (nicht nur wenn "Alle Gegenstände anzeigen" aus ist,
        // siehe allForZone) - sonst würde die Automation bei aktiviertem Schalter auch Quests
        // anlaufen, die als "Bedingung nicht erfüllt" markiert sind (z.B. "Simply to Dye For" ohne
        // abgeschlossene Artefakt-Rüstungsquest).
        var missingQuests = allForZone
            .Where(e => e.Type == CollectibleType.Quest && !plugin.IsOwned(e) && !Plugin.IsAchievementOrRankGated(e))
            .ToList();

        // Unabhängig davon, ob die Automation läuft - damit die rote "Nicht unterstützt"-Markierung
        // schon beim Betreten der Zone erscheint, statt erst nach einem gestarteten Automation-Lauf.
        plugin.QuestAutomation.RefreshSupportStatus(missingQuests);

        // Nur für den restringierten ToDo-Modus gebraucht (siehe QuestAutomation.Start/
        // UpdateRestrictedIdle) - muss unabhängig vom gerade sichtbaren Tab berechnet werden, da die
        // Automation (einmal im ToDo-Modus gestartet) auch weiterläuft, während der Overlay-Tab
        // sichtbar ist. Im normalen Modus (Start ohne restrictToQuestIds) unbenutzt.
        var toDoQuestEntries = plugin.ResolveToDoEntries().Where(e => e.Type == CollectibleType.Quest).ToList();
        plugin.QuestAutomation.Update(missingQuests, effectiveTerritoryId, toDoQuestEntries);

        // Sonderfall "The Eight Sentinels" (Flugverbots-Bereich in Mor Dhona, siehe NoFlyAreaExit): erst
        // über das Crystal Gate hinaus, dann die angehaltene Automation neu starten. Solange das läuft,
        // bekommen die vnavmesh-Automationen unten KEIN Update (würden sonst gegenlenken).
        var exitingNoFlyArea = plugin.NoFlyAreaExit.Update(new (Func<bool> IsActive, Action Restart)[]
        {
            (() => plugin.AetheryteAutomation.IsActive, () => { plugin.AetheryteAutomation.Stop(); plugin.AetheryteAutomation.Start(); }),
            (() => plugin.HuntingLogAutomation.IsActive, () => { plugin.HuntingLogAutomation.Stop(); plugin.HuntingLogAutomation.Start(); }),
            (() => plugin.AetherCurrentAutomation.IsActive, () => { plugin.AetherCurrentAutomation.Stop(); plugin.AetherCurrentAutomation.Start(); }),
            (() => plugin.SightseeingAutomation.IsActive, () => { plugin.SightseeingAutomation.Stop(); plugin.SightseeingAutomation.Start(); }),
            (() => plugin.ChocobokeepAutomation.IsActive, () => { plugin.ChocobokeepAutomation.Stop(); plugin.ChocobokeepAutomation.Start(); }),
            (() => plugin.TripleTriadAutomation.IsActive, () => { plugin.TripleTriadAutomation.Stop(); plugin.TripleTriadAutomation.Start(); }),
            (() => plugin.GoToAutomation.ActiveEntry != null, () => { if (plugin.GoToAutomation.ActiveEntry is { } goToEntry) plugin.GoToAutomation.GoTo(goToEntry); }),
        });

        // Bewusst die ganze Stadt (inkl. Kristalle aus Nachbarbezirken einer geteilten Hauptstadt,
        // siehe allForZone) - die Automation reist bei Bedarf selbst mit Lifestream zwischen den
        // Bezirken hin und her (siehe AetheryteAutomation.cs).
        var missingAetherytesCity = allForZone
            .Where(e => e.Type == CollectibleType.Aetheryte && (config.SimulateAetheryteAutomation || !plugin.IsOwned(e)))
            .ToList();
        if (!exitingNoFlyArea)
            plugin.AetheryteAutomation.Update(missingAetherytesCity);

        // Bewusst NICHT stadtweit wie Aetheryten/Quests - Hunting-Log-Monster gibt es nur in genau
        // dieser einen Zone (siehe Plugin.GetHuntingLogEntries), kein Bezirkswechsel nötig/möglich.
        var missingHuntingLogInZone = allForZone
            .Where(e => e.Type == CollectibleType.HuntingLog && !Plugin.IsAchievementOrRankGated(e)) // Kills zählen erst ab erreichter Rang-Stufe
            .ToList();
        if (!exitingNoFlyArea)
            plugin.HuntingLogAutomation.Update(missingHuntingLogInZone);

        // Wie Hunting Log bewusst NICHT stadtweit - Ätherströmungen kommen aus aethercurrents.json
        // mit exakter Zonen-Zuordnung, kein Bezirkswechsel nötig. !IsAchievementOrRankGated schließt
        // Strömungen mit noch fehlender Voraussetzungs-Quest (z.B. CollectibleEntry.RequiredQuest)
        // aus - Nutzer-Report: "wählt ihn aus, interagiert aber nicht" bei "Matoya's Cave vicinity",
        // das native Objekt lässt sich ohne die Quest offenbar gar nicht anvisieren/interagieren.
        var missingAetherCurrentsInZone = allForZone
            .Where(e => e.Type == CollectibleType.AetherCurrent && (config.SimulateAetherCurrentAutomation || !plugin.IsOwned(e)) && !Plugin.IsAchievementOrRankGated(e))
            .ToList();
        if (!exitingNoFlyArea)
            plugin.AetherCurrentAutomation.Update(missingAetherCurrentsInZone);

        // Ebenfalls nicht stadtweit - Sightseeing-Punkte kommen aus GetLiveZoneEntries mit exakter
        // Zonen-Zuordnung (siehe Plugin.ComputeLiveZoneEntries). Bewusst NICHT aus "allForZone" (das
        // würde bei deaktiviertem "Alle Gegenstände anzeigen" gerade durch Wetter/Uhrzeit/Buch-
        // Freischaltung gesperrte Punkte schon vor diesem Filter hier verlieren) - stattdessen direkt
        // aus GetLiveZoneEntries, damit SimulateSightseeingAutomation (siehe Configuration) unabhängig
        // von diesem Anzeige-Schalter zum Testen auch gesperrte Punkte anlaufen kann. Bewusst inkl.
        // siblingTerritories (geteilte Hauptstädte, z.B. "Barracuda Piers" in den Limsa Upper Decks,
        // während man selbst in den Lower Decks steht) - SightseeingAutomation reist bei Bedarf selbst
        // per Lifestream über den nächsten freigeschalteten Aetheryten in den Zielbezirk, genau wie
        // AetheryteAutomation/GoToAutomation (siehe SightseeingAutomation.TryTravelToDistrict).
        // IsSightseeingUnsupportedByAutomation und "kein Fliegen freigeschaltet" IMMER ausgeschlossen
        // (auch im Simulation-Modus, der die Gate-Prüfung darunter sonst bewusst umgeht) - echte
        // Jumping Puzzles ("The Carline Canopy", "The Leatherworkers' Guild"), die dauerhaft nur
        // manuell aufsuchbar sind (siehe Plugin.SightseeingUnsupportedByAutomation-Kommentar), bzw.
        // Fliegen als harte Voraussetzung fürs gesamte Feature (explizite Nutzeranforderung) - ohne
        // Fliegen kann vnavmesh die Punkte ohnehin nicht zuverlässig erreichen.
        var missingSightseeingInZone = plugin.GetLiveZoneEntries(effectiveTerritoryId)
            .Where(e => e.Type == CollectibleType.Sightseeing && siblingTerritories.Contains(e.TerritoryTypeId))
            .Where(e => !Plugin.IsBlacklisted(e)) // nicht aus allForZone abgeleitet, daher hier eigens
            // Ausnahme Simulation: Punkte mit hinterlegtem Jumping Puzzle (Plugin.SightseeingJumpingPuzzles)
            // dürfen dort trotz "nicht unterstützt" angelaufen werden - zum Testen des Sprung-Ablaufs.
            .Where(e => (!Plugin.IsSightseeingUnsupportedByAutomation(e.Id)
                         || (config.SimulateSightseeingAutomation && Plugin.TryGetSightseeingJumpingPuzzle(e.Id, out _)))
                        && !Plugin.IsSightseeingBlockedByFlying(e))
            // Bewusst NICHT IsAchievementOrRankGated (das lehnt sich an ComputeGrandCompanyOrTribeGateReason
            // an, welches für Sightseeing-Punkte AUCH Wetter/Uhrzeit als "Gate-Grund" zählt und damit einen
            // nur deswegen gerade blockierten Punkt hier fälschlich komplett ausgeschlossen hätte, noch
            // bevor hasActionableSightseeing unten ihn überhaupt sehen konnte - IsSightseeingBookAccessible
            // prüft exakt dieselben ÜBERGEORDNETEN Voraussetzungen (Log/Fliegen/erste 20/Gate-Quest) OHNE
            // Wetter/Uhrzeit.
            .Where(e => config.SimulateSightseeingAutomation || (!plugin.IsOwned(e) && Plugin.IsSightseeingBookAccessible(e)))
            .ToList();
        // Punkte, die NUR wegen Wetter/Uhrzeit gerade nicht gehen (siehe
        // Plugin.IsSightseeingOnlyTemporarilyUnavailable) - solange davon noch welche übrig sind, wartet
        // die Automation darauf (AFK-Modus), statt sich zu beenden.
        var pendingSightseeingInZone = plugin.GetLiveZoneEntries(effectiveTerritoryId)
            .Where(e => e.Type == CollectibleType.Sightseeing && siblingTerritories.Contains(e.TerritoryTypeId))
            .Where(e => !Plugin.IsBlacklisted(e) && !plugin.IsOwned(e) && Plugin.IsSightseeingOnlyTemporarilyUnavailable(e))
            .ToList();
        if (!exitingNoFlyArea)
            plugin.SightseeingAutomation.Update(missingSightseeingInZone, pendingSightseeingInZone);

        // Was tatsächlich im Overlay auftaucht (siehe "allForZone", inkl. dessen "Alle Gegenstände
        // anzeigen"-Schalter) - bewusst getrennt von missingSightseeingInZone oben, das für die
        // Automation extra ungefiltert ist. Nur wenn hier NICHTS mehr übrig ist, soll der Knopf ganz
        // verschwinden (siehe hasVisibleSightseeing unten); sind noch mit "Bedingung nicht erfüllt"
        // markierte Punkte sichtbar, bleibt er stehen und wird nur ausgegraut.
        var visibleSightseeingInZone = allForZone
            .Where(e => e.Type == CollectibleType.Sightseeing && !plugin.IsOwned(e))
            .ToList();

        // Ebenfalls nicht stadtweit - Chocobokeep-Standorte kommen aus GetChocobokeepEntries mit
        // exakter Zonen-Zuordnung, kein Bezirkswechsel nötig.
        var missingChocobokeepsInZone = allForZone
            .Where(e => e.Type == CollectibleType.Chocobokeep && (config.SimulateChocobokeepAutomation || !plugin.IsOwned(e)))
            .ToList();
        if (!exitingNoFlyArea)
            plugin.ChocobokeepAutomation.Update(missingChocobokeepsInZone);

        // Triple-Triad-NPC-Gegner (siehe Plugin.GetTripleTriadNpcEntries) - nur tatsächlich
        // erreichbare Karten (keine "Bedingung nicht erfüllt", Blacklist ist über allForZone schon raus).
        var missingNpcCardsInZone = allForZone
            .Where(e => e.Type == CollectibleType.TripleTriadCard && e.Category == Plugin.TripleTriadNpcCategory && e.EventNpcId != 0)
            .Where(e => !plugin.IsOwned(e) && !Plugin.IsAchievementOrRankGated(e))
            .ToList();
        if (!exitingNoFlyArea)
            plugin.TripleTriadAutomation.Update(missingNpcCardsInZone);

        // Unabhängig von den Automationen oben - das "Hinlaufen"-Icon (siehe DrawClickableName)
        // betrifft immer nur einen einzelnen Eintrag, egal ob gerade eine Automation läuft.
        if (!exitingNoFlyArea)
            plugin.GoToAutomation.Update();

        // Sobald ein per Auto-Knopf im ToDo-Tab angestoßener Zonenwechsel abgeschlossen ist
        // (GoToAutomation wieder ohne aktiven Auftrag - siehe StartToDoAutomation), jetzt die
        // eigentliche Automation starten. ActiveEntry wird sowohl bei erfolgreicher Ankunft als
        // auch bei Abbruch/Fehler null - im Fehlerfall findet der Automations-Start dann einfach
        // (noch) nichts zu tun und bleibt untätig, bis der Nutzer es erneut versucht.
        if (pendingCrossZoneAutomationStart != null && plugin.GoToAutomation.ActiveEntry == null)
        {
            var start = pendingCrossZoneAutomationStart;
            pendingCrossZoneAutomationStart = null;
            start();
        }

        // "Unterstützt" heißt hier: noch nicht als von Questionable abgelehnt bekannt (siehe
        // QuestAutomation.IsKnownUnsupported) - erst nach einem Versuch bekannt, siehe dort.
        var hasActionableQuests = missingQuests.Any(q => !plugin.QuestAutomation.IsKnownUnsupported(q.Id));
        var hasActionableAetherytes = missingAetherytesCity.Count > 0;
        var hasActionableHuntingLog = missingHuntingLogInZone.Any(e => e.WorldPosition.HasValue);
        // Ohne die abgeschlossene Quest "Divine Intervention" (siehe Plugin.ComputeGrandCompanyOrTribeGateReason)
        // sind ALLE Ätherströmungen gesperrt - der Automations-Knopf soll dann ausgegraut bleiben statt
        // sinnlos loszulaufen (Nutzeranforderung).
        var hasActionableAetherCurrents = missingAetherCurrentsInZone.Any(e => e.HasGoToTarget && !Plugin.IsAchievementOrRankGated(e));
        // hasVisibleSightseeing entscheidet nur, ob der Knopf überhaupt gezeichnet wird (siehe
        // DrawAutomationButtonIfNeeded) - hasActionableSightseeing entscheidet zusätzlich, ob er dabei
        // ausgegraut ist. Im Simulation-Modus (Configuration.SimulateSightseeingAutomation) zählen
        // bewusst auch schon BESESSENE Punkte (nicht nur visibleSightseeingInZone, das die nur zum
        // Testen ausblendet) - sonst verschwindet der Knopf dort, sobald alle Punkte der Zone bereits
        // abgeschlossen sind, obwohl der Simulation-Modus ja gerade dafür da ist, genau solche Punkte
        // erneut anzulaufen.
        var hasVisibleSightseeing = (config.SimulateSightseeingAutomation
                ? allForZone.Any(e => e.Type == CollectibleType.Sightseeing && e.HasGoToTarget)
                : visibleSightseeingInZone.Any(e => e.HasGoToTarget))
            && Plugin.IsSightseeingLogUnlocked();
        // Bewusst NICHT nach Wetter/Uhrzeit gefiltert (Nutzeranforderung: "nicht ausgrauen, wenn
        // mindestens 1 Eintrag mit condition not met aufgrund Wetter oder Zeit ist") - ein Punkt, der
        // NUR deswegen gerade nicht geht, zählt weiterhin als aktionierbar, die Automation wartet
        // dann einfach ab (siehe pendingSightseeingInZone/AFK-Modus oben). IsSightseeingBookAccessible
        // prüft dagegen alle ÜBERGEORDNETEN Voraussetzungen (Log/Fliegen/erste 20/Quest-Freischaltung
        // des Buchs) - fehlt eine davon, bleibt der Knopf wie bisher ausgegraut.
        var hasActionableSightseeing = missingSightseeingInZone.Any(e => e.HasGoToTarget && Plugin.IsSightseeingBookAccessible(e));
        var hasActionableChocobokeeps = missingChocobokeepsInZone.Any(e => e.HasGoToTarget);
        var hasActionableTripleTriad = missingNpcCardsInZone.Count > 0;

        // Siehe Configuration.HideOverlayWhenEmpty-Kommentar - erst NACH allen Automation.Update()-
        // Aufrufen oben geprüft (die laufen immer weiter, unabhängig von der Sichtbarkeit), aber
        // VOR jeglichem Zeichnen (auch vor dem Kopfbereich) - schrumpft das Fenster auf 0x0 und
        // überspringt den Rest von DrawContent komplett, für echte Unsichtbarkeit statt nur einer
        // leeren Kopfzeile wie bei "collapsed".
        if (config.HideOverlayWhenEmpty && entries.Count == 0)
        {
            hiddenDueToEmptyLastFrame = true;
            ImGui.SetWindowSize(Vector2.Zero, ImGuiCond.Always);
            return;
        }

        hiddenDueToEmptyLastFrame = false;

        // Titel + Schloss-/Einklapp-/Schließen-Knopf in einer Gruppe - so lässt sich ihre
        // tatsächliche Höhe direkt danach per ImGui.GetItemRectSize() messen (siehe collapsed unten),
        // ohne sie an eine feste, skalierungsabhängige Pixelzahl zu koppeln.
        ImGui.BeginGroup();

        OutlineText("The Explorer's Codex", TitleColor);
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemClicked())
            plugin.OpenOptions();

        // Alle drei Knöpfe gleich groß und direkt nebeneinander ganz am rechten Rand - die reine
        // Frame-Höhe war schmaler als die tatsächlichen Icon-Glyphen (Schloss/Times), wodurch beide
        // in ihrem eigenen Knopf beschnitten wirkten. Größe daher an der breiteren der Icon-Glyphen
        // ausgerichtet, plus ein kleiner rechter Rand, damit "x" nicht am Fensterrand klebt.
        float topRightIconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            topRightIconWidth = MathF.Max(
                ImGui.CalcTextSize(FontAwesomeIcon.Lock.ToIconString()).X,
                MathF.Max(
                    ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X,
                    ImGui.CalcTextSize(FontAwesomeIcon.ChevronUp.ToIconString()).X));
        }

        var topRightButtonSize = MathF.Max(ImGui.GetFrameHeight(), topRightIconWidth + ImGui.GetStyle().FramePadding.X * 2f);
        const float TopRightMargin = 4f;

        if (DrawLockButtonTopRight(config.CompactLocked, topRightButtonSize, TopRightMargin))
        {
            config.CompactLocked = !config.CompactLocked;
            config.Save();
        }

        if (DrawCollapseButtonTopRight(collapsed, topRightButtonSize, TopRightMargin))
            collapsed = !collapsed;

        if (DrawCloseButtonTopRight(topRightButtonSize, TopRightMargin))
        {
            IsOpen = false;
            config.ShowCompactOverlay = false;
            config.Save();
        }

        ImGui.EndGroup();

        if (EffectiveCollapsed())
        {
            // Fenster auf genau die Höhe der eben gezeichneten Kopfzeile (plus das obere/untere
            // Innenpolster, siehe PreDraw) schrumpfen - erst jetzt (nach dem Zeichnen) bekannt, siehe
            // Kommentar bei DrawContent-Aufruf/PreDraw. ImGuiCond.Always wirkt hier sofort, auch
            // innerhalb desselben Begin()/End(), nicht erst nächsten Frame. Greift auch beim
            // AUTOMATISCHEN Einklappen (überlappendes natives Fenster, siehe
            // autoCollapsedForNativeOverlap) - der Spieler sieht dann kurz nur die Kopfzeile, bis das
            // native Fenster wieder weg ist.
            var windowPaddingY = ImGui.GetStyle().WindowPadding.Y;
            var neededHeight = ImGui.GetItemRectSize().Y + windowPaddingY * 2f;
            ImGui.SetWindowSize(new Vector2(ImGui.GetWindowSize().X, neededHeight), ImGuiCond.Always);
            return;
        }

        // Ab hier zwei Tabs (Nutzeranforderung): Tab 1 = das bisherige Overlay (Zone/Automations-
        // Knöpfe/Status/Filter/Währungen/Liste dieser Zone), Tab 2 = die zonenunabhängige
        // ToDo-Liste (siehe Plugin.ToDoList/DrawToDoTabContent) - beide teilen sich dieselbe
        // Zeilen-Darstellung (siehe DrawEntryRow), damit die ToDo-Liste "genauso aufgebaut" aussieht.
        // Die Tab-Knöpfe selbst stehen bewusst GANZ OBEN (noch vor Zone/Automations-Knöpfen) - Zone
        // und Automations-Knöpfe beziehen sich nur auf Tab 1 und werden daher jetzt auch nur dort
        // gezeichnet (siehe unten), nicht mehr immer. Die Automationen selbst laufen unabhängig
        // davon immer weiter, welcher Tab gerade sichtbar ist (siehe .Update-Aufrufe oben).
        //
        // Bewusst KEINE ImGui.BeginTabBar/BeginTabItem (Standard-ImGui-Tabs) - deren Chrome (volle
        // Trennlinie über die ganze Fensterbreite, unlackierter Text ohne Schatten) passt nicht zum
        // Rest dieses Overlays, das komplett auf transparentem Hintergrund lebt und daher überall
        // Schattentext (OutlineText) und farbige Pillen-Knöpfe verwendet (siehe die
        // Automations-Knopfreihe unten). Stattdessen zwei selbstgezeichnete Pillen-Knöpfe im selben
        // Look, siehe DrawCompactTabButton.
        plugin.CleanUpToDoList();

        var todoCount = config.ToDoList.Count;
        DrawCompactTabButton(Loc.T("Overlay", "Overlay") + "##CompactTabOverlay", activeTab == CompactOverlayTab.Overlay, CompactOverlayTab.Overlay);
        ImGui.SameLine(0f, 6f);
        DrawCompactTabButton($"{Loc.T("ToDo-Liste", "ToDo list")} ({todoCount})##CompactTabToDo", activeTab == CompactOverlayTab.ToDo, CompactOverlayTab.ToDo);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (activeTab == CompactOverlayTab.Overlay)
        {
            OutlineText($"{Plugin.GetZoneName(currentTerritoryId)} ({currentTerritoryId})", MutedColor);

            if (config.ShowAutomationButtons)
            {
                DrawAutomationButtonsRow(
                    hasActionableQuests, () => plugin.QuestAutomation.Start(effectiveTerritoryId),
                    hasActionableAetherytes, () => plugin.AetheryteAutomation.Start(),
                    hasActionableHuntingLog, () => plugin.HuntingLogAutomation.Start(),
                    hasActionableAetherCurrents, () => plugin.AetherCurrentAutomation.Start(),
                    hasVisibleSightseeing, hasActionableSightseeing, () => plugin.SightseeingAutomation.Start(),
                    hasActionableChocobokeeps, () => plugin.ChocobokeepAutomation.Start(),
                    hasActionableTripleTriad, () => plugin.TripleTriadAutomation.Start());
            }

            DrawOverlayTabContent(config, entries, allForZone, afterTypeFilter);
        }
        else
        {
            DrawToDoTabContent(config);
        }
    }

    /// <summary>
    /// Die Automations-Knopfreihe (Auto Quest/Aetheryte/Hunting Log/...) - im Overlay-Tab für die
    /// ganze aktuelle Zone (config.ShowAutomationButtons-gated, siehe DrawContent, onStart startet
    /// dort sofort in der aktuellen Zone), im ToDo-Tab dieselben Knöpfe, aber sichtbar/aktiviert für
    /// JEDE Zone, in der die ToDo-Liste etwas Erreichbares hat (siehe DrawToDoAutomationButtonsRow) -
    /// deren onStart teleportiert dafür bei Bedarf erst in die passende Zone (siehe
    /// StartToDoAutomation), bevor die eigentliche Automation gestartet wird.
    /// </summary>
    private void DrawAutomationButtonsRow(
        bool hasActionableQuests, Action questOnStart,
        bool hasActionableAetherytes, Action aetheryteOnStart,
        bool hasActionableHuntingLog, Action huntingLogOnStart,
        bool hasActionableAetherCurrents, Action aetherCurrentOnStart,
        bool hasVisibleSightseeing, bool hasActionableSightseeing, Action sightseeingOnStart,
        bool hasActionableChocobokeeps, Action chocobokeepOnStart,
        bool hasActionableTripleTriad, Action tripleTriadOnStart)
    {
        // Reihe der Automations-Knöpfe bricht bei Bedarf selbst in eine zweite Zeile um (statt über
        // den Fensterrand hinauszulaufen), wenn das kompakte Fenster nicht breit genug gezogen
        // wurde - jeder Knopf entscheidet VOR dem eigentlichen Zeichnen anhand seiner (aus dem
        // Label vorab berechneten) Breite, ob er noch auf die aktuelle Zeile passt.
        var automationRowContentMaxX = ImGui.GetWindowContentRegionMax().X;
        var automationStopLabel = Loc.T("Automation stoppen", "Stop automation");
        var isFirstAutomationButtonOnRow = true;

        // Ein Knopf wird komplett ausgeblendet (statt nur ausgegraut), sobald es nichts (mehr) für
        // ihn zu tun gibt - UND er nicht gerade selbst läuft (läuft er schon, muss "Automation
        // stoppen" klickbar sichtbar bleiben, auch falls die Liste inzwischen leer aussieht). Ein
        // fehlendes Fremdplugin blendet bewusst NICHT aus - das bleibt ausgegraut mit erklärendem
        // Tooltip sichtbar, siehe MissingPluginTooltip in den einzelnen DrawXAutomationButton-Methoden.
        void DrawAutomationButtonIfNeeded(bool isActive, bool hasActionable, string startLabel, Action draw)
        {
            if (!isActive && !hasActionable)
                return;

            if (!isFirstAutomationButtonOnRow)
            {
                var label = isActive ? automationStopLabel : startLabel;
                var width = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f;
                ImGui.SameLine();
                if (ImGui.GetCursorPosX() + width > automationRowContentMaxX)
                    ImGui.NewLine();
            }
            isFirstAutomationButtonOnRow = false;
            draw();
        }

        DrawAutomationButtonIfNeeded(plugin.QuestAutomation.IsActive, hasActionableQuests,
            Loc.T("Auto Quest", "Auto Quest"), () => DrawQuestAutomationButton(hasActionableQuests, questOnStart));
        DrawAutomationButtonIfNeeded(plugin.AetheryteAutomation.IsActive, hasActionableAetherytes,
            Loc.T("Auto Aetheryte", "Auto Aetheryte"), () => DrawAetheryteAutomationButton(hasActionableAetherytes, aetheryteOnStart));
        DrawAutomationButtonIfNeeded(plugin.HuntingLogAutomation.IsActive, hasActionableHuntingLog,
            Loc.T("Auto Hunting Log", "Auto Hunting Log"), () => DrawHuntingLogAutomationButton(hasActionableHuntingLog, huntingLogOnStart));
        DrawAutomationButtonIfNeeded(plugin.AetherCurrentAutomation.IsActive, hasActionableAetherCurrents,
            Loc.T("Auto Ätherströmung", "Auto Aether Current"), () => DrawAetherCurrentAutomationButton(hasActionableAetherCurrents, aetherCurrentOnStart));
        DrawAutomationButtonIfNeeded(plugin.SightseeingAutomation.IsActive, hasVisibleSightseeing,
            Loc.T("Auto Sightseeing", "Auto Sightseeing"), () => DrawSightseeingAutomationButton(hasActionableSightseeing, sightseeingOnStart));
        DrawAutomationButtonIfNeeded(plugin.ChocobokeepAutomation.IsActive, hasActionableChocobokeeps,
            Loc.T("Auto Chocobokeep", "Auto Chocobokeep"), () => DrawChocobokeepAutomationButton(hasActionableChocobokeeps, chocobokeepOnStart));
        DrawAutomationButtonIfNeeded(plugin.TripleTriadAutomation.IsActive, hasActionableTripleTriad,
            Loc.T("Auto Triple Triad", "Auto Triple Triad"), () => DrawTripleTriadAutomationButton(hasActionableTripleTriad, tripleTriadOnStart));

        if (!isFirstAutomationButtonOnRow)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    // Gesetzt, sobald ein Automations-Knopf im ToDo-Tab einen Zonenwechsel anstößt (siehe
    // StartToDoAutomation) - die eigentliche Automation wird erst gestartet, sobald GoToAutomation
    // dort wieder inaktiv ist (siehe Update-Aufruf in DrawContent), da z.B. QuestAutomation.Start
    // die AKTUELLE Zone als "Heimatzone" festhält - ein Start VOR der Ankunft würde also die falsche
    // (Ausgangs-)Zone festhalten statt der Zielzone.
    private Action? pendingCrossZoneAutomationStart;

    /// <summary>
    /// Startet eine Automation für einen ToDo-Listen-Eintrag (siehe DrawToDoAutomationButtonsRow) -
    /// steht der Eintrag schon in der aktuellen Zone, sofort; andernfalls erst per GoToAutomation
    /// (dieselbe Logik wie das einzelne "Hinlaufen"-Icon, siehe DrawGoToColumn) in die Zielzone
    /// reisen und den eigentlichen Start zurückstellen, bis die Reise fertig ist.
    /// </summary>
    private void StartToDoAutomation(CollectibleEntry target, Action startNow)
    {
        var currentZone = Plugin.ResolveEffectiveTerritoryId(Plugin.ClientState.TerritoryType);
        var targetZone = target.FlagTerritoryTypeId ?? target.TerritoryTypeId;
        if (targetZone == currentZone)
        {
            startNow();
        }
        else
        {
            plugin.GoToAutomation.GoTo(target);
            pendingCrossZoneAutomationStart = startNow;
        }
    }

    private enum CompactOverlayTab
    {
        Overlay,
        ToDo,
    }

    // Welcher der beiden Tabs (siehe DrawCompactTabButton) gerade sichtbar ist - bewusst kein
    // Configuration-Feld, nur eine Sitzungs-Ansicht (wie showCurrencyCostMode).
    private CompactOverlayTab activeTab = CompactOverlayTab.Overlay;

    /// <summary>
    /// Selbstgezeichneter Tab-Knopf im selben Pillen-Look wie die Automations-Knöpfe (siehe
    /// PushAutomationButtonColors) statt eines nativen ImGui-Tabs - fügt sich dadurch nahtlos ins
    /// übrige, komplett auf Schattentext/Farbpillen aufgebaute Overlay-Design ein. Ausgewählt = kräftig
    /// gefüllt in TitleColor (derselbe Blauton wie der Fenstertitel/Zähler), nicht ausgewählt =
    /// dezente, fast unsichtbare Füllung, damit sie sich klar vom ausgewählten Tab abhebt, aber auf
    /// transparentem Hintergrund nicht wie ein grauer Klotz wirkt.
    /// </summary>
    private void DrawCompactTabButton(string label, bool selected, CompactOverlayTab tab)
    {
        var size = new Vector2(ImGui.CalcTextSize(label.Split("##")[0]).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(size))
        {
            ImGui.Dummy(size);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Button, selected ? new Vector4(TitleColor.X, TitleColor.Y, TitleColor.Z, 0.9f) : new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? new Vector4(TitleColor.X, TitleColor.Y, TitleColor.Z, 1f) : new Vector4(1f, 1f, 1f, 0.14f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(TitleColor.X, TitleColor.Y, TitleColor.Z, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? new Vector4(0.03f, 0.05f, 0.08f, 1f) : MutedColor);

        if (ImGui.Button(label, size) && !selected)
            activeTab = tab;

        ImGui.PopStyleColor(4);
    }

    /// <summary>
    /// Status-Text jeder Automation (z.B. "Bearbeite: ...", "Teleportiere nach ...") - gemeinsam von
    /// Overlay- und ToDo-Tab genutzt, da eine Automation (siehe QuestAutomation.restrictToQuestIds)
    /// auch im ToDo-Tab gestartet werden kann und ihr Status dort genauso sichtbar sein soll.
    /// </summary>
    private void DrawAutomationStatusTexts()
    {
        if (plugin.QuestAutomation.ShouldShowStatusText)
            OutlineText(plugin.QuestAutomation.StatusText, plugin.QuestAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.AetheryteAutomation.ShouldShowStatusText)
            OutlineText(plugin.AetheryteAutomation.StatusText, plugin.AetheryteAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.HuntingLogAutomation.ShouldShowStatusText)
            OutlineText(plugin.HuntingLogAutomation.StatusText, plugin.HuntingLogAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.AetherCurrentAutomation.ShouldShowStatusText)
            OutlineText(plugin.AetherCurrentAutomation.StatusText, plugin.AetherCurrentAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.SightseeingAutomation.ShouldShowStatusText)
            OutlineText(plugin.SightseeingAutomation.StatusText, plugin.SightseeingAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.ChocobokeepAutomation.ShouldShowStatusText)
            OutlineText(plugin.ChocobokeepAutomation.StatusText, plugin.ChocobokeepAutomation.IsActive ? AffordableColor : VendorLinkColor);

        if (plugin.NoFlyAreaExit.IsBusy)
            OutlineText(plugin.NoFlyAreaExit.StatusText, AffordableColor);

        if (plugin.TripleTriadAutomation.ShouldShowStatusText)
            OutlineText(plugin.TripleTriadAutomation.StatusText, plugin.TripleTriadAutomation.IsActive ? AffordableColor : VendorLinkColor);
    }

    /// <summary>Der bisherige Overlay-Inhalt (Status/Filter/Währungen/Liste) - jetzt Tab 1, siehe DrawContent.</summary>
    private void DrawOverlayTabContent(Configuration config, List<CollectibleEntry> entries, List<CollectibleEntry> allForZone, List<CollectibleEntry> afterTypeFilter)
    {
        DrawAutomationStatusTexts();

        if (config.ShowDebugInfo)
            OutlineText($"debug: zone={allForZone.Count} typefilter={afterTypeFilter.Count} missing={entries.Count}", MutedColor);

        // Zähler links, "Typen filtern" weiterhin ganz rechts an den Fensterrand - jetzt zusammen
        // auf derselben Zeile statt oben bei den Automation-Knöpfen, da sich der Filter direkt auf
        // diese Anzahl auswirkt.
        OutlineText($"[{entries.Count}]", TitleColor);

        var filterLabel = Loc.T("Typen filtern", "Filter types") + "##CompactTypeFilter";
        var filterButtonWidth = ImGui.CalcTextSize(Loc.T("Typen filtern", "Filter types")).X + ImGui.GetStyle().FramePadding.X * 2f;
        var currencyFilterLabel = Loc.T("Currencys filtern", "Filter currencies") + "##CompactCurrencyFilter";
        var currencyFilterButtonWidth = ImGui.CalcTextSize(Loc.T("Currencys filtern", "Filter currencies")).X + ImGui.GetStyle().FramePadding.X * 2f;

        // Beide Filter-Knöpfe ganz rechts an den Fensterrand, "Currencys filtern" links davon.
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - filterButtonWidth - currencyFilterButtonWidth - ImGui.GetStyle().ItemSpacing.X);
        var currencyFilterButtonSize = new Vector2(currencyFilterButtonWidth, ImGui.GetFrameHeight());
        if (IsOccluded(currencyFilterButtonSize))
            ImGui.Dummy(currencyFilterButtonSize);
        else if (ImGui.Button(currencyFilterLabel))
            ImGui.OpenPopup("CompactCurrencyFilterPopup");

        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - filterButtonWidth);
        var filterButtonSize = new Vector2(filterButtonWidth, ImGui.GetFrameHeight());
        if (IsOccluded(filterButtonSize))
            ImGui.Dummy(filterButtonSize);
        else if (ImGui.Button(filterLabel))
            ImGui.OpenPopup("CompactTypeFilterPopup");

        // Derselbe Hintergrundton wie im Optionsfenster (siehe ModernUi.PushStyle/PopupBg) - ohne
        // diesen expliziten Push würde die Popup hier stattdessen mit dem ImGui-Standardgrau statt
        // dem Rest des Plugin-Looks erscheinen.
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.10f, 0.12f, 0.17f, 0.98f));
        if (ImGui.BeginPopup("CompactCurrencyFilterPopup"))
        {
            DrawCurrencyFilterPopupContent();
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("CompactTypeFilterPopup"))
        {
            foreach (var type in config.TypeOrder)
            {
                var enabled = config.ShowType.GetValueOrDefault(type, true);
                var isNotYetPossible = !Plugin.IsTypeCurrentlyPossible(type);
                if (isNotYetPossible)
                    ImGui.PushStyleColor(ImGuiCol.Text, NotYetPossibleColor);

                if (ImGui.Checkbox($"{Loc.TypeName(type)}##CompactTypeFilterEntry", ref enabled))
                {
                    config.ShowType[type] = enabled;
                    config.Save();
                }

                if (isNotYetPossible)
                {
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(Plugin.GetTypeNotPossibleReason(type));
                }
            }

            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();

        if (entries.Count == 0)
        {
            OutlineText(Loc.T("Nichts Fehlendes in dieser Zone.", "Nothing missing in this zone."), MutedColor);
            return;
        }

        if (config.ShowCurrencyWallet)
            DrawCurrencyWallet(entries);

        DrawEntryList("##CompactEntryList", entries);
    }

    /// <summary>
    /// Tab 2: die zonenunabhängige ToDo-Liste (siehe Plugin.ToDoList) - per Rechtsklick-Menü im
    /// Overlay ODER in der Datenbank-Seite befüllt (siehe CompactOverlayWindow.DrawEntryContextMenu),
    /// wird automatisch bereinigt, sobald ein Eintrag besessen/abgeschlossen ist (siehe
    /// Plugin.CleanUpToDoList, oben in DrawContent aufgerufen). Nutzt dieselbe Zeilen-Darstellung wie
    /// das normale Overlay (siehe DrawEntryList/DrawEntryRow), damit es "genauso aufgebaut" aussieht.
    /// Zeigt außerdem dieselbe Automations-Knopfreihe wie der Overlay-Tab (siehe
    /// DrawAutomationButtonsRow) - anders als dort aber bewusst ZONENUNABHÄNGIG (Nutzeranforderung:
    /// die ToDo-Liste ist zonenübergreifend), siehe DrawToDoAutomationButtonsRow.
    /// </summary>
    private void DrawToDoTabContent(Configuration config)
    {
        DrawAutomationStatusTexts();

        var entries = plugin.ResolveToDoEntries();

        if (config.ShowAutomationButtons)
            DrawToDoAutomationButtonsRow(entries);

        if (entries.Count == 0)
        {
            OutlineText(Loc.T("ToDo-Liste ist leer - per Rechtsklick auf einen Eintrag hinzufügen.", "ToDo list is empty - right-click an entry to add it."), MutedColor);
            return;
        }

        // Dieselbe Währungsanzeige wie im Overlay-Tab (siehe DrawOverlayTabContent) - inklusive
        // Umschalt-Knopf zwischen "Deine Währungen"/"Benötigte Währung" und Retainer-Anzeige
        // (showCurrencyCostMode ist ein gemeinsames Feld für beide Tabs, siehe dessen Kommentar).
        if (plugin.Configuration.ShowCurrencyWallet)
            DrawCurrencyWallet(entries);

        DrawEntryList("##CompactToDoList", entries);
    }

    /// <summary>
    /// Automations-Knopfreihe für den ToDo-Tab (siehe DrawAutomationButtonsRow) - anders als im
    /// Overlay-Tab NICHT auf die aktuelle Zone beschränkt: ein Knopf erscheint, sobald die
    /// ToDo-Liste IRGENDWO einen erreichbaren Eintrag dieser Kategorie hat (Nutzeranforderung: die
    /// ToDo-Liste ist zonenübergreifend). Steht der gewählte Eintrag nicht in der aktuellen Zone,
    /// teleportiert der Klick zuerst dorthin (siehe StartToDoAutomation), statt den Knopf einfach
    /// auszublenden.
    /// </summary>
    private void DrawToDoAutomationButtonsRow(List<CollectibleEntry> todoEntries)
    {
        var currentZone = Plugin.ResolveEffectiveTerritoryId(Plugin.ClientState.TerritoryType);

        // Bevorzugt einen Eintrag in der AKTUELLEN Zone (dann keine Reise nötig) - sonst irgendeinen
        // anderen erreichbaren ToDo-Eintrag dieser Kategorie, egal in welcher Zone.
        CollectibleEntry? FindTarget(CollectibleType type, Func<CollectibleEntry, bool> extraFilter) =>
            todoEntries
                .Where(e => e.Type == type && !plugin.IsOwned(e) && e.HasGoToTarget && extraFilter(e))
                .OrderBy(e => (e.FlagTerritoryTypeId ?? e.TerritoryTypeId) == currentZone ? 0 : 1)
                .FirstOrDefault();

        var questTarget = FindTarget(CollectibleType.Quest, e => !plugin.QuestAutomation.IsKnownUnsupported(e.Id));
        var aetheryteTarget = FindTarget(CollectibleType.Aetheryte, _ => true);
        var huntingLogTarget = FindTarget(CollectibleType.HuntingLog, e => e.WorldPosition.HasValue);
        var aetherCurrentTarget = FindTarget(CollectibleType.AetherCurrent, _ => true);

        // Anders als die übrigen Kategorien: der Knopf soll sichtbar bleiben (nur ausgegraut, mit
        // Hinweis-Tooltip - siehe DrawSightseeingAutomationButton, identisch zum Overlay-Tab), sobald
        // überhaupt ein Sightseeing-Eintrag auf der ToDo-Liste steht, auch wenn er gerade wegen
        // Bedingung/Log-Freischaltung nicht anlaufbar ist - hasVisibleSightseeing (Sichtbarkeit) ist
        // daher bewusst NICHT an sightseeingTarget (Anlaufbarkeit) gekoppelt.
        var hasVisibleSightseeing = todoEntries.Any(e => e.Type == CollectibleType.Sightseeing && !plugin.IsOwned(e));
        var sightseeingTarget = Plugin.IsSightseeingLogUnlocked()
            ? FindTarget(CollectibleType.Sightseeing, e => !Plugin.IsAchievementOrRankGated(e))
            : null;
        var chocobokeepTarget = FindTarget(CollectibleType.Chocobokeep, _ => true);
        var tripleTriadTarget = FindTarget(CollectibleType.TripleTriadCard,
            e => e.Category == Plugin.TripleTriadNpcCategory && e.EventNpcId != 0 && !Plugin.IsAchievementOrRankGated(e));

        // Alle Quest-Ids der ToDo-Liste (nicht nur questTarget, das ist nur der erste Reiseanlauf-
        // punkt) - übergeben an QuestAutomation.Start als restrictToQuestIds, damit die Automation
        // WIRKLICH nur diese Quests abarbeitet (zonenübergreifend) und keine anderen, sonst in der
        // jeweiligen Zone zufällig auch noch fehlenden Quests mit erledigt (Nutzeranforderung).
        var todoQuestIds = todoEntries.Where(e => e.Type == CollectibleType.Quest).Select(e => e.Id).ToList();

        DrawAutomationButtonsRow(
            // effectiveTerritoryId hier bewusst ERST im Start-Callback (nicht oben als currentZone)
            // ausgewertet - der wird ggf. erst NACH einer Reise ausgeführt (siehe StartToDoAutomation/
            // pendingCrossZoneAutomationStart), currentZone wäre dann noch die inzwischen verlassene
            // Ausgangszone statt der tatsächlichen Zielzone.
            questTarget != null, () => StartToDoAutomation(questTarget!, () => plugin.QuestAutomation.Start(Plugin.ResolveEffectiveTerritoryId(Plugin.ClientState.TerritoryType), todoQuestIds)),
            aetheryteTarget != null, () => StartToDoAutomation(aetheryteTarget!, () => plugin.AetheryteAutomation.Start()),
            huntingLogTarget != null, () => StartToDoAutomation(huntingLogTarget!, () => plugin.HuntingLogAutomation.Start()),
            aetherCurrentTarget != null, () => StartToDoAutomation(aetherCurrentTarget!, () => plugin.AetherCurrentAutomation.Start()),
            hasVisibleSightseeing, sightseeingTarget != null, () => StartToDoAutomation(sightseeingTarget!, () => plugin.SightseeingAutomation.Start()),
            chocobokeepTarget != null, () => StartToDoAutomation(chocobokeepTarget!, () => plugin.ChocobokeepAutomation.Start()),
            tripleTriadTarget != null, () => StartToDoAutomation(tripleTriadTarget!, () => plugin.TripleTriadAutomation.Start()));
    }

    /// <summary>
    /// Die scrollbare Liste selbst (Scrollbar-Verdeckungs-Umgang + eine Zeile pro Eintrag, siehe
    /// DrawEntryRow) - gemeinsam von Tab 1 (Overlay) und Tab 2 (ToDo-Liste) genutzt, identisches
    /// Verhalten in beiden: die "Hinlaufen"-Spalte wird nur reserviert, wenn mindestens ein
    /// sichtbarer Eintrag tatsächlich ein Laufziel hat (siehe showGoToColumn).
    /// </summary>
    private void DrawEntryList(string childId, List<CollectibleEntry> entries)
    {
        // Die Scrollbar dieser Liste ist ein von ImGui selbst gezeichnetes Chrome-Element, nicht
        // Teil der einzelnen Zeilen oben (IsOccluded) - läge ein natives Fenster genau unter ihr,
        // würde sie trotzdem weiter sichtbar darüber gezeichnet (Dalamud/ImGui zeichnet immer über
        // dem nativen UI, siehe Plugin.GetOverlappingNativeWindowRects-Kommentar), und die
        // Überdeckungs-Illusion wäre an dieser schmalen Stelle kaputt. Deshalb vorab prüfen, ob die
        // (aus der bekannten Fenstergröße vorausberechnete) Scrollbar-Spalte überhaupt ein natives
        // Fenster überlappt, und die Scrollbar in dem Fall für diesen Frame komplett ausblenden
        // (ScrollbarSize=0) - scrollen per Mausrad bleibt dabei weiterhin möglich.
        var listMin = ImGui.GetCursorScreenPos();
        var listSize = ImGui.GetContentRegionAvail();
        var scrollbarSize = ImGui.GetStyle().ScrollbarSize;
        var scrollbarMin = new Vector2(listMin.X + listSize.X - scrollbarSize, listMin.Y);
        var scrollbarMax = listMin + listSize;
        var scrollbarOccluded = nativeOverlapRects.Any(r =>
            r.Min.X < scrollbarMax.X && r.Max.X > scrollbarMin.X && r.Min.Y < scrollbarMax.Y && r.Max.Y > scrollbarMin.Y);
        if (scrollbarOccluded)
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 0f);

        // Nur dieser Teil (die eigentliche Liste) soll scrollen - alles darüber (Titel, Knöpfe,
        // Status, Währungen) bleibt beim Scrollen fest stehen, size.Y=0 füllt dafür einfach den
        // Rest des (frei durch den Spieler skalierbaren) Fensters.
        ImGui.BeginChild(childId, new Vector2(0, 0), false);

        // Hat KEIN sichtbarer Eintrag ein Laufziel, bräuchte die "Hinlaufen"-Spalte nur Platzhalter -
        // dann gar nicht erst reservieren, statt die ganze Liste grundlos einzurücken (siehe DrawGoToColumn).
        showGoToColumn = plugin.Configuration.ShowGoToIcon && entries.Any(e => e.HasGoToTarget);

        foreach (var entry in entries)
        {
            // Liegt genau DIESE Zeile gerade unter einem nativen Fenster (siehe Draw/
            // nativeOverlapRects/IsOccluded), wird nur sie durch eine leere, gleich hohe Dummy-Zeile
            // ersetzt - der Rest der Liste bleibt normal sichtbar/klickbar, statt (wie früher) beim
            // geringsten Kontakt mit einem nativen Fenster komplett zu verschwinden.
            var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing());
            if (IsOccluded(rowSize))
            {
                ImGui.Dummy(rowSize);
                continue;
            }

            DrawEntryRow(entry);
        }

        ImGui.EndChild();

        if (scrollbarOccluded)
            ImGui.PopStyleVar();
    }

    /// <summary>Eine einzelne Zeile (Hinlaufen-Icon, Typ, Name, Preis, Bedingungs-/Wetterhinweis) - siehe DrawEntryList.</summary>
    private void DrawEntryRow(CollectibleEntry entry)
    {
        DrawGoToColumn(entry);

        var isUnsupportedQuest = entry.Type == CollectibleType.Quest && plugin.QuestAutomation.IsKnownUnsupported(entry.Id);
        // Noch nicht möglich (z.B. Sightseeing/Hunting Log ohne freigeschaltetes Fliegen in
        // dieser Zone) - Eintrag bleibt bewusst sichtbar (nicht rausgefiltert), nur ausgegraut,
        // siehe Plugin.IsTypeCurrentlyPossible.
        var isNotYetPossible = !isUnsupportedQuest && !Plugin.IsTypeCurrentlyPossible(entry.Type);
        var typeColor = isUnsupportedQuest ? UnsupportedColor : isNotYetPossible ? NotYetPossibleColor : TypeColors.GetValueOrDefault(entry.Type, NormalColor);
        OutlineText($"[{Loc.TypeName(entry.Type)}]", typeColor);
        if (isUnsupportedQuest && ImGui.IsItemHovered())
            ImGui.SetTooltip("Not supported with Questionable");
        else if (isNotYetPossible && ImGui.IsItemHovered())
            ImGui.SetTooltip(Plugin.GetTypeNotPossibleReason(entry.Type));

        ImGui.SameLine();
        DrawClickableName(entry, isNotYetPossible);

        // Quests, die beim Abschluss automatisch eine Ätherströmung mitbringen (siehe Plugin.
        // QuestGrantsAetherCurrent) - dieselbe Farbe wie der Auto-Ätherströmung-Knopf, damit der
        // Zusammenhang optisch sofort klar ist (Nutzeranforderung).
        if (entry.Type == CollectibleType.Quest && Plugin.QuestGrantsAetherCurrent(entry.Id))
        {
            ImGui.SameLine(0f, 4f);
            OutlineText("(Aether Current)", TypeColors[CollectibleType.AetherCurrent]);
        }

        if (!string.IsNullOrEmpty(entry.Currency))
        {
            ImGui.SameLine();
            OutlineText("-", MutedColor);

            var currencyAllaganToolsEligible = AllaganToolsEligibleTypes.Contains(entry.Type);
            if (IsTripleTriadNpcFight(entry))
            {
                // Kein Preis mit Icon/Menge (DrawCurrencyRequirement zeigt nur diese, den Namen
                // bloß im Tooltip) - stattdessen direkt der Name des NPC-Gegners.
                ImGui.SameLine();
                OutlineText($"NPC: {entry.Currency}", NormalColor);
            }
            else
            {
                DrawCurrencyRequirement(entry.Currency, entry.CurrencyIconId, entry.CurrencyItemId, entry.CurrencyAmount, currencyAllaganToolsEligible);
            }

            // Für die wenigen Einträge, die MEHRERE Währungen gleichzeitig verlangen (z.B.
            // Triple-Triad-Karte "G-Warrior": 1x Ruby Totem + 1x Emerald Totem + 1x Diamond
            // Totem) - jede weitere genauso wie die erste anzeigen, siehe
            // CollectibleEntry.AdditionalCurrencies.
            if (entry.AdditionalCurrencies != null)
            {
                foreach (var additional in entry.AdditionalCurrencies)
                    DrawCurrencyRequirement(additional.Currency, additional.CurrencyIconId, additional.CurrencyItemId, additional.CurrencyAmount, currencyAllaganToolsEligible);
            }
        }

        // Nur sichtbar, wenn "Alle Gegenstände anzeigen" aktiviert ist (siehe Filter weiter oben
        // in DrawContent) - bei deaktiviertem Schalter tauchen diese Einträge gar nicht erst in
        // der Liste auf, dieser Hinweis wäre dann redundant. Ausnahme: Hunting-Log-Ziele höherer
        // Rang-Stufen, die immer angezeigt werden (siehe Filter in DrawContent).
        if (Plugin.IsAchievementOrRankGated(entry))
        {
            ImGui.SameLine();

            if (Plugin.IsSightseeingBlockedByFlying(entry))
            {
                // Explizite Nutzeranforderung: das Sightseeing-Feature soll nur mit
                // freigeschaltetem Fliegen funktionieren - generisches Label, der Grund steht im
                // Tooltip. Hat Vorrang vor der Jumping-Puzzle-Sonderbehandlung unten (siehe
                // Plugin.ComputeGrandCompanyOrTribeGateReason-Reihenfolge).
                OutlineText(Loc.T("(Bedingung nicht erfüllt)", "(condition not met)"), UnsupportedColor);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Plugin.GetAchievementOrRankGateReason(entry));
            }
            else if (entry.Type == CollectibleType.Sightseeing && Plugin.IsSightseeingUnsupportedByAutomation(entry.Id) && Plugin.IsSightseeingBookAccessible(entry))
            {
                // Trotz "von der Automation nicht unterstützt" (echtes Jumping Puzzle) weiterhin
                // den tatsächlichen Wetter-/Zeit-Status zeigen - grün mit Restdauer, solange
                // gerade aktiv (man kann so einen Punkt ja manuell erreichen), sonst wie gewohnt
                // mit Countdown bis zur Verfügbarkeit. Der Text selbst bleibt IMMER
                // "(Bedingung nicht erfüllt)", unabhängig vom Wetter/Zeit-Status (der Hinweis auf
                // das Jumping Puzzle steht bereits im Hover-Tooltip, siehe unten).
                var activeLabel = Plugin.GetSightseeingActiveUntilLabel(entry);
                var isActive = !string.IsNullOrEmpty(activeLabel);
                var timerSuffix = isActive ? activeLabel : Plugin.GetSightseeingAvailabilityLabel(entry);
                var color = isActive ? AffordableColor : UnsupportedColor;

                OutlineText(
                    Loc.T($"(Aktuell nicht unterstützt{timerSuffix})", $"(currently unsupported{timerSuffix})"),
                    color);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Plugin.GetAchievementOrRankGateReason(entry));
            }
            else
            {
                // Für Sightseeing-Punkte, die gerade durch Wetter/Uhrzeit gesperrt sind, direkt im
                // Label sichtbar (nicht erst im Hover-Tooltip) - siehe GetSightseeingAvailabilityLabel.
                var availabilityLabel = Plugin.GetSightseeingAvailabilityLabel(entry);
                OutlineText(Loc.T($"(Bedingung nicht erfüllt{availabilityLabel})", $"(condition not met{availabilityLabel})"), UnsupportedColor);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Plugin.GetAchievementOrRankGateReason(entry));
            }
        }
        else
        {
            // Sightseeing-Punkte mit Wetter-/Zeitfenster-Bedingung, die GERADE aktiv sind - grün
            // mit Restdauer, bis diese Bedingung wieder kippt (siehe GetSightseeingActiveUntilLabel).
            var activeUntilLabel = Plugin.GetSightseeingActiveUntilLabel(entry);
            if (!string.IsNullOrEmpty(activeUntilLabel))
            {
                ImGui.SameLine();
                OutlineText($"({Loc.T("aktiv", "active")}{activeUntilLabel})", AffordableColor);
            }
        }
    }

    /// <summary>
    /// Zeichnet EINE Preisangabe (Icon + Menge) direkt neben dem zuletzt gezeichneten Element (per
    /// ImGui.SameLine) - für Einträge mit mehreren gleichzeitig benötigten Währungen (siehe
    /// CollectibleEntry.AdditionalCurrencies) mehrfach hintereinander aufgerufen, für den
    /// Normalfall (nur eine Währung) genau einmal.
    /// </summary>
    private void DrawCurrencyRequirement(string currencyText, uint currencyIconId, uint currencyItemId, uint currencyAmount, bool allaganToolsEligible)
    {
        ImGui.SameLine();

        // Icon + Menge zusammen in einer Gruppe, damit EIN Hover-Bereich beide abdeckt - der Name
        // der Währung (z.B. "Allied Seals") steht nur noch im Tooltip, nicht mehr permanent
        // ausgeschrieben daneben, um die Zeile kompakt zu halten.
        ImGui.BeginGroup();

        if (currencyIconId != 0)
        {
            var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(currencyIconId)).GetWrapOrEmpty();
            var size = new Vector2(ImGui.GetTextLineHeight());
            ImGui.Image(icon.Handle, size);
        }

        // CurrencyAmount 0 heißt "wird durch das Öffnen eines Packs/einer Zufallsziehung erhalten,
        // nicht in fester Anzahl gekauft" (z.B. Platinum/Dream/Imperial/...-Triad-Card-Packs) -
        // dort nur das Icon zeigen, da man vorher nicht weiß, wie viele Packs man dafür öffnen muss.
        if (currencyAmount != 0)
        {
            if (currencyIconId != 0)
                ImGui.SameLine();

            var affordable = currencyItemId != 0 && plugin.GetCurrencyAmount(currencyItemId) >= currencyAmount;
            var color = affordable ? AffordableColor : NormalColor;
            OutlineText(currencyAmount.ToString("N0"), color);
        }

        ImGui.EndGroup();

        // SHIFT + Linksklick: Allagan Tools' "Mehr Informationen"-Fenster für DIESE Währung öffnen
        // (siehe Plugin.OpenAllaganToolsItemInfo), falls aktiviert, eine Item-ID bekannt ist und der
        // BESITZENDE Eintrag zu den Item-Typen gehört (siehe AllaganToolsEligibleTypes) - Quest/
        // Sightseeing/etc. zeigen aktuell zwar ohnehin nie eine Währung, aus Konsistenzgründen aber
        // trotzdem mitgeprüft.
        var allaganToolsEnabled = allaganToolsEligible && currencyItemId != 0
                                   && plugin.Configuration.EnableAllaganToolsIntegration && Plugin.IsAllaganToolsAvailable();

        if (ImGui.IsItemHovered())
        {
            var label = GetCurrencyLabel(currencyText);
            ImGui.SetTooltip(allaganToolsEnabled
                ? $"{label}\n{Loc.T("SHIFT + Klick: Mehr Informationen (Allagan Tools)", "SHIFT + click: more information (Allagan Tools)")}"
                : label);
        }

        if (allaganToolsEnabled && ImGui.IsItemClicked() && ImGui.GetIO().KeyShift)
            Plugin.OpenAllaganToolsItemInfo(currencyItemId);
    }

    /// <summary>
    /// Zeigt entweder, wie viel der Spieler von jeder Währung besitzt, die für die aktuell
    /// angezeigten (gefilterten, bereits auf "noch nicht besessen" gefilterten - siehe
    /// DrawContent/entries) Einträge benötigt wird, oder (per showCurrencyCostMode umgeschaltet)
    /// wie viel davon INSGESAMT nötig wäre, um alle diese Einträge zu kaufen.
    /// </summary>
    private void DrawCurrencyWallet(List<CollectibleEntry> entries)
    {
        // Alle Kosten eines Eintrags, nicht nur die erste Währung - sonst fehlten zusätzlich
        // verlangte Währungen (CollectibleEntry.AdditionalCurrencies, z.B. "Uolon Horn Token" neben den
        // Irregular Tomestones beim Itinerant Moogle) komplett in dieser Liste.
        var allCosts = entries
            .SelectMany(e => new[]
                {
                    new CollectibleCurrency { Currency = e.Currency, CurrencyIconId = e.CurrencyIconId, CurrencyItemId = e.CurrencyItemId, CurrencyAmount = e.CurrencyAmount },
                }
                .Concat(e.AdditionalCurrencies ?? Enumerable.Empty<CollectibleCurrency>()))
            .Where(c => c.CurrencyItemId != 0)
            .ToList();

        var currencies = allCosts
            .GroupBy(c => c.CurrencyItemId)
            .Select(g => g.First())
            .ToList();

        if (currencies.Count == 0)
            return;

        var toggleSize = new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight());
        if (IsOccluded(toggleSize))
        {
            ImGui.Dummy(toggleSize);
        }
        else
        {
            bool toggled;
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                toggled = ImGui.Button($"{FontAwesomeIcon.ExchangeAlt.ToIconString()}##CompactCurrencyModeToggle", toggleSize);
            if (toggled)
                showCurrencyCostMode = !showCurrencyCostMode;
        }
        ImGui.SameLine();

        // X-Position, an der das Label beginnt (direkt nach dem Umschalt-Knopf) - die Währungsliste
        // darunter wird auf genau diese Spalte eingerückt (siehe SetCursorPosX unten), statt am
        // linken Fensterrand (unter dem Knopf) zu beginnen.
        var currencyIndentX = ImGui.GetCursorPosX();

        OutlineText(showCurrencyCostMode
            ? Loc.T("Benötigte Währung:", "Currency needed:")
            : Loc.T("Deine Währungen:", "Your currencies:"), MutedColor);

        // Mehrere Währungen pro Zeile statt jeweils einer eigenen - bricht (wie die Automations-
        // Knopfreihe weiter oben) selbst in eine weitere Zeile um, sobald das kompakte Fenster
        // nicht breit genug gezogen wurde. Jede Währung entscheidet VOR dem Zeichnen anhand ihrer
        // (aus Icon+Text vorab berechneten) Breite, ob sie noch auf die aktuelle Zeile passt.
        var contentMaxX = ImGui.GetWindowContentRegionMax().X;
        var iconSize = ImGui.GetTextLineHeight();
        var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        var isFirst = true;

        ImGui.SetCursorPosX(currencyIndentX);
        foreach (var sample in currencies)
        {
            var amount = showCurrencyCostMode
                ? (uint)allCosts.Where(c => c.CurrencyItemId == sample.CurrencyItemId).Sum(c => (long)c.CurrencyAmount)
                : plugin.GetCurrencyAmount(sample.CurrencyItemId);
            var label = GetCurrencyLabel(sample.Currency);
            var text = $"{amount.ToString("N0", CultureInfo.InvariantCulture)} {label}";
            var hasIcon = sample.CurrencyIconId != 0;

            // Nur im "Deine Währungen"-Modus (nicht "Benötigte Währung") - siehe Configuration.
            // ShowRetainerItemCounts-Kommentar. Leeres Dictionary (nicht null), solange Allagan
            // Tools fehlt/der Schalter aus ist - GetRetainerItemCounts prüft das selbst.
            var retainerCounts = showCurrencyCostMode
                ? EmptyRetainerCounts
                : Plugin.GetRetainerItemCounts(sample.CurrencyItemId);
            var retainerTotal = retainerCounts.Count == 0 ? 0u : (uint)retainerCounts.Values.Sum(v => (long)v);
            // Chocobo-Satteltasche mit in denselben "(<Anzahl>)"-Zusatz (Nutzeranforderung) - eigene
            // Abfrage, siehe Plugin.GetSaddlebagItemCount-Kommentar.
            var saddlebagCount = showCurrencyCostMode ? 0u : Plugin.GetSaddlebagItemCount(sample.CurrencyItemId);
            var combinedRetainerTotal = retainerTotal + saddlebagCount;
            var retainerSuffix = combinedRetainerTotal > 0 ? $" ({combinedRetainerTotal.ToString("N0", CultureInfo.InvariantCulture)})" : string.Empty;

            var itemWidth = ImGui.CalcTextSize(text + retainerSuffix).X + (hasIcon ? iconSize + itemSpacing : 0f);

            if (!isFirst)
            {
                ImGui.SameLine();
                if (ImGui.GetCursorPosX() + itemWidth > contentMaxX)
                {
                    ImGui.NewLine();
                    ImGui.SetCursorPosX(currencyIndentX);
                }
            }
            isFirst = false;

            if (IsOccluded(new Vector2(itemWidth, iconSize)))
            {
                ImGui.Dummy(new Vector2(itemWidth, iconSize));
                continue;
            }

            if (hasIcon)
            {
                var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(sample.CurrencyIconId)).GetWrapOrEmpty();
                ImGui.Image(icon.Handle, new Vector2(iconSize));
                ImGui.SameLine();
            }

            OutlineText(text, NormalColor);

            if (!string.IsNullOrEmpty(retainerSuffix))
            {
                ImGui.SameLine(0f, 0f);
                OutlineText(retainerSuffix, MutedColor);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    foreach (var kv in retainerCounts.OrderByDescending(kv => kv.Value))
                        ImGui.TextUnformatted($"{kv.Key}: {kv.Value.ToString("N0", CultureInfo.InvariantCulture)}");

                    // Satteltasche unten, per echter Trennlinie abgesetzt (Nutzeranforderung) - nicht
                    // einfach mit in die Retainer-Liste gemischt, da konzeptionell kein Retainer.
                    if (saddlebagCount > 0)
                    {
                        if (retainerCounts.Count > 0)
                            ImGui.Separator();
                        ImGui.TextUnformatted($"{Loc.T("Chocobo-Satteltasche", "Chocobo Saddlebag")}: {saddlebagCount.ToString("N0", CultureInfo.InvariantCulture)}");
                    }

                    ImGui.EndTooltip();
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    internal static string GetCurrencyLabel(string currencyText)
    {
        var t = Regex.Replace(currencyText, @"^\s*[\d,]+\s*", "");
        t = Regex.Replace(t, @"\s*\([^)]*\)\s*$", "");
        return t.Trim();
    }

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

    /// <summary>
    /// Alle Währungen (Kurzname + Icon-Id) eines Eintrags - normalerweise nur eine (die primäre,
    /// Currency/CurrencyIconId), bei mehreren gleichzeitig benötigten (siehe
    /// CollectibleEntry.AdditionalCurrencies, z.B. Triple-Triad-Karte "G-Warrior") auch die
    /// weiteren. Für den Currency-Filter (siehe DrawContent/DrawCurrencyFilterPopupContent), damit
    /// ein Eintrag bei JEDER seiner Währungen gefunden/ausgeblendet werden kann, nicht nur der ersten.
    /// Label ist bereits kanonisiert (siehe CanonicalizeCurrencyLabel), damit Singular-/Plural-
    /// Schreibvarianten derselben Währung als EINE zählen.
    /// </summary>
    /// <summary>Triple-Triad-NPC-Kampf (siehe Plugin.GetTripleTriadNpcEntries) - Currency enthält dort den Gegner-Namen statt eines Preises.</summary>
    private static bool IsTripleTriadNpcFight(CollectibleEntry entry) =>
        entry.Category == Plugin.TripleTriadNpcCategory && entry.CurrencyItemId == 0 && !string.IsNullOrEmpty(entry.Currency);

    /// <summary>Auch von <see cref="CodexOverlayWindow"/> genutzt (siehe dessen Währungsfilter-Kommentar) - dieselbe Kanonisierung sorgt dafür, dass Configuration.HiddenCurrencies in beiden Overlays dieselben Einträge meint.</summary>
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

    // Suchtext im "Currencys filtern"-Popup - bleibt über mehrere Frames erhalten (Popup öffnen,
    // tippen, wieder schließen), wird beim erneuten Öffnen bewusst NICHT zurückgesetzt.
    private string currencyFilterSearch = string.Empty;

    /// <summary>
    /// Inhalt des "Currencys filtern"-Popups - Suchfeld + Checkbox-Liste ALLER im Plugin bekannten
    /// Währungen (per GetCurrencyLabel-Kurzname, z.B. "MGP" statt "150.000 MGP"), unabhängig von der
    /// aktuellen Zone (CollectionData.GetAllEntries() ist die vollständige, einmal geladene
    /// Gesamtliste). Eine abgewählte Währung blendet ALLE Einträge mit genau dieser Währung aus dem
    /// Overlay aus (siehe afterCurrencyFilter in DrawContent), unabhängig vom Typ.
    /// </summary>
    private void DrawCurrencyFilterPopupContent()
    {
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##CompactCurrencyFilterSearch", Loc.T("Suchen...", "Search..."), ref currencyFilterSearch, 64);

        var allCurrencies = CollectionData.GetAllEntries()
            .SelectMany(GetAllCurrencies)
            .Where(c => !string.IsNullOrEmpty(c.Label))
            .GroupBy(c => c.Label)
            // Pro Label bevorzugt einen Vertreter MIT bekanntem Icon (mehrere Einträge derselben
            // Währung können unterschiedlich vollständige Icon-Daten haben) - sonst irgendeinen.
            .Select(g => (Label: g.Key, IconId: g.Select(c => c.IconId).FirstOrDefault(id => id != 0)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Where(c => string.IsNullOrEmpty(currencyFilterSearch) || c.Label.Contains(currencyFilterSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var iconSize = ImGui.GetTextLineHeight();
        ImGui.BeginChild("CompactCurrencyFilterList", new Vector2(220f, 260f));
        foreach (var (label, iconId) in allCurrencies)
        {
            if (iconId != 0)
            {
                var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                ImGui.Image(icon.Handle, new Vector2(iconSize));
                ImGui.SameLine();
            }

            var enabled = !plugin.Configuration.HiddenCurrencies.Contains(label);
            if (ImGui.Checkbox($"{label}##CompactCurrencyFilterEntry", ref enabled))
            {
                if (enabled)
                    plugin.Configuration.HiddenCurrencies.Remove(label);
                else
                    plugin.Configuration.HiddenCurrencies.Add(label);
                plugin.Configuration.Save();
            }
        }

        if (allCurrencies.Count == 0)
            OutlineText(Loc.T("Keine Treffer.", "No matches."), MutedColor);

        ImGui.EndChild();
    }

    /// <summary>
    /// Färbt einen Automation-Knopf passend zum Typ-Tag seiner Kategorie (leicht abgedunkelt, damit
    /// der Knopf nicht zu grell wirkt), oder rot, solange die Automation aktiv ist ("zum Stoppen").
    /// Muss von der Aufrufstelle immer mit ImGui.PopStyleColor(2) beendet werden.
    /// </summary>
    private static void PushAutomationButtonColors(bool isActive, Vector4 typeColor)
    {
        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.25f, 0.25f, 1f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(typeColor.X * 0.6f, typeColor.Y * 0.6f, typeColor.Z * 0.6f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(typeColor.X * 0.75f, typeColor.Y * 0.75f, typeColor.Z * 0.75f, 1f));
        }
    }

    /// <summary>
    /// Gemeinsamer Tooltip-Text für JEDEN Automations-Knopf, sobald irgendein als "Required"
    /// markiertes Plugin fehlt (siehe MainWindow.HasMissingRequiredDependency) - bewusst pauschal
    /// statt pro Knopf ein anderes konkretes Plugin zu nennen: welches Plugin eine Automation
    /// tatsächlich braucht, ist ohnehin auf der Plugins-Seite ersichtlich.
    /// </summary>
    private static string MissingPluginTooltip => Loc.T(
        "Es fehlt mindestens ein benötigtes Plugin - siehe Plugins-Seite.",
        "At least one required plugin is missing - see the Plugins page.");

    /// <summary>
    /// Gemeinsamer Tooltip-Text für JEDEN Automations-Knopf, solange man sich in einem
    /// Instanz-Inhalt befindet (siehe Plugin.IsInInstancedContent) - dort funktionieren vnavmesh/
    /// die angesteuerten Fremdplugins ohnehin nicht sinnvoll.
    /// </summary>
    private static string OtherAutomationActiveTooltip => Loc.T(
        "Es läuft bereits eine andere Automation - erst diese stoppen.",
        "Another automation is already running - stop it first.");

    /// <summary>
    /// Ob gerade eine ANDERE als die übergebene Automation läuft - es darf immer nur eine
    /// gleichzeitig laufen (sie steuern alle dieselben Fremdplugins/dieselbe Bewegung an), daher
    /// werden die Start-Knöpfe aller übrigen solange ausgegraut.
    /// </summary>
    private bool IsOtherAutomationActive(object self) =>
        (plugin.QuestAutomation.IsActive && !ReferenceEquals(self, plugin.QuestAutomation))
        || (plugin.AetheryteAutomation.IsActive && !ReferenceEquals(self, plugin.AetheryteAutomation))
        || (plugin.HuntingLogAutomation.IsActive && !ReferenceEquals(self, plugin.HuntingLogAutomation))
        || (plugin.AetherCurrentAutomation.IsActive && !ReferenceEquals(self, plugin.AetherCurrentAutomation))
        || (plugin.SightseeingAutomation.IsActive && !ReferenceEquals(self, plugin.SightseeingAutomation))
        || (plugin.ChocobokeepAutomation.IsActive && !ReferenceEquals(self, plugin.ChocobokeepAutomation))
        || (plugin.TripleTriadAutomation.IsActive && !ReferenceEquals(self, plugin.TripleTriadAutomation));

    private static string InstancedContentTooltip => Loc.T(
        "In Instanz-Inhalten (Dungeon, Trial, Raid, ...) nicht verfügbar.",
        "Not available in instanced content (dungeon, trial, raid, ...).");

    /// <summary>
    /// Knopf, der die Questionable-Automation (siehe QuestAutomation.cs) für die aktuell
    /// fehlenden Quests dieser Zone an-/ausschaltet. Ausgegraut, sobald irgendein als "Required"
    /// markiertes Plugin fehlt (nicht nur Questionable selbst) - siehe MissingPluginTooltip.
    /// </summary>
    private void DrawQuestAutomationButton(bool hasActionableQuests, Action onStart)
    {
        var automation = plugin.QuestAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Quest", "Auto Quest");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts sinnvoll zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableQuests || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.Quest]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactQuestAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMissingPlugin
                ? MissingPluginTooltip
                : inInstancedContent
                    ? InstancedContentTooltip
                    : otherAutomationActive
                        ? OtherAutomationActiveTooltip
                    : isDisabled
                        ? Loc.T("Keine von Questionable unterstützten Quests in dieser Zone.", "No quests supported by Questionable in this zone.")
                        : automation.IsActive
                        ? Loc.T("Bricht die aktuelle Quest sofort ab und stoppt die Automation.", "Immediately cancels the current quest and stops the automation.")
                        : Loc.T(
                            "Lässt Questionable nacheinander alle fehlenden Quests dieser Zone annehmen und abschließen.",
                            "Has Questionable pick up and complete all missing quests in this zone, one by one."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf, der die Aetheryten-Automation (siehe AetheryteAutomation.cs) für die aktuell
    /// fehlenden Aetheryten/Kristalle dieser Zone an-/ausschaltet. Nutzt das Fremdplugin
    /// "vnavmesh" zum Laufen - fehlt es, wird das per Tooltip erklärt statt der Knopf einfach
    /// nichts zu tun.
    /// </summary>
    private void DrawAetheryteAutomationButton(bool hasActionableAetherytes, Action onStart)
    {
        var automation = plugin.AetheryteAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Aetheryte", "Auto Aetheryte");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableAetherytes || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.Aetheryte]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactAetheryteAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMissingPlugin
                ? MissingPluginTooltip
                : inInstancedContent
                    ? InstancedContentTooltip
                    : otherAutomationActive
                        ? OtherAutomationActiveTooltip
                    : isDisabled
                        ? Loc.T("Keine fehlenden Aetheryten/Kristalle in dieser Zone.", "No missing aetherytes/crystals in this zone.")
                        : automation.IsActive
                        ? Loc.T("Bricht die Laufbewegung sofort ab und stoppt die Automation.", "Immediately stops movement and the automation.")
                        : Loc.T(
                            "Läuft mit vnavmesh nacheinander alle fehlenden Aetheryten/Kristalle ab und interagiert mit ihnen.",
                            "Uses vnavmesh to walk to and interact with all missing aetherytes/crystals, one by one."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf, der die Hunting-Log-Kill-Automation (siehe HuntingLogAutomation.cs) für die aktuell
    /// fehlenden Ziele (aktive Klasse/aktiver Rang) dieser Zone an-/ausschaltet. Braucht zum Laufen
    /// zwingend vnavmesh - fehlt es, wird das per Tooltip erklärt statt der Knopf einfach nichts zu
    /// tun. Gekämpft wird mit dem gewählten Kampf-Plugin (siehe CombatPluginBridge) - das ist über
    /// MainWindow.HasMissingRequiredDependency (Gruppe "mindestens eines") abgesichert.
    /// </summary>
    private static string CombatPluginNameForTooltip =>
        CombatPluginBridge.GetEffective() is { } kind ? CombatPluginBridge.DisplayName(kind) : "RotationSolver Reborn / Wrath Combo";

    private void DrawHuntingLogAutomationButton(bool hasActionableHuntingLog, Action onStart)
    {
        var automation = plugin.HuntingLogAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Hunting Log", "Auto Hunting Log");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableHuntingLog || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.HuntingLog]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactHuntingLogAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMissingPlugin
                ? MissingPluginTooltip
                : inInstancedContent
                    ? InstancedContentTooltip
                    : otherAutomationActive
                        ? OtherAutomationActiveTooltip
                    : isDisabled
                        ? Loc.T(
                            "Keine Hunting-Log-Ziele mit bekannter Position in dieser Zone.",
                            "No hunting log targets with a known position in this zone.")
                        : automation.IsActive
                        ? Loc.T(
                            "Stoppt die Automation - ein laufender Kampf wird noch zu Ende gebracht, statt den Charakter wehrlos stehen zu lassen.",
                            "Stops the automation - an ongoing fight is finished first instead of leaving the character defenseless.")
                        : Loc.T(
                            $"Läuft mit vnavmesh nacheinander alle fehlenden Hunting-Log-Ziele ab und tötet sie mit {CombatPluginNameForTooltip}.",
                            $"Uses vnavmesh to walk to all missing hunting log targets, one by one, and kills them with {CombatPluginNameForTooltip}."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf, der die Ätherströmungs-Automation (siehe AetherCurrentAutomation.cs) für die aktuell
    /// fehlenden Strömungen dieser Zone an-/ausschaltet. Braucht zum Laufen zwingend vnavmesh -
    /// fehlt es, wird das per Tooltip erklärt statt der Knopf einfach nichts zu tun.
    /// </summary>
    private void DrawAetherCurrentAutomationButton(bool hasActionableAetherCurrents, Action onStart)
    {
        var automation = plugin.AetherCurrentAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Ätherströmung", "Auto Aether Current");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableAetherCurrents || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.AetherCurrent]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactAetherCurrentAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMissingPlugin
                ? MissingPluginTooltip
                : inInstancedContent
                    ? InstancedContentTooltip
                    : otherAutomationActive
                        ? OtherAutomationActiveTooltip
                    : isDisabled
                        ? Plugin.IsDivineInterventionMissing()
                            ? Loc.T(
                                "Benötigt die abgeschlossene Quest \"Divine Intervention\".",
                                "Requires the completed quest \"Divine Intervention\".")
                            : Loc.T(
                                "Keine Ätherströmungen mit bekannter Position in dieser Zone.",
                                "No aether currents with a known position in this zone.")
                    : automation.IsActive
                        ? Loc.T("Bricht die Laufbewegung sofort ab und stoppt die Automation.", "Immediately stops movement and the automation.")
                        : Loc.T(
                            "Läuft mit vnavmesh nacheinander alle fehlenden Ätherströmungen ab und wartet auf die automatische Freischaltung.",
                            "Uses vnavmesh to walk to all missing aether currents, one by one, and waits for them to unlock automatically."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf, der die Sightseeing-Automation (siehe SightseeingAutomation.cs) für die aktuell
    /// fehlenden Punkte dieser Zone an-/ausschaltet. Prüfreihenfolge bewusst: erst ob das
    /// Sightseeing Log selbst überhaupt freigeschaltet ist (kein Plugin-Thema, betrifft nur ganz
    /// frische Charaktere), erst DANACH die Plugin-Verfügbarkeit (wie bei den anderen Knöpfen) -
    /// siehe Tooltip-Reihenfolge unten.
    /// </summary>
    private void DrawSightseeingAutomationButton(bool hasActionableSightseeing, Action onStart)
    {
        var automation = plugin.SightseeingAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Sightseeing", "Auto Sightseeing");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        // Kein Plugin-Thema - eigenständig VOR hasMissingPlugin geprüft (siehe Tooltip unten).
        var logUnlocked = Plugin.IsSightseeingLogUnlocked();
        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!logUnlocked || !hasActionableSightseeing || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.Sightseeing]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactSightseeingAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(!logUnlocked
                ? Loc.T("Sightseeing Log noch nicht freigeschaltet.", "Sightseeing Log not unlocked yet.")
                : hasMissingPlugin
                    ? MissingPluginTooltip
                    : inInstancedContent
                        ? InstancedContentTooltip
                        : otherAutomationActive
                            ? OtherAutomationActiveTooltip
                        : isDisabled
                            ? Loc.T(
                                "Aktuell kein Sightseeing-Punkt in dieser Zone erreichbar (keine bekannte Position, oder Fliegen/Quest-Freischaltung fehlt noch). Nur wegen Wetter/Uhrzeit gesperrte Punkte zählen weiterhin als erreichbar, die Automation wartet dann einfach ab.",
                                "No sightseeing point currently reachable in this zone (no known position, or flying/the gating quest isn't unlocked yet). Points only blocked by weather/time still count as reachable - the automation just waits them out.")
                            : automation.IsActive
                            ? Loc.T("Bricht die Laufbewegung sofort ab und stoppt die Automation.", "Immediately stops movement and the automation.")
                            : Loc.T(
                                "Läuft mit vnavmesh nacheinander alle fehlenden Sightseeing-Punkte ab und wartet auf die automatische Freischaltung.",
                                "Uses vnavmesh to walk to all missing sightseeing points, one by one, and waits for them to unlock automatically."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf, der die Chocobokeep-Automation (siehe ChocobokeepAutomation.cs) für die aktuell noch
    /// nicht besuchten Chocobokeep-Standorte dieser Zone an-/ausschaltet. Braucht zum Laufen
    /// zwingend vnavmesh - fehlt es, wird das per Tooltip erklärt statt der Knopf einfach nichts zu tun.
    /// </summary>
    private void DrawChocobokeepAutomationButton(bool hasActionableChocobokeeps, Action onStart)
    {
        var automation = plugin.ChocobokeepAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Chocobokeep", "Auto Chocobokeep");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();

        // Nur ausgrauen, wenn NICHT aktiv - läuft sie schon, muss der Knopf zum Stoppen klickbar
        // bleiben, auch falls die Liste inzwischen (kurz) leer aussieht. Fehlt ein Plugin, gibt es
        // aber unabhängig davon nichts zu starten, also trotzdem ausgrauen.
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableChocobokeeps || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.Chocobokeep]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactChocobokeepAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMissingPlugin
                ? MissingPluginTooltip
                : inInstancedContent
                    ? InstancedContentTooltip
                    : otherAutomationActive
                        ? OtherAutomationActiveTooltip
                    : isDisabled
                        ? Loc.T(
                            "Keine noch nicht besuchten Chocobokeeps in dieser Zone.",
                            "No unvisited chocobokeeps in this zone.")
                        : automation.IsActive
                        ? Loc.T("Bricht die Laufbewegung sofort ab und stoppt die Automation.", "Immediately stops movement and the automation.")
                        : Loc.T(
                            "Läuft mit vnavmesh nacheinander alle noch nicht besuchten Chocobokeeps ab und interagiert mit ihnen.",
                            "Uses vnavmesh to walk to all not-yet-visited chocobokeeps, one by one, and interacts with them."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
        {
            automation.Stop();
        }
        else if (!hasMissingPlugin)
        {
            onStart();
        }
        else
        {
            automation.MarkUnavailable();
        }
    }

    /// <summary>
    /// Knopf für die Triple-Triad-Automation (siehe TripleTriadAutomation.cs) - fliegt nacheinander
    /// alle NPC-Gegner der Zone mit noch fehlenden, erreichbaren Karten an und lässt Saucy spielen.
    /// Ausgegraut (mit eigenem Hinweis), solange Saucy nicht installiert ist.
    /// </summary>
    private void DrawTripleTriadAutomationButton(bool hasActionableTripleTriad, Action onStart)
    {
        var automation = plugin.TripleTriadAutomation;
        var label = automation.IsActive
            ? Loc.T("Automation stoppen", "Stop automation")
            : Loc.T("Auto Triple Triad", "Auto Triple Triad");

        var buttonSize = new Vector2(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f, ImGui.GetFrameHeight());
        if (IsOccluded(buttonSize))
        {
            ImGui.Dummy(buttonSize);
            return;
        }

        var saucyMissing = !TripleTriadAutomation.IsSaucyAvailable();
        var hasMissingPlugin = MainWindow.HasMissingRequiredDependency();
        var inInstancedContent = Plugin.IsInInstancedContent();
        var otherAutomationActive = IsOtherAutomationActive(automation);
        var isDisabled = !automation.IsActive && (!hasActionableTripleTriad || saucyMissing || hasMissingPlugin || inInstancedContent || otherAutomationActive);

        PushAutomationButtonColors(automation.IsActive, TypeColors[CollectibleType.TripleTriadCard]);
        if (isDisabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.Button(label + "##CompactTripleTriadAutomation");
        if (isDisabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(saucyMissing
                ? Loc.T("Saucy ist nicht installiert - wird für die Triple-Triad-Automation benötigt (siehe Plugins-Seite).", "Saucy isn't installed - it's required for the Triple Triad automation (see the Plugins page).")
                : hasMissingPlugin
                    ? MissingPluginTooltip
                    : inInstancedContent
                        ? InstancedContentTooltip
                        : otherAutomationActive
                            ? OtherAutomationActiveTooltip
                            : isDisabled
                                ? Loc.T("Keine NPC-Gegner mit noch erreichbaren Karten in dieser Zone.", "No NPC opponents with obtainable cards left in this zone.")
                                : automation.IsActive
                                    ? Loc.T("Stoppt Laufweg und Saucy sofort.", "Immediately stops movement and Saucy.")
                                    : Loc.T(
                                        "Fliegt nacheinander alle NPC-Gegner dieser Zone mit noch fehlenden Karten an, lässt Saucy spielen, bis alle Karten des Gegners gedroppt sind, und lernt sie danach.",
                                        "Flies to every NPC opponent in this zone with missing cards, lets Saucy play until all of the opponent's cards have dropped, then learns them."));
        }

        if (!clicked)
            return;

        if (automation.IsActive)
            automation.Stop();
        else if (!saucyMissing && !hasMissingPlugin)
            onStart();
    }

    // Nutzeranforderung: im Overlay nur noch den Klammerteil (z.B. "Matoya's Cave vicinity") statt
    // "Aether Current - Zone (Landmark)" oder auch nur "Zone (Landmark)" anzeigen - die Zone selbst
    // steht ohnehin schon als Gruppenüberschrift darüber, der Ortsname in der Klammer reicht zur
    // Unterscheidung. Die Daten selbst (Data/aethercurrents.json, Suche, Tooltips etc.) behalten den
    // vollen Namen, das ist rein eine Anzeige-Kürzung hier im Overlay.
    private const string AetherCurrentNamePrefix = "Aether Current - ";

    private static string GetOverlayDisplayName(CollectibleEntry entry)
    {
        if (entry.Type != CollectibleType.AetherCurrent)
            return entry.Name;

        var name = entry.Name.StartsWith(AetherCurrentNamePrefix, StringComparison.Ordinal)
            ? entry.Name[AetherCurrentNamePrefix.Length..]
            : entry.Name;

        var openParen = name.IndexOf('(');
        var closeParen = name.LastIndexOf(')');
        return openParen >= 0 && closeParen > openParen
            ? name[(openParen + 1)..closeParen]
            : name;
    }

    private void DrawClickableName(CollectibleEntry entry, bool isNotYetPossible = false)
    {
        var affordable = plugin.CanAfford(entry);
        var allaganToolsEnabled = plugin.Configuration.EnableAllaganToolsIntegration
                                   && Plugin.IsAllaganToolsAvailable()
                                   && AllaganToolsEligibleTypes.Contains(entry.Type)
                                   // Rahmen/Frisuren tragen nicht den Item-Namen - ohne auflösbares
                                   // Freischalt-Item (z.B. Rahmen per Errungenschaft) fände Allagan Tools nichts.
                                   && (entry.Type is not (CollectibleType.FrameKit or CollectibleType.Hairstyle) || Plugin.HasUnlockItem(entry));

        // Sowohl Kartenkoordinaten-Einträge (Händler/Aetheryten/Quest-NPCs) als auch Hunting-Log-
        // Monster mit bekannter Weltposition (siehe WorldPosition) bekommen denselben klickbaren
        // "Auf Karte anzeigen"-Namen - siehe Plugin.OpenEntryMap, das beide Positionsarten
        // einheitlich behandelt. Das "Hinlaufen"-Icon selbst sitzt nicht mehr hier, sondern ganz
        // vorne in der Zeile (siehe DrawGoToColumn).
        if (!entry.HasGoToTarget)
        {
            // Achievements haben zwar kein Kartenziel, aber (wie Einträge mit Verlinkung unten,
            // siehe VendorLinkColor) trotzdem einen Linksklick-Effekt - deshalb dieselbe Schriftfarbe,
            // damit man ihnen die Verlinkung genauso ansieht (Nutzeranforderung).
            var isLinked = entry.Type == CollectibleType.Achievement;
            OutlineText(GetOverlayDisplayName(entry), isNotYetPossible ? NotYetPossibleColor : affordable ? AffordableColor : isLinked ? VendorLinkColor : NormalColor);

            // Ohne Kartenziel sonst nicht interaktiv - außer für das Rechtsklick-Menü (siehe unten)
            // und, nur bei Achievements, den Linksklick unten (Nutzeranforderung).
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            // Linksklick auf einen Achievement-Eintrag öffnet das native Achievement-Fenster und
            // trägt den Namen ins Suchfeld ein, siehe Plugin.OpenAchievementWindow-Kommentar (kein
            // direkter Sprung zum Eintrag möglich, nur best-effort vorgefüllte Suche).
            if (entry.Type == CollectibleType.Achievement && ImGui.IsItemClicked())
                Plugin.OpenAchievementWindow(entry.Name);

            DrawEntryContextMenu(entry, allaganToolsEnabled);
            return;
        }

        OutlineText(GetOverlayDisplayName(entry), isNotYetPossible ? NotYetPossibleColor : affordable ? AffordableColor : VendorLinkColor);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            // Nur noch der Anbieter (Nutzeranforderung) - weder "Auf Karte anzeigen" noch der
            // Rechtsklick-Hinweis stehen hier noch im Tooltip.
            if (!string.IsNullOrEmpty(entry.Vendor))
                ImGui.SetTooltip(Loc.T($"Bei {entry.Vendor}", $"From {entry.Vendor}"));
        }

        if (ImGui.IsItemClicked())
            Plugin.OpenEntryMap(entry);

        DrawEntryContextMenu(entry, allaganToolsEnabled);
    }

    /// <summary>
    /// Rechtsklick-Menü direkt am Item-Namen (löst die frühere SHIFT + Klick / STRG + SHIFT + Klick-
    /// Tastenkombinationen ab, siehe Nutzerentscheidung: "nicht mehr mit unterschiedlichen
    /// Tastenkombinationen machen") - "Mehr Informationen" (Allagan Tools, nur falls verfügbar) und
    /// "Auf die Blacklist setzen". Muss direkt NACH dem Namen-Widget (OutlineText/ImGui-Item)
    /// aufgerufen werden, da ImGui.OpenPopupOnItemClick sich auf das zuletzt gezeichnete Item bezieht.
    /// </summary>
    internal static void DrawEntryContextMenu(CollectibleEntry entry, bool allaganToolsEnabled)
    {
        var popupId = $"##EntryMenu{entry.Type}{entry.Id}";
        ImGui.OpenPopupOnItemClick(popupId, ImGuiPopupFlags.MouseButtonRight);

        if (!ImGui.BeginPopup(popupId))
            return;

        if (allaganToolsEnabled && ImGui.Selectable(Loc.T("Mehr Informationen", "More information")))
            Plugin.OpenAllaganToolsItemInfo(entry);

        if (Plugin.IsOnToDoList(entry))
        {
            if (ImGui.Selectable(Loc.T("Von der ToDo-Liste entfernen", "Remove from ToDo list")))
                Plugin.RemoveFromToDoList(entry.Type, entry.Id);
        }
        else if (ImGui.Selectable(Loc.T("Auf die ToDo-Liste setzen", "Add to ToDo list")))
        {
            Plugin.AddToToDoList(entry);
        }

        // Abgetrennt und rot eingefärbt (Nutzeranforderung) - blendet den Eintrag komplett aus, statt
        // ihn wie die ToDo-Liste nur zu markieren, daher optisch klar von den Einträgen darüber
        // abgesetzt, damit man ihn nicht aus Versehen anklickt.
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.35f, 0.35f, 1f));
        var blacklistClicked = ImGui.Selectable(Loc.T("Auf die Blacklist setzen", "Add to blacklist"));
        ImGui.PopStyleColor();
        if (blacklistClicked)
            Plugin.AddToBlacklist(entry);

        ImGui.EndPopup();
    }

    /// <summary>
    /// Ganz vorne in jeder Zeile (vor dem [Typ]-Tag) statt des früheren Aufzählungspunkts - zeigt
    /// das "Hinlaufen"-Icon (siehe DrawGoToIcon), oder wenn keins gezeigt wird, weil DIESER Eintrag
    /// kein Laufziel hat (Einstellung aber an), einen gleich breiten Platzhalter, damit der [Typ]-Tag
    /// in jeder Zeile an derselben X-Position beginnt. Ist die Einstellung GLOBAL aus ODER hat gerade
    /// kein einziger sichtbarer Eintrag ein Laufziel (siehe showGoToColumn), wird gar keine Spalte
    /// reserviert - dann rutscht der [Typ]-Tag ganz an den Zeilenanfang, statt eine leere Lücke stehen zu lassen.
    /// </summary>
    // Siehe DrawContent - pro Frame neu bestimmt: Einstellung an UND mindestens ein Eintrag mit Laufziel.
    private bool showGoToColumn;

    private void DrawGoToColumn(CollectibleEntry entry)
    {
        if (!showGoToColumn)
            return;

        if (entry.HasGoToTarget)
        {
            DrawGoToIcon(entry);
        }
        else
        {
            float width;
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                width = ImGui.CalcTextSize(FontAwesomeIcon.Running.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2f;
            ImGui.Dummy(new Vector2(width, 1f));
        }

        ImGui.SameLine();
    }

    /// <summary>
    /// "Hinlaufen"-Icon - startet bzw. bricht per Klick GoToAutomation für GENAU DIESEN Eintrag ab
    /// (immer nur einer gleichzeitig, siehe GoToAutomation.GoTo). Ausgegraut, solange vnavmesh/
    /// Lifestream nicht beide verfügbar sind - ohne beide könnte der Klick ohnehin nicht
    /// zuverlässig ans Ziel führen.
    /// </summary>
    private void DrawGoToIcon(CollectibleEntry entry)
    {
        var automation = plugin.GoToAutomation;
        var isThisEntryActive = automation.IsNavigatingTo(entry);
        var available = automation.IsAvailable();

        bool clicked;
        bool hovered;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var icon = isThisEntryActive ? FontAwesomeIcon.StopCircle : FontAwesomeIcon.Running;
            var color = !available ? MutedColor : isThisEntryActive ? GoToActiveColor : AffordableColor;

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (!available)
                ImGui.BeginDisabled();

            // Typ mit in die ImGui-ID einbezogen, nicht nur die entry.Id - verschiedene
            // Sammelobjekt-Datenquellen (Mount/Minion/Aetheryte/Quest/HuntingLog) vergeben ihre IDs
            // unabhängig voneinander, fangen also alle wieder bei 1 an. Ohne den Typ hier hätten
            // z.B. Mount-Eintrag #1 und Hunting-Log-Ziel #1 (RowId aus MonsterNoteTarget) dieselbe
            // ImGui-ID gehabt - dadurch reagierte der Klick auf keinem der beiden mehr zuverlässig.
            clicked = ImGui.SmallButton($"{icon.ToIconString()}##GoTo{entry.Type}{entry.Id}");

            if (!available)
                ImGui.EndDisabled();
            ImGui.PopStyleColor();

            // Hover NUR hier feststellen, das eigentliche SetTooltip (siehe unten) muss außerhalb
            // dieses using-Blocks passieren: Solange die Icon-Schriftart (FontAwesome) noch aktiv
            // ist, würde der normale Tooltip-Text als Icon-Glyphen (also "komische Zeichen") statt
            // als lesbarer Text gerendert.
            hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        }

        if (hovered)
        {
            ImGui.SetTooltip(!available
                ? Loc.T("vnavmesh/Lifestream nicht gefunden - bitte installieren.", "vnavmesh/Lifestream not found - please install them.")
                : isThisEntryActive
                    ? Loc.T("Hinlaufen abbrechen", "Cancel walking there")
                    : Loc.T("Automatisch hinlaufen", "Automatically walk there"));
        }

        if (clicked && available)
        {
            if (isThisEntryActive)
                automation.Cancel();
            else
                automation.GoTo(entry);
        }
    }

    /// <summary>
    /// Aktiviert bei Bedarf eine abweichende Schrift für das Overlay (nur, wenn sie bereits
    /// geladen ist - andernfalls bleibt die reguläre Dalamud/ImGui-Schrift aktiv).
    /// </summary>
    private static System.IDisposable? PushCompactFont(Configuration config)
    {
        Dalamud.Interface.ManagedFontAtlas.IFontHandle? handle = config.CompactFontMode switch
        {
            CompactFontMode.Mono => Plugin.PluginInterface.UiBuilder.MonoFontHandle,
            CompactFontMode.Custom => CustomFontManager.GetOrCreate(config.CompactCustomFontPath),
            _ => null,
        };

        return handle is { Available: true } ? handle.Push() : null;
    }

    /// <summary>
    /// Schloss-Icon links neben dem Einklapp-Knopf - sperrt/entsperrt dieselbe Einstellung wie
    /// "Fenster sperren" im Optionsfenster (config.CompactLocked), nur direkt im Overlay erreichbar,
    /// ohne dafür extra die Optionen öffnen zu müssen. Drittes (linkestes) von drei Knöpfen ganz
    /// rechts - siehe DrawCollapseButtonTopRight/DrawCloseButtonTopRight.
    /// </summary>
    private bool DrawLockButtonTopRight(bool locked, float buttonSize, float rightMargin)
    {
        var icon = locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        var regionMaxX = ImGui.GetWindowContentRegionMax().X - rightMargin;
        ImGui.SameLine(regionMaxX - buttonSize * 3f - ImGui.GetStyle().ItemSpacing.X * 2f);

        var size = new Vector2(buttonSize, buttonSize);
        if (IsOccluded(size))
        {
            ImGui.Dummy(size);
            return false;
        }

        bool clicked;
        ImGui.PushStyleColor(ImGuiCol.Text, locked ? GoToActiveColor : AffordableColor);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.Button($"{icon.ToIconString()}##LockCompact", size);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(locked
                ? Loc.T("Fenster entsperren", "Unlock window")
                : Loc.T("Fenster sperren (Position fixieren)", "Lock window (fix position)"));
        }

        return clicked;
    }

    /// <summary>
    /// Ein-/Ausklapp-Knopf zwischen Schloss und Schließen-Knopf - blendet beim Einklappen alles
    /// außer der Kopfzeile aus (siehe DrawContent/collapsed), analog zum Optionsfenster.
    /// </summary>
    private bool DrawCollapseButtonTopRight(bool isCollapsed, float buttonSize, float rightMargin)
    {
        var icon = isCollapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp;
        var regionMaxX = ImGui.GetWindowContentRegionMax().X - rightMargin;
        ImGui.SameLine(regionMaxX - buttonSize * 2f - ImGui.GetStyle().ItemSpacing.X);

        var size = new Vector2(buttonSize, buttonSize);
        if (IsOccluded(size))
        {
            ImGui.Dummy(size);
            return false;
        }

        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.Button($"{icon.ToIconString()}##CollapseCompact", size);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCollapsed
                ? Loc.T("Ausklappen", "Expand")
                : Loc.T("Einklappen", "Collapse"));
        }

        return clicked;
    }

    private bool DrawCloseButtonTopRight(float buttonSize, float rightMargin)
    {
        var regionMaxX = ImGui.GetWindowContentRegionMax().X - rightMargin;
        ImGui.SameLine(regionMaxX - buttonSize);

        var size = new Vector2(buttonSize, buttonSize);
        if (IsOccluded(size))
        {
            ImGui.Dummy(size);
            return false;
        }

        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.Button($"{FontAwesomeIcon.Times.ToIconString()}##CloseCompact", size);

        return clicked;
    }

    private static readonly Vector4 TitleColor = new(0.55f, 0.8f, 1f, 1f);
    private static readonly Vector4 MutedColor = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Vector4 NormalColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Vector4 VendorLinkColor = new(0.5f, 0.8f, 1f, 1f);
    private static readonly Vector4 AffordableColor = new(0.55f, 0.95f, 0.55f, 1f);
    private static readonly Vector4 UnsupportedColor = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 NotYetPossibleColor = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 GoToActiveColor = new(1f, 0.65f, 0.2f, 1f);

    internal static readonly Dictionary<CollectibleType, Vector4> TypeColors = new() // internal: auch für die Blacklist-Seite im Hauptmenü
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
    // SHIFT + Linksklick (siehe DrawClickableName/DrawCurrencyRequirement). Quest/Sightseeing/
    // Aetheryte/HuntingLog/AetherCurrent/Chocobokeep sind keine Items. FrameKit/Hairstyle tragen zwar
    // nur den Namen des Rahmens/der Frisur, werden aber über Plugin.ResolveUnlockItemId auf das
    // freischaltende Item (Framer's Kit bzw. "Modern Aesthetics"-Buch) aufgelöst (siehe DrawClickableName).
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

    private static readonly Vector2[] ShadowOffsets =
    {
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
    };

    /// <summary>
    /// Zeichnet Text mit dunklem Rand, damit er bei voller Transparenz (kein Fensterhintergrund
    /// mehr) auf jedem beliebigen Ingame-Untergrund lesbar bleibt. Verhält sich wie ein normales
    /// Text-Widget (SameLine/IsItemHovered/IsItemClicked funktionieren danach wie gewohnt), da der
    /// letzte Zeichenaufruf an der eigentlichen Cursor-Position passiert. Liegt die Stelle gerade
    /// unter einem nativen Fenster (siehe IsOccluded), wird stattdessen ein gleich großer Dummy
    /// gezeichnet - IsItemHovered/IsItemClicked danach liefern dann automatisch immer false, ein
    /// verdeckter Text kann also nie mehr fälschlich als "angeklickt" gelten.
    /// </summary>
    private void OutlineText(string text, Vector4 color)
    {
        var size = ImGui.CalcTextSize(text);
        if (IsOccluded(size))
        {
            ImGui.Dummy(size);
            return;
        }

        var origin = ImGui.GetCursorPos();
        var shadow = new Vector4(0f, 0f, 0f, 0.9f);

        foreach (var offset in ShadowOffsets)
        {
            ImGui.SetCursorPos(origin + offset);
            ImGui.TextColored(shadow, text);
        }

        ImGui.SetCursorPos(origin);
        ImGui.TextColored(color, text);
    }
}
