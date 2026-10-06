namespace AssetRipper.Export.UnityProjects.Shaders.Enhance;

internal static class TransitionalShaderTemplate
{
	public static string GetTransitionFragment()
	{
		return """
			sampler2D _TransitionTex;
			float _TransitionProgress;
			float4 _TransitionParams;

			float4 ApplyTransition(float4 mainColor, float2 uv)
			{
				float4 transitionColor = tex2D(_TransitionTex, uv);
				return lerp(mainColor, transitionColor, _TransitionProgress);
			}
""";
	}

	public static string GetEnhancedShaderBody()
	{
		return """
SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = UnityObjectToClipPos(input.pos);
				return output;
			}

			sampler2D _MainTex;
			sampler2D _TransitionTex;
			float _TransitionProgress;
			float4 _TransitionParams;
			float4 _TintColor;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				float4 mainColor = tex2D(_MainTex, input.uv.xy) * _TintColor;
				float4 transitionColor = tex2D(_TransitionTex, input.uv.xy);
				return lerp(mainColor, transitionColor, _TransitionProgress);
			}

			ENDCG
		}
	}
""";
	}
}