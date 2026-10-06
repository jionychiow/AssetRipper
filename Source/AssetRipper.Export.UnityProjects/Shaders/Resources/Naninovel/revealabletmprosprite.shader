Shader "Naninovel/RevealableTMProSprite" {
	Properties {
		_MainTex ("Sprite Texture", 2D) = "white" {}
		_Color ("Tint", Vector) = (1,1,1,1)
		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_ColorMask ("Color Mask", Float) = 15
		_CullMode ("Cull Mode", Float) = 0
		_ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
		_LineClipRect ("Lines Clip Rect", Vector) = (-32767,-32767,32767,32767)
		_CharClipRect ("Characters Clip Rect", Vector) = (-32767,-32767,32767,32767)
		_CharFadeWidth ("Characters Fade Width", Float) = 0
		_CharSlantAngle ("Characters Slate Angle", Float) = 0
	}
	SubShader {
		Tags {
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
			"CanUseSpriteAtlas"="True"
		}
		Stencil {
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}
		Cull [_CullMode]
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend One OneMinusSrcAlpha
		ColorMask [_ColorMask]
		Pass {
			Name "Default"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#pragma multi_compile __ UNITY_UI_CLIP_RECT
			#pragma multi_compile __ UNITY_UI_ALPHACLIP
			#include "UnityCG.cginc"
			#include "UnityUI.cginc"

			struct appdata_t {
				float4 vertex   : POSITION;
				float4 color    : COLOR;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			struct v2f {
				float4 vertex   : SV_POSITION;
				fixed4 color    : COLOR;
				float2 texcoord  : TEXCOORD0;
				float4 worldPosition : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float4 _ClipRect;
			float4 _LineClipRect;
			float4 _CharClipRect;
			float _CharFadeWidth;
			float _CharSlantAngle;

			v2f vert(appdata_t v) {
				v2f OUT;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.worldPosition = v.vertex;
				OUT.vertex = UnityObjectToClipPos(v.vertex);
				OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				OUT.color = v.color * _Color;
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target {
				half4 color = tex2D(_MainTex, IN.texcoord) * IN.color;
				#ifdef UNITY_UI_CLIP_RECT
				color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif

				float2 wpos = IN.worldPosition.xy;
				float slantOffset = tan(_CharSlantAngle * 3.14159 / 180.0) * (wpos.y - _CharClipRect.y);
				float charLeft = _CharClipRect.x + slantOffset;
				float charRight = _CharClipRect.z + slantOffset;
				if (_CharFadeWidth > 0.001) {
					float fadeL = saturate((wpos.x - charLeft) / _CharFadeWidth);
					float fadeR = saturate((charRight - wpos.x) / _CharFadeWidth);
					color *= fadeL * fadeR;
				} else {
					color *= step(charLeft, wpos.x) * step(wpos.x, charRight);
				}
				color *= step(_LineClipRect.x, wpos.x) * step(wpos.x, _LineClipRect.z);
				color *= step(_LineClipRect.y, wpos.y) * step(wpos.y, _LineClipRect.w);

				#ifdef UNITY_UI_ALPHACLIP
				clip(color.a - 0.001);
				#endif
				return color;
			}
			ENDCG
		}
	}
}