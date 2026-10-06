Shader "Naninovel/FX/Digital Glitch" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_GlitchTex ("Glitch Texture", 2D) = "white" {}
		_Intensity ("Glitch Intensity", Range(0.5, 2)) = 1
		_ColorTint ("Color Tint", Vector) = (0.2,0.2,0,0)
		_BurnColors ("Burn Colors", Range(0, 1)) = 1
		_DodgeColors ("Dodge Colors", Range(0, 1)) = 0
		_PerformUVShifting ("Perform UV Shifting", Range(0, 1)) = 1
		_PerformColorShifting ("Perform Color Shifting", Range(0, 1)) = 1
		_PerformScreenShifting ("Perform Screen Shifting", Range(0, 1)) = 1
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		Cull Off ZWrite Off ZTest Always

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			struct appdata {
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			sampler2D _MainTex;
			sampler2D _GlitchTex;
			float4 _MainTex_ST;
			float _Intensity;
			float4 _ColorTint;
			float _BurnColors;
			float _DodgeColors;
			float _PerformUVShifting;
			float _PerformColorShifting;
			float _PerformScreenShifting;

			float rand(float2 co) {
				return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
			}

			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.pos);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target {
				float2 uv = i.uv;
				float glitch = tex2D(_GlitchTex, uv).r * _Intensity;

				if (_PerformUVShifting > 0.5) {
					float shift = step(0.5, glitch) * glitch * 0.1;
					uv.x += shift;
				}

				float4 color = tex2D(_MainTex, uv);

				if (_PerformColorShifting > 0.5) {
					float r = tex2D(_MainTex, uv + float2(glitch * 0.02, 0)).r;
					float b = tex2D(_MainTex, uv - float2(glitch * 0.02, 0)).b;
					color.r = lerp(color.r, r, glitch);
					color.b = lerp(color.b, b, glitch);
				}

				if (_PerformScreenShifting > 0.5) {
					color.rgb += _ColorTint.rgb * glitch;
				}

				if (_BurnColors > 0.5) {
					color.rgb = lerp(color.rgb, float3(1, 0.3, 0.1), glitch * 0.5);
				}

				if (_DodgeColors > 0.5) {
					color.rgb = lerp(color.rgb, float3(1, 1, 0.8), glitch * 0.3);
				}

				return color;
			}
			ENDCG
		}
	}
}