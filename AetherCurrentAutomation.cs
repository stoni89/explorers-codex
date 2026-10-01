using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Ipc;

namespace TheExplorersCodex;

/// <summary>
/// Läuft nacheinander alle aktuell noch fehlenden Ätherströmungen der Zone ab (siehe
/// CollectionData/aethercurrents.json - Weltpositionen aus einem Community-Export, da weder das
/// Lumina-Sheet "AetherCurrent" selbst noch "MapMarker" dafür Positionsdaten liefern - siehe
/// Plugin.DumpAetherCurrentDebugInfo(AllZones), mit dem das empirisch ausgeschlossen wurde).
/// Anders als bei Aetheryten gibt es keinen dauerhaften Kartenpin, dafür (bestätigt) eine explizite
/// Klick-Interaktion - die Automation läuft daher zum nächsten ObjectKind.EventObj bei der
/// Zielposition und interagiert damit, genau wie AetheryteAutomation.UpdateInteracting es für
/// Aetheryten tut, nur mit EventObj statt ObjectKind.Aetheryte als Objektart.
/// </summary>
public sealed class AetherCurrentAutomation
{
    private enum State
    {
        Idle,
        Mounting,
        MovingTo,
        Interacting,
        JumpRoute,
        ReturningToStart,
    }

    // Manche Ätherströmungen sind nur über einen kurzen Sprung erreichbar (z.B. "The Dravanian
    // Forelands (Loth ast Gnath past second door)", siehe Plugin.AetherCurrentJumpRoutes) - State.
    // MovingTo läuft dafür zuerst ganz normal zum Startpunkt (Route.Start), erst DANACH übernimmt
    // dieser Zustand: abmounten, zum Absprungpunkt laufen+springen, dann normal zur echten Position
    // (entry.WorldPosition) weiter über BeginFinalApproach.
    private enum JumpPhase
    {
        Dismounting,
        WalkingToRunUp,
        RunningToJump,
    }

    private static readonly TimeSpan MountWaitTimeout = TimeSpan.FromSeconds(6);

    // Ätherströmungen haben (anders als Aetheryten) kein festes Weltobjekt, an das man exakt
    // heranlaufen könnte - die Ankunftstoleranz ist daher etwas großzügiger als
    // AetheryteAutomation.InteractDistance, um trotz ungenauer Community-Koordinaten trotzdem noch
    // in den (unbekannten, vermutlich mehrere Yalm großen) Entdeckungsradius zu kommen.
    private const float ArrivalTolerance = 6f;

    // Zweiter, viel engerer Laufauftrag direkt zur bekannten WorldPosition (siehe BeginFinalApproach) -
    // NACH dem groben Anflug (ArrivalTolerance), NUR wenn eine exakte Weltposition hinterlegt ist
    // (z.B. per Hand nachgetragen wie "Overlooking The Convictory", siehe aethercurrents.json), sonst
    // bliebe der Charakter bis zu ArrivalTolerance-Yalm neben dem tatsächlichen Punkt stehen
    // (Nutzeranforderung: "exakt auf die Position laufen"). Gleiche Toleranz wie SightseeingAutomation.
    private const float FinalApproachTolerance = 0.1f;

    private const float SprintDisableDistance = 8f;
    private static readonly TimeSpan StepMaxDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PathStartGracePeriod = TimeSpan.FromSeconds(5);

    // Umkreis um die Zielposition, in dem nach dem interagierbaren Weltobjekt gesucht wird -
    // Ätherströmungen haben keine eigene ObjectKind, sondern tauchen (Vermutung, analog zu anderen
    // Feldinteraktionen) als ObjectKind.EventObj auf. Großzügiger als AetheryteAutomation's 15f, da
    // die Community-Koordinaten hier ungenauer sein können als die aus dem Spiel selbst berechneten
    // Aetheryte-Positionen.
    private const float InteractObjectSearchRadius = 20f;

    // Wie lange nach Ankunft ohne passendes Objekt in der Nähe gewartet wird, bevor aufgegeben wird
    // (kurze Ladeverzögerung wie bei AetheryteAutomation.InteractObjectGracePeriod).
    private static readonly TimeSpan InteractObjectGracePeriod = TimeSpan.FromSeconds(5);

    // Wie lange nach dem Interagieren auf die tatsächliche Freischaltung gewartet wird (der
    // "Entdecken"-Effekt braucht evtl. einen kurzen Moment, ähnlich AetheryteAutomation.
    // UnlockWaitTimeout).
    private static readonly TimeSpan UnlockWaitTimeout = TimeSpan.FromSeconds(15);

    private const int MaxAttemptsPerTarget = 2;
    private readonly Dictionary<uint, int> attemptCounts = new();
    private readonly HashSet<uint> skippedIds = new();

    // Gegenwehr, falls man auf dem Weg zur/an der Ätherströmung angegriffen wird (Nutzeranforderung:
    // "alle Gegner töten, falls man im Kampf ist, sonst nicht") - identisches Vorgehen wie
    // HuntingLogAutomation.UpdateDefendingSelf, nur ohne dessen eigentlichen Kampf-Zustand (Aether
    // Currents kämpfen nie absichtlich, das hier ist ausschließlich ungeplante Gegenwehr).
    private const float AttackRange = 3.5f;
    private bool isDefendingSelf;
    private DateTime lastDefendApproachAt = DateTime.MinValue;
    private static readonly TimeSpan DefendApproachRetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CombatEnsureInterval = TimeSpan.FromSeconds(2);
    private DateTime lastCombatEnsureAt = DateTime.MinValue;

    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<bool> navmeshIsReady;
    private readonly ICallGateSubscriber<Vector3?> queryFlagToPoint;

