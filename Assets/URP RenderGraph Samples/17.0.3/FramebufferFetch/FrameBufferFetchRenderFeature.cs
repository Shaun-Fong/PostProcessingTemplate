using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 本示例通过自定义材质和帧缓冲抓取（Framebuffer Fetch）功能，将前一个Pass的渲染结果复制到一个新纹理中。
/// 本示例仅用于API演示，因此这个新纹理在帧的其他地方不会被使用。你可以使用帧调试器验证其内容。
///
/// 帧缓冲抓取是一种高级的基于瓦片的延迟渲染（TBDR）GPU优化技术，允许后续的子通道直接从帧缓冲读取之前子通道的输出，
/// 从而极大地降低带宽使用。
/// </summary>
public class FrameBufferFetchRenderFeature : ScriptableRendererFeature
{
    class FrameBufferFetchPass : ScriptableRenderPass
    {
        private Material m_BlitMaterial;
        private Material m_FBFetchMaterial;

        public FrameBufferFetchPass(Material fbFetchMaterial)
        {
            m_FBFetchMaterial = fbFetchMaterial;

            // 此Pass将读取当前颜色纹理，因此必须使用中间纹理，不能直接读取BackBuffer（后备缓冲区）作为输入纹理。
            // 设置 requiresIntermediateTexture 为 true 后，URP 将自动创建中间纹理。注意这会带来一定性能开销，若非必要不应启用。
            // 建议在此处而非RenderFeature中设置该属性，使得此Pass具备自包含性，可以直接从MonoBehaviour中加入而无需依赖RenderFeature。
            requiresIntermediateTexture = true;
        }

        // 用于传递给执行函数的数据结构
        private class PassData
        {
            internal TextureHandle src;
            internal Material material;
            internal bool useMSAA;
        }

        // 此静态方法为具体的Pass执行逻辑，会作为RenderGraph的 RenderFunc 委托传入
        static void ExecuteFBFetchPass(PassData data, RasterGraphContext context)
        {
            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.useMSAA ? 1 : 0, MeshTopology.Triangles, 3, 1, null);
        }

        // 在RenderGraph中添加一个帧缓冲抓取Pass
        private void FBFetchPass(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source, TextureHandle destination, bool useMSAA)
        {
            string passName = "FrameBufferFetchPass";

            // 本Pass使用自定义材质和帧缓冲抓取功能，将上一个Pass的结果复制到一个新纹理中
            // 该纹理不会在后续流程中使用，仅用于展示RenderGraph API的使用方法

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                // 设置传递给执行函数的数据
                passData.material = m_FBFetchMaterial;
                passData.useMSAA = useMSAA;

                // 声明source为输入附件（input attachment），帧缓冲抓取必须使用此方式读取输入
                builder.SetInputAttachment(source, 0, AccessFlags.Read);

                // 设置destination为渲染目标，相当于传统的cmd.SetRenderTarget调用
                builder.SetRenderAttachment(destination, 0);

                // 为了示例展示关闭该Pass的剔除逻辑。正常情况下，如果目标纹理未被后续使用，该Pass会被剔除
                builder.AllowPassCulling(false);

                // 设置执行函数
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecuteFBFetchPass(data, context));
            }
        }

        // 每个 ScriptableRenderPass 都可以通过 RecordRenderGraph 使用 RenderGraph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 此Pass演示如何使用帧缓冲抓取。帧缓冲抓取是GPU中优化带宽的一种手段，可以让多个Pass共享相同的帧缓冲内容。
            // 第一个Pass（BlitPass）会将相机颜色拷贝到一个中间RT中，第二个Pass（FBFetchPass）再通过帧缓冲抓取从该RT读取数据，
            // 并输出到另一个RT中。
            // 通过RenderGraph Visualizer可以看到这些Pass被合并，并且不再需要中间RT实际存储数据，从而降低了带宽消耗。

            // UniversalResourceData 提供了当前帧使用的纹理资源句柄，包括颜色和深度纹理
            var resourceData = frameData.Get<UniversalResourceData>();

            // 获取当前的主颜色纹理
            var source = resourceData.activeColorTexture;

            // 创建与source尺寸相同的目标纹理
            var destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "FBFetchDestTexture";
            destinationDesc.clearBuffer = false;

            // 如果不使用MSAA或支持MSAA的拷贝流程
            if (destinationDesc.msaaSamples == MSAASamples.None || RenderGraphUtils.CanAddCopyPassMSAA())
            {
                TextureHandle fbFetchDestination = renderGraph.CreateTexture(destinationDesc);

                // 添加帧缓冲抓取Pass
                FBFetchPass(renderGraph, frameData, source, fbFetchDestination, destinationDesc.msaaSamples != MSAASamples.None);

                // 为了方便查看效果，将结果复制回相机颜色纹理（该CopyPass也使用FBF）
                renderGraph.AddCopyPass(fbFetchDestination, source, passName: "Copy Back FF Destination (also using FBF)");
            }
            else
            {
                Debug.Log("由于MSAA设置，无法添加FBF Pass及其拷贝流程");
            }
        }
    }

    FrameBufferFetchPass m_FbFetchPass;

    public Material m_FBFetchMaterial;

    /// <summary>创建RenderPass实例</summary>
    public override void Create()
    {
        m_FbFetchPass = new FrameBufferFetchPass(m_FBFetchMaterial);

        // 设置插入RenderPass的时间点
        m_FbFetchPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    /// <summary>
    /// 将自定义的RenderPass添加到渲染流程中。
    /// 每个相机在配置渲染器时调用一次。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 若材质未指定则跳过此RenderPass
        if (m_FBFetchMaterial == null)
        {
            Debug.LogWarning(this.name + " 材质未指定，跳过执行。");
            return;
        }

        renderer.EnqueuePass(m_FbFetchPass);
    }
}
