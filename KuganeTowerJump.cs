using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;

namespace TheExplorersCodex;

/// <summary>
/// Dediziertes, eigenständiges Jumping-Puzzle für Shiokaze Hostelry / Kugane Tower (Adventure-RowId
/// 2162855) - 1:1 übernommen aus einem SND-Lua-Skript (pot0to, "Kugane Tower jump puzzle and
/// sightseeing log"), NICHT über das generische SightseeingPuzzleStep/RunUp/JumpFromStandstill-Modell.
///
/// Grund für die eigene Implementierung (Nutzerentscheidung nach mehreren gescheiterten Versuchen,
/// das generische Modell draufzubiegen): das generische Modell bildet "RunUp" als "vom vorherigen
/// Schritt aus durchlaufen, beim Überqueren abspringen" ab. Das Original-Lua-Skript macht aber für
/// JEDEN Punkt unabhängig dasselbe: zum Punkt hinlaufen (MoveTo), nach "wait" Sekunden (falls
/// gesetzt) den Sprung auslösen - der Sprung passiert also WÄHREND der Bewegung zu GENAU diesem
/// Punkt, nicht als Fortsetzung vom vorherigen. Diese Klasse bildet exakt dieses Timing nach, ohne
/// die Serverlatenz-/Chain-Annahmen des generischen Modells.
/// </summary>
public sealed class KuganeTowerJump
{
    public const uint AdventureId = 2162855;
    private const uint KuganeTerritoryId = 628;

    // SkipSettleAfter = true: KEINE SettleDuration-Pause nach Ankunft an DIESEM Punkt, sofort weiter
    // zum nächsten - für Nutzeranforderungen wie "ohne stehend zu bleiben abspringen" (durchlaufender
    // Anlauf über mehrere Punkte hinweg, der übliche kurze Stopp würde den Schwung sonst abbrechen).
    // ExtraWaitAfter: GEGENTEIL davon - zusätzlich zur normalen SettleDuration noch so lange warten,
    // bevor es zum nächsten Punkt weitergeht (Nutzeranforderung, z.B. ein schmaler/wackliger Steg, bei
    // dem der Charakter erst wieder richtig Stand fassen muss).
    public readonly record struct JumpPoint(Vector3 Pos, float? JumpDelay, string? Note, bool SkipSettleAfter = false, TimeSpan? ExtraWaitAfter = null);

    public static readonly Vector3 StartPosition = new(-41.97526f, 14.025003f, -36.547295f);

