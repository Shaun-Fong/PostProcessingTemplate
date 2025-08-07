using UnityEngine;                          // 引用Unity引擎核心命名空间
using UnityEngine.Rendering;                // 引用渲染相关命名空间
using UnityEngine.Rendering.Universal;     // 引用URP相关命名空间

// 定义一个自定义的VolumeComponent，供核心Volume框架使用
// 参考官方核心API文档：https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@latest/index.html?subfolder=/api/UnityEngine.Rendering.VolumeComponent.html
// 参考URP文档：https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest/index.html?subfolder=/manual/Volumes.html
//
// 实现此类后，您可以：
// * 在URP全局设置中调整该VolumeComponent的默认值
// * 将该VolumeComponent添加为场景中局部或全局Volume配置文件中的覆盖（Overrides）
//   相关文档：https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest/index.html?subfolder=/manual/Volume-Profile.html
//   相关文档：https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest/index.html?subfolder=/manual/VolumeOverrides.html
// * 使用VolumeManager API在自定义ScriptableRenderPass或脚本中访问该VolumeComponent的混合值
//   参考核心API文档：https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@latest/index.html?subfolder=/api/UnityEngine.Rendering.VolumeManager.html
// * 通过将VolumeProfile放置在特定图层，并将摄像机的“Volume Mask”设置为该图层，来实现针对不同摄像机的值覆盖
//
// 注意事项：
// * 重命名、修改类型或删除公有字段时要小心，避免破坏已有序列化实例（该类继承自ScriptableObject，序列化规则相同）
// * 实现IPostProcessComponent接口可添加IsActive()方法，当前并非必需，仅方便使用
// * 建议仅暴露那些预期会改变的字段，诸如着色器、材质或LUT贴图等常量资源，最好放在AssetBundles或通过自定义ScriptableRendererFeatures序列化引用，避免构建时被剥离
[VolumeComponentMenu("Post-processing Custom/Template")]        // 在Volume菜单中注册此组件，路径为“Post-processing Custom/Template”
[VolumeRequiresRendererFeatures(typeof(TemplateRendererFeature))] // 该组件依赖TemplateRendererFeature渲染功能
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]  // 仅支持URP渲染管线
public sealed class TemplateVolumeComponent : VolumeComponent, IPostProcessComponent
{
    public TemplateVolumeComponent()
    {
        displayName = "Template";           // 在编辑器中显示的名字
    }

    [Tooltip("Enter the description for the property that is shown when hovered")] // 属性提示，鼠标悬浮时显示
    public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f); // 受限范围浮点参数，默认值1，范围0~1

    // 判断该VolumeComponent是否处于激活状态，主要用于控制是否执行后处理
    public bool IsActive()
    {
        return intensity.GetValue<float>() > 0.0f;  // 当强度大于0时，视为激活
    }
}
