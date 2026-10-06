using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;
using AssetRipper.SourceGenerated.Subclasses.SerializedProperties;
using AssetRipper.SourceGenerated.Subclasses.SerializedProperty;

namespace AssetRipper.Export.UnityProjects.Shaders;

public sealed class DummyShaderTextExporter : ShaderExporterBase
{
	// This uses CGPROGRAM instead of HLSLPROGRAM because the latter was supposedly introduced in Unity 5.6.
	// https://github.com/UnityCommunity/UnityReleaseNotes/blob/7b417b8ff64415e1e509d8c345b829c7cc11b650/5.6-Beta/5.6.0b1.txt#L143
	private static string FallbackDummyShader { get; } = """

			SubShader{
				Tags { "RenderType" = "Opaque" }
				LOD 200
				CGPROGRAM
		#pragma surface surf Lambert
		#pragma target 3.0
				sampler2D _MainTex;
				// _TintColor declared here to prevent Unity shader compiler from optimizing away the property.
				// Naninovel's TransitionalMaterial.Opacity reads GetColor("_TintColor").a; if optimized away, Opacity becomes 0 and rendering is skipped.
				float4 _TintColor;
				struct Input
				{
					float2 uv_MainTex;
				};
				void surf(Input IN, inout SurfaceOutput o)
				{
					float4 c = tex2D(_MainTex, IN.uv_MainTex);
					o.Albedo = c.rgb * _TintColor.rgb;
					o.Alpha = c.a * _TintColor.a;
				}
				ENDCG
			}

		""".Replace("\r", "");

