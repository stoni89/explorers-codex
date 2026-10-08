using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using PluginUiKit;

namespace TheExplorersCodex;

/// <summary>
/// Zusammengesetzte Codex-Widgets - seit der PluginUiKit-Extraktion (siehe
/// C:\FF14Mods\PluginUiKit\README.md) nur noch eine dünne Weiterleitungsschicht auf
/// PluginUiKit.UiWidgetsExtra statt einer eigenen Implementierung (Schritt 3 "Codex auf das Paket
/// umstellen"). Eigene Codex-Fonts werden explizit durchgereicht statt UiWidgetsExtra seine
/// Kit-Standardschriften nehmen zu lassen - sonst würde ein inneres Font-Push im Kit ein äußeres
/// CodexTheme-Font-Push überschreiben, da Codex an mehreren Stellen von den Kit-Default-Größen
/// abweicht (siehe CodexTheme-Klassenkommentar, derselbe Grund).
/// </summary>
public static class CodexWidgets
{
    /// <summary>Hinweis-Karte mit Tastenkürzel. "maxWidth" begrenzt die Kartenbreite optional (sonst volle verfügbare Breite).</summary>
    public static void ShortcutHint(IReadOnlyList<string> keys, string text, float? maxWidth = null) =>
        UiWidgetsExtra.ShortcutHint(keys, text, maxWidth, CodexTheme.FontShortcutKey, CodexTheme.FontShortcutText);

    /// <summary>Rückt den Cursor so weit nach rechts, dass ein als Nächstes gezeichnetes Element mit der Breite "itemWidth" horizontal zentriert ist.</summary>
    public static void CenterNext(float itemWidth, float? availableWidth = null) => UiWidgetsExtra.CenterNext(itemWidth, availableWidth);

    /// <summary>Rundes Logo mit zwei konzentrischen Ringen (innerer Ring Accent, äußerer LineControl) um das Kompass-Symbol - zentriert sich selbst über CenterNext.
    /// "size" ist der reine Bilddurchmesser OHNE die beiden Ringe (die kommen zusätzlich oben drauf).</summary>
    public static void SealLogo(float size, float? availableWidth = null)
    {
        var s = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
        size *= s;
        float inner = 5f * s, outer = 6f * s;
        var total = size + (inner + outer) * 2f;
        CenterNext(total, availableWidth);
        var p = ImGui.GetCursorScreenPos();
        var c = p + new Vector2(total / 2f);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(c - new Vector2(size / 2f));
        CodexTheme.DrawCompassIcon(size);

        dl.AddCircle(c, size / 2f + inner, ImGui.GetColorU32(CodexTheme.Accent), 64, 1f * s);
        dl.AddCircle(c, size / 2f + inner + outer, ImGui.GetColorU32(CodexTheme.LineControl), 64, 1f * s);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(total, total));
    }

    /// <summary>Um 45° gedrehtes Quadrat (Raute).</summary>
    public static void Diamond(Vector2 center, float size, uint fill, uint border) => UiWidgetsExtra.Diamond(center, size, fill, border);

    /// <summary>Fließtext mit zentrierten Zeilen - erwartet die gewünschte Schrift bereits gepusht.</summary>
    public static void CenteredWrappedText(string text, float maxWidth, uint color, float? centerLocalX = null, float? availableWidth = null) =>
        UiWidgetsExtra.CenteredWrappedText(text, maxWidth, color, centerLocalX, availableWidth);

    /// <summary>Trennlinie mit mittiger Beschriftung - erwartet die gewünschte Schrift bereits gepusht.</summary>
    public static void LabeledDivider(string label, float width, float? availableWidth = null) =>
        UiWidgetsExtra.LabeledDivider(label, width, availableWidth);

    /// <summary>Gefüllter Fortschrittsbalken - abgerundete Enden, Füllung bei fraction &gt; 0 mindestens 4px breit.</summary>
    public static void ProgressBar(float fraction, float height, string? tooltip = null, float? width = null) =>
        UiWidgetsExtra.ProgressBar(fraction, height, tooltip, width);

    /// <summary>Fließende Knopfreihe mit Umbruch (Debug-Seite, "Debug Dumps").</summary>
    public static void FlowButtons(IReadOnlyList<(string Label, Action OnClick)> items, float gap, Func<string, bool>? isConfirmed = null) =>
        UiWidgetsExtra.FlowButtons(items, gap, isConfirmed, FontAwesomeIcon.Terminal, CodexTheme.FontDebugButtonLabel);
}
