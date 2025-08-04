using UnityEngine;
using UnityEngine.Rendering;

public class CustomVolume : VolumeComponent
{

    public ClampedFloatParameter Intensity = new ClampedFloatParameter(0, 0, 1);

    public BoolParameter GameCameraOnly = new BoolParameter(true);

    public ColorParameter MainColor = new ColorParameter(new UnityEngine.Color(1, 1, 1, 1));

    public bool IsActive()
    {
        return Intensity.GetValue<float>() > 0.0f;
    }
}