using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class CustomRendererFeature : ScriptableRendererFeature
{

    private Material _mat;

    private CustomRenderPass _pass;

    public override void Create()
    {
        if (_mat == null)
        {
            var shader = Shader.Find("PostProcessing/Template");

            if (shader == null)
            {
                Debug.LogError("Shader Not Found.");
                return;
            }

            _mat = new Material(shader);

        }
        if (_pass == null)
        {
            _pass = new CustomRenderPass("CustomPostProcessing", _mat);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
        {
            return;
        }

        CustomVolume volume = VolumeManager.instance.stack?.GetComponent<CustomVolume>();
        if (volume == null || !volume.IsActive())
            return;

        _pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        // when using the FullScreenPass Shader Graph target you should simply use the "URP Sample Buffer" node which will handle the above for you
        _pass.ConfigureInput(ScriptableRenderPassInput.None);

        // only enqueue the pass if the camera is a Game camera
        if (volume.GameCameraOnly.value)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game)
            {
                renderer.EnqueuePass(_pass);
            }
        }
        else
        {
            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Application.isPlaying)
        {
            Destroy(_mat);
        }
        else
        {
            DestroyImmediate(_mat);
        }
    }

    class CustomRenderPass : ScriptableRenderPass
    {
        private Material _mat;

        private static MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();
        private static readonly int k_IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int k_MainColorId = Shader.PropertyToID("_MainColor");

        private static readonly int kMainTexPropertyId = Shader.PropertyToID("_MainTex");

        class PassData
        {
            public Material material;
            public TextureHandle inputTexture;
        }

        public CustomRenderPass(string passName, Material mat)
        {
            profilingSampler = new ProfilingSampler(passName);
            _mat = mat;
            requiresIntermediateTexture = true;
        }

        private static void ExecuteMainPass(PassData data, RasterGraphContext context)
        {
            ExecuteMainPass(context.cmd, data.inputTexture, data.material);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {

            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                passData.material = _mat;

                TextureHandle destination;

                // GPU graphics pipelines don't allow to sample the texture bound as the active color target, ie the cameraColor cannot both be an input and the render target.
                // Before, this required us to first copy the cameraColor to then blit back to it while sampling from the copy. Now that we have the ContextContainer, we can swap the cameraColor to 
                // another (temp) resource so that the next pass uses the temp resource. We don't need the copy anymore. However, this only works if you are writing to every 
                // pixel of the frame, a partial write will need the copy first to add to the existing content. See FullScreenPassRendererFeature.cs for an example. 
                var cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                cameraColorDesc.name = "_CustomPostProcessing";
                cameraColorDesc.clearBuffer = false;

                destination = renderGraph.CreateTexture(cameraColorDesc);
                passData.inputTexture = resourcesData.cameraColor;

                //If you use framebuffer fetch in your material then you need to use builder.SetInputAttachment. If the pass can be merged then this will reduce GPU bandwidth usage / power consumption and improve GPU performance. 
                builder.UseTexture(passData.inputTexture, AccessFlags.Read);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecuteMainPass(data, context));

                //Swap cameraColor to the new temp resource (destination) for the next pass
                resourcesData.cameraColor = destination;
            }

        }

        private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
        {
            Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
        }

        private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, Material material)
        {
            s_SharedPropertyBlock.Clear();

            if (sourceTexture != null)
                s_SharedPropertyBlock.SetTexture(kMainTexPropertyId, sourceTexture);

            CustomVolume volume = VolumeManager.instance.stack?.GetComponent<CustomVolume>();

            if (volume != null)
            {
                s_SharedPropertyBlock.SetFloat("_Intensity", volume.Intensity.value);
                s_SharedPropertyBlock.SetColor("_MainColor", volume.MainColor.value);
            }

            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
        }
    }
}
