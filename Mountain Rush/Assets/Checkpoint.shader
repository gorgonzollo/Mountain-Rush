Shader "Custom/CheckpointShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 1, 0.8, 0.5)
        _PulseSpeed ("Pulse Speed", Range(0.1, 5)) = 1
        _PulseIntensity ("Pulse Intensity", Range(0, 1)) = 0.3
        _PulseScale ("Pulse Scale", Range(0, 1)) = 0.1
        _MoveAmplitude ("Move Amplitude", Range(0, 1)) = 0.2
        _RimPower ("Rim Power", Range(1, 10)) = 5
        _FadeStart ("Fade Start Distance", Float) = 1.5
        _FadeEnd ("Fade End Distance", Float) = 4.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _PulseSpeed;
                float _PulseIntensity;
                float _PulseScale;
                float _MoveAmplitude;
                float _RimPower;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float time = _Time.y * _PulseSpeed;

                // Пульсация масштаба
                float pulse = 1.0 + _PulseScale * _PulseIntensity * sin(time);
                float3 pos = IN.positionOS.xyz * pulse;

                // Движение вверх-вниз
                float moveY = sin(time) * _MoveAmplitude;
                pos.y += moveY;

                // Вращение вокруг оси Y
                float angle = time;
                float cosA = cos(angle);
                float sinA = sin(angle);
                float3 rotatedPos;
                rotatedPos.x = pos.x * cosA - pos.z * sinA;
                rotatedPos.z = pos.x * sinA + pos.z * cosA;
                rotatedPos.y = pos.y;

                float3 worldPos = TransformObjectToWorld(rotatedPos);
                OUT.worldPos = worldPos;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(rotatedPos);
                OUT.positionHCS = positionInputs.positionCS;

                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.normalWS = normalInputs.normalWS;
                OUT.viewDirWS = GetWorldSpaceViewDir(worldPos);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);

                // Rim light
                float rim = 1.0 - saturate(dot(viewDirWS, normalWS));
                rim = pow(rim, _RimPower);

                // Пульсация цвета
                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                pulse = lerp(1.0, pulse, _PulseIntensity);

                float4 color = _BaseColor;
                color.rgb *= (1.0 + rim * pulse);

                // Camera fade (исчезает при приближении)
                float3 camPos = _WorldSpaceCameraPos;
                float dist = distance(camPos, IN.worldPos);
                float fade = smoothstep(_FadeStart, _FadeEnd, dist); // 0 при близком расстоянии, 1 вдали
                color.a *= fade;

                return color;
            }
            ENDHLSL
        }
    }
}
