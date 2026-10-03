using System;
using System.Numerics;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace TheExplorersCodex;

/// <summary>
/// Zentrales Farb-/Schrift-/Abstands-Theme für das neue Codex-Design (siehe
/// NewDesign/DESIGN_SPEC.md) - "dunkles Tinten-Braun mit Gold-Akzent". Alle Werte kommen 1:1 aus
/// Abschnitt 2-4 der Spezifikation; UI-Code darf keine eigenen Hex-Farben mehr hart codieren,
/// sondern verwendet ausschließlich diese Tokens (Vorgabe aus Abschnitt 1 der Spezifikation).
///
/// Bewusst eine KOMPLETT EIGENSTÄNDIGE Theme-Klasse neben dem bisherigen <see cref="Windows.ModernUi"/> -
/// das alte Design bleibt unverändert nutzbar, während das neue Design schrittweise (siehe
/// Abschnitt 8 der Spezifikation) parallel aufgebaut wird.
/// </summary>
public static class CodexTheme
{
    private static Vector4 Hex(string hex, float alpha = 1f)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
        var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
        var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
        return new Vector4(r, g, b, alpha);
    }

    // ---- Flächen (Abschnitt 2) ----
    public static readonly Vector4 BgOuter = Hex("#0B0906");
    public static readonly Vector4 BgWindow = Hex("#1A150F");
    public static readonly Vector4 BgSidebar = Hex("#130F0A");
    public static readonly Vector4 BgCard = Hex("#221C14");
    public static readonly Vector4 BgInput = Hex("#17120C");
    public static readonly Vector4 BgPopup = Hex("#1D1812");
    public static readonly Vector4 BgSelected = Hex("#2E2516");
    public static readonly Vector4 BgSelectedStrong = Hex("#3A2F1E");

    /// <summary>BgOverlay - Alpha folgt der Transparenz-Einstellung, siehe <see cref="OverlayBg"/>.</summary>
    private static readonly Vector4 BgOverlayBase = Hex("#16110B");

    /// <summary>Overlay-Hintergrund mit der aktuell eingestellten Transparenz (0 = voll deckend, 1 = unsichtbar, siehe Abschnitt 5.9/9.3).</summary>
    public static Vector4 OverlayBg(float transparency) => BgOverlayBase with { W = 0.93f * (1f - Math.Clamp(transparency, 0f, 1f)) };

    /// <summary>Abschnitt 5.9: Hintergrund für Knöpfe ohne eigene Füllung (bereit, gesperrt, Filter), sobald ab ~70% Overlay-Transparenz Textschatten aktiv sind - sonst wären diese Knöpfe auf dem fast unsichtbaren Fensterhintergrund kaum noch zu lesen.</summary>
    public static readonly Vector4 TranslucentButtonBg = BgOverlayBase with { W = 0.7f };

    // ---- Linien (Abschnitt 2) ----
    public static readonly Vector4 LineFrame = Hex("#6A5638");
    public static readonly Vector4 LineControl = Hex("#5A4A33");
    public static readonly Vector4 LineCard = Hex("#43372A");
    public static readonly Vector4 LineSubtle = Hex("#362C1F");
    public static readonly Vector4 LineRow = Hex("#2A2219");
    public static readonly Vector4 LineDisabled = Hex("#3E3324");

    // ---- Text (Abschnitt 2) ----
    public static readonly Vector4 TextHeading = Hex("#F6E9C8");
    public static readonly Vector4 TextPrimary = Hex("#F1E6CF");
    public static readonly Vector4 TextCardTitle = Hex("#E9D7AE");
    public static readonly Vector4 TextValue = Hex("#E2D3B2");
    public static readonly Vector4 TextSecondary = Hex("#C2B396");
    public static readonly Vector4 TextTertiary = Hex("#B8A88A");
    public static readonly Vector4 TextMuted = Hex("#A8987A");
    public static readonly Vector4 TextDim = Hex("#8A7B62");
    public static readonly Vector4 TextDisabled = Hex("#6E604A");
    public static readonly Vector4 TextOnAccent = Hex("#1A1208");

    // ---- Akzent und Status (Abschnitt 2) ----
    public static readonly Vector4 AccentGold = Hex("#D4A94F");
    public static readonly Vector4 AccentEmerald = Hex("#5FB98A");
    public static readonly Vector4 AccentArcane = Hex("#9C8CF0");
    public static readonly Vector4 AccentEmber = Hex("#E0785A");

    /// <summary>Aktuell eingestellte Akzentfarbe (Standard: Gold) - siehe Abschnitt 1 "Akzentfarbe einstellbar".</summary>
    public static Vector4 Accent { get; set; } = AccentGold;

    public static readonly Vector4 OkFg = Hex("#9BD3A2");
    public static readonly Vector4 OkBg = Hex("#1C2A1D");
    public static readonly Vector4 WarnFg = Hex("#E9C46A");
    public static readonly Vector4 WarnBg = Hex("#30271A");
    public static readonly Vector4 WarnLine = Hex("#5C4A26");
    public static readonly Vector4 ErrFg = Hex("#EE9A86");
    public static readonly Vector4 ErrBg = Hex("#331C17");
    public static readonly Vector4 TooltipLine = Hex("#8A6E3A");

    // ---- Typ-Etiketten (Abschnitt 2) ----
    public static readonly Vector4 MountBadgeBg = Hex("#3A2F1E");
    public static readonly Vector4 MountBadgeFg = Hex("#E9C46A");
    public static readonly Vector4 MinionBadgeBg = Hex("#26223A");
    public static readonly Vector4 MinionBadgeFg = Hex("#C3B8F5");

    // ---- Abstände und Formen (Abschnitt 4) ----
    public const float OverlayWidth = 450f;
    public const float OverlayPaddingX = 14f;
    public const float OverlayRowGap = 2.5f;
    public const float SidebarWidth = 236f;

    public const float RoundingOverlay = 6f;
    public const float RoundingCard = 6f;
    public const float RoundingControl = 3.5f;
    public const float RoundingWindow = 8f;

    /// <summary>Zierecken in jeder Fensterecke (Abschnitt 4) - Länge/Dicke/Randabstand für Overlay bzw. Menü.</summary>
    public const float CornerLenOverlay = 10f;
    public const float CornerThickOverlay = 1.5f;
    public const float CornerInsetOverlay = 4f;
    public const float CornerLenMenu = 18f;
    public const float CornerThickMenu = 2f;
    public const float CornerInsetMenu = 6f;

    /// <summary>Breite des Trenn-Ornaments unter Seitentiteln (Abschnitt 4).</summary>
    public const float DividerOrnamentWidth = 160f;

    // ---- Schriften (Abschnitt 3) ----
    private static IFontAtlas Atlas => Plugin.PluginInterface.UiBuilder.FontAtlas;
    private static float ScaledPx(float px) => px * ImGuiHelpers.GlobalScale;

    private static string FontPath(string fileName) =>
        System.IO.Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Data", "Fonts", fileName);

    /// <summary>
    /// Baut ein Font-Handle aus einer lokalen TTF-Datei, mit Dalamuds gebündelter Noto-Sans-CJK-
    /// Schrift als Fallback für japanische Glyphen gemergt (Abschnitt 3: "Japanisch (Fallback)") -
    /// eigene Schriftdateien decken nur lateinische Glyphen ab. Der Merge-Schritt ist bewusst in ein
    /// eigenes try/catch gepackt: scheitert NUR er (z.B. CJK-Asset von Dalamud gerade nicht bereit),
    /// soll das die eigentliche lateinische Schrift nicht mit zu Fall bringen - sonst bliebe das
    /// komplette Handle dauerhaft "nicht verfügbar" und JEDER Text fiele auf die ImGui-Standardschrift
    /// zurück (Nutzer-Report: Schriftarten/-größen im Overlay passen nicht zum Entwurf).
    /// </summary>
    private static IFontHandle BuildHandle(string fileName, float sizePx) =>
        Atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var baseFont = tk.AddFontFromFile(FontPath(fileName), new SafeFontConfig { SizePx = ScaledPx(sizePx) });
            try
            {
                tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkRegular, new SafeFontConfig
                {
                    SizePx = ScaledPx(sizePx),
                    MergeFont = baseFont,
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[CodexTheme] Japanisch-Fallback für {fileName} konnte nicht gemergt werden - Schrift bleibt ohne CJK-Fallback.");
            }
        }));

    private static IFontHandle? cinzelTitleOverlay;
    private static IFontHandle? cinzelTitleMenu;
    private static IFontHandle? cinzelCardTitle;
    private static IFontHandle? cinzelLabel;
    private static IFontHandle? alegreyaSansRegular;
    private static IFontHandle? alegreyaSansMedium;
    private static IFontHandle? alegreyaSansBold;
    private static IFontHandle? alegreyaItalic;

    // Nutzer-Feedback: die in Abschnitt 3 angegebenen 12px wirkten im Spiel kleiner als im Entwurf -
    // schrittweise auf 16px angehoben, mit der echten statischen Bold-Datei statt der variablen
    // Cinzel.ttf (die lädt ohne Achsen-Auswahl nur in ihrem Standardgewicht, nie wirklich fett).
    /// <summary>Fenstertitel Overlay - Cinzel Bold, 16px (Abschnitt 3, im Spiel angepasst statt 12px).</summary>
    public static IFontHandle FontTitleOverlay => cinzelTitleOverlay ??= BuildHandle("Cinzel-Bold.ttf", 16f);

    /// <summary>Seitentitel Menü - Cinzel Bold 33px (Abschnitt 3, im Spiel von 26px erhöht; echte statische Bold-Datei statt der variablen Cinzel.ttf, die ohne Achsen-Auswahl nie wirklich fett lädt).</summary>
    public static IFontHandle FontTitleMenu => cinzelTitleMenu ??= BuildHandle("Cinzel-Bold.ttf", 33f);

    private static IFontHandle? alegreyaMenuSubtitle;

    /// <summary>Untertitel im Menü-Seitenkopf (Abschnitt 6) - Alegreya Italic 20px, bewusst von FontSubtitleItalic gelöst, das auch für die leere Item-Liste im Overlay verwendet wird.</summary>
    public static IFontHandle FontMenuSubtitle => alegreyaMenuSubtitle ??= BuildHandle("AlegreyaItalic.ttf", 20f);

    /// <summary>Kartentitel im neuen Menü (Abschnitt 3/6, z.B. "SPRACHE"/"OVERLAY") - Cinzel 22px (im Spiel von 15px erhöht, siehe CardGroupLabel).</summary>
    public static IFontHandle FontCardTitle => cinzelCardTitle ??= BuildHandle("Cinzel.ttf", 22f);

    private static IFontHandle? alegreyaMenuFieldLabel;

    /// <summary>Feldbeschriftung im Menü (z.B. "Menu language" vor einer Combo) - Alegreya Sans Medium 21px, bewusst von FontBodyMedium gelöst, das auch für Nav-Punkte/Schalter-Zeilen verwendet wird.</summary>
    public static IFontHandle FontMenuFieldLabel => alegreyaMenuFieldLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 21f);

    private static IFontHandle? alegreyaMenuDropdownValue;

    /// <summary>Anzeige-/Auswahltext einer Menü-Combo (z.B. "English" in der Sprachauswahl) - Alegreya Sans Regular 17px (im Spiel von der ImGui-Standardschrift auf +3px erhöht).</summary>
    public static IFontHandle FontMenuDropdownValue => alegreyaMenuDropdownValue ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    private static IFontHandle? cinzelZoneName;

    // Nutzer-Feedback: eigenständig von FontCardTitle gelöst, auf 22px vergrößert und in Bold
    // dargestellt (echte statische Bold-Datei, siehe FontTitleOverlay-Kommentar) - FontCardTitle
    // bleibt bei 15px/normaler Stärke für künftige Menü-Kartentitel (Abschnitt 3).
    /// <summary>Zonenname im Overlay - Cinzel Bold, 22px (Abschnitt 3, im Spiel angepasst statt 15px).</summary>
    public static IFontHandle FontZoneName => cinzelZoneName ??= BuildHandle("Cinzel-Bold.ttf", 22f);

    /// <summary>Abschnitts-Labels (Versalien) - Cinzel SemiBold 17px (Abschnitt 3, im Spiel von 10px erhöht).</summary>
    public static IFontHandle FontSectionLabel => cinzelLabel ??= BuildHandle("Cinzel.ttf", 17f);

    /// <summary>Fließtext/Knöpfe/Listen Regular - Alegreya Sans 13px (Abschnitt 3).</summary>
    public static IFontHandle FontBody => alegreyaSansRegular ??= BuildHandle("AlegreyaSans-Regular.ttf", 13f);

    private static IFontHandle? alegreyaSansSmall;

    /// <summary>Kleine Fließtext-Labels (z.B. Zonen-Id neben dem Zonennamen) - Alegreya Sans, 14px (im Spiel angepasst statt 11px).</summary>
    public static IFontHandle FontBodySmall => alegreyaSansSmall ??= BuildHandle("AlegreyaSans-Regular.ttf", 14f);

    /// <summary>Fließtext/Knöpfe/Listen Medium - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Menü-Navigationspunkt von 13px erhöht).</summary>
    public static IFontHandle FontBodyMedium => alegreyaSansMedium ??= BuildHandle("AlegreyaSans-Medium.ttf", 18f);

    /// <summary>Fließtext/Knöpfe/Listen Bold - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Item-Namen von 13px erhöht).</summary>
    public static IFontHandle FontBodyBold => alegreyaSansBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 18f);

    private static IFontHandle? alegreyaAutoButtonRegular;
    private static IFontHandle? alegreyaAutoButtonBold;

    // Nutzer-Feedback: eigenständig von FontBody/FontBodyBold gelöst und auf 14px vergrößert - die
    // beiden bleiben bei 13px für den Rest des Overlays (z.B. Item-Liste), nur die Auto-Knöpfe
    // sollen größer werden.
    /// <summary>Label der Auto-Knöpfe - Alegreya Sans Bold, 14px (im Spiel angepasst statt 13px).</summary>
    public static IFontHandle FontAutoButtonLabel => alegreyaAutoButtonBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 14f);

    /// <summary>Zahl der Auto-Knöpfe - Alegreya Sans Regular, 14px (im Spiel angepasst statt 13px).</summary>
    public static IFontHandle FontAutoButtonCount => alegreyaAutoButtonRegular ??= BuildHandle("AlegreyaSans-Regular.ttf", 14f);

    /// <summary>Untertitel Menü - Alegreya Italic 15px (Abschnitt 3).</summary>
    public static IFontHandle FontSubtitleItalic => alegreyaItalic ??= BuildHandle("AlegreyaItalic.ttf", 15f);

    private static IFontHandle? alegreyaTabRowMedium;

    /// <summary>Reiter-Zeile (Abschnitt 5.5): Reiter-Anzahl hinter "Zone"/"ToDo-Liste" - Alegreya Sans Medium, 19px (im Spiel von 11px erhöht).</summary>
    public static IFontHandle FontTabRow => alegreyaTabRowMedium ??= BuildHandle("AlegreyaSans-Medium.ttf", 19f);

    private static IFontHandle? alegreyaTabLabelBold;

    /// <summary>Reiter-Beschriftung "Zone"/"ToDo-Liste" - Alegreya Sans Bold, 21px (im Spiel von 13px erhöht, bewusst von FontBodyBold gelöst, das auch in der Item-Liste verwendet wird).</summary>
    public static IFontHandle FontTabLabel => alegreyaTabLabelBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 21f);

    private static IFontHandle? alegreyaTypeBadgeBold;

    /// <summary>Typ-Etikett (z.B. "Accessory") in der Item-Liste - Alegreya Sans Bold, 16px (im Spiel von 13px erhöht, bewusst von FontBodyBold gelöst, das auch für den Item-Namen verwendet wird).</summary>
    public static IFontHandle FontTypeBadge => alegreyaTypeBadgeBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 16f);

    private static IFontHandle? alegreyaFilterButtonMedium;

    /// <summary>Beschriftung der Filter-Knöpfe "Typen"/"Währungen" - Alegreya Sans Medium, 13px (im Spiel von 11px erhöht, bewusst von FontTabRow gelöst, da die Reiter-Anzahl bei 11px bleiben soll).</summary>
    public static IFontHandle FontFilterButton => alegreyaFilterButtonMedium ??= BuildHandle("AlegreyaSans-Medium.ttf", 13f);

    private static IFontHandle? alegreyaCurrencyValue;
    private static IFontHandle? alegreyaCurrencyName;

    /// <summary>Wert in der Währungsübersicht - Alegreya Sans Bold, 19px (Abschnitt 5.4, im Spiel von 14px erhöht).</summary>
    public static IFontHandle FontCurrencyValue => alegreyaCurrencyValue ??= BuildHandle("AlegreyaSans-Bold.ttf", 19f);

    /// <summary>Name in der Währungsübersicht - Alegreya Sans Regular, 19px (Abschnitt 5.4, im Spiel an FontCurrencyValue angeglichen).</summary>
    public static IFontHandle FontCurrencyName => alegreyaCurrencyName ??= BuildHandle("AlegreyaSans-Regular.ttf", 19f);

    private static IFontHandle? cinzelSidebarBrandSmall;
    private static IFontHandle? cinzelSidebarBrandLarge;

    /// <summary>Menü-Logo (Abschnitt 6) - "THE EXPLORER'S" über dem großen "CODEX"-Schriftzug, klein - Cinzel 15px (im Spiel von 10px erhöht).</summary>
    public static IFontHandle FontSidebarBrandSmall => cinzelSidebarBrandSmall ??= BuildHandle("Cinzel.ttf", 15f);

    /// <summary>Menü-Logo (Abschnitt 6) - "CODEX"-Schriftzug - Cinzel Bold 26px (im Spiel von 21px erhöht).</summary>
    public static IFontHandle FontSidebarBrandLarge => cinzelSidebarBrandLarge ??= BuildHandle("Cinzel-Bold.ttf", 26f);

    /// <summary>
    /// Baut alle Font-Handles schon beim Plugin-Start an, statt erst beim ersten Zeichnen des neuen
    /// Overlays (Nutzer-Report: beim Öffnen sah man kurz die ImGui-Standardschrift, bis der
    /// Font-Atlas fertig gebaut war). Jeder Handle wird lazy über "??=" erzeugt - das bloße Lesen der
    /// Property hier reicht, um den (asynchronen) Bauvorgang bei Dalamud anzustoßen; auf das
    /// Ergebnis muss dabei NICHT gewartet werden. Von Plugin() aus aufgerufen.
    /// </summary>
    public static void PreloadFonts()
    {
        _ = FontTitleOverlay;
        _ = FontTitleMenu;
        _ = FontCardTitle;
        _ = FontZoneName;
        _ = FontSectionLabel;
        _ = FontBody;
        _ = FontBodySmall;
        _ = FontBodyMedium;
        _ = FontBodyBold;
        _ = FontSubtitleItalic;
        _ = FontMenuSubtitle;
        _ = FontMenuFieldLabel;
        _ = FontMenuDropdownValue;
        _ = FontAutoButtonLabel;
        _ = FontAutoButtonCount;
        _ = FontCurrencyValue;
        _ = FontCurrencyName;
        _ = FontTabRow;
        _ = FontFilterButton;
        _ = FontTabLabel;
        _ = FontTypeBadge;
        _ = FontSidebarBrandSmall;
        _ = FontSidebarBrandLarge;
    }

    /// <summary>
    /// Setzt die geteilten Grundfarben/-rundungen (Abschnitt 1 "ein Theme für alles") - vor
    /// ImGui.Begin() aufrufen (siehe ModernUi.PushStyle-Vorbild), mit PopStyle() beenden.
    /// </summary>
    public static void PushStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, RoundingWindow);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, RoundingCard);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, RoundingControl);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, RoundingControl);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 999f);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, BgWindow);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, BgPopup);
        ImGui.PushStyleColor(ImGuiCol.Border, LineControl);
        ImGui.PushStyleColor(ImGuiCol.Separator, LineCard);
        ImGui.PushStyleColor(ImGuiCol.Text, TextPrimary);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, BgInput);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, BgSelected);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, BgSelectedStrong);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BgSelected);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BgSelectedStrong);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, BgInput);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, LineFrame);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Accent);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Accent);
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(14);
        ImGui.PopStyleVar(5);
    }

    /// <summary>
    /// Zierecken (Abschnitt 4) - zwei kurze Akzentlinien je Fensterecke, die ein L bilden. Zeichnet
    /// über das aktuelle Fenster-Rechteck (ImGui.GetWindowPos()/GetWindowSize()).
    /// </summary>
    public static void DrawCornerOrnaments(float length, float thickness, float inset, Vector4? color = null)
    {
        var col = ImGui.GetColorU32(color ?? Accent);
        var min = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();

        void Corner(Vector2 origin, Vector2 h, Vector2 v)
        {
            drawList.AddLine(origin, origin + h, col, thickness);
            drawList.AddLine(origin, origin + v, col, thickness);
        }

        Corner(min + new Vector2(inset, inset), new Vector2(length, 0f), new Vector2(0f, length));
        Corner(new Vector2(max.X - inset, min.Y + inset), new Vector2(-length, 0f), new Vector2(0f, length));
        Corner(new Vector2(min.X + inset, max.Y - inset), new Vector2(length, 0f), new Vector2(0f, -length));
        Corner(max - new Vector2(inset, inset), new Vector2(-length, 0f), new Vector2(0f, -length));
    }

    /// <summary>
    /// Trenn-Ornament (Abschnitt 4) - zwei Linien (60% Deckkraft Akzent) mit kleiner Raute in der
    /// Mitte, mittig unter dem Cursor über <see cref="DividerOrnamentWidth"/> gezeichnet.
    /// </summary>
    public static void DrawDividerOrnament()
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var y = cursor.Y + 6f;
        var col = ImGui.GetColorU32(Accent with { W = 0.6f });
        const float diamond = 4f;
        var halfLine = (DividerOrnamentWidth - diamond * 2f - 8f) / 2f;

        var left = cursor.X;
        drawList.AddLine(new Vector2(left, y), new Vector2(left + halfLine, y), col, 1f);
        var center = new Vector2(left + halfLine + 4f + diamond / 2f, y);
        drawList.AddCircleFilled(center, diamond / 2f, col, 4);
        var right = center.X + diamond / 2f + 4f;
        drawList.AddLine(new Vector2(right, y), new Vector2(right + halfLine, y), col, 1f);

        ImGui.Dummy(new Vector2(DividerOrnamentWidth, 12f));
    }

    /// <summary>
    /// Zeichnet Text mit dunklem Schatten (Abschnitt 5.9, ab ~70% Overlay-Transparenz) - 1px in 4
    /// Richtungen versetzt in Schwarz (~80% Deckkraft), danach normal darüber.
    /// </summary>
    public static void TextShadowed(string text, Vector4 color, bool shadow)
    {
        if (!shadow)
        {
            ImGui.TextColored(color, text);
            return;
        }

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var shadowCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f));
        foreach (var offset in stackalloc Vector2[] { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) })
            drawList.AddText(pos + offset, shadowCol, text);
        drawList.AddText(pos, ImGui.GetColorU32(color), text);
        ImGui.Dummy(ImGui.CalcTextSize(text));
    }

    /// <summary>
    /// Wie <see cref="TextShadowed"/>, aber für Text, der bereits direkt per drawList.AddText an einer
    /// manuell berechneten Position gezeichnet wird (Auto-Knöpfe, Reiter, Filter-Knöpfe, Typ-Etikett -
    /// siehe CodexOverlayWindow), statt über den ImGui-Cursor.
    /// </summary>
    public static void DrawTextShadowed(ImDrawListPtr drawList, Vector2 pos, Vector4 color, string text, bool shadow)
    {
        if (shadow)
        {
            var shadowCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f));
            foreach (var offset in stackalloc Vector2[] { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) })
                drawList.AddText(pos + offset, shadowCol, text);
        }

        drawList.AddText(pos, ImGui.GetColorU32(color), text);
    }

    /// <summary>Abschnitts-Label (Abschnitt 5.4/6 etc.) - Versalien, Cinzel SemiBold, leicht gesperrt gesetzt.</summary>
    public static void SectionLabel(string text, bool shadow = false)
    {
        using (FontSectionLabel.Push())
            TextShadowed(text.ToUpperInvariant(), TextTertiary, shadow);
    }

    /// <summary>
    /// Schalter (Abschnitt 4) - 42x22px, selbst gezeichnet und per InvisibleButton klickbar. An:
    /// Akzent-Fläche mit dunklem Knopf (TextOnAccent). Aus: #2A2219 mit Rahmen LineControl und Knopf
    /// #A8987A (TextMuted). Gibt true zurück, wenn gerade umgeschaltet wurde - der Aufrufer liest den
    /// NEUEN Wert dann selbst aus "value" (ref), ändert ihn aber NICHT selbst (siehe CodexMenuWindow-
    /// Vorbild: der Aufrufer speichert erst nach dem Umschalten in die Configuration).
    /// </summary>
    public static bool Toggle(string id, ref bool value, float scale)
    {
        var size = new Vector2(42f * scale, 22f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);

        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;
        var bg = value ? Accent : Hex("#2A2219");
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), rounding);
        if (!value)
            drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(LineControl), rounding);

        var knobRadius = size.Y / 2f - 3f * scale;
        var knobX = value ? cursor.X + size.X - size.Y / 2f : cursor.X + size.Y / 2f;
        var knobColor = value ? TextOnAccent : TextMuted;
        drawList.AddCircleFilled(new Vector2(knobX, cursor.Y + size.Y / 2f), knobRadius, ImGui.GetColorU32(knobColor));

        if (clicked)
            value = !value;
        return clicked;
    }

    // Siehe BeginCard/EndCard - Karten haben beliebig viele Zeilen (Schalter/Combos), eine feste oder
    // per BeginChild "AutoResizeY" (das dieses ältere ImGui-Binding nicht kennt, siehe fehlendes
    // ImGuiChildFlags) automatisch mitwachsende Höhe gibt es hier nicht. Stattdessen das verbreitete
    // ImGui-Muster "Hintergrund hinter eine Gruppe nachträglich einziehen": der Kartenhintergrund wird
    // über einen eigenen Draw-Kanal (ChannelsSplit) VOR dem eigentlichen Inhalt gezeichnet, aber erst
    // NACH EndCard() (wenn die tatsächliche Höhe feststeht) per ChannelsMerge sichtbar - der Inhalt
    // selbst läuft dabei ganz normal über den ImGui-Cursor (Indent/Dummy), nur eben auf einem anderen
    // Kanal, daher keine Cursor-Sondertricks nötig.
    private static Vector2 cardOrigin;
    private static float cardWidth;
    private static float cardPaddingX;
    private static float cardPaddingY;

    /// <summary>Karte im Menü (Abschnitt 6) - BgCard-Hintergrund, LineCard-Rahmen, Innenabstand, Höhe passt sich dem Inhalt an. Mit EndCard() beenden.</summary>
    public static void BeginCard(float scale)
    {
        cardPaddingX = 16f * scale;
        cardPaddingY = 14f * scale;
        cardOrigin = ImGui.GetCursorScreenPos();
        cardWidth = ImGui.GetContentRegionAvail().X;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.Indent(cardPaddingX);
        ImGui.Dummy(new Vector2(0f, cardPaddingY));
    }

    public static void EndCard()
    {
        ImGui.Dummy(new Vector2(0f, cardPaddingY));
        ImGui.Unindent(cardPaddingX);

        var min = cardOrigin;
        var max = new Vector2(cardOrigin.X + cardWidth, ImGui.GetCursorScreenPos().Y);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(BgCard), RoundingCard);
        drawList.AddRect(min, max, ImGui.GetColorU32(LineCard), RoundingCard);
        drawList.ChannelsMerge();
    }

    /// <summary>Innerer Innenabstand der aktuell offenen Karte (siehe BeginCard) - für rechtsbündige Breiten per ImGui.SetNextItemWidth(-CardPaddingX), damit Combos auch rechts symmetrisch Abstand zum Kartenrand halten.</summary>
    public static float CardPaddingX => cardPaddingX;

    /// <summary>
    /// Kartentitel (Abschnitt 6, z.B. "SPRACHE"/"OVERLAY") - wie SectionLabel, darunter eine über die
    /// volle Kartenbreite durchgehende Trennlinie (Nutzervorgabe laut Entwurfsbild - fehlte vorher).
    /// </summary>
    /// <summary>
    /// lineWidth: optionale Breite der Trennlinie unter dem Titel, falls sie NICHT die volle
    /// Kartenbreite einnehmen soll - Nutzervorgabe für die Sprachkarte, deren Combo wegen ihres
    /// rechten Randabstands kürzer als die Karte ist, damit die Linie exakt dort endet, wo die Combo
    /// endet, statt optisch darüber hinauszuragen.
    /// </summary>
    public static void CardGroupLabel(string text, float scale, float? lineWidth = null)
    {
        using (FontCardTitle.Push())
            TextShadowed(text.ToUpperInvariant(), TextTertiary, false);
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var cursor = ImGui.GetCursorScreenPos();
        var width = lineWidth ?? ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(cursor, cursor + new Vector2(width, 0f), ImGui.GetColorU32(LineSubtle), 1f);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    /// <summary>
    /// Eine Einstellungszeile: Label links, Schalter rechtsbündig, optionaler Tooltip beim Hover über
    /// die ganze Zeile. Gibt true zurück, wenn der Schalter gerade umgeschaltet wurde.
    /// </summary>
    public static bool ToggleRow(string id, string label, ref bool value, float scale, string? tooltip = null)
    {
        var rowStartY = ImGui.GetCursorPosY();
        using (FontBodyMedium.Push())
            ImGui.TextColored(TextPrimary, label);
        var textBottomY = ImGui.GetCursorPosY();
        var textHeight = textBottomY - rowStartY;

        var toggleHeight = 22f * scale;
        var toggleWidth = 42f * scale;
        var toggleY = rowStartY + (textHeight - toggleHeight) / 2f;
        var rightX = ImGui.GetWindowContentRegionMax().X - toggleWidth;

        ImGui.SetCursorPos(new Vector2(rightX, toggleY));
        var changed = Toggle(id, ref value, scale);

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        ImGui.SetCursorPosY(MathF.Max(textBottomY, toggleY + toggleHeight));
        return changed;
    }

    private static IDisposable? dropdownValueFontPop;
    private static float dropdownRowTextBottomY;
    private static float dropdownRowComboY;
    private static float dropdownRowComboHeight;

    /// <summary>
    /// Zeilenlayout + einheitliches Aussehen für eine Menü-Combo (Nutzeranforderung: "Sprache"-Design
    /// für ALLE Dropdowns) - Label links, Combo rechtsbündig mit festem Randabstand, schlankere Höhe,
    /// eigene Schriftgröße (FontMenuDropdownValue) und ein dünner Rahmen im Kartenton (LineCard). Bei
    /// geöffnetem Dropdown bekommen die Einträge zusätzlich 5px Innenabstand (siehe EndDropdownRow).
    /// Muss IMMER mit EndDropdownRow() abgeschlossen werden, unabhängig vom Rückgabewert (wie
    /// ImGui.BeginCombo/EndCombo selbst nur bei geöffnetem Dropdown ein Gegenstück braucht - das
    /// übernimmt EndDropdownRow intern).
    /// </summary>
    public static bool BeginDropdownRow(string label, string currentValueLabel, string comboId, float scale,
        string? tooltip = null, bool disabled = false, float width = 205f, float rightMargin = 25f)
    {
        var rowStartY = ImGui.GetCursorPosY();
        using (FontMenuFieldLabel.Push())
            ImGui.TextColored(disabled ? TextDisabled : TextPrimary, label);
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered(disabled ? ImGuiHoveredFlags.AllowWhenDisabled : ImGuiHoveredFlags.None))
            ImGui.SetTooltip(tooltip);
        var textBottomY = ImGui.GetCursorPosY();

        var comboWidth = width * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * scale, 9f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleColor(ImGuiCol.Border, LineCard);
        dropdownValueFontPop = FontMenuDropdownValue.Push();

        var comboHeight = ImGui.GetFrameHeight();
        var comboY = rowStartY + (textBottomY - rowStartY - comboHeight) / 2f;
        dropdownRowTextBottomY = textBottomY;
        dropdownRowComboY = comboY;
        dropdownRowComboHeight = comboHeight;

        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowContentRegionMax().X - comboWidth - rightMargin * scale, comboY));
        ImGui.SetNextItemWidth(comboWidth);

        if (disabled)
            ImGui.BeginDisabled();

        var open = ImGui.BeginCombo(comboId, currentValueLabel);
        if (open)
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f * scale, 5f * scale));
        return open;
    }

    /// <summary>Gegenstück zu BeginDropdownRow - IMMER aufrufen, auch wenn das Dropdown gerade nicht offen ist.</summary>
    public static void EndDropdownRow(bool wasOpen, bool disabled)
    {
        if (wasOpen)
        {
            ImGui.PopStyleVar();
            ImGui.EndCombo();
        }

        if (disabled)
            ImGui.EndDisabled();

        dropdownValueFontPop?.Dispose();
        dropdownValueFontPop = null;
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        ImGui.SetCursorPosY(MathF.Max(dropdownRowTextBottomY, dropdownRowComboY + dropdownRowComboHeight));
    }

    /// <summary>
    /// Wie BeginDropdownRow, aber gibt zusätzlich den rechten Rand der Combo zurück (Fensterkoordinate,
    /// VOR dem Zeichnen) - für CardGroupLabel(lineWidth:), damit die Trennlinie unter dem Kartentitel
    /// exakt dort endet, wo die Combo dieser Zeile endet (Nutzervorgabe, siehe Sprachkarte).
    /// </summary>
    public static float DropdownRowLineWidth(float scale, float rightMargin = 25f) =>
        ImGui.GetContentRegionAvail().X - rightMargin * scale;

    /// <summary>Feine Trennlinie innerhalb einer Karte (Abschnitt 2 "LineSubtle"), mit etwas vertikalem Abstand.</summary>
    public static void CardDivider(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.PushStyleColor(ImGuiCol.Separator, LineSubtle);
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, 8f * scale));
    }

    /// <summary>
    /// Kompass-Symbol aus dem Entwurf (referenz/OverlayWidget.dc.html) nachgebaut: Kreis-Umriss plus
    /// gefüllte Raute (Nadel) in der Akzentfarbe. Gemeinsam von Overlay-Kopfzeile (CodexOverlayWindow)
    /// und Menü-Logo (CodexMenuWindow) genutzt, daher hier statt in einem der beiden Fenster.
    /// </summary>
    public static void DrawCompassIcon(float size)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var center = cursor + new Vector2(size / 2f, size / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var accent = ImGui.GetColorU32(Accent);

        drawList.AddCircle(center, size * 0.39f, accent, 24, 1.5f);

        var r = size * 0.3f;
        var top = center + new Vector2(0f, -r);
        var right = center + new Vector2(r * 0.65f, 0f);
        var bottom = center + new Vector2(0f, r);
        var left = center + new Vector2(-r * 0.65f, 0f);
        drawList.AddQuadFilled(top, right, bottom, left, accent);

        ImGui.Dummy(new Vector2(size, size));
    }
}
