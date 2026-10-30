using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Oddity.Game
{
    /// <summary>The 6500 K face key and eye catch light shared by the prologue and the room cuts.</summary>
    public static class FaceLights
    {
        public const float Kelvin = 6500f, KeyIntensity = 0.5f, CatchIntensity = 0.015f;

        public static (Light key, Light catchLight) Build(string keyName, string catchName, uint mask)
        {
            var key = Make(keyName, LightType.Spot, KeyIntensity, 4f, mask);
            key.spotAngle = 50f;
            key.innerSpotAngle = 25f;
            return (key, Make(catchName, LightType.Point, CatchIntensity, 1.2f, mask));
        }

        static Light Make(string name, LightType type, float intensity, float range, uint mask)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = type;
            l.color = Mathf.CorrelatedColorTemperatureToRGB(Kelvin);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.GetUniversalAdditionalLightData().renderingLayers = mask;
            return l;
        }
    }
}
