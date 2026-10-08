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

    public readonly record struct JumpPoint(Vector3 Pos, float? JumpDelay, string? Note);

    public static readonly Vector3 StartPosition = new(-41.66f, 14.02f, -34.77f);

    // 1:1 aus dem Lua-Skript übernommen (gleiche Reihenfolge/Koordinaten/wait-Werte); auskommentierte
    // Punkte im Original (Zeilen 76/77 der Lua-Quelle) wurden dort schon durch die jeweils aktive
    // Folgezeile ersetzt und sind hier entsprechend weggelassen.
    public static readonly JumpPoint[] Points =
    {
        new(new Vector3(-41.07f, 15.49f, -37.5f), 0.08f, "jump on railing"),
        new(new Vector3(-40.89f, 15.52f, -35.79f), null, "adjust on railing"),
        new(new Vector3(-39.36f, 17.2f, -38.42f), 0.05f, "jump on peg"),
        new(new Vector3(-39.3f, 17.2f, -37.9f), null, null),
        new(new Vector3(-36.97f, 17.41f, -39.1f), 0.00f, null),
        new(new Vector3(-36.92f, 17.41f, -39.15f), null, null),
        new(new Vector3(-33.76f, 19.21f, -38.91f), 0.08f, null),
        new(new Vector3(-30.42f, 20.91f, -38.71f), 0.08f, null),
        new(new Vector3(-32.01f, 22.85f, -40.35f), 0.1f, "green roof"),
        new(new Vector3(-28.05f, 23.63f, -45.98f), null, null),
        new(new Vector3(-27.65f, 24.18f, -70.36f), null, null),
        new(new Vector3(-38.29f, 24.09f, -80.26f), null, null),
        new(new Vector3(-41.47f, 25.55f, -78.2f), null, null),
        new(new Vector3(-43.11f, 26.6f, -78.66f), 0.08f, "edge of balcony"),
        new(new Vector3(-44.11f, 28.1f, -78.84f), 0.08f, "railing"),
        new(new Vector3(-51.4597f, 26.59994f, -80.77885f), null, null),
        new(new Vector3(-52.48f, 28.3f, -81.71f), 0.00f, "post"),
        new(new Vector3(-52.18f, 28.3f, -81.52f), null, null),
        new(new Vector3(-53.99f, 30f, -82.9f), 0.00f, "peg"),
        new(new Vector3(-54.31f, 31.76f, -80.3f), 0.08f, "roof"),
        new(new Vector3(-45.89f, 40.81f, -70.41f), null, null),
        new(new Vector3(-46.56f, 42.11f, -70.26f), 0.00f, "peg 1"),
        new(new Vector3(-46.41f, 42.11f, -70.47f), null, null),
        new(new Vector3(-49.59f, 43.81f, -70.29f), 0.08f, "peg 2"),
        new(new Vector3(-49.44f, 43.8f, -70.45f), null, null),
        new(new Vector3(-52.57f, 45.31f, -70.33f), 0.05f, "peg 3"),
        new(new Vector3(-49.62f, 47.11f, -70.24f), 0.08f, "peg 4"),
        new(new Vector3(-49.14f, 47.11f, -70.67f), null, null),
        new(new Vector3(-46.39f, 48.91f, -70.69f), 0.08f, "2nd to last jump before 3rd roof"),
        new(new Vector3(-46f, 48.91f, -71.08f), null, null),
        new(new Vector3(-49.02f, 50.44f, -70.56f), 0.08f, "jump to roof"),
        new(new Vector3(-53.22f, 52.1f, -66.66f), null, null),
        new(new Vector3(-53.89f, 53.68f, -65.98f), 0.00f, null),
        new(new Vector3(-55.72f, 54.51f, -66.72f), 0.00f, null),
        new(new Vector3(-55.64f, 53.44f, -65.25f), null, null),
        new(new Vector3(-56.31f, 54.82f, -63.22f), 0.08f, null),
        new(new Vector3(-57.34f, 54.78f, -62.79f), null, null),
        new(new Vector3(-57.14f, 54.95f, -58.82f), null, null),
        new(new Vector3(-55.81f, 56.47f, -58.96f), null, null),
        new(new Vector3(-54.5f, 57.74f, -58.44f), 0.08f, "jump onto wood area"),
        new(new Vector3(-54.48f, 57.73f, -56.56f), null, null),
        new(new Vector3(-55.06f, 59.53f, -55.71f), 0.00f, "peg 1"),
        new(new Vector3(-54.74f, 61.31f, -58.41f), 0.00f, "peg 2"),
        new(new Vector3(-54.46f, 62.75f, -56.34f), 0.08f, "flat board"),
        new(new Vector3(-54.48f, 62.75f, -56.67f), null, null),
        new(new Vector3(-55.01f, 64.31f, -59.55f), 0.08f, "jump to circle"),
        new(new Vector3(-55.05f, 65.94f, -62.18f), 0.08f, "jump above the circle"),
        new(new Vector3(-54.32f, 68.48f, -64.66f), null, null),
        new(new Vector3(-52.49f, 67.19f, -65.71f), null, null),
        new(new Vector3(-49.14f, 68.41f, -65.81f), 0.08f, "peg 1"),
        new(new Vector3(-46.84f, 70.21f, -65.81f), 0.00f, "peg 2"),
        new(new Vector3(-44.77f, 72.01f, -65.78f), 0.00f, "peg 3"),
        new(new Vector3(-49.23f, 73.51f, -65.64f), 0.08f, "peg 4"),
        new(new Vector3(-50.92f, 75.11f, -66.13f), 0.00f, "peg 5"),
        new(new Vector3(-47.11f, 76.41f, -65.88f), 0.08f, "peg 6"),
        new(new Vector3(-45.46f, 77.25f, -65.47f), 0.00f, "corner"),
        new(new Vector3(-41.58f, 79.05f, -65.55f), 0.08f, "peg 1"),
        new(new Vector3(-41.07f, 79.05f, -65.93f), null, "repositioning"),
        new(new Vector3(-39.84f, 80.85f, -63.6f), 0.08f, "peg 2"),
        new(new Vector3(-41.53f, 82.24f, -61.86f), 0.08f, "right side of flag"),
        new(new Vector3(-41.54f, 82.25f, -56.47f), 0.08f, "left side of flag"),
        new(new Vector3(-40.99f, 83.75f, -55.39f), 0.00f, "peg 1"),
        new(new Vector3(-40.4f, 83.75f, -55.31f), null, "reposition"),
        new(new Vector3(-39.12f, 85.55f, -51.9f), 0.1f, "peg 2"),
        new(new Vector3(-40.05f, 87.35f, -54.45f), 0.08f, "peg 3"),
        new(new Vector3(-40.35f, 88.49f, -54.42f), 0.00f, "jumping to ledge"),
        new(new Vector3(-39.96f, 89.65f, -52f), 0.08f, "peg 4"),
        new(new Vector3(-39.49f, 89.65f, -52.34f), null, null),
        new(new Vector3(-40.81f, 90.89f, -51.97f), 0.00f, "outer Ledge"),
        new(new Vector3(-42.18f, 89.16f, -53.73f), null, "going down to lower edge"),
        new(new Vector3(-41.64f, 89.15f, -65.36f), null, "walk across"),
        new(new Vector3(-46.15f, 89.15f, -65.34f), null, "turn the corner"),
        new(new Vector3(-48.28f, 90.88f, -66.2f), 0.00f, "jumping up"),
        new(new Vector3(-44.86f, 90.89f, -66.22f), null, "position for peg 3"),
        new(new Vector3(-39.95f, 91.01f, -68.3f), 0.1f, "last peg #3"),
        new(new Vector3(-40.25f, 91.01f, -68.85f), null, "positioning"),
        new(new Vector3(-36.94f, 92.81f, -67f), 0.1f, null),
        new(new Vector3(-37.46f, 94.6f, -65.43f), 0.00f, null),
        new(new Vector3(-38.51f, 95.38f, -65.56f), 0.00f, null), // Sightseeing-Punkt
    };

    private enum State
    {
        Idle,
        CheckingPreconditions,
        ApproachingJumpPoint,
        GoingToStart,
        TeleportingToMainAetheryte,
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

    /// <summary>Startet ab Punkt 1 (oder, falls zuvor ein Sturz vorlag und der Nutzer "Restart" wählt) komplett von vorne.</summary>
    public void Start() => StartFromPoint(0);

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
                case State.GoingToStart:
                    UpdateGoingToStart();
                    break;
                case State.TeleportingToMainAetheryte:
                    UpdateTeleportingToMainAetheryte();
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
                }
                else if (DateTime.UtcNow - goingToStartLastProgressCheckAt >= GoingToStartStuckCheckInterval)
                {
                    if (Vector3.Distance(goingToStartLastPos.Value, playerPos.Value) < GoingToStartStuckMinProgress)
                    {
                        Plugin.Log.Info("[KuganeTowerJump] UpdateGoingToStart: scheinbar steckengeblieben - teleportiere zum Haupt-Ätheryten und versuche erneut.");
                        TryTeleportToMainAetheryteThenRetry();
                        return;
                    }

                    goingToStartLastPos = playerPos.Value;
                    goingToStartLastProgressCheckAt = DateTime.UtcNow;
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

        var waypoints = new List<Vector3> { point.Pos };
        moveToPath.InvokeAction(waypoints, false);

        SetState(State.MovingToPoint);
        var note = string.IsNullOrEmpty(point.Note) ? string.Empty : $" · {point.Note}";
        StatusText = Loc.T(
            $"Kugane-Turm: Schritt {index + 1}/{Points.Length}{note}...",
            $"Kugane Tower: step {index + 1}/{Points.Length}{note}...");
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

            if (stillNearLaunch && localRetryCount < MaxLocalRetries)
            {
                localRetryCount++;
                Plugin.Log.Info($"[KuganeTowerJump] Schritt {currentPointIndex + 1}: daneben gesprungen, aber noch nahe am Absprungpunkt - wiederhole ({localRetryCount}/{MaxLocalRetries}).");
                StatusText = Loc.T(
                    $"Kugane-Turm: Schritt {currentPointIndex + 1} daneben - wiederhole ({localRetryCount}/{MaxLocalRetries})...",
                    $"Kugane Tower: missed step {currentPointIndex + 1} - retrying ({localRetryCount}/{MaxLocalRetries})...");
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
        if (DateTime.UtcNow - stateEnteredAt < SettleDuration)
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
        int? resumeIndex = null;
        for (var i = currentPointIndex; i >= 0; i--)
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