    // Nutzerentscheidung: komplett neu von Hand nachgebaut statt der bisherigen 1:1-Lua-Übernahme.
    // JumpDelay nutzt dieselbe Verzögerung wie Plugin.SightseeingPuzzleStep.Jump bei den anderen
    // Jumping Puzzles (siehe SightseeingAutomation.PuzzleJumpDelay, 0.1s), statt eigener Werte - null
    // = reiner Laufpunkt ohne Sprung (entspricht dort Jump: false), 0.1f = Sprung mit kurzem Anlauf
    // (entspricht dort Jump: true), 0f = Sprung "aus dem Stand" OHNE die übliche Verzögerung
    // (entspricht dort JumpFromStandstill: true).
    //
    // Nutzeranforderung: bei JEDEM neuen Sprungpunkt die Strecke zum vorherigen Punkt ausrechnen und
    // danach entscheiden, statt zu raten - horizontale Distanz (XZ) UND Höhenunterschied (Y) jeweils
    // einzeln betrachten:
    //   - horizontal <= 2.5y UND Höhe <= 2y -> aus dem Stand erreichbar (JumpDelay = 0f).
    //   - sonst -> braucht Schwung/Anlauf (JumpDelay = 0.1f, wie "Jump: true" bei den anderen Puzzles).
    // Nutzer-Korrektur (Punkt 5, ~3.2y horizontal/~0.2y hoch): vom Nutzer trotz geringer Höhe als
    // "kleiner Anlauf" statt "aus dem Stand" eingestuft - der 2.5y-Richtwert gilt also auch bei
    // geringem Höhenunterschied, nicht nur bei großem.
    public static readonly JumpPoint[] Points =
    {
        new(new Vector3(-40.87189f, 15.519358f, -37.750816f), 0f, null), // Punkt 2 (Sprung aus dem Stand: ~1.6y horizontal, ~1.5y hoch, ab Start)
        new(new Vector3(-39.535984f, 17.21f, -38.378956f), 0f, null), // Punkt 3 (Sprung aus dem Stand: ~1.5y horizontal, ~1.7y hoch, ab Punkt 2)
        new(new Vector3(-38.86601f, 17.21f, -37.88391f), null, null), // Punkt 4 (Laufen, kein Sprung)
        new(new Vector3(-36.530422f, 17.41003f, -39.140022f), 0f, null), // Punkt 5 (Nutzer-Report: mit 0.1f/"kleiner Anlauf" springt er zu weit über das Ziel hinaus - zurück auf 0f/aus dem Stand)
        new(new Vector3(-35.04217f, 19.210018f, -40.041595f), 0f, null), // Punkt 6 (Sprung aus dem Stand: ~1.7y horizontal, ~1.8y hoch, ab Punkt 5)
        new(new Vector3(-34.418545f, 19.21002f, -39.07486f), null, null), // Punkt 7 (Laufen, kein Sprung)
        new(new Vector3(-33.886597f, 19.21002f, -39.107407f), null, null, SkipSettleAfter: true), // Punkt 8 (Laufen, kein Sprung - direkt weiter zu Punkt 9, ohne stehen zu bleiben, siehe SkipSettleAfter-Kommentar)
        new(new Vector3(-30.874962f, 20.91015f, -39.23201f), 0.1f, null), // Punkt 9 (Sprung mit durchlaufendem Anlauf ab Punkt 7/8, siehe SkipSettleAfter bei Punkt 8)
        new(new Vector3(-32.201237f, 23.023392f, -40.521027f), 0.1f, null), // Punkt 10 (Sprung mit Anlauf: ~1.8y horizontal, ~2.1y hoch, ab Punkt 9 - über dem 2y-Höhen-Richtwert)
        new(new Vector3(-26.881052f, 24.191784f, -50.845734f), null, null), // Punkt 11 (Laufen, kein Sprung)
        new(new Vector3(-27.699408f, 24.371645f, -70.02153f), null, null), // Punkt 12 (Laufen, kein Sprung)
        new(new Vector3(-37.84239f, 24.396149f, -79.82993f), null, null), // Punkt 13 (Laufen, kein Sprung)
        new(new Vector3(-42.902836f, 25.164684f, -78.74233f), null, null), // Punkt 14 (Laufen, kein Sprung)
        new(new Vector3(-43.13485f, 26.59994f, -78.72593f), 0f, null), // Punkt 15 (Sprung aus dem Stand: ~0.2y horizontal, ~1.4y hoch, ab Punkt 14)
        new(new Vector3(-45.31623f, 26.59994f, -78.736206f), 0f, null), // Punkt 16 (Sprung aus dem Stand: ~2.2y horizontal, ~0y hoch, ab Punkt 15)

        // Phase 2 (beginnt an derselben Position wie Punkt 16).
        new(new Vector3(-51.580902f, 26.59994f, -81.04499f), null, null), // Punkt 17 (Laufen, kein Sprung)
        new(new Vector3(-52.45056f, 28.300001f, -81.713715f), 0f, null), // Punkt 18 (Sprung aus dem Stand)
        new(new Vector3(-54.241077f, 30.009998f, -81.879776f), 0f, null), // Punkt 19 (Sprung aus dem Stand)
        new(new Vector3(-54.091503f, 32.1379f, -79.693886f), 0.1f, null), // Punkt 20 (Sprung mit Anlauf: ~2.2y horizontal, ~2.1y hoch, ab Punkt 19 - über dem 2y-Höhen-Richtwert)

        // Phase 3 (beginnt an derselben Position wie Punkt 20). Nutzer-Report: bei einem Sturz in
        // dieser Phase landet man immer wieder auf Punkt 21 - das übernimmt bereits die normale
        // HandleFall-Wiedereinstiegssuche von selbst (reiner Laufpunkt ohne Sprung, exakt an dieser
        // Position), daher kein eigener Sonderfall nötig.
        new(new Vector3(-44.49901f, 40.995502f, -70.242325f), null, null), // Punkt 21 (Laufen, kein Sprung)
        new(new Vector3(-46.624504f, 42.11f, -70.46807f), 0f, null), // Punkt 22 (Sprung aus dem Stand)
        new(new Vector3(-46.211647f, 42.11f, -70.244446f), null, null), // Punkt 23 (Laufen, kein Sprung)
        new(new Vector3(-46.567833f, 42.109997f, -70.237816f), null, null, SkipSettleAfter: true), // Punkt 24 (Laufen, kein Sprung - direkt weiter zu Punkt 25, ohne stehen zu bleiben)
        new(new Vector3(-49.61704f, 43.809998f, -70.23925f), 0.1f, null), // Punkt 25 (Sprung mit durchlaufendem Anlauf ab Punkt 23/24, siehe SkipSettleAfter bei Punkt 24)
        new(new Vector3(-49.147827f, 43.809998f, -70.25937f), null, null), // Punkt 26 (Laufen, kein Sprung)
        new(new Vector3(-49.414448f, 43.809998f, -70.255875f), null, null, SkipSettleAfter: true), // Punkt 27 (Laufen, kein Sprung - direkt weiter zu Punkt 28, ohne stehen zu bleiben)
        new(new Vector3(-52.707806f, 45.309998f, -70.3278f), 0.1f, null), // Punkt 28 (Sprung mit durchlaufendem Anlauf ab Punkt 26/27)
        new(new Vector3(-49.642376f, 47.11f, -70.55653f), 0.1f, null), // Punkt 29 (Sprung mit Anlauf)
        new(new Vector3(-46.525677f, 48.91f, -70.772415f), 0.1f, null), // Punkt 30 (Sprung mit Anlauf)
        new(new Vector3(-46.57323f, 50.906086f, -69.88163f), 0f, null), // Punkt 31 (Sprung aus dem Stand: ~0.9y horizontal, ~2.0y hoch, ab Punkt 30)

        // Phase 4.
        new(new Vector3(-52.513966f, 52.20894f, -67.15003f), null, null), // Punkt 32 (Laufen, kein Sprung)
        new(new Vector3(-54.397243f, 53.677044f, -66.50174f), 0f, null), // Punkt 33 (Sprung aus dem Stand: ~2.0y horizontal, ~1.5y hoch, ab Punkt 32)
        new(new Vector3(-53.75507f, 54.511566f, -64.8093f), 0f, null), // Punkt 34 (Sprung aus dem Stand)
        new(new Vector3(-55.694466f, 55.153706f, -62.557777f), 0f, null), // Punkt 35 (Sprung aus dem Stand)
        new(new Vector3(-57.178474f, 54.917175f, -62.617317f), null, null), // Punkt 36 (Laufen, kein Sprung)
        new(new Vector3(-56.892593f, 55.169388f, -58.684414f), null, null), // Punkt 37 (Laufen, kein Sprung)
        new(new Vector3(-55.835228f, 56.43407f, -58.041874f), null, null), // Punkt 38 (Laufen, kein Sprung)
        new(new Vector3(-54.60141f, 57.74062f, -56.848305f), 0f, null), // Punkt 39 (Sprung aus dem Stand: ~1.7y horizontal, ~1.3y hoch, ab Punkt 38)
        new(new Vector3(-54.471474f, 59.53f, -55.941486f), 0f, null), // Punkt 40 (Sprung aus dem Stand)
        new(new Vector3(-54.966602f, 59.53f, -56.308514f), null, null), // Punkt 41 (Laufen, kein Sprung)
        new(new Vector3(-54.481884f, 61.309998f, -57.95891f), 0f, null), // Punkt 42 (Sprung aus dem Stand)
        new(new Vector3(-54.45792f, 62.750008f, -56.35635f), 0f, null), // Punkt 43 (Sprung aus dem Stand)
        new(new Vector3(-54.43975f, 62.750004f, -55.32718f), null, null), // Punkt 44 (Laufen, kein Sprung)
        new(new Vector3(-54.45643f, 62.750008f, -56.92789f), null, null, SkipSettleAfter: true), // Punkt 45 (Laufen, kein Sprung - direkt weiter zu Punkt 46, ohne stehen zu bleiben)
        new(new Vector3(-54.62063f, 64.31001f, -59.3235f), 0.1f, null), // Punkt 46 (Sprung mit durchlaufendem Anlauf ab Punkt 44/45, siehe SkipSettleAfter bei Punkt 45)
        new(new Vector3(-54.800632f, 65.863235f, -61.812607f), 0f, null), // Punkt 47 (Sprung aus dem Stand: ~2.5y horizontal, ~1.6y hoch, ab Punkt 46 - grenzwertig am 2.5y-Richtwert)
        new(new Vector3(-54.826817f, 67.261665f, -63.76159f), null, null), // Punkt 48 (Laufen, kein Sprung)
        new(new Vector3(-53.899036f, 69.05176f, -64.8604f), null, null), // Punkt 49 (Laufen, kein Sprung)
        new(new Vector3(-52.684933f, 67.374176f, -65.650185f), null, null), // Punkt 50 (Laufen, kein Sprung)
        new(new Vector3(-49.24206f, 68.41008f, -65.46935f), 0.1f, null), // Punkt 51 (Sprung mit Anlauf: ~3.4y horizontal, ~1.0y hoch, ab Punkt 50)
        new(new Vector3(-48.86774f, 68.41008f, -65.466415f), null, null), // Punkt 52 (Laufen, kein Sprung)
        new(new Vector3(-47.25968f, 70.21018f, -65.457825f), 0f, null), // Punkt 53 (Sprung aus dem Stand)
        new(new Vector3(-46.78837f, 70.21018f, -65.458115f), null, null), // Punkt 54 (Laufen, kein Sprung)
        new(new Vector3(-45.051853f, 72.01014f, -65.458786f), 0f, null), // Punkt 55 (Sprung aus dem Stand)
        new(new Vector3(-44.737785f, 72.01014f, -65.4826f), null, null), // Punkt 56 (Laufen, kein Sprung)
        new(new Vector3(-45.29382f, 72.01014f, -65.53708f), null, null, SkipSettleAfter: true), // Punkt 57 (Laufen, kein Sprung - direkt weiter zu Punkt 58, ohne stehen zu bleiben)
        new(new Vector3(-48.88196f, 73.510056f, -65.62532f), 0.1f, null), // Punkt 58 (Sprung mit durchlaufendem Anlauf ab Punkt 56/57, siehe SkipSettleAfter bei Punkt 57)
        new(new Vector3(-48.93604f, 73.51005f, -65.78254f), null, null), // Punkt 59 (Laufen, kein Sprung)
        new(new Vector3(-51.149788f, 75.11f, -66.03421f), 0f, null), // Punkt 60 (Sprung aus dem Stand)
        new(new Vector3(-51.133366f, 75.11f, -65.995f), null, null), // Punkt 61 (Laufen, kein Sprung)
        new(new Vector3(-50.602547f, 75.11f, -65.92812f), null, null, SkipSettleAfter: true), // Punkt 62 (Laufen, kein Sprung - direkt weiter zu Punkt 63, ohne stehen zu bleiben)
        new(new Vector3(-47.22149f, 76.40999f, -65.55759f), 0.1f, null), // Punkt 63 (Sprung mit durchlaufendem Anlauf ab Punkt 61/62, siehe SkipSettleAfter bei Punkt 62)
        new(new Vector3(-45.046974f, 77.25001f, -65.472336f), 0f, null), // Punkt 64 (Sprung aus dem Stand)
        new(new Vector3(-45.742794f, 77.25001f, -65.46853f), null, null), // Punkt 65 (Laufen, kein Sprung)
        new(new Vector3(-44.765495f, 77.25f, -65.45824f), null, null, SkipSettleAfter: true), // Punkt 66 (Laufen, kein Sprung - direkt weiter zu Punkt 67, ohne stehen zu bleiben)
        new(new Vector3(-41.558327f, 79.049995f, -65.45264f), 0.1f, null), // Punkt 67 (Sprung mit durchlaufendem Anlauf ab Punkt 65/66, siehe SkipSettleAfter bei Punkt 66)
        new(new Vector3(-41.19734f, 79.049995f, -65.596565f), null, null), // Punkt 68 (Laufen, kein Sprung - Punkt 69 entfällt, direkt weiter zum Stand-Sprung auf Punkt 70)
        new(new Vector3(-40.197933f, 80.85f, -64.04872f), 0f, null), // Punkt 69 (Sprung aus dem Stand ab Punkt 68, kein Anlauf mehr - altes Punkt 69 entfällt)
        new(new Vector3(-39.9147f, 80.85f, -63.36564f), null, null), // Punkt 70 (Laufen, kein Sprung)
        new(new Vector3(-41.534153f, 82.24997f, -61.438072f), 0f, null), // Punkt 71 (Sprung aus dem Stand ab Punkt 70)
        new(new Vector3(-41.533997f, 82.24997f, -62.512028f), null, null), // Punkt 72 (Laufen, kein Sprung)
        new(new Vector3(-41.539913f, 82.24997f, -60.96927f), null, null, SkipSettleAfter: true), // Punkt 73 (Laufen, kein Sprung - direkt weiter zu Punkt 74, ohne stehen zu bleiben)
        new(new Vector3(-41.540638f, 82.24997f, -56.44854f), 0.1f, null), // Punkt 74 (Sprung mit durchlaufendem Anlauf ab Punkt 72/73, siehe SkipSettleAfter bei Punkt 73)
        new(new Vector3(-40.83806f, 83.75f, -55.387295f), 0f, null), // Punkt 75 (Sprung aus dem Stand ab Punkt 74)
        new(new Vector3(-40.454075f, 83.75f, -55.562347f), null, null), // Punkt 76 (Laufen, kein Sprung)
        new(new Vector3(-40.282608f, 83.75f, -55.097027f), null, null, SkipSettleAfter: true), // Punkt 77 (Laufen, kein Sprung - direkt weiter zu Punkt 78, ohne stehen zu bleiben)
        new(new Vector3(-39.0005f, 85.549995f, -51.819763f), 0.1f, null), // Punkt 78 (Sprung mit durchlaufendem Anlauf ab Punkt 76/77, siehe SkipSettleAfter bei Punkt 77)
        new(new Vector3(-38.892506f, 85.549995f, -51.647594f), null, null), // Punkt 79 (Laufen, kein Sprung)
        new(new Vector3(-39.123787f, 85.549995f, -52.119736f), null, null, SkipSettleAfter: true), // Punkt 80 (Laufen, kein Sprung - direkt weiter zu Punkt 81, ohne stehen zu bleiben)
        new(new Vector3(-40.016693f, 87.35f, -54.155254f), 0.1f, null), // Punkt 81 (Sprung mit durchlaufendem Anlauf ab Punkt 79/80, siehe SkipSettleAfter bei Punkt 80)
        new(new Vector3(-40.343525f, 88.49285f, -54.294888f), 0f, null), // Punkt 82 (Sprung aus dem Stand)
        new(new Vector3(-40.247643f, 88.49285f, -53.966663f), null, null), // Punkt 83 (Laufen, kein Sprung)
        new(new Vector3(-40.05527f, 89.649994f, -52.00386f), 0f, null), // Punkt 84 (Sprung aus dem Stand)
        new(new Vector3(-42.166824f, 89.15719f, -53.31883f), 0f, null), // Punkt 85 (Sprung aus dem Stand)

        // Phase 5 (beginnt an derselben Position wie Punkt 85).
        new(new Vector3(-42.084156f, 89.15719f, -64.13245f), null, null), // Punkt 86 (Laufen, kein Sprung)
        new(new Vector3(-41.99545f, 90.88701f, -66.26462f), 0f, null), // Punkt 87 (Sprung aus dem Stand)
        new(new Vector3(-42.362892f, 90.88701f, -65.905136f), null, null), // Punkt 88 (Laufen, kein Sprung)
        new(new Vector3(-42.460136f, 90.88701f, -66.48604f), null, null), // Punkt 89 (Laufen, kein Sprung)
        new(new Vector3(-46.33827f, 90.88701f, -66.244644f), null, null), // Punkt 90 (Laufen, kein Sprung)
        new(new Vector3(-43.455265f, 89.31001f, -66.94128f), 0f, null), // Punkt 91 (Sprung aus dem Stand)
        new(new Vector3(-43.564713f, 89.31001f, -67.05347f), null, null), // Punkt 92 (Laufen, kein Sprung)
        new(new Vector3(-43.14773f, 89.31001f, -67.23409f), null, null, SkipSettleAfter: true), // Punkt 93 (Laufen, kein Sprung - direkt weiter zu Punkt 94, ohne stehen zu bleiben)
        new(new Vector3(-40.359295f, 91.01001f, -68.804565f), 0.1f, null), // Punkt 94 (Sprung mit durchlaufendem Anlauf ab Punkt 92/93, siehe SkipSettleAfter bei Punkt 93)
        new(new Vector3(-40.145977f, 91.01001f, -68.59465f), null, null), // Punkt 95 (Laufen, kein Sprung)
        new(new Vector3(-40.301197f, 91.01001f, -68.77302f), null, null, SkipSettleAfter: true), // Punkt 96 (Laufen, kein Sprung - direkt weiter zu Punkt 97, ohne stehen zu bleiben)
        new(new Vector3(-36.659447f, 92.81001f, -66.72768f), 0.1f, null), // Punkt 97 (Sprung mit durchlaufendem Anlauf ab Punkt 95/96, siehe SkipSettleAfter bei Punkt 96)
        new(new Vector3(-36.499847f, 92.81001f, -66.95923f), null, null), // Punkt 98 (Laufen, kein Sprung)
        new(new Vector3(-36.855556f, 92.81001f, -66.62481f), null, null, SkipSettleAfter: true), // Punkt 99 (Laufen, kein Sprung - direkt weiter zu Punkt 100, ohne stehen zu bleiben)
        new(new Vector3(-37.41728f, 94.609985f, -65.25758f), 0.1f, null), // Punkt 100 (Sprung mit durchlaufendem Anlauf ab Punkt 98/99, siehe SkipSettleAfter bei Punkt 99)
        new(new Vector3(-39.973953f, 96.09405f, -65.14379f), 0f, null), // Punkt 101 (Sprung aus dem Stand)
        new(new Vector3(-46.528275f, 102.4831f, -59.24943f), null, null), // Punkt 102 (Laufen, kein Sprung)
        new(new Vector3(-46.529892f, 102.48504f, -54.407715f), null, null), // Punkt 103 (Laufen, kein Sprung)
        new(new Vector3(-46.375847f, 103.66513f, -51.965286f), 0f, null), // Punkt 104 (Sprung aus dem Stand)
        new(new Vector3(-47.19064f, 104.608376f, -52.04943f), 0f, null), // Punkt 105 (Sprung aus dem Stand ab Punkt 104 - klappt nicht immer, siehe UnlimitedLocalRetryIndices)
        new(new Vector3(-49.19873f, 104.287926f, -52.212692f), null, null), // Punkt 106 (Laufen, kein Sprung)
        new(new Vector3(-49.19827f, 104.51166f, -52.588467f), null, null, SkipSettleAfter: true), // Punkt 107 (Laufen, kein Sprung - direkt weiter zu Punkt 108, ohne stehen zu bleiben)
        new(new Vector3(-49.378513f, 105.81f, -56.50036f), 0.1f, null), // Punkt 108 (Sprung mit durchlaufendem Anlauf ab Punkt 106/107, siehe SkipSettleAfter bei Punkt 107)
        new(new Vector3(-48.544003f, 107.603966f, -56.62415f), 0f, null), // Punkt 109 (Sprung aus dem Stand ab Punkt 108 - klappt nicht immer, siehe UnlimitedLocalRetryIndices)
        new(new Vector3(-48.08217f, 107.603966f, -58.966175f), null, "Sightseeing-Punkt"), // Punkt 110 (Laufen, kein Sprung - der eigentliche Sightseeing-Zielpunkt)
    };