	public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		return ExportShader((IShader)asset, path, fileSystem);
	}

	public static bool ExportShader(IShader shader, string path, FileSystem fileSystem)
	{
		using Stream fileStream = fileSystem.File.Create(path);
		using InvariantStreamWriter writer = new(fileStream);
		return ExportShader(shader, writer);
	}

	public static bool ExportShader(IShader shader, TextWriter writer)
	{
		// Technically, this outputs invalid shader code for Unity 5.5 because HLSLPROGRAM was not introduced until Unity 5.6.
		if (shader.Has_ParsedForm())
		{
			writer.Write($"Shader \"{shader.ParsedForm.Name}\" {{\n");
			Export(shader.ParsedForm.PropInfo, writer);

			TemplateShader templateShader = TemplateList.GetBestTemplate(shader);

			writer.Write("\t//DummyShaderTextExporter\n");
			string shaderBody;
			if (templateShader != null)
			{
				shaderBody = templateShader.ShaderText;
			}
			else
			{
				shaderBody = FallbackDummyShader;
			}

			// Post-process: ensure _TintColor property is actually used in the shader code.
			// Unity shader compiler strips unused Properties, causing Material.GetColor("_TintColor") to return (0,0,0,0).
			// This makes Naninovel's TransitionalMaterial.Opacity = 0, ShouldRender() = false, and background images don't render.
			if (HasTintColorProperty(shader) && !shaderBody.Contains("_TintColor"))
			{
				shaderBody = InjectTintColorUsage(shaderBody);
			}

			writer.Write(shaderBody);
			writer.Write('\n');

			if (shader.ParsedForm.FallbackName != string.Empty)
			{
				writer.WriteIndent(1);
				writer.Write($"Fallback \"{shader.ParsedForm.FallbackName}\"\n");
			}
			if (shader.ParsedForm.CustomEditorName != string.Empty)
			{
				writer.WriteIndent(1);
				writer.Write($"//CustomEditor \"{shader.ParsedForm.CustomEditorName}\"\n");
			}
			writer.Write('}');
		}
		else
		{
			string header = shader.Script.String;
			int subshaderIndex = header.IndexOf("SubShader");
			if (subshaderIndex < 0)
			{
				return false;
			}
			writer.WriteString(header, 0, subshaderIndex);

			writer.Write("\t//DummyShaderTextExporter\n");
			string fallbackBody = FallbackDummyShader;
			writer.WriteIndent(1);
			writer.Write(fallbackBody);

			writer.Write('}');
		}
		return true;
	}

	private static void Export(ISerializedProperties _this, TextWriter writer)
	{
		writer.WriteIndent(1);
		writer.Write("Properties {\n");
		foreach (ISerializedProperty prop in _this.Props)
		{
			Export(prop, writer);
		}
		writer.WriteIndent(1);
		writer.Write("}\n");
	}

	private static void Export(ISerializedProperty _this, TextWriter writer)
	{
		writer.WriteIndent(2);
		foreach (Utf8String attribute in _this.Attributes)
		{
			writer.Write($"[{attribute}] ");
		}
		SerializedPropertyFlag flags = (SerializedPropertyFlag)_this.Flags;
		if (flags.IsHideInInspector())
		{
			writer.Write("[HideInInspector] ");
		}
		if (flags.IsPerRendererData())
		{
			writer.Write("[PerRendererData] ");
		}
		if (flags.IsNoScaleOffset())
		{
			writer.Write("[NoScaleOffset] ");
		}
		if (flags.IsNormal())
		{
			writer.Write("[Normal] ");
		}
		if (flags.IsHDR())
		{
			writer.Write("[HDR] ");
		}
		if (flags.IsGamma())
		{
			writer.Write("[Gamma] ");
		}

		writer.Write($"{_this.Name} (\"{_this.Description}\", ");

		switch (_this.GetType_())
		{
			case SerializedPropertyType.Color:
			case SerializedPropertyType.Vector:
				writer.Write("Vector");
				break;

			case SerializedPropertyType.Float:
				writer.Write("Float");
				break;

			case SerializedPropertyType.Range:
				writer.Write($"Range({_this.DefValue_1_.ToStringInvariant()}, {_this.DefValue_2_.ToStringInvariant()})");
				break;

			case SerializedPropertyType.Texture:
				switch (_this.DefTexture.TexDim)
				{
					case 1:
						writer.Write("any");
						break;
					case 2:
						writer.Write("2D");
						break;
					case 3:
						writer.Write("3D");
						break;
					case 4:
						writer.Write("Cube");
						break;
					case 5:
						writer.Write("2DArray");
						break;
					case 6:
						writer.Write("CubeArray");
						break;
					default:
						throw new NotSupportedException("Texture dimension isn't supported");

				}
				break;

			case SerializedPropertyType.Int:
				writer.Write("Int");
				break;

			default:
				throw new NotSupportedException($"Serialized property type {_this.Type} isn't supported");
		}
		writer.Write(") = ");

		switch (_this.GetType_())
		{
			case SerializedPropertyType.Color:
			case SerializedPropertyType.Vector:
				writer.Write($"({_this.DefValue_0_.ToStringInvariant()},{_this.DefValue_1_.ToStringInvariant()},{_this.DefValue_2_.ToStringInvariant()},{_this.DefValue_3_.ToStringInvariant()})");
				break;

			case SerializedPropertyType.Float:
			case SerializedPropertyType.Range:
			case SerializedPropertyType.Int:
				writer.Write(_this.DefValue_0_.ToStringInvariant());
				break;

			case SerializedPropertyType.Texture:
				writer.Write($"\"{_this.DefTexture.DefaultName}\" {{}}");
				break;

			default:
				throw new NotSupportedException($"Serialized property type {_this.Type} isn't supported");
		}
		writer.Write('\n');
	}

	/// <summary>
	/// Checks whether the shader has a property named "_TintColor" in its ParsedForm.
	/// </summary>
	private static bool HasTintColorProperty(IShader shader)
	{
		if (!shader.Has_ParsedForm())
		{
			return false;
		}

		AccessListBase<ISerializedProperty>? properties = shader.ParsedForm?.PropInfo?.Props;
		if (properties is null || properties.Count == 0)
		{
			return false;
		}

		foreach (ISerializedProperty prop in properties)
		{
			if (prop.Name == "_TintColor")
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Injects _TintColor declaration and usage into shader body that doesn't reference it.
	/// Handles both vertex/fragment (frag) and surface (surf) shader styles.
	/// </summary>
	private static string InjectTintColorUsage(string shaderBody)
	{
		// Case 1: vertex/fragment shader with frag function returning tex2D(_MainTex, ...)
		if (shaderBody.Contains("return tex2D(_MainTex, input.uv.xy);"))
		{
			shaderBody = shaderBody.Replace(
				"sampler2D _MainTex;",
				"sampler2D _MainTex;\n\t\t\tfloat4 _TintColor; // Injected to prevent property optimization");
			shaderBody = shaderBody.Replace(
				"return tex2D(_MainTex, input.uv.xy);",
				"return tex2D(_MainTex, input.uv.xy) * _TintColor;");
			return shaderBody;
		}

		// Case 2: surface shader with surf function setting o.Albedo = c.rgb
		if (shaderBody.Contains("o.Albedo = c.rgb;"))
		{
			shaderBody = shaderBody.Replace(
				"sampler2D _MainTex;",
				"sampler2D _MainTex;\n\t\t\tfloat4 _TintColor; // Injected to prevent property optimization");
			shaderBody = shaderBody.Replace(
				"o.Albedo = c.rgb;",
				"o.Albedo = c.rgb * _TintColor.rgb;\n\t\t\t\to.Alpha = c.a * _TintColor.a;");
			return shaderBody;
		}

		return shaderBody;
	}
}
