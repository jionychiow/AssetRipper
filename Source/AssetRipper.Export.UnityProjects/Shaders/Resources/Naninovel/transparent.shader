Shader "Hidden/Naninovel/Transparent" {
	Properties {
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
		_TintColor ("Tint Color", Vector) = (1,1,1,1)
	}
	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		LOD 200
		Cull Off ZWrite Off ZTest LEqual
		Blend SrcAlpha OneMinusSrcAlpha

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
			float4 _MainTex_ST;
			float4 _TintColor;

			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.pos);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target {
				float4 tex = tex2D(_MainTex, i.uv);
				return tex * _TintColor;
			}
			ENDCG
		}
	}
}