    private enum State
    {
        Idle,
        CheckingPreconditions,
        ApproachingJumpPoint,
        FallRecoveryWaypoint,
        Phase4FallRecovery,
        GoingToStart,
        TeleportingToMainAetheryte,
        Crawling,
        MovingToPoint,
        Settling,
        FellWaitingForDecision,
        Failed,
        Done,
    }

    // Nutzer-Report: der direkte Weg zum Startpunkt führt über eine Stelle, die vnavmesh zu Fuß
    // nicht überwinden kann (ein echter Sprung über eine Kante ist nötig, ~14 Einheiten tiefer
    // dahinter) - von Hand ermittelter Zwischenpunkt, an dem erst gesprungen wird, bevor es normal
    // weiter zum eigentlichen Startpunkt geht.
    private static readonly Vector3 ApproachJumpPoint = new(-43.07232f, 14.025003f, -35.724815f);

    // Nutzer-Report: fällt man in "Phase 1" (den ersten fünf Punkten) ganz bis auf den Boden durch,
    // landet man an dieser Stelle - von dort aus führt KEIN direkter Laufweg zurück zum Startpunkt
    // (vnavmesh kommt nicht vorbei, ähnlich wie bei ApproachJumpPoint), sondern erst über diesen
    // Zwischenpunkt. Da von so tief unten ohnehin kein Punkt als Wiedereinstieg in Frage kommt, wird
    // danach immer komplett von Punkt 1 neu begonnen (ResumeIndex 0).
    private static readonly Vector3 DeepFallPosition = new(-35.071f, 12.708281f, -39.62058f);
    private const float DeepFallPositionTolerance = 3f;
    private static readonly Vector3 DeepFallRecoveryWaypoint = new(-25.022501f, 0.099999785f, -38.250374f);

    // Nutzer-Report: JEDER Sturz während Phase 3 (Punkt 21-31, Index 19-29 - Nutzerbestätigung
    // "für jeden Sturz in Phase 3", unabhängig von der genauen Landeposition) soll über diesen
    // Zwischenpunkt zurück zum Start von Phase 3 (Punkt 21, Index 19) laufen, statt über die normale
    // höhenbasierte Wiedereinstiegssuche weiter unten in HandleFall.
    private const int Phase3StartIndex = 19; // Punkt 21
    private const int Phase3LastIndex = 29; // Punkt 31
    private static readonly Vector3 Phase3FallRecoveryWaypoint = new(-52.48454f, 33.29696f, -77.86616f);

    // Nutzer-Report (Phase 4, Punkt 42): ein Sturz von genau diesem Punkt soll gezielt bei Punkt 40
    // (einem Sprungpunkt) wieder aufgenommen werden statt beim generischen, höhenbasierten
    // Wiedereinstieg weiter unten in HandleFall (der nur reine Laufpunkte vorschlägt und hier
    // fälschlich Punkt 41 statt Punkt 40 gewählt hätte) - Key = Index des Punkts, bei dessen Sturz
    // dieser Eintrag greift, Wert = Index des Wiedereinstiegspunkts. Kein eigener Zwischenpunkt nötig,
    // der direkte Weg zurück funktioniert hier.
    private static readonly Dictionary<int, int> FallResumeOverrides = new()
    {
        [40] = 38, // Sturz bei Punkt 42 -> weiter bei Punkt 40
        [38] = 38, // Sturz bei Punkt 40 (dem Sprung dorthin) -> einfach direkt wieder bei Punkt 40 selbst
    };

    // Siehe HandleFall/UpdateFallRecoveryWaypoint - Ziel NACH dem jeweiligen Zwischenpunkt (StartPosition
    // für die Phase-1-Variante, ein bestimmter Points[]-Eintrag für die Phase-3-Variante).
    private Vector3 pendingRecoveryTarget;

