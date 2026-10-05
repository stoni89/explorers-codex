using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace TheExplorersCodex;

/// <summary>
/// Wiederverwendbare, zusammengesetzte Codex-Widgets, die aus mehreren von Hand gezeichneten Teilen
/// bestehen (anders als die einzelnen Low-Level-Bausteine in <see cref="CodexTheme"/> wie BeginCard/
/// Toggle/OpacitySlider) - bewusst eine eigene Klasse, damit CodexTheme reines Farb-/Schrift-/
/// Abstands-Theme bleibt.
/// </summary>
public static class CodexWidgets
{
    /// <summary>
    /// Hinweis-Karte mit Tastenkürzel (z.B. "Ctrl" + "Shift" + "Click" ... Text) - volle Breite des
    /// Inhaltsbereichs, BgPopup-Hintergrund, LineCard-Rahmen. Es gibt dafür kein fertiges ImGui-
    /// Element, daher komplett über ImGui.GetWindowDrawList() gezeichnet und per ImGui.Dummy danach
    /// im Layout reserviert (gleiches Muster wie CodexTheme.BeginCard/EndCard). Aktuell nur von der
    /// Blacklist-Seite verwendet (Werkzeugzeile UND leerer Zustand, siehe
    /// Windows.CodexMenuWindow.DrawBlacklistPage/DrawBlacklistEmptyState), aber bewusst allgemein
    /// gehalten (beliebige Tastenliste), falls weitere Seiten ein Tastenkürzel bewerben wollen.
    /// "maxWidth" (Nutzervorgabe) begrenzt die Kartenbreite optional - ohne Angabe nutzt die Karte wie
    /// bisher die volle verfügbare Breite.
    /// </summary>
    public static void ShortcutHint(IReadOnlyList<string> keys, string text, float? maxWidth = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Min(ImGui.GetContentRegionAvail().X, maxWidth ?? float.MaxValue);

        float padX = 16f * s, padY = 12f * s;
        float keyPadX = 7f * s, keyPadY = 2f * s;
        float plusGap = 5f * s, textGap = 14f * s;

        float keyTextH, bodyH;
        using (CodexTheme.FontShortcutKey.Push())
            keyTextH = ImGui.GetTextLineHeight();
        using (CodexTheme.FontShortcutText.Push())
            bodyH = ImGui.GetTextLineHeight();
        // +1px für die dickere Unterkante (siehe weiter unten, "echte Taste"-Look).
        var keyH = keyTextH + keyPadY * 2f + 1f * s;
        var height = MathF.Max(keyH, bodyH) + padY * 2f;
        var midY = origin.Y + height / 2f;

        var cardMax = origin + new Vector2(width, height);
        dl.AddRectFilled(origin, cardMax, ImGui.GetColorU32(CodexTheme.BgPopup), 6f * s);
        dl.AddRect(origin, cardMax, ImGui.GetColorU32(CodexTheme.LineCard), 6f * s, ImDrawFlags.None, 1f * s);

        var x = origin.X + padX;

        using (CodexTheme.FontShortcutKey.Push())
        {
            for (var i = 0; i < keys.Count; i++)
            {
                var tw = ImGui.CalcTextSize(keys[i]).X;
                var kMin = new Vector2(x, midY - keyH / 2f);
                var kMax = new Vector2(x + tw + keyPadX * 2f, midY + keyH / 2f);

                dl.AddRectFilled(kMin, kMax, ImGui.GetColorU32(CodexTheme.BgInput), 3f * s);
                dl.AddRect(kMin, kMax, ImGui.GetColorU32(CodexTheme.LineFrame), 3f * s, ImDrawFlags.None, 1f * s);
                // Unterkante doppelt so dick wie der Rest des Rahmens, damit die Taste wie eine echte
                // (gedrückte) Taste wirkt (DESIGN_SPEC).
                dl.AddLine(new Vector2(kMin.X + 3f * s, kMax.Y - 1f * s),
                    new Vector2(kMax.X - 3f * s, kMax.Y - 1f * s),
                    ImGui.GetColorU32(CodexTheme.LineFrame), 2f * s);
                dl.AddText(new Vector2(kMin.X + keyPadX, kMin.Y + keyPadY), ImGui.GetColorU32(CodexTheme.TextHeading), keys[i]);

                x = kMax.X;
                if (i < keys.Count - 1)
                {
                    x += plusGap;
                    dl.AddText(new Vector2(x, midY - keyTextH / 2f), ImGui.GetColorU32(CodexTheme.TextDim), "+");
                    x += ImGui.CalcTextSize("+").X + plusGap;
                }
            }
        }

        x += textGap;
        using (CodexTheme.FontShortcutText.Push())
            dl.AddText(new Vector2(x, midY - bodyH / 2f), ImGui.GetColorU32(CodexTheme.TextSecondary), text);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// Rückt den Cursor so weit nach rechts, dass ein als Nächstes gezeichnetes Element mit der Breite
    /// "itemWidth" horizontal zentriert ist - vor dem jeweiligen Element aufzurufen (zentriert nicht
    /// rückwirkend). Ohne "availableWidth" zentriert sich das Element zum aktuell verfügbaren Bereich
    /// (ImGui.GetContentRegionAvail) - mit explizitem Wert (z.B. der Innenbreite einer Karte, siehe
    /// Windows.CodexMenuWindow.DrawAboutCard) lässt sich stattdessen gezielt gegen eine kleinere,
    /// lokale Breite zentrieren, relativ zur aktuellen Cursor-X-Position (siehe Aufrufer, der den
    /// Cursor dafür zuerst auf den linken Rand dieses lokalen Bereichs setzt).
    /// </summary>
    public static void CenterNext(float itemWidth, float? availableWidth = null)
    {
        var avail = availableWidth ?? ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (avail - itemWidth) / 2f));
    }

    /// <summary>
    /// Rundes Logo mit zwei konzentrischen Ringen (About-Seite, DESIGN_SPEC: innerer Ring in Accent,
    /// äußerer in LineControl) - zentriert sich selbst über CenterNext. "size" ist der reine
    /// Bilddurchmesser OHNE die beiden Ringe (die kommen zusätzlich oben drauf). "availableWidth" wie
    /// bei CenterNext (Nutzervorgabe: alle Elemente der About-Seite zentrieren sich zur selben
    /// Referenzbreite wie die "Charted by Hand"-Box, siehe Windows.CodexMenuWindow.GetAboutCardWidth).
    /// </summary>
    public static void SealLogo(float size, float? availableWidth = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        size *= s;
        float inner = 5f * s, outer = 6f * s;
        var total = size + (inner + outer) * 2f;
        CenterNext(total, availableWidth);
        var p = ImGui.GetCursorScreenPos();
        var c = p + new Vector2(total / 2f);
        var dl = ImGui.GetWindowDrawList();

        // Nutzeranforderung: dasselbe Kompass-Symbol wie oben links in der Seitenleiste (siehe
        // CodexTheme.DrawCompassIcon/DrawLogo), nur groß - statt des zuvor geladenen icon.png.
        ImGui.SetCursorScreenPos(c - new Vector2(size / 2f));
        CodexTheme.DrawCompassIcon(size);

        dl.AddCircle(c, size / 2f + inner, ImGui.GetColorU32(CodexTheme.Accent), 64, 1f * s);
        dl.AddCircle(c, size / 2f + inner + outer, ImGui.GetColorU32(CodexTheme.LineControl), 64, 1f * s);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(total, total));
    }

    /// <summary>Um 45° gedrehtes Quadrat (Raute) - About-Seite, als Hintergrund für das Herz-Symbol. Reine Formfläche, das Symbol selbst zeichnet der Aufrufer zentriert obendrauf.</summary>
    public static void Diamond(Vector2 center, float size, uint fill, uint border)
    {
        var dl = ImGui.GetWindowDrawList();
        var h = size * 0.7071f;
        var t = center + new Vector2(0f, -h);
        var r = center + new Vector2(h, 0f);
        var b = center + new Vector2(0f, h);
        var l = center + new Vector2(-h, 0f);
        dl.AddQuadFilled(t, r, b, l, fill);
        dl.AddQuad(t, r, b, l, border, 1f * ImGuiHelpers.GlobalScale);
    }

    /// <summary>
    /// Fließtext mit zentrierten Zeilen (About-Seite) - ImGui.TextWrapped umbricht zwar, richtet aber
    /// immer linksbündig aus, daher eigener Umbruch samt Zentrierung pro Zeile. Erwartet die gewünschte
    /// Schrift bereits gepusht (siehe Aufrufer). "centerLocalX"/"availableWidth" wie bei CenterNext -
    /// ohne Angabe zentriert jede Zeile zum aktuell verfügbaren Bereich, mit Angabe zu einer lokalen,
    /// expliziten Breite (siehe Windows.CodexMenuWindow.DrawAboutCard). "centerLocalX" ist dabei die
    /// window-lokale X-Position, auf die der Cursor VOR jeder Zeile zurückgesetzt wird (nötig, weil
    /// ImGui den Cursor nach jedem Zeilen-Widget auf die aktuelle Einzugsebene zurücksetzt, nicht auf
    /// diese lokale Startposition).
    /// </summary>
    public static void CenteredWrappedText(string text, float maxWidth, uint color, float? centerLocalX = null, float? availableWidth = null)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var test = line.Length == 0 ? word : line + " " + word;
            if (ImGui.CalcTextSize(test).X > maxWidth && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = test;
            }
        }
        if (line.Length > 0)
            lines.Add(line);

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        foreach (var l in lines)
        {
            if (centerLocalX is { } x)
                ImGui.SetCursorPosX(x);
            CenterNext(ImGui.CalcTextSize(l).X, availableWidth);
            ImGui.TextUnformatted(l);
        }
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Trennlinie mit mittiger Beschriftung (About-Seite, "FOLLOW THE JOURNEY") - erwartet die
    /// gewünschte Schrift bereits gepusht (siehe Aufrufer). Nutzer-Report: ohne "availableWidth"
    /// zentrierte sich die GESAMTE Trennlinie (Breite "width") zusätzlich noch einmal selbst gegen
    /// den vollen verfügbaren Bereich (CenterNext ohne Override) - lag der Aufrufer-Cursor (wie beim
    /// About-Abschnitt) bereits exakt am gewünschten linken Rand einer schmaleren Box, schob das die
    /// Linie sichtbar zu weit nach rechts, unausgerichtet zum Text darunter. Mit "availableWidth" =
    /// "width" übergeben verschwindet dieser zusätzliche Versatz (die Linie startet dann exakt am
    /// aktuellen Cursor, ohne erneute Zentrierung).
    /// </summary>
    public static void LabeledDivider(string label, float width, float? availableWidth = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        CenterNext(width, availableWidth);
        var p = ImGui.GetCursorScreenPos();
        var ts = ImGui.CalcTextSize(label);
        float gap = 12f * s;
        var y = p.Y + ts.Y / 2f;
        var textX = p.X + (width - ts.X) / 2f;
        var dl = ImGui.GetWindowDrawList();
        var line = ImGui.GetColorU32(CodexTheme.LineCard);
        dl.AddLine(new Vector2(p.X, y), new Vector2(textX - gap, y), line, 1f * s);
        dl.AddLine(new Vector2(textX + ts.X + gap, y), new Vector2(p.X + width, y), line, 1f * s);
        dl.AddText(new Vector2(textX, p.Y), ImGui.GetColorU32(CodexTheme.TextTertiary), label);
        ImGui.Dummy(new Vector2(width, ts.Y));
    }

    /// <summary>
    /// Gefüllter Fortschrittsbalken (Statistics-Seite, dicke Variante in der "Total discovered"-
    /// Kachel, schmale Variante je Kategorie-Zeile) - volle verfügbare Breite, abgerundete Enden.
    /// Füllung bei fraction &gt; 0 mindestens 4px breit, damit auch sehr kleine Anteile sichtbar
    /// bleiben (sonst z.B. bei 0.3% praktisch unsichtbar). "width" überschreibt optional die sonst
    /// live über ImGui.GetContentRegionAvail() ermittelte Breite (Nutzervorgabe: in den Statistics-
    /// Kacheln soll der Balken rechts denselben Randabstand wie links einhalten - BeginCard rückt
    /// nur links per Indent ein, GetContentRegionAvail() reicht aber bis zum echten rechten
    /// Kartenrand, siehe Windows.CodexMenuWindow.DrawStatisticsTotalTile).
    /// </summary>
    public static void ProgressBar(float fraction, float height, string? tooltip = null, float? width = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        height *= s;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = width ?? ImGui.GetContentRegionAvail().X;
        var r = height / 2f;

        dl.AddRectFilled(p, p + new Vector2(w, height), ImGui.GetColorU32(CodexTheme.BgSelected), r);
        if (fraction > 0f)
        {
            var fw = MathF.Max(w * Math.Clamp(fraction, 0f, 1f), 4f * s);
            dl.AddRectFilled(p, p + new Vector2(fw, height), ImGui.GetColorU32(CodexTheme.Accent), r);
        }

        ImGui.Dummy(new Vector2(w, height));
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    /// <summary>
    /// Fließende Knopfreihe mit Umbruch (Debug-Seite, "Debug Dumps") - ImGui bricht SameLine-Ketten
    /// nicht von selbst um, daher hier manuell: ein Knopf, der nicht mehr in die aktuelle Zeile passt,
    /// beginnt eine neue. "isConfirmed" markiert einen Knopf optional als "gerade bestätigt" (z.B.
    /// 1,5 Sekunden nach einem Klick) - zeigt dann ein Häkchen in OkFg statt des Terminal-Symbols,
    /// siehe Windows.CodexMenuWindow.DrawDebugDumpsCard.
    /// </summary>
    public static void FlowButtons(IReadOnlyList<(string Label, Action OnClick)> items, float gap, Func<string, bool>? isConfirmed = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        gap *= s;
        var rightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var i = 0; i < items.Count; i++)
        {
            var (label, onClick) = items[i];
            var confirmed = isConfirmed?.Invoke(label) ?? false;
            var size = MeasureFlowButton(label, s);

            if (i > 0)
            {
                ImGui.SameLine();
                if (ImGui.GetCursorScreenPos().X + size.X > rightEdge)
                    ImGui.NewLine();
            }

            if (DrawFlowButton(label, confirmed, size, s))
                onClick();
        }
        ImGui.PopStyleVar();
    }

    private static Vector2 MeasureFlowButton(string label, float scale)
    {
        var padding = new Vector2(11f * scale, 5f * scale);
        var iconGap = 6f * scale;

        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.Terminal.ToIconString()).X;
        float textWidth, textHeight;
        using (CodexTheme.FontDebugButtonLabel.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
    }

    private static bool DrawFlowButton(string label, bool confirmed, Vector2 buttonSize, float scale)
    {
        var padding = new Vector2(11f * scale, 5f * scale);
        var iconGap = 6f * scale;
        var icon = confirmed ? FontAwesomeIcon.Check : FontAwesomeIcon.Terminal;
        var iconColor = confirmed ? CodexTheme.OkFg : CodexTheme.TextMuted;

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##CodexFlowButton{label}", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var bg = hovered ? CodexTheme.BgSelected : CodexTheme.BgPopup;
        var border = hovered ? CodexTheme.Accent : CodexTheme.LineControl;
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), CodexTheme.RoundingControl);
        dl.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), CodexTheme.RoundingControl);

        float iconWidth, iconHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconSize = ImGui.CalcTextSize(icon.ToIconString());
            iconWidth = iconSize.X;
            iconHeight = iconSize.Y;
        }
        float textHeight;
        using (CodexTheme.FontDebugButtonLabel.Push())
            textHeight = ImGui.CalcTextSize(label).Y;

        var contentCursor = cursor + new Vector2(padding.X, (buttonSize.Y - iconHeight) / 2f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            dl.AddText(contentCursor, ImGui.GetColorU32(iconColor), icon.ToIconString());
        contentCursor = new Vector2(cursor.X + padding.X + iconWidth + iconGap, cursor.Y + (buttonSize.Y - textHeight) / 2f);
        using (CodexTheme.FontDebugButtonLabel.Push())
            dl.AddText(contentCursor, ImGui.GetColorU32(CodexTheme.TextHeading), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }
}
