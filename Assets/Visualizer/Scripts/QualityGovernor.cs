using UnityEngine;

// Hlídá FPS v brýlích a podle AdaptiveQuality upravuje render scale a práh vypínání slabých světel.
// Na PC (mimo VR) nedělá nic.
public class QualityGovernor : MonoBehaviour
{
    public static int Level { get; private set; }

    readonly AdaptiveQuality quality = new AdaptiveQuality();
    VREnvironment env;
    float target = 72f;

    void Start()
    {
        env = GetComponent<VREnvironment>();
        float hz = (float)Screen.currentResolution.refreshRateRatio.value;
        if (hz >= 30f && hz <= 200f) target = hz;
    }

    void Update()
    {
        if (!VRRig.IsVR) return;
        if (quality.Update(Time.unscaledDeltaTime, target)) Apply();
    }

    void Apply()
    {
        Level = quality.Level;
        VisUtil.LightCullThreshold = AdaptiveQuality.LightCullThreshold(Level);
        if (env != null) env.SetRenderQuality(AdaptiveQuality.RenderScaleFactor(Level));
        Debug.Log("Quality: auto level " + Level + " (target " + target + " Hz)");
    }

    void OnDisable()
    {
        Level = 0;
        VisUtil.LightCullThreshold = AdaptiveQuality.LightCullThreshold(0);
    }
}
