using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NuclearOption.Networking;

namespace NO_PTA;

[HarmonyPatch]
internal static class HarmonyPatches
{
    private static ConditionalWeakTable<Turret, BlacklistEntry> _blacklistedTurrets = new();
    private static Aircraft? _stateAircraft;
    
    private static bool IsAutoFireBlocked(Turret turret) =>
        turret != null && _blacklistedTurrets.TryGetValue(turret, out _);
    
    private static void SetAutoFireBlocked(Turret turret, bool blocked)
    {
        if (turret == null)
            return;
        
        _blacklistedTurrets.Remove(turret);
        
        if (blocked)
            _blacklistedTurrets.Add(turret, new BlacklistEntry());
    }
    
    private static void ToggleCurrentTurretAutoFire()
    {
        if (!GameManager.GetLocalAircraft(out var aircraft) || aircraft == null ||
            aircraft.weaponManager == null) return;
        
        CheckAircraftState(aircraft);
        
        var station = aircraft.weaponManager.currentWeaponStation;
        
        if (station == null || !station.HasTurret() || station.Turrets == null)
            return;
        
        var foundTurret = false;
        var allBlocked = true;
        
        foreach (var turret in station.Turrets.OfType<Turret>())
        {
            foundTurret = true;
            
            if (!IsAutoFireBlocked(turret))
                allBlocked = false;
        }
        
        if (!foundTurret)
            return;
        
        var blocked = !allBlocked;
        
        foreach (var turret in station.Turrets.OfType<Turret>())
            SetAutoFireBlocked(turret, blocked);
        
        TurretBlacklistHud.Refresh();
        
        var weaponName = station.WeaponInfo != null && !string.IsNullOrEmpty(station.WeaponInfo.weaponName)
            ? station.WeaponInfo.weaponName
            : "Selected turret";
        var report = blocked
            ? weaponName + " auto fire blacklisted"
            : weaponName + " removed from auto fire blacklist";
        
        SceneSingleton<AircraftActionsReport>.i.ReportText(report, 4f);
    }
    
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.PlayerControls))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void PlayerControlsPrefix(PilotPlayerState __instance)
    {
        if (!GameManager.flightControlsEnabled || __instance.pilotStrength < 0.2f)
            return;
        
        var inputPlayer = __instance.player;
        
        if (inputPlayer == null || !inputPlayer.GetButtonTimedPressDown("Turret Control", PlayerSettings.pressDelay))
            return;
        
        if (inputPlayer.GetButton("Axis Modifier"))
            ClearCurrentTurretBlacklist();
        else
            ToggleCurrentTurretAutoFire();
    }
    
    [HarmonyPatch(typeof(Turret), nameof(Turret.FixedUpdate))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> TurretTranspiler(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {
        var matcher = new CodeMatcher(instructions, generator);
        var elevationTransformField = AccessTools.Field(typeof(Turret), nameof(Turret.elevationTransform));
        var blacklistCheck = AccessTools.Method(typeof(HarmonyPatches), nameof(IsAutoFireBlocked));
        
        // Find Transform elevationTransform = this.elevationTransform; near end, check if turret is blacklisted
        // if so, exit early without further checks on whether to allow it to fire
        
        matcher.MatchForward(true,
            new CodeMatch(OpCodes.Ldarg_0),
            new CodeMatch(OpCodes.Ldfld, elevationTransformField),
            new CodeMatch(OpCodes.Stloc_1)
        ).ThrowIfInvalid("Couldn't find Turret.FixedUpdate pattern.");
        
        matcher.Advance(1);
        
        var continueLabel = generator.DefineLabel();
        matcher.Instruction.labels.Add(continueLabel);
        
        matcher.Insert(
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, blacklistCheck),
            new CodeInstruction(OpCodes.Brfalse, continueLabel),
            new CodeInstruction(OpCodes.Ret));
        
        return matcher.InstructionEnumeration();
    }
    
    [HarmonyPatch(typeof(PilotPlayerState), nameof(PilotPlayerState.EnterState))]
    [HarmonyPostfix]
    private static void EnterStatePostfix(Pilot? pilot)
    {
        CheckAircraftState(pilot?.aircraft);
    }
    
    [HarmonyPatch(typeof(Player), nameof(Player.RemoveAircraft))]
    [HarmonyPostfix]
    private static void RemoveAircraftPostfix(Aircraft aircraft)
    {
        if (ReferenceEquals(_stateAircraft, aircraft))
            ResetState();
    }
    
    private static void CheckAircraftState(Aircraft? aircraft)
    {
        if (ReferenceEquals(_stateAircraft, aircraft))
            return;
        
        _stateAircraft = aircraft;
        _blacklistedTurrets = new ConditionalWeakTable<Turret, BlacklistEntry>();
        
        TurretBlacklistHud.Refresh();
    }
    
    internal static void ResetState()
    {
        _stateAircraft = null;
        _blacklistedTurrets = new ConditionalWeakTable<Turret, BlacklistEntry>();
        
        TurretBlacklistHud.Refresh();
    }
    
    private static void ClearCurrentTurretBlacklist()
    {
        if (!GameManager.GetLocalAircraft(out var aircraft) || aircraft == null)
            return;
        
        CheckAircraftState(aircraft);
        
        _blacklistedTurrets = new ConditionalWeakTable<Turret, BlacklistEntry>();
        
        TurretBlacklistHud.Refresh();
        SceneSingleton<AircraftActionsReport>.i.ReportText("Turret auto fire blacklist cleared", 4f);
    }
    
    internal static bool TryGetBlacklistHudText(out string text)
    {
        text = string.Empty;
        
        if (_stateAircraft == null || !GameManager.GetLocalAircraft(out var aircraft) ||
            aircraft == null || !ReferenceEquals(_stateAircraft, aircraft))
            return false;
        
        var names = new List<string>();
        
        foreach (var station in aircraft.weaponStations)
        {
            if (station == null || !station.HasTurret() || station.Turrets == null)
                continue;
            
            var blocked = false;
            
            foreach (var turret in station.Turrets.OfType<Turret>())
            {
                if (!IsAutoFireBlocked(turret))
                    continue;
                
                blocked = true;
                break;
            }
            
            if (!blocked)
                continue;
            
            var weaponName = station.WeaponInfo?.weaponName;
            
            if (string.IsNullOrEmpty(weaponName))
                weaponName = station.WeaponInfo != null
                    ? station.WeaponInfo.name
                    : $"Station {station.Number + 1}";
            
            if (weaponName!.Length > 20)
                weaponName = weaponName.Substring(0, 17) + "...";
            
            names.Add(weaponName);
        }
        
        if (names.Count == 0)
            return false;
        
        text = "<b>No auto:</b>\n" + string.Join("\n", names);
        
        return true;
    }
    
    private sealed class BlacklistEntry
    {
    }
}