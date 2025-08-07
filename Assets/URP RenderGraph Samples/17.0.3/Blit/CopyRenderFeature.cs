using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 此示例将活动颜色纹理复制到一个新的纹理中。
// 该示例仅用于 API 演示目的，因此新的纹理不会在该帧的其他地方使用。
// 你可以通过 Frame Debugger（帧调试器）来验证其内容。
public class CopyRenderFeature : ScriptableRendererFeature
{
    class CopyRenderPass : ScriptableRenderPass
    {
        public CopyRenderPass()
        {
            // 此 Pass 将会读取当前的颜色纹理。
            // 该纹理必须是一个中间纹理，不能直接使用后缓冲（BackBuffer）作为输入纹理。
            // 通过设置此属性，URP 会自动创建一个中间纹理。
            // 最佳实践是在此处（Pass 内部）设置，而不是在 RenderFeature 中设置。
            // 这样，该 Pass 是自包含的，你也可以从 MonoBehaviour 中直接将此 Pass 入队，而无需依赖 RenderFeature。
            requiresIntermediateTexture = true;
        }

        // 这里是可以访问 RenderGraph 句柄的地方。
        // 每个 ScriptableRenderPass 都可以使用 RenderGraph 句柄向渲染图（Render Graph）中添加多个渲染 Pass。
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "Copy To or From Temp Texture"; // 复制到临时纹理或从临时纹理复制

            // UniversalResourceData 包含渲染器使用的所有纹理句柄，包括活动的颜色纹理和深度纹理。
            // 活动的颜色和深度纹理是摄像机渲染到的主颜色和深度缓冲区。
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // 目标纹理在此处被创建，
            // 该纹理的尺寸与活动颜色纹理一致。
            var source = resourceData.activeColorTexture;

            var destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"CameraColor-{passName}";
            destinationDesc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            if (RenderGraphUtils.CanAddCopyPassMSAA())
            {
                // 这个简单的 Pass 会将活动颜色纹理复制到一个新的纹理中。
                renderGraph.AddCopyPass(resourceData.activeColorTexture, destination, passName: passName);

                // 由于前一次的复制结果没有被读取，因此需要再将结果复制回去，否则这个 Pass 会被裁剪掉。
                // 这只是用于演示目的。
                renderGraph.AddCopyPass(destination, resourceData.activeColorTexture, passName: passName);
            }
            else
            {
                Debug.Log("由于启用了 MSAA，无法添加复制 Pass");
            }
        }
    }

    CopyRenderPass m_CopyRenderPass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_CopyRenderPass = new CopyRenderPass();

        // 配置此渲染 Pass 应该被注入的位置。
        m_CopyRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // 你可以在此处向渲染器中注入一个或多个渲染 Pass。
    // 该方法在为每个摄像机设置渲染器时会调用一次。
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_CopyRenderPass);
    }
}
