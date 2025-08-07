using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// 本示例展示了如何在渲染通道中使用 GBuffer 分量，即便它们不是全局的。
/// 渲染通道默认会在场景中的几何体上显示高光金属贴图（_GBuffer2）的内容，
/// 你可以通过修改示例着色器来改变读取的 GBuffer 分量。
/// 请确保：（1）将渲染路径设置为 Deferred（延迟渲染），（2）场景中有一个3D物体，才能看到效果。
/// </summary>
public class GbufferVisualizationRendererFeature : ScriptableRendererFeature
{
    class GBufferVisualizationRenderPass : ScriptableRenderPass
    {
        Material m_Material;
        string m_PassName = "可视化 GBuffer 分量（并使 GBuffer 成为全局变量）";

        private static readonly int GbufferLightingIndex = 3;

        // 其他 GBuffer 分量索引
        // private static readonly int GBufferNormalSmoothnessIndex = 2;
        // private static readonly int GbufferDepthIndex = 4;
        // private static readonly int GBufferRenderingLayersIndex = 5;

        // 标记为可选的组件仅在管线请求时存在。
        // 例如如果没有 rendering layers 纹理，_GBuffer5 将包含 ShadowMask 贴图
        private static readonly int[] s_GBufferShaderPropertyIDs = new int[]
        {
            // 包含 Albedo 贴图
            Shader.PropertyToID("_GBuffer0"),

            // 包含 Specular Metallic 贴图
            Shader.PropertyToID("_GBuffer1"),

            // 包含法线与光滑度，在其他着色器中可通过 _CameraNormalsTexture 获取
            Shader.PropertyToID("_GBuffer2"),

            // 包含光照信息
            Shader.PropertyToID("_GBuffer3"),

            // 包含深度信息，在其他着色器中引用为 _CameraDepthTexture（可选）
            Shader.PropertyToID("_GBuffer4"),

            // 包含 Rendering Layers 贴图，在其他着色器中引用为 _CameraRenderingLayersTexture（可选）
            Shader.PropertyToID("_GBuffer5"),

            // 包含 ShadowMask 贴图（可选）
            Shader.PropertyToID("_GBuffer6")
        };

        private class PassData
        {
            // 在此示例中，我们希望在Pass中使用 GBuffer 分量
            public TextureHandle[] gBuffer;
            public Material material;
        }

        public void Setup(Material material)
        {
            m_Material = material;
        }

        // 此方法会绘制由着色器请求的 GBuffer 分量内容
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            // 虽然着色器只需要一个分量，我们这里作为示例读取全部 gBuffer 分量。
            // 由于它们不是全局变量，我们需要显式传递给材质，否则着色器无法访问。
            for (int i = 0; i < data.gBuffer.Length; i++)
            {
                data.material.SetTexture(s_GBufferShaderPropertyIDs[i], data.gBuffer[i]);
            }

            // 使用着色器请求的 GBuffer 分量绘制在场景中的几何体上
            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
        }

        // RecordRenderGraph 方法允许访问 RenderGraph 句柄，并通过它添加 Pass
        // frameData 是一个上下文容器，用于访问和管理 URP 资源
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();

            // GBuffer 仅在延迟渲染模式下使用
            if (m_Material == null || universalRenderingData.renderingMode != RenderingMode.Deferred)
                return;

            // 从资源数据中获取 gBuffer 的纹理句柄
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle[] gBuffer = resourceData.gBuffer;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_PassName, out var passData))
            {
                passData.material = m_Material;

                // 在延迟渲染路径中，我们希望将输出写入 activeColorTexture，它就是 GBuffer 的 Lighting 分量 (_GBuffer3)
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                // 本 Pass 中需要读取 gBuffer 分量，因此需要调用 UseTexture 绑定资源。
                // 如果这些资源是全局的，可以直接调用 builder.UseAllGlobalTexture(true)；
                // 但在这个示例中，它们不是全局的。
                for (int i = 0; i < resourceData.gBuffer.Length; i++)
                {
                    if (i == GbufferLightingIndex)
                    {
                        // 此资源已通过 SetRenderAttachment 写入，无需再绑定为读取
                        continue;
                    }

                    builder.UseTexture(resourceData.gBuffer[i]);
                }

                // 如果不传入 gBuffer，Pass 执行时将无法访问这些纹理
                passData.gBuffer = gBuffer;

                // 设置执行函数。当渲染图执行此Pass时将调用此函数
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }

    GBufferVisualizationRenderPass m_GBufferRenderPass;
    public Material m_Material;

    /// <inheritdoc/>
    public override void Create()
    {
        m_GBufferRenderPass = new GBufferVisualizationRenderPass
        {
            // 此 Pass 必须在延迟光照之后或更晚插入
            renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // GBuffer 仅在延迟渲染路径中使用
        if (m_Material != null)
        {
            m_GBufferRenderPass.Setup(m_Material);
            renderer.EnqueuePass(m_GBufferRenderPass);
        }
    }
}