    // Rohe Wegpunkt-Bewegung (kein Pathfinding) für den Sprung selbst (siehe State.JumpRoute) - genau
    // dieselbe IPC wie SightseeingAutomation für ihre Jumping Puzzles nutzt.
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> moveToPath;
    private readonly ICallGateSubscriber<float> pathGetTolerance;
    private readonly ICallGateSubscriber<float, object> pathSetTolerance;
    private float? savedPathTolerance;

    private State state = State.Idle;
    private CollectibleEntry? currentTargetEntry;
    private Vector3 currentTargetPosition;
    private DateTime stateEnteredAt;
    private bool hasSeenPathRunning;
    private DateTime lastRemountAttempt = DateTime.MinValue;
    private readonly NavigationStuckDetector stuckDetector = new();
    private readonly FlightPathUpgrade flightUpgrade = new(); // siehe Plugin.FlightPathUpgrade (Flugverbots-Bereiche)
    private bool hasInteractedThisCycle;
    private DateTime? interactObjectNotFoundSince;

    // Siehe UpdateInteracting-Kommentar zu Configuration.SimulateAetherCurrentAutomation - kurze
    // künstliche Pause statt auf ein eventuell gar nicht mehr vorhandenes Objekt zu warten.
    private DateTime? simulatedActivationStartedAt;
    private static readonly TimeSpan SimulatedActivationPause = TimeSpan.FromSeconds(1.5);
    private bool didFinalApproach;

    // Verhindert, dass Plugin.TryRemountAfterForcedDismount (gedacht für unfreiwilliges Absteigen
    // beim Schwimmen) den Charakter wieder aufsitzen lässt, nachdem WIR ihn absichtlich für den
    // engen Final Approach abgestiegen haben (siehe BeginFinalApproach) - sonst versucht er auf dem
    // letzten Stück wieder aufzumounten, statt zu Fuß exakt anzukommen (Nutzer-Report: "mountet paar
    // yards vorher ab und auf dem Punkt mountet er wieder auf"). Identisches Problem/dieselbe Lösung
    // wie in AetheryteAutomation/ChocobokeepAutomation/SightseeingAutomation.
    private bool hasIntentionallyDismounted;

    // Siehe Plugin.AetherCurrentJumpRoutes/State.JumpRoute-Kommentar - null, solange das aktuelle
    // Ziel keine hinterlegte Sprungroute hat oder noch nicht am Startpunkt angekommen ist.
    private Plugin.AetherCurrentJumpRoute? activeJumpRoute;
    private JumpPhase jumpPhase;
    private DateTime? jumpDismountedAt;

    // Bleibt (anders als activeJumpRoute, das schon nach der Landung wieder null wird) für die
    // GESAMTE Dauer des aktuellen Ziels gesetzt, sobald eine Sprungroute hinterlegt ist - nach
    // erfolgreicher Freischaltung läuft State.ReturningToStart damit noch einmal zurück zum
    // Startpunkt der Route, BEVOR es zum nächsten Ziel weitergeht (Nutzeranforderung: "nachdem der
    // Aether Current aktiviert wurde erstmal wieder zurück auf den Startpunkt"; die Landestelle
    // selbst ist oft ein schmaler Vorsprung, von dem aus man nicht sinnvoll weiterlaufen/-fliegen
    // kann).
    private Vector3? jumpRouteStartPoint;

    // Kurze Wartezeit nach dem Abmounten (wie DismountSettleDelay in anderen Automationen) - ein
    // Sprung-Versuch mitten in der Absteige-Animation greift nicht.
    private static readonly TimeSpan JumpDismountSettleDelay = TimeSpan.FromSeconds(1);

    public bool IsActive { get; private set; }

    private static readonly TimeSpan StatusLingerDuration = TimeSpan.FromSeconds(8);
    private string statusText = string.Empty;
    private DateTime statusSetAt = DateTime.MinValue;

    public string StatusText
    {
        get => statusText;
        private set
        {
            statusText = value;
            statusSetAt = DateTime.UtcNow;
        }
    }

    public bool ShouldShowStatusText => IsActive || DateTime.UtcNow - statusSetAt < StatusLingerDuration;

    public AetherCurrentAutomation()
    {
        pathfindAndMoveCloseTo = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathIsRunning = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathStop = Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        navmeshIsReady = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        queryFlagToPoint = Plugin.PluginInterface.GetIpcSubscriber<Vector3?>("vnavmesh.Query.Mesh.FlagToPoint");
        moveToPath = Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        pathGetTolerance = Plugin.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");
        pathSetTolerance = Plugin.PluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
    }

