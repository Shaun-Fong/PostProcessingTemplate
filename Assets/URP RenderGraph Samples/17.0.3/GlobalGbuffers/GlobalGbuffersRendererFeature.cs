using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

// 此示例功能会将 gBuffer 组件设置为全局变量（本身不会执行任何渲染）。通过将此功能添加到 Scriptable Renderer，后续的 Pass 就可以作为全局变量访问 gBuffer。
// 要使其正常工作，请确保渲染路径设置为 Deferred（延迟渲染）。

// 将 gBuffer 设置为全局变量可能会导致性能下降和内存占用增加。理想情况下，最好自己管理纹理，并且只对实际需要的纹理调用 builder.UseTexture。
public class GlobalGbuffersRendererFeature : ScriptableRendererFeature
{
    class GlobalGBuffersRenderPass : ScriptableRenderPass
    {
        Material m_Material;
        string m_PassName = "将 gBuffer 组件设为全局变量";

        private static readonly int GBufferNormalSmoothnessIndex = 2;
        private static readonly int GbufferLightingIndex = 3;
        private static readonly int GBufferRenderingLayersIndex = 5;

        // 渲染管线已经在多个地方将 gBuffer 的深度组件设为全局变量，如有需要可取消注释以下代码
        // private static readonly int GbufferDepthIndex = 4;

        // 被标记为可选的组件仅在管线请求时才会存在。
        // 例如，如果没有 Rendering Layers 纹理，_GBuffer5 将包含 ShadowMask 纹理
        private static readonly int[] s_GBufferShaderPropertyIDs = new int[]
        {
            // 包含 Albedo 纹理
            Shader.PropertyToID("_GBuffer0"),

            // 包含 Specular Metallic 纹理
            Shader.PropertyToID("_GBuffer1"),

            // 包含法线和光滑度纹理，在其他 shader 中可通过 _CameraNormalsTexture 引用
            Shader.PropertyToID("_GBuffer2"),

            // 包含光照信息纹理
            Shader.PropertyToID("_GBuffer3"),

            // 包含深度纹理，在其他 shader 中可通过 _CameraDepthTexture 引用（可选）
            Shader.PropertyToID("_GBuffer4"),

            // 包含渲染层信息纹理，在其他 shader 中可通过 _CameraRenderingLayersTexture 引用（可选）
            Shader.PropertyToID("_GBuffer5"),

            // 包含 ShadowMask 纹理（可选）
            Shader.PropertyToID("_GBuffer6")
        };

        private class PassData
        {
        }

        // 此函数在当前 Pass 之后将 gBuffer 设置为全局变量。这样之后的 Pass 就可以通过 builder.UseAllGlobalTextures(true) 访问 gBuffer，
        // 而不需要使用 builder.UseTexture(gBuffer[i])
        // 使用全局纹理的 Shader 可以直接访问这些纹理，无需像 ExecutePass 中那样调用 material.SetTexture()
        private void SetGlobalGBufferTextures(IRasterRenderGraphBuilder builder, TextureHandle[] gBuffer)
        {
            // 此循环将所有 _GBufferX 设置为全局纹理，供所有 shader 使用
            for (int i = 0; i < gBuffer.Length; i++)
            {
                if (i != GbufferLightingIndex && gBuffer[i].IsValid())
                    builder.SetGlobalTextureAfterPass(gBuffer[i], s_GBufferShaderPropertyIDs[i]);
            }

            // 某些全局纹理通过 URP 内部使用的特定 Shader ID 访问，因此我们需要手动指定
            if (gBuffer[GBufferNormalSmoothnessIndex].IsValid())
            {
                // 在本 pass 之后，使用 _CameraNormalsTexture 的 shader 将获取 gBuffer 的法线和光滑度组件
                builder.SetGlobalTextureAfterPass(gBuffer[GBufferNormalSmoothnessIndex],
                    Shader.PropertyToID("_CameraNormalsTexture"));
            }

            // 渲染管线已经在多个地方将 gBuffer 的深度组件设为全局变量，如有需要可取消注释以下代码
            // if (GbufferDepthIndex < gBuffer.Length && gBuffer[GbufferDepthIndex].IsValid())
            // {
            //     // 在本 pass 之后，使用 _CameraDepthTexture 的 shader 将获取 gBuffer 的深度组件（注意：CopyDepth Pass 也会设置此值）
            //     builder.SetGlobalTextureAfterPass(gBuffer[GbufferDepthIndex],
            //         Shader.PropertyToID("_CameraDepthTexture"));
            // }

            if (GBufferRenderingLayersIndex < gBuffer.Length && gBuffer[GBufferRenderingLayersIndex].IsValid())
            {
                // 在本 pass 之后，使用 _CameraRenderingLayersTexture 的 shader 将获取 gBuffer 的渲染层信息组件
                builder.SetGlobalTextureAfterPass(gBuffer[GBufferRenderingLayersIndex],
                    Shader.PropertyToID("_CameraRenderingLayersTexture"));
            }
        }

        // RecordRenderGraph 是用于访问 RenderGraph 句柄的方法，我们可以通过它向图中添加渲染 Pass。
        // FrameData 是一个上下文容器，用于访问和管理 URP 资源。
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            // gBuffer 组件仅在延迟渲染模式下使用
            if (universalRenderingData.renderingMode != RenderingMode.Deferred)
                return;

            // 从资源数据中获取 gBuffer 纹理句柄
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle[] gBuffer = resourceData.gBuffer;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_PassName, out var passData))
            {
                builder.AllowPassCulling(false);
                // 设置 Pass 之后将 gBuffer 设为全局变量
                SetGlobalGBufferTextures(builder, gBuffer);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => { /* 此 Pass 不执行渲染操作 */ });
            }
        }
    }

    GlobalGBuffersRenderPass m_GlobalGbuffersRenderPass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_GlobalGbuffersRenderPass = new GlobalGBuffersRenderPass
        {
            // 此 Pass 必须在延迟光照渲染之后或更晚注入
            renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_GlobalGbuffersRenderPass);
    }
}
