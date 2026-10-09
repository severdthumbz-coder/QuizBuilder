// ─────────────────────────────────────────────────────────────────────────────
//  OnboardingKit — drop-in guided tour, ⓘ hint bubbles and ❔ legend for WPF.
//  Ported from Video Metadata Editor (build 151–154). MIT licensed.
// ─────────────────────────────────────────────────────────────────────────────
using System;

namespace OnboardingKit;

/// <summary>
/// One card in the guided tour.
/// <para><see cref="TabIndex"/>: the tab to switch to when this step shows
/// (-1 = leave the current tab alone, e.g. for "Welcome" / "You're all set").</para>
/// <para><see cref="OnEnter"/>: optional extra action when the step is shown
/// (expand a panel, scroll something into view, select a nav item…).</para>
/// </summary>
public sealed record TourStep(string Title, string Body, int TabIndex = -1, Action? OnEnter = null);

/// <summary>
/// One row of the Legend. Either a glyph (an emoji / icon character such as "⚠")
/// or a colour swatch (any WPF colour string such as "#4CAF50" or "OrangeRed").
/// When <see cref="Swatch"/> is set the swatch is drawn instead of the glyph.
/// </summary>
public sealed record LegendItem(string Glyph, string Meaning, string? Swatch = null)
{
    public bool HasSwatch => !string.IsNullOrEmpty(Swatch);
    public bool HasGlyph => string.IsNullOrEmpty(Swatch);
}
