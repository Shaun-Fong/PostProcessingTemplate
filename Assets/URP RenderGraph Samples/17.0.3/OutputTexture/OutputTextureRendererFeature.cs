using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule.Util;

// 此 Pass 可以与 Draw Objects Pass 和 Draw Skybox Pass 合并，前提是在检查器中将 m_PassEvent 设置为 After Rendering Opaques，
// 并将纹理类型设置为 Normal。
// 可以在 Render Graph Visualizer 中观察此合并行为。
// 如果设置为 After Rendering Post Processing，则该 Pass 不会与其他任何内容合并。

// 本 RenderFeature 展示了如何使用 RenderGraph 输出 URP 中的特定纹理，
// 如何将纹理通过名称绑定到材质上，
// 以及如果按照正确的执行顺序，两个渲染 Pass 如何合并为一个。
public class OutputTextureRendererFeature : ScriptableRendererFeature
{
    // 枚举类型，用于选择要输出的纹理
    [Serializable]
    enum TextureType
    {
        OpaqueColor,     // 不透明颜色纹理
        Depth,           // 深度纹理
        Normal,          // 法线纹理
        MotionVector,    // 运动矢量纹理
    }

    // 根据传入的资源数据和所需的纹理类型来获取相应的纹理
    static TextureHandle GetTextureHandleFromType(UniversalResourceData resourceData, TextureType textureType)
    {
        switch (textureType)
        {
            case TextureType.OpaqueColor:
                return resourceData.cameraOpaqueTexture; // 不透明颜色纹理
            case TextureType.Depth:
                return resourceData.cameraDepthTexture; // 深度纹理
            case TextureType.Normal:
                return resourceData.cameraNormalsTexture; // 法线纹理
            case TextureType.MotionVector:
                return resourceData.motionVectorColor; // 运动矢量纹理
            default:
                return TextureHandle.nullHandle; // 无效纹理句柄
        }
    }

    // 用于输出纹理的渲染通道，用于检查纹理内容
    class OutputTexturePass : ScriptableRenderPass
    {
        // 绑定给材质的纹理名称
        string m_TextureName;
        // 要从 URP 中获取的纹理类型
        TextureType m_TextureType;
        // 用于 Blit 到颜色输出的材质
        Material m_Material;

        // 设置函数，配置输入并将渲染器特性设置传递给渲染通道
        public void Setup(string textureName, TextureType textureType, Material material)
        {
            // 根据纹理类型配置输入资源，保证对应纹理可用
            if (textureType == TextureType.OpaqueColor)
                ConfigureInput(ScriptableRenderPassInput.Color);
            else if (textureType == TextureType.Depth)
                ConfigureInput(ScriptableRenderPassInput.Depth);
            else if (textureType == TextureType.Normal)
                ConfigureInput(ScriptableRenderPassInput.Normal);
            else if (textureType == TextureType.MotionVector)
                ConfigureInput(ScriptableRenderPassInput.Motion);

            // 设置纹理名称，若传入为空则使用默认 "_BlitTexture"
            m_TextureName = String.IsNullOrEmpty(textureName) ? "_BlitTexture" : textureName;
            // 设置纹理类型
            m_TextureType = textureType;
            // 设置材质
            m_Material = material;
        }

        // 记录 RenderGraph Pass，将指定纹理 Blit 回摄像机颜色附件
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 从 frameData 中获取 URP 资源数据，取得对应纹理句柄
            var resourceData = frameData.Get<UniversalResourceData>();
            var source = GetTextureHandleFromType(resourceData, m_TextureType);

            if (!source.IsValid())
            {
                Debug.Log("输入纹理尚未创建，可能是 Pass 事件早于资源创建。跳过 OutputTexturePass。");
                return;
            }

            // 设置 Blit 参数，将 source 纹理拷贝到当前活跃颜色纹理，使用指定材质
            RenderGraphUtils.BlitMaterialParameters para = new(source, resourceData.activeColorTexture, m_Material, 0);
            para.sourceTexturePropertyID = Shader.PropertyToID(m_TextureName);
            renderGraph.AddBlitPass(para, passName: "Blit Selected Resource");
        }
    }

    // 检查器中的输入参数，用于调整渲染器特性配置
    [SerializeField]
    RenderPassEvent m_PassEvent = RenderPassEvent.AfterRenderingTransparents;
    [SerializeField]
    string m_TextureName = "_InputTexture";
    [SerializeField]
    TextureType m_TextureType;
    [SerializeField]
    Material m_Material;

    OutputTexturePass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new OutputTexturePass();
        // 配置渲染通道注入事件
        m_ScriptablePass.renderPassEvent = m_PassEvent;
    }

    // 注入一个或多个渲染通道，此方法在每个摄像机设置时调用
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 设置渲染通道的数据，并将渲染器特性数据传递给渲染通道
        m_ScriptablePass.Setup(m_TextureName, m_TextureType, m_Material);
        renderer.EnqueuePass(m_ScriptablePass);
    }
}
