Shader "Hidden/Naninovel/DepthMask" {
	Properties {
		_MainTex ("Main Texture", 2D) = "black" {}
		_DepthAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
	}
	SubShader {
		Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
		LOD 200
		Cull Off ZWrite On ZTest LEqual
		ColorMask 0

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata {
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _DepthAlphaCutoff;

			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.pos);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target {
				float4 tex = tex2D(_MainTex, i.uv);
				clip(tex.a * i.color.a - _DepthAlphaCutoff);
				return 0;
			}
			ENDCG
		}
	}
}