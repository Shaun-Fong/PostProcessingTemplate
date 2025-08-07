using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 本示例展示了如何在 URP 中通过 RenderGraph 使用 MRT（多渲染目标）。当单个 RGBA 纹理无法满足超过 4 个通道的数据输出时，使用 MRT 非常有用。
public class MrtRendererFeature : ScriptableRendererFeature
{
    // 此 Pass 使用 MRT 并输出到 3 个不同的渲染目标。
    class MrtPass : ScriptableRenderPass
    {
        // 我们希望在记录后传递给渲染函数的数据
        class PassData
        {
            // 输入颜色纹理句柄
            public TextureHandle color;
            // 材质中使用的输入纹理名称
            public string texName;
            // MRT Pass 使用的材质
            public Material material;
        }

        // 材质中使用的输入纹理名称
        string m_texName;
        // MRT Pass 使用的材质
        Material m_Material;
        // MRT 输出目标的 RTHandle 数组
        RTHandle[] m_RTs = new RTHandle[3];
        RenderTargetInfo[] m_RTInfos = new RenderTargetInfo[3];

        // 用于将材质从 RendererFeature 传递给 RenderPass 的函数
        public void Setup(string texName, Material material, RenderTexture[] renderTextures)
        {
            m_Material = material;
            m_texName = String.IsNullOrEmpty(texName) ? "_ColorTexture" : texName;

            // 如果 RenderTexture 已更改，则从 RenderTexture 创建 RTHandle
            for (int i = 0; i < 3; i++)
            {
                if (m_RTs[i] == null || m_RTs[i].rt != renderTextures[i])
                {
                    m_RTs[i]?.Release();
                    m_RTs[i] = RTHandles.Alloc(renderTextures[i], $"ChannelTexture[{i}]");
                    m_RTInfos[i] = new RenderTargetInfo()
                    {
                        format = renderTextures[i].graphicsFormat,
                        height = renderTextures[i].height,
                        width = renderTextures[i].width,
                        bindMS = renderTextures[i].bindTextureMS,
                        msaaSamples = 1,
                        volumeDepth = renderTextures[i].volumeDepth,
                    };
                }
            }
        }

        // 此函数将使用给定材质进行全屏 Blit（屏幕复制）
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var handles = new TextureHandle[3];
            // 将 RTHandle 导入 RenderGraph 并获取对应句柄
            for (int i = 0; i < 3; i++)
            {
                handles[i] = renderGraph.ImportTexture(m_RTs[i], m_RTInfos[i]);
            }

            // 使用 RenderGraph 添加一个新的光栅渲染 Pass，并传出用于渲染函数的数据结构
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("MRT Pass", out var passData))
            {
                // 获取通用资源数据，用于提取相机的颜色附件
                var resourceData = frameData.Get<UniversalResourceData>();

                // 填充渲染函数所需的数据结构
                passData.color = resourceData.activeColorTexture; // 使用当前相机颜色纹理作为输入
                passData.texName = m_texName;                     // 材质中使用的输入纹理名
                passData.material = m_Material;                   // 使用的材质

                // 设置输入纹理
                builder.UseTexture(passData.color);
                // 设置渲染目标（MRT 输出）
                for (int i = 0; i < 3; i++)
                {
                    builder.SetRenderAttachment(handles[i], i);
                }

                // 设置渲染函数
                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
            }
        }

        // ExecutePass 是实际的渲染函数，RenderGraph 在执行 Pass 时会调用此方法
        // 推荐使用静态函数以避免对外部状态依赖
        static void ExecutePass(PassData data, RasterGraphContext rgContext)
        {
            // 设置材质中的输入纹理
            data.material.SetTexture(data.texName, data.color);
            // 使用 MRT 着色器绘制全屏三角形
            rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
        }
    }

    [Tooltip("用于 MRT Pass 的材质。")]
    public Material mrtMaterial;
    [Tooltip("用于传递相机颜色纹理给材质的纹理名。")]
    public string textureName = "_ColorTexture";
    [Tooltip("MRT 输出的 RenderTextures，必须包含 3 个元素。")]
    public RenderTexture[] renderTextures = new RenderTexture[3];

    MrtPass m_MrtPass;

    // 在此处创建并初始化渲染 Pass，此方法在序列化时调用
    public override void Create()
    {
        m_MrtPass = new MrtPass();

        // 配置该渲染 Pass 应插入的时间点
        m_MrtPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    // 在此处将渲染 Pass 注入到渲染器中，每个相机只调用一次
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 同一 RenderPassEvent 的多个 Pass，其添加顺序会影响执行顺序

        // 如果材质为空或 RenderTextures 数量不为 3，则提前退出
        if (mrtMaterial == null || renderTextures.Length != 3)
        {
            Debug.LogWarning("跳过 MRTPass：材质为空或 RenderTextures 数量不为 3。");
            return;
        }

        // 检查每个 RenderTexture 是否为 null
        foreach (var rt in renderTextures)
        {
            if (rt == null)
            {
                Debug.LogWarning("跳过 MRTPass：某个 RenderTexture 为空。");
                return;
            }
        }

        // 调用 Setup 将 RendererFeature 设置传递给 RenderPass
        m_MrtPass.Setup(textureName, mrtMaterial, renderTextures);
        renderer.EnqueuePass(m_MrtPass);
    }
}
