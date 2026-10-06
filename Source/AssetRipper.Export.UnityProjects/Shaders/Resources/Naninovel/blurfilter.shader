Shader "Hidden/Naninovel/BlurFilter" {
	Properties {
		_MainTex ("-", 2D) = "white" {}
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
				float4 uv01 : TEXCOORD1;
				float4 uv23 : TEXCOORD2;
				float4 uv45 : TEXCOORD3;
			};

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float _BlurSize;

			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.pos);
				o.uv = v.uv;
				float2 off = _MainTex_TexelSize.xy * _BlurSize;
				o.uv01 = float4(v.uv + float2(-off.x, -off.y), v.uv + float2(off.x, -off.y));
				o.uv23 = float4(v.uv + float2(-off.x,  off.y), v.uv + float2(off.x,  off.y));
				o.uv45 = float4(v.uv + float2(0, -off.y * 2.0), v.uv + float2(0, off.y * 2.0));
				return o;
			}

			fixed4 frag(v2f i) : SV_Target {
				float4 c = tex2D(_MainTex, i.uv) * 0.25;
				c += tex2D(_MainTex, i.uv01.xy) * 0.125;
				c += tex2D(_MainTex, i.uv01.zw) * 0.125;
				c += tex2D(_MainTex, i.uv23.xy) * 0.125;
				c += tex2D(_MainTex, i.uv23.zw) * 0.125;
				c += tex2D(_MainTex, i.uv45.xy) * 0.0625;
				c += tex2D(_MainTex, i.uv45.zw) * 0.0625;
				return c;
			}
			ENDCG
		}
	}
}