    // Nutzer-Report: fällt man in die Nähe EINER dieser Positionen, führt der Rückweg zum jeweiligen
    // Phasenstart (siehe ResumePhaseIndex, Index in PhaseStarts - meist Phase 4, die letzte Route
    // aber zurück zu Phase 3) über MEHRERE eigene Zwischenschritte (laufen/springen/..., je Route
    // unterschiedlich) statt nur einen einzelnen Zwischenpunkt (wie bei DeepFallRecoveryWaypoint/
    // Phase3FallRecoveryWaypoint) - danach beginnt die jeweilige Phase wieder ganz von vorne.
    private static readonly (Vector3 FallPosition, JumpPoint[] Steps, int ResumePhaseIndex)[] Phase4MultiStepFallRoutes =
    {
        (new Vector3(-38.641415f, 51.962856f, -60.0002f), new[]
        {
            new JumpPoint(new Vector3(-40.14871f, 52.048992f, -64.14628f), null, null), // Laufen
            new JumpPoint(new Vector3(-41.344597f, 53.660126f, -64.51937f), 0f, null), // Springen
            new JumpPoint(new Vector3(-43.10975f, 53.23463f, -65.669556f), 0f, null), // Springen
            new JumpPoint(new Vector3(-45.192936f, 52.087f, -68.137825f), null, null), // Laufen
        }, 3),
        (new Vector3(-39.21023f, 52.105267f, -54.824596f), new[]
        {
            new JumpPoint(new Vector3(-40.09033f, 52.149494f, -63.87909f), null, null), // Laufen
            new JumpPoint(new Vector3(-41.30561f, 53.66835f, -64.57368f), 0f, null), // Springen
            new JumpPoint(new Vector3(-43.698624f, 52.643394f, -66.4647f), 0f, null), // Springen
        }, 3),
        (new Vector3(-33.43735f, 37.23091f, -53.18915f), new[]
        {
            new JumpPoint(new Vector3(-36.2906f, 40.500183f, -57.66927f), null, null), // Laufen
            new JumpPoint(new Vector3(-36.82764f, 41.11553f, -61.535553f), 0f, null), // Springen
            new JumpPoint(new Vector3(-38.49339f, 38.937664f, -69.08041f), null, null), // Laufen
        }, 2),
        (new Vector3(-39.906742f, 52.218567f, -63.55179f), new[]
        {
            new JumpPoint(new Vector3(-40.6935f, 52.44046f, -63.87889f), null, null), // Laufen
            new JumpPoint(new Vector3(-41.409283f, 53.66303f, -64.46009f), 0f, null), // Springen
            new JumpPoint(new Vector3(-44.373768f, 52.66753f, -67.089714f), 0f, null), // Springen
        }, 3),
        (new Vector3(-40.453243f, 52.525604f, -63.46146f), new[]
        {
            new JumpPoint(new Vector3(-41.38667f, 53.661877f, -64.48056f), 0f, null), // Springen
            new JumpPoint(new Vector3(-44.15208f, 52.698364f, -66.80396f), 0f, null), // Springen
        }, 3),
    };
    private const float Phase4MultiStepFallPositionTolerance = 3f;
    private JumpPoint[] activePhase4RecoverySteps = Array.Empty<JumpPoint>();
    private int phase4RecoveryStepIndex;

    // Weitere bekannte Sturz-Landepositionen in Phase 4, die ALLE über denselben einzelnen
    // Zwischenpunkt zurück zum Start von Phase 4 laufen (anders als Phase4FallRecoverySteps oben, das
    // mehrere eigene Zwischenschritte braucht) - wie bei Phase3FallRecoveryWaypoint.
    private static readonly Vector3[] Phase4SecondFallPositions =
    {
        new(-45.18767f, 62.750008f, -65.566795f),
        new(-44.533184f, 66.6143f, -65.45908f),
    };
    private const float Phase4SecondFallPositionTolerance = 3f;
    private static readonly Vector3 Phase4SecondFallRecoveryWaypoint = new(-46.549526f, 51.896275f, -68.419464f);

    // Nutzer-Report: fällt man in die Nähe EINER dieser Positionen, geht es OHNE Zwischenschritt
    // (kein Hindernis im Weg, anders als bei Phase4MultiStepFallRoutes/Phase4SecondFallPositions)
    // direkt per normalem Laufauftrag zurück zum Start der angegebenen Phase (ResumePhaseIndex,
    // Index in PhaseStarts).
    private static readonly (Vector3 FallPosition, int ResumePhaseIndex)[] DirectPhaseRestartFallPositions =
    {
        (new Vector3(-41.959354f, 89.15719f, -53.866585f), 4), // Phase 5
    };
    private const float DirectPhaseRestartFallPositionTolerance = 3f;

    // Nutzer-Report (Punkt 90): hier hilft kein normaler Laufauftrag (das Stück ist zu schmal/
    // instabil, ein normaler vnavmesh-Laufweg lässt den Charakter zu früh/an der falschen Stelle
    // herunterfallen) - stattdessen alle CrawlStepInterval nur CrawlStepDistance (1mm) in Richtung
    // Punkt 90 rücken, bis man dadurch am Rand herunterfällt und auf der (niedrigeren) Zielposition
    // landet. Sobald nah genug dran, übernimmt die normale UpdateMovingToPoint-Logik (Sturzprüfung/
    // Settling) unverändert weiter - es wird dafür nur noch kein weiterer moveToPath-Auftrag erteilt.
    // Aktuell ungenutzt (Nutzeranforderung: Punkt 90 wird jetzt stattdessen über eine normale
    // Laufroute erreicht) - Mechanik bleibt für mögliche künftige Fälle erhalten.
    private static readonly HashSet<int> CrawlIndices = new();
    private static readonly TimeSpan CrawlStepInterval = TimeSpan.FromSeconds(2);
    private const float CrawlStepDistance = 0.001f;
    private const float CrawlArrivalTolerance = 1f;
    private DateTime crawlLastStepAt;

    // Siehe "repeat wait(0.1) until not IsRunning(); wait(0.5)" im Original - dieselbe 0,5s Pause
    // nach Ankunft, bevor der nächste Punkt beginnt.
    private static readonly TimeSpan SettleDuration = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan GoingToStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MovingTimeout = TimeSpan.FromSeconds(20);

    // Siehe UpdateGoingToStart/TryTeleportToMainAetheryte - identische Werte/Begründung wie
    // SightseeingAutomation.DistrictTravelTimeout/-SettleDelay (Teleport zum Haupt-Ätheryten bleibt in
    // derselben Zone, BetweenAreas statt Zonenwechsel als Ladebildschirm-Indikator).
    private static readonly TimeSpan MainAetheryteTeleportTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MainAetheryteTeleportSettleDelay = TimeSpan.FromSeconds(2);
    private const float FallYMargin = 1.5f;
    private const float FallHorizontalMargin = 2f;
    private const int MaxAutoRetries = 3;

    // Siehe UpdateMovingToPoint-Kommentar zu "stillNearLaunch" - großzügiger als die generische
    // SightseeingAutomation.PuzzleStepRetryRadius (3f/0,5f), da die Punkte hier teils eng beieinander
    // liegende Pfosten/schmale Stege sind (Nutzeranforderung: "er könnte es von dort aus erneut
    // versuchen", statt nach einem knapp verfehlten Sprung gleich ganz aufzugeben).
    private const float LocalRetryRadius = 3f;
    private const float LocalRetryHeightMargin = 1.5f;
    private const int MaxLocalRetries = 5;

    // Nutzer-Report (Punkt 31): dieser Sprung klappt nicht immer beim ersten Versuch - hier
    // UNBEGRENZT (statt nur MaxLocalRetries mal) wiederholen, bis er sitzt, solange man noch in der
    // Nähe des Absprungpunkts steht (siehe stillNearLaunch).
    private static readonly HashSet<int> UnlimitedLocalRetryIndices = new() { 14, 29, 103, 107 }; // Schritt 15 (Phase 1), Punkt 31, Punkt 105, Punkt 109

    private readonly ICallGateSubscriber<List<Vector3>, bool, object> moveToPath;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<bool> navmeshIsReady;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<uint, byte, bool> lifestreamTeleport;
    private readonly ICallGateSubscriber<bool> lifestreamIsBusy;
    private readonly ICallGateSubscriber<object> lifestreamAbort;

    private State state = State.Idle;
    private int currentPointIndex;
    private DateTime stateEnteredAt;
    private bool jumpSent;
    private bool hasSeenPathRunningThisLeg;
    private DateTime pathStartedAt;
    private int localRetryCount;
    private int autoRetryCount;
    private int? resumeFromIndex;
    private int fellAtIndex;
    private Vector3? goingToStartLastPos;
    private DateTime goingToStartLastProgressCheckAt;
    private bool mainAetheryteTeleportHasSeenLoadingScreen;

    // Nutzeranforderung: "nach ein paar Sekunden" reagieren, nicht erst nach dem generischen
    // NavigationStuckDetector-Rhythmus (4s/1,5 Einheiten, für andere Automationen gedacht) - gegen
    // eine Brücke/Wand laufen zeigt sich fast sofort durch praktisch keine Bewegung mehr.
    private static readonly TimeSpan GoingToStartStuckCheckInterval = TimeSpan.FromSeconds(2);
    private const float GoingToStartStuckMinProgress = 1f;

    // Nutzeranforderung: bevor beim Steckenbleiben gleich zum Haupt-Ätheryten teleportiert wird,
    // erst ein paar Mal einen Sprung versuchen - viele Hindernisse hier sind niedrige Kanten/Geländer,
    // über die ein Sprung hinwegkommt, wo reines Laufen hängen bleibt.
    private const int GoingToStartMaxStuckJumpAttempts = 3;
    private int goingToStartStuckJumpAttempts;

    public bool IsActive { get; private set; }
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>True für genau einen Update()-Aufruf, nachdem der letzte Punkt erreicht wurde - der Aufrufer (SightseeingAutomation) soll danach in die normale Vista-Erkennung übergeben.</summary>
    public bool ReachedTop { get; private set; }

    public static Vector3 FinalPosition => Points[^1].Pos;

    /// <summary>Siehe FellWaitingForDecision - null, solange kein Sturz gerade auf eine Entscheidung (Resume/Restart/Stop) wartet.</summary>
    public (int FellAtPoint, int? ResumeFromPoint)? PendingFallDecision =>
        state == State.FellWaitingForDecision ? (fellAtIndex + 1, resumeFromIndex + 1) : null;

