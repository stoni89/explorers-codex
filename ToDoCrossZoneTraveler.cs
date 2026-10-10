using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;

namespace TheExplorersCodex;

/// <summary>
/// Nutzeranforderung: "Die ToDo List kann zonenübergreifend sein, also muss ggf. in die nächste Zone
/// teleportiert werden." Anders als QuestAutomation (die ihre eigene Zonenwechsel-Logik über
/// TryTravelTo/State.TravelingHome mitbringt) haben die sechs "einfachen" Automationen (Aetheryte/
/// HuntingLog/AetherCurrent/Sightseeing/Chocobokeep/TripleTriad) KEINE eigene Reise-Logik - sie
/// arbeiten pro Tick rein auf der ihnen von Plugin.UpdateZoneAutomations übergebenen Zonen-Liste.
///
/// Diese Klasse übernimmt das Reisen GENERISCH für alle sechs, EINMAL, von außen: findet (wenn eine
/// dieser Automationen im ToDo-Modus läuft, siehe *.RestrictedToToDo, aber in der aktuellen Zone
/// nichts mehr zu tun hat) den nächsten noch offenen ToDo-Eintrag IHRES Typs in einer anderen Zone und
/// teleportiert per Lifestream dorthin. Während der Reise (Teleport-Ladebildschirm + kurze
/// Einpendelzeit danach) wird IsBusy true - Plugin.UpdateZoneAutomations ruft dann KEINE der sechs
/// Update()-Methoden auf, bis die Reise fertig ist (identisches Prinzip zu NoFlyAreaExit.IsBusy).
///
/// Da die Auto-Knopfreihe im Overlay immer nur EINE Automation gleichzeitig laufen lässt (siehe
/// CodexOverlayWindow.anyRunning), reicht eine einzige geteilte Instanz für alle sechs Typen.
/// </summary>
public sealed class ToDoCrossZoneTraveler
{
    private static readonly TimeSpan TravelRetryCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoadingSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoadingTimeout = TimeSpan.FromSeconds(60);

    private readonly ICallGateSubscriber<uint, byte, bool> lifestreamTeleport;

    private bool isTraveling;
    private DateTime travelStartedAt;
    private bool hasSeenLoadingScreen;
    private DateTime? loadingEndedAt;
    private DateTime lastAttemptAt = DateTime.MinValue;
    private uint? lastFailedTargetTerritory;

    public ToDoCrossZoneTraveler()
    {
        lifestreamTeleport = Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
    }

    public bool IsBusy => isTraveling;
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>
    /// Jeden Tick VOR den sechs einfachen Automationen aus Plugin.UpdateZoneAutomations aufgerufen,
    /// jeweils einmal pro Typ mit dessen (schon auf die ToDo-Liste eingeschränkter) Zonen-Liste.
    /// Läuft eine Reise bereits, wird nur deren Fortschritt geprüft (die übergebenen Parameter werden
    /// dann ignoriert - es kann wie oben beschrieben ohnehin nur eine Automation gleichzeitig aktiv sein).
    /// </summary>
    public void Advance(CollectibleType type, bool isActive, bool restrictedToToDo, IReadOnlyList<CollectibleEntry> missingInZoneRestricted,
        IReadOnlyList<CollectibleEntry> toDoEntries, uint currentEffectiveTerritoryId)
    {
        if (isTraveling)
        {
            UpdateTraveling();
            return;
        }

        if (!isActive || !restrictedToToDo || missingInZoneRestricted.Count > 0)
            return;

        if (DateTime.UtcNow - lastAttemptAt < TravelRetryCooldown)
            return;

        var currentDistricts = Plugin.GetSplitCityTerritories(currentEffectiveTerritoryId);
        var next = toDoEntries.FirstOrDefault(e => e.Type == type
            && (e.FlagTerritoryTypeId ?? e.TerritoryTypeId) != 0
            && !currentDistricts.Contains(e.FlagTerritoryTypeId ?? e.TerritoryTypeId));
        if (next == null)
            return;

        var targetTerritoryId = next.FlagTerritoryTypeId ?? next.TerritoryTypeId;
        lastAttemptAt = DateTime.UtcNow;
        if (targetTerritoryId == lastFailedTargetTerritory)
            return; // schon mal fehlgeschlagen (z.B. kein Ätheryte dort freigeschaltet) - nicht endlos erneut versuchen

        if (!TryStartTravel(targetTerritoryId))
            lastFailedTargetTerritory = targetTerritoryId;
    }

    private bool TryStartTravel(uint targetTerritoryId)
    {
        try
        {
            if (!lifestreamTeleport.HasFunction)
                return false;
        }
        catch
        {
            return false;
        }

        uint? mainAetheryteId = null;
        foreach (var territory in Plugin.GetSplitCityTerritories(targetTerritoryId))
        {
            mainAetheryteId = Plugin.FindUnlockedMainAetheryteId(territory);
            if (mainAetheryteId != null)
                break;
        }

        if (mainAetheryteId == null || !lifestreamTeleport.InvokeFunc(mainAetheryteId.Value, (byte)0))
            return false;

        isTraveling = true;
        travelStartedAt = DateTime.UtcNow;
        hasSeenLoadingScreen = false;
        loadingEndedAt = null;
        lastFailedTargetTerritory = null;
        var zoneName = Plugin.GetZoneName(targetTerritoryId);
        StatusText = Loc.T($"Reise zur nächsten ToDo-Zone ({zoneName})...", $"Traveling to the next ToDo zone ({zoneName})...");
        return true;
    }

    private void UpdateTraveling()
    {
        var loading = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (loading)
            hasSeenLoadingScreen = true;

        if (loading || !hasSeenLoadingScreen)
        {
            if (DateTime.UtcNow - travelStartedAt > LoadingTimeout)
                isTraveling = false;

            return;
        }

        loadingEndedAt ??= DateTime.UtcNow;
        if (DateTime.UtcNow - loadingEndedAt.Value < LoadingSettleDelay)
            return;

        isTraveling = false;
    }

    /// <summary>Beim Stoppen irgendeiner Automation (Nutzer-Klick auf Stop) sicherheitshalber zurücksetzen.</summary>
    public void Reset()
    {
        isTraveling = false;
        lastFailedTargetTerritory = null;
        StatusText = string.Empty;
    }
}
