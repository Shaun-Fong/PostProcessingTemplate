using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule.Util;

// 此示例将活动 CameraColor Blit（拷贝）到一个新纹理。
// 该示例展示了如何使用材质进行 Blit，以及如何使用 ResourceData 来避免再执行一次 Blit 回到活动颜色目标。
// 此示例仅用于 API 演示目的。


// 此 Pass 使用指定的材质将整个屏幕 Blit 到一个临时纹理，并将 UniversalResourceData.cameraColor 切换到该临时纹理。
// 因此，下一个引用 cameraColor 的 Pass 将会使用这个新的临时纹理作为 cameraColor，从而节省了一次 Blit。
// 使用 ResourceData，你可以自己管理资源的切换，而无需使用专门的 SwapColorBuffer API（该 API 只针对 cameraColor）。
// 这允许你编写更解耦的 Pass，同时避免不必要的拷贝/Blit 带来的性能开销。
public class BlitAndSwapColorPass : ScriptableRenderPass
{
    const string m_PassName = "BlitAndSwapColorPass";

    // 在 Blit 操作中使用的材质。
    Material m_BlitMaterial;

    // 用于将材质从 RendererFeature 传递到 RenderPass 的方法。
    public void Setup(Material mat)
    {
        m_BlitMaterial = mat;

        // 此 Pass 将会读取当前的颜色纹理。
        // 该纹理必须是一个中间纹理，不能直接使用后缓冲（BackBuffer）作为输入纹理。
        // 通过设置此属性，URP 将自动创建一个中间纹理。但这会有性能开销，因此不要在不需要时设置此项。
        // 最佳实践是在此处（Pass 内部）设置，而不是在 RenderFeature 中设置。
        // 这样，该 Pass 是自包含的，你也可以从 MonoBehaviour 中直接将此 Pass 入队，而无需依赖 RenderFeature。
        requiresIntermediateTexture = true;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // UniversalResourceData 包含渲染器使用的所有纹理句柄，包括活动的颜色纹理和深度纹理。
        // 活动的颜色和深度纹理是摄像机渲染的主要颜色缓冲区和深度缓冲区。
        var resourceData = frameData.Get<UniversalResourceData>();

        // 这通常不会发生，因为我们设置了 m_Pass.requiresIntermediateTexture = true。
        // 除非你将渲染事件设置为 AfterRendering，在该阶段我们只能使用后缓冲（BackBuffer）。
        if (resourceData.isActiveTargetBackBuffer)
        {
            Debug.LogError($"跳过渲染 Pass。BlitAndSwapColorRendererFeature 需要一个中间颜色纹理，我们不能使用 BackBuffer 作为纹理输入。");
            return;
        }

        // 在此创建目标纹理，
        // 该纹理会和活动颜色纹理具有相同的尺寸。
        var source = resourceData.activeColorTexture;

        var destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = $"CameraColor-{m_PassName}";
        destinationDesc.clearBuffer = false;

        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        // 使用材质进行 Blit 操作的参数
        RenderGraphUtils.BlitMaterialParameters para = new(source, destination, m_BlitMaterial, 0);
        renderGraph.AddBlitPass(para, passName: m_PassName);

        // FrameData 允许获取和设置内部管线缓冲区。
        // 在这里，我们将 CameraColorBuffer 更新为本 Pass 刚写入的纹理。
        // 因为 RenderGraph 会管理管线资源和依赖关系，后续的 Pass 会正确使用这个新的颜色缓冲区。
        // 该优化有一些注意事项：当颜色缓冲区需要在帧与帧之间、以及不同摄像机之间持久存在（例如相机堆叠）时，
        // 你必须确保你的纹理是 RTHandle，并且你正确管理了它的生命周期。
        resourceData.cameraColor = destination;
    }
}

public class BlitAndSwapColorRendererFeature : ScriptableRendererFeature
{
    [Tooltip("执行 Blit 操作时使用的材质。")]
    public Material material;

    [Tooltip("用于注入该 Pass 的渲染事件阶段。")]
    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

    BlitAndSwapColorPass m_Pass;

    // 你可以在此处创建 Pass 并进行初始化。此方法会在每次序列化时调用。
    public override void Create()
    {
        m_Pass = new BlitAndSwapColorPass();

        // 配置渲染 Pass 的注入位置。
        m_Pass.renderPassEvent = renderPassEvent;
    }

    // 你可以在此处向渲染器中注入一个或多个渲染 Pass。
    // 该方法会在为每个摄像机设置渲染器时调用一次。
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 如果材质为空则提前退出。
        if (material == null)
        {
            Debug.LogWarning(this.name + " 的材质为空，将被跳过。");
            return;
        }

        m_Pass.Setup(material);
        renderer.EnqueuePass(m_Pass);
    }
}
