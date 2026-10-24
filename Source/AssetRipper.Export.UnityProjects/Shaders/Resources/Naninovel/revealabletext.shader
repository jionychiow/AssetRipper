Shader "Naninovel/RevealableText" {
	Properties {
		[PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
		_Color ("Tint", Vector) = (1,1,1,1)
		[HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
		[HideInInspector] _Stencil ("Stencil ID", Float) = 0
		[HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
		[HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
		[HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
		[HideInInspector] _ColorMask ("Color Mask", Float) = 15
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
		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha
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
				// 2026-10-24 黑字根因修复：动态字体 Font Texture 为 Alpha8 格式，其 RGB 通道
				// 在 GPU 采样时行为不稳定（本环境实测 RGB=0，字形覆盖度仅在 alpha 通道）。
				// 原 tex2D*IN.color 在 RGB=0 时输出黑字。改用 alpha-as-intensity（Unity 内置
				// 字体 shader 同款做法）：字形 alpha 决定透明度，顶点色决定颜色——对任何
				// Alpha8 RGB 展开行为均稳健，文字颜色 = Text.color（白）。
				half4 tex = tex2D(_MainTex, IN.texcoord);
				half4 color;
				color.rgb = IN.color.rgb;
				color.a = tex.a * IN.color.a;
				#ifdef UNITY_UI_CLIP_RECT
				color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif

				float2 wpos = IN.worldPosition.xy;
				float charClipWidth = _CharClipRect.z - _CharClipRect.x;
				if (_CharClipRect.z > _CharClipRect.x && charClipWidth < 60000.0) {
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
				}
				float lineClipWidth = _LineClipRect.z - _LineClipRect.x;
				float lineClipHeight = _LineClipRect.w - _LineClipRect.y;
				if (_LineClipRect.z > _LineClipRect.x && _LineClipRect.w > _LineClipRect.y && lineClipWidth < 60000.0 && lineClipHeight < 60000.0) {
					color *= step(_LineClipRect.x, wpos.x) * step(wpos.x, _LineClipRect.z);
					color *= step(_LineClipRect.y, wpos.y) * step(wpos.y, _LineClipRect.w);
				}

				#ifdef UNITY_UI_ALPHACLIP
				clip(color.a - 0.001);
				#endif
				return color;
			}
			ENDCG
		}
	}
}