    public KuganeTowerJump()
    {
        moveToPath = Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        pathIsRunning = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathStop = Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        navmeshIsReady = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveCloseTo = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        lifestreamTeleport = Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        lifestreamIsBusy = Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        lifestreamAbort = Plugin.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
    }

    private bool IsLifestreamAvailable()
    {
        try
        {
            return lifestreamTeleport.HasFunction && lifestreamIsBusy.HasFunction;
        }
        catch
        {
            return false;
        }
    }

    private void StopLifestream()
    {
        try
        {
            if (lifestreamAbort.HasAction)
                lifestreamAbort.InvokeAction();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[KuganeTowerJump] Fehler beim Abbrechen von Lifestream.");
        }
    }

    // Nutzeranforderung: beim Start erkennen, in welcher Phase man gerade steht (z.B. nach einem
    // Stop mitten im Klettern), und direkt zum Startpunkt DIESER Phase laufen, statt immer ganz von
    // Punkt 1 neu zu beginnen - per 3D-Distanz zum jeweiligen Phasen-Startpunkt verglichen.
    private static readonly (Vector3 Pos, int ResumeIndex)[] PhaseStarts =
    {
        (StartPosition, 0), // Phase 1
        (Points[14].Pos, 15), // Phase 2 (Position von Punkt 16 - weiter geht es mit Punkt 17)
        (Points[Phase3StartIndex].Pos, Phase3StartIndex), // Phase 3 (Punkt 21)
        (Points[29].Pos, 30), // Phase 4 (Position von Punkt 31 - weiter geht es mit Punkt 32)
        (Points[83].Pos, 84), // Phase 5 (Position von Punkt 85 - weiter geht es mit Punkt 86)
    };

    /// <summary>
    /// Startet - erkennt zuerst per PhaseStarts, in welcher Phase der Charakter gerade steht, und
    /// läuft direkt zu deren Startpunkt. Steht er näher an Phase 1 (oder gar keine Positionsdaten
    /// verfügbar), unverändert wie bisher ab Punkt 1 (StartFromPoint(0), inkl. der dortigen
    /// Nahbereichs-/ApproachJumpPoint-Logik in UpdateCheckingPreconditions).
    /// </summary>
    public void Start()
    {
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        if (playerPos is { } pos)
        {
            // Nutzeranforderung: steht man schon GENAU an einem der 79 Punkte (nicht nur grob in der
            // Nähe eines der 4 Phasenstarts), dort direkt fortsetzen statt erst zum (u.U. weit
            // entfernten) Start der erkannten Phase zurückzulaufen - identische Prüfung wie die
            // bereits bestehende Nahbereichs-Erkennung in UpdateCheckingPreconditions, hier nur VOR
            // der groberen Phasen-Erkennung unten.
            var nearestPoint = FindNearestPointIndex(pos);
            if (nearestPoint.HasValue)
            {
                Plugin.Log.Info($"[KuganeTowerJump] Start: schon nah an Schritt {nearestPoint.Value + 1} - setze dort fort statt zu einem Phasenstart zu laufen.");
                autoRetryCount = 0;
                localRetryCount = 0;
                ReachedTop = false;
                IsActive = true;
                BeginPoint(nearestPoint.Value);
                return;
            }

            var nearestPhase = 0;
            var nearestDistance = Vector3.Distance(pos, PhaseStarts[0].Pos);
            for (var i = 1; i < PhaseStarts.Length; i++)
            {
                var distance = Vector3.Distance(pos, PhaseStarts[i].Pos);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPhase = i;
                }
            }

            if (nearestPhase > 0)
            {
                Plugin.Log.Info($"[KuganeTowerJump] Start: erkannt als Phase {nearestPhase + 1} (Distanz {nearestDistance:F1}y) - laufe zum dortigen Startpunkt statt ganz von vorne.");
                StartFromPhase(PhaseStarts[nearestPhase].ResumeIndex, PhaseStarts[nearestPhase].Pos);
                return;
            }
        }

