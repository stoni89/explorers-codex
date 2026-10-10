using System;
using System.Numerics;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Bindings.ImGui;
using PluginUiKit;

namespace TheExplorersCodex;

/// <summary>
/// Codex-Theme - seit der PluginUiKit-Extraktion (siehe C:\FF14Mods\PluginUiKit\README.md) nur
/// noch eine dünne Weiterleitungsschicht auf PluginUiKit.UiTheme/UiFonts/UiWidgets statt einer
/// eigenen Implementierung (Schritt 3 "Codex auf das Paket umstellen"). Alle Aufrufstellen im
/// restlichen Projekt (CodexMenuWindow.cs, CodexOverlayWindow.cs, CodexWidgets.cs) bleiben
/// UNVERÄNDERT bei "CodexTheme.X" - das Aussehen darf sich durch die Umstellung nicht ändern,
/// daher bleiben an den wenigen Stellen, wo der Schritt-1-Bestandsaufnahme-Bericht abweichende
/// Werte zwischen Codex und dem PluginUiKit-Standarddefault gefunden hat (z.B. RoundingControl
/// 3.5f vs. UiMetrics.ControlRadius 3f), Codex' ORIGINALWERTE als eigene Literale erhalten statt
/// sie auf den Kit-Default umzubiegen - diese Vereinheitlichung ist eine separate, noch offene
/// Entscheidung (siehe Bestandsaufnahme), keine stille Nebenwirkung dieser Umstellung.
///
/// Farb-/Fonttokens, die exakt denselben Hex-Wert wie ein PluginUiKit.UiTheme-Token haben, wurden
/// hier entfernt - Aufrufstellen, die sie nutzten, zeigen jetzt direkt auf UiTheme.Active.X (z.B.
/// LogInfoFg -> UiTheme.Active.InfoFg, beide waren schon immer #9CC4E4).
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

    // ---- Flächen - 1:1 Weiterleitung auf UiTheme.Active (siehe Klassenkommentar) ----
    public static Vector4 BgWindow => UiTheme.Active.BgWindow;
    public static Vector4 BgSidebar => UiTheme.Active.BgSidebar;
    public static Vector4 BgCard => UiTheme.Active.BgCard;
    public static Vector4 BgInput => UiTheme.Active.BgInput;
    public static Vector4 BgPopup => UiTheme.Active.BgPopup;
    public static Vector4 BgSelected => UiTheme.Active.BgSelected;
    public static Vector4 BgSelectedStrong => UiTheme.Active.BgSelectedStrong;

    // ---- Codex-eigene Zusatzflächen (kein PluginUiKit-Äquivalent - Overlay-Transparenz ist
    // Codex-spezifisches Verhalten, nicht Teil der generischen Palette) ----
    public static readonly Vector4 BgOuter = Hex("#0B0906");
    private static readonly Vector4 BgOverlayBase = Hex("#16110B");

    /// <summary>Overlay-Hintergrund mit der aktuell eingestellten Transparenz (0 = voll deckend, 1 = unsichtbar).</summary>
    public static Vector4 OverlayBg(float transparency) => BgOverlayBase with { W = 0.93f * (1f - Math.Clamp(transparency, 0f, 1f)) };

    /// <summary>Hintergrund für Knöpfe ohne eigene Füllung, sobald ab ~70% Overlay-Transparenz Textschatten aktiv sind.</summary>
    public static readonly Vector4 TranslucentButtonBg = BgOverlayBase with { W = 0.7f };

    // ---- Linien ----
    public static Vector4 LineFrame => UiTheme.Active.LineFrame;
    public static Vector4 LineControl => UiTheme.Active.LineControl;
    public static Vector4 LineCard => UiTheme.Active.LineCard;
    public static Vector4 LineSubtle => UiTheme.Active.LineSubtle;
    public static Vector4 LineRow => UiTheme.Active.LineRow;
    public static Vector4 LineDisabled => UiTheme.Active.LineDisabled;

    // ---- Text ----
    public static Vector4 TextHeading => UiTheme.Active.TextHeading;
    public static Vector4 TextPrimary => UiTheme.Active.TextPrimary;
    public static Vector4 TextCardTitle => UiTheme.Active.TextCardTitle;
    public static Vector4 TextValue => UiTheme.Active.TextValue;
    public static Vector4 TextSecondary => UiTheme.Active.TextSecondary;
    public static Vector4 TextTertiary => UiTheme.Active.TextTertiary;
    public static Vector4 TextMuted => UiTheme.Active.TextMuted;
    public static Vector4 TextDim => UiTheme.Active.TextDim;
    public static Vector4 TextDisabled => UiTheme.Active.TextDisabled;
    public static Vector4 TextOnAccent => UiTheme.Active.TextOnAccent;

    // ---- Akzent und Status ----
    public static readonly Vector4 AccentGold = Hex("#D4A94F");
    public static readonly Vector4 AccentEmerald = Hex("#5FB98A");
    public static readonly Vector4 AccentArcane = Hex("#9C8CF0");
    public static readonly Vector4 AccentEmber = Hex("#E0785A");

    /// <summary>Aktuell eingestellte Akzentfarbe (Standard: Gold) - weitergeleitet auf UiTheme.Active.Accent, damit ein Wechsel hier UND im Kit konsistent bleibt.</summary>
    public static Vector4 Accent
    {
        get => UiTheme.Active.Accent;
        set => UiTheme.Active.Accent = value;
    }

    public static Vector4 OkFg => UiTheme.Active.OkFg;
    public static Vector4 OkBg => UiTheme.Active.OkBg;
    public static Vector4 OkLine => UiTheme.Active.OkLine;
    public static Vector4 WarnFg => UiTheme.Active.WarnFg;
    public static Vector4 WarnBg => UiTheme.Active.WarnBg;
    public static Vector4 WarnLine => UiTheme.Active.WarnLine;
    public static Vector4 ErrFg => UiTheme.Active.ErrFg;
    public static Vector4 ErrBg => UiTheme.Active.ErrBg;
    public static Vector4 ErrLine => UiTheme.Active.ErrLine;
    public static readonly Vector4 TooltipLine = Hex("#8A6E3A");

    // "FIXED"-Tag (Changelog) / Log-Info-Level: #9CC4E4/#18242E/#2C4458 sind exakt UiTheme.Active.
    // InfoFg/InfoBg/InfoLine - Aufrufstellen zeigen jetzt direkt dorthin (keine eigenen Tokens mehr).

    // ---- Log-Seite: Critical-Level kommt in keiner anderen Seite vor, kein Kit-Äquivalent. ----
    public static readonly Vector4 LogCritFg = Hex("#F06A7A");

    // ---- Typ-Etiketten ----
    public static readonly Vector4 MountBadgeBg = Hex("#3A2F1E");
    public static readonly Vector4 MountBadgeFg = Hex("#E9C46A");
    public static readonly Vector4 MinionBadgeBg = Hex("#26223A");
    public static readonly Vector4 MinionBadgeFg = Hex("#C3B8F5");

    // ---- Abstände und Formen ----
    public const float OverlayWidth = 450f;
    public const float OverlayPaddingX = 14f;
    public const float OverlayRowGap = 2.5f;
    public const float SidebarWidth = UiMetrics.SidebarWidth;

    public const float RoundingOverlay = UiMetrics.OverlayRadius;
    public const float RoundingCard = UiMetrics.CardRadius;

    /// <summary>Bewusst NICHT auf UiMetrics.ControlRadius (3f) umgebogen - Codex nutzt seit jeher 3.5f
    /// an dieser einen Stelle (Nav-Item-Auswahl); die übrigen ~15 Badge-/Chip-Stellen im Projekt
    /// nutzen ohnehin einen eigenen 3f-Literal statt dieser Konstante (siehe Bestandsaufnahme) - die
    /// Vereinheitlichung ist eine offene, separate Entscheidung.</summary>
    public const float RoundingControl = 3.5f;

    public const float RoundingWindow = UiMetrics.WindowRadius;

    /// <summary>Zierecken in jeder Fensterecke - Länge/Dicke/Randabstand für Overlay bzw. Menü.</summary>
    public const float CornerLenOverlay = UiMetrics.CornerLenOverlay;
    public const float CornerThickOverlay = UiMetrics.CornerThickOverlay;
    public const float CornerInsetOverlay = UiMetrics.CornerInsetOverlay;
    public const float CornerLenMenu = UiMetrics.CornerLenMenu;
    public const float CornerThickMenu = UiMetrics.CornerThickMenu;
    public const float CornerInsetMenu = UiMetrics.CornerInsetMenu;

    /// <summary>Breite des Trenn-Ornaments unter Seitentiteln.</summary>
    public const float DividerOrnamentWidth = UiMetrics.DividerOrnamentWidth;

    // Die beiden Ornament-Zeichner (goldene L-Striche in Menü-/Overlay-Größe) - siehe
    // DrawCornerOrnaments weiter unten.
    private static readonly CodexOrnaments MenuOrnaments = new(CornerLenMenu, CornerThickMenu);
    private static readonly CodexOrnaments OverlayOrnaments = new(CornerLenOverlay, CornerThickOverlay);

    // ---- Schriften - Aufbau jetzt über PluginUiKit.UiFonts.BuildHandle (identische Atlas-/CJK-
    // Merge-Logik, siehe dessen Kommentar) statt einer eigenen Implementierung. ----
    private static IFontHandle BuildHandle(string fileName, float sizePx, bool mergeCjk = false) =>
        UiFonts.BuildHandle(fileName, sizePx, mergeCjk);

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

    private static IFontHandle? alegreyaOverlayStatusText;

    /// <summary>Automation-Statuszeile im neuen Overlay (z.B. "Laufe zu..."/"Walking to...", siehe CodexOverlayWindow.
    /// DrawAutomationStatusText) - Nutzervorgabe: +2px gegenüber FontBodySmall, betrifft NUR diese eine Zeile.
    /// Alegreya Sans, 16px.</summary>
    public static IFontHandle FontOverlayStatusText => alegreyaOverlayStatusText ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

    /// <summary>Fließtext/Knöpfe/Listen Medium - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Menü-Navigationspunkt von 13px erhöht).</summary>
    public static IFontHandle FontBodyMedium => alegreyaSansMedium ??= BuildHandle("AlegreyaSans-Medium.ttf", 18f);

    /// <summary>Fließtext/Knöpfe/Listen Bold - Alegreya Sans 18px (Abschnitt 3, im Spiel für den Item-Namen von 13px erhöht).</summary>
    public static IFontHandle FontBodyBold => alegreyaSansBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 18f, mergeCjk: true);

    private static IFontHandle? alegreyaAutoButtonRegular;
    private static IFontHandle? alegreyaAutoButtonBold;

    // Nutzer-Feedback: eigenständig von FontBody/FontBodyBold gelöst und auf 14px vergrößert - die
    // beiden bleiben bei 13px für den Rest des Overlays (z.B. Item-Liste), nur die Auto-Knöpfe
    // sollen größer werden.
    /// <summary>Label der Auto-Knöpfe - Alegreya Sans Bold, 16px (Nutzervorgabe: nochmal +2px, war 14px).</summary>
    public static IFontHandle FontAutoButtonLabel => alegreyaAutoButtonBold ??= BuildHandle("AlegreyaSans-Bold.ttf", 16f);

    /// <summary>Zahl der Auto-Knöpfe - Alegreya Sans Regular, 16px (Nutzervorgabe: nochmal +2px, war 14px).</summary>
    public static IFontHandle FontAutoButtonCount => alegreyaAutoButtonRegular ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

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

    private static IFontHandle? alegreyaOrderPosition;

    /// <summary>Positionsnummer je Zeile auf der "Order"-Seite - Alegreya Sans Bold, 13px.</summary>
    public static IFontHandle FontOrderPosition => alegreyaOrderPosition ??= BuildHandle("AlegreyaSans-Bold.ttf", 13f);

    private static IFontHandle? alegreyaOrderName;

    /// <summary>Kategoriename je Zeile auf der "Order"-Seite - Alegreya Sans Bold, 15px.</summary>
    public static IFontHandle FontOrderName => alegreyaOrderName ??= BuildHandle("AlegreyaSans-Bold.ttf", 15f);

    private static IFontHandle? alegreyaOrderHint;

    /// <summary>"Drag ⠿ or use the arrows"-Hinweis im Kartenkopf der "Order"-Seite - Alegreya Sans Regular, 12px.</summary>
    public static IFontHandle FontOrderHint => alegreyaOrderHint ??= BuildHandle("AlegreyaSans-Regular.ttf", 12f);

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
    public static IFontHandle FontDebugStatusText => alegreyaDebugStatusText ??= BuildHandle("AlegreyaSans-Regular.ttf", 17f);

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
    public static IFontHandle FontLogRow => alegreyaLogRow ??= BuildHandle("AlegreyaSans-Regular.ttf", 16f);

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
    public static IFontHandle FontChangelogChangeText => alegreyaChangelogChangeText ??= BuildHandle("AlegreyaSans-Regular.ttf", 18f);

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

    private static IFontHandle? cinzelCollapsedBrandSmall;
    private static IFontHandle? cinzelCollapsedBrandLarge;
    private static IFontHandle? alegreyaCollapsedPageLabel;

    /// <summary>Eingeklappte Mini-Leiste (Nutzeranforderung) - "THE EXPLORER'S", Cinzel 10px.</summary>
    public static IFontHandle FontCollapsedBrandSmall => cinzelCollapsedBrandSmall ??= BuildHandle("Cinzel.ttf", 10f);

    /// <summary>Eingeklappte Mini-Leiste (Nutzeranforderung) - "Codex", Cinzel Bold 19px.</summary>
    public static IFontHandle FontCollapsedBrandLarge => cinzelCollapsedBrandLarge ??= BuildHandle("Cinzel-Bold.ttf", 19f);

    /// <summary>Eingeklappte Mini-Leiste (Nutzeranforderung) - " · {Seitenname}", Alegreya Sans Medium 13px.</summary>
    public static IFontHandle FontCollapsedPageLabel => alegreyaCollapsedPageLabel ??= BuildHandle("AlegreyaSans-Medium.ttf", 13f);

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
        _ = FontOrderPosition;
        _ = FontOrderName;
        _ = FontOrderHint;
        _ = FontZoneName;
        _ = FontSectionLabel;
        _ = FontBody;
        _ = FontBodySmall;
        _ = FontDropdownCaption;
        _ = FontOverlayStatusText;
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
        _ = FontCollapsedBrandSmall;
        _ = FontCollapsedBrandLarge;
        _ = FontCollapsedPageLabel;
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

    /// <summary>Zierecken in Menü-Größe (CornerLenMenu/ThickMenu) - zwei kurze Akzentlinien je Fensterecke, die ein L bilden.</summary>
    public static void DrawMenuCornerOrnaments(float inset) => UiWidgets.DrawWindowCorners(MenuOrnaments, inset);

    /// <summary>Hintergrund+Eckverzierungen der eingeklappten Menü-Mini-Leiste (Nutzeranforderung) -
    /// dieselben Eckverzierungen wie das ausgeklappte Menü (MenuOrnaments), damit beide Zustände
    /// optisch zusammengehören.</summary>
    public static void DrawCollapsedMenuChrome(float scale, float inset) => UiWindowControls.DrawChrome(scale, MenuOrnaments, inset, RoundingWindow);

    /// <summary>Zierecken in Overlay-Größe (CornerLenOverlay/ThickOverlay) - zwei kurze Akzentlinien je Fensterecke, die ein L bilden.</summary>
    public static void DrawOverlayCornerOrnaments(float inset) => UiWidgets.DrawWindowCorners(OverlayOrnaments, inset);

    /// <summary>Trenn-Ornament - zwei Linien mit kleiner Raute in der Mitte, mittig unter dem Cursor über DividerOrnamentWidth gezeichnet.</summary>
    public static void DrawDividerOrnament() => UiWidgets.DrawDividerOrnament(MenuOrnaments, 1f);

    /// <summary>Zeichnet Text mit dunklem Schatten (ab ~70% Overlay-Transparenz).</summary>
    public static void TextShadowed(string text, Vector4 color, bool shadow) => UiWidgets.TextShadowed(text, color, shadow);

    /// <summary>Wie <see cref="TextShadowed"/>, aber für Text, der bereits direkt per drawList.AddText an einer manuell berechneten Position gezeichnet wird.</summary>
    public static void DrawTextShadowed(ImDrawListPtr drawList, Vector2 pos, Vector4 color, string text, bool shadow) =>
        UiWidgets.DrawTextShadowed(drawList, pos, color, text, shadow);

    /// <summary>Zeichnet Text mit zusätzlichem Zeichenabstand (Letter-Spacing).</summary>
    public static void DrawSpacedText(string text, Vector4 color, float spacing) => UiWidgets.DrawSpacedText(text, color, spacing);

    /// <summary>Abschnitts-Label - Versalien, Cinzel SemiBold, leicht gesperrt gesetzt.</summary>
    public static void SectionLabel(string text, bool shadow = false)
    {
        using (FontSectionLabel.Push())
            UiWidgets.TextShadowed(text.ToUpperInvariant(), TextTertiary, shadow);
    }

    /// <summary>Schalter, 42x22px, selbst gezeichnet und per InvisibleButton klickbar.</summary>
    public static bool Toggle(string id, ref bool value, float scale) => UiWidgets.Toggle(id, ref value, scale);

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

    /// <summary>Karte mit inhaltsabhängiger Höhe - BgCard-Hintergrund, LineCard-Rahmen. Mit EndCard() beenden.</summary>
    public static void BeginCard(float scale, bool accentBar = false, float rightMargin = 0f, float? width = null) =>
        UiWidgets.BeginCard(scale, accentBar, rightMargin, width);

    public static void EndCard() => UiWidgets.EndCard();

    /// <summary>Innerer Innenabstand der aktuell offenen Karte (siehe BeginCard).</summary>
    public static float CardPaddingX => UiWidgets.CardPaddingX;

    /// <summary>Oberer/unterer Innenabstand der aktuell offenen Karte (siehe BeginCard).</summary>
    public static float CardPaddingY => UiWidgets.CardPaddingY;

    /// <summary>Kartentitel - wie SectionLabel, darunter eine Trennlinie. "lineWidth" begrenzt die Linie optional.</summary>
    public static void CardGroupLabel(string text, float scale, float? lineWidth = null) =>
        UiWidgets.CardGroupLabel(text, scale, lineWidth, FontCardTitle);

    /// <summary>Eine Einstellungszeile: Label links, Schalter rechtsbündig, optionaler Erklärtext darunter.
    /// Eigene Schriftarten explizit übergeben statt UiWidgets.ToggleRow seine Kit-Standardschriften nehmen zu lassen
    /// (sonst würde das innere Push in UiWidgets ein äußeres CodexTheme-Font-Push überschreiben - Codex' Schriftgrößen weichen von den Kit-Defaults ab).</summary>
    public static bool ToggleRow(string id, string label, ref bool value, float scale, string? caption = null) =>
        UiWidgets.ToggleRow(id, label, ref value, scale, caption, FontMenuFieldLabel, FontDropdownCaption);

    /// <summary>Zeilenlayout + einheitliches Aussehen für eine Menü-Combo. Muss IMMER mit EndDropdownRow() abgeschlossen werden.</summary>
    public static bool BeginDropdownRow(string label, string currentValueLabel, string comboId, float scale,
        string? tooltip = null, string? caption = null, bool disabled = false, float width = 205f, float rightMargin = 25f) =>
        UiWidgets.BeginDropdownRow(label, currentValueLabel, comboId, scale, tooltip, caption, disabled, width, rightMargin,
            FontMenuFieldLabel, FontDropdownCaption, FontMenuDropdownValue);

    /// <summary>Gegenstück zu BeginDropdownRow - IMMER aufrufen, auch wenn das Dropdown gerade nicht offen ist.</summary>
    public static void EndDropdownRow(bool wasOpen, bool disabled) => UiWidgets.EndDropdownRow(wasOpen, disabled);

    /// <summary>Für CardGroupLabel(lineWidth:) - damit die Trennlinie unter dem Kartentitel exakt dort endet, wo ToggleRow/BeginDropdownRow ihr Steuerelement enden lassen.</summary>
    public static float CardFieldLineWidth(float scale, float rightMargin = 25f) => UiWidgets.CardFieldLineWidth(scale, rightMargin);

    /// <summary>Feine Trennlinie innerhalb einer Karte, mit etwas vertikalem Abstand.</summary>
    public static void CardDivider(float scale) => UiWidgets.CardDivider(scale);

    /// <summary>Kompass-Symbol: Kreis-Umriss plus gefüllte Raute (Nadel) in der Akzentfarbe. Gemeinsam von Overlay-Kopfzeile und Menü-Logo genutzt.</summary>
    public static void DrawCompassIcon(float size) => MenuOrnaments.DrawSidebarLogo(size);
}
