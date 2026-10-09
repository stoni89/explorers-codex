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
        ManualRoute,
        GateRoute,
        ReturningToStart,
        TeleportingHomeAfterCompletion,
        TeleportingToStart,
    }

    // Siehe Plugin.AetherCurrentGateRoute-Kommentar - State.MovingTo läuft zuerst ganz normal zum Tor
    // (Route.GatePosition), erst DANACH übernimmt dieser Zustand: interagieren, Ja/Nein bestätigen,
    // Ladeanimation abwarten, dann normal weiter (Mount anfordern, BeginPathfind zum echten Ziel).
    private enum GatePhase
    {
        Approaching,
        Interacting,
        WaitingForLoadingScreen,
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
        WalkingToPostJumpWaypoint,
    }

    // Wie JumpPhase, aber für Plugin.AetherCurrentManualRoute (z.B. "The Ruby Sea #1" - schwimmen/
    // auftauchen/laufen bis zu einer Stelle, dort aufmounten, dann reiten) - State.MovingTo läuft
    // zuerst ganz normal zum Startpunkt (Route.Start), erst DANACH übernimmt State.ManualRoute: zu
    // Fuß/schwimmend durch WalkWaypoints, aufmounten, dann geritten durch RideWaypoints (letzter Punkt
    // = entry.WorldPosition), danach normal weiter zur Interaktion.
    private enum ManualRoutePhase
    {
        // Nur, wenn Plugin.AetherCurrentManualRoute.CrossingWaypoints gesetzt ist (z.B. "The Ruby Sea
        // #1": beritten ein Stück in einen Tunnel hineinreiten, der eine Ladeanimation/
        // Unterbereichswechsel auslöst) - GANZ VOR allem anderen, noch vor Start.
        CrossingWaypoints,
        WaitingForCrossingLoadingScreen,
        // Nur, wenn Plugin.AetherCurrentManualRoute.MountedApproachTarget gesetzt ist (z.B. "The Ruby
        // Sea #1": ab Start UNBERITTEN zur ehemaligen Startposition abtauchen) - VOR WalkingWaypoints.
        RidingToMountedApproach,
        WalkingWaypoints,
        // Nur, wenn Plugin.AetherCurrentManualRoute.WalkWaypointsFlying gesetzt ist - nach einem
        // Tauchgang erst das Auftauchen abwarten (Condition[Diving]==false), bevor es normal weitergeht.
        WaitingToSurface,
        Mounting,
        RidingWaypoints,
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
    // UnlockWaitTimeout). Gilt nur noch für den LETZTEN Versuch (siehe InteractRetryWaitTimeout für
    // die Versuche davor, Nutzeranforderung: "Abstand zwischen den interacting Versuchen verringern").
    private static readonly TimeSpan UnlockWaitTimeout = TimeSpan.FromSeconds(15);

    // Kürzerer Abstand zwischen den einzelnen Interact-Wiederholversuchen (Nutzeranforderung: "alle 2
    // Sekunden") - ein fehlgeschlagener Interact (z.B. durch eine noch laufende Abmount-Animation)
    // zeigt sich praktisch sofort, ein voller UnlockWaitTimeout-Wartezyklus pro Versuch war unnötig
    // langsam. Nur der allerletzte Versuch (siehe InteractMaxAttempts) wartet weiterhin die volle
    // UnlockWaitTimeout, falls die Freischaltung selbst (nicht der Interact) noch etwas Zeit braucht.
    private static readonly TimeSpan InteractRetryWaitTimeout = TimeSpan.FromSeconds(2);

    // Wie AetheryteAutomation.InteractPathSettleDuration - kurz bestätigt abwarten, dass der Laufweg
    // wirklich steht, bevor interagiert wird (siehe UpdateInteracting-Kommentar).
    private static readonly TimeSpan InteractPathSettleDuration = TimeSpan.FromMilliseconds(500);
    private DateTime? interactPathSettleConfirmedSince;

    // Sicherheitsnetz, falls pathIsRunning nie (lange genug) false meldet (siehe UpdateInteracting-
    // Kommentar) - danach trotzdem interagieren, statt endlos zu warten.
    private static readonly TimeSpan InteractPathSettleMaxWait = TimeSpan.FromSeconds(3);

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
    private readonly ICallGateSubscriber<uint, byte, bool> lifestreamTeleport;
    private readonly ICallGateSubscriber<bool> lifestreamIsBusy;
    private readonly ICallGateSubscriber<object> lifestreamAbort;

    // Siehe TryTeleportHomeAfterCompletion-Kommentar - Punkte, nach denen zum Haupt-Ätheryten der
    // Zone teleportiert wird, bevor der nächste Punkt gewählt wird (Nutzeranforderung, z.B. "The Ruby
    // Sea #2"/"#1" - die Landestelle/der Rückweg der Sprung-/Handroute liegt ungünstig für den
    // nächsten Punkt).
    private static readonly HashSet<uint> TeleportHomeAfterCompletionIds = new() { 2818182, 2818187 };

    // Für Ätherströmungen, deren Handroute bekanntermaßen (noch) nicht zuverlässig automatisierbar ist
    // (Nutzeranforderung: "The Ruby Sea #1" - der Tauchgang ab der freien Wasseroberfläche lässt sich
    // über vnavmesh bisher nicht nachbilden, siehe Plugin.AetherCurrentManualRoutes-Kommentar) - werden
    // von TryStartNext NIE automatisch angelaufen (müssen von Hand erledigt werden), bekommen aber
    // trotzdem wie Plugin.QuestAutomation.IsKnownUnsupported ein Schloss-Symbol mit Hinweistext im
    // Overlay statt einfach aus der Liste zu verschwinden.
    private static readonly HashSet<uint> KnownUnsupportedIds = new();

    /// <summary>Siehe KnownUnsupportedIds-Kommentar.</summary>
    public static bool IsKnownUnsupported(uint aetherCurrentId) => KnownUnsupportedIds.Contains(aetherCurrentId);

    private bool postCompletionTeleportHasSeenLoadingScreen;
    private static readonly TimeSpan PostCompletionTeleportTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PostCompletionTeleportSettleDelay = TimeSpan.FromSeconds(2);

    // Nutzer-Report (Azim Steppe #3): von der normalen Ankunftsposition aus kaum sauber erreichbar -
    // erst per Lifestream zu diesem Ätheryten teleportieren, danach ganz normal weiter (siehe
    // StartMovingTo/TryTeleportToStart). Key = Ätherströmungs-Id, Wert = Name des Ziel-Ätheryten.
    private static readonly Dictionary<uint, string> PreStartTeleportAetheryteNames = new()
    {
        [2818212] = "The Dawn Throne", // The Azim Steppe #3
        [2818139] = "The Peering Stones", // The Fringes #3
        [2818154] = "Ala Ghiri", // The Peaks #3
        [2818157] = "Ala Ghiri", // The Peaks #4
        [2818187] = "Tamamizu", // The Ruby Sea #1
    };
    private bool preStartTeleportHasSeenLoadingScreen;
    private float? savedPathTolerance;

    // Wie AetheryteAutomation.postInteractDiagnosticStartedAt - jeden Frame für kurze Zeit NACH dem
    // Interact loggen, was mit Position/Condition-Flags passiert (Nutzer-Report: "interagiert... macht
    // nix" - ohne das lässt sich nicht unterscheiden, ob z.B. noch beritten, ein Cast abbricht, oder
    // der Laufweg doch wieder anläuft und wegschiebt).
    private DateTime? postInteractDiagnosticStartedAt;
    private Vector3? postInteractDiagnosticLastPos;
    private static readonly TimeSpan PostInteractDiagnosticDuration = TimeSpan.FromSeconds(3);

    private State state = State.Idle;
    private CollectibleEntry? currentTargetEntry;
    private Vector3 currentTargetPosition;
    private DateTime stateEnteredAt;
    private bool hasSeenPathRunning;
    private DateTime lastRemountAttempt = DateTime.MinValue;
    private readonly NavigationStuckDetector stuckDetector = new();
    private readonly FlightPathUpgrade flightUpgrade = new(); // siehe Plugin.FlightPathUpgrade (Flugverbots-Bereiche)
    private bool hasInteractedThisCycle;
    private int interactAttemptCount;
    private const int InteractMaxAttempts = 3;
    private DateTime? interactObjectNotFoundSince;

    // Siehe UpdateInteracting-Kommentar - genaues Nachlaufen zum tatsächlich gefundenen Spielobjekt,
    // falls die hinterlegte WorldPosition etwas danebenliegt. hasApproachedInteractObject bleibt true,
    // sobald einmal nah genug dran (kein wiederholtes Nachlaufen nötig); approachingInteractObject nur
    // während der eine Laufauftrag dafür noch läuft.
    private bool hasApproachedInteractObject;
    private bool approachingInteractObject;
    private bool interactObjectDismounting;
    private DateTime? interactObjectDismountedAt;
    // Fallback, falls der Fußweg zum Objekt nicht nah genug herankommt (z.B. Ätherströmung über einem
    // Abgrund/in der Luft, zu Fuß unerreichbar - Nutzer-Report "bleibt wie immer so weit weg stehen"):
    // einmalig fliegend direkt auf die exakte Position versuchen, statt den Fußweg-Stand als "angekommen"
    // zu akzeptieren.
    private bool interactObjectTriedFlyingApproach;
    private bool interactObjectMountingForFlight;
    private const float InteractObjectApproachThreshold = 1f;

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

    // Wie activeJumpRoute, nur für Plugin.AetherCurrentManualRoutes/State.ManualRoute.
    private Plugin.AetherCurrentManualRoute? activeManualRoute;
    private ManualRoutePhase manualRoutePhase;
    private int manualRouteWaypointIndex;
    private bool manualRouteCrossingDone;
    private bool manualRouteCrossingHasSeenLoadingScreen;
    private DateTime? manualRouteCrossingLoadingEndedAt;

    // Wie activeJumpRoute, nur für Plugin.AetherCurrentGateRoutes/State.GateRoute.
    private Plugin.AetherCurrentGateRoute? activeGateRoute;
    private GatePhase gatePhase;
    private DateTime? gateDismountedAt;
    private int gateInteractAttempts;
    private DateTime gateInteractedAt;
    private bool gateHasSeenLoadingScreen;
    private DateTime? gateLoadingEndedAt;
    private const int GateMaxInteractAttempts = 3;
    private const float GateObjectSearchRadius = 8f;
    private static readonly TimeSpan GateDismountSettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GateInteractRetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GateLoadingTimeout = TimeSpan.FromSeconds(30);

    // Bleibt (anders als activeJumpRoute, das schon nach der Landung wieder null wird) für die
    // GESAMTE Dauer des aktuellen Ziels gesetzt, sobald eine Sprungroute hinterlegt ist - nach
    // erfolgreicher Freischaltung läuft State.ReturningToStart damit noch einmal diesen Weg zurück
    // (normalerweise nur route.Start, ODER route.ReturnPath, falls abweichend/mehrstufig hinterlegt -
    // siehe Plugin.AetherCurrentJumpRoute-Kommentar), BEVOR es zum nächsten Ziel weitergeht (Nutzer-
    // anforderung: "nachdem der Aether Current aktiviert wurde erstmal wieder zurück"; die
    // Landestelle selbst ist oft ein schmaler Vorsprung, von dem aus man nicht sinnvoll
    // weiterlaufen/-fliegen kann).
    private List<Vector3>? jumpRouteReturnPath;

    // Siehe BeginReturnToStart/UpdateReturningToStart - Index in jumpRouteReturnPath, welcher
    // Wegpunkt aktuell angelaufen wird. Das eigentliche Ziel DIESES Teilstücks wird am tatsächlichen
    // Abstand statt blind an "pathIsRunning == false" erkannt, damit ein unterbrochener Weg (Mount-
    // Wechsel, Kampf-Treffer) nötigenfalls neu angestoßen werden kann.
    private int returnWaypointIndex;

    // Kurze Pause NACH der Ankunft am Startpunkt, bevor es zum nächsten Ziel weitergeht
    // (Nutzeranforderung: "erst zurück an die Startposition, dann kurz warten und dann den nächsten
    // Aether Current machen").
    private static readonly TimeSpan PostReturnToStartSettleDelay = TimeSpan.FromSeconds(2);
    private DateTime? returnToStartArrivedAt;

    // Siehe TryStartNext-Kommentar: die ID der GERADE erst abgeschlossenen Ätherströmung - steht man
    // nach dem Rückweg direkt daneben, wäre sie (im Simulations-Modus, wo bereits freigeschaltete
    // absichtlich Ziel bleiben) über die "nächstgelegene zuerst"-Sortierung sonst fast immer wieder
    // die erste Wahl (Nutzer-Report: "hat wieder den gleichen Current versucht anstatt zum nächsten
    // zu laufen").
    private uint? lastFinishedId;

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
            Plugin.Log.Error(ex, "[AetherCurrentAutomation] Fehler beim Abbrechen von Lifestream.");
        }
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

    /// <summary>Siehe AetheryteAutomation.RestrictedToToDo-Kommentar.</summary>
    public bool RestrictedToToDo { get; set; }

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
        jumpRouteReturnPath = null;
        returnWaypointIndex = 0;
        returnToStartArrivedAt = null;
        activeManualRoute = null;
        manualRouteWaypointIndex = 0;
        activeGateRoute = null;
        lastFinishedId = null;
        StatusText = Loc.T("Automation gestartet...", "Automation started...");
    }

    public void Stop()
    {
        IsActive = false;
        state = State.Idle;
        currentTargetEntry = null;
        StopPath();
        StopLifestream();
        Plugin.ClearNavigationTarget();

        // Mitten in einer Sprung-/Handroute gestoppt - enge vnavmesh-Toleranz nicht dauerhaft gesetzt lassen.
        RestorePathTolerance();
        activeJumpRoute = null;
        jumpRouteReturnPath = null;
        returnWaypointIndex = 0;
        returnToStartArrivedAt = null;
        activeManualRoute = null;
        manualRouteWaypointIndex = 0;
        activeGateRoute = null;
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
            // Ist das Ziel schon freigeschaltet (z.B. Angriff auf dem Rückweg zum Startpunkt, siehe
            // State.ReturningToStart/BeginReturnToStart) - NICHT die Sprungroute von vorne versuchen,
            // einfach den Rückweg fortsetzen (Nutzer-Report: "will den Aether Current erneut
            // versuchen", obwohl er schon aktiviert war).
            if (jumpRouteReturnPath != null && Plugin.IsAetherCurrentUnlocked(currentTargetEntry.Id))
            {
                Plugin.Log.Info($"[AetherCurrentAutomation] Kampf vorbei - bereits freigeschaltet, setze Rückweg zum Startpunkt fort: {currentTargetEntry.Name}.");
                ResumeReturnToStart();
                return false;
            }

            // Ätherströmungen mit Sprungroute (siehe Plugin.AetherCurrentJumpRoutes) lassen sich vom
            // Kampf-Ort aus meist gar nicht direkt anlaufen (genau deshalb braucht es den Sprung) -
            // dort komplett neu vom Startpunkt aus versuchen statt direkt zur Zielposition zu laufen
            // (Nutzeranforderung: "danach vom Startpunkt es erneut versuchen").
            if (Plugin.TryGetAetherCurrentJumpRoute(currentTargetEntry.Id, out var route))
            {
                Plugin.Log.Info($"[AetherCurrentAutomation] Kampf vorbei - Kampf-Plugin wieder aus, starte Sprungroute neu: {currentTargetEntry.Name}.");
                activeJumpRoute = route;
                // Siehe StartMovingTo-Kommentar zu TeleportHomeAfterCompletionIds - derselbe Sonderfall
                // gilt auch beim Neustart der Sprungroute nach einem Kampf.
                jumpRouteReturnPath = route.ReturnPath != null
                    ? route.ReturnPath.ToList()
                    : TeleportHomeAfterCompletionIds.Contains(currentTargetEntry.Id) ? null : new List<Vector3> { route.Start };
                returnWaypointIndex = 0;
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

                case State.ManualRoute:
                    UpdateManualRoute();
                    break;

                case State.GateRoute:
                    UpdateGateRoute();
                    break;

                case State.ReturningToStart:
                    UpdateReturningToStart();
                    break;

                case State.TeleportingHomeAfterCompletion:
                    UpdateTeleportingHomeAfterCompletion();
                    break;

                case State.TeleportingToStart:
                    UpdateTeleportingToStart();
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
        var candidates = entries.Where(e => e.HasGoToTarget && !skippedIds.Contains(e.Id) && !IsKnownUnsupported(e.Id)).ToList();

        // Siehe lastFinishedId-Kommentar - nur ausschließen, wenn tatsächlich noch etwas ANDERES zur
        // Auswahl steht, sonst (einzige verbleibende Ätherströmung, z.B. Simulation mit nur einem
        // Eintrag) ganz normal wieder dieselbe nehmen dürfen.
        if (lastFinishedId is { } finishedId && candidates.Any(e => e.Id != finishedId))
            candidates = candidates.Where(e => e.Id != finishedId).ToList();

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
        interactAttemptCount = 0;
        hasApproachedInteractObject = false;
        approachingInteractObject = false;
        interactObjectDismounting = false;
        interactObjectDismountedAt = null;
        interactObjectTriedFlyingApproach = false;
        interactObjectMountingForFlight = false;
        interactObjectNotFoundSince = null;
        interactPathSettleConfirmedSince = null;
        postInteractDiagnosticStartedAt = null;
        postInteractDiagnosticLastPos = null;
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
            // Ohne eigenen ReturnPath normalerweise zurück zum Startpunkt (Default unten) - AUSSER der
            // Punkt ist in TeleportHomeAfterCompletionIds gelistet, dann soll nach der Freischaltung
            // direkt zum Haupt-Ätheryten teleportiert werden (siehe FinishCurrent/
            // TryTeleportHomeAfterCompletion), kein Rückweg zum Startpunkt (Nutzer-Report: lief sonst
            // immer zum Startpunkt zurück statt zu teleportieren).
            jumpRouteReturnPath = jumpRoute.ReturnPath != null
                ? jumpRoute.ReturnPath.ToList()
                : TeleportHomeAfterCompletionIds.Contains(entry.Id) ? null : new List<Vector3> { jumpRoute.Start };
            returnWaypointIndex = 0;
            currentTargetPosition = jumpRoute.Start;
            activeManualRoute = null;
            activeGateRoute = null;
        }
        // Handroute hinterlegt (siehe Plugin.AetherCurrentManualRoutes)? Dann wie bei der Sprungroute
        // erst zum Startpunkt laufen - der eigentliche Lauf-/Reitweg passiert erst nach Ankunft dort
        // (siehe UpdateMoving/State.ManualRoute).
        else if (Plugin.TryGetAetherCurrentManualRoute(entry.Id, out var manualRoute))
        {
            activeJumpRoute = null;
            jumpRouteReturnPath = null;
            activeManualRoute = manualRoute;
            activeGateRoute = null;
            manualRouteCrossingDone = false;
            manualRouteCrossingHasSeenLoadingScreen = false;
            manualRouteCrossingLoadingEndedAt = null;
            currentTargetPosition = manualRoute.CrossingWaypoints is { Length: > 0 } crossingWaypoints
                ? crossingWaypoints[0]
                : manualRoute.Start;
        }
        // Tor-Route hinterlegt (siehe Plugin.AetherCurrentGateRoutes)? Dann erst zum Tor laufen (ganz
        // normal, siehe unten) - das Interagieren/Bestätigen/Warten passiert erst nach Ankunft dort
        // (siehe UpdateMoving/State.GateRoute).
        else if (Plugin.TryGetAetherCurrentGateRoute(entry.Id, out var gateRoute))
        {
            activeJumpRoute = null;
            jumpRouteReturnPath = null;
            activeManualRoute = null;
            activeGateRoute = gateRoute;
            currentTargetPosition = gateRoute.GatePosition;
        }
        else
        {
            activeJumpRoute = null;
            jumpRouteReturnPath = null;
            activeManualRoute = null;
            activeGateRoute = null;
        }

        // Manche Ätherströmungen sind von der normalen Ankunftsposition aus zu Fuß/fliegend kaum
        // sauber erreichbar (Nutzer-Report, z.B. Azim Steppe #3) - dort erst per Lifestream zu einem
        // konkreten, bekannten Ätheryten teleportieren, DANACH ganz normal weiter (Mount anfordern,
        // BeginPathfind) wie unten.
        var hasPreStartTeleport = PreStartTeleportAetheryteNames.TryGetValue(entry.Id, out var teleportAetheryteName);
        Plugin.Log.Info($"[AetherCurrentAutomation] StartMovingTo({entry.Name}, Id={entry.Id}): PreStartTeleport hinterlegt={hasPreStartTeleport}{(hasPreStartTeleport ? $" ({teleportAetheryteName})" : "")}.");
        if (hasPreStartTeleport && TryTeleportToStart(teleportAetheryteName!, entry))
            return;

        BeginMountAndPathfind(entry);
    }

    /// <summary>Siehe PreStartTeleportAetheryteNames-Kommentar - Mount anfordern (falls möglich), dann BeginPathfind, identisch zum bisherigen Ende von StartMovingTo.</summary>
    private void BeginMountAndPathfind(CollectibleEntry entry)
    {
        if (Plugin.TryRequestAetheryteMount())
        {
            state = State.Mounting;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T($"Rufe Mount, dann: {entry.Name}...", $"Summoning mount, then: {entry.Name}...");
            return;
        }

        BeginPathfind();
    }

    /// <summary>
    /// Siehe PreStartTeleportAetheryteNames-Kommentar. Klappt der Teleport nicht (Lifestream fehlt/
    /// Ätheryte-Name nicht auflösbar/noch nicht freigeschaltet), einfach normal weiter wie bisher -
    /// der nachfolgende Lauf-/Flugweg bleibt dann der bisherige, ungünstigere Startpunkt, aber die
    /// Automation bleibt nicht hängen.
    /// </summary>
    private bool TryTeleportToStart(string aetheryteName, CollectibleEntry entry)
    {
        if (!IsLifestreamAvailable())
        {
            Plugin.Log.Info($"[AetherCurrentAutomation] TryTeleportToStart({entry.Name}): Lifestream-IPC nicht verfügbar - teleportiere nicht.");
            return false;
        }

        var aetheryteId = Plugin.ResolveAetheryteIdByName(aetheryteName);
        if (aetheryteId == null)
        {
            Plugin.Log.Warning($"[AetherCurrentAutomation] TryTeleportToStart({entry.Name}): Ätheryte \"{aetheryteName}\" nicht im Aetheryte-Sheet gefunden (Name/Sprache falsch?) - teleportiere nicht.");
            return false;
        }

        if (!lifestreamTeleport.InvokeFunc(aetheryteId.Value, (byte)0))
        {
            Plugin.Log.Warning($"[AetherCurrentAutomation] TryTeleportToStart({entry.Name}): Lifestream.Teleport zu \"{aetheryteName}\" (Id={aetheryteId.Value}) abgelehnt (noch nicht freigeschaltet?) - teleportiere nicht.");
            return false;
        }

        preStartTeleportHasSeenLoadingScreen = false;
        state = State.TeleportingToStart;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T($"Teleportiere zu {aetheryteName}, dann: {entry.Name}...", $"Teleporting to {aetheryteName}, then: {entry.Name}...");
        return true;
    }

    /// <summary>Wie UpdateTeleportingHomeAfterCompletion, aber führt danach BeginMountAndPathfind statt FinishCurrentImmediate aus.</summary>
    private void UpdateTeleportingToStart()
    {
        if (currentTargetEntry == null)
        {
            state = State.Idle;
            return;
        }

        var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (loading)
            preStartTeleportHasSeenLoadingScreen = true;

        if (loading || !preStartTeleportHasSeenLoadingScreen)
        {
            if (DateTime.UtcNow - stateEnteredAt > PostCompletionTeleportTimeout)
                BeginMountAndPathfind(currentTargetEntry);

            return;
        }

        if (DateTime.UtcNow - stateEnteredAt < PostCompletionTeleportSettleDelay)
            return;

        BeginMountAndPathfind(currentTargetEntry);
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

        // Ist eine exakte WorldPosition hinterlegt, den ZU-FUSS-Laufauftrag von Anfang an mit der engen
        // FinalApproachTolerance stellen statt der großzügigen ArrivalTolerance (Nutzer-Report: "immer
        // viel zu weit weg zum interagieren") - der bisherige zweite, enge Laufauftrag danach (siehe
        // UpdateMoving/BeginFinalApproach) lehnte vnavmesh in dem Fall teils ab, mit dersselben
        // Mehrdeutigkeit wie "schon nah genug dran" ("false" bedeutet beides) - dann blieb der erste,
        // grobe Anlauf als tatsächliches Ergebnis stehen. Fliegend bewusst weiterhin die großzügigere
        // ArrivalTolerance (wie gehabt) - die enge Toleranz lässt sich fliegend oft gar nicht erreichen
        // (siehe BeginFinalApproach-Kommentar "NIE fliegend"), der anschließende Final-Approach-
        // Laufauftrag zu Fuß übernimmt die letzten Meter dann wie bisher. Ohne bekannte WorldPosition
        // (reine Kartenkoordinate) bleibt es überall bei der großzügigeren ArrivalTolerance, da deren
        // Zielpunkt selbst schon ungenau ist.
        // Eine aktive Tor-/Sprung-/Handroute läuft immer zuerst zu einem exakten, von Hand erfassten
        // Punkt (route.GatePosition/Start) - auch wenn entry.WorldPosition selbst noch unbekannt ist
        // (z.B. "The Peaks #4": das echte Ziel liegt erst hinter dem Tor und wird erst danach
        // aufgelöst), sonst bliebe der Charakter mit der groben ArrivalTolerance zu weit vom Tor
        // entfernt stehen (Nutzer-Report: "Gate Position bitte exakt sonst ist er zu weit weg").
        var hasExactPosition = currentTargetEntry?.WorldPosition != null
            || activeJumpRoute != null || activeManualRoute != null || activeGateRoute != null;
        var groundTolerance = hasExactPosition ? FinalApproachTolerance : ArrivalTolerance;

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
            accepted = pathfindAndMoveCloseTo.InvokeFunc(currentTargetPosition, false, groundTolerance);

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

        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? target;
        var distance = Vector3.Distance(playerPos, target);
        var accepted = pathfindAndMoveCloseTo.InvokeFunc(target, false, FinalApproachTolerance);

        // vnavmeshs "false" ist mehrdeutig (siehe Klassenkommentar: "schon nah genug dran" ODER
        // "abgelehnt, obwohl noch weit weg") - hier selbst nachmessen statt dem Rückgabewert allein zu
        // vertrauen (Nutzer-Report: "bleibt zu weit entfernt"/"interagiert nicht", obwohl eine exakte
        // WorldPosition hinterlegt ist). Nur falls WIRKLICH noch weit weg: erneuter Versuch mit der
        // großzügigeren ArrivalTolerance statt stillschweigend von hier aus zu interagieren.
        if (!accepted && distance > FinalApproachTolerance * 3f)
        {
            Plugin.Log.Warning($"[AetherCurrentAutomation] BeginFinalApproach({currentTargetEntry?.Name}): vnavmesh lehnte den engen Laufweg ab, Entfernung noch {distance:F2}y - versuche erneut mit Standardtoleranz.");
            accepted = pathfindAndMoveCloseTo.InvokeFunc(target, false, ArrivalTolerance);
        }

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

                // Gelandet - falls ein Zwischenpunkt hinterlegt ist (siehe Plugin.AetherCurrentJumpRoute.
                // PostJumpWaypoint-Kommentar), erst dort hinlaufen, bevor es zur echten Position weitergeht.
                if (route.PostJumpWaypoint is { } postJumpWaypoint)
                {
                    moveToPath.InvokeAction(new List<Vector3> { postJumpWaypoint }, false);
                    jumpPhase = JumpPhase.WalkingToPostJumpWaypoint;
                    stateEnteredAt = DateTime.UtcNow;
                    return;
                }

                CompleteJumpRoute();
                return;
            }

            case JumpPhase.WalkingToPostJumpWaypoint:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Zwischenpunkt nach dem Sprung dauert zu lange", "Post-jump waypoint is taking too long"));
                    return;
                }

                CompleteJumpRoute();
                return;
            }
        }
    }

    /// <summary>
    /// Route (inkl. optionalem PostJumpWaypoint) abgeschlossen - wieder normale Toleranz, normal zur
    /// echten Position weiter (BeginFinalApproach übernimmt auch das Abmounten erneut, schadet aber
    /// nicht, falls schon unten).
    /// </summary>
    private void CompleteJumpRoute()
    {
        RestorePathTolerance();
        activeJumpRoute = null;
        didFinalApproach = true;
        if (currentTargetEntry!.WorldPosition is { } exactPosition && BeginFinalApproach(exactPosition))
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
    }

    /// <summary>Startpunkt einer Handroute erreicht (siehe Plugin.AetherCurrentManualRoutes) - erst die
    /// WalkWaypoints zu Fuß/schwimmend ablaufen, siehe UpdateManualRoute.</summary>
    private void BeginManualRoute()
    {
        state = State.ManualRoute;

        // CrossingWaypoints gesetzt und noch nicht abgefahren (z.B. "The Ruby Sea #1": beritten ein
        // Stück in einen Tunnel hineinreiten, der eine Ladeanimation auslöst) - GANZ VOR Start/
        // MountedApproachTarget/WalkWaypoints, siehe UpdateManualRoute.
        if (!manualRouteCrossingDone && activeManualRoute!.Value.CrossingWaypoints is { Length: > 0 } crossingWaypoints)
        {
            manualRoutePhase = ManualRoutePhase.CrossingWaypoints;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T($"Reite in den Tunnel: {currentTargetEntry?.Name}...", $"Riding into the tunnel: {currentTargetEntry?.Name}...");
            // Wegpunkt 0 ist bereits erreicht (das war das vorherige BeginPathfind-Ziel) - weiter zum
            // nächsten, falls vorhanden, sonst direkt die Ladeanimation abwarten.
            if (crossingWaypoints.Length > 1)
            {
                manualRouteWaypointIndex = 1;
                IssueManualRouteWaypointMove(crossingWaypoints[1], flying: false);
            }
            else
            {
                manualRoutePhase = ManualRoutePhase.WaitingForCrossingLoadingScreen;
                manualRouteCrossingHasSeenLoadingScreen = false;
                manualRouteCrossingLoadingEndedAt = null;
            }

            return;
        }

        // MountedApproachTarget gesetzt (z.B. "The Ruby Sea #1": ab Start zur ehemaligen Startposition
        // abtauchen)? Nutzer-Report bestätigt: Aufsitzen klappt an dieser Stelle schwimmend GAR NICHT
        // (weder von Hand noch automatisiert), Abtauchen klappt dagegen von Hand einwandfrei - also
        // UNBERITTEN bleiben/abmounten und direkt dorthin tauchen. fly:true (nicht false), da ein reiner
        // Bodenlaufweg (fly:false) sich an die begehbare Navmesh-Oberfläche hält, die an der freien
        // Wasseroberfläche offenbar nicht tiefer reicht - der direkte 3D-Laufweg (sonst fürs Fliegen
        // gedacht) ist der einzige vnavmesh-Modus, der überhaupt eine Tiefe jenseits der Oberfläche
        // ansteuert, und wird hier testweise auch unberitten versucht.
        if (activeManualRoute!.Value.MountedApproachTarget is { } mountedApproachTarget)
        {
            manualRoutePhase = ManualRoutePhase.RidingToMountedApproach;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T($"Tauche zur ehemaligen Startposition: {currentTargetEntry?.Name}...", $"Diving to the former start position: {currentTargetEntry?.Name}...");
            LogManualRouteState("BeginManualRoute (vor Tauchgang)");
            Plugin.TryDismount();
            var accepted = IssueManualRouteWaypointMove(mountedApproachTarget, flying: true);
            Plugin.Log.Info($"[AetherCurrentAutomation] BeginManualRoute({currentTargetEntry?.Name}): Tauchgang zu {mountedApproachTarget} mit fly:true ausgelöst, angenommen={accepted}.");
            return;
        }

        BeginManualRouteWalkingPhase();
    }

    /// <summary>
    /// Tiefen-Fade am Taucheingang passiert (siehe ManualRoutePhase.CrossingWaypoints) - NICHT über
    /// die normale State.MovingTo/BeginPathfind (die nutzt fly:false für den Bodenlaufauftrag, der in
    /// dieser Tiefe keinen Weg findet - Nutzer-Report "Movement never started"), sondern direkt in die
    /// WalkWaypoints-Phase mit vnavmeshs 3D-Laufauftrag (fly:true, siehe WalkWaypointsFlying-Kommentar),
    /// beginnend bei WalkWaypoints[0]. manualRouteCrossingDone verhindert, dass BeginManualRoute die
    /// Taucheingangs-Fahrt bei einem erneuten Aufruf (z.B. nach erfolgreichem Auftauchen) nochmal von
    /// vorne beginnt.
    /// </summary>
    private void ResumeAfterManualRouteCrossing()
    {
        manualRouteCrossingDone = true;
        LogManualRouteState("ResumeAfterManualRouteCrossing (vor WalkWaypoints)");
        BeginManualRouteWalkingPhase();
    }

    /// <summary>Für die Diagnose des Tauchgangs (Nutzer-Report: "kannst du ein Log einbauen, das ich dir
    /// schicken kann?") - Mounted/Swimming/Diving/Position in einer Zeile.</summary>
    private void LogManualRouteState(string context)
    {
        var pos = Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        Plugin.Log.Info($"[AetherCurrentAutomation] {context}({currentTargetEntry?.Name}): pos={pos}, Mounted={Plugin.Condition[ConditionFlag.Mounted]}, " +
                         $"Swimming={Plugin.Condition[ConditionFlag.Swimming]}, Diving={Plugin.Condition[ConditionFlag.Diving]}, pathIsRunning={pathIsRunning.InvokeFunc()}.");
    }

    private static readonly TimeSpan SurfaceWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Alle Lauf-/Schwimm-/Tauchpunkte erreicht - jetzt für den Reitweg aufmounten (bzw. direkt weiter, falls schon beritten).</summary>
    private void BeginRidingWaypoints(Plugin.AetherCurrentManualRoute route)
    {
        manualRoutePhase = ManualRoutePhase.Mounting;
        stateEnteredAt = DateTime.UtcNow;
        if (!Plugin.TryRequestAetheryteMount())
        {
            // Kein Mount konfiguriert/Zone erlaubt kein Aufsitzen (oder schon beritten) - trotzdem
            // weiter, statt hier stehen zu bleiben (Nutzeranforderung: Automation soll nicht hängen bleiben).
            manualRoutePhase = ManualRoutePhase.RidingWaypoints;
            manualRouteWaypointIndex = 0;
            IssueManualRouteWaypointMove(route.RideWaypoints[0], flying: false);
        }
    }

    private void BeginManualRouteWalkingPhase()
    {
        manualRoutePhase = ManualRoutePhase.WalkingWaypoints;
        manualRouteWaypointIndex = 0;
        stateEnteredAt = DateTime.UtcNow;

        // Nur abmounten, wenn die Route das zwingend braucht (MountedApproachTarget gesetzt, z.B. "The
        // Ruby Sea #1" - dort müssen die WalkWaypoints unberitten geschwommen/getaucht werden). Sonst
        // (Nutzer-Report: "Er soll nicht abmounten zwischen den Punkten, nur dann am Aether Current")
        // beritten bleiben - das Abmounten direkt am Objekt übernimmt ohnehin schon UpdateInteracting.
        if (activeManualRoute!.Value.MountedApproachTarget != null)
        {
            Plugin.TryDismount();
            hasIntentionallyDismounted = true;
        }

        StatusText = Loc.T($"Laufe zur Ätherströmung: {currentTargetEntry?.Name}...", $"Walking to the aether current: {currentTargetEntry?.Name}...");
        IssueManualRouteWaypointMove(activeManualRoute!.Value.WalkWaypoints[0], flying: activeManualRoute!.Value.WalkWaypointsFlying);
    }

    /// <summary>Löst den Laufauftrag zu einem einzelnen Handroute-Wegpunkt aus - immer mit der engen
    /// JumpRoutePreciseTolerance (Nutzeranforderung: "ohne große Toleranz"), siehe UpdateManualRoute.</summary>
    private bool IssueManualRouteWaypointMove(Vector3 waypoint, bool flying)
    {
        SetExactPathTolerance(true);
        var accepted = pathfindAndMoveCloseTo.InvokeFunc(waypoint, flying, JumpRoutePreciseTolerance);
        stuckDetector.Reset();
        return accepted;
    }

    /// <summary>
    /// Läuft die von Hand hinterlegten Wegpunkte einer Plugin.AetherCurrentManualRoute ab: erst zu Fuß/
    /// schwimmend durch WalkWaypoints, dann aufmounten und durch RideWaypoints reiten (letzter Punkt =
    /// entry.WorldPosition) - danach normal weiter zur Interaktion (wie CompleteJumpRoute, aber ohne
    /// erneuten BeginFinalApproach, die Ankunft am letzten RideWaypoint ist bereits exakt genug).
    /// </summary>
    private void UpdateManualRoute()
    {
        if (currentTargetEntry == null || activeManualRoute is not { } route)
        {
            state = State.Idle;
            return;
        }

        switch (manualRoutePhase)
        {
            case ManualRoutePhase.CrossingWaypoints:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Anfahrt zum Tunnel dauert zu lange", "Approach to the tunnel is taking too long"));
                    return;
                }

                var crossingWaypoints = route.CrossingWaypoints!;
                manualRouteWaypointIndex++;
                if (manualRouteWaypointIndex < crossingWaypoints.Length)
                {
                    stateEnteredAt = DateTime.UtcNow;
                    IssueManualRouteWaypointMove(crossingWaypoints[manualRouteWaypointIndex], flying: false);
                    return;
                }

                manualRoutePhase = ManualRoutePhase.WaitingForCrossingLoadingScreen;
                manualRouteCrossingHasSeenLoadingScreen = false;
                manualRouteCrossingLoadingEndedAt = null;
                stateEnteredAt = DateTime.UtcNow;
                StatusText = Loc.T($"Warte auf Ladeanimation: {currentTargetEntry.Name}...", $"Waiting for the loading animation: {currentTargetEntry.Name}...");
                return;
            }

            case ManualRoutePhase.WaitingForCrossingLoadingScreen:
            {
                var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
                if (loading)
                    manualRouteCrossingHasSeenLoadingScreen = true;

                if (loading || !manualRouteCrossingHasSeenLoadingScreen)
                {
                    manualRouteCrossingLoadingEndedAt = null;
                    if (DateTime.UtcNow - stateEnteredAt > GateLoadingTimeout)
                        ResumeAfterManualRouteCrossing();

                    return;
                }

                // Settle-Delay wie bei UpdateGateRoute.WaitingForLoadingScreen - direkt nach Ende der
                // Ladeanimation ist der Charakter noch nicht wieder voll "steuerbar".
                manualRouteCrossingLoadingEndedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - manualRouteCrossingLoadingEndedAt.Value < PostCompletionTeleportSettleDelay)
                    return;

                // vnavmesh braucht nach einem Unterbereichswechsel einen Moment, bis die Navmesh für
                // den neuen Bereich aufgebaut ist - ein Laufauftrag davor wird abgelehnt, was ohne
                // diesen Check zu SkipCurrent("vnavmesh lehnt Laufweg ab") führte und die GESAMTE
                // Route (inkl. erneutem Teleport zu Tamamizu!) von vorne starten ließ (Nutzer-Report:
                // "will er immer wieder zurück zum Aetheryten").
                if (!navmeshIsReady.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > GateLoadingTimeout)
                        ResumeAfterManualRouteCrossing();

                    StatusText = Loc.T("Warte auf vnavmesh-Navmesh für diesen Bereich...", "Waiting for vnavmesh's navmesh for this area...");
                    return;
                }

                ResumeAfterManualRouteCrossing();
                return;
            }

            case ManualRoutePhase.RidingToMountedApproach:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Anfahrt zur Ätherströmung dauert zu lange", "Approach to the aether current is taking too long"));
                    return;
                }

                LogManualRouteState("UpdateManualRoute.RidingToMountedApproach->WalkingWaypoints (arrived)");
                BeginManualRouteWalkingPhase();
                return;
            }

            case ManualRoutePhase.WalkingWaypoints:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Laufweg zur Ätherströmung dauert zu lange", "Path to the aether current is taking too long"));
                    return;
                }

                manualRouteWaypointIndex++;
                if (manualRouteWaypointIndex < route.WalkWaypoints.Length)
                {
                    stateEnteredAt = DateTime.UtcNow;
                    IssueManualRouteWaypointMove(route.WalkWaypoints[manualRouteWaypointIndex], flying: route.WalkWaypointsFlying);
                    return;
                }

                // Nach einem Tauchgang (WalkWaypointsFlying) erst das Auftauchen abwarten (Nutzer-
                // Anforderung "Nach der Animation zum Aether Current Punkt") - ein Bodenlaufauftrag
                // direkt im Tauchgang-Zustand würde sonst wieder nicht starten (siehe
                // ResumeAfterManualRouteCrossing-Kommentar).
                if (route.WalkWaypointsFlying)
                {
                    manualRoutePhase = ManualRoutePhase.WaitingToSurface;
                    manualRouteCrossingHasSeenLoadingScreen = false;
                    manualRouteCrossingLoadingEndedAt = null;
                    stateEnteredAt = DateTime.UtcNow;
                    StatusText = Loc.T($"Warte auf das Auftauchen: {currentTargetEntry.Name}...", $"Waiting to surface: {currentTargetEntry.Name}...");
                    return;
                }

                BeginRidingWaypoints(route);
                return;
            }

            case ManualRoutePhase.WaitingToSurface:
            {
                // Wie ManualRoutePhase.WaitingForCrossingLoadingScreen - das Auftauchen an Punkt 6 löst
                // laut Nutzer-Anforderung ebenfalls eine Ladeanimation aus, nicht nur ein Ende von
                // Condition[Diving].
                var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
                if (loading)
                    manualRouteCrossingHasSeenLoadingScreen = true;

                if (Plugin.Condition[ConditionFlag.Diving] || loading || (!manualRouteCrossingHasSeenLoadingScreen && DateTime.UtcNow - stateEnteredAt < SurfaceWaitTimeout))
                {
                    manualRouteCrossingLoadingEndedAt = null;
                    if (DateTime.UtcNow - stateEnteredAt > GateLoadingTimeout)
                        BeginRidingWaypoints(route);

                    return;
                }

                manualRouteCrossingLoadingEndedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - manualRouteCrossingLoadingEndedAt.Value < PostCompletionTeleportSettleDelay)
                    return;

                if (!navmeshIsReady.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > GateLoadingTimeout)
                        BeginRidingWaypoints(route);

                    StatusText = Loc.T("Warte auf vnavmesh-Navmesh für diesen Bereich...", "Waiting for vnavmesh's navmesh for this area...");
                    return;
                }

                BeginRidingWaypoints(route);
                return;
            }

            case ManualRoutePhase.Mounting:
            {
                if (Plugin.Condition[ConditionFlag.Mounted] || DateTime.UtcNow - stateEnteredAt > MountWaitTimeout)
                {
                    manualRoutePhase = ManualRoutePhase.RidingWaypoints;
                    manualRouteWaypointIndex = 0;
                    stateEnteredAt = DateTime.UtcNow;
                    IssueManualRouteWaypointMove(route.RideWaypoints[0], flying: false);
                }
                return;
            }

            case ManualRoutePhase.RidingWaypoints:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Reitweg zur Ätherströmung dauert zu lange", "Riding path to the aether current is taking too long"));
                    return;
                }

                manualRouteWaypointIndex++;
                if (manualRouteWaypointIndex < route.RideWaypoints.Length)
                {
                    stateEnteredAt = DateTime.UtcNow;
                    IssueManualRouteWaypointMove(route.RideWaypoints[manualRouteWaypointIndex], flying: false);
                    return;
                }

                // Letzter Punkt (= entry.WorldPosition) erreicht - normal weiter zur Interaktion. OHNE
                // dieses Update bliebe currentTargetPosition auf route.Start stehen (Nutzer-Report:
                // "klickt den Aether Current nicht an") - FindNearestEventObj würde dann am weit
                // entfernten Startpunkt statt am echten Ziel nach dem Objekt suchen.
                currentTargetPosition = currentTargetEntry.WorldPosition ?? route.RideWaypoints[^1];
                RestorePathTolerance();
                activeManualRoute = null;
                didFinalApproach = true;
                state = State.Interacting;
                stateEnteredAt = DateTime.UtcNow;
                StatusText = Loc.T($"Interagiere: {currentTargetEntry.Name}...", $"Interacting: {currentTargetEntry.Name}...");
                return;
            }
        }
    }

    /// <summary>
    /// Grobe Ankunft in der Nähe des Tors (siehe Plugin.AetherCurrentGateRoutes) - erst noch GENAU zum
    /// Tor laufen (BeginFinalApproach, wie bei Sprung-/Handrouten üblich), denn ein fliegend
    /// akzeptierter erster Laufauftrag landet nur innerhalb der großzügigen ArrivalTolerance
    /// (Nutzer-Report: "Gate Position bitte exakt sonst ist er zu weit weg") - erst DANACH
    /// interagieren.
    /// </summary>
    private void BeginGateRoute()
    {
        gatePhase = GatePhase.Approaching;
        stateEnteredAt = DateTime.UtcNow;
        state = State.GateRoute;
        BeginFinalApproach(activeGateRoute!.Value.GatePosition);
        StatusText = Loc.T($"Laufe genau zum Tor: {currentTargetEntry?.Name}...", $"Walking precisely to the gate: {currentTargetEntry?.Name}...");
    }

    /// <summary>
    /// Läuft eine Plugin.AetherCurrentGateRoute ab: abmounten, mit dem Tor-Objekt interagieren, ein
    /// aufkommendes Ja/Nein-Fenster bestätigen (wie NoFlyAreaExit.UpdateInteracting/
    /// Plugin.TryConfirmSelectYesno), dann die Ladeanimation abwarten - danach normal weiter (Mount
    /// anfordern, BeginPathfind zum echten Ziel, dessen Position jetzt - nach dem Öffnen des Tors -
    /// erst zuverlässig per queryFlagToPoint auflösbar ist, falls keine WorldPosition hinterlegt ist).
    /// </summary>
    private void UpdateGateRoute()
    {
        if (currentTargetEntry == null || activeGateRoute is not { } route)
        {
            state = State.Idle;
            return;
        }

        switch (gatePhase)
        {
            case GatePhase.Approaching:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Laufweg zum Tor dauert zu lange", "Path to the gate is taking too long"));
                    return;
                }

                gatePhase = GatePhase.Interacting;
                gateInteractAttempts = 0;
                gateDismountedAt = null;
                stateEnteredAt = DateTime.UtcNow;
                StatusText = Loc.T($"Öffne das Tor: {currentTargetEntry.Name}...", $"Opening the gate: {currentTargetEntry.Name}...");
                return;
            }

            case GatePhase.Interacting:
            {
                if (Plugin.TryConfirmSelectYesno())
                {
                    gatePhase = GatePhase.WaitingForLoadingScreen;
                    gateHasSeenLoadingScreen = false;
                    gateLoadingEndedAt = null;
                    stateEnteredAt = DateTime.UtcNow;
                    StatusText = Loc.T($"Warte auf Ladeanimation: {currentTargetEntry.Name}...", $"Waiting for the loading animation: {currentTargetEntry.Name}...");
                    return;
                }

                if (Plugin.Condition[ConditionFlag.Mounted])
                {
                    Plugin.TryDismount();
                    gateDismountedAt = null;
                    return;
                }

                gateDismountedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - gateDismountedAt.Value < GateDismountSettleDelay)
                    return;

                if (gateInteractAttempts > 0 && DateTime.UtcNow - gateInteractedAt < GateInteractRetryInterval)
                    return;

                if (gateInteractAttempts >= GateMaxInteractAttempts)
                {
                    SkipCurrent(Loc.T("Tor reagiert nicht", "The gate does not respond"));
                    return;
                }

                if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                {
                    SkipCurrent(Loc.T("Tor-Objekt nicht gefunden", "Gate object not found"));
                    return;
                }

                var gateObject = FindNearestNamedObject(route.GatePosition, GateObjectSearchRadius, route.GateObjectName);
                if (gateObject == null)
                    return;

                if (!Plugin.IsCurrentTarget(gateObject))
                {
                    Plugin.SetTarget(gateObject);
                    return;
                }

                Plugin.Log.Info($"[AetherCurrentAutomation] UpdateGateRoute({currentTargetEntry.Name}): interagiere mit '{gateObject.Name}'.");
                Plugin.InteractWithGameObject(gateObject);
                gateInteractAttempts++;
                gateInteractedAt = DateTime.UtcNow;
                return;
            }

            case GatePhase.WaitingForLoadingScreen:
            {
                // Falls das Fenster ein zweites Mal kommt (siehe NoFlyAreaExit.UpdateWaitingForExit).
                Plugin.TryConfirmSelectYesno();

                var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
                if (loading)
                    gateHasSeenLoadingScreen = true;

                if (loading || !gateHasSeenLoadingScreen)
                {
                    gateLoadingEndedAt = null;
                    if (DateTime.UtcNow - stateEnteredAt > GateLoadingTimeout)
                        ResumeAfterGateRoute();

                    return;
                }

                // Kurz nach Ende der Ladeanimation ist der Charakter noch nicht wieder voll
                // "steuerbar" (Nutzer-Report: "Nach der Gate Animation hat er nicht aufgemountet") -
                // derselbe Effekt wie bei TryTeleportHomeAfterCompletion/
                // PostCompletionTeleportSettleDelay, ein /mount-Befehl direkt im ersten Frame danach
                // verhallt sonst ungenutzt.
                gateLoadingEndedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - gateLoadingEndedAt.Value < PostCompletionTeleportSettleDelay)
                    return;

                ResumeAfterGateRoute();
                return;
            }
        }
    }

    /// <summary>
    /// Tor passiert - jetzt die echte Zielposition auflösen (entry.WorldPosition, falls bekannt,
    /// sonst erst JETZT per queryFlagToPoint, da der Bereich hinter dem Tor vorher ggf. gar nicht
    /// begehbar/auflösbar war) und ganz normal weiter wie nach einer Sprung-/Handroute.
    /// </summary>
    private void ResumeAfterGateRoute()
    {
        activeGateRoute = null;

        Vector3? floorPoint;
        if (currentTargetEntry!.WorldPosition is { } exactPosition)
        {
            floorPoint = exactPosition;
        }
        else
        {
            Plugin.OpenEntryMap(currentTargetEntry, showMapWindow: false);
            floorPoint = queryFlagToPoint.InvokeFunc();
        }

        if (floorPoint == null)
        {
            SkipCurrent(Loc.T("Nicht erreichbar", "Not reachable"));
            return;
        }

        currentTargetPosition = floorPoint.Value;
        BeginMountAndPathfind(currentTargetEntry);
    }

    /// <summary>Wie FindNearestEventObj, aber ohne ObjectKind-Einschränkung und mit Namensfilter (Teilstring, Groß-/Kleinschreibung egal) - für Torobjekte wie "Cermet Bulkhead" (siehe NoFlyAreaExit.FindGateObject).</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestNamedObject(Vector3 nearPosition, float maxDistance, string nameContains)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? nearest = null;
        var bestDistance = maxDistance;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (!obj.IsTargetable || obj is Dalamud.Game.ClientState.Objects.Types.ICharacter)
                continue;

            if (obj.Name.TextValue.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
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

            // Gerade am Startpunkt einer Handroute angekommen (siehe StartMovingTo/
            // Plugin.AetherCurrentManualRoutes) - jetzt die festen Wegpunkte abgehen, statt normal
            // weiterzumachen.
            if (activeManualRoute != null)
            {
                BeginManualRoute();
                return;
            }

            // Gerade am Tor einer Torroute angekommen (siehe StartMovingTo/Plugin.AetherCurrentGateRoutes) -
            // jetzt interagieren/bestätigen/warten, statt normal weiterzumachen.
            if (activeGateRoute != null)
            {
                BeginGateRoute();
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
            if (jumpRouteReturnPath is { } simulatedReturnPath)
            {
                BeginReturnToStart(simulatedReturnPath);
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

        // Die hinterlegte WorldPosition ist nur so genau wie ihre Quelle (von Hand/Community erfasst) -
        // auch nach exaktem Anlaufen DIESER Koordinate (siehe FinalApproachTolerance) kann das wirkliche
        // Spielobjekt noch ein paar Yalm danebenliegen (Nutzer-Report: "steht an ihm dran, markiert ihn,
        // interagiert aber nicht" - laut PostInteractDiag tatsächlich 2.7y vom gefundenen Objekt
        // entfernt). Jetzt, wo das echte Objekt bekannt ist, einmal noch genau DORTHIN laufen, statt dem
        // ungenauen Datenwert blind zu vertrauen - erst NACH bestätigter Ankunft (pathIsRunning==false)
        // mit dem eigentlichen Interact weitermachen.
        if (!hasApproachedInteractObject)
        {
            if (interactObjectMountingForFlight)
            {
                if (!Plugin.Condition[ConditionFlag.Mounted])
                {
                    if (DateTime.UtcNow - stateEnteredAt > MountWaitTimeout)
                    {
                        interactObjectMountingForFlight = false;
                        hasApproachedInteractObject = true;
                        Plugin.Log.Warning($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): Aufsitzen für den Flug-Anflug hat zu lange gedauert - akzeptiere Fußweg-Stand.");
                    }
                    return;
                }

                pathfindAndMoveCloseTo.InvokeFunc(gameObject.Position, true, FinalApproachTolerance);
                stuckDetector.Reset();
                stateEnteredAt = DateTime.UtcNow;
                interactObjectMountingForFlight = false;
                approachingInteractObject = true;
                Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): aufgesessen - fliege jetzt genau zum Objekt ({gameObject.Position}).");
                StatusText = Loc.T($"Fliege genau zum Objekt: {currentTargetEntry.Name}...", $"Flying precisely to the object: {currentTargetEntry.Name}...");
                return;
            }

            if (!approachingInteractObject && !interactObjectDismounting)
            {
                var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? gameObject.Position;
                var distanceToObject = Vector3.Distance(playerPos, gameObject.Position);
                if (distanceToObject <= InteractObjectApproachThreshold)
                {
                    hasApproachedInteractObject = true;
                }
                else
                {
                    interactObjectDismounting = true;
                    interactObjectDismountedAt = null;
                    SetExactPathTolerance(true);
                    Plugin.TryDismount();
                    hasIntentionallyDismounted = true;
                    stateEnteredAt = DateTime.UtcNow;
                    Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): Objekt {distanceToObject:F2}y entfernt gefunden - steige erst ab, bevor dorthin gelaufen wird ({gameObject.Position}).");
                    StatusText = Loc.T($"Steige ab, bevor zum Objekt gelaufen wird: {currentTargetEntry.Name}...", $"Dismounting before walking to the object: {currentTargetEntry.Name}...");
                    return;
                }
            }
            else if (interactObjectDismounting)
            {
                // Condition[Mounted] wird schon VOR dem Ende der sichtbaren Absteige-Animation false -
                // ein Laufauftrag mitten in dieser Animation greift nicht (identisches Problem/dieselbe
                // Lösung wie JumpDismountSettleDelay in UpdateJumpRoute).
                if (Plugin.Condition[ConditionFlag.Mounted])
                {
                    Plugin.TryDismount();
                    return;
                }

                interactObjectDismountedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - interactObjectDismountedAt.Value < JumpDismountSettleDelay)
                    return;

                pathfindAndMoveCloseTo.InvokeFunc(gameObject.Position, false, FinalApproachTolerance);
                stuckDetector.Reset();
                stateEnteredAt = DateTime.UtcNow;
                interactObjectDismounting = false;
                approachingInteractObject = true;
                Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): abgestiegen und eingependelt - laufe jetzt genau zum Objekt ({gameObject.Position}).");
                StatusText = Loc.T($"Laufe genau zum Objekt: {currentTargetEntry.Name}...", $"Walking precisely to the object: {currentTargetEntry.Name}...");
                return;
            }
            else
            {
                if (pathIsRunning.InvokeFunc())
                {
                    if (DateTime.UtcNow - stateEnteredAt > StepMaxDuration)
                        SkipCurrent(Loc.T("Laufweg zum Objekt dauert zu lange", "Path to the object is taking too long"));
                    return;
                }

                approachingInteractObject = false;

                var arrivedPos = Plugin.ObjectTable.LocalPlayer?.Position ?? gameObject.Position;
                var remainingDistance = Vector3.Distance(arrivedPos, gameObject.Position);
                if (remainingDistance <= InteractObjectApproachThreshold || interactObjectTriedFlyingApproach)
                {
                    hasApproachedInteractObject = true;
                }
                else
                {
                    // Fußweg kam nicht nah genug heran (z.B. Ätherströmung über einem Abgrund/in der
                    // Luft, zu Fuß unerreichbar - Nutzer-Report "bleibt wie immer so weit weg stehen").
                    // Einmalig per Flug-Anflug direkt auf die exakte Position versuchen.
                    interactObjectTriedFlyingApproach = true;
                    if (Plugin.CanFly && Plugin.TryRequestAetheryteMount())
                    {
                        interactObjectMountingForFlight = true;
                        stateEnteredAt = DateTime.UtcNow;
                        Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): noch {remainingDistance:F2}y entfernt nach Fußweg - versuche Flug-Anflug.");
                        StatusText = Loc.T($"Steige auf für den Flug-Anflug: {currentTargetEntry.Name}...", $"Mounting for the flight approach: {currentTargetEntry.Name}...");
                    }
                    else
                    {
                        hasApproachedInteractObject = true;
                    }
                }
            }
        }

        if (!hasInteractedThisCycle)
        {
            // Wie AetheryteAutomation.UpdateInteracting (dort per Diagnose-Log bestätigt): ein einzelner
            // StopPath()-Aufruf reicht nicht - ein eben erst als "angekommen" gewerteter, enger
            // Laufauftrag (siehe die neue, enge FinalApproachTolerance-Anfahrt) läuft manchmal noch 1-2
            // Frames nach, schiebt den Charakter dabei aus der Interagieren-Reichweite und lässt den
            // Interact-Aufruf ins Leere laufen (Nutzer-Report: "interagiert nicht mit dem Aether
            // Current", wiederkehrend). Deshalb jeden Frame erneut stoppen UND eine kurze Zeit lang
            // bestätigt bekommen, dass wirklich nichts mehr läuft, bevor Ziel gesetzt/interagiert wird -
            // läuft es währenddessen doch wieder an, fängt die Bestätigung neu an.
            StopPath();

            // Sicherheitsnetz: meldet vnavmesh IsRunning==true dauerhaft (z.B. IPC-Hänger, die enge
            // FinalApproachTolerance wird nie als "angekommen" bestätigt), würde das Warten auf
            // pathIsRunning==false HIER sonst für immer blockieren, noch VOR jedem Timeout (UnlockWait-
            // Timeout greift erst NACH dem Interact) - "steht davor und macht nix" (Nutzer-Report).
            // Nach InteractPathSettleMaxWait trotzdem interagieren, unabhängig vom IsRunning-Zustand.
            if (pathIsRunning.InvokeFunc() && DateTime.UtcNow - stateEnteredAt < InteractPathSettleMaxWait)
            {
                interactPathSettleConfirmedSince = null;
                return;
            }

            interactPathSettleConfirmedSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - interactPathSettleConfirmedSince.Value < InteractPathSettleDuration
                && DateTime.UtcNow - stateEnteredAt < InteractPathSettleMaxWait)
                return;

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
            postInteractDiagnosticStartedAt = DateTime.UtcNow;
            postInteractDiagnosticLastPos = Plugin.ObjectTable.LocalPlayer?.Position;
            Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): interagiert mit BaseId={gameObject.BaseId} @ {gameObject.Position}, warte auf Freischaltung...");
            return;
        }

        // Siehe postInteractDiagnosticStartedAt-Kommentar - jeden Frame für kurze Zeit NACH dem
        // Interact loggen, was mit der Position passiert und welche Condition-Flags aktiv sind.
        if (postInteractDiagnosticStartedAt is { } diagStart)
        {
            if (DateTime.UtcNow - diagStart < PostInteractDiagnosticDuration)
            {
                var diagPos = Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
                var movedSinceLastFrame = postInteractDiagnosticLastPos.HasValue ? Vector3.Distance(postInteractDiagnosticLastPos.Value, diagPos) : 0f;
                postInteractDiagnosticLastPos = diagPos;
                Plugin.Log.Info($"[AetherCurrentAutomation] PostInteractDiag({currentTargetEntry.Name}): pos={diagPos}, movedSinceLastFrame={movedSinceLastFrame:F4}, " +
                                 $"distToObject={Vector3.Distance(diagPos, gameObject.Position):F3}, Mounted={Plugin.Condition[ConditionFlag.Mounted]}, " +
                                 $"Casting={Plugin.Condition[ConditionFlag.Casting]}, OccupiedInEvent={Plugin.Condition[ConditionFlag.OccupiedInEvent]}, " +
                                 $"InCombat={Plugin.Condition[ConditionFlag.InCombat]}, BetweenAreas={Plugin.Condition[ConditionFlag.BetweenAreas]}, " +
                                 $"pathIsRunning={pathIsRunning.InvokeFunc()}, unlocked={Plugin.IsAetherCurrentUnlocked(currentTargetEntry.Id)}");
            }
            else
            {
                postInteractDiagnosticStartedAt = null;
            }
        }

        if (Plugin.IsAetherCurrentUnlocked(currentTargetEntry.Id))
        {
            Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): freigeschaltet.");

            if (jumpRouteReturnPath is { } returnPath)
            {
                BeginReturnToStart(returnPath);
                return;
            }

            FinishCurrent();
            return;
        }

        var isLastAttempt = interactAttemptCount >= InteractMaxAttempts - 1;
        var currentTimeout = isLastAttempt ? UnlockWaitTimeout : InteractRetryWaitTimeout;
        if (DateTime.UtcNow - stateEnteredAt > currentTimeout)
        {
            interactAttemptCount++;
            if (interactAttemptCount < InteractMaxAttempts)
            {
                // Erster Interact kann z.B. durch eine noch laufende Abmount-Animation ins Leere laufen
                // (Nutzer-Report: "versucht beim Abmounten zu schnell zu interagieren") - statt direkt
                // aufzugeben, bis zu InteractMaxAttempts erneut versuchen.
                Plugin.Log.Info($"[AetherCurrentAutomation] UpdateInteracting({currentTargetEntry.Name}): Freischalten nicht erfolgt (Versuch {interactAttemptCount}/{InteractMaxAttempts}) - versuche erneut zu interagieren.");
                hasInteractedThisCycle = false;
                interactPathSettleConfirmedSince = null;
                postInteractDiagnosticStartedAt = null;
                stateEnteredAt = DateTime.UtcNow;
                return;
            }

            SkipCurrent(Loc.T("Freischalten hat nicht geklappt", "unlocking did not go through"));
        }
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

    /// <summary>
    /// Siehe TeleportHomeAfterCompletionIds-Kommentar - für die dort gelisteten Punkte erst zum
    /// Haupt-Ätheryten teleportieren (TryTeleportHomeAfterCompletion), bevor tatsächlich
    /// fertiggestellt wird; für alle anderen unverändert sofort FinishCurrentImmediate.
    /// </summary>
    private void FinishCurrent()
    {
        if (currentTargetEntry != null && TeleportHomeAfterCompletionIds.Contains(currentTargetEntry.Id))
        {
            TryTeleportHomeAfterCompletion();
            return;
        }

        FinishCurrentImmediate();
    }

    private void FinishCurrentImmediate()
    {
        Plugin.Log.Info($"[AetherCurrentAutomation] FinishCurrent({currentTargetEntry?.Name}): freigeschaltet.");
        attemptCounts.Remove(currentTargetEntry!.Id);
        lastFinishedId = currentTargetEntry.Id;
        currentTargetEntry = null;
        jumpRouteReturnPath = null;
        returnWaypointIndex = 0;
        returnToStartArrivedAt = null;
        activeManualRoute = null;
        manualRouteWaypointIndex = 0;
        activeGateRoute = null;
        // Enge Toleranz (siehe IssueReturnWaypointMove) nicht dauerhaft gesetzt lassen - das nächste
        // Ziel wird wieder ganz normal mit der großzügigeren ArrivalTolerance angelaufen.
        RestorePathTolerance();
        state = State.Idle;
    }

    /// <summary>
    /// Teleportiert zum Haupt-Ätheryten der aktuellen Zone, BEVOR der nächste Punkt gewählt wird
    /// (siehe UpdateTeleportingHomeAfterCompletion). Klappt das nicht (Lifestream fehlt/Ätheryte noch
    /// nicht freigeschaltet), einfach normal fertigstellen statt den Punkt zu überspringen - der war
    /// ja schon erfolgreich erledigt.
    /// </summary>
    private void TryTeleportHomeAfterCompletion()
    {
        if (!IsLifestreamAvailable())
        {
            FinishCurrentImmediate();
            return;
        }

        var territory = Plugin.ResolveEffectiveTerritoryId(Plugin.ClientState.TerritoryType);
        var mainAetheryteId = Plugin.FindUnlockedMainAetheryteId(territory);
        if (mainAetheryteId == null || !lifestreamTeleport.InvokeFunc(mainAetheryteId.Value, (byte)0))
        {
            FinishCurrentImmediate();
            return;
        }

        postCompletionTeleportHasSeenLoadingScreen = false;
        state = State.TeleportingHomeAfterCompletion;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T("Teleportiere zum Haupt-Ätheryten...", "Teleporting to the main aetheryte...");
    }

    /// <summary>
    /// Wie SightseeingAutomation.UpdateTeleportingHomeAfterCompletion - BetweenAreas/-51 statt
    /// lifestreamIsBusy/Zonenwechsel (lifestreamIsBusy meldet "fertig" schon während der
    /// Teleport-Besetzungszeit, lange vor dem eigentlichen Ladebildschirm; ein Teleport zum eigenen
    /// Haupt-Ätheryten bleibt außerdem oft in derselben Zonen-ID).
    /// </summary>
    private void UpdateTeleportingHomeAfterCompletion()
    {
        var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (loading)
            postCompletionTeleportHasSeenLoadingScreen = true;

        if (loading || !postCompletionTeleportHasSeenLoadingScreen)
        {
            if (DateTime.UtcNow - stateEnteredAt > PostCompletionTeleportTimeout)
                FinishCurrentImmediate();

            return;
        }

        if (DateTime.UtcNow - stateEnteredAt < PostCompletionTeleportSettleDelay)
            return;

        FinishCurrentImmediate();
    }

    /// <summary>
    /// Siehe jumpRouteReturnPath-Kommentar: nach erfolgreicher Freischaltung einer Ätherströmung mit
    /// Sprungroute erst noch den hinterlegten Rückweg (ein oder mehrere Wegpunkte) ablaufen, statt
    /// direkt (von der oft schmalen Landestelle aus) zum nächsten Ziel weiterzumachen.
    /// </summary>
    private void BeginReturnToStart(List<Vector3> path)
    {
        jumpRouteReturnPath = path;
        returnWaypointIndex = 0;
        returnToStartArrivedAt = null;
        IssueReturnWaypointMove();
    }

    /// <summary>Wie BeginReturnToStart, setzt aber NICHT bei Wegpunkt 0 neu auf - für die Fortsetzung nach einem Kampf mitten im Rückweg (siehe UpdateDefendingSelf).</summary>
    private void ResumeReturnToStart()
    {
        returnToStartArrivedAt = null;
        IssueReturnWaypointMove();
    }

    private void IssueReturnWaypointMove()
    {
        // Ohne Toleranz genau auf die hinterlegten Punkte laufen (Nutzeranforderung: "ohne Toleranz
        // auf die Punkte laufen und erst zum nächsten wenn er angekommen ist") - ArrivalTolerance
        // (6y, für die grobe Anfahrt zur Ätherströmung gedacht) blieb hier schon weit vor den echten
        // Punkten stehen. Gleiche enge Toleranz wie beim Sprung selbst (JumpRoutePreciseTolerance).
        SetExactPathTolerance(true);
        var waypoint = jumpRouteReturnPath![returnWaypointIndex];
        if (!pathfindAndMoveCloseTo.InvokeFunc(waypoint, false, JumpRoutePreciseTolerance))
        {
            // vnavmesh lehnt ab (z.B. schon am Wegpunkt) - kein Problem, einfach direkt fertig.
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
        // Der Rückweg ist bewusst zu Fuß (Nutzeranforderung: "zu Fuß zurück an die Startposition,
        // dann aufmounten") - manche Spiel-/Mount-Einstellungen mounten sonst während eines langen
        // Laufauftrags automatisch wieder auf. Nur absteigen und DIESEN Frame abwarten - der Weg
        // selbst wird unten über die tatsächliche Distanz (nicht blind pathIsRunning) neu angestoßen,
        // falls das Absteigen ihn unterbrochen hat.
        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            Plugin.TryDismount();
            return;
        }

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

        // pathIsRunning==false heißt nicht zwingend "angekommen" - ein Mount-Wechsel (siehe oben)
        // oder ein Treffer im Kampf (Nutzeranforderung: während des Rückwegs soll NICHT gekämpft
        // werden, siehe UpdateDefendingSelf, das außerhalb von State.Interacting bewusst nichts tut)
        // kann den vnavmesh-Weg vorher abbrechen. Tatsächliche Distanz zum aktuellen Wegpunkt prüfen,
        // bevor wirklich fertig gemeldet wird - sonst würde ein nur UNTERBROCHENER Weg fälschlich als
        // "angekommen" gelten und sofort das nächste (im Simulations-Modus oft wieder dasselbe) Ziel
        // angefangen werden (Nutzer-Report: "fängt den gleichen von vorne an").
        var waypoint = jumpRouteReturnPath![returnWaypointIndex];
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        if (playerPos is { } pos && Vector3.Distance(pos, waypoint) > JumpRoutePreciseTolerance)
        {
            pathfindAndMoveCloseTo.InvokeFunc(waypoint, false, JumpRoutePreciseTolerance);
            return;
        }

        // Dieser Wegpunkt ist erreicht - folgt noch ein weiterer (mehrstufiger Rückweg, siehe
        // Plugin.AetherCurrentJumpRoute.ReturnPath), direkt dorthin weiter, ohne schon die
        // Abschluss-Pause unten zu starten (die gilt nur für den LETZTEN Wegpunkt).
        if (returnWaypointIndex < jumpRouteReturnPath.Count - 1)
        {
            returnWaypointIndex++;
            IssueReturnWaypointMove();
            return;
        }

        returnToStartArrivedAt ??= DateTime.UtcNow;
        if (DateTime.UtcNow - returnToStartArrivedAt.Value < PostReturnToStartSettleDelay)
        {
            StatusText = Loc.T(
                $"Am Startpunkt, kurze Pause nach: {currentTargetEntry?.Name}...",
                $"At the start point, brief pause after: {currentTargetEntry?.Name}...");
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
