namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class UsingDirectiveFixRule : IPostProcessRule
{
	public string Name => "UsingDirectiveFix";

	private static readonly (string UsingDirective, string[] KnownTypeNames)[] CandidateNamespaces =
	[
		("using UnityEngine.UI;", [
			"Text", "Button", "Image", "InputField", "Toggle", "Slider", "Scrollbar", "ScrollRect",
			"Dropdown", "RawImage", "Outline", "Shadow", "ContentSizeFitter", "AspectRatioFitter",
			"HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup", "LayoutElement",
			"Canvas", "CanvasRenderer", "CanvasGroup", "Graphic", "Selectable", "ToggleGroup",
			"ColorBlock", "SpriteState", "Navigation", "FontData", "RectOffset", "Mask",
		]),
		("using TMPro;", [
			"TMP_Text", "TextMeshPro", "TextMeshProUGUI", "TMP_InputField", "TMP_Dropdown",
			"TMP_FontAsset", "TMP_Settings", "TMP_SpriteAsset", "TMP_RichTextTags",
		]),
		("using UnityEngine.EventSystems;", [
			"PointerEventData", "ExecuteEvents", "EventTrigger", "EventSystem", "BaseEventData",
			"AxisEventData", "DragEventData", "IBeginDragHandler", "IDragHandler", "IEndDragHandler",
			"IPointerClickHandler", "IPointerDownHandler", "IPointerUpHandler", "IPointerEnterHandler",
			"IPointerExitHandler", "ISelectHandler", "IDeselectHandler", "IScrollHandler",
			"IMoveHandler", "ISubmitHandler", "ICancelHandler",
		]),
		("using UnityEngine.Rendering;", [
			"CommandBuffer", "RenderTexture", "CameraEvent", "BufferFlags", "ShaderPropertyType",
		]),
		("using UnityEngine.Rendering.Universal;", [
			"RenderPipelineAsset", "UniversalRenderPipelineAsset", "ScriptableRendererFeature",
			"ScriptableRenderPass", "ForwardRendererData", "DeferredRendererData",
		]),
		("using UnityEngine.Rendering.HighDefinition;", [
			"HDRenderPipelineAsset", "HDAdditionalCameraData", "HDAdditionalLightData",
		]),
		("using Unity.Mathematics;", [
			"float3", "float4", "float2", "float4x4", "float3x3", "quaternion", "math",
			"int3", "int4", "int2", "bool3", "bool4", "double3", "double4",
		]),
		("using Unity.Collections;", [
			"NativeArray", "NativeList", "NativeHashMap", "NativeMultiHashMap", "Allocator",
			"NativeSlice", "UnsafeList",
		]),
		("using Unity.Burst;", [
			"BurstCompile", "BurstDiscard", "BurstMonoDisable", "NoAlias",
		]),
		("using Unity.Jobs;", [
			"IJob", "IJobParallelFor", "IJobChunk", "IJobEntity", "JobHandle", "IJobFor",
		]),
		("using UnityEngine.Animations;", [
			"Animator", "AnimationClip", "AnimationCurve", "AvatarMask", "HumanDescription",
		]),
		("using UnityEngine.Tilemaps;", [
			"Tilemap", "Tile", "TileBase", "TileData", "RuleTile", "AnimatedTile",
		]),
	];

	public bool TryFix(string content, out string fixedContent, out string fixRecord)
	{
		fixedContent = content;
		fixRecord = string.Empty;

		if (string.IsNullOrEmpty(content))
		{
			return false;
		}

		List<string> removedUsings = [];
		foreach ((string usingDirective, string[] knownTypeNames) in CandidateNamespaces)
		{
			if (!content.Contains(usingDirective, StringComparison.Ordinal))
			{
				continue;
			}

			bool usesAnyType = false;
			foreach (string typeName in knownTypeNames)
			{
				if (content.Contains(typeName, StringComparison.Ordinal))
				{
					usesAnyType = true;
					break;
				}
			}

			if (!usesAnyType)
			{
				content = RemoveUsingDirective(content, usingDirective);
				removedUsings.Add(usingDirective);
			}
		}

		if (removedUsings.Count == 0)
		{
			return false;
		}

		fixedContent = content;
		fixRecord = $"Removed: {string.Join(", ", removedUsings)}";
		return true;
	}

	private static string RemoveUsingDirective(string content, string usingDirective)
	{
		string[] lines = content.Split('\n');
		List<string> result = new(lines.Length);
		foreach (string line in lines)
		{
			if (line.Trim() == usingDirective)
			{
				continue;
			}
			result.Add(line);
		}
		return string.Join("\n", result);
	}
}
