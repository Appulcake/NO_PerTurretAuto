using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NO_PTA;

[HarmonyPatch]
internal static class TurretBlacklistHud
{
    private const string IndicatorName = "PTA_TurretBlacklistIndicator";
    
    private static ConfigEntry<bool> _showIndicator = null!;
    private static ConfigEntry<float> _xOffset = null!;
    private static ConfigEntry<float> _yOffset = null!;
    private static ConfigEntry<float> _fontSize = null!;
    
    private static ThrottleGauge? _currentGauge;
    private static TextMeshProUGUI? _indicator;
    private static RectTransform? _referenceRect;
    
    private static bool _gaugeShown;
    
    internal static void Initialise(ConfigFile config)
    {
        _showIndicator = config.Bind("HUD", "Show Turret Blacklist", true,
            "Shows turrets currently blocked from automatic fire next to the throttle gauge");
        _xOffset = config.Bind("HUD", "Turret Blacklist X Offset", 150f,
            "Horizontal offset of the turret blacklist indicator");
        _yOffset = config.Bind("HUD", "Turret Blacklist Y Offset", -30f,
            "Vertical offset of the turret blacklist indicator");
        _fontSize = config.Bind("HUD", "Turret Blacklist Font Size", 36f,
            "Font size of the turret blacklist indicator");
        
        _showIndicator.SettingChanged += (_, _) => ApplyEnabledState();
        _xOffset.SettingChanged += (_, _) => ApplyPosition();
        _yOffset.SettingChanged += (_, _) => ApplyPosition();
        _fontSize.SettingChanged += (_, _) => ApplyPosition();
    }
    
    [HarmonyPatch(typeof(ThrottleGauge), nameof(ThrottleGauge.Initialize))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void InitializePostfix(ThrottleGauge __instance)
    {
        _currentGauge = __instance;
        _gaugeShown = PlayerSettings.gauges;
        
        if (!_showIndicator.Value)
            return;
        
        CreateIndicator(__instance);
        Refresh();
    }
    
    [HarmonyPatch(typeof(ThrottleGauge), nameof(ThrottleGauge.Show))]
    [HarmonyPostfix]
    private static void ShowPostfix(bool arg)
    {
        _gaugeShown = arg;
        ApplyVisibility();
    }
    
    private static void CreateIndicator(ThrottleGauge gauge)
    {
        DestroyIndicator();
        
        var referenceText = gauge.throttleLabel;
        if (referenceText == null)
            return;
        
        _referenceRect = referenceText.rectTransform;
        
        var indicatorObject = Object.Instantiate(referenceText.gameObject, _referenceRect.parent);
        indicatorObject.name = IndicatorName;
        _indicator = indicatorObject.GetComponent<TextMeshProUGUI>();
        
        if (_indicator == null)
        {
            Object.Destroy(indicatorObject);
            return;
        }
        
        _indicator.raycastTarget = false;
        _indicator.overflowMode = TextOverflowModes.Overflow;
        _indicator.alignment = TextAlignmentOptions.TopLeft;
        
        ApplyPosition();
    }
    
    private static void ApplyPosition()
    {
        if (_indicator == null || _referenceRect == null)
            return;
        
        _indicator.rectTransform.anchoredPosition = _referenceRect.anchoredPosition + new Vector2(_xOffset.Value, _yOffset.Value);
        _indicator.fontSize = _fontSize.Value;
    }
    
    internal static void Refresh()
    {
        if (!_showIndicator.Value || _indicator == null)
            return;
        
        if (!HarmonyPatches.TryGetBlacklistHudText(out var text))
        {
            _indicator.text = string.Empty;
            _indicator.gameObject.SetActive(false);
            return;
        }
        
        _indicator.text = text;
        ApplyVisibility();
    }
    
    private static void ApplyVisibility()
    {
        if (_indicator == null)
            return;
        
        _indicator.gameObject.SetActive(_showIndicator.Value && _gaugeShown && !string.IsNullOrEmpty(_indicator.text));
    }
    
    private static void ApplyEnabledState()
    {
        if (!_showIndicator.Value)
        {
            DestroyIndicator();
            return;
        }
        
        if (_currentGauge == null)
            return;
        
        CreateIndicator(_currentGauge);
        Refresh();
    }
    
    private static void DestroyIndicator()
    {
        if (_indicator != null)
            Object.Destroy(_indicator.gameObject);
        
        _indicator = null;
        _referenceRect = null;
    }
    
    internal static void Reset()
    {
        DestroyIndicator();
        
        _currentGauge = null;
        _gaugeShown = false;
    }
}