        StartFromPoint(0);
    }

    /// <summary>Siehe PhaseStarts-Kommentar - läuft direkt zum Startpunkt einer erkannten Phase 2/3, dann normal weiter ab resumeIndex (über die bestehende GoingToStart-Logik inkl. Stuck-Erkennung).</summary>
    private void StartFromPhase(int resumeIndex, Vector3 phaseStartPos)
    {
        currentPointIndex = resumeIndex;
        autoRetryCount = 0;
        localRetryCount = 0;
        ReachedTop = false;
        IsActive = true;
        pendingRecoveryTarget = phaseStartPos;
        pathfindAndMoveCloseTo.InvokeFunc(phaseStartPos, false, 0.3f);
        goingToStartLastPos = null;
        SetState(State.GoingToStart);
        StatusText = Loc.T("Kugane-Turm: laufe zum Startpunkt der erkannten Phase...", "Kugane Tower: walking to the start point of the detected phase...");
    }

    /// <summary>Siehe Configuration.KuganeTowerJumpStartStep (manueller Debug-Einstieg) - 1-basiert wie im Original ("Starting Jump Number").</summary>
    public void StartFromStep(int stepNumber) => StartFromPoint(Math.Clamp(stepNumber - 1, 0, Points.Length - 1));

    private void StartFromPoint(int pointIndex)
    {
        currentPointIndex = pointIndex;
        autoRetryCount = 0;
        localRetryCount = 0;
        ReachedTop = false;
        IsActive = true;
        state = State.CheckingPreconditions;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T("Kugane-Turm: Prüfe Voraussetzungen...", "Kugane Tower: Checking preconditions...");
    }

    public void Stop()
    {
        IsActive = false;
        state = State.Idle;
        StopPath();
        StopLifestream();
    }

    private void StopPath()
    {
        try
        {
            if (pathStop.HasAction)
                pathStop.InvokeAction();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[KuganeTowerJump] Fehler beim Stoppen von vnavmesh.");
        }
    }

    /// <summary>Nutzerentscheidung nach einem Sturz: ab dem vorgeschlagenen Wiedereinstiegspunkt weitermachen.</summary>
    public void Resume()
    {
        if (state != State.FellWaitingForDecision)
            return;

        StartFromPoint(resumeFromIndex ?? 0);
    }

    /// <summary>Nutzerentscheidung nach einem Sturz: komplett von Punkt 1 neu.</summary>
    public void Restart()
    {
        if (state != State.FellWaitingForDecision)
            return;

        StartFromPoint(0);
    }

    // Ohne Resume/Restart/Stop-Knöpfe im Overlay (noch nicht gebaut) bliebe FellWaitingForDecision
    // sonst für immer stehen (Nutzer-Report: "sagt back to start point, läuft aber nicht hin") - nach
    // dieser kurzen, lesbaren Pause automatisch fortsetzen, als hätte der Nutzer "Resume" geklickt.
    // Sobald die echten Knöpfe existieren, können sie VOR Ablauf dieser Zeit eine echte Wahl treffen.
    private static readonly TimeSpan FellDecisionAutoContinueDelay = TimeSpan.FromSeconds(3);

    private void UpdateFellWaitingForDecision()
    {
        if (DateTime.UtcNow - stateEnteredAt < FellDecisionAutoContinueDelay)
            return;

        StartFromPoint(resumeFromIndex ?? 0);
    }

    public void Update()
    {
        if (!IsActive)
            return;

        try
        {
            switch (state)
            {
                case State.CheckingPreconditions:
                    UpdateCheckingPreconditions();
                    break;
                case State.ApproachingJumpPoint:
                    UpdateApproachingJumpPoint();
                    break;
                case State.FallRecoveryWaypoint:
                    UpdateFallRecoveryWaypoint();
                    break;
                case State.Phase4FallRecovery:
                    UpdatePhase4FallRecovery();
                    break;
                case State.GoingToStart:
                    UpdateGoingToStart();
                    break;
                case State.TeleportingToMainAetheryte:
                    UpdateTeleportingToMainAetheryte();
                    break;
                case State.Crawling:
                    UpdateCrawling();
                    break;
                case State.MovingToPoint:
                    UpdateMovingToPoint();
                    break;
                case State.Settling:
                    UpdateSettling();
                    break;
                case State.FellWaitingForDecision:
                    UpdateFellWaitingForDecision();
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[KuganeTowerJump] Fehler - Automation gestoppt.");
            StatusText = Loc.T("Fehler beim Kugane-Turm-Puzzle - gestoppt.", "Error in the Kugane Tower puzzle - stopped.");
            Stop();
        }
    }

    private void UpdateCheckingPreconditions()
    {
        if (Plugin.ClientState.TerritoryType != KuganeTerritoryId)
        {
            StatusText = Loc.T("Kugane-Turm: nicht in Kugane - abgebrochen.", "Kugane Tower: not in Kugane - aborted.");
            Stop();
            return;
        }

        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            StatusText = Loc.T("Kugane-Turm: wartet, bis der Kampf vorbei ist...", "Kugane Tower: waiting for combat to end...");
            return;
        }

        if (!navmeshIsReady.InvokeFunc())
        {
            StatusText = Loc.T("Kugane-Turm: warte auf vnavmesh-Navmesh...", "Kugane Tower: waiting for vnavmesh's navmesh...");
            return;
        }

        if (Plugin.Condition[ConditionFlag.Mounted])
            Plugin.TryDismount();

        // Nutzeranforderung: steht man beim (Neu-)Start schon nah an einem der 79 Punkte (z.B. nach
        // einem Stop mitten im Klettern, oder weil der vorgeschlagene Wiedereinstiegspunkt nach einem
        // Sturz genau dort liegt), dort direkt fortsetzen statt erst komplett zum bodennahen
        // Startpunkt zu laufen und von da wieder hochzuklettern.
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        if (playerPos is { } pos)
        {
            var nearest = FindNearestPointIndex(pos);
            if (nearest.HasValue)
            {
                Plugin.Log.Info($"[KuganeTowerJump] Schon nah an Schritt {nearest.Value + 1} - setze dort fort statt zum Startpunkt zu laufen.");
                BeginPoint(nearest.Value);
                return;
            }
        }

        // Nutzeranforderung: nicht mehr IMMER erst über ApproachJumpPoint umwegen - zuerst eine
        // echte vnavmesh-Wegsuche direkt zum Startpunkt versuchen (von dort kommt man inzwischen aus
        // vielen Anlaufrichtungen, z.B. über Sightseeing, ohne den alten Hinderniss-Umweg). Lehnt
        // vnavmesh das ab (siehe ApproachJumpPoint-Kommentar - echter Sprung über eine Kante nötig,
        // der per reiner Wegsuche nicht gefunden wird), erst dann wie bisher über den Zwischenpunkt.
        if (pathfindAndMoveCloseTo.InvokeFunc(StartPosition, false, 0.3f))
        {
            goingToStartLastPos = null;
            SetState(State.GoingToStart);
            StatusText = Loc.T("Kugane-Turm: laufe zum Startpunkt...", "Kugane Tower: walking to the start point...");
            return;
        }

        moveToPath.InvokeAction(new List<Vector3> { ApproachJumpPoint }, false);
        goingToStartLastPos = null;
        SetState(State.ApproachingJumpPoint);
        StatusText = Loc.T("Kugane-Turm: laufe zum Sprungpunkt vor dem Startpunkt...", "Kugane Tower: walking to the jump point before the start point...");
    }

    /// <summary>Höchster Punktindex, dessen Position innerhalb von LocalRetryRadius/-HeightMargin der übergebenen Position liegt - null, wenn keiner nahe genug ist.</summary>
    private static int? FindNearestPointIndex(Vector3 playerPos)
    {
        for (var i = Points.Length - 1; i >= 0; i--)
        {
            var p = Points[i].Pos;
            if (playerPos.Y >= p.Y - LocalRetryHeightMargin
                && Vector2.Distance(new Vector2(playerPos.X, playerPos.Z), new Vector2(p.X, p.Z)) <= LocalRetryRadius)
                return i;
        }

        return null;
    }

    /// <summary>Siehe ApproachJumpPoint-Kommentar - läuft zu diesem Zwischenpunkt, springt dort ab, dann normal weiter zum eigentlichen Startpunkt (über die bereits vorhandene GoingToStart-Logik inkl. Stuck-Erkennung).</summary>
    private void UpdateApproachingJumpPoint()
    {
        if (pathIsRunning.InvokeFunc() || Plugin.IsVnavPathfindInProgress())
        {
            if (DateTime.UtcNow - stateEnteredAt > GoingToStartTimeout)
            {
                Plugin.Log.Info("[KuganeTowerJump] UpdateApproachingJumpPoint: Zeitüberschreitung - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                TryTeleportToMainAetheryteThenRetry();
            }

            return;
        }

        Plugin.TryJump();
        pathfindAndMoveCloseTo.InvokeFunc(StartPosition, false, 0.3f);
        goingToStartLastPos = null;
        SetState(State.GoingToStart);
        StatusText = Loc.T("Kugane-Turm: laufe zum Startpunkt...", "Kugane Tower: walking to the start point...");
    }

    /// <summary>
    /// Siehe HandleFall/pendingRecoveryTarget-Kommentar - läuft zum Zwischenpunkt, dann ganz normal
    /// weiter zu pendingRecoveryTarget (StartPosition für die Phase-1-Variante, ein bestimmter
    /// Points[]-Eintrag für die Phase-3-Variante) über die bestehende GoingToStart-Logik (deren
    /// Stuck-Erkennung/Abschluss [BeginPoint(currentPointIndex)] ist unabhängig vom konkreten Ziel).
    /// </summary>
    private void UpdateFallRecoveryWaypoint()
    {
        if (pathIsRunning.InvokeFunc() || Plugin.IsVnavPathfindInProgress())
        {
            if (DateTime.UtcNow - stateEnteredAt > GoingToStartTimeout)
            {
                Plugin.Log.Info("[KuganeTowerJump] UpdateFallRecoveryWaypoint: Zeitüberschreitung - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                TryTeleportToMainAetheryteThenRetry();
            }

            return;
        }

        pathfindAndMoveCloseTo.InvokeFunc(pendingRecoveryTarget, false, 0.3f);
        goingToStartLastPos = null;
        SetState(State.GoingToStart);
        StatusText = Loc.T("Kugane-Turm: laufe zurück...", "Kugane Tower: walking back...");
    }

    /// <summary>Siehe activePhase4RecoverySteps-Kommentar - läuft/springt der Reihe nach alle hinterlegten Zwischenschritte ab, dann weiter zu pendingRecoveryTarget über die bestehende GoingToStart-Logik.</summary>
    private void BeginPhase4RecoveryStep(int index)
    {
        phase4RecoveryStepIndex = index;
        var step = activePhase4RecoverySteps[index];
        jumpSent = step.JumpDelay == null;
        hasSeenPathRunningThisLeg = false;
        moveToPath.InvokeAction(new List<Vector3> { step.Pos }, false);
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T("Kugane-Turm: Sturz in Phase 4 - laufe zurück zum Start von Phase 4...", "Kugane Tower: fell in phase 4 - walking back to the start of phase 4...");
    }

    private void UpdatePhase4FallRecovery()
    {
        var step = activePhase4RecoverySteps[phase4RecoveryStepIndex];
        var pathActive = pathIsRunning.InvokeFunc() || Plugin.IsVnavPathfindInProgress();

        if (pathActive && !hasSeenPathRunningThisLeg)
        {
            hasSeenPathRunningThisLeg = true;
            pathStartedAt = DateTime.UtcNow;
        }

        if (!jumpSent && step.JumpDelay is { } delay)
        {
            if (delay <= 0f)
            {
                jumpSent = true;
                Plugin.TryJump();
            }
            else if (hasSeenPathRunningThisLeg && (DateTime.UtcNow - pathStartedAt).TotalSeconds >= delay)
            {
                jumpSent = true;
                Plugin.TryJump();
            }
        }

        if (pathActive)
        {
            if (DateTime.UtcNow - stateEnteredAt > MovingTimeout)
            {
                Plugin.Log.Info("[KuganeTowerJump] UpdatePhase4FallRecovery: Zeitüberschreitung - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                TryTeleportToMainAetheryteThenRetry();
            }

            return;
        }

        if (phase4RecoveryStepIndex + 1 < activePhase4RecoverySteps.Length)
        {
            BeginPhase4RecoveryStep(phase4RecoveryStepIndex + 1);
            return;
        }

        pathfindAndMoveCloseTo.InvokeFunc(pendingRecoveryTarget, false, 0.3f);
        goingToStartLastPos = null;
        SetState(State.GoingToStart);
        StatusText = Loc.T("Kugane-Turm: laufe zum Start von Phase 4...", "Kugane Tower: walking to the start of phase 4...");
    }

    private void UpdateGoingToStart()
    {
        if (pathIsRunning.InvokeFunc() || Plugin.IsVnavPathfindInProgress())
        {
            // Nutzeranforderung: läuft er dabei gegen eine Wand/Brücke und kommt nicht voran, nach
            // ein paar Sekunden (nicht erst nach dem langsameren generischen Stuck-Rhythmus) zum
            // Haupt-Ätheryten teleportieren und von dort aus erneut zum Startpunkt versuchen.
            var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
            if (playerPos.HasValue)
            {
                if (goingToStartLastPos == null)
                {
                    goingToStartLastPos = playerPos.Value;
                    goingToStartLastProgressCheckAt = DateTime.UtcNow;
                    goingToStartStuckJumpAttempts = 0;
                }
                else if (DateTime.UtcNow - goingToStartLastProgressCheckAt >= GoingToStartStuckCheckInterval)
                {
                    if (Vector3.Distance(goingToStartLastPos.Value, playerPos.Value) < GoingToStartStuckMinProgress)
                    {
                        if (goingToStartStuckJumpAttempts < GoingToStartMaxStuckJumpAttempts)
                        {
                            goingToStartStuckJumpAttempts++;
                            Plugin.Log.Info($"[KuganeTowerJump] UpdateGoingToStart: scheinbar steckengeblieben - versuche einen Sprung ({goingToStartStuckJumpAttempts}/{GoingToStartMaxStuckJumpAttempts}).");
                            Plugin.TryJump();
                            goingToStartLastPos = playerPos.Value;
                            goingToStartLastProgressCheckAt = DateTime.UtcNow;
                            return;
                        }

                        Plugin.Log.Info("[KuganeTowerJump] UpdateGoingToStart: weiterhin steckengeblieben (auch nach Sprüngen) - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                        TryTeleportToMainAetheryteThenRetry();
                        return;
                    }

                    goingToStartLastPos = playerPos.Value;
                    goingToStartLastProgressCheckAt = DateTime.UtcNow;
                    goingToStartStuckJumpAttempts = 0;
                }
            }

            if (DateTime.UtcNow - stateEnteredAt > GoingToStartTimeout)
            {
                Plugin.Log.Info("[KuganeTowerJump] UpdateGoingToStart: Zeitüberschreitung - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                TryTeleportToMainAetheryteThenRetry();
            }

            return;
        }

        BeginPoint(currentPointIndex);
    }

    /// <summary>
    /// Siehe UpdateGoingToStart-Kommentar - teleportiert zum Haupt-Ätheryten von Kugane (per
    /// Lifestream) und versucht danach erneut, zu Fuß zum Startpunkt (bzw. dem nächsten nahen Punkt,
    /// siehe UpdateCheckingPreconditions) zu kommen. Klappt der Teleport selbst nicht (Lifestream
    /// fehlt/Ätheryte nicht freigeschaltet), ganz abbrechen statt endlos weiterzuversuchen.
    /// </summary>
    private void TryTeleportToMainAetheryteThenRetry()
    {
        StopPath();

        if (!IsLifestreamAvailable())
        {
            StatusText = Loc.T("Kugane-Turm: Startpunkt nicht erreichbar (Lifestream fehlt) - abgebrochen.", "Kugane Tower: start point not reachable (Lifestream missing) - aborted.");
            Stop();
            return;
        }

        var mainAetheryteId = Plugin.FindUnlockedMainAetheryteId(KuganeTerritoryId);
        if (mainAetheryteId == null || !lifestreamTeleport.InvokeFunc(mainAetheryteId.Value, (byte)0))
        {
            StatusText = Loc.T("Kugane-Turm: Startpunkt nicht erreichbar (Teleport abgelehnt) - abgebrochen.", "Kugane Tower: start point not reachable (teleport rejected) - aborted.");
            Stop();
            return;
        }

        mainAetheryteTeleportHasSeenLoadingScreen = false;
        SetState(State.TeleportingToMainAetheryte);
        StatusText = Loc.T("Kugane-Turm: teleportiere zum Haupt-Ätheryten...", "Kugane Tower: teleporting to the main aetheryte...");
    }

    /// <summary>
    /// Wie SightseeingAutomation.UpdateTeleportingHomeAfterCompletion - BetweenAreas/-51 statt
    /// lifestreamIsBusy/Zonenwechsel (Kugane hat nur eine Zonen-ID, der Teleport bleibt also in
    /// derselben Zone; lifestreamIsBusy meldet "fertig" schon während der Besetzungszeit, lange vor
    /// dem eigentlichen Ladebildschirm).
    /// </summary>
    private void UpdateTeleportingToMainAetheryte()
    {
        var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (loading)
            mainAetheryteTeleportHasSeenLoadingScreen = true;

        if (loading || !mainAetheryteTeleportHasSeenLoadingScreen)
        {
            if (DateTime.UtcNow - stateEnteredAt > MainAetheryteTeleportTimeout)
            {
                StatusText = Loc.T("Kugane-Turm: Teleport zum Haupt-Ätheryten dauert zu lange - abgebrochen.", "Kugane Tower: teleporting to the main aetheryte is taking too long - aborted.");
                StopLifestream();
                Stop();
            }

            return;
        }

        if (DateTime.UtcNow - stateEnteredAt < MainAetheryteTeleportSettleDelay)
            return;

        SetState(State.CheckingPreconditions);
        StatusText = Loc.T("Kugane-Turm: Prüfe Voraussetzungen...", "Kugane Tower: Checking preconditions...");
    }

    private void BeginPoint(int index)
    {
        if (index != currentPointIndex)
            localRetryCount = 0;

        currentPointIndex = index;
        var point = Points[index];
        jumpSent = point.JumpDelay == null;
        hasSeenPathRunningThisLeg = false;

        var note = string.IsNullOrEmpty(point.Note) ? string.Empty : $" · {point.Note}";

        // Siehe CrawlIndices-Kommentar - kein normaler Laufauftrag, stattdessen millimeterweise
        // nudgen (UpdateCrawling).
        if (CrawlIndices.Contains(index))
        {
            crawlLastStepAt = DateTime.UtcNow;
            SetState(State.Crawling);
            StatusText = Loc.T(
                $"Kugane-Turm: Schritt {index + 1}/{Points.Length}{note}...",
                $"Kugane Tower: step {index + 1}/{Points.Length}{note}...");
            return;
        }

        var waypoints = new List<Vector3> { point.Pos };
        moveToPath.InvokeAction(waypoints, false);

        SetState(State.MovingToPoint);
        StatusText = Loc.T(
            $"Kugane-Turm: Schritt {index + 1}/{Points.Length}{note}...",
            $"Kugane Tower: step {index + 1}/{Points.Length}{note}...");
    }

    /// <summary>Siehe CrawlIndices-Kommentar.</summary>
    private void UpdateCrawling()
    {
        var point = Points[currentPointIndex];
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        if (playerPos is not { } pos)
            return;

        if (Vector3.Distance(pos, point.Pos) <= CrawlArrivalTolerance)
        {
            // Nah genug dran (durch den Sturz über die Kante) - ab hier übernimmt die normale
            // Sturzprüfung/Settling-Logik wie bei jedem anderen Punkt, ohne einen weiteren
            // moveToPath-Auftrag (pathIsRunning ist bereits/sofort false).
            hasSeenPathRunningThisLeg = false;
            SetState(State.MovingToPoint);
            return;
        }

        if (DateTime.UtcNow - crawlLastStepAt < CrawlStepInterval)
            return;

        crawlLastStepAt = DateTime.UtcNow;
        var direction = Vector3.Normalize(point.Pos - pos);
        var nudgeTarget = pos + direction * CrawlStepDistance;
        moveToPath.InvokeAction(new List<Vector3> { nudgeTarget }, false);
    }

    private void UpdateMovingToPoint()
    {
        var point = Points[currentPointIndex];
        var sinceStart = DateTime.UtcNow - stateEnteredAt;
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        var pathActive = pathIsRunning.InvokeFunc() || Plugin.IsVnavPathfindInProgress();

        // Für sehr kurze Verzögerungen (0.05/0.08s) kann der Dalamud-IPC-Roundtrip zu vnavmesh länger
        // dauern, als die Verzögerung selbst - ohne diese Prüfung wurde teils gesprungen, BEVOR der
        // Charakter überhaupt losgelaufen war (kein Schwung, Sprung fast aus dem Stand statt mit
        // Anlauf), Nutzer-Report: "fällt jedes Mal zwischen den ersten beiden Pfosten". Daher die
        // Verzögerung erst ab dem Moment zählen, in dem die Bewegung TATSÄCHLICH bestätigt läuft -
        // außer bei wait=0 (reiner Stand-Sprung), der soll weiterhin sofort/ohne Bewegung auslösen.
        if (pathActive && !hasSeenPathRunningThisLeg)
        {
            hasSeenPathRunningThisLeg = true;
            pathStartedAt = DateTime.UtcNow;
        }

        if (!jumpSent && point.JumpDelay is { } delay)
        {
            if (delay <= 0f)
            {
                jumpSent = true;
                Plugin.TryJump();
            }
            else if (hasSeenPathRunningThisLeg && (DateTime.UtcNow - pathStartedAt).TotalSeconds >= delay)
            {
                jumpSent = true;
                Plugin.TryJump();
            }
        }

        if (pathActive)
        {
            if (sinceStart > MovingTimeout)
                HandleFall();

            return;
        }

        // Sturzerkennung NACH der Ankunft (siehe Aufgabenstellung: "nach jedem Punkt prüfen") gegen
        // den ZIELPUNKT - absichtlich ODER (nicht UND): während des Laufs/Sprungs selbst ist man
        // natürlicherweise noch unter/neben dem Ziel, das wäre sonst dauernd ein Fehlalarm. Erst NACH
        // dem Stillstand zeigt "deutlich tiefer ODER weit daneben" einen echten Sturz an.
        if (playerPos is { } pos
            && (pos.Y < point.Pos.Y - FallYMargin
                || Vector2.Distance(new Vector2(pos.X, pos.Z), new Vector2(point.Pos.X, point.Pos.Z)) > FallHorizontalMargin))
        {
            // Nutzeranforderung: daneben gesprungen (z.B. Pfosten verfehlt), aber noch in der Nähe
            // des Absprungpunkts (Balkon etc.) - dann einfach DENSELBEN Sprung von dort aus erneut
            // versuchen, statt gleich die volle Sturz-Behandlung (Wiedereinstiegspunkt vorschlagen,
            // ggf. ganz neu starten) anzustoßen.
            var launchPoint = LaunchPointFor(currentPointIndex);
            var stillNearLaunch = pos.Y >= launchPoint.Y - LocalRetryHeightMargin
                                  && Vector2.Distance(new Vector2(pos.X, pos.Z), new Vector2(launchPoint.X, launchPoint.Z)) <= LocalRetryRadius;

            var unlimitedRetries = UnlimitedLocalRetryIndices.Contains(currentPointIndex);
            if (stillNearLaunch && (localRetryCount < MaxLocalRetries || unlimitedRetries))
            {
                localRetryCount++;
                var retryLabel = unlimitedRetries ? $"{localRetryCount}" : $"{localRetryCount}/{MaxLocalRetries}";
                Plugin.Log.Info($"[KuganeTowerJump] Schritt {currentPointIndex + 1}: daneben gesprungen, aber noch nahe am Absprungpunkt - wiederhole ({retryLabel}).");
                StatusText = Loc.T(
                    $"Kugane-Turm: Schritt {currentPointIndex + 1} daneben - wiederhole ({retryLabel})...",
                    $"Kugane Tower: missed step {currentPointIndex + 1} - retrying ({retryLabel})...");
                BeginPoint(currentPointIndex);
                return;
            }

            HandleFall();
            return;
        }

        SetState(State.Settling);
    }

    private static Vector3 LaunchPointFor(int index) => index == 0 ? StartPosition : Points[index - 1].Pos;

    private void UpdateSettling()
    {
        var point = Points[currentPointIndex];

        // Siehe JumpPoint.SkipSettleAfter-Kommentar - diesen Punkt ohne die übliche Pause überspringen.
        // Siehe JumpPoint.ExtraWaitAfter-Kommentar - umgekehrter Fall, zusätzlich zur SettleDuration
        // noch länger warten.
        var requiredWait = point.SkipSettleAfter ? TimeSpan.Zero : SettleDuration + (point.ExtraWaitAfter ?? TimeSpan.Zero);
        if (DateTime.UtcNow - stateEnteredAt < requiredWait)
            return;

        if (currentPointIndex + 1 < Points.Length)
        {
            BeginPoint(currentPointIndex + 1);
            return;
        }

        state = State.Done;
        IsActive = false;
        ReachedTop = true;
        StatusText = Loc.T("Kugane-Turm: oben angekommen.", "Kugane Tower: reached the top.");
    }

    /// <summary>
    /// Sturz - vnavmesh stoppen, besten Wiedereinstiegspunkt vorschlagen (letzter reiner Laufpunkt
    /// [JumpDelay == null], dessen Y-Höhe höchstens 0,5 über der aktuellen liegt) und entweder
    /// automatisch erneut versuchen (Configuration.KuganeTowerJumpAutoRetry, max. 3x) oder auf eine
    /// Nutzerentscheidung (Resume/Restart/Stop) warten.
    /// </summary>
    private void HandleFall()
    {
        StopPath();
        fellAtIndex = currentPointIndex;

        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? Points[currentPointIndex].Pos;

        // Siehe DeepFallPosition-Kommentar - ganz bis auf den Boden gefallen, kein normaler
        // Wiedereinstiegspunkt kommt von hier aus in Frage, UND der direkte Weg zurück zum
        // Startpunkt klappt nicht - erst über den eigens dafür hinterlegten Zwischenpunkt laufen.
        if (Vector3.Distance(playerPos, DeepFallPosition) <= DeepFallPositionTolerance)
        {
            resumeFromIndex = 0;
            currentPointIndex = 0;
            autoRetryCount = 0;
            pendingRecoveryTarget = StartPosition;
            Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {fellAtIndex + 1} - ganz auf den Boden gefallen, laufe über {DeepFallRecoveryWaypoint} zurück zum Startpunkt.");
            moveToPath.InvokeAction(new List<Vector3> { DeepFallRecoveryWaypoint }, false);
            goingToStartLastPos = null;
            SetState(State.FallRecoveryWaypoint);
            StatusText = Loc.T("Kugane-Turm: ganz runtergefallen - laufe zurück zum Startpunkt...", "Kugane Tower: fell all the way down - walking back to the start point...");
            return;
        }

        // Siehe Phase3FallRecoveryWaypoint-Kommentar - JEDER Sturz in Phase 3, unabhängig von der
        // genauen Landeposition (Nutzerbestätigung).
        if (fellAtIndex >= Phase3StartIndex && fellAtIndex <= Phase3LastIndex)
        {
            resumeFromIndex = Phase3StartIndex;
            currentPointIndex = Phase3StartIndex;
            autoRetryCount = 0;
            pendingRecoveryTarget = Points[Phase3StartIndex].Pos;
            Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {fellAtIndex + 1} (Phase 3) - laufe über {Phase3FallRecoveryWaypoint} zurück zum Start von Phase 3 (Schritt {Phase3StartIndex + 1}).");
            moveToPath.InvokeAction(new List<Vector3> { Phase3FallRecoveryWaypoint }, false);
            goingToStartLastPos = null;
            SetState(State.FallRecoveryWaypoint);
            StatusText = Loc.T("Kugane-Turm: Sturz in Phase 3 - laufe zurück zum Start von Phase 3...", "Kugane Tower: fell in phase 3 - walking back to the start of phase 3...");
            return;
        }

        // Siehe Phase4MultiStepFallRoutes-Kommentar - Sturz in die Nähe EINER dieser Positionen in
        // Phase 4 (unabhängig vom genauen Sturzpunkt, ähnlich Phase3FallRecoveryWaypoint).
        foreach (var route in Phase4MultiStepFallRoutes)
        {
            if (Vector3.Distance(playerPos, route.FallPosition) > Phase4MultiStepFallPositionTolerance)
                continue;

            var (resumePos, resumeIndexForRoute) = PhaseStarts[route.ResumePhaseIndex];
            resumeFromIndex = resumeIndexForRoute;
            currentPointIndex = resumeIndexForRoute;
            autoRetryCount = 0;
            pendingRecoveryTarget = resumePos;
            activePhase4RecoverySteps = route.Steps;
            Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {fellAtIndex + 1} - laufe über mehrere Zwischenschritte zurück zum Start von Phase {route.ResumePhaseIndex + 1} (Schritt {resumeIndexForRoute + 1}).");
            BeginPhase4RecoveryStep(0);
            SetState(State.Phase4FallRecovery);
            return;
        }

        // Siehe Phase4SecondFallRecoveryWaypoint-Kommentar.
        var nearSecondFallPosition = false;
        foreach (var candidate in Phase4SecondFallPositions)
        {
            if (Vector3.Distance(playerPos, candidate) <= Phase4SecondFallPositionTolerance)
            {
                nearSecondFallPosition = true;
                break;
            }
        }

        if (nearSecondFallPosition)
        {
            var (phase4PosB, phase4ResumeIndexB) = PhaseStarts[3];
            resumeFromIndex = phase4ResumeIndexB;
            currentPointIndex = phase4ResumeIndexB;
            autoRetryCount = 0;
            pendingRecoveryTarget = phase4PosB;
            Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {fellAtIndex + 1} (Phase 4, zweite bekannte Landeposition) - laufe über {Phase4SecondFallRecoveryWaypoint} zurück zum Start von Phase 4 (Schritt {phase4ResumeIndexB + 1}).");
            moveToPath.InvokeAction(new List<Vector3> { Phase4SecondFallRecoveryWaypoint }, false);
            goingToStartLastPos = null;
            SetState(State.FallRecoveryWaypoint);
            StatusText = Loc.T("Kugane-Turm: Sturz in Phase 4 - laufe zurück zum Start von Phase 4...", "Kugane Tower: fell in phase 4 - walking back to the start of phase 4...");
            return;
        }

        // Siehe DirectPhaseRestartFallPositions-Kommentar.
        foreach (var (directFallPos, directResumePhaseIndex) in DirectPhaseRestartFallPositions)
        {
            if (Vector3.Distance(playerPos, directFallPos) > DirectPhaseRestartFallPositionTolerance)
                continue;

            var (directResumePos, directResumeIndex) = PhaseStarts[directResumePhaseIndex];
            resumeFromIndex = directResumeIndex;
            currentPointIndex = directResumeIndex;
            autoRetryCount = 0;
            pendingRecoveryTarget = directResumePos;
            Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {fellAtIndex + 1} - laufe direkt zurück zum Start von Phase {directResumePhaseIndex + 1} (Schritt {directResumeIndex + 1}).");
            pathfindAndMoveCloseTo.InvokeFunc(directResumePos, false, 0.3f);
            goingToStartLastPos = null;
            SetState(State.GoingToStart);
            StatusText = Loc.T(
                $"Kugane-Turm: Sturz - laufe zurück zum Start von Phase {directResumePhaseIndex + 1}...",
                $"Kugane Tower: fell - walking back to the start of phase {directResumePhaseIndex + 1}...");
            return;
        }

        // Siehe FallResumeOverrides-Kommentar - gezielt hinterlegter Wiedereinstieg für einzelne
        // Sturzpunkte, VOR der generischen Suche weiter unten (die nur reine Laufpunkte vorschlägt).
        int? resumeIndex = FallResumeOverrides.TryGetValue(fellAtIndex, out var overrideIndex) ? overrideIndex : null;
        for (var i = currentPointIndex; resumeIndex == null && i >= 0; i--)
        {
            if (Points[i].JumpDelay != null)
                continue;

            if (Points[i].Pos.Y <= playerPos.Y + 0.5f)
            {
                resumeIndex = i;
                break;
            }
        }

        resumeFromIndex = resumeIndex;
        Plugin.Log.Info($"[KuganeTowerJump] Sturz bei Schritt {currentPointIndex + 1} - Wiedereinstieg bei Schritt {(resumeIndex + 1)?.ToString() ?? "1 (Startpunkt)"}.");

        if (Plugin.Instance.Configuration.KuganeTowerJumpAutoRetry && autoRetryCount < MaxAutoRetries)
        {
            autoRetryCount++;
            StatusText = Loc.T(
                $"Kugane-Turm: Sturz bei Schritt {currentPointIndex + 1} - automatischer Versuch {autoRetryCount}/{MaxAutoRetries}...",
                $"Kugane Tower: fell at step {currentPointIndex + 1} - automatic retry {autoRetryCount}/{MaxAutoRetries}...");
            StartFromPoint(resumeIndex ?? 0);
            return;
        }

        state = State.FellWaitingForDecision;
        stateEnteredAt = DateTime.UtcNow;
        var resumeText = resumeIndex.HasValue
            ? Loc.T($"Weiter ab Schritt {resumeIndex.Value + 1}?", $"Resume from step {resumeIndex.Value + 1}?")
            : Loc.T("Zurück zum Startpunkt?", "Back to the start point?");
        StatusText = Loc.T(
            $"Kugane-Turm: Sturz bei Schritt {currentPointIndex + 1} - {resumeText}",
            $"Kugane Tower: fell at step {currentPointIndex + 1} - {resumeText}");
    }

    private void SetState(State newState)
    {
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
