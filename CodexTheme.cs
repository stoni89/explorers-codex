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
    public static readonly Vector4 OkLine = Hex("#35513A");
    public static readonly Vector4 WarnFg = Hex("#E9C46A");
    public static readonly Vector4 WarnBg = Hex("#30271A");
    public static readonly Vector4 WarnLine = Hex("#5C4A26");
    public static readonly Vector4 ErrFg = Hex("#EE9A86");
    public static readonly Vector4 ErrBg = Hex("#331C17");
    public static readonly Vector4 ErrLine = Hex("#5A2E26");
    public static readonly Vector4 TooltipLine = Hex("#8A6E3A");

    // ---- Changelog-Seite ("FIXED"-Tag - NEW/IMPROVED/REMOVED decken sich bereits exakt mit
    // Ok/Warn/Err oben, nur FIXED (blau) kommt sonst nirgends vor). ----
    public static readonly Vector4 InfoBg = Hex("#18242E");
    public static readonly Vector4 InfoLine = Hex("#2C4458");

    // ---- Log-Seite (Abschnitt 4/6 des Log-DESIGN_SPEC) - Info/Critical kommen in keiner anderen
    // Seite vor, daher keine passenden Tokens oben wiederverwendbar. ----
    public static readonly Vector4 LogInfoFg = Hex("#9CC4E4");
    public static readonly Vector4 LogCritFg = Hex("#F06A7A");

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
    /// Baut ein Font-Handle aus einer lokalen TTF-Datei - mergeCjk=true merged zusätzlich Dalamuds
    /// gebündelte Noto-Sans-CJK-Schrift als Fallback für japanische Glyphen (Abschnitt 3: "Japanisch
    /// (Fallback)"), eigene Schriftdateien decken nur lateinische Glyphen ab. Der Merge-Schritt ist
    /// bewusst in ein eigenes try/catch gepackt: scheitert NUR er (z.B. CJK-Asset von Dalamud gerade
    /// nicht bereit), soll das die eigentliche lateinische Schrift nicht mit zu Fall bringen - sonst
    /// bliebe das komplette Handle dauerhaft "nicht verfügbar" und JEDER Text fiele auf die
    /// ImGui-Standardschrift zurück (Nutzer-Report: Schriftarten/-größen im Overlay passen nicht zum
    /// Entwurf).
    ///
    /// Nutzer-Report "Rendern des Menüs dauert über 1 Minute": der CJK-Merge rastert Dalamuds
    /// komplette Noto-Sans-CJK-Schrift (mehrere tausend Glyphen) NEU, einmal PRO Handle - bei inzwischen
    /// ~70 verschiedenen (Datei, Größe)-Kombinationen in diesem Theme war das der dominante Anteil der
    /// Font-Atlas-Bauzeit beim Plugin-Start. mergeCjk ist daher jetzt standardmäßig AUS und wird NUR
    /// für die Handles auf true gesetzt, die tatsächlich rohe Spieldaten anzeigen (Item-/Zonen-/
    /// Händler-/Währungsnamen, die bei japanischem Spielclient japanische Zeichen enthalten können) -
    /// alle reinen UI-Beschriftungen dieses Plugins kommen ausschließlich aus Loc.T(de, en)/
    /// Loc.TypeName, sind also NIE japanisch und brauchen den Fallback nicht.
    /// </summary>
    private static IFontHandle BuildHandle(string fileName, float sizePx, bool mergeCjk = false) =>
        Atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var baseFont = tk.AddFontFromFile(FontPath(fileName), new SafeFontConfig { SizePx = ScaledPx(sizePx) });
            if (!mergeCjk)
                return;

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
    public static IFontHandle FontZoneName => cinzelZoneName ??= BuildHandle("Cinzel-Bold.ttf", 22f, mergeCjk: true);

    /// <summary>Abschnitts-Labels (Versalien) - Cinzel SemiBold 17px (Abschnitt 3, im Spiel von 10px erhöht).</summary>
    public static IFontHandle FontSectionLabel => cinzelLabel ??= BuildHandle("Cinzel.ttf", 17f);

    /// <summary>Fließtext/Knöpfe/Listen Regular - Alegreya Sans 13px (Abschnitt 3).</summary>
    public static IFontHandle FontBody => alegreyaSansRegular ??= BuildHandle("AlegreyaSans-Regular.ttf", 13f);

    private static IFontHandle? alegreyaSansSmall;

    /// <summary>Kleine Fließtext-Labels (z.B. Zonen-Id neben dem Zonennamen) - Alegreya Sans, 14px (im Spiel angepasst statt 11px).</summary>
    public static IFontHandle FontBodySmall => alegreyaSansSmall ??= BuildHandle("AlegreyaSans-Regular.ttf", 14f);

    private static IFontHandle? alegreyaSansDropdownCaption;

    /// <summary>Erklärtext unter einem BeginDropdownRow-Label (Nutzervorgabe: +3px gegenüber FontBodySmall) - Alegreya Sans, 17px.</summary>
    public static IFontHandle FontDropdownCaption => alegreyaSansDropdownCaption ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    /// <summary>Fließtext/Knöpfe/Listen Medium - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Menü-Navigationspunkt von 13px erhöht).</summary>
    public static IFontHandle FontBodyMedium => alegreyaSansMedium ??= BuildHandle("AlegreyaSans-Medium.ttf", 18f);

    /// <summary>Fließtext/Knöpfe/Listen Bold - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Item-Namen von 13px erhöht).</summary>
    public static IFontHandle FontBodyBold => alegreyaSansBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 18f, mergeCjk: true);

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

    private static IFontHandle? alegreyaBadgeSmall;

    /// <summary>Kompakte Badges auf der Plugins-Seite (z.B. "ONE REQUIRED", "5 / 5") - Alegreya Sans Bold, 16px (ursprünglich 11px, +5px auf Nutzerwunsch).</summary>
    public static IFontHandle FontBadgeSmall => alegreyaBadgeSmall ??= BuildHandle("AlegreyaSans-Bold.ttf", 16f);

    private static IFontHandle? cinzelPluginGroupLabel;

    /// <summary>Gruppen-Überschriften auf der Plugins-Seite ("Combat plugin"/"Required"/"Optional") - Cinzel 22px (von FontSectionLabel +5px gelöst, da dessen 17px z.B. auch für die Sidebar-Labels gebraucht werden).</summary>
    public static IFontHandle FontPluginGroupLabel => cinzelPluginGroupLabel ??= BuildHandle("Cinzel.ttf", 22f);

    private static IFontHandle? alegreyaPluginName;

    /// <summary>Plugin-Name in einer Plugins-Zeile - Alegreya Sans Bold, 21px (von FontBodyBold +3px, dann +3px, dann wieder -3px gelöst, das auch für Item-Namen im Overlay gebraucht wird).</summary>
    public static IFontHandle FontPluginName => alegreyaPluginName ??= BuildHandle("AlegreyaSans-Bold.ttf", 21f);

    private static IFontHandle? alegreyaPluginDescription;

    /// <summary>Beschreibung unter dem Plugin-Namen - Alegreya Sans Regular, 15px (von FontBody +2px gelöst, das u.a. auch für die Status-Zeile oben gebraucht wird).</summary>
    public static IFontHandle FontPluginDescription => alegreyaPluginDescription ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaPluginStatusRest;

    /// <summary>Zweiter Teil der Statuszeile auf der Plugins-Seite (z.B. "7 of 8 plugins installed...") - Alegreya Sans Medium, 17px (-1px gegenüber FontBodyMedium, das auch anderswo gebraucht wird).</summary>
    public static IFontHandle FontPluginStatusRest => alegreyaPluginStatusRest ??= BuildHandle("AlegreyaSans-Medium.ttf", 17f);

    private static IFontHandle? alegreyaPluginInstalledLabel;

    /// <summary>"Installed"-Label einer Plugin-Zeile - Alegreya Sans Bold, 17px (von FontAutoButtonLabel +3px gelöst, das auch für die Auto-Knöpfe im Overlay gebraucht wird).</summary>
    public static IFontHandle FontPluginInstalledLabel => alegreyaPluginInstalledLabel ??= BuildHandle("AlegreyaSans-Bold.ttf", 17f);

    private static IFontHandle? cinzelDatabaseHeader;

    /// <summary>Spaltennamen der Datenbank-Tabelle (NAME/PREIS/VON/ZONE/STATUS) - Cinzel SemiBold, 21px (ursprünglich 11px, +10px auf Nutzerwunsch).</summary>
    public static IFontHandle FontDatabaseHeader => cinzelDatabaseHeader ??= BuildHandle("Cinzel.ttf", 21f);

    private static IFontHandle? alegreyaDatabaseItemName;

    /// <summary>Item-Name in der Datenbank-Tabelle - Alegreya Sans Bold, 21px (von FontBodyBold +3px gelöst, das auch für Item-Namen im Overlay gebraucht wird).</summary>
    public static IFontHandle FontDatabaseItemName => alegreyaDatabaseItemName ??= BuildHandle("AlegreyaSans-Bold.ttf", 21f, mergeCjk: true);

    private static IFontHandle? alegreyaDatabaseItemText;

    /// <summary>Preis/Von/Zone-Text in der Datenbank-Tabelle - Alegreya Sans Regular, 18px (von FontBody +3px, dann nochmal +2px gelöst, das auch anderswo gebraucht wird).</summary>
    public static IFontHandle FontDatabaseItemText => alegreyaDatabaseItemText ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f, mergeCjk: true);

    private static IFontHandle? alegreyaDatabaseStatusLabel;

    /// <summary>"Besessen"/"Fehlt"-Label in der Datenbank-Tabelle - Alegreya Sans Bold, 14px (eigener, kleinerer Handle statt FontPluginInstalledLabel, das auf der Plugins-Seite 17px braucht).</summary>
    public static IFontHandle FontDatabaseStatusLabel => alegreyaDatabaseStatusLabel ??= BuildHandle("AlegreyaSans-Bold.ttf", 14f);

    private static IFontHandle? alegreyaDatabaseSearchInput;

    /// <summary>Text/Platzhalter im Datenbank-Suchfeld - Alegreya Sans Regular, 15px (von FontBody +2px gelöst, das auch anderswo gebraucht wird).</summary>
    public static IFontHandle FontDatabaseSearchInput => alegreyaDatabaseSearchInput ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaDatabaseCountRow;

    /// <summary>Zähler-/Sortierzeile der Datenbank-Seite ("X von Y Einträgen", "Sortiert nach ...") - Alegreya Sans Regular, 16px (von FontBody +3px gelöst, das auch anderswo gebraucht wird).</summary>
    public static IFontHandle FontDatabaseCountRow => alegreyaDatabaseCountRow ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

    private static IFontHandle? alegreyaBlacklistTypeBadge;

    /// <summary>Typ-Etikett in der Blacklist-Tabelle - Alegreya Sans Bold 13px (von FontTypeBadge, 16px, gelöst - die Blacklist-Tabelle ist insgesamt kleiner gesetzt als das Overlay; +2px Nutzervorgabe von ursprünglich 11px).</summary>
    public static IFontHandle FontBlacklistTypeBadge => alegreyaBlacklistTypeBadge ??= BuildHandle("AlegreyaSans-Bold.ttf", 13f);

    private static IFontHandle? alegreyaBlacklistBody;

    /// <summary>Übrige Zellentexte (Zone, "hidden"-Hinweis, Fußzeile, Hinweis-Karte) der Blacklist-Tabelle - Alegreya Sans Regular 17px (DESIGN_SPEC-Basis 14px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontBlacklistBody => alegreyaBlacklistBody ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    private static IFontHandle? alegreyaBlacklistButtonLabel;

    /// <summary>Rahmen-Knöpfe der Blacklist-Seite ("Restore all"/"Restore"/"Cancel" im Bestätigungs-Popup) - Alegreya Sans Medium 14px (DESIGN_SPEC).</summary>
    public static IFontHandle FontBlacklistButtonLabel => alegreyaBlacklistButtonLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 14f);

    private static IFontHandle? alegreyaShortcutKey;

    /// <summary>Tasten-Kästchen in CodexWidgets.ShortcutHint ("Ctrl"/"Shift"/"Click") - Alegreya Sans Bold 14px (DESIGN_SPEC-Basis 12px, +2px Nutzervorgabe).</summary>
    public static IFontHandle FontShortcutKey => alegreyaShortcutKey ??= BuildHandle("AlegreyaSans-Bold.ttf", 14f);

    private static IFontHandle? alegreyaShortcutText;

    /// <summary>Begleittext in CodexWidgets.ShortcutHint (z.B. "... setzt ihn auf die Blacklist.") - Alegreya Sans Regular 16px (DESIGN_SPEC-Basis 14px, +2px Nutzervorgabe).</summary>
    public static IFontHandle FontShortcutText => alegreyaShortcutText ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

    private static IFontHandle? cinzelAboutCardTitle;

    /// <summary>Titel der "Charted by Hand"-Karte auf der About-Seite - Cinzel SemiBold 18px (DESIGN_SPEC-Basis 16px, +2px Nutzervorgabe).</summary>
    public static IFontHandle FontAboutCardTitle => cinzelAboutCardTitle ??= BuildHandle("Cinzel.ttf", 18f);

    private static IFontHandle? alegreyaAboutBody;

    /// <summary>Fließtext der "Charted by Hand"-Karte - Alegreya Sans Regular 17px (DESIGN_SPEC-Basis 15px, +2px Nutzervorgabe).</summary>
    public static IFontHandle FontAboutBody => alegreyaAboutBody ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    private static IFontHandle? alegreyaAboutKofiLabel;

    /// <summary>"Support on Ko-fi"-Knopf auf der About-Seite - Alegreya Sans Bold 15px (DESIGN_SPEC).</summary>
    public static IFontHandle FontAboutKofiLabel => alegreyaAboutKofiLabel ??= BuildHandle("AlegreyaSans-Bold.ttf", 15f);

    private static IFontHandle? alegreyaAboutLinkButtonLabel;

    /// <summary>"Report a bug"/"Suggest a feature"-Rahmen-Knöpfe auf der About-Seite - Alegreya Sans Medium 14px (DESIGN_SPEC).</summary>
    public static IFontHandle FontAboutLinkButtonLabel => alegreyaAboutLinkButtonLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 14f);

    private static IFontHandle? cinzelAboutDividerLabel;

    /// <summary>"FOLLOW THE JOURNEY"-Beschriftung der Trennlinie auf der About-Seite - Cinzel SemiBold 14px (DESIGN_SPEC-Basis 11px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontAboutDividerLabel => cinzelAboutDividerLabel ??= BuildHandle("Cinzel.ttf", 14f);

    private static IFontHandle? cinzelAboutTitle;

    /// <summary>Plugin-Name auf der About-Seite ("The Explorer's Codex") - Cinzel Bold 31px (von FontSidebarBrandLarge, 26px, gelöst: +5px Nutzervorgabe, betrifft NUR die About-Seite, nicht das Sidebar-Logo).</summary>
    public static IFontHandle FontAboutTitle => cinzelAboutTitle ??= BuildHandle("Cinzel-Bold.ttf", 31f);

    private static IFontHandle? alegreyaAboutSubtitle;

    /// <summary>Untertitel auf der About-Seite ("Your companion for...") - Alegreya Italic 20px (von FontSubtitleItalic, 15px, gelöst: +5px Nutzervorgabe, betrifft NUR die About-Seite).</summary>
    public static IFontHandle FontAboutSubtitle => alegreyaAboutSubtitle ??= BuildHandle("AlegreyaItalic.ttf", 20f);

    private static IFontHandle? alegreyaAboutVersionBadge;

    /// <summary>Versions-Etikett auf der About-Seite ("Version x.y.z") - Alegreya Sans Regular 18px (von FontBody, 13px, gelöst: +5px Nutzervorgabe, betrifft NUR die About-Seite).</summary>
    public static IFontHandle FontAboutVersionBadge => alegreyaAboutVersionBadge ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f);

    private static IFontHandle? alegreyaAboutHint;

    /// <summary>"Found a bug or have an idea ..."-Hinweistext auf der About-Seite - Alegreya Sans Regular 16px (von FontBodySmall, 14px, gelöst: +2px Nutzervorgabe, betrifft NUR die About-Seite).</summary>
    public static IFontHandle FontAboutHint => alegreyaAboutHint ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

    private static IFontHandle? cinzelStatsLabel;

    /// <summary>Kachel-/Gruppen-Label der Statistics-Seite ("TOTAL DISCOVERED" etc.) - Cinzel SemiBold 17px (DESIGN_SPEC-Basis 11px, +6px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsLabel => cinzelStatsLabel ??= BuildHandle("Cinzel.ttf", 17f);

    private static IFontHandle? cinzelStatsBigNumber;

    /// <summary>Gesamtzahl in der "Total discovered"-Kachel - Cinzel Bold 35px (DESIGN_SPEC-Basis 30px, +5px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsBigNumber => cinzelStatsBigNumber ??= BuildHandle("Cinzel-Bold.ttf", 35f);

    private static IFontHandle? alegreyaStatsSuffix;

    /// <summary>"/ 3,104"-Zusatz in der "Total discovered"-Kachel - Alegreya Sans Regular 15px (DESIGN_SPEC).</summary>
    public static IFontHandle FontStatsSuffix => alegreyaStatsSuffix ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaStatsPercentBig;

    /// <summary>Prozentwert in der "Total discovered"-Kachel - Alegreya Sans Bold 18px (DESIGN_SPEC).</summary>
    public static IFontHandle FontStatsPercentBig => alegreyaStatsPercentBig ??= BuildHandle("AlegreyaSans-Bold.ttf", 18f);

    private static IFontHandle? cinzelStatsMediumNumber;

    /// <summary>Wert in den Kacheln "Furthest along"/"Still to find" - Cinzel Bold 27px (DESIGN_SPEC-Basis 22px, +5px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsMediumNumber => cinzelStatsMediumNumber ??= BuildHandle("Cinzel-Bold.ttf", 27f);

    private static IFontHandle? alegreyaStatsDetail;

    /// <summary>Prozentwert je Kategorie-Zeile ("18.9 %") - Alegreya Sans Regular 16px (DESIGN_SPEC-Basis 13px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsDetail => alegreyaStatsDetail ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

    private static IFontHandle? alegreyaStatsTileDetail;

    /// <summary>Detailzeile der Kacheln "Furthest along"/"Still to find" ("121 of 641 · 18.9%"/"entries across 12 categories") - Alegreya Sans Regular 15px (DESIGN_SPEC-Basis 13px, +2px Nutzervorgabe, von FontStatsDetail gelöst, das weiterhin für die Kategorie-Zeilen gebraucht wird).</summary>
    public static IFontHandle FontStatsTileDetail => alegreyaStatsTileDetail ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? cinzelStatsGroupTitle;

    /// <summary>Gruppen-Überschrift ("Collections"/"Progress") - Cinzel SemiBold 19px (DESIGN_SPEC-Basis 14px, +5px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsGroupTitle => cinzelStatsGroupTitle ??= BuildHandle("Cinzel.ttf", 19f);

    private static IFontHandle? alegreyaStatsBadge;

    /// <summary>Gruppenstand-Badge ("25 / 1,693") - Alegreya Sans Bold 16px (DESIGN_SPEC-Basis 11px, +5px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsBadge => alegreyaStatsBadge ??= BuildHandle("AlegreyaSans-Bold.ttf", 16f);

    private static IFontHandle? alegreyaStatsCategoryName;

    /// <summary>Kategorie-Name je Zeile ("Mount" etc.) - Alegreya Sans Bold 20px (DESIGN_SPEC-Basis 15px, +5px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsCategoryName => alegreyaStatsCategoryName ??= BuildHandle("AlegreyaSans-Bold.ttf", 20f);

    private static IFontHandle? alegreyaStatsCategoryCount;

    /// <summary>Zählung je Kategorie-Zeile ("5 / 192") - Alegreya Sans Regular 18px (DESIGN_SPEC-Basis 15px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontStatsCategoryCount => alegreyaStatsCategoryCount ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f);

    private static IFontHandle? cinzelDebugCardTitle;

    /// <summary>Kartentitel auf der Debug-Seite ("General"/"Current Status"/"Simulation"/"Debug Dumps") - Cinzel SemiBold 20px (DESIGN_SPEC-Basis 14px, +6px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugCardTitle => cinzelDebugCardTitle ??= BuildHandle("Cinzel.ttf", 20f);

    private static IFontHandle? alegreyaDebugRowLabel;

    /// <summary>Zeilentext neben einem Schalter auf der Debug-Seite ("Show debug info in overlay" etc.) - Alegreya Sans Medium 18px (DESIGN_SPEC-Basis 15px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugRowLabel => alegreyaDebugRowLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 18f);

    private static IFontHandle? alegreyaDebugDescription;

    /// <summary>Kleine Beschreibung unter einer Schalter-Zeile auf der Debug-Seite - Alegreya Sans Regular 15px (DESIGN_SPEC-Basis 12px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugDescription => alegreyaDebugDescription ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaDebugButtonLabel;

    /// <summary>"Copy to clipboard"/Debug-Dump-Knöpfe auf der Debug-Seite - Alegreya Sans Medium 13px (DESIGN_SPEC).</summary>
    public static IFontHandle FontDebugButtonLabel => alegreyaDebugButtonLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 13f);

    private static IFontHandle? alegreyaDebugStatusText;

    /// <summary>Bezeichnung/Wert in der "Current Status"-Karte der Debug-Seite - Alegreya Sans Regular 17px (DESIGN_SPEC-Basis 14px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugStatusText => alegreyaDebugStatusText ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f, mergeCjk: true);

    private static IFontHandle? alegreyaDebugBadge;

    /// <summary>"TESTING ONLY"/"ACTIVE"-Badge auf der Debug-Seite - Alegreya Sans Bold 15px (DESIGN_SPEC-Basis 11px, +4px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugBadge => alegreyaDebugBadge ??= BuildHandle("AlegreyaSans-Bold.ttf", 15f);

    private static IFontHandle? alegreyaDebugWarningText;

    /// <summary>Text im Warnkasten der "Simulation"-Karte - Alegreya Sans Regular 15px (DESIGN_SPEC-Basis 13px, +2px Nutzervorgabe).</summary>
    public static IFontHandle FontDebugWarningText => alegreyaDebugWarningText ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaDebugWarningBold;

    /// <summary>Fett hervorgehobener erster Satz im Warnkasten ("Affects automation only.") - Alegreya Sans Bold 15px, dieselbe Größe wie FontDebugWarningText.</summary>
    public static IFontHandle FontDebugWarningBold => alegreyaDebugWarningBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 15f);

    /// <summary>
    /// Festbreitenschrift für Zonen-ID/Koordinaten auf der Debug-Seite - DESIGN_SPEC erlaubt
    /// ausdrücklich den pragmatischen Fallback "Standardschrift nehmen", falls eine eigene Monospace-
    /// Schrift zu aufwendig ist. Dalamud bringt dafür bereits eine fertige, echte Monospace-Schrift
    /// mit (UiBuilder.MonoFontHandle) - kein eigenes .ttf nötig.
    /// </summary>
    public static IFontHandle FontMono12 => Plugin.PluginInterface.UiBuilder.MonoFontHandle;

    private static IFontHandle? cinzelLogHeader;

    /// <summary>Tabellenkopf der Log-Seite (TIME/LEVEL/SOURCE/MESSAGE) - Cinzel 14px (zunächst identisch zu FontDatabaseHeader/21px gemacht, dann Nutzervorgabe: -4px, dann weitere -3px - daher ein eigener Handle statt des geteilten Database-Fonts).</summary>
    public static IFontHandle FontLogTableHeader => cinzelLogHeader ??= BuildHandle("Cinzel.ttf", 14f);

    private static IFontHandle? alegreyaLogRow;

    /// <summary>Zeileninhalt der Log-Konsole (SOURCE/MESSAGE) - Alegreya Sans Regular 16px (DESIGN_SPEC-Basis 13px, +3px Nutzervorgabe), mergeCjk, da MESSAGE rohe Plugin-Log-Texte (inkl. Zonen-/Item-Namen) enthalten kann.</summary>
    public static IFontHandle FontLogRow => alegreyaLogRow ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f, mergeCjk: true);

    private static IFontHandle? alegreyaLogBadge;

    /// <summary>Level-Badge in der Log-Konsole (DBG/INF/WRN/ERR/CRT/VRB) - Alegreya Sans Bold 13px (DESIGN_SPEC-Basis 10px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontLogBadge => alegreyaLogBadge ??= BuildHandle("AlegreyaSans-Bold.ttf", 13f);

    private static IFontHandle? alegreyaLogMeta;

    /// <summary>"{n} of {m} lines"/"Follow new lines"-Meta-Zeile unter den Level-Chips - Alegreya Sans Regular 15px (DESIGN_SPEC-Basis 12px, +3px Nutzervorgabe).</summary>
    public static IFontHandle FontLogMeta => alegreyaLogMeta ??= BuildHandle("AlegreyaSans-Regular.ttf", 15f);

    private static IFontHandle? alegreyaLogChipCount;

    /// <summary>Zähler in einem Level-Chip ("Debug 98") - Alegreya Sans Regular 14px (DESIGN_SPEC-Basis 11px, +3px Nutzervorgabe), kleiner als das Chip-Label selbst (FontLogFilterLabel).</summary>
    public static IFontHandle FontLogChipCount => alegreyaLogChipCount ??= BuildHandle("AlegreyaSans-Regular.ttf", 14f);

    private static IFontHandle? alegreyaLogFilterLabel;

    /// <summary>Beschriftung des "Source: ..."-Dropdowns und der Level-Chip-Labels auf der Log-Seite - Alegreya Sans Medium 16px (von FontFilterButton, 13px, gelöst: +3px Nutzervorgabe, betrifft nur diese Seite).</summary>
    public static IFontHandle FontLogFilterLabel => alegreyaLogFilterLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 16f);

    private static IFontHandle? alegreyaLogSearchInput;

    /// <summary>Such-Eingabefeld der Log-Seite - Alegreya Sans Regular 18px (von FontDatabaseSearchInput, 15px, gelöst: +3px Nutzervorgabe, betrifft nur diese Seite).</summary>
    public static IFontHandle FontLogSearchInput => alegreyaLogSearchInput ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f);

    private static IFontHandle? alegreyaLogGhostButton;

    /// <summary>"Select lines"/"Copy all"/"Clear"-Knöpfe der Log-Seite - Alegreya Sans Medium 17px (von FontFilterButton, 13px, gelöst: +4px Nutzervorgabe, betrifft nur diese Seite).</summary>
    public static IFontHandle FontLogGhostButton => alegreyaLogGhostButton ??= BuildHandle("AlegreyaSans-Medium.ttf", 17f);

    private static IFontHandle? cinzelChangelogVersionTitle;

    /// <summary>Kartentitel einer aufgeklappten Changelog-Version ("Version 1.9.0") - Cinzel Bold 22px (DESIGN_SPEC-Basis 18px, +4px Nutzervorgabe).</summary>
    public static IFontHandle FontChangelogVersionTitle => cinzelChangelogVersionTitle ??= BuildHandle("Cinzel-Bold.ttf", 22f);

    private static IFontHandle? cinzelChangelogCollapsedTitle;

    /// <summary>Kartentitel einer zugeklappten Changelog-Version - Cinzel Bold 15px.</summary>
    public static IFontHandle FontChangelogCollapsedTitle => cinzelChangelogCollapsedTitle ??= BuildHandle("Cinzel-Bold.ttf", 15f);

    private static IFontHandle? alegreyaChangelogMeta;

    /// <summary>Datum/"{n} changes" auf der Changelog-Seite - Alegreya Sans Regular 17px (DESIGN_SPEC-Basis 13px, +4px Nutzervorgabe).</summary>
    public static IFontHandle FontChangelogMeta => alegreyaChangelogMeta ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    private static IFontHandle? alegreyaChangelogChangeText;

    /// <summary>Einzelner Änderungseintrag in einer Versionskarte - Alegreya Sans Regular 18px (DESIGN_SPEC-Basis 14px, +4px Nutzervorgabe), mergeCjk (kann rohe Plugin-/Spieltexte enthalten).</summary>
    public static IFontHandle FontChangelogChangeText => alegreyaChangelogChangeText ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f, mergeCjk: true);

    private static IFontHandle? alegreyaChangelogTag;

    /// <summary>Das "◆ NEW"-Badge im Navigationsmenü - Alegreya Sans Bold 11px. Bewusst NICHT für die Badges auf der Changelog-Seite selbst verwendet (siehe FontChangelogTagBadge), die wurden per Nutzervorgabe separat vergrößert, der kleine Sidebar-Badge sollte dabei unverändert bleiben.</summary>
    public static IFontHandle FontChangelogTag => alegreyaChangelogTag ??= BuildHandle("AlegreyaSans-Bold.ttf", 11f);

    private static IFontHandle? alegreyaChangelogTagBadge;

    /// <summary>NEW/IMPROVED/FIXED/REMOVED-Tags vor einem Änderungseintrag und das "NEW"-Badge neben einem Versionstitel (beide auf der Changelog-Seite selbst) - Alegreya Sans Bold 15px (DESIGN_SPEC-Basis 11px, +4px Nutzervorgabe).</summary>
    public static IFontHandle FontChangelogTagBadge => alegreyaChangelogTagBadge ??= BuildHandle("AlegreyaSans-Bold.ttf", 15f);

    private static IFontHandle? alegreyaCurrencyValue;
    private static IFontHandle? alegreyaCurrencyName;

    /// <summary>Wert in der Währungsübersicht - Alegreya Sans Bold, 19px (Abschnitt 5.4, im Spiel von 14px erhöht).</summary>
    public static IFontHandle FontCurrencyValue => alegreyaCurrencyValue ??= BuildHandle("AlegreyaSans-Bold.ttf", 19f);

    /// <summary>Name in der Währungsübersicht - Alegreya Sans Regular, 19px (Abschnitt 5.4, im Spiel an FontCurrencyValue angeglichen).</summary>
    public static IFontHandle FontCurrencyName => alegreyaCurrencyName ??= BuildHandle("AlegreyaSans-Regular.ttf", 19f, mergeCjk: true);

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
        _ = FontDropdownCaption;
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
        _ = FontBadgeSmall;
        _ = FontPluginGroupLabel;
        _ = FontPluginName;
        _ = FontPluginDescription;
        _ = FontPluginStatusRest;
        _ = FontPluginInstalledLabel;
        _ = FontDatabaseHeader;
        _ = FontDatabaseItemName;
        _ = FontDatabaseItemText;
        _ = FontDatabaseStatusLabel;
        _ = FontDatabaseCountRow;
        _ = FontDatabaseSearchInput;
        _ = FontTabLabel;
        _ = FontTypeBadge;
        _ = FontSidebarBrandSmall;
        _ = FontSidebarBrandLarge;
        _ = FontBlacklistTypeBadge;
        _ = FontBlacklistBody;
        _ = FontBlacklistButtonLabel;
        _ = FontShortcutKey;
        _ = FontShortcutText;
        _ = FontAboutCardTitle;
        _ = FontAboutBody;
        _ = FontAboutKofiLabel;
        _ = FontAboutLinkButtonLabel;
        _ = FontAboutDividerLabel;
        _ = FontAboutTitle;
        _ = FontAboutSubtitle;
        _ = FontAboutVersionBadge;
        _ = FontAboutHint;
        _ = FontStatsLabel;
        _ = FontStatsBigNumber;
        _ = FontStatsSuffix;
        _ = FontStatsPercentBig;
        _ = FontStatsMediumNumber;
        _ = FontStatsDetail;
        _ = FontStatsTileDetail;
        _ = FontStatsGroupTitle;
        _ = FontStatsBadge;
        _ = FontStatsCategoryName;
        _ = FontStatsCategoryCount;
        _ = FontDebugCardTitle;
        _ = FontDebugRowLabel;
        _ = FontDebugDescription;
        _ = FontDebugButtonLabel;
        _ = FontDebugStatusText;
        _ = FontDebugBadge;
        _ = FontDebugWarningText;
        _ = FontDebugWarningBold;
        _ = FontLogTableHeader;
        _ = FontLogRow;
        _ = FontLogBadge;
        _ = FontLogMeta;
        _ = FontLogChipCount;
        _ = FontLogFilterLabel;
        _ = FontLogSearchInput;
        _ = FontLogGhostButton;
        _ = FontChangelogVersionTitle;
        _ = FontChangelogCollapsedTitle;
        _ = FontChangelogMeta;
        _ = FontChangelogChangeText;
        _ = FontChangelogTag;
        _ = FontChangelogTagBadge;
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

        // Nutzervorgabe: der aktuell ausgewählte Eintrag in Dropdown-/Auswahllisten (ImGui.Selectable
        // mit selected=true, z.B. in BeginDropdownRow-Comboboxen) soll golden (Accent) markiert sein
        // statt im (fast schwarzen) ImGui-Standardton.
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Accent);

        // Golden statt der ImGui-Standardfarbe für Schieberegler (z.B. Deckkraft-Slider, Abschnitt 8).
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Accent);
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(19);
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

    /// <summary>
    /// Zeichnet Text mit zusätzlichem Zeichenabstand (Versalien-Spaltennamen der Datenbank-Tabelle,
    /// ~0.08em) - ImGui kennt kein natives Letter-Spacing, daher Zeichen für Zeichen über die
    /// Draw-List gezeichnet. Zeichnet AM AKTUELLEN Cursor (wie ein normales ImGui-Widget) und rückt
    /// den Cursor um die tatsächlich gezeichnete Breite vor (per Dummy), erwartet also die gewünschte
    /// Schrift bereits gepusht (siehe Aufrufer).
    /// </summary>
    public static void DrawSpacedText(string text, Vector4 color, float spacing)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight();
        var x = pos.X;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            drawList.AddText(new Vector2(x, pos.Y), ImGui.GetColorU32(color), s);
            x += ImGui.CalcTextSize(s).X + spacing;
        }

        ImGui.Dummy(new Vector2(MathF.Max(0f, x - pos.X - spacing), height));
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
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;
        var bg = value ? Accent : Hex("#2A2219");
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), rounding);
        if (!value)
            drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(LineControl), rounding);

        // Nutzervorgabe: dezenter Hover-Effekt (wie bei den Auto-Knöpfen im Overlay).
        if (hovered)
            drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), rounding);

        var knobRadius = size.Y / 2f - 3f * scale;
        var knobX = value ? cursor.X + size.X - size.Y / 2f : cursor.X + size.Y / 2f;
        var knobColor = value ? TextOnAccent : TextMuted;
        drawList.AddCircleFilled(new Vector2(knobX, cursor.Y + size.Y / 2f), knobRadius, ImGui.GetColorU32(knobColor));

        if (clicked)
            value = !value;
        return clicked;
    }

    /// <summary>
    /// Schieberegler 0..1 mit golden einfärbendem Füllbalken (Nutzervorgabe: "soll sich gold füllen,
    /// bei 100% komplett gold") - ImGui.SliderFloat zeigt stattdessen nur einen schmalen Griff auf
    /// flachem Hintergrund ohne Füllbalken, daher wie Toggle() komplett selbst gezeichnet
    /// (InvisibleButton + manuelles Rechteck, per Maus-X-Position beim Ziehen aktualisiert). Gibt true
    /// zurück, wenn der Wert sich gerade geändert hat.
    /// </summary>
    public static bool OpacitySlider(string id, ref float value01, Vector2 size)
    {
        value01 = Math.Clamp(value01, 0f, 1f);
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var changed = false;

        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var fraction = Math.Clamp((ImGui.GetMousePos().X - cursor.X) / size.X, 0f, 1f);
            if (MathF.Abs(fraction - value01) > 0.0001f)
            {
                value01 = fraction;
                changed = true;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(BgInput), rounding);

        var fillWidth = size.X * value01;
        if (fillWidth > 0.5f)
            drawList.AddRectFilled(cursor, cursor + new Vector2(fillWidth, size.Y), ImGui.GetColorU32(Accent), rounding);

        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(LineControl), rounding);

        // Nutzervorgabe: dezenter Hover-Effekt (wie beim Toggle und den Auto-Knöpfen im Overlay).
        if (hovered)
            drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), rounding);

        return changed;
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
    private static bool cardAccentBar;

    /// <summary>
    /// Karte im Menü (Abschnitt 6) - BgCard-Hintergrund, LineCard-Rahmen, Innenabstand, Höhe passt
    /// sich dem Inhalt an. Mit EndCard() beenden. "accentBar" (Nutzeranforderung, Plugins-Seite:
    /// ausgewähltes Kampf-Plugin) zeichnet einen 2px goldenen Strich ganz links an der Karte, über die
    /// VOLLE Kartenhöhe (inkl. Innenabstand oben/unten) - deshalb erst in EndCard gezeichnet, wenn die
    /// tatsächliche Höhe feststeht, nicht vom Aufrufer selbst (der kennt nur die Zeilenhöhe, nicht die
    /// der ganzen Karte).
    /// </summary>
    /// <summary>
    /// "rightMargin" (Nutzervorgabe: Debug-Seite soll denselben rechten Randabstand wie die
    /// Statistics-Seite einhalten) begrenzt die Kartenbreite optional - ohne Angabe nutzt die Karte
    /// wie bisher die volle verfügbare Breite. "width" überschreibt die Breite komplett explizit (für
    /// Karten, die NICHT die volle Zeilenbreite einnehmen, z.B. zwei Karten nebeneinander ohne
    /// ImGui.BeginTable, siehe Windows.CodexMenuWindow.DrawDebugPage-Kommentar: ImGui.
    /// GetContentRegionAvail() kennt eine solche manuell aufgeteilte Spaltenbreite nicht von selbst).
    /// </summary>
    public static void BeginCard(float scale, bool accentBar = false, float rightMargin = 0f, float? width = null)
    {
        cardPaddingX = 16f * scale;
        cardPaddingY = 14f * scale;
        cardOrigin = ImGui.GetCursorScreenPos();
        cardWidth = width ?? (ImGui.GetContentRegionAvail().X - rightMargin);
        cardAccentBar = accentBar;

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
        if (cardAccentBar)
            drawList.AddRectFilled(min, new Vector2(min.X + 2f, max.Y), ImGui.GetColorU32(Accent));
        drawList.ChannelsMerge();
    }

    /// <summary>Innerer Innenabstand der aktuell offenen Karte (siehe BeginCard) - für rechtsbündige Breiten per ImGui.SetNextItemWidth(-CardPaddingX), damit Combos auch rechts symmetrisch Abstand zum Kartenrand halten.</summary>
    public static float CardPaddingX => cardPaddingX;

    /// <summary>Oberer/unterer Innenabstand der aktuell offenen Karte (siehe BeginCard) - z.B. für die Changelog-Seite, die die Y-Position der Zeitleisten-Raute auf die Kartenkopfzeile ausrichtet, bevor die Karte selbst gezeichnet wird.</summary>
    public static float CardPaddingY => cardPaddingY;

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
    /// Eine Einstellungszeile: Label links, Schalter rechtsbündig, optionaler Erklärtext darunter
    /// (immer sichtbar, kein Hover-Tooltip mehr - Nutzeranforderung, Vorbild: BeginDropdownRow.caption).
    /// Gibt true zurück, wenn der Schalter gerade umgeschaltet wurde.
    /// </summary>
    public static bool ToggleRow(string id, string label, ref bool value, float scale, string? caption = null)
    {
        var toggleHeight = 22f * scale;
        var toggleWidth = 42f * scale;

        var rowStartY = ImGui.GetCursorPosY();
        using (FontMenuFieldLabel.Push())
            ImGui.TextColored(TextPrimary, label);

        // Umbruch 5px vor dem Toggle-Knopf (Nutzervorgabe), damit lange Erklärtexte nicht unter den
        // Knopf selbst laufen.
        if (!string.IsNullOrEmpty(caption))
        {
            var wrapX = ImGui.GetWindowContentRegionMax().X - toggleWidth - 25f * scale - 5f * scale;
            using (FontDropdownCaption.Push())
            {
                ImGui.PushTextWrapPos(wrapX);
                ImGui.TextColored(TextTertiary, caption);
                ImGui.PopTextWrapPos();
            }
        }

        var textBottomY = ImGui.GetCursorPosY();
        var textHeight = textBottomY - rowStartY;

        var toggleY = rowStartY + (textHeight - toggleHeight) / 2f;
        // Derselbe rechte Randabstand wie bei den Dropdowns (siehe BeginDropdownRow rightMargin,
        // Nutzervorgabe).
        var rightX = ImGui.GetWindowContentRegionMax().X - toggleWidth - 25f * scale;

        ImGui.SetCursorPos(new Vector2(rightX, toggleY));
        var changed = Toggle(id, ref value, scale);

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
        string? tooltip = null, string? caption = null, bool disabled = false, float width = 205f, float rightMargin = 25f)
    {
        var comboWidth = width * scale;

        var rowStartY = ImGui.GetCursorPosY();
        using (FontMenuFieldLabel.Push())
            ImGui.TextColored(disabled ? TextDisabled : TextPrimary, label);
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered(disabled ? ImGuiHoveredFlags.AllowWhenDisabled : ImGuiHoveredFlags.None))
            ImGui.SetTooltip(tooltip);

        // Optionaler, immer sichtbarer Erklärtext unter dem Label (Nutzeranforderung, Vorbild:
        // Erklärtext unter "Währungen anzeigen") - anders als "tooltip" (nur beim Hover sichtbar).
        // Zählt mit in die Zeilenhöhe, die Combobox zentriert sich dadurch automatisch über
        // Label+caption zusammen. Umbruch 5px vor der Combobox (Nutzervorgabe), damit lange Texte
        // nicht unter die Combobox selbst laufen.
        if (!string.IsNullOrEmpty(caption))
        {
            var wrapX = ImGui.GetWindowContentRegionMax().X - comboWidth - rightMargin * scale - 5f * scale;
            using (FontDropdownCaption.Push())
            {
                ImGui.PushTextWrapPos(wrapX);
                ImGui.TextColored(TextTertiary, caption);
                ImGui.PopTextWrapPos();
            }
        }

        var textBottomY = ImGui.GetCursorPosY();
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

        // Nutzervorgabe: der Hover-Effekt soll nicht auf dem kleinen Pfeil-Knopf der Combobox
        // erscheinen (der intern dieselben ImGuiCol.Button-Farben wie echte Knöpfe nutzt, siehe
        // PushStyle) - hier lokal auf "keine Füllung" zurückgesetzt, nur für den BeginCombo-Aufruf
        // selbst (der Pfeil wird ausschließlich darin gezeichnet).
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0f));
        var open = ImGui.BeginCombo(comboId, currentValueLabel);
        ImGui.PopStyleColor(2);

        if (open)
            // +2px Innenabstand oben/unten (Nutzervorgabe) auf die bisherigen 5px.
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f * scale, 7f * scale));
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
    /// Für CardGroupLabel(lineWidth:) - damit die Trennlinie unter dem Kartentitel exakt dort endet,
    /// wo ToggleRow/BeginDropdownRow ihr Steuerelement enden lassen (beide nutzen denselben 25px
    /// rechten Randabstand, Nutzervorgabe), statt über die volle Kartenbreite zu gehen.
    /// </summary>
    public static float CardFieldLineWidth(float scale, float rightMargin = 25f) =>
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
