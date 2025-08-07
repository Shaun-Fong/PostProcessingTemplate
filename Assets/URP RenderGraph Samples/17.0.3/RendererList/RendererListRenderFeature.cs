using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

// 该示例清除当前活动的颜色纹理，然后渲染与 m_LayerMask 图层相关联的场景几何体。
// 你可以将场景几何体添加到自定义图层，并尝试在渲染特性UI中切换图层掩码。
// 可以使用帧调试器检查该 Pass 的输出结果。
public class RendererListRenderFeature : ScriptableRendererFeature
{
    class RendererListPass : ScriptableRenderPass
    {
        // 用于过滤需要加入渲染器列表的对象的图层掩码
        private LayerMask m_LayerMask;

        // 用于构建渲染器列表的着色器标签ID列表
        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        public RendererListPass(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
        }

        // 该类存储 Pass 执行时所需的数据，作为参数传递给执行委托函数
        private class PassData
        {
            public RendererListHandle rendererListHandle;
        }

        // 通过 RenderGraph API 创建渲染器列表的示例实用方法
        private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph)
        {
            // 访问通用渲染管线的相关帧数据
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.opaque;
            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange, m_LayerMask);

            ShaderTagId[] forwardOnlyShaderTagIds = new ShaderTagId[]
            {
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"), // 传统着色器（无 gbuffer pass）被视为仅前向渲染以保持兼容性
                new ShaderTagId("LightweightForward") // 传统着色器（无 gbuffer pass）被视为仅前向渲染以保持兼容性
            };

            m_ShaderTagIdList.Clear();

            foreach (ShaderTagId sid in forwardOnlyShaderTagIds)
                m_ShaderTagIdList.Add(sid);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        // 该静态方法用于执行 Pass，并作为 RenderFunc 委托传递给 RenderGraph 的渲染 Pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            // 清除渲染目标颜色缓冲，颜色为绿色，深度为1
            context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.green, 1, 0);

            // 绘制渲染器列表中的所有对象
            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // 这里可以访问 RenderGraph 句柄，允许每个 ScriptableRenderPass 通过 RenderGraph 添加多个渲染 Pass
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            string passName = "RenderList 渲染通道";

            // 此简单 Pass 清除当前活跃的颜色纹理，然后渲染与 m_LayerMask 图层相关联的场景几何体。
            // 你可以将场景几何体添加到自定义图层，并在渲染特性UI中尝试切换图层掩码。
            // 可以使用帧调试器检查 Pass 输出。

            // 向渲染图中添加一个光栅渲染通道，指定名称及执行函数所需的数据类型
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                // UniversalResourceData 包含渲染器使用的所有纹理句柄，包括活跃的颜色和深度纹理
                // 活跃颜色和深度纹理是摄像机渲染的主颜色和深度缓冲
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // 填充 PassData 所需数据
                InitRendererLists(frameData, ref passData, renderGraph);

                // 确保渲染器列表有效
                // if (!passData.rendererListHandle.IsValid())
                //    return;

                // 通过 UseRendererList 将刚创建的渲染器列表声明为该 Pass 的输入依赖
                builder.UseRendererList(passData.rendererListHandle);

                // 通过 UseTextureFragment 和 UseTextureFragmentDepth 设置渲染目标，相当于旧版的 cmd.SetRenderTarget(color, depth)
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);

                // 将 ExecutePass 函数分配为渲染通道的执行委托，渲染图执行该 Pass 时调用
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));

                // 也可以直接用泛型方式传递
                builder.SetRenderFunc<PassData>(ExecutePass);
            }
        }
    }

    RendererListPass m_ScriptablePass;

    public LayerMask m_LayerMask;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new RendererListPass(m_LayerMask);

        // 配置渲染通道注入事件
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // 在渲染器中注入一个或多个渲染通道，此方法在每个摄像机设置时调用
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }
}