    public bool IsVNavmeshAvailable()
    {
        try
        {
            return pathfindAndMoveCloseTo.HasFunction && pathIsRunning.HasFunction && navmeshIsReady.HasFunction;
        }
        catch
        {
            return false;
        }
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
            Plugin.Log.Error(ex, "Fehler beim Stoppen von vnavmesh.");
        }
    }

    public void Start()
    {
        IsActive = true;
        state = State.Idle;
        currentTargetEntry = null;
        skippedIds.Clear();
        attemptCounts.Clear();
        isDefendingSelf = false;
        lastCombatEnsureAt = DateTime.MinValue;
        hasIntentionallyDismounted = false;
        activeJumpRoute = null;
        jumpRouteStartPoint = null;
        StatusText = Loc.T("Automation gestartet...", "Automation started...");
    }

    public void Stop()
    {
        IsActive = false;
        state = State.Idle;
        currentTargetEntry = null;
        StopPath();
        Plugin.ClearNavigationTarget();

        // Mitten in einer Sprungroute gestoppt - enge vnavmesh-Toleranz nicht dauerhaft gesetzt lassen.
        RestorePathTolerance();
        activeJumpRoute = null;
        jumpRouteStartPoint = null;
    }

    public void MarkUnavailable()
    {
        StatusText = Loc.T("vnavmesh nicht gefunden - bitte installieren.", "vnavmesh not found - please install it.");
    }

    /// <summary>
    /// NUR am Ziel (State.Interacting, siehe Nutzeranforderung "erst wenn man am Ziel angekommen ist")
    /// - wird man dort angegriffen, wird der normale Zustandsautomat angehalten, der Angreifer
    /// anvisiert (bei Bedarf hingelaufen) und das Kampf-Plugin (RotationSolver/WrathCombo/BossMod,
    /// siehe Plugin.CombatPlugin) eingeschaltet, bis kein Gegner mehr lebt - danach wird erst die
    /// Ätherströmung aktiviert. Während des Hinlaufens (MovingTo/Mounting) greift diese Gegenwehr
    /// bewusst NICHT, da sie sonst auch fremde, nur zufällig in der Nähe kämpfende Gegner einbeziehen
    /// würde. Ähnliches Vorgehen wie HuntingLogAutomation.UpdateDefendingSelf. Gibt true zurück,
    /// solange verteidigt wird (Aufrufer überspringt dann den Zustandsautomaten für diesen Frame).
    /// </summary>
    private bool UpdateDefendingSelf()
    {
        if (state != State.Interacting)
        {
            isDefendingSelf = false;
            return false;
        }

        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? currentTargetPosition;
            var attacker = Plugin.FindNearestAttacker(playerPos);
            if (attacker != null || (isDefendingSelf && Plugin.HasLiveTarget()))
            {
                if (!isDefendingSelf)
                {
                    Plugin.Log.Info($"[AetherCurrentAutomation] Angegriffen im Zustand {state} (von {attacker?.Name}) - wehre mich, bevor es weitergeht.");
                    isDefendingSelf = true;
                    StopPath();
                }

                if (!Plugin.HasLiveTarget() && attacker != null)
                    Plugin.SetTarget(attacker);

                Plugin.TryDismount();
                EnsureCombatMode();

                // Kampf-Plugins bewegen den Charakter nicht selbst - steht das Ziel außer Reichweite,
                // gedrosselt hinlaufen.
                if (Plugin.TargetManager.Target is { } target
                    && Vector3.Distance(playerPos, target.Position) > AttackRange
                    && !pathIsRunning.InvokeFunc()
                    && DateTime.UtcNow - lastDefendApproachAt > DefendApproachRetryInterval)
                {
                    lastDefendApproachAt = DateTime.UtcNow;
                    pathfindAndMoveCloseTo.InvokeFunc(target.Position, false, AttackRange);
                }

                StatusText = Loc.T($"Wehre mich gegen: {Plugin.TargetManager.Target?.Name}...", $"Defending against: {Plugin.TargetManager.Target?.Name}...");
                return true;
            }
        }

        if (!isDefendingSelf)
            return false;

        isDefendingSelf = false;
        Plugin.CombatPlugin.SetCombatMode(false);

        // Der Kampf kann den Charakter vom eigentlichen Ziel weggezogen haben (z.B. einem Angreifer
        // hinterher) - IMMER zurück zur Ätherströmung laufen, bevor wieder interagiert wird (Nutzer-
        // Report: "wenn man recht nah dran ist... muss er trotzdem danach zu dem Aether Punkt
        // laufen, sonst kann er ihn nicht aktivieren"). InteractObjectSearchRadius (20y, zum bloßen
        // AUFFINDEN des Objekts) reicht dafür nicht als Kriterium - der echte Interact-Radius des
        // Spiels ist viel enger. Statt BeginPathfind() (das bei Ablehnung überspringen würde) direkt
        // per pathfindAndMoveCloseTo: lehnt vnavmesh ab, weil ohnehin schon nah genug dran, bleibt es
        // einfach bei State.Interacting - kein Überspringen.
        if (currentTargetEntry != null)
        {
            // Ätherströmungen mit Sprungroute (siehe Plugin.AetherCurrentJumpRoutes) lassen sich vom
            // Kampf-Ort aus meist gar nicht direkt anlaufen (genau deshalb braucht es den Sprung) -
            // dort komplett neu vom Startpunkt aus versuchen statt direkt zur Zielposition zu laufen
            // (Nutzeranforderung: "danach vom Startpunkt es erneut versuchen").
            if (Plugin.TryGetAetherCurrentJumpRoute(currentTargetEntry.Id, out var route))
            {
                Plugin.Log.Info($"[AetherCurrentAutomation] Kampf vorbei - Kampf-Plugin wieder aus, starte Sprungroute neu: {currentTargetEntry.Name}.");
                activeJumpRoute = route;
                jumpRouteStartPoint = route.Start;
                didFinalApproach = false;
                currentTargetPosition = route.Start;
                if (pathfindAndMoveCloseTo.InvokeFunc(route.Start, false, ArrivalTolerance))
                {
                    state = State.MovingTo;
                    stateEnteredAt = DateTime.UtcNow;
                    hasSeenPathRunning = false;
                    stuckDetector.Reset();
                }

                return false;
            }

            Plugin.Log.Info($"[AetherCurrentAutomation] Kampf vorbei - Kampf-Plugin wieder aus, laufe zurück zu: {currentTargetEntry.Name}.");
            didFinalApproach = false;
            Plugin.TryDismount();
            hasIntentionallyDismounted = true;
            if (pathfindAndMoveCloseTo.InvokeFunc(currentTargetPosition, false, ArrivalTolerance))
            {
                state = State.MovingTo;
                stateEnteredAt = DateTime.UtcNow;
                hasSeenPathRunning = false;
                stuckDetector.Reset();
            }
        }

        return false;
    }

    /// <summary>
    /// Solange gekämpft wird, jeden Frame aufrufen - schaltet das Kampf-Plugin (gedrosselt) wieder in
    /// den Manual-Modus, falls es laut IPC gerade NICHT aktiv ist (siehe HuntingLogAutomation.
    /// EnsureCombatMode - identisches Vorgehen).
    /// </summary>
    private void EnsureCombatMode()
    {
        if (DateTime.UtcNow - lastCombatEnsureAt < CombatEnsureInterval)
            return;

        lastCombatEnsureAt = DateTime.UtcNow;
        if (Plugin.CombatPlugin.IsCombatModeActive())
            return;

        Plugin.CombatPlugin.SetCombatMode(true);
    }

    /// <summary>
    /// Muss jeden Frame (während das Overlay offen ist) mit den aktuell fehlenden Ätherströmungen
    /// DER AKTUELLEN ZONE aufgerufen werden.
    /// </summary>
    public void Update(IReadOnlyList<CollectibleEntry> aetherCurrentsInZone)
    {
        if (!IsActive)
            return;

        if (UpdateDefendingSelf())
            return;

        try
        {
            switch (state)
            {
                case State.Idle:
                    TryStartNext(aetherCurrentsInZone);
                    break;

                case State.Mounting:
                    UpdateMounting();
                    break;

                case State.MovingTo:
                    UpdateMoving(aetherCurrentsInZone);
                    break;

                case State.Interacting:
                    UpdateInteracting(aetherCurrentsInZone);
                    break;

                case State.JumpRoute:
                    UpdateJumpRoute(aetherCurrentsInZone);
                    break;

                case State.ReturningToStart:
                    UpdateReturningToStart();
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler bei der Ätherströmungs-Automation - wird gestoppt.");
            StatusText = Loc.T("Fehler bei vnavmesh - Automation gestoppt.", "Error talking to vnavmesh - automation stopped.");
            Stop();
        }
    }

    private static bool StillNeeded(IReadOnlyList<CollectibleEntry> entries, uint id) => entries.Any(e => e.Id == id);

    private void TryStartNext(IReadOnlyList<CollectibleEntry> entries)
    {
        // HasGoToTarget statt nur WorldPosition.HasValue - Ätherströmungen kommen (anders als
        // Hunting-Log-Monster) meist als reine Kartenkoordinate (VendorMapX/Y) aus einem Community-
        // Export, ohne eigene rohe Weltposition (siehe aethercurrents.json-Kommentar in
        // CollectionData.cs). Plugin.OpenEntryMap in StartMovingTo kommt mit beiden Varianten klar.
        var candidates = entries.Where(e => e.HasGoToTarget && !skippedIds.Contains(e.Id)).ToList();
        if (candidates.Count == 0)
        {
            StatusText = Loc.T(
                "Keine Ätherströmungen mit bekannter Position mehr in dieser Zone.",
                "No aether currents with a known position left in this zone.");
            Stop();
            return;
        }

        // Die räumlich nächstgelegene noch nicht freigeschaltete Ätherströmung zuerst (Nutzeranforderung),
        // statt stur der Zonen-Listenreihenfolge zu folgen - Ätherströmungen kommen (anders als Hunting-
        // Log-Monster) meist nur als Kartenkoordinate (VendorMapX/Y) ohne rohe Weltposition, deshalb wie
        // bei QuestAutomation.TryStartNext über Plugin.ResolveWorldPositionFromMapCoords zurückgerechnet.
        // Einträge ohne auflösbare Position fallen ans Ende, statt die Sortierung abzubrechen.
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var next = candidates
            .OrderBy(e => e.WorldPosition is { } worldPos
                ? Vector3.Distance(playerPos, worldPos)
                : Plugin.ResolveWorldPositionFromMapCoords(e.MapId, e.VendorMapX, e.VendorMapY) is { } mapPos
                    ? Vector3.Distance(playerPos, mapPos)
                    : float.MaxValue)
            .ThenBy(e => e.Name)
            .First();
        StartMovingTo(next);
    }

    private void StartMovingTo(CollectibleEntry entry)
    {
        // VOR dem Versuchszähler prüfen (Nutzer-Report: Automationsstart während eines laufenden
        // vnavmesh-Meshbaus überspringt das Ziel sofort als "zu oft versucht") - siehe
        // ChocobokeepAutomation.StartMovingTo-Kommentar (identisches Problem/dieselbe Lösung).
        if (!navmeshIsReady.InvokeFunc())
        {
            StatusText = Loc.T("Warte auf vnavmesh-Navmesh für diese Zone...", "Waiting for vnavmesh's navmesh for this zone...");
            return;
        }

        var attempts = attemptCounts.GetValueOrDefault(entry.Id, 0) + 1;
        attemptCounts[entry.Id] = attempts;
        if (attempts > MaxAttemptsPerTarget)
        {
            skippedIds.Add(entry.Id);
            StatusText = Loc.T($"Übersprungen (zu oft versucht): {entry.Name}", $"Skipped (too many attempts): {entry.Name}");
            state = State.Idle;
            return;
        }

        // Ist eine exakte WorldPosition hinterlegt (z.B. per Hand nachgetragen wie "Overlooking The
        // Convictory", siehe aethercurrents.json), DIREKT dorthin laufen statt über den Karten-
        // Flaggen-Umweg - dessen Rückumrechnung (Weltposition -> Kartenkoordinate -> Flagge ->
        // FlagToPoint) landete beim ersten Versuch teils an einer anderen, falschen Stelle und traf
        // die echte Position erst beim zweiten (BeginFinalApproach), was wie ein falscher erster
        // Anlauf aussah (Nutzer-Report). Ohne bekannte WorldPosition weiterhin derselbe Trick wie bei
        // GoToAutomation/HuntingLogAutomation: die Karten-Flagge auf die (rohe, aus VendorMapX/Y
        // umgerechnete) Zielposition setzen und vnavmesh nach einem begehbaren Punkt in deren Nähe
        // fragen - Ätherströmungen liegen oft in der Luft/an Klippenkanten, eine reine
        // Koordinatensuche (PointOnFloor) fände dort häufig gar keinen begehbaren Punkt.
        Vector3? floorPoint;
        if (entry.WorldPosition is { } exactPosition)
        {
            floorPoint = exactPosition;
        }
        else
        {
            Plugin.OpenEntryMap(entry, showMapWindow: false);
            floorPoint = queryFlagToPoint.InvokeFunc();
        }

        if (floorPoint == null)
        {
            skippedIds.Add(entry.Id);
            StatusText = Loc.T($"Übersprungen (nicht erreichbar): {entry.Name}", $"Skipped (not reachable): {entry.Name}");
            state = State.Idle;
            return;
        }

        currentTargetEntry = entry;
        currentTargetPosition = floorPoint.Value;
        hasInteractedThisCycle = false;
        interactObjectNotFoundSince = null;
        simulatedActivationStartedAt = null;
        didFinalApproach = false;
        hasIntentionallyDismounted = false;

        // Sprungroute hinterlegt (siehe Plugin.AetherCurrentJumpRoutes)? Dann erst zum Startpunkt der
        // Route laufen (ganz normal, siehe unten) - der Sprung selbst passiert erst nach Ankunft dort
        // (siehe UpdateMoving/State.JumpRoute), die eigentliche Zielposition bleibt unverändert
        // bekannt (entry.WorldPosition).
        if (Plugin.TryGetAetherCurrentJumpRoute(entry.Id, out var jumpRoute))
        {
            activeJumpRoute = jumpRoute;
            jumpRouteStartPoint = jumpRoute.Start;
            currentTargetPosition = jumpRoute.Start;
        }
        else
        {
            activeJumpRoute = null;
            jumpRouteStartPoint = null;
        }

        if (Plugin.TryRequestAetheryteMount())
        {
            state = State.Mounting;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T($"Rufe Mount, dann: {entry.Name}...", $"Summoning mount, then: {entry.Name}...");
            return;
        }

        BeginPathfind();
    }

    /// <param name="forceGround">
    /// Fliegen für diesen Versuch gar nicht erst probieren - für den Steckengeblieben-Retry (siehe
    /// UpdateMoving): steckte der Charakter beim Fliegen fest, ist das oft ein Gebäude, gegen das
    /// vnavmeshs Flug-Beeline läuft (Nutzer-Report). Ein Fußweg findet dort eher den Ausgang; sobald
    /// die verbleibende Strecke wieder groß genug ist, plant FlightPathUpgrade von selbst auf Fliegen um.
    /// </param>
    private void BeginPathfind(bool forceGround = false)
    {
        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var accepted = false;
        var flyingAccepted = false;

        // Fliegend zuerst versuchen, aber nur wenn Plugin.CanFly gerade true ist (Fliegen in dieser
        // Zone bereits freigeschaltet) - vnavmesh nimmt einen Flugauftrag sonst teils trotzdem an,
        // obwohl der Charakter gar nicht abheben kann, und hüpft nur sinnlos am Boden herum statt zu
        // laufen. Ätherströmungen liegen zwar oft erhöht/schwer zu Fuß erreichbar, aber genau die
        // ersten einer Zone müssen ohnehin ohne Fliegen erreichbar sein (Henne-Ei: Fliegen schaltet
        // erst frei, wenn alle Strömungen der Zone eingesammelt sind) - zu Fuß ist also immer ein
        // gültiger Fallback.
        if (!forceGround && mounted && Plugin.CanFly)
            accepted = flyingAccepted = pathfindAndMoveCloseTo.InvokeFunc(currentTargetPosition, true, ArrivalTolerance);

        if (!accepted)
            accepted = pathfindAndMoveCloseTo.InvokeFunc(currentTargetPosition, false, ArrivalTolerance);

        if (!accepted)
        {
            SkipCurrent(Loc.T("vnavmesh lehnt Laufweg ab", "vnavmesh rejected the path"));
            return;
        }

        state = State.MovingTo;
        stateEnteredAt = DateTime.UtcNow;
        hasSeenPathRunning = false;
        stuckDetector.Reset();
        flightUpgrade.OnPathStarted(flyingAccepted);
        StatusText = Loc.T($"Laufe zu: {currentTargetEntry?.Name}...", $"Walking to: {currentTargetEntry?.Name}...");
    }

    private void UpdateMounting()
    {
        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            BeginPathfind();
            return;
        }

        if (DateTime.UtcNow - stateEnteredAt > MountWaitTimeout)
            BeginPathfind();
    }

    /// <summary>
    /// Wie BeginPathfind, aber mit FinalApproachTolerance direkt zur bekannten WorldPosition - siehe
    /// deren Kommentar. Gibt zurück, ob vnavmesh den Laufweg angenommen hat (false z.B. wenn der
    /// Charakter bereits nah genug dran ist, dann direkt weiter zu Interacting statt hier hängen zu bleiben).
    /// Bewusst NIE fliegend: die enge FinalApproachTolerance (0.1y) lässt sich fliegend oft gar nicht
    /// erreichen (der Charakter schwebt nur knapp daneben, ohne je "anzukommen") - dadurch blieb die
    /// Automation nach dem Verteidigen (siehe UpdateDefendingSelf, das währenddessen wieder aufsitzen
    /// lässt) sichtbar auf dem Punkt stehen, ohne je zu interagieren (Nutzer-Report).
    /// </summary>
    private bool BeginFinalApproach(Vector3 target)
    {
        currentTargetPosition = target;

        Plugin.TryDismount();
        hasIntentionallyDismounted = true;
        var accepted = pathfindAndMoveCloseTo.InvokeFunc(target, false, FinalApproachTolerance);

        return accepted;
    }

    /// <summary>Startpunkt einer Sprungroute erreicht - siehe JumpPhase/UpdateJumpRoute.</summary>
    // Enge vnavmesh-Wegpunkt-Toleranz für die Sprungroute (Nutzeranforderung: "genaue Positionen
    // nutzen, ohne große Toleranz") - Absprung-/Landepunkte liegen auf schmalen Vorsprüngen, die
    // normale (großzügigere) vnavmesh-Standardtoleranz würde dort schon viel zu früh "angekommen"
    // meldet. Gleicher Wert wie SightseeingAutomation.PuzzleFinalPreciseTolerance.
    private const float JumpRoutePreciseTolerance = 0.05f;

    private void BeginJumpRoute()
    {
        Plugin.TryDismount();
        hasIntentionallyDismounted = true;
        jumpDismountedAt = null;
        jumpPhase = JumpPhase.Dismounting;
        state = State.JumpRoute;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T($"Springe zur Ätherströmung: {currentTargetEntry?.Name}...", $"Jumping to the aether current: {currentTargetEntry?.Name}...");
    }

    /// <summary>
    /// Abmounten, dann zu Fuß zum Anlaufpunkt der Route, von dort mit Anlauf zum Absprung-/
    /// Landepunkt springen - viel einfacher als SightseeingAutomation.UpdateJumpingPuzzle, da hier nur
    /// EIN Sprung nötig ist, kein Mehrschritt-Parcours. Nach der Landung geht es normal über
    /// BeginFinalApproach zur echten Position weiter (siehe Plugin.AetherCurrentJumpRoutes-Kommentar).
    /// </summary>
    private void UpdateJumpRoute(IReadOnlyList<CollectibleEntry> entries)
    {
        if (currentTargetEntry == null || activeJumpRoute is not { } route)
        {
            state = State.Idle;
            return;
        }

        if (!StillNeeded(entries, currentTargetEntry.Id))
        {
            FinishCurrent();
            return;
        }

        switch (jumpPhase)
        {
            case JumpPhase.Dismounting:
            {
                if (Plugin.Condition[ConditionFlag.Mounted])
                {
                    Plugin.TryDismount();
                    return;
                }

                // Condition[Mounted] wird schon VOR dem Ende der sichtbaren Absteige-Animation false -
                // ein Laufauftrag mitten in dieser Animation greift nicht (identisches Problem/dieselbe
                // Lösung wie DismountSettleDelay in anderen Automationen).
                jumpDismountedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - jumpDismountedAt.Value < JumpDismountSettleDelay)
                    return;

                SetExactPathTolerance(true);
                moveToPath.InvokeAction(new List<Vector3> { route.RunUpPoint }, false);
                jumpPhase = JumpPhase.WalkingToRunUp;
                stateEnteredAt = DateTime.UtcNow;
                return;
            }

            case JumpPhase.WalkingToRunUp:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Anlauf zur Ätherströmung dauert zu lange", "Run-up to the aether current is taking too long"));
                    return;
                }

                moveToPath.InvokeAction(new List<Vector3> { route.JumpTarget }, false);
                Plugin.TryJump();
                jumpPhase = JumpPhase.RunningToJump;
                stateEnteredAt = DateTime.UtcNow;
                return;
            }

            case JumpPhase.RunningToJump:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Sprung zur Ätherströmung dauert zu lange", "Jump to the aether current is taking too long"));
                    return;
                }

                // Gelandet - Route abgeschlossen, wieder normale Toleranz, normal zur echten Position
                // weiter (BeginFinalApproach übernimmt auch das Abmounten erneut, schadet aber nicht,
                // falls schon unten).
                RestorePathTolerance();
                activeJumpRoute = null;
                didFinalApproach = true;
                if (currentTargetEntry.WorldPosition is { } exactPosition && BeginFinalApproach(exactPosition))
                {
                    state = State.MovingTo;
                    hasSeenPathRunning = false;
                    stateEnteredAt = DateTime.UtcNow;
                    stuckDetector.Reset();
                    StatusText = Loc.T(
                        $"Laufe genau auf den Punkt: {currentTargetEntry.Name}...",
                        $"Walking precisely onto the point: {currentTargetEntry.Name}...");
                    return;
                }

                state = State.Interacting;
                stateEnteredAt = DateTime.UtcNow;
                StatusText = Loc.T($"Interagiere: {currentTargetEntry.Name}...", $"Interacting: {currentTargetEntry.Name}...");
                return;
            }
        }
    }

    // Für die Sprungroute: enge vnavmesh-Wegpunkt-Toleranz, sonst wieder die ursprüngliche - gleiches
    // Prinzip wie SightseeingAutomation.SetExactPathTolerance/RestorePathTolerance.
    private void SetExactPathTolerance(bool exact)
    {
        if (!exact)
        {
            RestorePathTolerance();
            return;
        }

        try
        {
            savedPathTolerance ??= pathGetTolerance.InvokeFunc();
            pathSetTolerance.InvokeAction(JumpRoutePreciseTolerance);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[AetherCurrentAutomation] vnavmesh-Toleranz konnte nicht gesetzt werden.");
        }
    }

    private void RestorePathTolerance()
    {
        if (savedPathTolerance is not { } tolerance)
            return;

        savedPathTolerance = null;
        try
        {
            pathSetTolerance.InvokeAction(tolerance);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[AetherCurrentAutomation] vnavmesh-Toleranz konnte nicht zurückgesetzt werden.");
        }
    }

    private void UpdateMoving(IReadOnlyList<CollectibleEntry> entries)
    {
        if (currentTargetEntry == null)
        {
            state = State.Idle;
            return;
        }

        if (!StillNeeded(entries, currentTargetEntry.Id))
        {
            FinishCurrent();
            return;
        }

        if (pathIsRunning.InvokeFunc())
        {
            hasSeenPathRunning = true;

            var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? currentTargetPosition;
            if (Vector3.Distance(playerPos, currentTargetPosition) > SprintDisableDistance)
                Plugin.TryUseSprint();

            // Falls unterwegs durch Schwimmen zwangsweise abgestiegen wurde - sobald wieder Land
            // erreicht ist, erneut aufsitzen. NICHT, nachdem wir selbst absichtlich für den Final
            // Approach abgestiegen sind (siehe hasIntentionallyDismounted).
            if (!hasIntentionallyDismounted)
                Plugin.TryRemountAfterForcedDismount(ref lastRemountAttempt);

            // Aus einem Flugverbots-Bereich heraus (siehe FlightPathUpgrade) - jetzt fliegend weiter.
            if (flightUpgrade.ShouldReplanFlying(playerPos, currentTargetPosition))
            {
                StopPath();
                BeginPathfind();
                return;
            }

            // Steckengeblieben (z.B. gegen eine Wand) - Pfad neu anfordern statt untätig zu warten.
            // War der festgesteckte Weg fliegend, steckt meist ein Gebäude im Weg (vnavmeshs Flug-
            // Beeline findet dessen Ausgang nicht) - dann diesmal zu Fuß probieren (siehe
            // BeginPathfind-Kommentar).
            if (stuckDetector.CheckStuck(playerPos))
            {
                var wasFlying = flightUpgrade.IsFlying;
                Plugin.Log.Info($"[AetherCurrentAutomation] UpdateMoving({currentTargetEntry.Name}): scheinbar steckengeblieben{(wasFlying ? " (beim Fliegen, evtl. Gebäude im Weg)" : "")} - Laufweg wird neu angefordert.");
                StopPath();
                BeginPathfind(forceGround: wasFlying);
                return;
            }

            if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                SkipCurrent(Loc.T("Laufweg dauert zu lange", "Path is taking too long"));

            return;
        }

        if (hasSeenPathRunning)
        {
            // Gerade am Startpunkt einer Sprungroute angekommen (siehe StartMovingTo/
            // Plugin.AetherCurrentJumpRoutes) - jetzt abmounten+springen, statt normal weiterzumachen.
            if (activeJumpRoute != null)
            {
                BeginJumpRoute();
                return;
            }

            // Zweiter, engerer Laufauftrag direkt zur bekannten WorldPosition, falls hinterlegt (siehe
            // FinalApproachTolerance-Kommentar) - nur einmal pro Ziel, danach normal weiter zu Interacting.
            if (!didFinalApproach)
            {
                didFinalApproach = true;
                if (currentTargetEntry.WorldPosition is { } exactPosition && BeginFinalApproach(exactPosition))
                {
                    hasSeenPathRunning = false;
                    stateEnteredAt = DateTime.UtcNow;
                    stuckDetector.Reset();
                    StatusText = Loc.T(
                        $"Laufe genau auf den Punkt: {currentTargetEntry.Name}...",
                        $"Walking precisely onto the point: {currentTargetEntry.Name}...");
                    return;
                }

                // Keine WorldPosition hinterlegt, oder vnavmesh lehnt ab (z.B. schon nah genug dran) -
                // direkt weiter wie bisher.
            }

            state = State.Interacting;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T($"Interagiere: {currentTargetEntry.Name}...", $"Interacting: {currentTargetEntry.Name}...");
            return;
        }

        if (Plugin.HasPathStartGraceElapsed(stateEnteredAt, PathStartGracePeriod))
            SkipCurrent(Loc.T("Laufweg nie gestartet", "Movement never started"));
    }

    /// <summary>
    /// Sucht das nächstgelegene ObjectKind.EventObj bei der Zielposition und interagiert damit -
    /// analog zu AetheryteAutomation.FindNearestAetheryteObject/UpdateInteracting, nur mit EventObj
    /// statt der eigenen Aetheryte-Objektart (Ätherströmungen haben keine dedizierte ObjectKind).
    /// </summary>
    private void UpdateInteracting(IReadOnlyList<CollectibleEntry> entries)
    {
        if (currentTargetEntry == null)
        {
            state = State.Idle;
            return;
        }

        if (!StillNeeded(entries, currentTargetEntry.Id))
        {
            FinishCurrent();
            return;
        }

        // Im Simulations-Modus (siehe Configuration.SimulateAetherCurrentAutomation) bleiben bereits
        // freigeschaltete Ätherströmungen absichtlich trotzdem Ziel (siehe CompactOverlayWindow), um
        // Laufweg/Interaktion erneut zu testen - das echte Einsammel-Objekt existiert für sie im
        // Spiel aber oft gar nicht mehr (schon entdeckt), und IsAetherCurrentUnlocked ist ohnehin
        // schon true, OHNE dass überhaupt interagiert wurde. Statt endlos auf ein nicht (mehr)
        // vorhandenes Objekt zu warten: kurze künstliche Pause simulieren, dann normal weiter wie bei
        // einer echten Freischaltung (Nutzeranforderung: "nur simulieren dass er ihn aktiviert").
        if (Plugin.SimulateAetherCurrentAutomation && Plugin.IsAetherCurrentUnlocked(currentTargetEntry.Id))
        {
            simulatedActivationStartedAt ??= DateTime.UtcNow;
            if (DateTime.UtcNow - simulatedActivationStartedAt.Value < SimulatedActivationPause)
            {
                StatusText = Loc.T(
                    $"Simuliere Aktivierung: {currentTargetEntry.Name}...",
                    $"Simulating activation: {currentTargetEntry.Name}...");
                return;
            }

            Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): bereits freigeschaltet, Simulation beendet.");
            if (jumpRouteStartPoint is { } simulatedStartPoint)
            {
                BeginReturnToStart(simulatedStartPoint);
                return;
            }

            FinishCurrent();
            return;
        }

        var gameObject = FindNearestEventObj(currentTargetPosition, InteractObjectSearchRadius);
        if (gameObject == null)
        {
            interactObjectNotFoundSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - interactObjectNotFoundSince.Value < InteractObjectGracePeriod)
                return;

            SkipCurrent(Loc.T("Kein interagierbares Objekt in der Nähe gefunden", "No interactable object found nearby"));
            return;
        }

        if (!hasInteractedThisCycle)
        {
            // Interact braucht das Objekt als aktuelles Ziel - das muss erst einen Frame lang
            // angewendet worden sein, bevor der eigentliche Interact-Aufruf greift (siehe
            // AetheryteAutomation.UpdateInteracting).
            if (!Plugin.IsCurrentTarget(gameObject))
            {
                Plugin.SetTarget(gameObject);
                return;
            }

            Plugin.InteractWithGameObject(gameObject);
            hasInteractedThisCycle = true;
            stateEnteredAt = DateTime.UtcNow;
            Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): interagiert mit BaseId={gameObject.BaseId} @ {gameObject.Position}, warte auf Freischaltung...");
            return;
        }

        if (Plugin.IsAetherCurrentUnlocked(currentTargetEntry.Id))
        {
            Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): freigeschaltet.");

            if (jumpRouteStartPoint is { } startPoint)
            {
                BeginReturnToStart(startPoint);
                return;
            }

            FinishCurrent();
            return;
        }

        if (DateTime.UtcNow - stateEnteredAt > UnlockWaitTimeout)
            SkipCurrent(Loc.T("Freischalten hat nicht geklappt", "unlocking did not go through"));
    }

    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestEventObj(Vector3 nearPosition, float maxDistance)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? nearest = null;
        var bestDistance = maxDistance;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind != ObjectKind.EventObj)
                continue;

            var distance = Vector3.Distance(obj.Position, nearPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = obj;
            }
        }

        return nearest;
    }

    private void FinishCurrent()
    {
        Plugin.Log.Info($"[AetherCurrentAutomation] FinishCurrent({currentTargetEntry?.Name}): freigeschaltet.");
        attemptCounts.Remove(currentTargetEntry!.Id);
        currentTargetEntry = null;
        jumpRouteStartPoint = null;
        state = State.Idle;
    }

    /// <summary>
    /// Siehe jumpRouteStartPoint-Kommentar: nach erfolgreicher Freischaltung einer Ätherströmung mit
    /// Sprungroute erst wieder zum Startpunkt zurücklaufen, statt direkt (von der oft schmalen
    /// Landestelle aus) zum nächsten Ziel weiterzumachen.
    /// </summary>
    private void BeginReturnToStart(Vector3 startPoint)
    {
        SetExactPathTolerance(false);
        if (!pathfindAndMoveCloseTo.InvokeFunc(startPoint, false, ArrivalTolerance))
        {
            // vnavmesh lehnt ab (z.B. schon am Startpunkt) - kein Problem, einfach direkt fertig.
            FinishCurrent();
            return;
        }

        state = State.ReturningToStart;
        stateEnteredAt = DateTime.UtcNow;
        hasSeenPathRunning = false;
        stuckDetector.Reset();
        StatusText = Loc.T(
            $"Zurück zum Startpunkt nach: {currentTargetEntry?.Name}...",
            $"Returning to the start point after: {currentTargetEntry?.Name}...");
    }

    private void UpdateReturningToStart()
    {
        if (pathIsRunning.InvokeFunc())
        {
            if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
            {
                Plugin.Log.Warning($"[AetherCurrentAutomation] UpdateReturningToStart({currentTargetEntry?.Name}): Rückweg dauert zu lange, breche trotzdem ab und mache weiter.");
                StopPath();
                FinishCurrent();
            }

            return;
        }

        FinishCurrent();
    }

    private void SkipCurrent(string reason)
    {
        Plugin.Log.Info($"[AetherCurrentAutomation] SkipCurrent({currentTargetEntry?.Name}): {reason}");
        if (currentTargetEntry != null)
        {
            skippedIds.Add(currentTargetEntry.Id);
            StatusText = $"{Loc.T("Übersprungen", "Skipped")} ({reason}): {currentTargetEntry.Name}";
        }

        StopPath();
        currentTargetEntry = null;
        state = State.Idle;
    }
}
