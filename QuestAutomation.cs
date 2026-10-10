using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Plugin.Ipc;

namespace TheExplorersCodex;

/// <summary>
/// Steuert das Fremdplugin "Questionable" (https://github.com/WigglyMuffin/Questionable) über
/// dessen Dalamud-IPC, um die aktuell fehlenden Quests einer Zone nacheinander automatisch
/// abzuarbeiten. Questionable selbst stellt dafür keine offizielle/dokumentierte IPC-Version
/// bereit - alle Aufrufe sind daher defensiv (try/catch, HasFunction-Prüfung), da das Plugin
/// jederzeit fehlen oder sich in einer neueren Version anders verhalten kann.
/// </summary>
public sealed class QuestAutomation
{
    private enum State
    {
        SummoningChocobo,
        Idle,
        TeleportingToQuestStart,
        GateGuardRoute,
        WaitingForPickup,
        Running,
        TravelingHome,
    }

    private enum GateGuardPhase
    {
        Mounting,
        Approaching,
        Interacting,
        WaitingForLoadingScreen,
    }

    // Von Hand hinterlegt: manche Quests lassen sich zwar in der Zone annehmen, aber Questionable
    // läuft von der normalen Ankunftsposition aus nicht sauber zum Questgeber (Nutzer-Report, z.B.
    // "A Hunger for Trade"/"Closing Up Shop"/"Out of Sight" in The Peaks) - für diese erst per
    // Lifestream zu einem konkreten, bekannten Ätheryten teleportieren, DANACH erst
    // Questionable.StartSingleQuest aufrufen (gleiches Prinzip wie
    // AetherCurrentAutomation.PreStartTeleportAetheryteNames). Nach Anzeigenamen statt Quest-RowId
    // indiziert, da TryStartNext ohnehin schon den vollen CollectibleEntry (inkl. Name) kennt.
    private static readonly Dictionary<string, string> PreStartTeleportAetheryteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A Hunger for Trade"] = "Ala Ghiri",
        ["Closing Up Shop"] = "Ala Ghiri",
        ["Out of Sight"] = "Ala Ghiri",
    };

    // Siehe PreStartTeleportAetheryteNames-Kommentar - gleiche Werte wie
    // AetherCurrentAutomation.PostCompletionTeleportTimeout/-SettleDelay.
    private static readonly TimeSpan PreStartTeleportTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PreStartTeleportSettleDelay = TimeSpan.FromSeconds(2);

    // Von Hand hinterlegt: "A Hunger for Trade"/"Closing Up Shop" (The Peaks, nach dem Teleport zu
    // Ala Ghiri, siehe PreStartTeleportAetheryteNames) liegen hinter einem Tor, das erst ein
    // Wachposten öffnet - erst zur angegebenen Position laufen, den NPC ansprechen, den Dialog
    // durchklicken und die Ja/Nein-Abfrage bestätigen, dann (nach der Ladeanimation) abmounten und
    // erst DANACH Questionable starten (identisches Prinzip wie AetherCurrentAutomation.
    // AetherCurrentGateRoutes, hier aber mit einem echten NPC statt eines Tor-Objekts).
    private static readonly Dictionary<string, (Vector3 Position, string NpcName)> GateGuardRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A Hunger for Trade"] = (new Vector3(-130.32358f, 305.35394f, 190.10594f), "Ala Mhigan Resistance Gate Guard"),
        ["Closing Up Shop"] = (new Vector3(-130.32358f, 305.35394f, 190.10594f), "Ala Mhigan Resistance Gate Guard"),
    };

    private const int GateGuardMaxInteractAttempts = 3;
    private const float GateGuardObjectSearchRadius = 8f;
    private const float GateGuardArrivalTolerance = 3f;
    private static readonly TimeSpan GateGuardDismountSettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GateGuardInteractRetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GateGuardLoadingTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GateGuardStepMaxDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GateGuardMountWaitTimeout = TimeSpan.FromSeconds(15);

    // Ein einzelner Plugin.TryRequestAetheryteMount-Ruf schlägt direkt nach dem Lifestream-Teleport
    // manchmal wortlos fehl (Nutzer-Report: Status zeigt "Rufe Mount..." an, der Charakter bleibt
    // aber zu Fuß) - vermutlich, weil der Spielclient unmittelbar nach der Ladeanimation noch kurz
    // "beschäftigt" ist und die Mount-Aktion/den "/mount"-Text-Befehl in diesem Fenster verschluckt,
    // ohne dass Condition[Mounted] oder der Rückgabewert das anzeigen. Deshalb wird hier (anders als
    // bei AetherCurrentAutomation.BeginMountAndPathfind, das nur einmal ruft) in Abständen erneut
    // versucht, bis entweder Mounted==true wird oder GateGuardMountWaitTimeout abläuft - identisches
    // Intervall wie Plugin.TryRemountAfterForcedDismount.
    private static readonly TimeSpan GateGuardMountRetryInterval = TimeSpan.FromSeconds(3);
    private DateTime gateGuardLastMountAttemptAt;

    // Wie lange maximal auf das Beschwören + Setzen der Stance gewartet wird, bevor trotzdem mit der
    // eigentlichen Automation begonnen wird (z.B. falls keine Gysahl Greens vorhanden sind) - siehe
    // UpdateSummoningChocobo. Großzügig, da ggf. erst aus der Luft gelandet werden muss (siehe
    // ChocoboCompanionSupport.TryRequestLanding).
    private static readonly TimeSpan ChocoboSummonWaitTimeout = TimeSpan.FromSeconds(30);

    // Wie lange nach dem Start einer Quest gewartet wird, bis Questionable sie tatsächlich
    // übernimmt (IsRunning == true) - reagiert es nicht, gilt die Quest als nicht unterstützt.
    private static readonly TimeSpan PickupTimeout = TimeSpan.FromSeconds(6);

    // Verhindert eine Endlosschleife, falls Questionable eine Quest zwar annimmt, aber nie
    // abschließt (z.B. weil ein manueller Schritt nötig ist) - nach so vielen Versuchen wird
    // dieselbe Quest überspringen.
    private const int MaxAttemptsPerQuest = 2;

    // Wie lange IsRunning DURCHGEHEND false sein muss, bevor eine laufende Quest als "fertig" gilt
    // (siehe State.Running in Update) - kurze, einzelne false-Meldungen (z.B. während eines quest-
    // internen Ladebildschirms bei einem Zonenwechsel mitten in der Quest) sollen nicht sofort als
    // Abschluss gewertet werden.
    private static readonly TimeSpan RunningFalseGracePeriod = TimeSpan.FromSeconds(6);

    // Wie lange maximal auf die Rückreise per Lifestream zur Startzone gewartet wird (siehe
    // TryTravelHome), bevor die Automation aufgibt statt endlos zu warten.
    private static readonly TimeSpan TravelHomeTimeout = TimeSpan.FromSeconds(60);

    // Nach "Lifestream.IsBusy() == false" kann es noch einen Moment dauern, bis Plugin.ClientState.
    // TerritoryType tatsächlich auf die neue Zone aktualisiert ist - ohne diese kurze Verzögerung
    // hält der nächste Update-Aufruf die Zone noch für die alte und stößt eine erneute (unnötige)
    // Rückreise an (siehe auch AetheryteAutomation.DistrictTravelSettleDelay).
    private static readonly TimeSpan TravelHomeSettleDelay = TimeSpan.FromSeconds(2);

    // Lifestream.IsBusy() wird bei der bezahlten Teleport-Aktion beobachtet schon lange VOR dem
    // tatsächlichen Abschluss wieder false (die echte Zauberzeit von ~5s plus Ladebildschirm laufen
    // noch, siehe UpdateTravelingHome) - ohne diese Mindestdauer (Zauberzeit + typischer
    // Ladebildschirm + kleiner Puffer) würde die Ankunftsprüfung viel zu früh laufen, fälschlich
    // "nicht angekommen" melden und sofort einen neuen Versuch starten, der dann an der
    // Teleport-Abklingzeit scheitert (accepted=False).
    private static readonly TimeSpan TeleportMinimumTravelDuration = TimeSpan.FromSeconds(10);

    // Verhindert eine Endlosschleife, falls Lifestream eine Rückreise wiederholt asynchron ablehnt
    // (siehe UpdateTravelingHome) - nach so vielen gescheiterten Versuchen wird die aktuelle Zone
    // stattdessen zur neuen Startzone, statt es immer wieder mit demselben Ergebnis zu versuchen.
    private const int MaxTravelHomeAttempts = 2;
    private int travelHomeFailureCount;

    private readonly ICallGateSubscriber<string, bool> startSingleQuest;
    private readonly ICallGateSubscriber<bool> isRunning;

    // questId -> "gesperrt"? Prüft nur (ohne Nebenwirkung), ob Questionable eine Quest aktuell
    // bearbeiten könnte - für die proaktive Support-Markierung im Overlay direkt beim Betreten
    // einer Zone (siehe RefreshSupportStatus), statt erst nach einem echten Start-Versuch.
    private readonly ICallGateSubscriber<string, bool> isQuestLocked;

    // aetheryteId, subIndex(0 = großer Aetheryte) -> angenommen? Volle (kostenpflichtige) Teleport-
    // Aktion für die Rückreise zur Startzone (siehe TryTravelHome). Der kostenlose Aethernetz-Sprung
    // (Lifestream.AethernetTeleportById) wird bewusst NICHT mehr versucht: TryTravelHome läuft laut
    // Update/State.Idle NUR, wenn die aktuelle Zone NICHT zur Startstadt gehört (isHome bereits
    // false) - ein Aethernetz-Sprung setzt aber voraus, dass man sich BEREITS in Reichweite des
    // Ziel-Netzwerks befindet, was dadurch nie zutrifft. Er wurde früher trotzdem versucht, meldete
    // "accepted=true" (Lifestream validiert das selbst nicht vorher) und scheiterte danach immer
    // asynchron mit "[Lifestream] Destination could not be found" im Chat, bevor endlich auf diesen
    // zuverlässigen bezahlten Teleport zurückgefallen wurde.
    private readonly ICallGateSubscriber<uint, byte, bool> lifestreamTeleport;

    private readonly ICallGateSubscriber<bool> lifestreamIsBusy;
    private readonly ICallGateSubscriber<object> lifestreamAbort;

    // Siehe GateGuardRoutes-Kommentar - Questionable selbst läuft erst NACH dem Start, daher braucht
    // diese Klasse (anders als sonst) für den Weg bis zum Wachposten eigenes vnavmesh.
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<object> pathStop;

    private State state = State.Idle;
    private uint? currentQuestId;
    private string currentQuestName = string.Empty;
    private uint? homeTerritoryId;
    private DateTime stateEnteredAt;
    private DateTime? travelHomeFinishedAt;
    private DateTime? runningWentFalseAt;

    // Gesetzt, sobald die Automation über den ToDo-Tab gestartet wurde (siehe Start) - dann werden
    // NUR Quests aus dieser Menge angenommen, alle übrigen (auch sonst in der aktuellen Zone
    // fehlenden) Quests bleiben unangetastet. null = normaler Modus (unverändertes Verhalten:
    // arbeitet einfach die ganze Zone ab, siehe TryStartNext).
    private HashSet<uint>? restrictToQuestIds;

    // Ziel der aktuell laufenden Reise (State.TravelingHome) - im normalen Modus immer
    // homeTerritoryId, im restringierten Modus die Zone der nächsten noch offenen ToDo-Quest (siehe
    // UpdateRestrictedIdle). Getrennt von homeTerritoryId gehalten, damit eine Zwischenreise zu
    // einer ToDo-Quest in einer anderen Zone die eigentliche "Heimatzone" nicht überschreibt.
    private uint? travelTargetTerritoryId;
    private bool travelIsReturnHome;
    private uint? travelFailureSkipQuestId;

    // Siehe PreStartTeleportAetheryteNames-Kommentar - welche Quest nach dem Teleport tatsächlich
    // gestartet werden soll (State.TeleportingToQuestStart), und ob der Ladebildschirm des Teleports
    // schon (einmal) gesehen wurde (verhindert ein verfrühtes "fertig", falls er erst mit Verzögerung
    // einsetzt - gleiches Prinzip wie AetherCurrentAutomation.preStartTeleportHasSeenLoadingScreen).
    private CollectibleEntry? pendingQuestEntryForTeleport;
    private bool preStartTeleportHasSeenLoadingScreen;

    // Siehe GateGuardRoutes-Kommentar - Zustand der eigenen kleinen Tor-Wach-Routine (State.
    // GateGuardRoute), läuft NACH dem Pre-Start-Teleport und VOR dem eigentlichen Questionable-Start.
    private (Vector3 Position, string NpcName)? activeGateGuardRoute;
    private GateGuardPhase gateGuardPhase;
    private bool gateGuardHasSeenPathRunning;
    private int gateGuardInteractAttempts;
    private DateTime gateGuardInteractedAt;
    private DateTime? gateGuardDismountedAt;
    private bool gateGuardHasSeenLoadingScreen;
    private DateTime? gateGuardLoadingEndedAt;

    private readonly HashSet<uint> skippedQuestIds = new();
    private readonly Dictionary<uint, int> attemptCounts = new();

    // Bleibt (anders als skippedQuestIds/attemptCounts) über Start()/Stop()-Zyklen hinweg
    // bestehen - das ist eine dauerhaft gültige Erkenntnis ("Questionable kennt diese Quest
    // nicht"), keine Buchhaltung für den aktuellen Automation-Durchlauf. Für die rote
    // "Nicht unterstützt"-Markierung im Overlay, auch außerhalb einer laufenden Automation.
    private readonly HashSet<uint> notSupportedQuestIds = new();

    // Welche Quest-IDs bereits per IsQuestLocked geprüft wurden (unabhängig vom Ergebnis) - auch
    // das bleibt dauerhaft bestehen, damit nicht jeden Frame erneut per IPC nachgefragt wird.
    private readonly HashSet<uint> checkedSupportQuestIds = new();

    public bool IsKnownUnsupported(uint questId) => notSupportedQuestIds.Contains(questId);

    // Jede Statusänderung bleibt danach noch eine Weile sichtbar (auch nachdem IsActive schon
    // false ist) - sonst verschwindet der eigentliche Grund für ein Stoppen/Überspringen sofort
    // wieder, bevor man ihn lesen kann.
    private static readonly TimeSpan StatusLingerDuration = TimeSpan.FromSeconds(8);
    private string statusText = string.Empty;
    private DateTime statusSetAt = DateTime.MinValue;

    public bool IsActive { get; private set; }

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

    public QuestAutomation()
    {
        startSingleQuest = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Questionable.StartSingleQuest");
        isRunning = Plugin.PluginInterface.GetIpcSubscriber<bool>("Questionable.IsRunning");
        isQuestLocked = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Questionable.IsQuestLocked");

        lifestreamTeleport = Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        lifestreamIsBusy = Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        lifestreamAbort = Plugin.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");

        pathfindAndMoveCloseTo = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathIsRunning = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathStop = Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
    }

    /// <summary>
    /// Questionable meldet Fehler (z.B. "Failed to start task ...", feststeckende Bewegung) nicht
    /// über IsRunning, sondern nur als Chatzeile mit dem festen Präfix "[Questionable] " im
    /// XivChatType.Urgent-Kanal (per PrintError - der Sender bleibt dabei immer leer, daher wird
    /// hier auf den Text geprüft, nicht auf den Absender). Läuft gerade eine Quest, wird sie beim
    /// ersten Auftreten einer solchen Meldung übersprungen, statt endlos auf ein IsRunning==false
    /// zu warten, das so nie kommt.
    /// </summary>
    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!IsActive || state == State.Idle)
            return;

        if (message.LogKind != XivChatType.Urgent)
            return;

        if (!message.Message.TextValue.StartsWith("[Questionable] ", StringComparison.Ordinal))
            return;

        SkipCurrentQuest(Loc.T("Questionable meldet einen Fehler im Chat", "Questionable reported an error in chat"));
    }

    /// <summary>
    /// Prüft, ob Questionable aktuell installiert/geladen ist (registrierte IPC-Provider).
    /// Nur eine Momentaufnahme - Questionable kann jederzeit nachgeladen/entladen werden.
    /// </summary>
    public bool IsQuestionableAvailable()
    {
        try
        {
            return startSingleQuest.HasFunction && isRunning.HasFunction;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Prüft für alle noch nicht geprüften Quests der Zone (unabhängig davon, ob die Automation
    /// gerade läuft), ob Questionable sie unterstützt - über IsQuestLocked, das (anders als
    /// StartSingleQuest) rein lesend ist und nichts anstößt. So kann die rote "Nicht unterstützt"-
    /// Markierung im Overlay schon beim Betreten der Zone erscheinen, statt erst nachdem man die
    /// Automation einmal gestartet und einen echten Start-Versuch pro Quest abgewartet hat. Muss
    /// jeden Frame aufgerufen werden (wie Update) - geprüfte Quests werden übersprungen, es wird
    /// also nicht wiederholt nachgefragt.
    /// </summary>
    public void RefreshSupportStatus(IReadOnlyList<CollectibleEntry> missingQuestsInZone)
    {
        if (!IsQuestionableAvailable())
            return;

        try
        {
            if (!isQuestLocked.HasFunction)
                return;
        }
        catch
        {
            return;
        }

        foreach (var quest in missingQuestsInZone)
        {
            if (checkedSupportQuestIds.Contains(quest.Id))
                continue;

            try
            {
                // Gleiche ID-Umrechnung wie beim echten Start (siehe TryStartNext) - Questionable
                // erwartet überall die 16-Bit-Quest-ID des Spielclients, nicht die volle RowId.
                var locked = isQuestLocked.InvokeFunc(((ushort)quest.Id).ToString());
                checkedSupportQuestIds.Add(quest.Id);
                if (locked)
                    notSupportedQuestIds.Add(quest.Id);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, $"Fehler beim Prüfen des Support-Status von Quest {quest.Id} ({quest.Name}).");
            }
        }
    }

    /// <summary>
    /// homeTerritoryId ist die (aufgelöste, siehe Plugin.ResolveEffectiveTerritoryId) Zone, in der
    /// die Automation gestartet wurde - führt eine Quest den Charakter anderswohin und endet dort
    /// auch (siehe TryTravelHome), reist die Automation danach automatisch per Lifestream wieder
    /// hierher zurück, statt einfach dort weiterzumachen, wo die Quest zufällig endete.
    ///
    /// restrictToQuestIds (optional): vom ToDo-Tab genutzt (siehe CompactOverlayWindow.
    /// DrawToDoAutomationButtonsRow) - arbeitet dann NUR diese Quest-Ids ab, zonenübergreifend (reist
    /// bei Bedarf selbst zur jeweiligen Zone, siehe UpdateRestrictedIdle/TryTravelTo), statt wie im
    /// normalen Modus einfach alle in der aktuellen Zone fehlenden Quests anzunehmen.
    /// </summary>
    public void Start(uint homeTerritoryId, IReadOnlyCollection<uint>? restrictToQuestIds = null)
    {
        IsActive = true;
        currentQuestId = null;
        this.homeTerritoryId = homeTerritoryId;
        this.restrictToQuestIds = restrictToQuestIds is { Count: > 0 } ? new HashSet<uint>(restrictToQuestIds) : null;
        travelHomeFinishedAt = null;
        travelHomeFailureCount = 0;
        runningWentFalseAt = null;
        skippedQuestIds.Clear();
        attemptCounts.Clear();
        Plugin.ChocoboCompanionSupport.Reset();

        // Erst den Chocobo-Begleiter beschwören/die Stance setzen (siehe UpdateSummoningChocobo),
        // BEVOR überhaupt die erste Quest gestartet wird - nur, wenn das Feature aktiv und
        // freigeschaltet ist, sonst direkt wie bisher.
        if (Plugin.UseChocoboCompanion && Plugin.IsChocoboCompanionUnlocked())
        {
            state = State.SummoningChocobo;
            stateEnteredAt = DateTime.UtcNow;
            StatusText = Loc.T("Beschwöre Chocobo-Begleiter...", "Summoning Chocobo Companion...");
        }
        else
        {
            state = State.Idle;
            StatusText = Loc.T("Automation gestartet...", "Automation started...");
        }
    }

    /// <summary>
    /// Questionable hat zwar keine Stopp-IPC (siehe Klassenkommentar), aber denselben harten Stopp
    /// wie sein eigener "Stop all actions now"-Knopf über den Chat-Befehl "/qst stop" - der bricht
    /// Bewegung/Quest/Sammeln bei Questionable sofort ab, nicht nur unser eigenes Nachschieben.
    /// </summary>
    public void Stop()
    {
        IsActive = false;
        state = State.Idle;
        currentQuestId = null;
        homeTerritoryId = null;
        restrictToQuestIds = null;
        StopLifestream();
        StopQuestionable();
    }

    /// <summary>
    /// Nur Questionable selbst hart stoppen (siehe Stop-Klassenkommentar zu "/qst stop"), OHNE
    /// unseren eigenen Automation-Zustand anzufassen - für den Moment vor einer selbst gesteuerten
    /// Lifestream-Rückreise (siehe Update/State.Idle), damit Questionable währenddessen nicht von
    /// sich aus weiterläuft und mit unserer Teleport-Aktion kollidiert.
    /// </summary>
    private void StopQuestionable()
    {
        try
        {
            Plugin.CommandManager.ProcessCommand("/qst stop");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler beim harten Stoppen von Questionable über /qst stop.");
        }
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
            Plugin.Log.Error(ex, "Fehler beim Abbrechen von Lifestream.");
        }
    }

    public void MarkUnavailable()
    {
        StatusText = Loc.T("Questionable nicht gefunden - bitte installieren.", "Questionable not found - please install it.");
    }

    /// <summary>
    /// Blockiert den eigentlichen Automation-Start, bis der Chocobo-Begleiter beschworen und die
    /// gewünschte Stance gesetzt ist (Plugin.ChocoboCompanionSupport.Tick() übernimmt das eigentliche
    /// Beschwören/Stance-Setzen, hier wird nur beobachtet, wann das erledigt ist) - gibt aber
    /// spätestens nach ChocoboSummonWaitTimeout auf (z.B. falls keine Gysahl Greens vorhanden sind),
    /// statt die Quest-Automation endlos zu blockieren.
    /// </summary>
    private void UpdateSummoningChocobo()
    {
        var settled = !Plugin.UseChocoboCompanion || !Plugin.IsChocoboCompanionUnlocked();
        if (!settled)
        {
            // Erst fertig, wenn nicht (mehr) beschworen werden muss (auch bei weniger als 1 Minute
            // Restzeit, siehe ChocoboCompanionSupport.NeedsSummon) UND die Stance steht.
            settled = !ChocoboCompanionSupport.NeedsSummon
                && (!Plugin.IsChocoboCompanionSummoned()
                    || Plugin.ChocoboCompanionSupport.HasAppliedStanceForCurrentSummon
                    || !Plugin.IsChocoboStanceUnlocked(Plugin.ChocoboStance)
                    // Beritten wird die Stance erst beim nächsten Absteigen gesetzt (siehe ChocoboCompanionSupport.Tick) -
                    // bereits beschworen reicht dann, statt den Start dafür zu blockieren.
                    || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]);
        }

        if (settled || DateTime.UtcNow - stateEnteredAt > ChocoboSummonWaitTimeout)
        {
            state = State.Idle;
            StatusText = Loc.T("Automation gestartet...", "Automation started...");
        }
    }

    /// <summary>
    /// Muss jeden Frame (während das Overlay offen ist) mit den aktuell fehlenden Quests DER
    /// STARTZONE (siehe Start) und der aktuell aufgelösten Zone des Spielers aufgerufen werden.
    /// Startet nach und nach jede Quest per Questionable-IPC und wartet jeweils, bis Questionable
    /// sie abgeschlossen hat (oder nicht unterstützt), bevor die nächste angestoßen wird. Landet
    /// der Charakter zwischendurch (durch eine Quest) in einer anderen Zone, wird zwischen zwei
    /// Quests automatisch zur Startzone zurückgereist (siehe TryTravelHome), bevor es weitergeht.
    ///
    /// allToDoQuestEntries: alle (zonenunabhängigen) ToDo-Listen-Quests, egal in welcher Zone -
    /// wird NUR im restringierten Modus (siehe Start/restrictToQuestIds) gebraucht, um zu wissen, in
    /// welche Zone als Nächstes gereist werden muss, sobald hier in der aktuellen Zone keine ToDo-
    /// Quest mehr offen ist (siehe UpdateRestrictedIdle). Im normalen Modus unbenutzt, darf null sein.
    /// </summary>
    public void Update(IReadOnlyList<CollectibleEntry> missingQuestsInZone, uint currentEffectiveTerritoryId, IReadOnlyList<CollectibleEntry>? allToDoQuestEntries = null)
    {
        if (!IsActive)
            return;

        Plugin.ChocoboCompanionSupport.Tick(allowDismount: state == State.SummoningChocobo);

        try
        {
            switch (state)
            {
                case State.SummoningChocobo:
                    UpdateSummoningChocobo();
                    break;

                case State.Idle:
                    if (restrictToQuestIds != null)
                    {
                        UpdateRestrictedIdle(missingQuestsInZone, currentEffectiveTerritoryId, allToDoQuestEntries ?? Array.Empty<CollectibleEntry>());
                        break;
                    }

                    // Split-Hauptstädte (Ul'dah etc.) zählen als EIN Zuhause, egal in welchem
                    // Bezirk man gerade steht (siehe Plugin.GetSplitCityTerritories) - konsistent
                    // damit, wie missingQuestsInZone selbst zonenübergreifend zusammengestellt wird.
                    var isHome = !homeTerritoryId.HasValue
                                 || Plugin.GetSplitCityTerritories(homeTerritoryId.Value).Contains(currentEffectiveTerritoryId);
                    if (isHome)
                        TryStartNext(missingQuestsInZone);
                    else
                    {
                        Plugin.Log.Info($"[QuestAutomation] Idle: nicht daheim (homeTerritoryId={homeTerritoryId}, currentEffectiveTerritoryId={currentEffectiveTerritoryId}) - starte TryTravelHome.");
                        // Questionable erst hart stoppen, bevor wir selbst per Lifestream reisen -
                        // sonst kann es (z.B. weil es schon von sich aus zur nächsten erkannten
                        // Quest weiterlaufen will) mit unserer eigenen Teleport-Aktion kollidieren,
                        // was den Teleport-Erfolg/die Ankunftsprüfung verfälscht.
                        StopQuestionable();
                        TryTravelTo(homeTerritoryId!.Value, currentEffectiveTerritoryId, isReturnHome: true);
                    }
                    break;

                case State.TeleportingToQuestStart:
                    UpdateTeleportingToQuestStart();
                    break;

                case State.GateGuardRoute:
                    UpdateGateGuardRoute();
                    break;

                case State.WaitingForPickup:
                    if (isRunning.InvokeFunc())
                    {
                        state = State.Running;
                        StatusText = Loc.T($"Bearbeite: {currentQuestName}...", $"Processing: {currentQuestName}...");
                    }
                    else if (DateTime.UtcNow - stateEnteredAt > PickupTimeout)
                    {
                        SkipCurrentQuest(Loc.T("keine Reaktion von Questionable", "no response from Questionable"));
                    }
                    break;

                case State.Running:
                    if (isRunning.InvokeFunc())
                    {
                        runningWentFalseAt = null;
                    }
                    else
                    {
                        // Erst nach ein paar Sekunden DURCHGEHEND false als "fertig" werten, nicht
                        // schon beim ersten Mal: Bei quest-internen Zonenwechseln (z.B. "Tougher
                        // Than Leather" führt von Ul'dah nach Central Thanalan) meldet Questionable
                        // während des Ladebildschirms teils kurz IsRunning==false, obwohl die Quest
                        // gleich danach ganz normal weiterläuft. Ohne diese Gnadenfrist hätte das
                        // hier fälschlich "fertig" ausgelöst - und im nächsten Idle-Durchlauf, weil
                        // die aktuelle Zone dann nicht mehr die Startzone ist, eine Rückreise per
                        // Lifestream, die die laufende Quest komplett durcheinanderbringt (Charakter
                        // wird mitten aus der Quest herausteleportiert, während Questionable selbst
                        // munter weitermacht) und im schlimmsten Fall die ganze Automation stoppt.
                        runningWentFalseAt ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - runningWentFalseAt.Value > RunningFalseGracePeriod)
                        {
                            // Fertig (erledigt, abgebrochen oder Questionable ist von selbst gestoppt) -
                            // ob die Quest jetzt tatsächlich abgeschlossen ist, entscheidet die Liste
                            // beim nächsten Update: taucht sie noch auf, wird es (bis MaxAttemptsPerQuest) erneut versucht.
                            // Die Zonen-Prüfung (siehe oben) passiert erst im NÄCHSTEN Update-Aufruf im
                            // Idle-Zweig - nicht hier mitten in der Quest, sonst würde eine gerade von
                            // Questionable selbst durchgeführte Reise unterbrochen.
                            Plugin.Log.Info($"[QuestAutomation] Running->Idle: Quest '{currentQuestName}' ({currentQuestId}) fertig, currentEffectiveTerritoryId={currentEffectiveTerritoryId}.");
                            runningWentFalseAt = null;
                            state = State.Idle;
                            currentQuestId = null;
                        }
                    }
                    break;

                case State.TravelingHome:
                    UpdateTravelingHome(currentEffectiveTerritoryId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler bei der Questionable-Automation - wird gestoppt.");
            StatusText = Loc.T("Fehler bei Questionable - Automation gestoppt.", "Error talking to Questionable - automation stopped.");
            Stop();
        }
    }

    /// <summary>
    /// Restringierter Modus (siehe Start/restrictToQuestIds): läuft die ganze ToDo-Liste zonenüber-
    /// greifend ab, statt einfach alle in der aktuellen Zone fehlenden Quests anzunehmen. Ist noch
    /// eine ToDo-Quest in DIESER Zone offen, wie gewohnt über TryStartNext; ist keine mehr hier,
    /// aber anderswo, wird zuerst per Lifestream dorthin gereist (siehe TryTravelTo). Erst wenn
    /// GAR KEINE ToDo-Quest mehr offen ist, endet die Automation regulär.
    /// </summary>
    private void UpdateRestrictedIdle(IReadOnlyList<CollectibleEntry> missingQuestsInZone, uint currentEffectiveTerritoryId, IReadOnlyList<CollectibleEntry> allToDoQuestEntries)
    {
        var remaining = allToDoQuestEntries
            .Where(q => restrictToQuestIds!.Contains(q.Id) && !skippedQuestIds.Contains(q.Id))
            .ToList();

        if (remaining.Count == 0)
        {
            StatusText = Loc.T("Keine ToDo-Quests mehr übrig.", "No ToDo quests left.");
            Stop();
            return;
        }

        var inZoneIds = new HashSet<uint>(missingQuestsInZone.Select(q => q.Id));
        if (remaining.Any(q => inZoneIds.Contains(q.Id)))
        {
            TryStartNext(missingQuestsInZone);
            return;
        }

        // Keine der übrigen ToDo-Quests liegt in dieser Zone - zur Zone der ersten (in
        // Speicherreihenfolge) noch offenen reisen.
        var next = remaining[0];
        var targetTerritory = next.FlagTerritoryTypeId ?? next.TerritoryTypeId;
        if (targetTerritory == 0)
        {
            // Keine bekannte Zone (z.B. eine sehr alte ToDo-Liste ohne gespeicherte Zone) - kann so
            // nicht angelaufen werden, überspringen statt in eine Endlosschleife zu laufen.
            skippedQuestIds.Add(next.Id);
            return;
        }

        Plugin.Log.Info($"[QuestAutomation] UpdateRestrictedIdle: keine ToDo-Quest in aktueller Zone - reise zu {targetTerritory} für Quest {next.Id}.");
        StopQuestionable();
        TryTravelTo(targetTerritory, currentEffectiveTerritoryId, isReturnHome: false, failureSkipQuestId: next.Id);
    }

    /// <summary>
    /// Reist per kostenpflichtiger Lifestream-Teleport-Aktion in die angegebene Zone - im normalen
    /// Modus zurück zur Startzone (isReturnHome=true, siehe Start/homeTerritoryId), im
    /// restringierten ToDo-Modus zur Zone der nächsten noch offenen ToDo-Quest (siehe
    /// UpdateRestrictedIdle). Bewusst KEIN vorheriger Versuch über den kostenlosen Aethernetz-Sprung
    /// (siehe lifestreamTeleport-Feldkommentar) - der wäre an dieser Stelle immer aussichtslos, da
    /// diese Methode laut Update/State.Idle nur läuft, wenn die aktuelle Zone NICHT schon die
    /// Zielzone ist, ein Aethernetz-Sprung aber voraussetzt, bereits in deren Netzwerk-Reichweite zu
    /// sein. Schlägt auch der bezahlte Teleport ab (z.B. "Insufficient gil"), wird NICHT gewartet:
    /// im normalen Modus wird die aktuelle Zone einfach zur neuen "Startzone" (bestehendes
    /// Verhalten), im restringierten Modus wird nur die eine nicht erreichbare Quest übersprungen,
    /// der Rest der ToDo-Liste bleibt unangetastet. Nur ohne Lifestream selbst wird sofort gestoppt,
    /// da dann gar kein Teleport möglich wäre.
    /// </summary>
    private void TryTravelTo(uint targetTerritoryId, uint currentEffectiveTerritoryId, bool isReturnHome, uint? failureSkipQuestId = null)
    {
        if (!IsLifestreamAvailable())
        {
            Plugin.Log.Info("[QuestAutomation] TryTravelTo: Lifestream nicht verfügbar - Automation wird gestoppt.");
            StatusText = Loc.T(
                "Kann nicht zur Zielzone reisen (Lifestream nicht gefunden) - Automation gestoppt.",
                "Can't travel to the target zone (Lifestream not found) - automation stopped.");
            Stop();
            return;
        }

        var targetDistrictIds = Plugin.GetSplitCityTerritories(targetTerritoryId);
        Plugin.Log.Info($"[QuestAutomation] TryTravelTo: targetTerritoryId={targetTerritoryId}, targetDistrictIds=[{string.Join(",", targetDistrictIds)}], currentEffectiveTerritoryId={currentEffectiveTerritoryId}.");

        // Manche geteilte Hauptstädte (z.B. Ul'dah) haben nur EINEN großen Aetheryten für die ganze
        // Stadt, physisch in nur einem Bezirk - daher über alle Bezirke der Zielzone suchen, nicht
        // nur exakt die eine.
        uint? mainAetheryteId = null;
        foreach (var territory in targetDistrictIds)
        {
            mainAetheryteId = Plugin.FindUnlockedMainAetheryteId(territory);
            if (mainAetheryteId != null)
                break;
        }
        Plugin.Log.Info($"[QuestAutomation] TryTravelTo: mainAetheryteId={mainAetheryteId}.");

        var accepted = mainAetheryteId.HasValue && lifestreamTeleport.InvokeFunc(mainAetheryteId.Value, (byte)0);
        Plugin.Log.Info($"[QuestAutomation] TryTravelTo: bezahlter Teleport zu {mainAetheryteId} -> accepted={accepted}.");
        if (!accepted)
        {
            if (isReturnHome)
            {
                // Kein Teleport möglich (kein Aetheryte dort freigeschaltet, oder der Teleport wurde
                // abgelehnt, z.B. "Insufficient gil") - statt endlos zu warten oder ganz zu stoppen,
                // wird die aktuelle Zone einfach zur neuen "Startzone": die Automation macht direkt
                // hier mit den dortigen fehlenden Quests weiter.
                homeTerritoryId = currentEffectiveTerritoryId;
                StatusText = Loc.T(
                    "Rückreise nicht möglich (z.B. zu wenig Gil) - mache stattdessen hier weiter.",
                    "Trip back not possible (e.g. not enough gil) - continuing from here instead.");
            }
            else
            {
                if (failureSkipQuestId.HasValue)
                    skippedQuestIds.Add(failureSkipQuestId.Value);
                StatusText = Loc.T(
                    "Reise zur nächsten ToDo-Quest nicht möglich (z.B. zu wenig Gil) - übersprungen.",
                    "Trip to the next ToDo quest not possible (e.g. not enough gil) - skipped.");
            }

            state = State.Idle;
            return;
        }

        travelTargetTerritoryId = targetTerritoryId;
        travelIsReturnHome = isReturnHome;
        travelFailureSkipQuestId = failureSkipQuestId;
        state = State.TravelingHome;
        stateEnteredAt = DateTime.UtcNow;
        travelHomeFinishedAt = null;
        var teleportZoneName = Plugin.GetZoneName(targetTerritoryId);
        StatusText = Loc.T($"Teleportiere nach {teleportZoneName}...", $"Teleporting to {teleportZoneName}...");
    }

    private void UpdateTravelingHome(uint currentEffectiveTerritoryId)
    {
        // Siehe TeleportMinimumTravelDuration-Kommentar - Lifestream.IsBusy() allein wird der
        // tatsächlichen Reisedauer (echte Zauberzeit + Ladebildschirm) nicht zuverlässig gerecht,
        // daher zusätzlich eine Mindestdauer abwarten, bevor "nicht mehr beschäftigt" überhaupt als
        // "fertig" gewertet wird.
        var stillTraveling = lifestreamIsBusy.InvokeFunc() || DateTime.UtcNow - stateEnteredAt < TeleportMinimumTravelDuration;

        if (!stillTraveling)
        {
            // Kurz warten, bis sich Plugin.ClientState.TerritoryType tatsächlich auf die neue Zone
            // aktualisiert hat (siehe TravelHomeSettleDelay) - sonst hält der nächste Update-Aufruf
            // die Zone noch für die alte und reist prompt wieder los.
            travelHomeFinishedAt ??= DateTime.UtcNow;
            if (DateTime.UtcNow - travelHomeFinishedAt.Value < TravelHomeSettleDelay)
                return;

            travelHomeFinishedAt = null;

            // Lifestream kann eine Reise auch NACH dem angenommenen Auftrag noch asynchron
            // ablehnen (z.B. ein Ziel, das laut IsAetheryteUnlocked zwar freigeschaltet ist, aber von
            // Lifestream selbst nicht gefunden wird) - dabei wird IsBusy() genauso false wie bei
            // einer echten, erfolgreichen Ankunft. Ohne diese Prüfung würde der nächste Idle-
            // Durchlauf die (unveränderte) Zone weiter als "nicht angekommen" erkennen und denselben,
            // deterministisch wieder scheiternden Reiseversuch endlos wiederholen, statt jemals
            // weiterzumachen.
            var arrived = travelTargetTerritoryId.HasValue && Plugin.GetSplitCityTerritories(travelTargetTerritoryId.Value).Contains(currentEffectiveTerritoryId);
            Plugin.Log.Info($"[QuestAutomation] UpdateTravelingHome: Lifestream fertig, travelTargetTerritoryId={travelTargetTerritoryId}, currentEffectiveTerritoryId={currentEffectiveTerritoryId}, arrived={arrived}, travelHomeFailureCount={travelHomeFailureCount}.");
            if (!arrived && ++travelHomeFailureCount <= MaxTravelHomeAttempts)
            {
                // Noch Versuche übrig - im nächsten Idle-Durchlauf erneut versuchen.
                state = State.Idle;
                return;
            }

            if (!arrived)
            {
                if (travelIsReturnHome)
                {
                    homeTerritoryId = currentEffectiveTerritoryId;
                    StatusText = Loc.T(
                        "Rückreise wiederholt gescheitert - mache stattdessen hier weiter.",
                        "Trip back repeatedly failed - continuing from here instead.");
                }
                else
                {
                    if (travelFailureSkipQuestId.HasValue)
                        skippedQuestIds.Add(travelFailureSkipQuestId.Value);
                    StatusText = Loc.T(
                        "Reise zur nächsten ToDo-Quest wiederholt gescheitert - übersprungen.",
                        "Trip to the next ToDo quest repeatedly failed - skipped.");
                }
            }

            travelHomeFailureCount = 0;
            state = State.Idle;
            return;
        }

        travelHomeFinishedAt = null;
        if (DateTime.UtcNow - stateEnteredAt > TravelHomeTimeout)
        {
            StopLifestream();
            state = State.Idle;
            StatusText = Loc.T("Reise dauert zu lange - abgebrochen", "Trip took too long - aborted");
        }
    }

    private void TryStartNext(IReadOnlyList<CollectibleEntry> missingQuestsInZone)
    {
        var pool = restrictToQuestIds != null
            ? missingQuestsInZone.Where(q => restrictToQuestIds.Contains(q.Id))
            : missingQuestsInZone;
        var candidates = pool.Where(q => !skippedQuestIds.Contains(q.Id)).ToList();
        if (candidates.Count == 0)
        {
            StatusText = restrictToQuestIds != null
                ? Loc.T("Keine ToDo-Quests mehr übrig.", "No ToDo quests left.")
                : Loc.T("Keine Quests mehr übrig.", "No quests left.");
            Stop();
            return;
        }

        // Die räumlich nächstgelegene annehmbare Quest zuerst, statt stur der Zonen-Listenreihenfolge
        // zu folgen - sonst läuft der Charakter quer durch die Zone, obwohl die nächste Quest direkt
        // neben der gerade abgeschlossenen liegt (die Vergabe-Position wird aus der Kartenkoordinate
        // zurückgerechnet, siehe Plugin.ResolveWorldPositionFromMapCoords). Quests ohne auflösbare
        // Position fallen ans Ende, statt die Sortierung ganz abzubrechen.
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var next = candidates
            .OrderBy(q => q.HasVendorLocation
                ? Plugin.ResolveWorldPositionFromMapCoords(q.MapId, q.VendorMapX, q.VendorMapY) is { } questPos
                    ? Vector3.Distance(playerPos, questPos)
                    : float.MaxValue
                : float.MaxValue)
            .First();

        var attempts = attemptCounts.GetValueOrDefault(next.Id, 0) + 1;
        attemptCounts[next.Id] = attempts;
        if (attempts > MaxAttemptsPerQuest)
        {
            skippedQuestIds.Add(next.Id);
            StatusText = Loc.T($"Übersprungen (zu oft versucht): {next.Name}", $"Skipped (too many attempts): {next.Name}");
            return;
        }

        // Siehe PreStartTeleportAetheryteNames-Kommentar - für hinterlegte Quests erst per Lifestream
        // zu einem bekannten Ätheryten teleportieren, der eigentliche Questionable-Start (siehe
        // BeginQuestionableStart) folgt erst danach in UpdateTeleportingToQuestStart. Klappt der
        // Teleport nicht (Lifestream fehlt/Ätheryte nicht auflösbar/noch nicht freigeschaltet), ganz
        // normal direkt weiter wie bisher.
        if (PreStartTeleportAetheryteNames.TryGetValue(next.Name, out var teleportAetheryteName)
            && TryTeleportToQuestStart(teleportAetheryteName, next))
            return;

        BeginQuestionableStart(next);
    }

    /// <summary>Siehe PreStartTeleportAetheryteNames-Kommentar.</summary>
    private bool TryTeleportToQuestStart(string aetheryteName, CollectibleEntry quest)
    {
        if (!IsLifestreamAvailable())
        {
            Plugin.Log.Info($"[QuestAutomation] TryTeleportToQuestStart({quest.Name}): Lifestream-IPC nicht verfügbar - teleportiere nicht.");
            return false;
        }

        var aetheryteId = Plugin.ResolveAetheryteIdByName(aetheryteName);
        if (aetheryteId == null)
        {
            Plugin.Log.Warning($"[QuestAutomation] TryTeleportToQuestStart({quest.Name}): Ätheryte \"{aetheryteName}\" nicht im Aetheryte-Sheet gefunden (Name/Sprache falsch?) - teleportiere nicht.");
            return false;
        }

        if (!lifestreamTeleport.InvokeFunc(aetheryteId.Value, (byte)0))
        {
            Plugin.Log.Warning($"[QuestAutomation] TryTeleportToQuestStart({quest.Name}): Lifestream.Teleport zu \"{aetheryteName}\" (Id={aetheryteId.Value}) abgelehnt (noch nicht freigeschaltet?) - teleportiere nicht.");
            return false;
        }

        pendingQuestEntryForTeleport = quest;
        preStartTeleportHasSeenLoadingScreen = false;
        state = State.TeleportingToQuestStart;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = Loc.T($"Teleportiere zu {aetheryteName}, dann: {quest.Name}...", $"Teleporting to {aetheryteName}, then: {quest.Name}...");
        return true;
    }

    private void UpdateTeleportingToQuestStart()
    {
        if (pendingQuestEntryForTeleport == null)
        {
            state = State.Idle;
            return;
        }

        var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (loading)
            preStartTeleportHasSeenLoadingScreen = true;

        if (loading || !preStartTeleportHasSeenLoadingScreen)
        {
            if (DateTime.UtcNow - stateEnteredAt > PreStartTeleportTimeout)
                ProceedAfterPreStartTeleport(pendingQuestEntryForTeleport);

            return;
        }

        if (DateTime.UtcNow - stateEnteredAt < PreStartTeleportSettleDelay)
            return;

        var quest = pendingQuestEntryForTeleport;
        pendingQuestEntryForTeleport = null;
        ProceedAfterPreStartTeleport(quest);
    }

    /// <summary>
    /// Nach dem Pre-Start-Teleport (siehe PreStartTeleportAetheryteNames) ist der Charakter immer
    /// abgestiegen (Lifestream-Teleport) - bei ALLEN drei hinterlegten Quests erst ein Mount anfordern
    /// (falls möglich), bevor es weitergeht (Nutzeranforderung: "soll aufmounten und dann reiten",
    /// nicht zu Fuß laufen). Manche dieser Quests liegen zusätzlich hinter einem Tor-Wachposten
    /// (siehe GateGuardRoutes), der erst noch per BeginGateGuardApproach geöffnet werden muss, bevor
    /// Questionable überhaupt starten kann - alle anderen gehen nach dem Mount-Versuch direkt weiter.
    /// </summary>
    private void ProceedAfterPreStartTeleport(CollectibleEntry quest)
    {
        pendingQuestEntryForTeleport = quest;
        activeGateGuardRoute = GateGuardRoutes.TryGetValue(quest.Name, out var gateRoute) ? gateRoute : null;
        state = State.GateGuardRoute;
        stateEnteredAt = DateTime.UtcNow;

        if (Plugin.TryRequestAetheryteMount())
        {
            gateGuardPhase = GateGuardPhase.Mounting;
            gateGuardLastMountAttemptAt = DateTime.UtcNow;
            StatusText = Loc.T($"Rufe Mount, dann: {quest.Name}...", $"Summoning mount, then: {quest.Name}...");
            return;
        }

        ContinueAfterMountRequest(quest);
    }

    /// <summary>Siehe ProceedAfterPreStartTeleport-Kommentar - nach dem (versuchten) Mount-Ruf entweder zum Wachposten (GateGuardRoutes) oder, falls keiner hinterlegt ist, direkt zu Questionable.</summary>
    private void ContinueAfterMountRequest(CollectibleEntry quest)
    {
        if (activeGateGuardRoute is { } route)
        {
            BeginGateGuardApproach(route, quest);
            return;
        }

        pendingQuestEntryForTeleport = null;
        BeginQuestionableStart(quest);
    }

    private void BeginGateGuardApproach((Vector3 Position, string NpcName) route, CollectibleEntry quest)
    {
        gateGuardPhase = GateGuardPhase.Approaching;
        gateGuardHasSeenPathRunning = false;
        stateEnteredAt = DateTime.UtcNow;

        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var accepted = false;
        if (mounted && Plugin.CanFly)
            accepted = pathfindAndMoveCloseTo.InvokeFunc(route.Position, true, GateGuardArrivalTolerance);
        if (!accepted)
            accepted = pathfindAndMoveCloseTo.InvokeFunc(route.Position, false, GateGuardArrivalTolerance);

        if (!accepted)
        {
            Plugin.Log.Warning($"[QuestAutomation] BeginGateGuardApproach({quest.Name}): vnavmesh lehnt den Laufweg zum Wachposten ab - starte Questionable trotzdem direkt.");
            activeGateGuardRoute = null;
            pendingQuestEntryForTeleport = null;
            BeginQuestionableStart(quest);
            return;
        }

        StatusText = Loc.T($"Laufe zum Wachposten, dann: {quest.Name}...", $"Walking to the gate guard, then: {quest.Name}...");
    }

    private void UpdateGateGuardRoute()
    {
        if (pendingQuestEntryForTeleport == null)
        {
            state = State.Idle;
            return;
        }

        // Mounting läuft für ALLE drei Quests (siehe ProceedAfterPreStartTeleport-Kommentar), auch
        // für die ohne hinterlegte GateGuardRoutes - activeGateGuardRoute ist dann bewusst noch null,
        // erst die übrigen Phasen (Approaching/Interacting/WaitingForLoadingScreen) brauchen sie
        // zwingend.
        if (gateGuardPhase == GateGuardPhase.Mounting)
        {
            if (Plugin.Condition[ConditionFlag.Mounted])
            {
                ContinueAfterMountRequest(pendingQuestEntryForTeleport);
                return;
            }

            if (DateTime.UtcNow - stateEnteredAt > GateGuardMountWaitTimeout)
            {
                Plugin.Log.Warning($"[QuestAutomation] UpdateGateGuardRoute({pendingQuestEntryForTeleport.Name}): nach {GateGuardMountWaitTimeout.TotalSeconds}s nicht aufgesessen - mache trotzdem zu Fuß weiter.");
                ContinueAfterMountRequest(pendingQuestEntryForTeleport);
                return;
            }

            // Siehe GateGuardMountRetryInterval-Kommentar - ein einzelner Ruf verhallt nach dem
            // Teleport manchmal wortlos, daher in Abständen erneut versuchen statt nur einmal zu
            // rufen und passiv auf Mounted zu warten.
            if (DateTime.UtcNow - gateGuardLastMountAttemptAt > GateGuardMountRetryInterval)
            {
                gateGuardLastMountAttemptAt = DateTime.UtcNow;
                Plugin.TryRequestAetheryteMount();
            }

            return;
        }

        if (activeGateGuardRoute is not { } route)
        {
            state = State.Idle;
            return;
        }

        switch (gateGuardPhase)
        {
            case GateGuardPhase.Approaching:
            {
                if (pathIsRunning.InvokeFunc())
                {
                    gateGuardHasSeenPathRunning = true;
                    if (DateTime.UtcNow - stateEnteredAt > GateGuardStepMaxDuration)
                        FailGateGuardRoute("Laufweg zum Wachposten dauert zu lange", "Path to the gate guard is taking too long");

                    return;
                }

                if (!gateGuardHasSeenPathRunning)
                    return;

                gateGuardPhase = GateGuardPhase.Interacting;
                gateGuardInteractAttempts = 0;
                gateGuardDismountedAt = null;
                stateEnteredAt = DateTime.UtcNow;
                StatusText = Loc.T($"Spreche mit dem Wachposten: {pendingQuestEntryForTeleport.Name}...", $"Talking to the gate guard: {pendingQuestEntryForTeleport.Name}...");
                return;
            }

            case GateGuardPhase.Interacting:
            {
                if (Plugin.TryConfirmSelectYesno())
                {
                    gateGuardPhase = GateGuardPhase.WaitingForLoadingScreen;
                    gateGuardHasSeenLoadingScreen = false;
                    gateGuardLoadingEndedAt = null;
                    stateEnteredAt = DateTime.UtcNow;
                    StatusText = Loc.T($"Warte auf Ladeanimation: {pendingQuestEntryForTeleport.Name}...", $"Waiting for the loading animation: {pendingQuestEntryForTeleport.Name}...");
                    return;
                }

                // Zwischen Interact und der Ja/Nein-Abfrage kommt erst noch mehrzeiliger Dialogtext
                // (siehe Plugin.TryAdvanceTalkDialogue-Kommentar) - jeden Frame erneut versucht, bis
                // keiner mehr offen ist.
                Plugin.TryAdvanceTalkDialogue();

                if (Plugin.Condition[ConditionFlag.Mounted])
                {
                    Plugin.TryDismount();
                    gateGuardDismountedAt = null;
                    return;
                }

                gateGuardDismountedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - gateGuardDismountedAt.Value < GateGuardDismountSettleDelay)
                    return;

                if (gateGuardInteractAttempts > 0 && DateTime.UtcNow - gateGuardInteractedAt < GateGuardInteractRetryInterval)
                    return;

                if (gateGuardInteractAttempts >= GateGuardMaxInteractAttempts)
                {
                    FailGateGuardRoute("Wachposten reagiert nicht", "The gate guard does not respond");
                    return;
                }

                if (DateTime.UtcNow - stateEnteredAt > GateGuardStepMaxDuration)
                {
                    FailGateGuardRoute("Wachposten nicht gefunden", "Gate guard not found");
                    return;
                }

                var npc = FindNearestGateGuardObject(route.Position, GateGuardObjectSearchRadius, route.NpcName);
                if (npc == null)
                    return;

                if (!Plugin.IsCurrentTarget(npc))
                {
                    Plugin.SetTarget(npc);
                    return;
                }

                Plugin.Log.Info($"[QuestAutomation] UpdateGateGuardRoute({pendingQuestEntryForTeleport.Name}): interagiere mit '{npc.Name}'.");
                Plugin.InteractWithGameObject(npc);
                gateGuardInteractAttempts++;
                gateGuardInteractedAt = DateTime.UtcNow;
                return;
            }

            case GateGuardPhase.WaitingForLoadingScreen:
            {
                // Falls das Fenster ein zweites Mal kommt (siehe NoFlyAreaExit.UpdateWaitingForExit).
                Plugin.TryConfirmSelectYesno();

                var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
                if (loading)
                    gateGuardHasSeenLoadingScreen = true;

                if (loading || !gateGuardHasSeenLoadingScreen)
                {
                    gateGuardLoadingEndedAt = null;
                    if (DateTime.UtcNow - stateEnteredAt > GateGuardLoadingTimeout)
                        ResumeAfterGateGuardRoute();

                    return;
                }

                // Siehe GateDismountSettleDelay-Kommentar in AetherCurrentAutomation - direkt nach
                // Ende der Ladeanimation ist der Charakter noch nicht wieder voll "steuerbar".
                gateGuardLoadingEndedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - gateGuardLoadingEndedAt.Value < GateGuardDismountSettleDelay)
                    return;

                ResumeAfterGateGuardRoute();
                return;
            }
        }
    }

    /// <summary>Nutzeranforderung: nach der Ladeanimation nochmal explizit abmounten, bevor Questionable (das selbst wieder zur Quest läuft) gestartet wird.</summary>
    private void ResumeAfterGateGuardRoute()
    {
        activeGateGuardRoute = null;
        if (Plugin.Condition[ConditionFlag.Mounted])
            Plugin.TryDismount();

        var quest = pendingQuestEntryForTeleport!;
        pendingQuestEntryForTeleport = null;
        BeginQuestionableStart(quest);
    }

    private void FailGateGuardRoute(string de, string en)
    {
        pathStop.InvokeAction();
        activeGateGuardRoute = null;
        if (pendingQuestEntryForTeleport != null)
            skippedQuestIds.Add(pendingQuestEntryForTeleport.Id);

        pendingQuestEntryForTeleport = null;
        StatusText = Loc.T($"Übersprungen ({de})", $"Skipped ({en})");
        state = State.Idle;
    }

    /// <summary>Wie ChocobokeepAutomation.FindNearestChocobokeepObject, aber ohne ObjectKind-Einschränkung auf EventNpc/EventObj - ein Wachposten-NPC ist ein echter Charakter (ICharacter), nicht statisch.</summary>
    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestGateGuardObject(Vector3 nearPosition, float maxDistance, string npcName)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? nearest = null;
        var bestDistance = maxDistance;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (!obj.IsTargetable)
                continue;
            if (!string.Equals(obj.Name.TextValue, npcName, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>Tatsächlicher Questionable-Start, unverändert zum bisherigen Ende von TryStartNext - jetzt eigenständig, damit State.TeleportingToQuestStart ihn nach Ankunft ebenfalls aufrufen kann.</summary>
    private void BeginQuestionableStart(CollectibleEntry next)
    {
        // Questionable erwartet die "echte" Quest-ID des Spielclients (16 Bit, wie sie z.B. auch
        // QuestManager.IsQuestComplete nutzt), nicht die volle Lumina-Excel-RowId (die ab 0x10000
        // zählt) - sonst wird jede einzelne Quest fälschlich als "unbekannt" abgelehnt.
        var accepted = startSingleQuest.InvokeFunc(((ushort)next.Id).ToString());
        if (!accepted)
        {
            skippedQuestIds.Add(next.Id);
            notSupportedQuestIds.Add(next.Id);
            StatusText = Loc.T($"Nicht unterstützt: {next.Name}", $"Not supported: {next.Name}");
            state = State.Idle;
            return;
        }

        currentQuestId = next.Id;
        currentQuestName = next.Name;
        state = State.WaitingForPickup;
        stateEnteredAt = DateTime.UtcNow;
        runningWentFalseAt = null;
        StatusText = Loc.T($"Starte: {next.Name}...", $"Starting: {next.Name}...");
    }

    private void SkipCurrentQuest(string reason)
    {
        if (currentQuestId.HasValue)
            skippedQuestIds.Add(currentQuestId.Value);

        StatusText = Loc.T($"Übersprungen ({reason})", $"Skipped ({reason})");
        state = State.Idle;
        currentQuestId = null;
        runningWentFalseAt = null;
    }
}
