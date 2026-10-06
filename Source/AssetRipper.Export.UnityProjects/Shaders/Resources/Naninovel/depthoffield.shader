Shader "Naninovel/FX/DepthOfField" {
	Properties {
		_MainTex ("", 2D) = "" {}
		_BlurTex ("", 2D) = "" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		Cull Off ZWrite Off ZTest Always

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
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
			sampler2D _BlurTex;
			float4 _MainTex_ST;

			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.pos);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target {
				float4 main = tex2D(_MainTex, i.uv);
				float4 blur = tex2D(_BlurTex, i.uv);
				return lerp(blur, main, main.a);
			}
			ENDCG
		}
	}
}