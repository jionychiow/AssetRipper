using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Project;

public sealed class NaninovelResourcePatcherPostExporter : IPostExporter
{
	private readonly NaninovelPatchSettings _patchSettings;

	public NaninovelResourcePatcherPostExporter() : this(new NaninovelPatchSettings()) { }

	public NaninovelResourcePatcherPostExporter(NaninovelPatchSettings settings)
	{
		_patchSettings = settings;
	}

	private const string NaninovelRuntimeDllName = "Elringus.Naninovel.Runtime.dll";
	private const string ProjectResourcesTypeName = "ProjectResources";
	private const string GetMethodName = "Get";
	private const string LocateAllResourcesMethodName = "LocateAllResources";
	private const string ResourcesFolderName = "Resources";
	private const string UnityCommonFolderName = "UnityCommon";

	private static readonly string[] NaninovelDllsToSync =
	[
		"Naninovel.Parsing.dll",
		"Naninovel.Lexing.dll",
		"Naninovel.NCalc.dll",
		"Elringus.Naninovel.Runtime.dll",
		"Elringus.NaninovelInventory.Runtime.dll",
		"UniRx.Async.dll",
	];

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string pluginsPath = fileSystem.Path.Join(settings.AssetsPath, "Plugins", "GameLibs");
		if (!fileSystem.Directory.Exists(pluginsPath))
		{
			return;
		}

		string dllPath = fileSystem.Path.Join(pluginsPath, NaninovelRuntimeDllName);
		if (!fileSystem.File.Exists(dllPath))
		{
			Logger.Warning(LogCategory.Export, $"Naninovel runtime DLL not found at {dllPath}");
			return;
		}

		FixResourceFolderCasing(settings.AssetsPath, fileSystem);

		TouchAllRuntimeScripts(settings.AssetsPath, fileSystem);

		bool dllWasSynced = SyncNaninovelDlls(gameData, settings, pluginsPath, fileSystem);

		try
		{
			PatchNaninovelDll(dllPath, _patchSettings);
			Logger.Info(LogCategory.Export, "Successfully patched Naninovel ProjectResources.Get() to always use Resources.Load");
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"Failed to patch Naninovel DLL at {dllPath}: {ex}");
		}

		if (_patchSettings.EnableUniRxAsyncPatch)
		{
			try
			{
				string uniRxAsyncDllPath = fileSystem.Path.Join(pluginsPath, "UniRx.Async.dll");
				if (fileSystem.File.Exists(uniRxAsyncDllPath))
				{
					PatchUniRxAsyncDll(uniRxAsyncDllPath, _patchSettings);
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to patch UniRx.Async.dll at {fileSystem.Path.Join(pluginsPath, "UniRx.Async.dll")}: {ex}");
			}
		}
		else
		{
			Logger.Info(LogCategory.Export, "UniRx.Async.dll patching disabled, skipping.");
		}

		RemoveAddressableResourceProvider(settings.AssetsPath, fileSystem);
		RemoveEditorAssembliesFromConfig(settings.AssetsPath, fileSystem);
		SanitizeProviderTypes(settings.AssetsPath, fileSystem);

 		FixCorruptedPngFiles(settings.AssetsPath, fileSystem);
 		FixPrefabEffectColorBlack(settings.AssetsPath, fileSystem);

		GenerateRevealableTextFixerScript(settings.AssetsPath, fileSystem);

		if (_patchSettings.EnableRenderOrderFix)
		{
			FixWidePrefabRenderOrder(settings.AssetsPath, fileSystem, _patchSettings.TargetCanvasSortingOrder);
			FixNaninovelShaderRenderConfig(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableRuntimeRenderOrderFixer)
		{
			GenerateDialogueRenderOrderFixer(settings.AssetsPath, fileSystem, _patchSettings);
		}
		if (_patchSettings.EnableRenderOrderDiagnostic)
		{
			GenerateRenderOrderDiagnosticScript(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableShaderIncludeFix)
		{
			FixNaninovelShaderIncludePath(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableTMProShaderThresholdFix)
		{
			FixTMProShaderThreshold(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableAudioListenerFix)
		{
			GenerateAudioListenerFixer(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableOriginalBaselineCompare)
		{
			try
			{
				GenerateBaselineCollectorScript(settings.AssetsPath, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to generate BaselineCollector.cs: {ex}");
			}
		}

		InvalidateUnityLibraryCache(settings, fileSystem);
	}

	private static void GenerateRevealableTextFixerScript(string assetsPath, FileSystem fileSystem)
	{
		string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
		if (!fileSystem.Directory.Exists(editorDir))
		{
			Directory.CreateDirectory(editorDir);
		}

		string scriptPath = fileSystem.Path.Join(editorDir, "RevealableTextFixer.cs");
		string scriptContent = """"
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

[InitializeOnLoad]
public static class RevealableTextFixer
{
    private static Type revealableTextType;
    private static int charClipRectId;
    private static bool initialized;

    static RevealableTextFixer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            initialized = false;
            EditorApplication.update += FixRevealableText;
        }
        else if (change == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= FixRevealableText;
        }
    }

    private static void Initialize()
    {
        revealableTextType = null;
        charClipRectId = Shader.PropertyToID("_CharClipRect");

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Naninovel.UI.RevealableUIText");
            if (t != null)
            {
                revealableTextType = t;
                break;
            }
        }

        initialized = true;
    }

    private static void FixRevealableText()
    {
        if (!EditorApplication.isPlaying) return;
        if (!initialized) Initialize();
        if (revealableTextType == null) return;

        try
        {
            var components = UnityEngine.Object.FindObjectsOfType(revealableTextType);
            foreach (var comp in components)
            {
                var graphic = comp as Graphic;
                if (graphic == null) continue;

                var mat = graphic.material;
                if (mat == null) continue;

                var charClip = mat.GetVector(charClipRectId);
                if (Mathf.Approximately(charClip.x, 2000f))
                {
                    charClip.x = -32767f;
                    mat.SetVector(charClipRectId, charClip);
                }
            }
        }
        catch { }
    }
}
"""";

		File.WriteAllText(scriptPath, scriptContent);
		Logger.Info(LogCategory.Export, $"Generated RevealableTextFixer.cs at {scriptPath}");
	}

	private static void FixWidePrefabRenderOrder(string assetsPath, FileSystem fileSystem, int targetSortingOrder)
	{
		try
		{
			string prefabPath = null;
			string[] searchPaths =
			[
				fileSystem.Path.Join(assetsPath, "Resources", "TextPrinters", "Wide.prefab"),
				fileSystem.Path.Join(assetsPath, "Resources", "myui", "textprinters 1", "Wide.prefab"),
			];
			foreach (string path in searchPaths)
			{
				if (fileSystem.File.Exists(path))
				{
					prefabPath = path;
					break;
				}
			}
			if (prefabPath == null)
			{
				Logger.Warning(LogCategory.Export, "Wide.prefab not found, skipping render order fix");
				return;
			}

			string content = File.ReadAllText(prefabPath);
			bool modified = false;

			string canvasBlockMarker = "--- !u!223 &223344419225485095";
			int canvasStart = content.IndexOf(canvasBlockMarker, StringComparison.Ordinal);
			if (canvasStart >= 0)
			{
				int nextSeparator = content.IndexOf("\n--- ", canvasStart + 1, StringComparison.Ordinal);
				if (nextSeparator < 0) nextSeparator = content.Length;
				int sortLineStart = content.LastIndexOf("\n  m_SortingOrder:", nextSeparator, StringComparison.Ordinal);
				if (sortLineStart >= 0)
				{
					int lineEnd = content.IndexOf('\n', sortLineStart + 1);
					string oldLine = content.Substring(sortLineStart + 1, lineEnd - sortLineStart - 1);
					string newLine = $"  m_SortingOrder: {targetSortingOrder}";
					if (oldLine.Trim() != newLine.Trim())
					{
						content = content.Substring(0, sortLineStart + 1) + newLine + content.Substring(lineEnd);
						Logger.Info(LogCategory.Export, $"Wide.prefab Canvas sortingOrder: {oldLine.Trim()} -> {newLine.Trim()}");
						modified = true;
					}
				}
			}

			if (modified)
			{
				File.WriteAllText(prefabPath, content);
				Logger.Info(LogCategory.Export, $"Fixed Wide.prefab render order at {prefabPath}");
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix Wide.prefab render order: {ex}");
		}
	}

	private static void FixNaninovelShaderRenderConfig(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string shaderDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "shaders");
			if (!fileSystem.Directory.Exists(shaderDir))
			{
				Logger.Warning(LogCategory.Export, $"Naninovel shader directory not found: {shaderDir}");
				return;
			}

			FixShaderFile(fileSystem.Path.Join(shaderDir, "revealabletext.shader"), "Transparent", "Always", "Off", null);
			FixShaderFile(fileSystem.Path.Join(shaderDir, "transitionaltexture.shader"), "Background", "LEqual", "On", null);
			FixShaderFile(fileSystem.Path.Join(shaderDir, "transparent.shader"), "Transparent", "LEqual", "Off", null);
			FixShaderFile(fileSystem.Path.Join(shaderDir, "depthmask.shader"), "AlphaTest", "LEqual", "On", "0");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix Naninovel shader render config: {ex}");
		}
	}

	private static void FixShaderFile(string shaderPath, string queue, string zTest, string zWrite, string colorMask)
	{
		try
		{
			if (!File.Exists(shaderPath)) return;
			string content = File.ReadAllText(shaderPath);
			bool modified = false;

			string newQueue = $"\"Queue\"=\"{queue}\"";
			int queueIdx = content.IndexOf("\"Queue\"=", StringComparison.Ordinal);
			if (queueIdx >= 0)
			{
				int lineEnd = content.IndexOf('\n', queueIdx);
				int lineStart = content.LastIndexOf('\n', queueIdx) + 1;
				string oldLine = content.Substring(lineStart, lineEnd - lineStart).Trim();
				if (!oldLine.Contains(newQueue))
				{
					content = content.Substring(0, lineStart) + newQueue + content.Substring(lineEnd);
					modified = true;
				}
			}

			content = ReplaceShaderDirective(content, "ZTest", zTest, ref modified);
			content = ReplaceShaderDirective(content, "ZWrite", zWrite, ref modified);

			if (colorMask != null)
			{
				content = ReplaceShaderDirective(content, "ColorMask", colorMask, ref modified);
			}

			if (modified)
			{
				File.WriteAllText(shaderPath, content);
				Logger.Info(LogCategory.Export, $"Fixed shader config: {Path.GetFileName(shaderPath)}");
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix shader {shaderPath}: {ex}");
		}
	}

	private static string ReplaceShaderDirective(string content, string directive, string value, ref bool modified)
	{
		string pattern = $"{directive} ";
		int idx = content.IndexOf(pattern, StringComparison.Ordinal);
		while (idx >= 0)
		{
			int lineStart = content.LastIndexOf('\n', idx) + 1;
			int lineEnd = content.IndexOf('\n', idx);
			if (lineEnd < 0) lineEnd = content.Length;
			string oldLine = content.Substring(lineStart, lineEnd - lineStart).Trim();
			string newDirective = $"{directive} {value}";
			if (oldLine != newDirective && !oldLine.StartsWith("//"))
			{
				content = content.Substring(0, lineStart) + newDirective + content.Substring(lineEnd);
				modified = true;
				return content;
			}
			idx = content.IndexOf(pattern, idx + 1, StringComparison.Ordinal);
		}
		return content;
	}

	private static void FixNaninovelShaderIncludePath(string assetsPath, FileSystem fileSystem)
	{
		try
		{
		string naniShaderDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "shaders");
		string tmpShaderDir = fileSystem.Path.Join(assetsPath, "TextMesh Pro", "Resources", "Shaders");
		if (!Directory.Exists(naniShaderDir) || !Directory.Exists(tmpShaderDir))
		{
			Logger.Warning(LogCategory.Export, $"Shader directories not found: nani={naniShaderDir}, tmp={tmpShaderDir}");
			return;
		}

		string[] cgincFiles = ["TMPro_Properties.cginc", "TMPro.cginc", "TMPro_Surface.cginc"];
		foreach (string cgincFile in cgincFiles)
		{
			string srcPath = Path.Combine(tmpShaderDir, cgincFile);
			string dstPath = Path.Combine(naniShaderDir, cgincFile);
			if (File.Exists(srcPath) && !File.Exists(dstPath))
			{
				File.Copy(srcPath, dstPath);
				Logger.Info(LogCategory.Export, $"Copied {cgincFile} to Naninovel shader directory");
			}
		}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix shader include paths: {ex}");
		}
	}

	private static void FixTMProShaderThreshold(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string shaderPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "shaders", "revealabletmprotext.shader");
			if (!fileSystem.File.Exists(shaderPath))
			{
				Logger.Warning(LogCategory.Export, $"RevealableTMProText shader not found at {shaderPath}");
				return;
			}

			string shaderContent = File.ReadAllText(shaderPath);
			if (shaderContent.Contains("charClipWidth < 60000.0"))
			{
				Logger.Info(LogCategory.Export, "RevealableTMProText shader already has 60000 threshold check, skipping.");
				return;
			}

			string oldCharClip = """
				float2 wpos = input.worldPos.xy;
				float slantOffset = tan(_CharSlantAngle * 3.14159 / 180.0) * (wpos.y - _CharClipRect.y);
				float charLeft = _CharClipRect.x + slantOffset;
				float charRight = _CharClipRect.z + slantOffset;
				if (_CharFadeWidth > 0.001) {
					float fadeL = saturate((wpos.x - charLeft) / _CharFadeWidth);
					float fadeR = saturate((charRight - wpos.x) / _CharFadeWidth);
					c *= fadeL * fadeR;
				} else {
					c *= step(charLeft, wpos.x) * step(wpos.x, charRight);
				}
				c *= step(_LineClipRect.x, wpos.x) * step(wpos.x, _LineClipRect.z);
				c *= step(_LineClipRect.y, wpos.y) * step(wpos.y, _LineClipRect.w);
""";

			string newCharClip = """
				float2 wpos = input.worldPos.xy;
				float charClipWidth = _CharClipRect.z - _CharClipRect.x;
				if (_CharClipRect.z > _CharClipRect.x && charClipWidth < 60000.0) {
					float slantOffset = tan(_CharSlantAngle * 3.14159 / 180.0) * (wpos.y - _CharClipRect.y);
					float charLeft = _CharClipRect.x + slantOffset;
					float charRight = _CharClipRect.z + slantOffset;
					if (_CharFadeWidth > 0.001) {
						float fadeL = saturate((wpos.x - charLeft) / _CharFadeWidth);
						float fadeR = saturate((charRight - wpos.x) / _CharFadeWidth);
						c *= fadeL * fadeR;
					} else {
						c *= step(charLeft, wpos.x) * step(wpos.x, charRight);
					}
				}
				float lineClipWidth = _LineClipRect.z - _LineClipRect.x;
				float lineClipHeight = _LineClipRect.w - _LineClipRect.y;
				if (_LineClipRect.z > _LineClipRect.x && _LineClipRect.w > _LineClipRect.y && lineClipWidth < 60000.0 && lineClipHeight < 60000.0) {
					c *= step(_LineClipRect.x, wpos.x) * step(wpos.x, _LineClipRect.z);
					c *= step(_LineClipRect.y, wpos.y) * step(wpos.y, _LineClipRect.w);
				}
""";

			if (shaderContent.Contains(oldCharClip))
			{
				shaderContent = shaderContent.Replace(oldCharClip, newCharClip);
				File.WriteAllText(shaderPath, shaderContent);
				Logger.Info(LogCategory.Export, "Successfully added 60000 threshold check to RevealableTMProText shader.");
			}
			else
			{
				Logger.Warning(LogCategory.Export, "Could not find expected char clip pattern in RevealableTMProText shader, skipping threshold fix.");
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix TMPro shader threshold: {ex}");
		}
	}

	private static void GenerateAudioListenerFixer(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string scriptPath = fileSystem.Path.Join(editorDir, "AudioListenerFixer.cs");
			string scriptContent = """
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class AudioListenerFixer
{
    static AudioListenerFixer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            EnsureAudioListener();
        }
    }

    private static void EnsureAudioListener()
    {
        var listeners = Object.FindObjectsOfType<AudioListener>();
        if (listeners == null || listeners.Length == 0)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.gameObject.AddComponent<AudioListener>();
                Debug.Log("[AudioListenerFixer] Added AudioListener to main camera");
            }
            else
            {
                var anyCam = Object.FindObjectsOfType<Camera>();
                if (anyCam != null && anyCam.Length > 0)
                {
                    anyCam[0].gameObject.AddComponent<AudioListener>();
                    Debug.Log($"[AudioListenerFixer] Added AudioListener to camera: {anyCam[0].name}");
                }
            }
        }
        else if (listeners.Length > 1)
        {
            for (int i = 1; i < listeners.Length; i++)
            {
                Debug.Log($"[AudioListenerFixer] Disabling duplicate AudioListener on: {listeners[i].gameObject.name}");
                listeners[i].enabled = false;
            }
        }
    }
}
""";
			File.WriteAllText(scriptPath, scriptContent);
			Logger.Info(LogCategory.Export, $"Generated AudioListenerFixer.cs at {scriptPath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to generate AudioListenerFixer.cs: {ex}");
		}
	}

	private static void GenerateDialogueRenderOrderFixer(string assetsPath, FileSystem fileSystem, NaninovelPatchSettings patchSettings)
	{
		try
		{
			string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			int targetSortingOrder = patchSettings.TargetCanvasSortingOrder;
			bool enableRuntimeDiagnosis = patchSettings.EnableRuntimeDiagnosis;
			bool enableObscurationFix = patchSettings.EnableObscurationFix;
			bool enableDiagnosisFixLoop = patchSettings.EnableDiagnosisFixLoop;
		bool enableOriginalBaselineCompare = patchSettings.EnableOriginalBaselineCompare;
		bool enableTextVisibilityDiagnosis = patchSettings.EnableTextVisibilityDiagnosis;
		bool enableTextVisibilityFix = patchSettings.EnableTextVisibilityFix;
		bool enableTextVisibilityFixLoop = patchSettings.EnableTextVisibilityFixLoop;
		int diagnosisFixLoopMaxRounds = patchSettings.DiagnosisFixLoopMaxRounds;
			int diagnosisEarlyFrameCount = patchSettings.DiagnosisEarlyFrameCount;
			int diagnosisSteadyFrameInterval = patchSettings.DiagnosisSteadyFrameInterval;
			int fixEarlyFrameCount = patchSettings.FixEarlyFrameCount;
			int fixSteadyFrameInterval = patchSettings.FixSteadyFrameInterval;
			float clipRectOOBThreshold = patchSettings.ClipRectOOBThreshold;
			string expectedParentChain = patchSettings.ExpectedParentChain;
			bool enableTMProShaderDiagnosis = patchSettings.EnableTMProShaderDiagnosis;
			bool enableResourcePathDiagnosis = patchSettings.EnableResourcePathDiagnosis;
			bool enableErrorContentCollection = patchSettings.EnableErrorContentCollection;
			bool enableEngineInitDiagnosis = patchSettings.EnableEngineInitDiagnosis;
			bool enableEngineInitFix = patchSettings.EnableEngineInitFix;
			bool enableComponentTypeDiagnosis = patchSettings.EnableComponentTypeDiagnosis;
			bool enableTMProShaderThresholdFix = patchSettings.EnableTMProShaderThresholdFix;
			bool enableResourcePathFix = patchSettings.EnableResourcePathFix;
			int errorCollectionMaxCount = patchSettings.ErrorCollectionMaxCount;
			string safeClipRectDefault = patchSettings.SafeClipRectDefault;
			bool enableShadowDiagnosis = patchSettings.EnableShadowDiagnosis;
			bool enableVertexDataDiagnosis = patchSettings.EnableVertexDataDiagnosis;
			bool enableColorTemporalTrace = patchSettings.EnableColorTemporalTrace;
			bool enableOnAuthorChangedTrace = patchSettings.EnableOnAuthorChangedTrace;
			bool enableMainTexMatchCheck = patchSettings.EnableMainTexMatchCheck;
			bool enableScriptPlayerTriggerFix = patchSettings.EnableScriptPlayerTriggerFix;
			bool enableVariableReplaceFix = patchSettings.EnableVariableReplaceFix;
			bool enableRuntimeErrorFix = patchSettings.EnableRuntimeErrorFix;
			bool enableHasMovedRootCauseAnalysis = patchSettings.EnableHasMovedRootCauseAnalysis;

			string scriptPath = fileSystem.Path.Join(editorDir, "DialogueRenderOrderFixer.cs");
			string scriptContent = $$"""
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

#pragma warning disable CS0414
#pragma warning disable CS0169
#pragma warning disable CS0649
#pragma warning disable CS0618

[InitializeOnLoad]
public static class DialogueRenderOrderFixer
{
    private static bool isPlaying;
    private static int frameCount;
    private static Type revealableTextType;
    private static Type revealableTMProType;
    private static int charClipRectId;
    private static bool enableRuntimeDiagnosis = {{(enableRuntimeDiagnosis ? "true" : "false")}};
    private static bool enableObscurationFix = {{(enableObscurationFix ? "true" : "false")}};
    private static bool enableDiagnosisFixLoop = {{(enableDiagnosisFixLoop ? "true" : "false")}};
    private static bool enableOriginalBaselineCompare = {{(enableOriginalBaselineCompare ? "true" : "false")}};
    private static int diagnosisFixLoopMaxRounds = {{diagnosisFixLoopMaxRounds}};
    private static int diagnosisEarlyFrameCount = {{diagnosisEarlyFrameCount}};
    private static int diagnosisSteadyFrameInterval = {{diagnosisSteadyFrameInterval}};
    private static int fixEarlyFrameCount = {{fixEarlyFrameCount}};
    private static int fixSteadyFrameInterval = {{fixSteadyFrameInterval}};
    private static float clipRectOOBThreshold = {{clipRectOOBThreshold}}f;
    private static string expectedParentChain = "{{expectedParentChain}}";
    private static BaselineSnapshot baseline;
    private static DiagnosisFixLoopContext loopContext;
    private static int consecutiveVisibleFrames;
    private static bool enableTextVisibilityDiagnosis = {{(enableTextVisibilityDiagnosis ? "true" : "false")}};
    private static bool enableTextVisibilityFix = {{(enableTextVisibilityFix ? "true" : "false")}};
    private static bool enableTextVisibilityFixLoop = {{(enableTextVisibilityFixLoop ? "true" : "false")}};
    private static bool enableTMProShaderDiagnosis = {{(enableTMProShaderDiagnosis ? "true" : "false")}};
    private static bool enableResourcePathDiagnosis = {{(enableResourcePathDiagnosis ? "true" : "false")}};
    private static bool enableErrorContentCollection = {{(enableErrorContentCollection ? "true" : "false")}};
    private static bool enableEngineInitDiagnosis = {{(enableEngineInitDiagnosis ? "true" : "false")}};
    private static bool enableEngineInitFix = {{(enableEngineInitFix ? "true" : "false")}};
    private static bool enableComponentTypeDiagnosis = {{(enableComponentTypeDiagnosis ? "true" : "false")}};
    private static bool enableTMProShaderThresholdFix = {{(enableTMProShaderThresholdFix ? "true" : "false")}};
    private static bool enableResourcePathFix = {{(enableResourcePathFix ? "true" : "false")}};
    private static int errorCollectionMaxCount = {{errorCollectionMaxCount}};
    private static string safeClipRectDefault = "{{safeClipRectDefault}}";
    private static bool enableShadowDiagnosis = {{(enableShadowDiagnosis ? "true" : "false")}};
    private static bool enableVertexDataDiagnosis = {{(enableVertexDataDiagnosis ? "true" : "false")}};
    private static bool enableColorTemporalTrace = {{(enableColorTemporalTrace ? "true" : "false")}};
    private static bool enableOnAuthorChangedTrace = {{(enableOnAuthorChangedTrace ? "true" : "false")}};
    private static bool enableMainTexMatchCheck = {{(enableMainTexMatchCheck ? "true" : "false")}};
    private static bool enableScriptPlayerTriggerFix = {{(enableScriptPlayerTriggerFix ? "true" : "false")}};
    private static bool enableVariableReplaceFix = {{(enableVariableReplaceFix ? "true" : "false")}};
    private static bool enableRuntimeErrorFix = {{(enableRuntimeErrorFix ? "true" : "false")}};
    private static bool enableHasMovedRootCauseAnalysis = {{(enableHasMovedRootCauseAnalysis ? "true" : "false")}};
    private static HashSet<string> loggedErrors;
    private static int lineClipRectId;
    private static Type textRevealBehaviourType;
    private static Type engineType;
    private static Type printManagerType;
    private static bool printerActiveLogged;
    private static Dictionary<string, Vector4> prevCharClipRects;
    private static Dictionary<string, Vector4> prevLineClipRects;
    private static RootCauseType previousRoundCause;
    private static int textVisConsecutiveVisibleFrames;
    private static float prevTextAlpha = 1f;
    private static bool engineInitCalled;
    private static int engineInitCallFrame;
    private static int engineInitWaitFrameCount;
    private static Type runtimeInitializerType;
    private static Type configProviderType;
    private static Type runtimeBehaviourType;
    private static int currentInitPlan;
    private static bool triedPlanA;
    private static bool triedPlanB;
    private static bool triedPlanC;
    private static Type scriptPlayerType;
    private static Type customVariablesType;
    private static bool scriptPlayerExecCalled;
    private static bool variableReplaceCalled;
    private static bool callbackRegistered;
    private static int scriptPlayerWaitFrameCount;
    private static List<ErrorContentEntry> realtimeErrorEntries;
    private static bool prevEngineInitialized;
    private static int periodicDefaultColorResetFrameCount;
    private static bool hasMovedAnalysisCalled;
    private static string lastHasMovedTextState;
    private static int lastErrorCount;
    private static int prevTextContentLength;
    private static float prevMaterialAlpha = 1f;
    private static string prevTextContent = "";
    private static bool temporalTraceInitialized;
    private static bool engineInitConfirmed;
    private static bool revealableFixCalled;
    private static int revealableFixWaitFrameCount;
    private static bool revealableFixFailed;
    private static int findObjectTypeSuppressCount;
    private static int batchErrorSuppressCount;
    private static Dictionary<string, int> errorOccurrenceCounts;
    private static int errorOccurrenceMaxCount = 2000;
    private static HashSet<int> stencilFixedMaterialIds;
    private static int stencilFixCount;
    private static bool enableShadowDirectFix = true;
    private static bool enableRevealableUITextFix = true;
    private static bool enableStencilPropertyFix = false;
    private static bool enableTextPreviewDiagnosis = true;
    private static bool enableFindObjectTypeSuppress = true;
    private static bool enableBatchErrorSuppress = true;
    private static int textPreviewDiagFrameCount;
    private static Type cachedTextPrinterManagerType;
    private static Type cachedITextPrinterType;
    private static bool textPreviewTypeLookupDone;
    private static int shadowContinuousFixCount;
    private static int shadowContinuousFixThreshold = 30;
    private static bool shadowDisabled;
    private static bool enableShadowContinuousFix = true;
    private static bool enableShadowDisable = true;
    private static MethodInfo cachedRebuildMethod;
    private static ParameterInfo[] cachedRebuildParamInfos;
    private static bool rebuildSignatureChecked;
    private static bool placeholderDiagCalled;
    private static int placeholderDiagWaitFrameCount;
    private static bool placeholderTextLoaded;
    private static int placeholderTextWarnCount;
    private static int revealFadeFixCount;
    private static int revealFadeFixFrameCounter;
    private static HashSet<int> revealFadeFixedInstanceIds = new HashSet<int>();
    private static bool enablePlaceholderTextDiag = true;
    private static int stencilErrorSuppressCount;
    private static bool enableStencilErrorSuppress = true;
    private static bool hasMovedAlternativeFixCalled;
    private static MethodInfo[] cachedScriptPlayerMethods = null;
    private static MethodInfo cachedScriptPlayerExecMethod = null;
    private static ParameterInfo[] cachedScriptPlayerExecMethodParams = null;
    private static bool scriptPlayerMethodsEnumerated = false;
    private static bool enableScriptPlayerMethodEnum = true;
    private static MethodInfo cachedScriptPlayerLoadMethod = null;
    private static ParameterInfo[] cachedScriptPlayerLoadMethodParams = null;
    private static bool scriptLoadMethodFound = false;
    private static string cachedScriptFileName = null;
    private static Dictionary<string, PropertyInfo> cachedScriptPlayerStateProps = null;
    private static bool scriptAlreadyExecuted = false;
    private static bool scriptExecTriggered = false;
    private static bool enableScriptPlayerStateCheck = true;
    private static string cachedStartupTriggerMechanism = null;
    private static bool startupCheckDone = false;
    private static bool enableStartupTriggerCheck = true;
    private static int scriptExecVerifyWaitFrameCount = 0;
    private static bool scriptExecVerifyDone = false;
    private static bool revealableTimingFixApplied = false;
    private static bool contentResetTriggered = false;
    private static int contentResetWaitFrameCount = 0;
    private static bool enableContentResetTrigger = true;
    private static int hasMovedRootCauseVerifyWaitFrameCount = 0;
    private static bool hasMovedRootCauseVerifyCalled = false;
    private static bool hasMovedRootCauseVerifyFailed = false;
    private static bool enableHasMovedRootCauseVerify = true;
    private static bool configColorFieldCheckDone = false;
    private static bool configDiffCheckDone = false;
    private static bool revealableColorCheckDone = false;
    private static bool printerInitColorCheckDone = false;
    private static bool naniScriptColorCheckDone = false;
    private static bool prefabCompCheckDone = false;
    private static int textColorBlackFixCount = 0;
    private static int textColorBlackFixThreshold = 100;
    private static bool textColorOverrideDisabled = false;
    private static bool enableTextColorContinuousFix = true;
    private static bool enableDisableColorOverride = true;
    private static bool enableConfigColorFieldCheck = true;
    private static bool enableRevealableColorCheck = true;
    private static bool enablePrinterInitColorCheck = true;

    static DialogueRenderOrderFixer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        realtimeErrorEntries = new List<ErrorContentEntry>();
        errorOccurrenceCounts = new Dictionary<string, int>();
        stencilFixedMaterialIds = new HashSet<int>();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            isPlaying = true;
            frameCount = 0;
            consecutiveVisibleFrames = 0;
            textVisConsecutiveVisibleFrames = 0;
            printerActiveLogged = false;
            previousRoundCause = RootCauseType.Unknown;
            engineInitCalled = false;
            engineInitCallFrame = 0;
            engineInitWaitFrameCount = 0;
            prevTextContentLength = 0;
            charClipRectId = Shader.PropertyToID("_CharClipRect");
            lineClipRectId = Shader.PropertyToID("_LineClipRect");
            revealableTextType = null;
            revealableTMProType = null;
            textRevealBehaviourType = null;
            engineType = null;
            printManagerType = null;
            prevCharClipRects = new Dictionary<string, Vector4>();
            prevLineClipRects = new Dictionary<string, Vector4>();
            loggedErrors = new HashSet<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t1 = asm.GetType("Naninovel.UI.RevealableUIText");
                if (t1 != null) revealableTextType = t1;
                var t2 = asm.GetType("Naninovel.UI.RevealableTMProText");
                if (t2 != null) revealableTMProType = t2;
                var t3 = asm.GetType("Naninovel.UI.TextRevealBehaviour");
                if (t3 != null) textRevealBehaviourType = t3;
                var t4 = asm.GetType("Naninovel.Engine");
                if (t4 != null) engineType = t4;
                var t5 = asm.GetType("Naninovel.UI.IPrintManager");
                if (t5 != null) printManagerType = t5;
            }
            baseline = LoadBaseline();
            loopContext = new DiagnosisFixLoopContext();
            loopContext.MaxRounds = diagnosisFixLoopMaxRounds;
            EditorApplication.update += OnUpdate;
        }
        else if (change == PlayModeStateChange.ExitingPlayMode)
        {
            isPlaying = false;
            EditorApplication.update -= OnUpdate;
        }
    }

    private static void OnUpdate()
    {
        if (!isPlaying || !EditorApplication.isPlaying) return;
        frameCount++;
        try
        {
            if (!callbackRegistered)
            {
                RegisterLogMessageReceivedCallback();
                Debug.Log("[TEXTVIS] OnUpdate: callbackRegistered was false, re-registered log callback");
            }
        }
        catch (Exception exCallback)
        {
            Debug.LogWarning("[TEXTVIS] OnUpdate callback registration check exception: " + exCallback.Message);
        }
        try
        {
            FixCanvasSortingOrder();
            FixTextHierarchy();
            if (enableTextVisibilityFix)
            {
                bool shouldFix = ShouldExecuteFixThisFrame(frameCount, fixEarlyFrameCount, fixSteadyFrameInterval);
                if (shouldFix)
                {
                    FixDialogueTextDirect();
                    FixTextMaterialAndColor();
                    FixCharClipRect();
                    FixClipRectOOB();
                }
            }
            if (enableRuntimeDiagnosis && (frameCount <= diagnosisEarlyFrameCount || frameCount % diagnosisSteadyFrameInterval == 0))
            {
                LogDiagnosticInfo();
                LogDialogueTextGraphicDiagnostic();
            }

            if (enableObscurationFix)
            {
                var dt = FindDialogueText();
                if (dt != null)
                {
                    var snap = CollectSnapshot(dt);
                    if (snap != null && snap.IsValid)
                    {
                        var cause = LocateRootCause(snap, baseline);
                        if (cause.PrimaryCause != RootCauseType.Unknown)
                        {
                            bool shouldFix = frameCount <= fixEarlyFrameCount || frameCount % fixSteadyFrameInterval == 0;
                            if (shouldFix)
                            {
                                ApplyTargetedFix(cause, dt, baseline);
                            }
                        }
                    }
                }
            }
            if (enableTextVisibilityDiagnosis)
            {
                bool shouldDiagnose = frameCount <= diagnosisEarlyFrameCount || frameCount % diagnosisSteadyFrameInterval == 0;
                if (shouldDiagnose)
                {
                    LogTextVisibilityDiagnostic();
                    if (enableShadowDiagnosis || enableVertexDataDiagnosis || enableMainTexMatchCheck)
                    {
                        var diagDt = FindDialogueText();
                        if (diagDt != null)
                        {
                            if (enableShadowDiagnosis)
                            {
                                try
                                {
                                    var shadow = diagDt.GetComponent<Shadow>();
                                    var outline = diagDt.GetComponent<Outline>();
                                    if (shadow != null) Debug.Log("[TEXTVIS] Shadow: effectColor=" + shadow.effectColor + " effectDistance=" + shadow.effectDistance + " useGraphicAlpha=" + shadow.useGraphicAlpha);
                                    if (outline != null) Debug.Log("[TEXTVIS] Outline: effectColor=" + outline.effectColor + " effectDistance=" + outline.effectDistance);
                                    var cr = diagDt.GetComponent<Graphic>();
                                    if (cr != null && cr.canvasRenderer != null) Debug.Log("[TEXTVIS] CanvasRenderer hasMoved=" + cr.canvasRenderer.hasMoved + " cull=" + cr.canvasRenderer.cull);
                                }
                                catch (Exception ex) { Debug.LogWarning("[TEXTVIS] ShadowDiagnosis: " + ex.Message); }
                            }
                            if (enableVertexDataDiagnosis)
                            {
                                try
                                {
                                    var g = diagDt.GetComponent<Graphic>();
                                    if (g != null && g.canvasRenderer != null) Debug.Log("[TEXTVIS] VertexData: hasMoved=" + g.canvasRenderer.hasMoved + " cull=" + g.canvasRenderer.cull);
                                }
                                catch (Exception ex) { Debug.LogWarning("[TEXTVIS] VertexDiagnosis: " + ex.Message); }
                            }
                            if (enableMainTexMatchCheck)
                            {
                                try
                                {
                                    var g = diagDt.GetComponent<Graphic>();
                                    var tc = g as Text;
                                    if (g != null && tc != null && tc.font != null && tc.font.material != null)
                                    {
                                        var mat = g.material;
                                        var mainTex = mat != null ? mat.GetTexture("_MainTex") : null;
                                        var fontTex = tc.font.material.mainTexture;
                                        Debug.Log("[TEXTVIS] MainTexMatch: mainTex=" + (mainTex != null ? mainTex.name : "null") + " fontTex=" + (fontTex != null ? fontTex.name : "null") + " sameRef=" + ReferenceEquals(mainTex, fontTex));
                                    }
                                }
                                catch (Exception ex) { Debug.LogWarning("[TEXTVIS] MainTexMatchDiagnosis: " + ex.Message); }
                            }
                        }
                    }
                }
            }
            if (enableTextVisibilityFix)
            {
                var dt = FindDialogueText();
                if (dt != null)
                {
                    var visSnap = CollectVisibilitySnapshot(dt);
                    if (visSnap != null && visSnap.IsValid)
                    {
                        var visCause = LocateTextVisibilityRootCause(visSnap, baseline);
                        if (visCause.PrimaryCause != RootCauseType.Unknown && visCause.PrimaryCause != RootCauseType.UnknownTextVisibilityCause)
                        {
                            bool shouldFix = ShouldExecuteFixThisFrame(frameCount, fixEarlyFrameCount, fixSteadyFrameInterval);
                            if (shouldFix)
                            {
                                ApplyTextVisibilityFix(visCause, dt, baseline, visSnap);
                            }
                        }
                        try
                        {
                            if (enableShadowDirectFix && visSnap.ShadowDiag.IsShadowBlack && Mathf.Abs(visSnap.ShadowDiag.EffectDistance.x) < 2f && Mathf.Abs(visSnap.ShadowDiag.EffectDistance.y) < 2f)
                            {
                                var shadowDirectRec = FixShadowEffectBlackDirectCall(dt, visSnap.ShadowDiag);
                                if (shadowDirectRec != null)
                                    Debug.Log("[TEXTVIS] OnUpdate: FixShadowEffectBlackDirectCall applied, summary=" + shadowDirectRec.Summary);
                            }
                        }
                        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] OnUpdate ShadowDirectCall exception: " + ex.Message); }
                        try
                        {
                            Text textColorComp = dt.GetComponent<Text>();
                            if (textColorComp != null)
                            {
                                Color textColor = textColorComp.color;
                                if (textColor.r < 0.01f && textColor.g < 0.01f && textColor.b < 0.01f)
                                {
                                    Color oldColor = textColor;
                                    Color targetColor = new Color(1f, 1f, 1f, textColor.a);
                                    Transform parentForColor = dt.parent;
                                    while (parentForColor != null)
                                    {
                                        Graphic parentGraphic = parentForColor.GetComponent<Graphic>();
                                        if (parentGraphic != null)
                                        {
                                            Color parentColor = parentGraphic.color;
                                            if (parentColor.r > 0.01f || parentColor.g > 0.01f || parentColor.b > 0.01f)
                                            {
                                                targetColor = new Color(parentColor.r, parentColor.g, parentColor.b, textColor.a);
                                                Debug.Log("[TEXTVIS] OnUpdate: Text.color inherited from parent " + parentForColor.name + ", parentColor=" + parentColor);
                                                break;
                                            }
                                        }
                                        parentForColor = parentForColor.parent;
                                    }
                                    textColorComp.color = targetColor;
                                    textColorComp.SetVerticesDirty();
                                    if (enableTextColorContinuousFix) textColorBlackFixCount++;
                                    Debug.Log("[TEXTVIS] OnUpdate: Text.color fixed from black to " + targetColor + ", oldColor=" + oldColor + ", fixCount=" + textColorBlackFixCount);
                                    if (enableDisableColorOverride && textColorBlackFixCount > textColorBlackFixThreshold && !textColorOverrideDisabled)
                                    {
                                        try
                                        {
                                            Component[] colorComps = dt.GetComponents<Component>();
                                            for (int ci = 0; ci < colorComps.Length; ci++)
                                            {
                                                if (colorComps[ci] == null) continue;
                                                Type compType = colorComps[ci].GetType();
                                                if (compType.Name.Contains("RevealableUIText"))
                                                {
                                                    string[] colorPropNames = new string[] { "Color", "TextColor", "DefaultColor", "color" };
                                                    for (int cpi = 0; cpi < colorPropNames.Length; cpi++)
                                                    {
                                                        PropertyInfo colorProp = null;
                                                        try { colorProp = compType.GetProperty(colorPropNames[cpi], BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic); } catch { }
                                                        if (colorProp != null && colorProp.PropertyType == typeof(Color))
                                                        {
                                                            try
                                                            {
                                                                Color propColor = (Color)colorProp.GetValue(colorComps[ci], null);
                                                                if (propColor.r < 0.01f && propColor.g < 0.01f && propColor.b < 0.01f)
                                                                {
                                                                    colorProp.SetValue(colorComps[ci], targetColor, null);
                                                                    Debug.Log("[TEXTVIS] OnUpdate: RevealableUIText." + colorPropNames[cpi] + " fixed from black to " + targetColor);
                                                                }
                                                            }
                                                            catch (Exception exSetProp) { Debug.LogWarning("[TEXTVIS] OnUpdate: RevealableUIText." + colorPropNames[cpi] + " set exception: " + exSetProp.Message); }
                                                        }
                                                    }
                                                    FieldInfo[] colorFields = compType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                                    for (int fi = 0; fi < colorFields.Length; fi++)
                                                    {
                                                        if (colorFields[fi].FieldType == typeof(Color) && (colorFields[fi].Name.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0 || colorFields[fi].Name.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0))
                                                        {
                                                            try
                                                            {
                                                                Color fieldColor = (Color)colorFields[fi].GetValue(colorComps[ci]);
                                                                if (fieldColor.r < 0.01f && fieldColor.g < 0.01f && fieldColor.b < 0.01f)
                                                                {
                                                                    colorFields[fi].SetValue(colorComps[ci], targetColor);
                                                                    Debug.Log("[TEXTVIS] OnUpdate: RevealableUIText field " + colorFields[fi].Name + " fixed from black to " + targetColor);
                                                                }
                                                            }
                                                            catch (Exception exSetField) { Debug.LogWarning("[TEXTVIS] OnUpdate: RevealableUIText field " + colorFields[fi].Name + " set exception: " + exSetField.Message); }
                                                        }
                                                    }
                                                    break;
                                                }
                                            }
                                            textColorOverrideDisabled = true;
                                            Debug.Log("[TEXTVIS] OnUpdate: textColorOverrideDisabled=true after " + textColorBlackFixCount + " fixes, RevealableUIText color properties checked");
                                        }
                                        catch (Exception exDisable) { Debug.LogWarning("[TEXTVIS] OnUpdate DisableColorOverride exception: " + exDisable.Message); }
                                    }
                                }
                                else
                                {
                                    if (textColorBlackFixCount > 0 && textColorBlackFixCount % 50 == 0)
                                    {
                                        Debug.Log("[TEXTVIS] OnUpdate: Text.color not black, reset textColorBlackFixCount=0, color=" + textColor);
                                        textColorBlackFixCount = 0;
                                    }
                                }
                            }
                        }
                        catch (Exception exTextColor) { Debug.LogWarning("[TEXTVIS] OnUpdate TextColor fix exception: " + exTextColor.Message); }
                        if (enableHasMovedRootCauseAnalysis && visSnap.NaninovelInit.EngineInitialized)
                        {
                            var textComp = dt.GetComponent<Text>();
                            if (textComp != null)
                            {
                                var graphic = dt.GetComponent<Graphic>();
                                bool hasMoved = graphic != null && graphic.canvasRenderer != null && graphic.canvasRenderer.hasMoved;
                                if (!hasMoved)
                                {
                                    string currentState = string.IsNullOrEmpty(textComp.text) ? "TextEmpty" : "TextNonEmpty";
                                    if (currentState != lastHasMovedTextState || !hasMovedAnalysisCalled)
                                    {
                                        var hasMovedResult = AnalyzeHasMovedFalseRootCause(dt, textComp, true);
                                        lastHasMovedTextState = currentState;
                                        hasMovedAnalysisCalled = true;
                                        if (hasMovedResult.TextEmpty && enableScriptPlayerTriggerFix)
                                        {
                                            FixTextPropertyNotSet(dt);
                                        }
                                    }
                                    try
                                    {
                                        if (enablePlaceholderTextDiag && !placeholderDiagCalled && dt != null)
                                        {
                                            Text textCompForPlaceholder = dt.GetComponent<Text>();
                                            if (textCompForPlaceholder != null && !string.IsNullOrEmpty(textCompForPlaceholder.text) && textCompForPlaceholder.text.Contains("Lorem ipsum"))
                                            {
                                                DiagnosePlaceholderTextSource(dt, textCompForPlaceholder);
                                                TriggerActualDialogueContentLoad(dt, textCompForPlaceholder);
                                                placeholderDiagCalled = true;
                                                Debug.Log("[TEXTVIS] OnUpdate: placeholder text diagnosed and actual content load triggered, placeholderDiagCalled=true");
                                            }
                                        }
                                    }
                                    catch (Exception exPlaceholder)
                                    {
                                        Debug.LogWarning("[TEXTVIS] OnUpdate placeholder text diag integration exception: " + exPlaceholder.Message);
                                    }
                                    if (placeholderDiagCalled && !placeholderTextLoaded)
                                    {
                                        placeholderDiagWaitFrameCount++;
                                        if (placeholderDiagWaitFrameCount >= 10)
                                        {
                                            Text textCompForVerify = dt != null ? dt.GetComponent<Text>() : null;
                                            if (textCompForVerify != null && !string.IsNullOrEmpty(textCompForVerify.text) && !textCompForVerify.text.Contains("Lorem ipsum"))
                                            {
                                                placeholderTextLoaded = true;
                                                Debug.Log("[TEXTVIS] OnUpdate: actual dialogue content loaded, textComp.text no longer Lorem ipsum, placeholderTextLoaded=true");
                                            }
                                            else
                                            {
                                                placeholderTextWarnCount++;
                                                if (placeholderTextWarnCount == 1 || placeholderTextWarnCount % 300 == 0)
                                                {
                                                    Debug.LogWarning("[TEXTVIS] OnUpdate: actual dialogue content load failed, text still Lorem ipsum or empty after 10 frames (warned x" + placeholderTextWarnCount + ", throttled)");
                                                }
                                            }
                                        }
                                    }
                                    if (scriptExecTriggered && !scriptExecVerifyDone)
                                    {
                                        scriptExecVerifyWaitFrameCount++;
                                        if (scriptExecVerifyWaitFrameCount >= 15)
                                        {
                                            scriptExecVerifyDone = true;
                                            Text textCompForExecVerify = dt != null ? dt.GetComponent<Text>() : null;
                                            if (textCompForExecVerify != null && !string.IsNullOrEmpty(textCompForExecVerify.text))
                                            {
                                                Debug.Log("[TEXTVIS] OnUpdate: ScriptPlayer exec verified, textComp.text non-empty after " + scriptExecVerifyWaitFrameCount + " frames, text=" + (textCompForExecVerify.text.Length > 50 ? textCompForExecVerify.text.Substring(0, 50) + "..." : textCompForExecVerify.text));
                                            }
                                            else
                                            {
                                                Debug.LogWarning("[TEXTVIS] OnUpdate: ScriptPlayer exec verification failed, textComp.text still empty after " + scriptExecVerifyWaitFrameCount + " frames, trying FixTextPropertyNotSet");
                                                FixTextPropertyNotSet(dt);
                                            }
                                        }
                                    }
                                    try
                                    {
                                        if (enableRevealableUITextFix && !string.IsNullOrEmpty(textComp.text) && !revealableFixCalled)
                                        {
                                            var revealRec = FixRevealableUITextRevealProcess(dt, textComp);
                                            if (revealRec != null)
                                                Debug.Log("[TEXTVIS] OnUpdate: FixRevealableUITextRevealProcess applied");
                                        }
                                    }
                                    catch (Exception ex) { Debug.LogWarning("[TEXTVIS] OnUpdate RevealableUITextFix exception: " + ex.Message); }
                                }
                            }
                        }
                        try
                        {
                            if (revealableFixCalled && !revealableFixFailed)
                            {
                                revealableFixWaitFrameCount++;
                                if (revealableFixWaitFrameCount >= 5)
                                {
                                    bool hasMovedAfterFix = visSnap.ShadowDiag.TextHasVertices;
                                    if (!hasMovedAfterFix)
                                    {
                                        revealableFixFailed = true;
                                        if (!hasMovedAlternativeFixCalled)
                                        {
                                            Debug.LogWarning("[TEXTVIS] OnUpdate: RevealableUIText fix failed, hasMoved still false after 5 frames, try alternative fix with ForceUpdateCanvases + RebuildCanvas");
                                            try
                                            {
                                                Text altTextComp = dt.GetComponent<Text>();
                                                if (altTextComp != null && !string.IsNullOrEmpty(altTextComp.text))
                                                {
                                                    string savedText = altTextComp.text;
                                                    altTextComp.text = "";
                                                    altTextComp.text = savedText;
                                                    altTextComp.SetVerticesDirty();
                                                    altTextComp.SetLayoutDirty();
                                                    Debug.Log("[TEXTVIS] OnUpdate: alternative fix applied, reset textComp.text to trigger OnPopulateMesh");
                                                }
                                                Graphic graphicForAltFix = dt.GetComponent<Graphic>();
                                                if (graphicForAltFix != null)
                                                {
                                                    Canvas canvasComp = graphicForAltFix.canvas;
                                                    if (canvasComp != null)
                                                    {
                                                        Canvas.ForceUpdateCanvases();
                                                        Debug.Log("[TEXTVIS] OnUpdate: Canvas.ForceUpdateCanvases() called");
                                                    }
                                                    MethodInfo rebuildCanvasMethod = typeof(Graphic).GetMethod("RebuildCanvas", BindingFlags.NonPublic | BindingFlags.Instance);
                                                    if (rebuildCanvasMethod != null)
                                                    {
                                                        rebuildCanvasMethod.Invoke(graphicForAltFix, null);
                                                        Debug.Log("[TEXTVIS] OnUpdate: Graphic.RebuildCanvas() called");
                                                    }
                                                }
                                                hasMovedAlternativeFixCalled = true;
                                            }
                                            catch (Exception exAltFix)
                                            {
                                                Debug.LogWarning("[TEXTVIS] OnUpdate alternative fix exception: " + exAltFix.Message);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Debug.Log("[TEXTVIS] OnUpdate: RevealableUIText fix success, hasMoved=true after " + revealableFixWaitFrameCount + " frames");
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] OnUpdate RevealableVerify exception: " + ex.Message); }
                        try
                        {
                            if (enableStencilPropertyFix && dt != null)
                            {
                                Graphic graphicForStencil = dt.GetComponent<Graphic>();
                                if (graphicForStencil != null)
                                {
                                    Material matForStencil = graphicForStencil.materialForRendering;
                                    if (matForStencil != null && !matForStencil.HasProperty("_StencilRef") && !stencilFixedMaterialIds.Contains(matForStencil.GetInstanceID()))
                                    {
                                        FixStencilPropertyMissing(matForStencil);
                                    }
                                }
                                Text textCompForStencilMat = dt.GetComponent<Text>();
                                if (textCompForStencilMat != null)
                                {
                                    Material textMat = textCompForStencilMat.materialForRendering;
                                    if (textMat != null && !textMat.HasProperty("_StencilRef") && !stencilFixedMaterialIds.Contains(textMat.GetInstanceID()))
                                    {
                                        FixStencilPropertyMissing(textMat);
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] OnUpdate StencilFix exception: " + ex.Message); }
                    }
                }
            }
            if (enableRuntimeErrorFix)
            {
                var errSnap = CollectErrorContentSnapshot();
                if (errSnap.Entries != null && errSnap.Entries.Count > 0 && errSnap.TotalCount != lastErrorCount)
                {
                    FixRuntimeErrors(errSnap);
                    lastErrorCount = errSnap.TotalCount;
                }
            }
            try
            {
                if (enableTextPreviewDiagnosis)
                {
                    textPreviewDiagFrameCount++;
                    if (textPreviewDiagFrameCount >= 120)
                    {
                        textPreviewDiagFrameCount = 0;
                        DiagnoseTextPreviewState();
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[TEXTVIS] OnUpdate TextPreviewDiag exception: " + ex.Message); }
            if (enableDiagnosisFixLoop || enableTextVisibilityFixLoop)
            {
                bool engineInitialized = CheckEngineInitialized();
                ResetDiagnosisFixLoopOnEngineInit(engineInitialized);
                RunDiagnosisFixLoop();
            }
            try
            {
                periodicDefaultColorResetFrameCount++;
                if (periodicDefaultColorResetFrameCount >= 600)
                {
                    periodicDefaultColorResetFrameCount = 0;
                    if (isPlaying && EditorApplication.isPlaying)
                    {
                        ResetTextPrinterDefaultColors();
                    }
                }
            }
            catch { }
            try
            {
                if (isPlaying && EditorApplication.isPlaying)
                {
                    revealFadeFixFrameCounter++;
                    if (revealFadeFixFrameCounter >= 30)
                    {
                        revealFadeFixFrameCounter = 0;
                        FixRevealableFadeWidthAndReveal();
                    }
                }
            }
            catch { }
        }
        catch { }
    }

    /// <summary>
    /// 2026-10-23 v2 对话文字半透明/黑色兜底修复（限流 30 帧一次）。
    /// v1 教训：直接 SetFloat 材质会被 RevealableUIText.SetMaterialProperties 每帧覆盖回 0，
    /// 每帧打架致渲染闪烁；且每帧 FindObjectsOfType 全场景扫描性能过重。
    /// v2 正确修法：反射设置组件字段 revealFadeWidth=100（prefab 值，运行时丢失的源头），
    /// 使 SetMaterialProperties 自然传导 100；每个对象仅首次修复时调用一次 RevealAll。
    /// </summary>
    private static void FixRevealableFadeWidthAndReveal()
    {
        Component[] allBehaviours = UnityEngine.Object.FindObjectsOfType<Component>();
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            Component comp = allBehaviours[i];
            if (comp == null || comp.GetType().Name != "RevealableUIText") continue;
            try
            {
                int instanceId = comp.GetInstanceID();
                if (revealFadeFixedInstanceIds.Contains(instanceId)) continue;
                Text textComp = comp as Text;
                if (textComp == null || string.IsNullOrEmpty(textComp.text) || textComp.text.Contains("Lorem ipsum")) continue;
                Graphic graphic = comp as Graphic;
                if (graphic == null) continue;
                Material mat = graphic.materialForRendering;
                if (mat == null || !mat.HasProperty("_CharFadeWidth")) continue;
                float fadeWidth = mat.GetFloat("_CharFadeWidth");
                if (fadeWidth > 0.001f) continue;

                FieldInfo fadeWidthField = comp.GetType().GetField("revealFadeWidth", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (fadeWidthField != null && fadeWidthField.FieldType == typeof(float))
                {
                    fadeWidthField.SetValue(comp, 100f);
                }
                mat.SetFloat("_CharFadeWidth", 100f);
                revealFadeFixedInstanceIds.Add(instanceId);
                revealFadeFixCount++;
                Debug.Log("[TEXTVIS] FixRevealableFadeWidthAndReveal: revealFadeWidth field + material restored to 100 on '" + comp.gameObject.name + "' (fixCount=" + revealFadeFixCount + ")");

                try
                {
                    MethodInfo revealAll = comp.GetType().GetMethod("RevealAll", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    if (revealAll != null)
                    {
                        revealAll.Invoke(comp, null);
                    }
                }
                catch { }
            }
            catch { }
        }
    }

    private static void FixCanvasSortingOrder()
    {
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
        foreach (var canvas in canvases)
        {
            if (canvas.name == "Wide" && canvas.sortingOrder < {{targetSortingOrder}})
            {
                canvas.sortingOrder = {{targetSortingOrder}};
            }
        }
    }

    private static void FixTextHierarchy()
    {
        var wideCanvas = FindWideCanvas();
        if (wideCanvas == null) return;
        var content = FindChild(wideCanvas.transform, "Content");
        if (content == null) return;
        var printerPanel = FindChild(content, "PrinterPanel");
        if (printerPanel == null) return;
        var printer = FindChild(printerPanel, "Printer");
        if (printer == null) return;

        var textPanel = FindChild(printer, "TextPanel");
        if (textPanel != null) textPanel.SetAsLastSibling();

        var controlPanel = FindChild(printerPanel, "ControlPanel");
        if (controlPanel != null) controlPanel.SetAsLastSibling();

        var dialogueText = FindChild(textPanel, "DialogueText");
        if (dialogueText != null) dialogueText.SetAsLastSibling();
    }

    private static void FixTextMaterialAndColor()
    {
        FixColorForType(revealableTextType);
        FixColorForType(revealableTMProType);
    }

    private static void FixDialogueTextDirect()
    {
        try
        {
            var dt = FindDialogueText();
            if (dt == null) return;
            var textComp = dt.GetComponent<Text>();
            if (textComp != null)
            {
                var c = textComp.color;
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f)
                {
                    textComp.color = new Color(1f, 1f, 1f, c.a < 0.01f ? 1f : c.a);
                }
                else if (c.a < 0.01f)
                {
                    textComp.color = new Color(c.r, c.g, c.b, 1f);
                }
            }
            var graphic = dt.GetComponent<Graphic>();
            if (graphic != null && graphic.material != null)
            {
                var mat = graphic.material;
                if (mat.HasProperty("_Color"))
                {
                    var mc = mat.GetColor("_Color");
                    if (mc.r < 0.01f && mc.g < 0.01f && mc.b < 0.01f)
                    {
                        mat.SetColor("_Color", new Color(1f, 1f, 1f, mc.a < 0.01f ? 1f : mc.a));
                    }
                }
                if (mat.HasProperty("_CharClipRect"))
                {
                    var charClip = mat.GetVector(charClipRectId);
                    float charWidth = charClip.z - charClip.x;
                    if (charClip.z > charClip.x && charWidth > clipRectOOBThreshold)
                    {
                        mat.SetVector(charClipRectId, new Vector4(-32767f, -32767f, 32767f, 32767f));
                    }
                }
                if (mat.HasProperty("_LineClipRect"))
                {
                    var lineClip = mat.GetVector(lineClipRectId);
                    float lineW = lineClip.z - lineClip.x;
                    float lineH = lineClip.w - lineClip.y;
                    if (lineClip.z > lineClip.x && lineClip.w > lineClip.y && (lineW > clipRectOOBThreshold || lineH > clipRectOOBThreshold))
                    {
                        mat.SetVector(lineClipRectId, new Vector4(-32767f, -32767f, 32767f, 32767f));
                    }
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixDialogueTextDirect: " + ex.Message); }
    }

    private static void FixColorForType(Type type)
    {
        if (type == null) return;
        var components = UnityEngine.Object.FindObjectsOfType(type);
        foreach (var comp in components)
        {
            var graphic = comp as Graphic;
            if (graphic == null) continue;

            var c = graphic.color;
            if (c.r < 0.01f && c.g < 0.01f && c.b < 0.01f)
            {
                c.r = 1f; c.g = 1f; c.b = 1f; c.a = 1f;
                graphic.color = c;
            }

            var mat = graphic.material;
            if (mat != null)
            {
                var matColor = mat.GetColor("_Color");
                if (matColor.r < 0.01f && matColor.g < 0.01f && matColor.b < 0.01f)
                {
                    mat.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
                }
            }

            if (mat == null || mat.shader == null || (mat.shader.name != "Naninovel/RevealableText" && mat.shader.name != "Naninovel/RevealableTMProText"))
            {
                var shader = Shader.Find("Naninovel/RevealableText");
                if (shader != null)
                {
                    var newMat = new Material(shader);
                    var oldMat = mat;
                    graphic.material = newMat;
                    if (oldMat != null && oldMat.shader != null && oldMat.shader.name == "Naninovel/RevealableText")
                    {
                        newMat.SetVector(charClipRectId, oldMat.GetVector(charClipRectId));
                    }
                }
            }
        }
    }

    private static void FixCharClipRect()
    {
        FixCharClipForType(revealableTextType);
        FixCharClipForType(revealableTMProType);
    }

    private static void FixCharClipForType(Type type)
    {
        if (type == null) return;
        var components = UnityEngine.Object.FindObjectsOfType(type);
        foreach (var comp in components)
        {
            var graphic = comp as Graphic;
            if (graphic == null) continue;
            var mat = graphic.material;
            if (mat == null) continue;
            var charClip = mat.GetVector(charClipRectId);
            if (Mathf.Approximately(charClip.x, 2000f))
            {
                charClip.x = -32767f;
                mat.SetVector(charClipRectId, charClip);
            }
            float charWidth = charClip.z - charClip.x;
            if (charClip.z > charClip.x && charWidth > clipRectOOBThreshold)
            {
                mat.SetVector(charClipRectId, new Vector4(-32767f, -32767f, 32767f, 32767f));
            }
            if (mat.HasProperty("_LineClipRect"))
            {
                var lineClip = mat.GetVector(lineClipRectId);
                float lineW = lineClip.z - lineClip.x;
                float lineH = lineClip.w - lineClip.y;
                if (lineClip.z > lineClip.x && lineClip.w > lineClip.y && (lineW > clipRectOOBThreshold || lineH > clipRectOOBThreshold))
                {
                    mat.SetVector(lineClipRectId, new Vector4(-32767f, -32767f, 32767f, 32767f));
                }
            }
            if (mat.HasProperty("_ClipRect"))
            {
                var unityClip = mat.GetVector(Shader.PropertyToID("_ClipRect"));
                float uW = unityClip.z - unityClip.x;
                float uH = unityClip.w - unityClip.y;
                if (unityClip.z > unityClip.x && unityClip.w > unityClip.y && (uW > clipRectOOBThreshold || uH > clipRectOOBThreshold))
                {
                    mat.SetVector(Shader.PropertyToID("_ClipRect"), new Vector4(-32767f, -32767f, 32767f, 32767f));
                }
            }
        }
    }

    private static void FixClipRectOOB()
    {
        try
        {
            FixCharClipForType(revealableTextType);
            FixCharClipForType(revealableTMProType);
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixClipRectOOB: " + ex.Message); }
    }

    private static void LogDiagnosticInfo()
    {
        var wideCanvas = FindWideCanvas();
        if (wideCanvas == null) return;
        Debug.Log($"[DialogueRenderOrderFixer] Frame {frameCount}: Wide Canvas sortingOrder={wideCanvas.sortingOrder}, renderMode={wideCanvas.renderMode}");

        var renderers = wideCanvas.GetComponentsInChildren<CanvasRenderer>(true);
        foreach (var cr in renderers)
        {
            if (cr.gameObject.name.Contains("Text") || cr.gameObject.name.Contains("Stripe") || cr.gameObject.name.Contains("Printer"))
            {
                var c = cr.gameObject.GetComponent<Graphic>();
                string matInfo = c != null && c.material != null ? c.material.shader.name : "null";
                string colorInfo = c != null ? $"a={c.color.a:F2}" : "no-graphic";
                Debug.Log($"  {cr.gameObject.name}: sibling={cr.transform.GetSiblingIndex()}, color={colorInfo}, mat={matInfo}");
            }
        }

        var dt = FindDialogueText();
        if (dt == null)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] DialogueText not found, diagnosis skipped");
            return;
        }
        var snap = CollectSnapshot(dt);
        if (snap != null && snap.IsValid)
        {
            var deviations = CompareWithBaseline(snap, baseline);
            if (deviations != null && deviations.Count > 0)
            {
                Debug.Log($"[DialogueRenderOrderFixer] Deviations:");
                foreach (var dev in deviations) Debug.Log($"  {dev}");
            }
        }
    }

    private static void LogDialogueTextGraphicDiagnostic()
    {
        try
        {
            var dt = FindDialogueText();
            if (dt == null)
            {
                if (frameCount <= 5) Debug.LogWarning("[TEXTDIAG] DialogueText NOT FOUND by any strategy");
                return;
            }
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null)
            {
                if (frameCount <= 5) Debug.LogWarning("[TEXTDIAG] DialogueText found but has NO Graphic component: " + GetParentChain(dt));
                return;
            }
            string graphicType = graphic.GetType().Name;
            var c = graphic.color;
            string matShader = graphic.material != null ? graphic.material.shader.name : "null";
            string matColor = "n/a";
            string charClip = "n/a";
            string lineClip = "n/a";
            if (graphic.material != null)
            {
                if (graphic.material.HasProperty("_Color"))
                {
                    var mc = graphic.material.GetColor("_Color");
                    matColor = $"({mc.r:F2},{mc.g:F2},{mc.b:F2},{mc.a:F2})";
                }
                if (graphic.material.HasProperty("_CharClipRect"))
                {
                    var cc = graphic.material.GetVector(charClipRectId);
                    charClip = $"({cc.x:F0},{cc.y:F0},{cc.z:F0},{cc.w:F0}) w={cc.z-cc.x:F0}";
                }
                if (graphic.material.HasProperty("_LineClipRect"))
                {
                    var lc = graphic.material.GetVector(lineClipRectId);
                    lineClip = $"{lc.x:F0},{lc.y:F0},{lc.z:F0},{lc.w:F0}";
                }
            }
            Debug.Log($"[TEXTDIAG] f={frameCount} type={graphicType} color=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2}) shader={matShader} matColor={matColor} charClip={charClip} lineClip={lineClip} path={GetParentChain(dt)}");
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTDIAG] " + ex.Message); }
    }


    private static Canvas FindWideCanvas()
    {
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
        foreach (var c in canvases)
        {
            if (c.name == "Wide") return c;
        }
        return null;
    }

    private static Transform FindChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name) return child;
        }
        return null;
    }

    private static Transform FindDialogueText()
    {
        if (revealableTextType != null)
        {
            var comps = UnityEngine.Object.FindObjectsOfType(revealableTextType);
            if (comps != null && comps.Length > 0) return (comps[0] as Component).transform;
        }
        if (revealableTMProType != null)
        {
            var comps = UnityEngine.Object.FindObjectsOfType(revealableTMProType);
            if (comps != null && comps.Length > 0) return (comps[0] as Component).transform;
        }
        var wide = FindWideCanvas();
        if (wide == null) return FindDialogueTextByName();
        var content = FindChild(wide.transform, "Content");
        if (content == null) return FindDialogueTextByName();
        var pp = FindChild(content, "PrinterPanel");
        if (pp == null) return FindDialogueTextByName();
        var printer = FindChild(pp, "Printer");
        if (printer == null) return FindDialogueTextByName();
        var tp = FindChild(printer, "TextPanel");
        if (tp == null) return FindDialogueTextByName();
        var result = FindChild(tp, "DialogueText");
        if (result == null) return FindDialogueTextByName();
        return result;
    }

    private static Transform FindDialogueTextByName()
    {
        var allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();
        foreach (var t in allTransforms)
        {
            if (t.name == "DialogueText") return t;
        }
        return null;
    }

    private static string GetParentChain(Transform t)
    {
        if (t == null) return "";
        var chain = new List<string>();
        var cur = t;
        while (cur != null)
        {
            chain.Add(cur.name);
            cur = cur.parent;
        }
        chain.Reverse();
        return string.Join("/", chain.ToArray());
    }

    private static string GetAncestorSiblingChain(Transform t)
    {
        if (t == null) return "";
        var parts = new List<string>();
        var cur = t;
        while (cur != null && cur.parent != null)
        {
            parts.Add(cur.name + "=" + cur.GetSiblingIndex());
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join(", ", parts.ToArray());
    }

    // === DATA STRUCTURES ===

    private class DialogueTextSnapshot
    {
        public int Layer;
        public Vector3 Position;
        public Vector3 LocalPosition;
        public Vector3 LossyScale;
        public Vector2 SizeDelta;
        public float RectWidth;
        public float RectHeight;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public string ParentChain;
        public string CanvasWorldCameraName;
        public int CanvasSortingOrder;
        public int CanvasRenderMode;
        public float CanvasPlaneDistance;
        public bool CanvasRendererCull;
        public bool CanvasRendererHasMoved;
        public string MaterialShaderName;
        public Color MaterialColor;
        public int StencilRef;
        public int StencilComp;
        public int StencilPass;
        public int StencilReadMask;
        public int StencilWriteMask;
        public bool UsesRenderTexture;
        public int SiblingIndex;
        public string AncestorSiblingChain;
        public bool IsValid;
    }

    private class BaselineSnapshot
    {
        public int ExpectedLayer;
        public string ExpectedParentChain;
        public string ExpectedCanvasWorldCameraName;
        public int ExpectedCanvasSortingOrder;
        public int ExpectedCanvasRenderMode;
        public float ExpectedCanvasPlaneDistance;
        public Vector2 ExpectedSizeDelta;
        public Vector3 ExpectedLocalPosition;
        public string Source;
    }

    private enum RootCauseType
    {
        LayerError,
        ZPositionError,
        ParentError,
        CanvasWorldCameraError,
        RectTransformAnomaly,
        CanvasRendererCull,
        StencilTestFailure,
        RenderTextureMechanism,
        BlackText,
        TextAlphaTooLow,
        TextTemporalClear,
        RenderOcclusion,
        PrinterBehaviorAnomaly,
        MaterialMainTexNotSet,
        FontTextureMissing,
        ShaderModificationNotEffective,
        FontMaterialConfigError,
        ClipRectZeroVector,
        ClipRectUpdateLogicDefect,
        NaninovelInitFailure,
        TextPropertyNotSet,
        MainTexMismatchWithFontTexture,
        ShadowEffectBlack,
        PlaceholderNotReplaced,
        UnknownTextVisibilityCause,
        Unknown
    }

    private class RootCauseResult
    {
        public RootCauseType PrimaryCause;
        public List<RootCauseType> SecondaryCauses;
        public string SupportingData;
        public string ExclusionBasis;
        public bool IsDataComplete;
        public bool ResourcePathMismatch;
        public bool TMProShaderThresholdMissing;
        public bool EngineInitFailure;
        public bool ComponentTypeMismatch;
    }

    private class PropertyDeviation
    {
        public string PropertyName;
        public string CurrentValue;
        public string BaselineValue;
        public bool IsDeviation;
        public string SourceClassification;
    }

    private class FixChangeRecord
    {
        public string ObjectPath;
        public string PropertyName;
        public string OldValue;
        public string NewValue;
        public string Summary;
    }

    private enum LoopState
    {
        Idle,
        Diagnosing,
        Locating,
        Fixing,
        Verifying,
        Converged,
        Escalated
    }

    private class DiagnosisFixLoopContext
    {
        public LoopState State;
        public int RoundCount;
        public int MaxRounds;
        public int ConsecutiveVisibleFrames;
        public DialogueTextSnapshot LastSnapshot;
        public RootCauseResult LastCause;
        public List<FixChangeRecord> LastFixRecords;
        public RootCauseType PreviousRoundCause;
    }

    private enum BaselineSource { Runtime, StaticConfig, Default }

    private struct ShaderCompilationSnapshot
    {
        public bool ShaderFindResult;
        public bool IsSupported;
        public bool HasCompileError;
        public bool SourceContainsDegenerateSkip;
        public bool CharClipRectPropertyExists;
        public bool LineClipRectPropertyExists;
        public string MaterialShaderName;
        public bool MainTexPropertyExists;
        public bool ColorPropertyExists;
        public bool CharFadeWidthPropertyExists;
        public bool CharSlantAnglePropertyExists;
        public string CompilationErrorMessage;
        public string ButtonShaderName;
        public bool ButtonShaderSupported;
        public bool IsPinkFallbackShader;
    }

    private struct MaterialMainTexSnapshot
    {
        public bool HasMainTexProperty;
        public bool MainTexIsNull;
        public string MainTexName;
        public int MainTexWidth;
        public int MainTexHeight;
        public bool IsInstanceMaterial;
        public bool FontMaterialMainTexIsNull;
        public string FontMaterialMainTexName;
        public Color ColorValue;
        public bool IsColorWhite;
        public bool IsPinkFallbackShader;
        public string ButtonMaterialShaderName;
        public bool ButtonMainTexIsNull;
        public bool SameRef;
    }

    private struct FontMaterialConfigSnapshot
    {
        public bool FontIsNull;
        public string FontName;
        public bool FontMaterialIsNull;
        public string FontMaterialName;
        public string FontMaterialShaderName;
        public bool MaterialIsNull;
        public string MaterialName;
        public string MaterialShaderName;
        public bool MainTextureIsNull;
        public string MainTextureName;
        public bool FontGuidValid;
    }

    private struct ClipRectUpdateSnapshot
    {
        public Vector4 CharClipRect;
        public Vector4 LineClipRect;
        public bool IsCharClipRectZero;
        public bool IsLineClipRectZero;
        public bool TextRevealBehaviourExists;
        public bool TextRevealBehaviourActive;
        public bool UpdateClipRectsMethodExists;
        public bool OnRectDimensionsChangeMethodExists;
        public bool ClipRectChangedSinceLastFrame;
        public bool ClipRectUpdateTriggeredInferred;
        public bool ShaderHas60000Threshold;
        public bool ZeroVectorCausesClipping;
        public bool CausesTextDisappear;
    }

    private struct NaninovelInitSnapshot
    {
        public bool RevealableTextTypeLoaded;
        public bool EngineTypeFound;
        public bool EngineInitialized;
        public bool PrintManagerTypeFound;
        public bool ActivePrinterNotNull;
        public bool ConfigResourcesLoaded;
        public int ConfigResourceCount;
        public bool GameObjectActiveInHierarchy;
        public bool ComponentEnabled;
        public bool UITextPrinterInstantiated;
        public bool RevealableUITextCreated;
        public bool PrintCommandExecutable;
        public string EngineInitErrorMessage;
        public bool RuntimeInitializerTypeFound;
        public bool OnApplicationLoadedMethodFound;
        public int OnApplicationLoadedParamCount;
        public int EngineInitAsyncParamCount;
        public int RuntimeInitAsyncParamCount;
    }

    private struct TMProShaderCompilationSnapshot
    {
        public string MaterialShaderName;
        public bool ShaderFindResult;
        public bool IsSupported;
        public bool IncludePathResolved;
        public bool Has60000Threshold;
        public bool CustomEditorAvailable;
        public string CompilationErrorMessage;
        public string IncludePathErrorMessage;
    }

    private struct ResourcePathSnapshot
    {
        public string PathPrefix;
        public string DefaultPrinterId;
        public string WidePrefabActualPath;
        public bool PathMatched;
        public bool WidePrefabExists;
        public string Implementation;
    }

    private enum ErrorCategory
    {
        ShaderCompilation,
        ResourceLoading,
        NullReference,
        MissingReference,
        NaninovelRuntime,
        TypeLoad,
        Other
    }

    private struct ErrorContentEntry
    {
        public string Message;
        public string ErrorType;
        public string StackTrace;
        public ErrorCategory Category;
        public bool RelatedToDialogueText;
        public bool Suppressed;
        public int OccurrenceCount;
    }

    private struct ErrorContentSnapshot
    {
        public List<ErrorContentEntry> Entries;
        public int TotalCount;
        public int CollectedCount;
        public Dictionary<ErrorCategory, int> CategoryCounts;
        public int DialogueRelatedCount;
        public Dictionary<string, int> ErrorPatternCounts;
        public int RealtimeCollectedCount;
    }

    private struct ComponentTypeSnapshot
    {
        public bool HasTextComponent;
        public bool HasTMPTextComponent;
        public bool HasGraphicComponent;
        public string RuntimeComponentType;
        public bool ConsistentWithPrefab;
        public bool ShaderMatched;
        public bool ConsistentWithConfig;
    }

    private struct TextPropertySnapshot
    {
        public string Text;
        public int TextLength;
        public bool TextIsNullOrEmpty;
        public bool IsPlaceholderText;
        public int CharacterCountVisible;
        public int CharacterCount;
        public int VertexCount;
        public bool TextChangedSinceLastFrame;
    }

    private struct FontTexturePixelSnapshot
    {
        public bool FontMaterialMainTexIsNull;
        public string FontMaterialMainTexName;
        public int FontMaterialMainTexWidth;
        public int FontMaterialMainTexHeight;
        public int FontMaterialMainTexFormat;
        public bool IsDynamicFont;
        public float AllTransparentRatio;
        public float AllWhiteRatio;
        public bool IsTextureValid;
        public int SampledPixelCount;
        public string CanvasRendererMainTexName;
    }

    private struct TextColorSnapshot
    {
        public Color TextCompColor;
        public Color MaterialColor;
        public Color CanvasRendererColor;
        public bool IsBlack;
        public bool IsAlphaTooLow;
        public bool IsMaterialBlack;
        public bool IsMaterialAlphaTooLow;
        public Color BaselineColor;
        public bool HasBaseline;
    }

    private struct TemporalTraceSnapshot
    {
        public float CurrentAlpha;
        public float CurrentMaterialAlpha;
        public string CurrentText;
        public int CurrentTextLength;
        public float PrevAlpha;
        public float PrevMaterialAlpha;
        public string PrevText;
        public bool AlphaDroppedToZero;
        public bool TextClearedAfterContent;
        public bool WasInitialized;
    }

    private struct RenderOcclusionSnapshot
    {
        public int SortingOrder;
        public int SiblingIndex;
        public float ZPosition;
        public bool IsOccluded;
        public string OccludingElementName;
        public int OccludingSortingOrder;
        public int SiblingCount;
    }

    private struct PrinterBehaviorSnapshot
    {
        public string ActivePrinterTypeName;
        public bool ActivePrinterActiveInHierarchy;
        public bool IsPrinting;
        public bool AutoPlay;
        public bool ClearResetInferred;
        public float AutoWait;
        public float PrintSpeed;
        public float RevealSpeed;
        public string ConfigSource;
        public bool ReflectionSucceeded;
    }

    private struct TextContentTraceSnapshot
    {
        public int CurrentTextLength;
        public int PrevTextLength;
        public bool PrintExecutedInferred;
        public bool TextEmptyForAllFrames;
        public int TracedFrameCount;
        public string EmptyReasonInferred;
        public bool ContainsPlaceholder;
        public string PlaceholderType;
        public bool CustomVariablesServiceActive;
        public bool ScriptPlayerActive;
    }

    private struct ShadowDiagnosisSnapshot
    {
        public bool ShadowExists;
        public Color EffectColor;
        public Vector2 EffectDistance;
        public bool IsShadowBlack;
        public bool TextHasVertices;
        public string BlackSourceInferred;
    }

    private struct HasMovedRootCauseAnalysis
    {
        public bool TextEmpty;
        public string TextContent;
        public bool RevealableUITextFound;
        public bool RevealableUITextEnabled;
        public bool ClipRectIsZero;
        public Vector4 ClipRectValue;
        public float RevealProgress;
        public string RootCauseSummary;
        public bool SetVerticesDirtyCalled;
    }

    private struct TextPreviewDiagSnapshot
    {
        public string PrinterId;
        public string Text;
        public float RevealProgress;
        public Vector4 ClipRect;
        public string Font;
        public Color Color;
        public bool PrinterFound;
        public bool ManagerFound;
    }

    private struct StencilFixRecord
    {
        public string MaterialPath;
        public int MaterialInstanceId;
        public string AddedProperties;
        public float DefaultValue;
    }

    private class DialogueTextVisibilitySnapshot
    {
        public ShaderCompilationSnapshot Shader;
        public MaterialMainTexSnapshot MaterialMainTex;
        public FontMaterialConfigSnapshot FontMaterialConfig;
        public ClipRectUpdateSnapshot ClipRectUpdate;
        public NaninovelInitSnapshot NaninovelInit;
        public TextPropertySnapshot TextProp;
        public FontTexturePixelSnapshot FontTexturePixel;
        public TextColorSnapshot TextColor;
        public TemporalTraceSnapshot TemporalTrace;
        public RenderOcclusionSnapshot RenderOcclusion;
        public PrinterBehaviorSnapshot PrinterBehavior;
        public TMProShaderCompilationSnapshot TMProShader;
        public ResourcePathSnapshot ResourcePath;
        public ErrorContentSnapshot ErrorContent;
        public ComponentTypeSnapshot ComponentType;
        public TextContentTraceSnapshot TextContentTrace;
        public ShadowDiagnosisSnapshot ShadowDiag;
        public bool IsValid;
        public int Frame;
        public string ObjectPath;
        public BaselineSource BaselineSrc;
    }

    // === DIAGNOSTIC METHODS ===

    private static BaselineSnapshot LoadBaseline()
    {
        var b = new BaselineSnapshot();
        b.ExpectedLayer = 5;
        b.ExpectedParentChain = expectedParentChain;
        b.ExpectedCanvasWorldCameraName = "UICamera";
        b.ExpectedCanvasSortingOrder = {{targetSortingOrder}};
        b.ExpectedCanvasRenderMode = 1;
        b.ExpectedCanvasPlaneDistance = 100f;
        b.ExpectedSizeDelta = new Vector2(0f, 0f);
        b.ExpectedLocalPosition = new Vector3(0f, 0f, 0f);
        b.Source = "StaticConfig";
        if (enableOriginalBaselineCompare)
        {
            Debug.Log("[DialogueRenderOrderFixer] Baseline source: StaticConfig (original game runtime baseline not available, using static prefab config)");
        }
        return b;
    }

    private static DialogueTextSnapshot CollectSnapshot(Transform dt)
    {
        var snap = new DialogueTextSnapshot();
        snap.IsValid = false;
        if (dt == null) return snap;
        try
        {
            snap.Layer = dt.gameObject.layer;
            snap.Position = dt.position;
            snap.LocalPosition = dt.localPosition;
            snap.LossyScale = dt.lossyScale;
            snap.SiblingIndex = dt.GetSiblingIndex();
            snap.ParentChain = GetParentChain(dt);
            snap.AncestorSiblingChain = GetAncestorSiblingChain(dt);

            var rt = dt as RectTransform;
            if (rt != null)
            {
                snap.SizeDelta = rt.sizeDelta;
                snap.RectWidth = rt.rect.width;
                snap.RectHeight = rt.rect.height;
                snap.AnchorMin = rt.anchorMin;
                snap.AnchorMax = rt.anchorMax;
            }

            var canvas = dt.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                snap.CanvasSortingOrder = canvas.sortingOrder;
                snap.CanvasRenderMode = (int)canvas.renderMode;
                snap.CanvasPlaneDistance = canvas.planeDistance;
                if (canvas.worldCamera != null)
                {
                    snap.CanvasWorldCameraName = canvas.worldCamera.name;
                }
                else
                {
                    snap.CanvasWorldCameraName = "null";
                }
            }

            var cr = dt.GetComponent<CanvasRenderer>();
            if (cr != null)
            {
                snap.CanvasRendererCull = cr.cull;
                snap.CanvasRendererHasMoved = cr.hasMoved;
            }

            var graphic = dt.GetComponent<Graphic>();
            if (graphic != null && graphic.material != null)
            {
                var mat = graphic.material;
                snap.MaterialShaderName = mat.shader != null ? mat.shader.name : "null";
                snap.MaterialColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                snap.StencilRef = mat.HasProperty("_StencilRef") ? mat.GetInt("_StencilRef") : 0;
                snap.StencilComp = mat.HasProperty("_StencilComp") ? mat.GetInt("_StencilComp") : 0;
                snap.StencilPass = mat.HasProperty("_StencilPass") ? mat.GetInt("_StencilPass") : 0;
                snap.StencilReadMask = mat.HasProperty("_StencilReadMask") ? mat.GetInt("_StencilReadMask") : 0;
                snap.StencilWriteMask = mat.HasProperty("_StencilWriteMask") ? mat.GetInt("_StencilWriteMask") : 0;
                var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                snap.UsesRenderTexture = mainTex != null && mainTex is RenderTexture;
            }
            snap.IsValid = true;
        }
        catch (NullReferenceException)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] NullReferenceException during CollectSnapshot, object may be destroyed");
            return snap;
        }
        catch (MissingReferenceException)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] MissingReferenceException during CollectSnapshot, object may be destroyed");
            return snap;
        }
        return snap;
    }

    private static List<PropertyDeviation> CompareWithBaseline(DialogueTextSnapshot snap, BaselineSnapshot b)
    {
        var deviations = new List<PropertyDeviation>();
        if (snap == null || b == null) return deviations;

        var d = new PropertyDeviation();
        d.PropertyName = "Layer";
        d.CurrentValue = snap.Layer.ToString();
        d.BaselineValue = b.ExpectedLayer.ToString();
        d.IsDeviation = snap.Layer != b.ExpectedLayer;
        d.SourceClassification = "AssetRipperExport";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "ParentChain";
        d.CurrentValue = snap.ParentChain;
        d.BaselineValue = b.ExpectedParentChain;
        d.IsDeviation = snap.ParentChain != b.ExpectedParentChain;
        d.SourceClassification = "NaninovelRuntime";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "CanvasWorldCamera";
        d.CurrentValue = snap.CanvasWorldCameraName;
        d.BaselineValue = b.ExpectedCanvasWorldCameraName;
        d.IsDeviation = snap.CanvasWorldCameraName != b.ExpectedCanvasWorldCameraName;
        d.SourceClassification = "NaninovelRuntime";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "CanvasSortingOrder";
        d.CurrentValue = snap.CanvasSortingOrder.ToString();
        d.BaselineValue = b.ExpectedCanvasSortingOrder.ToString();
        d.IsDeviation = snap.CanvasSortingOrder != b.ExpectedCanvasSortingOrder;
        d.SourceClassification = "AssetRipperExport";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "LocalPositionZ";
        d.CurrentValue = snap.LocalPosition.z.ToString("F3");
        d.BaselineValue = b.ExpectedLocalPosition.z.ToString("F3");
        d.IsDeviation = snap.LocalPosition.z < -0.001f || snap.LocalPosition.z > snap.CanvasPlaneDistance;
        d.SourceClassification = "NaninovelRuntime";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "SizeDelta";
        d.CurrentValue = "(" + snap.SizeDelta.x.ToString("F1") + "," + snap.SizeDelta.y.ToString("F1") + ")";
        d.BaselineValue = "(" + b.ExpectedSizeDelta.x.ToString("F1") + "," + b.ExpectedSizeDelta.y.ToString("F1") + ")";
        d.IsDeviation = Mathf.Approximately(snap.SizeDelta.x, 0f) && Mathf.Approximately(snap.SizeDelta.y, 0f);
        d.SourceClassification = "NaninovelRuntime";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "CanvasRendererCull";
        d.CurrentValue = snap.CanvasRendererCull.ToString();
        d.BaselineValue = "False";
        d.IsDeviation = snap.CanvasRendererCull;
        d.SourceClassification = "UnityEngine";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "StencilComp";
        d.CurrentValue = snap.StencilComp.ToString();
        d.BaselineValue = "8(Always)";
        d.IsDeviation = snap.StencilComp != 8;
        d.SourceClassification = "AssetRipperExport";
        deviations.Add(d);

        d = new PropertyDeviation();
        d.PropertyName = "UsesRenderTexture";
        d.CurrentValue = snap.UsesRenderTexture.ToString();
        d.BaselineValue = "False";
        d.IsDeviation = snap.UsesRenderTexture;
        d.SourceClassification = "NaninovelRuntime";
        deviations.Add(d);

        return deviations;
    }

    private static string FormatSnapshotLog(DialogueTextSnapshot snap, BaselineSnapshot b, int frame)
    {
        if (snap == null) return "[SNAP] null snapshot";
        string log = "[SNAP] frame=" + frame + " valid=" + snap.IsValid;
        log += "\n  layer=" + snap.Layer + "(expect=" + (b != null ? b.ExpectedLayer.ToString() : "?") + ")";
        log += "\n  pos=" + snap.Position.ToString("F2") + " lpos=" + snap.LocalPosition.ToString("F2") + " scale=" + snap.LossyScale.ToString("F2");
        log += "\n  sizeDelta=(" + snap.SizeDelta.x.ToString("F1") + "," + snap.SizeDelta.y.ToString("F1") + ") rect=(" + snap.RectWidth.ToString("F1") + "," + snap.RectHeight.ToString("F1") + ")";
        log += "\n  anchor=(" + snap.AnchorMin.ToString("F2") + ")-(" + snap.AnchorMax.ToString("F2") + ")";
        log += "\n  parentChain=" + snap.ParentChain;
        log += "\n  canvas: cam=" + snap.CanvasWorldCameraName + " sort=" + snap.CanvasSortingOrder + " mode=" + snap.CanvasRenderMode + " plane=" + snap.CanvasPlaneDistance.ToString("F1");
        log += "\n  cr: cull=" + snap.CanvasRendererCull + " moved=" + snap.CanvasRendererHasMoved;
        log += "\n  mat: shader=" + snap.MaterialShaderName + " color=(" + snap.MaterialColor.r.ToString("F2") + "," + snap.MaterialColor.g.ToString("F2") + "," + snap.MaterialColor.b.ToString("F2") + "," + snap.MaterialColor.a.ToString("F2") + ")";
        log += "\n  stencil: ref=" + snap.StencilRef + " comp=" + snap.StencilComp + " pass=" + snap.StencilPass + " rmask=" + snap.StencilReadMask + " wmask=" + snap.StencilWriteMask;
        log += "\n  rt=" + snap.UsesRenderTexture + " sibling=" + snap.SiblingIndex;
        log += "\n  ancestorChain=" + snap.AncestorSiblingChain;
        return log;
    }

    // === ROOT CAUSE LOCALIZATION ===

    private static RootCauseResult LocateRootCause(DialogueTextSnapshot snap, BaselineSnapshot b)
    {
        var result = new RootCauseResult();
        result.PrimaryCause = RootCauseType.Unknown;
        result.SecondaryCauses = new List<RootCauseType>();
        result.IsDataComplete = snap != null && snap.IsValid && b != null;
        result.SupportingData = "";
        result.ExclusionBasis = "";

        if (!result.IsDataComplete)
        {
            result.SupportingData = "Incomplete data";
            return result;
        }

        var deviations = CompareWithBaseline(snap, b);
        bool layerErr = snap.Layer != b.ExpectedLayer;
        bool parentErr = snap.ParentChain != b.ExpectedParentChain;
        bool camErr = snap.CanvasWorldCameraName == "null" || snap.CanvasWorldCameraName == "CameraCustom";
        bool zErr = snap.LocalPosition.z < -0.001f || snap.LocalPosition.z > snap.CanvasPlaneDistance;
        bool rectErr = (Mathf.Approximately(snap.SizeDelta.x, 0f) && Mathf.Approximately(snap.SizeDelta.y, 0f)) || snap.RectWidth == 0f || snap.RectHeight == 0f;
        bool cullErr = snap.CanvasRendererCull;
        bool stencilErr = snap.StencilComp != 8;
        bool rtErr = snap.UsesRenderTexture;

        if (layerErr)
        {
            result.PrimaryCause = RootCauseType.LayerError;
            result.SupportingData = "layer=" + snap.Layer + " expected=" + b.ExpectedLayer;
        }
        else if (parentErr)
        {
            result.PrimaryCause = RootCauseType.ParentError;
            result.SupportingData = "parentChain=" + snap.ParentChain + " expected=" + b.ExpectedParentChain;
        }
        else if (camErr)
        {
            result.PrimaryCause = RootCauseType.CanvasWorldCameraError;
            result.SupportingData = "canvasWorldCamera=" + snap.CanvasWorldCameraName + " expected=UICamera";
        }
        else if (zErr)
        {
            result.PrimaryCause = RootCauseType.ZPositionError;
            result.SupportingData = "localPosition.z=" + snap.LocalPosition.z.ToString("F3") + " planeDistance=" + snap.CanvasPlaneDistance.ToString("F1");
        }
        else if (rectErr)
        {
            result.PrimaryCause = RootCauseType.RectTransformAnomaly;
            result.SupportingData = "sizeDelta=(" + snap.SizeDelta.x.ToString("F1") + "," + snap.SizeDelta.y.ToString("F1") + ") rectW=" + snap.RectWidth + " rectH=" + snap.RectHeight;
        }
        else if (cullErr)
        {
            result.PrimaryCause = RootCauseType.CanvasRendererCull;
            result.SupportingData = "cull=true";
        }
        else if (stencilErr)
        {
            result.PrimaryCause = RootCauseType.StencilTestFailure;
            result.SupportingData = "stencilComp=" + snap.StencilComp + " expected=8(Always)";
        }
        else if (rtErr)
        {
            result.PrimaryCause = RootCauseType.RenderTextureMechanism;
            result.SupportingData = "usesRenderTexture=true";
        }

        if (parentErr && result.PrimaryCause != RootCauseType.ParentError) result.SecondaryCauses.Add(RootCauseType.ParentError);
        if (camErr && result.PrimaryCause != RootCauseType.CanvasWorldCameraError) result.SecondaryCauses.Add(RootCauseType.CanvasWorldCameraError);
        if (zErr && result.PrimaryCause != RootCauseType.ZPositionError) result.SecondaryCauses.Add(RootCauseType.ZPositionError);
        if (rectErr && result.PrimaryCause != RootCauseType.RectTransformAnomaly) result.SecondaryCauses.Add(RootCauseType.RectTransformAnomaly);
        if (cullErr && result.PrimaryCause != RootCauseType.CanvasRendererCull) result.SecondaryCauses.Add(RootCauseType.CanvasRendererCull);
        if (stencilErr && result.PrimaryCause != RootCauseType.StencilTestFailure) result.SecondaryCauses.Add(RootCauseType.StencilTestFailure);
        if (rtErr && result.PrimaryCause != RootCauseType.RenderTextureMechanism) result.SecondaryCauses.Add(RootCauseType.RenderTextureMechanism);

        result.ExclusionBasis = "Checked: layer, parent, cam, z, rect, cull, stencil, rt";
        return result;
    }

    // === TARGETED FIX METHODS ===

    private static FixChangeRecord FixLayerError(Transform dt, int expectedLayer)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "layer";
        rec.OldValue = dt.gameObject.layer.ToString();
        try
        {
            dt.gameObject.layer = expectedLayer;
            var cur = dt.parent;
            while (cur != null)
            {
                cur.gameObject.layer = expectedLayer;
                cur = cur.parent;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixLayerError exception: " + ex.Message);
        }
        rec.NewValue = dt.gameObject.layer.ToString();
        rec.Summary = "Set layer to " + expectedLayer + " for DialogueText and ancestors";
        return rec;
    }

    private static FixChangeRecord FixZPositionError(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "localPosition.z";
        rec.OldValue = dt.localPosition.z.ToString("F3");
        try
        {
            var p = dt.localPosition;
            p.z = 0f;
            dt.localPosition = p;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixZPositionError exception: " + ex.Message);
        }
        rec.NewValue = dt.localPosition.z.ToString("F3");
        rec.Summary = "Set localPosition.z to 0";
        return rec;
    }

    private static FixChangeRecord FixParentError(Transform dt, string expectedChain)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "parent";
        rec.OldValue = dt.parent != null ? dt.parent.name : "null";
        try
        {
            var parts = expectedChain.Split('/');
            if (parts.Length < 2) return null;
            var wide = FindWideCanvas();
            if (wide == null) return null;
            Transform target = wide.transform;
            for (int i = 1; i < parts.Length - 1; i++)
            {
                target = FindChild(target, parts[i]);
                if (target == null) return null;
            }
            dt.SetParent(target, false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixParentError exception: " + ex.Message);
            return null;
        }
        rec.NewValue = dt.parent != null ? dt.parent.name : "null";
        rec.Summary = "Re-parented to " + rec.NewValue;
        return rec;
    }

    private static FixChangeRecord FixCanvasWorldCameraError(Canvas wideCanvas)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = wideCanvas != null ? wideCanvas.name : "null";
        rec.PropertyName = "worldCamera";
        rec.OldValue = wideCanvas != null && wideCanvas.worldCamera != null ? wideCanvas.worldCamera.name : "null";
        try
        {
            Camera uiCam = null;
            var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
            foreach (var c in cams)
            {
                if (c.depth == 1f && (c.cullingMask & (1 << 5)) != 0)
                {
                    uiCam = c;
                    break;
                }
            }
            if (uiCam == null)
            {
                var go = new GameObject("UICamera");
                uiCam = go.AddComponent<Camera>();
                uiCam.depth = 1f;
                uiCam.cullingMask = 0x20;
                uiCam.clearFlags = CameraClearFlags.Depth;
                uiCam.orthographic = true;
            }
            wideCanvas.worldCamera = uiCam;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixCanvasWorldCameraError exception: " + ex.Message);
        }
        rec.NewValue = wideCanvas != null && wideCanvas.worldCamera != null ? wideCanvas.worldCamera.name : "null";
        rec.Summary = "Set worldCamera to " + rec.NewValue;
        return rec;
    }

    private static FixChangeRecord FixRectTransformAnomaly(RectTransform rt, BaselineSnapshot b)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = rt != null ? rt.name : "null";
        rec.PropertyName = "sizeDelta/position";
        rec.OldValue = rt != null ? "(" + rt.sizeDelta.x.ToString("F1") + "," + rt.sizeDelta.y.ToString("F1") + ")" : "null";
        try
        {
            if (rt != null && b != null)
            {
                if (Mathf.Approximately(rt.sizeDelta.x, 0f) && Mathf.Approximately(rt.sizeDelta.y, 0f))
                {
                    rt.sizeDelta = b.ExpectedSizeDelta;
                }
                var p = rt.localPosition;
                p.z = b.ExpectedLocalPosition.z;
                rt.localPosition = p;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixRectTransformAnomaly exception: " + ex.Message);
        }
        rec.NewValue = rt != null ? "(" + rt.sizeDelta.x.ToString("F1") + "," + rt.sizeDelta.y.ToString("F1") + ")" : "null";
        rec.Summary = "Restored sizeDelta and position";
        return rec;
    }

    private static FixChangeRecord FixCanvasRendererCull(CanvasRenderer cr)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = cr != null ? cr.gameObject.name : "null";
        rec.PropertyName = "cull";
        rec.OldValue = cr != null ? cr.cull.ToString() : "null";
        try
        {
            if (cr != null)
            {
                var method = cr.GetType().GetMethod("SetCull", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(cr, new object[] { false });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixCanvasRendererCull exception: " + ex.Message);
        }
        rec.NewValue = cr != null ? cr.cull.ToString() : "null";
        rec.Summary = "Attempted SetCull(false)";
        return rec;
    }

    private static FixChangeRecord FixStencilTestFailure(Material mat)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = mat != null ? mat.name : "null";
        rec.PropertyName = "_StencilComp";
        rec.OldValue = mat != null ? mat.GetInt("_StencilComp").ToString() : "null";
        try
        {
            if (mat != null)
            {
                mat.SetInt("_StencilComp", 8);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixStencilTestFailure exception: " + ex.Message);
        }
        rec.NewValue = mat != null ? mat.GetInt("_StencilComp").ToString() : "null";
        rec.Summary = "Set _StencilComp to 8(Always)";
        return rec;
    }

    private static FixChangeRecord FixRenderTextureMechanism(Graphic graphic)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = graphic != null ? graphic.gameObject.name : "null";
        rec.PropertyName = "_MainTex";
        rec.OldValue = "RenderTexture";
        try
        {
            if (graphic != null && graphic.material != null)
            {
                graphic.material.SetTexture("_MainTex", null);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] FixRenderTextureMechanism exception: " + ex.Message);
        }
        rec.NewValue = "null";
        rec.Summary = "Cleared RenderTexture _MainTex";
        return rec;
    }

    private static FixChangeRecord ApplyTargetedFix(RootCauseResult cause, Transform dt, BaselineSnapshot b)
    {
        if (cause == null || dt == null || cause.PrimaryCause == RootCauseType.Unknown) return null;
        FixChangeRecord rec = null;
        try
        {
            switch (cause.PrimaryCause)
            {
                case RootCauseType.LayerError:
                    rec = FixLayerError(dt, b.ExpectedLayer);
                    break;
                case RootCauseType.ZPositionError:
                    rec = FixZPositionError(dt);
                    break;
                case RootCauseType.ParentError:
                    rec = FixParentError(dt, b.ExpectedParentChain);
                    break;
                case RootCauseType.CanvasWorldCameraError:
                    var wide = FindWideCanvas();
                    if (wide != null) rec = FixCanvasWorldCameraError(wide);
                    break;
                case RootCauseType.RectTransformAnomaly:
                    var rt = dt as RectTransform;
                    if (rt != null) rec = FixRectTransformAnomaly(rt, b);
                    break;
                case RootCauseType.CanvasRendererCull:
                    var cr = dt.GetComponent<CanvasRenderer>();
                    if (cr != null) rec = FixCanvasRendererCull(cr);
                    break;
                case RootCauseType.StencilTestFailure:
                    var g = dt.GetComponent<Graphic>();
                    if (g != null && g.material != null) rec = FixStencilTestFailure(g.material);
                    break;
                case RootCauseType.RenderTextureMechanism:
                    var graphic = dt.GetComponent<Graphic>();
                    if (graphic != null) rec = FixRenderTextureMechanism(graphic);
                    break;
                default:
                    Debug.Log("[DialogueRenderOrderFixer] Unknown root cause, no fix applied");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueRenderOrderFixer] ApplyTargetedFix exception: " + ex.Message);
        }
        if (rec != null)
        {
            Debug.Log("[DialogueRenderOrderFixer] Applied fix: " + rec.Summary + " (old=" + rec.OldValue + " new=" + rec.NewValue + ")");
        }
        return rec;
    }

    // === LOOP CONTROL ===

    private static bool VerifyFixEffect(Transform dt)
    {
        if (dt == null) return false;
        var snap = CollectSnapshot(dt);
        if (snap == null || !snap.IsValid) return false;
        bool visible = snap.Layer == 5
            && !snap.CanvasRendererCull
            && (!Mathf.Approximately(snap.SizeDelta.x, 0f) || !Mathf.Approximately(snap.SizeDelta.y, 0f))
            && snap.MaterialColor.r >= 0.9f && snap.MaterialColor.g >= 0.9f && snap.MaterialColor.b >= 0.9f && snap.MaterialColor.a >= 0.9f;
        if (visible)
        {
            consecutiveVisibleFrames++;
        }
        else
        {
            consecutiveVisibleFrames = 0;
        }
        return consecutiveVisibleFrames >= 60;
    }

    private static void ResetDiagnosisFixLoopOnEngineInit(bool engineInitialized)
    {
        if (engineInitialized && !prevEngineInitialized)
        {
            if (loopContext != null && (loopContext.State == LoopState.Converged || loopContext.State == LoopState.Escalated))
            {
                loopContext.State = LoopState.Diagnosing;
                loopContext.RoundCount = 0;
                Debug.Log("[TEXTVIS] ResetDiagnosisFixLoop: engine initialized, resetting loop from Converged/Escalated to Diagnosing");
            }
            ResetTextPrinterDefaultColors();
        }
        prevEngineInitialized = engineInitialized;
    }

    private static void ResetTextPrinterDefaultColors()
    {
        try
        {
            Component[] allBehaviours = UnityEngine.Object.FindObjectsOfType<Component>();
            int resetCount = 0;
            for (int i = 0; i < allBehaviours.Length; i++)
            {
                Component comp = allBehaviours[i];
                if (comp == null) continue;
                Type compType = comp.GetType();
                if (compType.Name != "RevealableTextPrinterPanel") continue;

                FieldInfo msgField = compType.GetField("defaultMessageColor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (msgField != null && msgField.FieldType == typeof(Color))
                {
                    Color current = (Color)msgField.GetValue(comp);
                    if (current.r < 0.3f && current.g < 0.3f && current.b < 0.3f)
                    {
                        float fixedAlpha = current.a < 0.3f ? 1f : current.a;
                        msgField.SetValue(comp, new Color(1f, 1f, 1f, fixedAlpha));
                        resetCount++;
                        Debug.Log("[TEXTVIS] ResetTextPrinterDefaultColors: fixed defaultMessageColor from (" + current.r.ToString("F2") + "," + current.g.ToString("F2") + "," + current.b.ToString("F2") + "," + current.a.ToString("F2") + ") to white on " + compType.Name);
                    }
                }
                FieldInfo nameField = compType.GetField("defaultNameColor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (nameField != null && nameField.FieldType == typeof(Color))
                {
                    Color currentName = (Color)nameField.GetValue(comp);
                    if (currentName.r < 0.3f && currentName.g < 0.3f && currentName.b < 0.3f)
                    {
                        float fixedNameAlpha = currentName.a < 0.3f ? 1f : currentName.a;
                        nameField.SetValue(comp, new Color(1f, 1f, 1f, fixedNameAlpha));
                        Debug.Log("[TEXTVIS] ResetTextPrinterDefaultColors: fixed defaultNameColor from (" + currentName.r.ToString("F2") + "," + currentName.g.ToString("F2") + "," + currentName.b.ToString("F2") + "," + currentName.a.ToString("F2") + ") to white on " + compType.Name);
                    }
                }
            }
            if (resetCount == 0)
            {
                Debug.Log("[TEXTVIS] ResetTextPrinterDefaultColors: no black defaultMessageColor found (panels checked: " + allBehaviours.Length + " components scanned)");
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] ResetTextPrinterDefaultColors exception: " + ex.Message); }
    }

    private static void RunDiagnosisFixLoop()
    {
        if (loopContext == null)
        {
            loopContext = new DiagnosisFixLoopContext();
            loopContext.MaxRounds = diagnosisFixLoopMaxRounds;
        }
        if (loopContext.State == LoopState.Converged || loopContext.State == LoopState.Escalated) return;

        var dt = FindDialogueText();
        if (dt == null) return;

        loopContext.LastSnapshot = CollectSnapshot(dt);
        if (loopContext.LastSnapshot == null || !loopContext.LastSnapshot.IsValid) return;

        loopContext.LastCause = LocateRootCause(loopContext.LastSnapshot, baseline);

        if (loopContext.LastCause.PrimaryCause == RootCauseType.Unknown)
        {
            if (VerifyFixEffect(dt))
            {
                loopContext.State = LoopState.Converged;
                Debug.Log("[DialogueRenderOrderFixer] Diagnosis-Fix Loop CONVERGED after " + loopContext.RoundCount + " rounds");
                return;
            }
        }
        else
        {
            var rec = ApplyTargetedFix(loopContext.LastCause, dt, baseline);
            if (loopContext.LastFixRecords == null) loopContext.LastFixRecords = new List<FixChangeRecord>();
            if (rec != null) loopContext.LastFixRecords.Add(rec);
        }

        if (VerifyFixEffect(dt))
        {
            loopContext.State = LoopState.Converged;
            Debug.Log("[DialogueRenderOrderFixer] Diagnosis-Fix Loop CONVERGED after " + loopContext.RoundCount + " rounds");
        }
        else
        {
            loopContext.RoundCount++;
            if (loopContext.RoundCount >= loopContext.MaxRounds)
            {
                loopContext.State = LoopState.Escalated;
                Debug.LogError("[DialogueRenderOrderFixer] Diagnosis-Fix Loop ESCALATED after " + loopContext.RoundCount + " rounds. Manual intervention required. Last root cause: " + (loopContext.LastCause != null ? loopContext.LastCause.PrimaryCause.ToString() : "null") + " Supporting data: " + (loopContext.LastCause != null ? loopContext.LastCause.SupportingData : "null"));
            }
        }
    }

    private static TextColorSnapshot CollectTextColorSnapshot(Text textComp, Material mat, Transform dt)
    {
        var s = new TextColorSnapshot();
        if (textComp == null) return s;
        try
        {
            s.TextCompColor = textComp.color;
            s.IsBlack = s.TextCompColor.r < 0.01f && s.TextCompColor.g < 0.01f && s.TextCompColor.b < 0.01f;
            s.IsAlphaTooLow = s.TextCompColor.a < 0.01f;
            if (mat != null && mat.HasProperty("_Color"))
            {
                s.MaterialColor = mat.GetColor("_Color");
                s.IsMaterialBlack = s.MaterialColor.r < 0.01f && s.MaterialColor.g < 0.01f && s.MaterialColor.b < 0.01f;
                s.IsMaterialAlphaTooLow = s.MaterialColor.a < 0.01f;
            }
            var cr = dt.GetComponent<CanvasRenderer>();
            if (cr != null) s.CanvasRendererColor = cr.GetColor();
            s.BaselineColor = new Color(1f, 1f, 1f, 1f);
            s.HasBaseline = true;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectTextColor: " + ex.Message); }
        return s;
    }

    private static TemporalTraceSnapshot CollectTemporalTraceSnapshot(Text textComp, Material mat, Transform dt)
    {
        var s = new TemporalTraceSnapshot();
        if (textComp == null) return s;
        try
        {
            s.CurrentAlpha = textComp.color.a;
            s.CurrentText = textComp.text ?? "";
            s.CurrentTextLength = s.CurrentText.Length;
            if (mat != null && mat.HasProperty("_Color")) s.CurrentMaterialAlpha = mat.GetColor("_Color").a;
            if (temporalTraceInitialized)
            {
                s.PrevAlpha = prevTextAlpha;
                s.PrevMaterialAlpha = prevMaterialAlpha;
                s.PrevText = prevTextContent ?? "";
                s.AlphaDroppedToZero = prevTextAlpha > 0.5f && s.CurrentAlpha < 0.01f;
                s.TextClearedAfterContent = (prevTextContent != null && prevTextContent.Length > 0 && s.CurrentTextLength == 0);
            }
            prevTextAlpha = s.CurrentAlpha;
            prevMaterialAlpha = s.CurrentMaterialAlpha;
            prevTextContent = s.CurrentText;
            temporalTraceInitialized = true;
            s.WasInitialized = true;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectTemporalTrace: " + ex.Message); }
        return s;
    }

    private static RenderOcclusionSnapshot CollectRenderOcclusionSnapshot(Transform dt, Graphic graphic)
    {
        var s = new RenderOcclusionSnapshot();
        if (dt == null) return s;
        try
        {
            s.SiblingIndex = dt.GetSiblingIndex();
            s.ZPosition = dt.position.z;
            var canvas = dt.GetComponentInParent<Canvas>();
            if (canvas != null) s.SortingOrder = canvas.sortingOrder;
            var parent = dt.parent;
            if (parent != null)
            {
                s.SiblingCount = parent.childCount;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    if (sibling == dt) continue;
                    var sibCanvas = sibling.GetComponent<Canvas>();
                    int sibOrder = sibCanvas != null ? sibCanvas.sortingOrder : 0;
                    if (sibOrder > s.SortingOrder && i > s.SiblingIndex)
                    {
                        s.IsOccluded = true;
                        s.OccludingElementName = sibling.name;
                        s.OccludingSortingOrder = sibOrder;
                    }
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectRenderOcclusion: " + ex.Message); }
        return s;
    }

    private static PrinterBehaviorSnapshot CollectPrinterBehaviorSnapshot(Transform dt, Text textComp)
    {
        var s = new PrinterBehaviorSnapshot();
        try
        {
            if (engineType != null)
            {
                var engineProp = engineType.GetProperty("ActivePrinter", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (engineProp != null)
                {
                    var printer = engineProp.GetValue(null, null);
                    if (printer != null)
                    {
                        s.ActivePrinterTypeName = printer.GetType().Name;
                        var goProp = printer.GetType().GetProperty("gameObject");
                        if (goProp != null)
                        {
                            var go = goProp.GetValue(printer, null) as GameObject;
                            if (go != null) s.ActivePrinterActiveInHierarchy = go.activeInHierarchy;
                        }
                        var printingProp = printer.GetType().GetProperty("IsPrinting");
                        if (printingProp != null) s.IsPrinting = (bool)printingProp.GetValue(printer, null);
                    }
                }
            }
            s.AutoWait = 0f;
            s.PrintSpeed = 0f;
            s.RevealSpeed = 0f;
            s.AutoPlay = false;
            s.ConfigSource = "Unknown";
            s.ReflectionSucceeded = !string.IsNullOrEmpty(s.ActivePrinterTypeName);
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectPrinterBehavior: " + ex.Message); }
        return s;
    }

    // === TEXT VISIBILITY DIAGNOSTIC ===

    private static void LogTextVisibilityDiagnostic()
    {
        var dt = FindDialogueText();
        if (dt == null) return;
        var snap = CollectVisibilitySnapshot(dt);
        if (snap == null || !snap.IsValid) return;
        string log = "[TEXTVIS] frame=" + frameCount + " path=" + snap.ObjectPath;
        log += "\n[SHADER] find=" + snap.Shader.ShaderFindResult + " supported=" + snap.Shader.IsSupported + " compileErr=" + snap.Shader.HasCompileError + " degenSkip=" + snap.Shader.SourceContainsDegenerateSkip;
        log += " charProp=" + snap.Shader.CharClipRectPropertyExists + " lineProp=" + snap.Shader.LineClipRectPropertyExists + " matShader=\"" + snap.Shader.MaterialShaderName + "\"";
        log += "\n[MATMAINTEX] hasProp=" + snap.MaterialMainTex.HasMainTexProperty + " isNull=" + snap.MaterialMainTex.MainTexIsNull + " name=\"" + snap.MaterialMainTex.MainTexName + "\"";
        log += " size=" + snap.MaterialMainTex.MainTexWidth + "x" + snap.MaterialMainTex.MainTexHeight + " instance=" + snap.MaterialMainTex.IsInstanceMaterial;
        log += " fontMatNull=" + snap.MaterialMainTex.FontMaterialMainTexIsNull + " fontMatName=\"" + snap.MaterialMainTex.FontMaterialMainTexName + "\"";
        log += "\n[FONTMAT] fontNull=" + snap.FontMaterialConfig.FontIsNull + " fontName=\"" + snap.FontMaterialConfig.FontName + "\"";
        log += " fontMatNull=" + snap.FontMaterialConfig.FontMaterialIsNull + " fontMatName=\"" + snap.FontMaterialConfig.FontMaterialName + "\" fontMatShader=\"" + snap.FontMaterialConfig.FontMaterialShaderName + "\"";
        log += " matNull=" + snap.FontMaterialConfig.MaterialIsNull + " mainTexNull=" + snap.FontMaterialConfig.MainTextureIsNull + " mainTexName=\"" + snap.FontMaterialConfig.MainTextureName + "\" guidValid=" + snap.FontMaterialConfig.FontGuidValid;
        log += "\n[CLIPRECT] char=(" + snap.ClipRectUpdate.CharClipRect.x.ToString("F0") + "," + snap.ClipRectUpdate.CharClipRect.y.ToString("F0") + "," + snap.ClipRectUpdate.CharClipRect.z.ToString("F0") + "," + snap.ClipRectUpdate.CharClipRect.w.ToString("F0") + ")";
        log += " line=(" + snap.ClipRectUpdate.LineClipRect.x.ToString("F0") + "," + snap.ClipRectUpdate.LineClipRect.y.ToString("F0") + "," + snap.ClipRectUpdate.LineClipRect.z.ToString("F0") + "," + snap.ClipRectUpdate.LineClipRect.w.ToString("F0") + ")";
        log += " charZero=" + snap.ClipRectUpdate.IsCharClipRectZero + " lineZero=" + snap.ClipRectUpdate.IsLineClipRectZero;
        log += " trbExists=" + snap.ClipRectUpdate.TextRevealBehaviourExists + " trbActive=" + snap.ClipRectUpdate.TextRevealBehaviourActive;
        log += " updateMethod=" + snap.ClipRectUpdate.UpdateClipRectsMethodExists + " changed=" + snap.ClipRectUpdate.ClipRectChangedSinceLastFrame;
        log += "\n[NANIINIT] revealableLoaded=" + snap.NaninovelInit.RevealableTextTypeLoaded + " engineFound=" + snap.NaninovelInit.EngineTypeFound;
        log += " engineInit=" + snap.NaninovelInit.EngineInitialized + " printMgrFound=" + snap.NaninovelInit.PrintManagerTypeFound;
        log += " activePrinter!=" + snap.NaninovelInit.ActivePrinterNotNull + " cfgLoaded=" + snap.NaninovelInit.ConfigResourcesLoaded + " cfgCount=" + snap.NaninovelInit.ConfigResourceCount;
        log += " goActive=" + snap.NaninovelInit.GameObjectActiveInHierarchy + " compEnabled=" + snap.NaninovelInit.ComponentEnabled;
        string textPreview = snap.TextProp.Text != null && snap.TextProp.Text.Length > 40 ? snap.TextProp.Text.Substring(0, 40) + "..." : snap.TextProp.Text;
        log += "\n[TEXTPROP] text=\"" + textPreview + "\"";
        log += " len=" + snap.TextProp.TextLength + " isNull=" + snap.TextProp.TextIsNullOrEmpty + " placeholder=" + snap.TextProp.IsPlaceholderText;
        log += " charVisible=" + snap.TextProp.CharacterCountVisible + " charCount=" + snap.TextProp.CharacterCount + " vertCount=" + snap.TextProp.VertexCount + " changed=" + snap.TextProp.TextChangedSinceLastFrame;
        log += "\n[FONTTEX] fontMatNull=" + snap.FontTexturePixel.FontMaterialMainTexIsNull + " name=\"" + snap.FontTexturePixel.FontMaterialMainTexName + "\"";
        log += " size=" + snap.FontTexturePixel.FontMaterialMainTexWidth + "x" + snap.FontTexturePixel.FontMaterialMainTexHeight + " fmt=" + snap.FontTexturePixel.FontMaterialMainTexFormat;
        log += " dynamic=" + snap.FontTexturePixel.IsDynamicFont + " transparentRatio=" + snap.FontTexturePixel.AllTransparentRatio.ToString("F2") + " whiteRatio=" + snap.FontTexturePixel.AllWhiteRatio.ToString("F2");
        log += " valid=" + snap.FontTexturePixel.IsTextureValid + " pixels=" + snap.FontTexturePixel.SampledPixelCount + " crMainTex=\"" + snap.FontTexturePixel.CanvasRendererMainTexName + "\"";
        log += "\n[TEXTCOLOR] textComp=(" + snap.TextColor.TextCompColor.r.ToString("F2") + "," + snap.TextColor.TextCompColor.g.ToString("F2") + "," + snap.TextColor.TextCompColor.b.ToString("F2") + "," + snap.TextColor.TextCompColor.a.ToString("F2") + ")";
        log += " isBlack=" + snap.TextColor.IsBlack + " alphaLow=" + snap.TextColor.IsAlphaTooLow;
        log += " matColor=(" + snap.TextColor.MaterialColor.r.ToString("F2") + "," + snap.TextColor.MaterialColor.g.ToString("F2") + "," + snap.TextColor.MaterialColor.b.ToString("F2") + "," + snap.TextColor.MaterialColor.a.ToString("F2") + ")";
        log += " matBlack=" + snap.TextColor.IsMaterialBlack + " matAlphaLow=" + snap.TextColor.IsMaterialAlphaTooLow;
        log += "\n[TEMPORAL] curAlpha=" + snap.TemporalTrace.CurrentAlpha.ToString("F2") + " prevAlpha=" + snap.TemporalTrace.PrevAlpha.ToString("F2");
        log += " alphaDropped=" + snap.TemporalTrace.AlphaDroppedToZero + " textCleared=" + snap.TemporalTrace.TextClearedAfterContent;
        log += " curLen=" + snap.TemporalTrace.CurrentTextLength + " init=" + snap.TemporalTrace.WasInitialized;
        log += "\n[OCCLUSION] sortOrder=" + snap.RenderOcclusion.SortingOrder + " sibIdx=" + snap.RenderOcclusion.SiblingIndex + " z=" + snap.RenderOcclusion.ZPosition.ToString("F2");
        log += " occluded=" + snap.RenderOcclusion.IsOccluded + " by=\"" + snap.RenderOcclusion.OccludingElementName + "\" occlOrder=" + snap.RenderOcclusion.OccludingSortingOrder + " sibCount=" + snap.RenderOcclusion.SiblingCount;
        log += "\n[PRINTER] type=\"" + snap.PrinterBehavior.ActivePrinterTypeName + "\" active=" + snap.PrinterBehavior.ActivePrinterActiveInHierarchy;
        log += " isPrinting=" + snap.PrinterBehavior.IsPrinting + " autoPlay=" + snap.PrinterBehavior.AutoPlay + " reflectOK=" + snap.PrinterBehavior.ReflectionSucceeded;
        if (enableTMProShaderDiagnosis)
        {
            log += "\n[TMPROSHADER] find=" + snap.TMProShader.ShaderFindResult + " supported=" + snap.TMProShader.IsSupported;
            log += " includeResolved=" + snap.TMProShader.IncludePathResolved + " has60000=" + snap.TMProShader.Has60000Threshold;
            log += " matShader=\"" + snap.TMProShader.MaterialShaderName + "\"";
            if (!snap.TMProShader.IncludePathResolved) log += " includeErr=\"" + snap.TMProShader.IncludePathErrorMessage + "\"";
        }
        if (enableResourcePathDiagnosis)
        {
            log += "\n[RESPATH] prefix=\"" + snap.ResourcePath.PathPrefix + "\" defaultId=\"" + snap.ResourcePath.DefaultPrinterId + "\"";
            log += " widePath=\"" + snap.ResourcePath.WidePrefabActualPath + "\" matched=" + snap.ResourcePath.PathMatched + " exists=" + snap.ResourcePath.WidePrefabExists;
            log += " impl=\"" + snap.ResourcePath.Implementation + "\"";
        }
        if (enableErrorContentCollection && snap.ErrorContent.Entries != null && snap.ErrorContent.Entries.Count > 0)
        {
            log += "\n[ERRORS] total=" + snap.ErrorContent.TotalCount + " collected=" + snap.ErrorContent.CollectedCount + " dialogueRelated=" + snap.ErrorContent.DialogueRelatedCount;
            foreach (var kv in snap.ErrorContent.CategoryCounts) log += " " + kv.Key + "=" + kv.Value;
            int shown = 0;
            foreach (var e in snap.ErrorContent.Entries) { if (shown < 20) { log += "\n  ERR[" + e.Category + "] " + e.Message.Substring(0, System.Math.Min(100, e.Message.Length)) + (e.Category == ErrorCategory.Other ? " (Other category, manual classification needed)" : ""); shown++; } }
        }
        if (enableComponentTypeDiagnosis)
        {
            log += "\n[COMPTYPE] hasText=" + snap.ComponentType.HasTextComponent + " hasTMP=" + snap.ComponentType.HasTMPTextComponent + " hasGraphic=" + snap.ComponentType.HasGraphicComponent;
            log += " runtimeType=\"" + snap.ComponentType.RuntimeComponentType + "\" consistentPrefab=" + snap.ComponentType.ConsistentWithPrefab;
            log += " shaderMatched=" + snap.ComponentType.ShaderMatched + " consistentConfig=" + snap.ComponentType.ConsistentWithConfig;
        }
        if (enableEngineInitDiagnosis)
        {
            log += "\n[ENGINEINIT] uiTextPrinterInst=" + snap.NaninovelInit.UITextPrinterInstantiated + " revealableCreated=" + snap.NaninovelInit.RevealableUITextCreated;
            log += " printCmdExec=" + snap.NaninovelInit.PrintCommandExecutable;
            if (!string.IsNullOrEmpty(snap.NaninovelInit.EngineInitErrorMessage)) log += " err=\"" + snap.NaninovelInit.EngineInitErrorMessage + "\"";
        }
        Debug.Log(log);
    }

    private static TextContentTraceSnapshot CollectTextContentTraceSnapshot(Text textComp, bool engineInitialized)
    {
        var s = new TextContentTraceSnapshot();
        try
        {
            s.CurrentTextLength = textComp != null ? textComp.text.Length : 0;
            s.PrevTextLength = prevTextContentLength;
            s.PrintExecutedInferred = (prevTextContentLength == 0 && s.CurrentTextLength > 0);
            s.TracedFrameCount = frameCount;
            s.TextEmptyForAllFrames = (s.CurrentTextLength == 0 && frameCount > 60);
            if (s.TextEmptyForAllFrames)
            {
                s.EmptyReasonInferred = engineInitialized ? "@print not executed" : "engine not initialized";
            }
            else if (s.CurrentTextLength == 0)
            {
                s.EmptyReasonInferred = "waiting (frame " + frameCount + "/60)";
            }
            else
            {
                s.EmptyReasonInferred = "text present";
            }
            if (textComp != null && textComp.text.Contains("%0"))
            {
                s.ContainsPlaceholder = true;
                s.PlaceholderType = "%0";
                Debug.LogWarning("[TEXTVIS] %0 placeholder detected, script variable substitution may be broken");
            }
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.FullName == "Naninovel.CustomVariables" || t.FullName == "Elringus.Naninovel.CustomVariables")
                            {
                                var initProp = t.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
                                if (initProp != null) s.CustomVariablesServiceActive = (bool)initProp.GetValue(null, null);
                                break;
                            }
                        }
                    }
                    catch { }
                    if (s.CustomVariablesServiceActive) break;
                }
            }
            catch { }
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.FullName == "Naninovel.ScriptPlayer" || t.FullName == "Elringus.Naninovel.ScriptPlayer")
                            {
                                var playingProp = t.GetProperty("Playing", BindingFlags.Public | BindingFlags.Static);
                                if (playingProp != null) s.ScriptPlayerActive = (bool)playingProp.GetValue(null, null);
                                break;
                            }
                        }
                    }
                    catch { }
                    if (s.ScriptPlayerActive) break;
                }
            }
            catch { }
            prevTextContentLength = s.CurrentTextLength;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectTextContentTraceSnapshot: " + ex.Message); }
        return s;
    }

    private static ShadowDiagnosisSnapshot CollectShadowDiagnosisSnapshot(Transform dt)
    {
        var s = new ShadowDiagnosisSnapshot();
        try
        {
            var shadow = dt.GetComponent<Shadow>();
            s.ShadowExists = shadow != null;
            if (shadow != null)
            {
                s.EffectColor = shadow.effectColor;
                s.EffectDistance = shadow.effectDistance;
                s.IsShadowBlack = s.EffectColor.r < 0.01f && s.EffectColor.g < 0.01f && s.EffectColor.b < 0.01f;
            }
            var graphic = dt.GetComponent<Graphic>();
            s.TextHasVertices = graphic != null && graphic.canvasRenderer != null && graphic.canvasRenderer.hasMoved;
            if (s.IsShadowBlack && !s.TextHasVertices) s.BlackSourceInferred = "shadow";
            else if (!s.IsShadowBlack && s.TextHasVertices) s.BlackSourceInferred = "text";
            else if (s.IsShadowBlack && s.TextHasVertices) s.BlackSourceInferred = "both";
            else s.BlackSourceInferred = "none";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] CollectShadowDiagnosisSnapshot: " + ex.Message); }
        return s;
    }

    private static DialogueTextVisibilitySnapshot CollectVisibilitySnapshot(Transform dt)
    {
        var snap = new DialogueTextVisibilitySnapshot();
        snap.IsValid = false;
        snap.Frame = frameCount;
        snap.BaselineSrc = BaselineSource.StaticConfig;
        if (dt == null) return snap;
        try
        {
            snap.ObjectPath = GetParentChain(dt);
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null) return snap;
            var mat = graphic.material;
            var textComp = graphic as Text;
            snap.Shader = CollectShaderSnapshot(mat);
            snap.MaterialMainTex = CollectMaterialMainTexSnapshot(mat, textComp);
            snap.FontMaterialConfig = CollectFontMaterialConfigSnapshot(textComp, mat);
            snap.ClipRectUpdate = CollectClipRectUpdateSnapshot(dt, mat);
            snap.NaninovelInit = CollectNaninovelInitSnapshot(dt, graphic);
            snap.TextProp = CollectTextPropertySnapshot(textComp);
            snap.FontTexturePixel = CollectFontTexturePixelSnapshot(textComp, dt);
            snap.TextColor = CollectTextColorSnapshot(textComp, mat, dt);
            snap.TemporalTrace = CollectTemporalTraceSnapshot(textComp, mat, dt);
            snap.RenderOcclusion = CollectRenderOcclusionSnapshot(dt, graphic);
            snap.PrinterBehavior = CollectPrinterBehaviorSnapshot(dt, textComp);
            snap.TMProShader = CollectTMProShaderSnapshot(mat);
            snap.ResourcePath = CollectResourcePathSnapshot();
            snap.ErrorContent = CollectErrorContentSnapshot();
            snap.TextContentTrace = CollectTextContentTraceSnapshot(textComp, snap.NaninovelInit.EngineInitialized);
            snap.ShadowDiag = CollectShadowDiagnosisSnapshot(dt);
            snap.ComponentType = CollectComponentTypeSnapshot(dt);
            snap.IsValid = true;
        }
        catch (NullReferenceException) { }
        catch (MissingReferenceException) { }
        return snap;
    }

    private static ShaderCompilationSnapshot CollectShaderSnapshot(Material mat)
    {
        var s = new ShaderCompilationSnapshot();
        if (mat != null && mat.shader != null)
        {
            s.MaterialShaderName = mat.shader.name;
            s.IsPinkFallbackShader = mat.shader.name == "Hidden/InternalErrorShader";
            var shader = Shader.Find("Naninovel/RevealableText");
            s.ShaderFindResult = shader != null;
            if (shader != null)
            {
                s.IsSupported = shader.isSupported;
                s.CharClipRectPropertyExists = mat.HasProperty("_CharClipRect");
                s.LineClipRectPropertyExists = mat.HasProperty("_LineClipRect");
                s.MainTexPropertyExists = mat.HasProperty("_MainTex");
                s.ColorPropertyExists = mat.HasProperty("_Color");
                s.CharFadeWidthPropertyExists = mat.HasProperty("_CharFadeWidth");
                s.CharSlantAnglePropertyExists = mat.HasProperty("_CharSlantAngle");
            }
        }
        try
        {
            string shaderPath = System.IO.Path.Combine(Application.dataPath, "Resources/naninovel/shaders/revealabletext.shader");
            if (System.IO.File.Exists(shaderPath))
            {
                string src = System.IO.File.ReadAllText(shaderPath);
                s.SourceContainsDegenerateSkip = src.Contains("_CharClipRect.z > _CharClipRect.x") || src.Contains("_LineClipRect.z > _LineClipRect.x");
            }
        }
        catch { }
        try
        {
            var btn = UnityEngine.Object.FindObjectOfType<UnityEngine.UI.Button>();
            if (btn != null)
            {
                var btnGraphic = btn.GetComponent<Graphic>();
                if (btnGraphic != null && btnGraphic.material != null && btnGraphic.material.shader != null)
                {
                    s.ButtonShaderName = btnGraphic.material.shader.name;
                    s.ButtonShaderSupported = btnGraphic.material.shader.isSupported;
                }
            }
        }
        catch { }
        return s;
    }

    private static MaterialMainTexSnapshot CollectMaterialMainTexSnapshot(Material mat, Text textComp)
    {
        var s = new MaterialMainTexSnapshot();
        if (mat == null) return s;
        s.HasMainTexProperty = mat.HasProperty("_MainTex");
        s.IsInstanceMaterial = !mat.name.StartsWith("Default-");
        var mainTex = mat.GetTexture("_MainTex");
        s.MainTexIsNull = mainTex == null;
        if (mainTex != null)
        {
            s.MainTexName = mainTex.name;
            var tex2d = mainTex as Texture2D;
            if (tex2d != null) { s.MainTexWidth = tex2d.width; s.MainTexHeight = tex2d.height; }
        }
        if (textComp != null && textComp.font != null && textComp.font.material != null)
        {
            var fmt = textComp.font.material.mainTexture;
            s.FontMaterialMainTexIsNull = fmt == null;
            if (fmt != null) s.FontMaterialMainTexName = fmt.name;
            s.SameRef = ReferenceEquals(mainTex, fmt);
        }
        try
        {
            if (mat.HasProperty("_Color"))
            {
                s.ColorValue = mat.GetColor("_Color");
                s.IsColorWhite = s.ColorValue.r > 0.99f && s.ColorValue.g > 0.99f && s.ColorValue.b > 0.99f && s.ColorValue.a > 0.99f;
            }
            if (mat.shader != null)
            {
                s.IsPinkFallbackShader = mat.shader.name == "Hidden/InternalErrorShader";
            }
            var btn = UnityEngine.Object.FindObjectOfType<UnityEngine.UI.Button>();
            if (btn != null)
            {
                var btnGraphic = btn.GetComponent<Graphic>();
                if (btnGraphic != null && btnGraphic.material != null)
                {
                    s.ButtonMaterialShaderName = btnGraphic.material.shader != null ? btnGraphic.material.shader.name : "null";
                    s.ButtonMainTexIsNull = btnGraphic.material.GetTexture("_MainTex") == null;
                }
            }
        }
        catch { }
        return s;
    }

    private static FontMaterialConfigSnapshot CollectFontMaterialConfigSnapshot(Text textComp, Material mat)
    {
        var s = new FontMaterialConfigSnapshot();
        if (textComp == null) return s;
        s.FontIsNull = textComp.font == null;
        if (textComp.font != null)
        {
            s.FontName = textComp.font.name;
            s.FontMaterialIsNull = textComp.font.material == null;
            if (textComp.font.material != null)
            {
                s.FontMaterialName = textComp.font.material.name;
                s.FontMaterialShaderName = textComp.font.material.shader != null ? textComp.font.material.shader.name : "null";
            }
        }
        s.MaterialIsNull = mat == null;
        if (mat != null)
        {
            s.MaterialName = mat.name;
            s.MaterialShaderName = mat.shader != null ? mat.shader.name : "null";
            var mainTex = mat.mainTexture;
            s.MainTextureIsNull = mainTex == null;
            if (mainTex != null) s.MainTextureName = mainTex.name;
        }
        return s;
    }

    private static ClipRectUpdateSnapshot CollectClipRectUpdateSnapshot(Transform dt, Material mat)
    {
        var s = new ClipRectUpdateSnapshot();
        if (mat == null) return s;
        s.CharClipRect = mat.GetVector(charClipRectId);
        s.LineClipRect = mat.GetVector(lineClipRectId);
        s.IsCharClipRectZero = s.CharClipRect == Vector4.zero;
        s.IsLineClipRectZero = s.LineClipRect == Vector4.zero;
        if (textRevealBehaviourType != null && typeof(UnityEngine.Object).IsAssignableFrom(textRevealBehaviourType))
        {
            var comps = UnityEngine.Object.FindObjectsOfType(textRevealBehaviourType);
            s.TextRevealBehaviourExists = comps != null && comps.Length > 0;
            if (comps != null && comps.Length > 0)
            {
                var c = comps[0] as Component;
                if (c != null)
                {
                    s.TextRevealBehaviourActive = c.gameObject.activeInHierarchy;
                    var beh = c as Behaviour;
                    if (beh != null) s.TextRevealBehaviourActive = s.TextRevealBehaviourActive && beh.enabled;
                }
                var updateMethod = textRevealBehaviourType.GetMethod("UpdateClipRects", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var onRectMethod = textRevealBehaviourType.GetMethod("OnRectTransformDimensionsChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                s.UpdateClipRectsMethodExists = updateMethod != null;
                s.OnRectDimensionsChangeMethodExists = onRectMethod != null;
            }
        }
        string key = dt.GetInstanceID().ToString();
        Vector4 prevChar, prevLine;
        if (prevCharClipRects.TryGetValue(key, out prevChar))
        {
            s.ClipRectChangedSinceLastFrame = s.CharClipRect != prevChar;
        }
        if (prevLineClipRects.TryGetValue(key, out prevLine))
        {
            s.ClipRectUpdateTriggeredInferred = s.LineClipRect != prevLine || s.CharClipRect != prevChar;
        }
        prevCharClipRects[key] = s.CharClipRect;
        prevLineClipRects[key] = s.LineClipRect;
        try
        {
            if (mat.shader != null)
            {
                string sn = mat.shader.name;
                string sp = null;
                if (sn == "Naninovel/RevealableText") sp = System.IO.Path.Combine(Application.dataPath, "Resources/naninovel/shaders/revealabletext.shader");
                else if (sn == "Naninovel/RevealableTMProText") sp = System.IO.Path.Combine(Application.dataPath, "Resources/naninovel/shaders/revealabletmprotext.shader");
                if (sp != null && System.IO.File.Exists(sp))
                {
                    string src = System.IO.File.ReadAllText(sp);
                    s.ShaderHas60000Threshold = src.Contains("charClipWidth < 60000.0");
                }
            }
            s.ZeroVectorCausesClipping = (s.IsCharClipRectZero || s.IsLineClipRectZero) && !s.ShaderHas60000Threshold;
            s.CausesTextDisappear = s.ZeroVectorCausesClipping;
        }
        catch { }
        return s;
    }

    private static NaninovelInitSnapshot CollectNaninovelInitSnapshot(Transform dt, Graphic graphic)
    {
        var s = new NaninovelInitSnapshot();
        s.RevealableTextTypeLoaded = revealableTextType != null;
        s.EngineTypeFound = engineType != null;
        if (engineType != null)
        {
            var initProp = engineType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
            if (initProp != null)
            {
                try { s.EngineInitialized = (bool)initProp.GetValue(null, null); } catch { }
            }
        }
        s.PrintManagerTypeFound = printManagerType != null;
        if (printManagerType != null)
        {
            var activeProp = printManagerType.GetProperty("ActivePrinter", BindingFlags.Public | BindingFlags.Instance);
            if (activeProp != null)
            {
                try
                {
                    var instances = UnityEngine.Object.FindObjectsOfType(printManagerType);
                    if (instances != null && instances.Length > 0)
                    {
                        var printer = activeProp.GetValue(instances[0], null);
                        s.ActivePrinterNotNull = printer != null;
                        if (s.ActivePrinterNotNull && !printerActiveLogged)
                        {
                            printerActiveLogged = true;
                            Debug.Log("[TEXTVIS] ActivePrinter first activated at frame " + frameCount);
                        }
                    }
                }
                catch { }
            }
        }
        try
        {
            var resources = Resources.LoadAll<UnityEngine.Object>("naninovel");
            s.ConfigResourcesLoaded = resources != null && resources.Length > 0;
            s.ConfigResourceCount = resources != null ? resources.Length : 0;
        }
        catch { }
        s.GameObjectActiveInHierarchy = dt != null && dt.gameObject.activeInHierarchy;
        s.ComponentEnabled = graphic != null && graphic.enabled;
        try
        {
            Type uiTextPrinterType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Naninovel.UI.UITextPrinter");
                if (t != null) { uiTextPrinterType = t; break; }
            }
            if (uiTextPrinterType != null)
            {
                var instances = UnityEngine.Object.FindObjectsOfType(uiTextPrinterType);
                s.UITextPrinterInstantiated = instances != null && instances.Length > 0;
            }
        }
        catch (Exception ex) { s.EngineInitErrorMessage = "UITextPrinter check failed: " + ex.Message; }
        s.RevealableUITextCreated = revealableTextType != null && dt != null && dt.GetComponent(revealableTextType) != null;
        try
        {
            Type commandManagerType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Naninovel.Commands.CommandManager");
                if (t != null) { commandManagerType = t; break; }
            }
            s.PrintCommandExecutable = commandManagerType != null;
        }
        catch { }
        return s;
    }

    private static TMProShaderCompilationSnapshot CollectTMProShaderSnapshot(Material mat)
    {
        var s = new TMProShaderCompilationSnapshot();
        try
        {
            if (mat != null && mat.shader != null) s.MaterialShaderName = mat.shader.name;
            var shader = Shader.Find("Naninovel/RevealableTMProText");
            s.ShaderFindResult = shader != null;
            if (shader != null) s.IsSupported = shader.isSupported;
            string shaderPath = System.IO.Path.Combine(Application.dataPath, "Resources/naninovel/shaders/revealabletmprotext.shader");
            if (System.IO.File.Exists(shaderPath))
            {
                string src = System.IO.File.ReadAllText(shaderPath);
                s.Has60000Threshold = src.Contains("charClipWidth < 60000.0");
                s.IncludePathResolved = !src.Contains("#include \"TMPro_Properties.cginc\"") || System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "Resources/naninovel/shaders/TMPro_Properties.cginc"));
                if (!s.IncludePathResolved) s.IncludePathErrorMessage = "TMPro_Properties.cginc not found in shader directory";
            }
            else
            {
                s.IncludePathResolved = false;
                s.IncludePathErrorMessage = "revealabletmprotext.shader not found at " + shaderPath;
            }
        }
        catch (Exception ex) { s.CompilationErrorMessage = "CollectTMProShaderSnapshot failed: " + ex.Message; }
        return s;
    }

    private static ResourcePathSnapshot CollectResourcePathSnapshot()
    {
        var s = new ResourcePathSnapshot();
        try
        {
            Type configType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Naninovel.Configuration.TextPrintersConfiguration");
                if (t != null) { configType = t; break; }
            }
            if (configType != null)
            {
                var pathPrefixProp = configType.GetProperty("PathPrefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pathPrefixProp != null) s.PathPrefix = pathPrefixProp.GetValue(null, null) as string;
                var defaultPrinterProp = configType.GetProperty("DefaultPrinterId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (defaultPrinterProp != null) s.DefaultPrinterId = defaultPrinterProp.GetValue(null, null) as string;
                var implProp = configType.GetProperty("Implementation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (implProp != null) s.Implementation = implProp.GetValue(null, null) as string;
            }
            if (string.IsNullOrEmpty(s.PathPrefix)) s.PathPrefix = "(unknown)";
            if (string.IsNullOrEmpty(s.DefaultPrinterId)) s.DefaultPrinterId = "(unknown)";
            if (string.IsNullOrEmpty(s.Implementation)) s.Implementation = "(unknown)";
            string[] guids = AssetDatabase.FindAssets("Wide t:Prefab");
            s.WidePrefabExists = guids != null && guids.Length > 0;
            if (s.WidePrefabExists)
            {
                s.WidePrefabActualPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                s.PathMatched = s.WidePrefabActualPath != null && s.WidePrefabActualPath.Contains(s.PathPrefix);
            }
            else
            {
                s.PathMatched = false;
            }
        }
        catch { }
        return s;
    }

    private static ErrorContentSnapshot CollectErrorContentSnapshot()
    {
        var s = new ErrorContentSnapshot();
        s.Entries = new List<ErrorContentEntry>();
        s.CategoryCounts = new Dictionary<ErrorCategory, int>();
        s.ErrorPatternCounts = new Dictionary<string, int>();
        RegisterLogMessageReceivedCallback();
        try
        {
            var logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
            if (logEntriesType != null)
            {
                var getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                if (getCountMethod != null)
                {
                    int totalCount = (int)getCountMethod.Invoke(null, null);
                    s.TotalCount = totalCount;
                    int limit = System.Math.Min(totalCount, errorCollectionMaxCount);
                    var getEntryMethod = logEntriesType.GetMethod("GetEntry", BindingFlags.Static | BindingFlags.Public);
                    if (getEntryMethod != null)
                    {
                        for (int i = 0; i < limit; i++)
                        {
                            try
                            {
                                string message = "", stack = "";
                                int logType = 0;
                                object[] parms = new object[] { i, message, stack, logType };
                                getEntryMethod.Invoke(null, parms);
                                message = parms[1] as string ?? "";
                                stack = parms[2] as string ?? "";
                                logType = (int)parms[3];
                                if (logType != 2) continue;
                                if (loggedErrors.Contains(message)) continue;
                                loggedErrors.Add(message);
                                var entry = new ErrorContentEntry();
                                entry.Message = message;
                                entry.StackTrace = stack;
                                entry.ErrorType = "LogError";
                                if (message.Contains("Shader")) entry.Category = ErrorCategory.ShaderCompilation;
                                else if (message.Contains("Resources") || message.Contains("Load")) entry.Category = ErrorCategory.ResourceLoading;
                                else if (message.Contains("NullReference")) entry.Category = ErrorCategory.NullReference;
                                else if (message.Contains("MissingReference")) entry.Category = ErrorCategory.MissingReference;
                                else if (message.Contains("Naninovel")) entry.Category = ErrorCategory.NaninovelRuntime;
                                else if (message.Contains("TypeLoad") || message.Contains("Could not load") || message.Contains("type load")) entry.Category = ErrorCategory.TypeLoad;
                                else entry.Category = ErrorCategory.Other;
                                entry.RelatedToDialogueText = message.Contains("Revealable") || message.Contains("Printer") || message.Contains("Dialogue") || message.Contains("Text") || message.Contains("Shader");
                                if (entry.RelatedToDialogueText) s.DialogueRelatedCount++;
                                if (!s.CategoryCounts.ContainsKey(entry.Category)) s.CategoryCounts[entry.Category] = 0;
                                s.CategoryCounts[entry.Category]++;
                                s.Entries.Add(entry);
                            }
                            catch { }
                        }
                    }
                }
            }
            if (realtimeErrorEntries != null && realtimeErrorEntries.Count > 0)
            {
                s.RealtimeCollectedCount = realtimeErrorEntries.Count;
                for (int i = 0; i < realtimeErrorEntries.Count; i++)
                {
                    if (!loggedErrors.Contains(realtimeErrorEntries[i].Message))
                    {
                        s.Entries.Add(realtimeErrorEntries[i]);
                        if (!s.CategoryCounts.ContainsKey(realtimeErrorEntries[i].Category)) s.CategoryCounts[realtimeErrorEntries[i].Category] = 0;
                        s.CategoryCounts[realtimeErrorEntries[i].Category]++;
                        if (realtimeErrorEntries[i].RelatedToDialogueText) s.DialogueRelatedCount++;
                    }
                }
            }
            for (int i = 0; i < s.Entries.Count; i++)
            {
                var msg = s.Entries[i].Message;
                if (!s.ErrorPatternCounts.ContainsKey(msg)) s.ErrorPatternCounts[msg] = 0;
                s.ErrorPatternCounts[msg]++;
            }
            s.CollectedCount = s.Entries.Count;
        }
        catch { }
        return s;
    }

    private static ComponentTypeSnapshot CollectComponentTypeSnapshot(Transform dt)
    {
        var s = new ComponentTypeSnapshot();
        if (dt == null) return s;
        try
        {
            var textComp = dt.GetComponent<Text>();
            s.HasTextComponent = textComp != null;
            s.HasGraphicComponent = dt.GetComponent<Graphic>() != null;
            try
            {
                Type tmpTextType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("TMPro.TMP_Text");
                    if (t != null) { tmpTextType = t; break; }
                }
                if (tmpTextType != null)
                {
                    var tmpComp = dt.GetComponent(tmpTextType);
                    s.HasTMPTextComponent = tmpComp != null;
                }
            }
            catch { }
            if (s.HasTextComponent && !s.HasTMPTextComponent) s.RuntimeComponentType = "RevealableUIText";
            else if (s.HasTMPTextComponent) s.RuntimeComponentType = "RevealableTMProText";
            else s.RuntimeComponentType = "Unknown";
            s.ConsistentWithPrefab = s.HasTextComponent;
            var graphic = dt.GetComponent<Graphic>();
            if (graphic != null && graphic.material != null && graphic.material.shader != null)
            {
                string shaderName = graphic.material.shader.name;
                if (s.RuntimeComponentType == "RevealableUIText") s.ShaderMatched = shaderName == "Naninovel/RevealableText";
                else if (s.RuntimeComponentType == "RevealableTMProText") s.ShaderMatched = shaderName == "Naninovel/RevealableTMProText";
                else s.ShaderMatched = false;
            }
            s.ConsistentWithConfig = s.RuntimeComponentType == "RevealableUIText";
        }
        catch { }
        return s;
    }

    private static TextPropertySnapshot CollectTextPropertySnapshot(Text textComp)
    {
        var s = new TextPropertySnapshot();
        if (textComp == null) return s;
        s.Text = textComp.text;
        s.TextLength = textComp.text != null ? textComp.text.Length : 0;
        s.TextIsNullOrEmpty = string.IsNullOrEmpty(textComp.text);
        s.IsPlaceholderText = textComp.text == "New Text" || textComp.text == "";

        s.CharacterCount = s.TextLength;
        s.CharacterCountVisible = s.TextIsNullOrEmpty ? 0 : s.TextLength;
        return s;
    }

    private static FontTexturePixelSnapshot CollectFontTexturePixelSnapshot(Text textComp, Transform dt)
    {
        var s = new FontTexturePixelSnapshot();
        if (textComp == null || textComp.font == null) return s;
        s.IsDynamicFont = textComp.font.dynamic;
        if (textComp.font.material != null && textComp.font.material.mainTexture != null)
        {
            var tex = textComp.font.material.mainTexture as Texture2D;
            s.FontMaterialMainTexName = textComp.font.material.mainTexture.name;
            if (tex != null)
            {
                s.FontMaterialMainTexWidth = tex.width;
                s.FontMaterialMainTexHeight = tex.height;
                s.FontMaterialMainTexFormat = (int)tex.format;
                try
                {
                    int sampleW = System.Math.Min(4, tex.width);
                    int sampleH = System.Math.Min(4, tex.height);
                    var pixels = tex.GetPixels(0, 0, sampleW, sampleH);
                    s.SampledPixelCount = pixels.Length;
                    int transparent = 0, white = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        if (pixels[i].a < 0.01f) transparent++;
                        if (pixels[i].r > 0.99f && pixels[i].g > 0.99f && pixels[i].b > 0.99f && pixels[i].a > 0.99f) white++;
                    }
                    s.AllTransparentRatio = (float)transparent / pixels.Length;
                    s.AllWhiteRatio = (float)white / pixels.Length;
                    s.IsTextureValid = s.AllTransparentRatio < 0.99f && s.AllWhiteRatio < 0.99f;
                }
                catch { s.IsTextureValid = false; }
            }
        }
        else
        {
            s.FontMaterialMainTexIsNull = true;
        }
        try
        {
            var cr = dt.GetComponent<CanvasRenderer>();
            if (cr != null)
            {
                var crMat = cr.GetMaterial(0);
                if (crMat != null && crMat.mainTexture != null)
                    s.CanvasRendererMainTexName = crMat.mainTexture.name;
            }
        }
        catch { }
        return s;
    }

    // === TEXT VISIBILITY ROOT CAUSE LOCALIZATION ===

    private static RootCauseResult LocateTextVisibilityRootCause(DialogueTextVisibilitySnapshot snap, BaselineSnapshot b)
    {
        var result = new RootCauseResult();
        result.PrimaryCause = RootCauseType.Unknown;
        result.SecondaryCauses = new List<RootCauseType>();
        result.IsDataComplete = snap != null && snap.IsValid;
        result.SupportingData = "";
        result.ExclusionBasis = "";
        if (!result.IsDataComplete) { result.SupportingData = "Incomplete data"; return result; }

        bool mainTexNotSet = (snap.MaterialMainTex.MainTexIsNull || (snap.MaterialMainTex.MainTexWidth == 1 && snap.MaterialMainTex.MainTexHeight == 1)) && !snap.MaterialMainTex.FontMaterialMainTexIsNull;
        bool mainTexMismatch = !snap.MaterialMainTex.MainTexIsNull && !snap.MaterialMainTex.FontMaterialMainTexIsNull && snap.MaterialMainTex.MainTexName != snap.MaterialMainTex.FontMaterialMainTexName;
        bool fontTexMissing = snap.MaterialMainTex.FontMaterialMainTexIsNull || (!snap.FontTexturePixel.IsTextureValid && !snap.MaterialMainTex.SameRef);
        bool shaderNotEffective = !snap.Shader.ShaderFindResult || !snap.Shader.IsSupported || !snap.Shader.SourceContainsDegenerateSkip;
        bool fontMatConfigErr = snap.FontMaterialConfig.FontIsNull || (snap.FontMaterialConfig.MaterialShaderName != "Naninovel/RevealableText" && snap.FontMaterialConfig.MaterialShaderName != "null" && snap.FontMaterialConfig.MaterialShaderName != "");
        bool clipRectZero = snap.ClipRectUpdate.IsCharClipRectZero && snap.ClipRectUpdate.IsLineClipRectZero;
        bool clipRectUpdateDefect = snap.ClipRectUpdate.TextRevealBehaviourExists && !snap.ClipRectUpdate.ClipRectChangedSinceLastFrame && snap.ClipRectUpdate.IsCharClipRectZero;
        bool naniInitFail = !snap.NaninovelInit.EngineInitialized || !snap.NaninovelInit.ActivePrinterNotNull;
        bool textNotSet = snap.TextProp.TextIsNullOrEmpty || snap.TextProp.CharacterCountVisible == 0;

        bool blackText = snap.TextColor.IsBlack && snap.TextColor.HasBaseline && (snap.TextColor.BaselineColor.r > 0.5f || snap.TextColor.BaselineColor.g > 0.5f || snap.TextColor.BaselineColor.b > 0.5f);
        bool alphaTooLow = snap.TextColor.IsAlphaTooLow && snap.TextColor.HasBaseline && snap.TextColor.BaselineColor.a > 0.5f;
        bool temporalClear = snap.TemporalTrace.TextClearedAfterContent || snap.TemporalTrace.AlphaDroppedToZero;
        bool renderOccluded = snap.RenderOcclusion.IsOccluded;
        bool printerAnomaly = snap.PrinterBehavior.ReflectionSucceeded && (!snap.PrinterBehavior.ActivePrinterActiveInHierarchy || snap.PrinterBehavior.ClearResetInferred);

        if (mainTexNotSet)
        {
            result.PrimaryCause = RootCauseType.MaterialMainTexNotSet;
            result.SupportingData = "_MainTex=null or 1x1, font.material.mainTexture=" + (snap.MaterialMainTex.FontMaterialMainTexIsNull ? "null" : snap.MaterialMainTex.FontMaterialMainTexName);
        }
        else if (mainTexMismatch)
        {
            result.PrimaryCause = RootCauseType.MainTexMismatchWithFontTexture;
            result.SupportingData = "_MainTex=\"" + snap.MaterialMainTex.MainTexName + "\" fontTex=\"" + snap.MaterialMainTex.FontMaterialMainTexName + "\"";
        }
        else if (blackText)
        {
            result.PrimaryCause = RootCauseType.BlackText;
            result.SupportingData = "textComp.color=(" + snap.TextColor.TextCompColor.r.ToString("F2") + "," + snap.TextColor.TextCompColor.g.ToString("F2") + "," + snap.TextColor.TextCompColor.b.ToString("F2") + "," + snap.TextColor.TextCompColor.a.ToString("F2") + ") baseline=(" + snap.TextColor.BaselineColor.r.ToString("F2") + "," + snap.TextColor.BaselineColor.g.ToString("F2") + "," + snap.TextColor.BaselineColor.b.ToString("F2") + ")";
        }
        else if (alphaTooLow)
        {
            result.PrimaryCause = RootCauseType.TextAlphaTooLow;
            result.SupportingData = "textComp.color.a=" + snap.TextColor.TextCompColor.a.ToString("F3") + " materialAlpha=" + snap.TextColor.MaterialColor.a.ToString("F3") + " baselineAlpha=" + snap.TextColor.BaselineColor.a.ToString("F2");
        }
        else if (temporalClear)
        {
            result.PrimaryCause = RootCauseType.TextTemporalClear;
            result.SupportingData = "alphaDropped=" + snap.TemporalTrace.AlphaDroppedToZero + " textCleared=" + snap.TemporalTrace.TextClearedAfterContent + " prevAlpha=" + snap.TemporalTrace.PrevAlpha.ToString("F2") + " curAlpha=" + snap.TemporalTrace.CurrentAlpha.ToString("F2") + " prevLen=" + (snap.TemporalTrace.PrevText != null ? snap.TemporalTrace.PrevText.Length : 0) + " curLen=" + snap.TemporalTrace.CurrentTextLength;
        }
        else if (renderOccluded)
        {
            result.PrimaryCause = RootCauseType.RenderOcclusion;
            result.SupportingData = "occluded by \"" + snap.RenderOcclusion.OccludingElementName + "\" occlOrder=" + snap.RenderOcclusion.OccludingSortingOrder + " myOrder=" + snap.RenderOcclusion.SortingOrder + " sibIdx=" + snap.RenderOcclusion.SiblingIndex;
        }
        else if (printerAnomaly)
        {
            result.PrimaryCause = RootCauseType.PrinterBehaviorAnomaly;
            result.SupportingData = "printerType=\"" + snap.PrinterBehavior.ActivePrinterTypeName + "\" active=" + snap.PrinterBehavior.ActivePrinterActiveInHierarchy + " isPrinting=" + snap.PrinterBehavior.IsPrinting + " clearReset=" + snap.PrinterBehavior.ClearResetInferred;
        }
        else if (naniInitFail)
        {
            result.PrimaryCause = RootCauseType.NaninovelInitFailure;
            result.SupportingData = "engineInit=" + snap.NaninovelInit.EngineInitialized + " activePrinter=" + snap.NaninovelInit.ActivePrinterNotNull;
        }
        else if (snap.TextContentTrace.ContainsPlaceholder && snap.NaninovelInit.EngineInitialized)
        {
            result.PrimaryCause = RootCauseType.PlaceholderNotReplaced;
            result.SupportingData = "textComp.text contains " + snap.TextContentTrace.PlaceholderType + " placeholder, CustomVariables service active=" + snap.TextContentTrace.CustomVariablesServiceActive + ", ScriptPlayer active=" + snap.TextContentTrace.ScriptPlayerActive;
        }
        else if (snap.ShadowDiag.IsShadowBlack && snap.NaninovelInit.EngineInitialized)
        {
            result.PrimaryCause = RootCauseType.ShadowEffectBlack;
            result.SupportingData = "shadowBlack=" + snap.ShadowDiag.IsShadowBlack + " textHasVertices=" + snap.ShadowDiag.TextHasVertices + " engineInit=" + snap.NaninovelInit.EngineInitialized;
        }

        else if (fontTexMissing)
        {
            result.PrimaryCause = RootCauseType.FontTextureMissing;
            result.SupportingData = "fontMatMainTexNull=" + snap.FontTexturePixel.FontMaterialMainTexIsNull + " valid=" + snap.FontTexturePixel.IsTextureValid + " transparentRatio=" + snap.FontTexturePixel.AllTransparentRatio.ToString("F2");
        }
        else if (shaderNotEffective)
        {
            result.PrimaryCause = RootCauseType.ShaderModificationNotEffective;
            result.SupportingData = "shaderFind=" + snap.Shader.ShaderFindResult + " supported=" + snap.Shader.IsSupported + " degenSkip=" + snap.Shader.SourceContainsDegenerateSkip;
        }
        else if (fontMatConfigErr)
        {
            result.PrimaryCause = RootCauseType.FontMaterialConfigError;
            result.SupportingData = "fontNull=" + snap.FontMaterialConfig.FontIsNull + " matShader=" + snap.FontMaterialConfig.MaterialShaderName;
        }
        else if (clipRectZero)
        {
            result.PrimaryCause = RootCauseType.ClipRectZeroVector;
            result.SupportingData = "charClipRect=(" + snap.ClipRectUpdate.CharClipRect.x + "," + snap.ClipRectUpdate.CharClipRect.y + "," + snap.ClipRectUpdate.CharClipRect.z + "," + snap.ClipRectUpdate.CharClipRect.w + ")";
        }
        else if (clipRectUpdateDefect)
        {
            result.PrimaryCause = RootCauseType.ClipRectUpdateLogicDefect;
            result.SupportingData = "trbExists=" + snap.ClipRectUpdate.TextRevealBehaviourExists + " changed=" + snap.ClipRectUpdate.ClipRectChangedSinceLastFrame + " charZero=" + snap.ClipRectUpdate.IsCharClipRectZero;
        }

        else if (textNotSet)
        {
            result.PrimaryCause = RootCauseType.TextPropertyNotSet;
            result.SupportingData = "textNull=" + snap.TextProp.TextIsNullOrEmpty + " charVisible=" + snap.TextProp.CharacterCountVisible + " text=\"" + snap.TextProp.Text + "\"";
        }
        else
        {
            result.PrimaryCause = RootCauseType.UnknownTextVisibilityCause;
            result.SupportingData = "All 8 checks passed but text not visible. Suggest: check CanvasRenderer material array, RenderTexture, camera clearFlags, Canvas overrideSorting, GraphicRaycaster, font character set.";
        }

        if (fontTexMissing && result.PrimaryCause != RootCauseType.FontTextureMissing) result.SecondaryCauses.Add(RootCauseType.FontTextureMissing);
        if (clipRectZero && result.PrimaryCause != RootCauseType.ClipRectZeroVector) result.SecondaryCauses.Add(RootCauseType.ClipRectZeroVector);
        if (naniInitFail && result.PrimaryCause != RootCauseType.NaninovelInitFailure) result.SecondaryCauses.Add(RootCauseType.NaninovelInitFailure);
        if (textNotSet && result.PrimaryCause != RootCauseType.TextPropertyNotSet) result.SecondaryCauses.Add(RootCauseType.TextPropertyNotSet);

        result.ResourcePathMismatch = !snap.ResourcePath.PathMatched;
        result.TMProShaderThresholdMissing = !snap.TMProShader.Has60000Threshold && snap.TMProShader.ShaderFindResult;
        result.EngineInitFailure = !snap.NaninovelInit.EngineInitialized || !snap.NaninovelInit.UITextPrinterInstantiated;
        result.ComponentTypeMismatch = !snap.ComponentType.ConsistentWithConfig || !snap.ComponentType.ShaderMatched;

        result.ExclusionBasis = "Checked: blackText, alphaLow, temporalClear, occlusion, printer, mainTex, fontTex, shader, fontMat, clipRect, clipRectUpdate, naniInit, textProp, resourcePath, tmproThreshold, engineInit, componentType";
        return result;
    }

    // === TEXT VISIBILITY FIX METHODS ===

    private static FixChangeRecord FixBlackText(Transform dt, BaselineSnapshot b)
    {
        if (dt == null) return null;
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "color";
        try
        {
            var textComp = dt.GetComponent<Text>();
            if (textComp == null) return null;
            rec.OldValue = "(" + textComp.color.r.ToString("F2") + "," + textComp.color.g.ToString("F2") + "," + textComp.color.b.ToString("F2") + "," + textComp.color.a.ToString("F2") + ")";
            if (textComp.color.r > 0.99f && textComp.color.g > 0.99f && textComp.color.b > 0.99f) return null;
            textComp.color = new Color(1f, 1f, 1f, textComp.color.a);
            rec.NewValue = "(1.00,1.00,1.00," + textComp.color.a.ToString("F2") + ")";
            rec.Summary = "Set text color to white";
            var graphic = dt.GetComponent<Graphic>();
            if (graphic != null && graphic.material != null && graphic.material.HasProperty("_Color"))
            {
                var matColor = graphic.material.GetColor("_Color");
                if (matColor.r < 0.01f && matColor.g < 0.01f && matColor.b < 0.01f)
                    graphic.material.SetColor("_Color", new Color(1f, 1f, 1f, matColor.a));
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixBlackText: " + ex.Message); return null; }
        return rec;
    }

    private static FixChangeRecord FixAlphaTooLow(Transform dt, BaselineSnapshot b)
    {
        if (dt == null) return null;
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "color.a";
        try
        {
            var textComp = dt.GetComponent<Text>();
            if (textComp == null) return null;
            rec.OldValue = textComp.color.a.ToString("F3");
            if (textComp.color.a >= 0.99f) return null;
            textComp.color = new Color(textComp.color.r, textComp.color.g, textComp.color.b, 1f);
            rec.NewValue = "1.000";
            rec.Summary = "Set text alpha to 1";
            var graphic = dt.GetComponent<Graphic>();
            if (graphic != null && graphic.material != null && graphic.material.HasProperty("_Color"))
            {
                var matColor = graphic.material.GetColor("_Color");
                if (matColor.a < 0.99f)
                    graphic.material.SetColor("_Color", new Color(matColor.r, matColor.g, matColor.b, 1f));
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixAlphaTooLow: " + ex.Message); return null; }
        return rec;
    }

    private static FixChangeRecord FixTemporalClear(Transform dt, TemporalTraceSnapshot trace)
    {
        if (dt == null) return null;
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "text";
        try
        {
            var textComp = dt.GetComponent<Text>();
            if (textComp == null) return null;
            rec.OldValue = "\"" + (textComp.text != null ? (textComp.text.Length > 20 ? textComp.text.Substring(0, 20) + "..." : textComp.text) : "null") + "\"";
            if (trace.WasInitialized && !string.IsNullOrEmpty(trace.PrevText) && string.IsNullOrEmpty(textComp.text))
            {
                textComp.text = trace.PrevText;
                rec.NewValue = "\"" + (trace.PrevText.Length > 20 ? trace.PrevText.Substring(0, 20) + "..." : trace.PrevText) + "\"";
                rec.Summary = "Restored text from previous frame";
            }
            else
            {
                var c = textComp.color;
                if (c.a < 0.01f)
                {
                    textComp.color = new Color(c.r, c.g, c.b, 1f);
                    rec.NewValue = "alpha=1";
                    rec.Summary = "Restored alpha from 0 to 1";
                }
                else return null;
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixTemporalClear: " + ex.Message); return null; }
        return rec;
    }

    private static FixChangeRecord FixRenderOcclusion(Transform dt, RenderOcclusionSnapshot occlusion)
    {
        if (dt == null) return null;
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "siblingIndex";
        try
        {
            rec.OldValue = dt.GetSiblingIndex().ToString();
            if (!occlusion.IsOccluded) return null;
            dt.SetAsLastSibling();
            rec.NewValue = dt.GetSiblingIndex().ToString();
            rec.Summary = "Moved DialogueText to last sibling to avoid occlusion by \"" + occlusion.OccludingElementName + "\"";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixRenderOcclusion: " + ex.Message); return null; }
        return rec;
    }

    private static FixChangeRecord FixPrinterBehaviorAnomaly(Transform dt, PrinterBehaviorSnapshot printer, BaselineSnapshot b)
    {
        if (dt == null) return null;
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "printerBehavior";
        try
        {
            var textComp = dt.GetComponent<Text>();
            if (textComp == null) return null;
            var c = textComp.color;
            if (c.a < 0.01f)
            {
                textComp.color = new Color(c.r, c.g, c.b, 1f);
                rec.OldValue = "alpha=" + c.a.ToString("F2");
                rec.NewValue = "alpha=1";
                rec.Summary = "Printer anomaly: restored alpha to 1";
            }
            else if (c.r < 0.01f && c.g < 0.01f && c.b < 0.01f)
            {
                textComp.color = new Color(1f, 1f, 1f, c.a);
                rec.OldValue = "color=black";
                rec.NewValue = "color=white";
                rec.Summary = "Printer anomaly: set color to white";
            }
            else return null;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixPrinterBehaviorAnomaly: " + ex.Message); return null; }
        return rec;
    }

    private static FixChangeRecord FixMaterialMainTexNotSet(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "_MainTex";
        rec.OldValue = "null";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null) return null;
            var mat = graphic.material;
            if (mat == null) return null;
            var textComp = graphic as Text;
            if (textComp != null && textComp.font != null && textComp.font.material != null && textComp.font.material.mainTexture != null)
            {
                mat.SetTexture("_MainTex", textComp.font.material.mainTexture);
                rec.NewValue = textComp.font.material.mainTexture.name;
            }
            else
            {
                if (textComp != null && textComp.font != null)
                {
                    textComp.font.RequestCharactersInTexture(textComp.text, textComp.fontSize, textComp.fontStyle);
                    if (textComp.font.material != null && textComp.font.material.mainTexture != null)
                    {
                        mat.SetTexture("_MainTex", textComp.font.material.mainTexture);
                        rec.NewValue = textComp.font.material.mainTexture.name + " (after RequestCharacters)";
                    }
                    else
                    {
                        Debug.LogWarning("[TEXTVIS] FixMaterialMainTexNotSet: font.texture unavailable after RequestCharactersInTexture, no fallback created");
                        rec.NewValue = "font.texture unavailable, no fallback created";
                    }
                }
                else
                {
                    Debug.LogWarning("[TEXTVIS] FixMaterialMainTexNotSet: textComp or font is null, no fallback created");
                    rec.NewValue = "font.texture unavailable, no fallback created";
                }
            }
            rec.Summary = "Set _MainTex from font.material.mainTexture";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixMaterialMainTexNotSet: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixMaterialMainTexFromFont(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "_MainTex";
        rec.OldValue = "null or mismatched";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null) return null;
            var mat = graphic.material;
            if (mat == null) return null;
            var textComp = graphic as Text;
            if (textComp != null && textComp.font != null && textComp.font.material != null && textComp.font.material.mainTexture != null)
            {
                mat.SetTexture("_MainTex", textComp.font.material.mainTexture);
                rec.NewValue = textComp.font.material.mainTexture.name;
                rec.Summary = "Set _MainTex from font.material.mainTexture";
            }
            else if (textComp != null && textComp.font != null)
            {
                textComp.font.RequestCharactersInTexture(textComp.text, textComp.fontSize, textComp.fontStyle);
                if (textComp.font.material != null && textComp.font.material.mainTexture != null)
                {
                    mat.SetTexture("_MainTex", textComp.font.material.mainTexture);
                    rec.NewValue = textComp.font.material.mainTexture.name + " (after RequestCharacters)";
                    rec.Summary = "Set _MainTex after RequestCharactersInTexture";
                }
                else
                {
                    Debug.LogWarning("[TEXTVIS] FixMaterialMainTexFromFont: font.texture unavailable after RequestCharactersInTexture, no fallback created");
                    rec.NewValue = "font.texture unavailable, no fallback created";
                }
            }
            else
            {
                Debug.LogWarning("[TEXTVIS] FixMaterialMainTexFromFont: textComp or font is null, no fallback created");
                rec.NewValue = "font.texture unavailable, no fallback created";
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixMaterialMainTexFromFont: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixColorBlackOnly(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "color";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null) return null;
            var c = graphic.color;
            rec.OldValue = "(" + c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2") + "," + c.a.ToString("F2") + ")";
            if (c.r < 0.01f && c.g < 0.01f && c.b < 0.01f)
            {
                c.r = 1f; c.g = 1f; c.b = 1f; c.a = 1f;
                graphic.color = c;
                rec.NewValue = "white (was black)";
                rec.Summary = "Corrected black color to white";
            }
            else if (c.a < 0.01f)
            {
                c.a = 1f;
                graphic.color = c;
                rec.NewValue = "(" + c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2") + ",1) alpha restored";
                rec.Summary = "Restored alpha only, preserved RGB";
            }
            else
            {
                rec.NewValue = "unchanged (non-black color preserved)";
                rec.Summary = "Color is not black, no correction needed";
            }
            var mat = graphic.material;
            if (mat != null)
            {
                var matColor = mat.GetColor("_Color");
                if (matColor.r < 0.01f && matColor.g < 0.01f && matColor.b < 0.01f)
                {
                    mat.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixColorBlackOnly: " + ex.Message); }
        return rec;
    }

    private static Type FindScriptPlayerType()
    {
        if (scriptPlayerType != null) return scriptPlayerType;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.FullName == "Naninovel.ScriptPlayer" || t.FullName == "Elringus.Naninovel.ScriptPlayer")
                    {
                        scriptPlayerType = t;
                        return t;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static MethodInfo GetScriptPlayerExecMethod(Type spType)
    {
        if (spType == null) return null;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try
        {
            var mi = spType.GetMethod("Play", flags);
            if (mi != null) return mi;
            mi = spType.GetMethod("StartScript", flags);
            if (mi != null) return mi;
            mi = spType.GetMethod("Execute", flags);
            if (mi != null) return mi;
        }
        catch { }
        Debug.LogWarning("[TEXTVIS] ScriptPlayer exec methods not found: Play/StartScript/Execute");
        return null;
    }

    private static MethodInfo EnumerateAndSelectScriptPlayerExecMethod(Type spType)
    {
        try
        {
            if (scriptPlayerMethodsEnumerated) return cachedScriptPlayerExecMethod;
            if (spType == null)
            {
                Debug.LogWarning("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: spType is null, skip");
                scriptPlayerMethodsEnumerated = true;
                return null;
            }
            MethodInfo[] allMethods = spType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            cachedScriptPlayerMethods = allMethods;
            string methodNamesLog = "";
            for (int i = 0; i < allMethods.Length; i++)
            {
                if (methodNamesLog.Length > 0) methodNamesLog += ", ";
                methodNamesLog += allMethods[i].Name;
                if (methodNamesLog.Length > 2000) { methodNamesLog += "...(truncated)"; break; }
            }
            Debug.Log("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: ScriptPlayer all public methods: " + methodNamesLog);
            MethodInfo priority1 = null;
            MethodInfo priority2 = null;
            MethodInfo priority3 = null;
            MethodInfo priority4 = null;
            ParameterInfo[] priority1Params = null;
            ParameterInfo[] priority2Params = null;
            ParameterInfo[] priority3Params = null;
            ParameterInfo[] priority4Params = null;
            for (int i = 0; i < allMethods.Length; i++)
            {
                string methodName = allMethods[i].Name;
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_") || methodName.StartsWith("add_") || methodName.StartsWith("remove_")) continue;
                ParameterInfo[] mParams = allMethods[i].GetParameters();
                bool nameMatchPlay = methodName.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchRun = methodName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchStart = methodName.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchExec = methodName.IndexOf("Exec", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchLoad = methodName.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchBegin = methodName.IndexOf("Begin", StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatchPreload = methodName.IndexOf("Preload", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isPlayRunStart = nameMatchPlay || nameMatchRun || nameMatchStart;
                bool isExecBegin = nameMatchExec || nameMatchBegin;
                bool isLoadPreload = nameMatchLoad || nameMatchPreload;
                if (mParams.Length == 0 && isPlayRunStart && priority1 == null)
                {
                    priority1 = allMethods[i];
                    priority1Params = mParams;
                }
                else if (mParams.Length == 1 && mParams[0].ParameterType == typeof(string) && (isLoadPreload || isPlayRunStart) && priority2 == null)
                {
                    priority2 = allMethods[i];
                    priority2Params = mParams;
                }
                else if (mParams.Length == 1 && (isLoadPreload || isPlayRunStart) && priority3 == null)
                {
                    priority3 = allMethods[i];
                    priority3Params = mParams;
                }
                else if (mParams.Length == 0 && isExecBegin && priority4 == null)
                {
                    priority4 = allMethods[i];
                    priority4Params = mParams;
                }
            }
            if (priority1 != null)
            {
                cachedScriptPlayerExecMethod = priority1;
                cachedScriptPlayerExecMethodParams = priority1Params;
                scriptPlayerMethodsEnumerated = true;
                Debug.Log("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: selected priority1 no-param Play/Run/Start method: " + priority1.Name);
                return priority1;
            }
            if (priority2 != null)
            {
                cachedScriptPlayerExecMethod = priority2;
                cachedScriptPlayerExecMethodParams = priority2Params;
                scriptPlayerMethodsEnumerated = true;
                Debug.Log("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: selected priority2 string-param Load/Preload method: " + priority2.Name);
                return priority2;
            }
            if (priority3 != null)
            {
                cachedScriptPlayerExecMethod = priority3;
                cachedScriptPlayerExecMethodParams = priority3Params;
                scriptPlayerMethodsEnumerated = true;
                Debug.Log("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: selected priority3 other-param Load/Preload method: " + priority3.Name);
                return priority3;
            }
            if (priority4 != null)
            {
                cachedScriptPlayerExecMethod = priority4;
                cachedScriptPlayerExecMethodParams = priority4Params;
                scriptPlayerMethodsEnumerated = true;
                Debug.Log("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: selected priority4 no-param Execute/Begin method: " + priority4.Name);
                return priority4;
            }
            scriptPlayerMethodsEnumerated = true;
            Debug.LogWarning("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod: no candidate exec method found among " + allMethods.Length + " methods");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TEXTVIS] EnumerateAndSelectScriptPlayerExecMethod exception: " + ex.Message);
            scriptPlayerMethodsEnumerated = true;
        }
        return null;
    }

    private static object GetScriptPlayerInstance(Type spType)
    {
        try
        {
            if (spType == null) return null;
            var instanceProp = spType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp != null)
            {
                object inst = instanceProp.GetValue(null, null);
                if (inst != null) { Debug.Log("[TEXTVIS] GetScriptPlayerInstance: got via Instance property"); return inst; }
            }
            var instanceField = spType.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceField != null)
            {
                object inst2 = instanceField.GetValue(null);
                if (inst2 != null) { Debug.Log("[TEXTVIS] GetScriptPlayerInstance: got via instance field"); return inst2; }
            }
            Type engineType2 = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int ai = 0; ai < assemblies.Length; ai++)
            {
                try
                {
                    Type[] types = assemblies[ai].GetTypes();
                    for (int tj = 0; tj < types.Length; tj++)
                    {
                        string tName = types[tj].Name;
                        string tFullName = types[tj].FullName != null ? types[tj].FullName : "";
                        if (tName == "Engine" && (tFullName.Contains("Naninovel") || tFullName.Contains("Elringus")))
                        {
                            engineType2 = types[tj];
                            break;
                        }
                    }
                }
                catch { }
                if (engineType2 != null) break;
            }
            if (engineType2 != null)
            {
                Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine type found: " + engineType2.FullName);
                object engineInstance = null;
                var engineInstanceProp = engineType2.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (engineInstanceProp != null)
                {
                    engineInstance = engineInstanceProp.GetValue(null, null);
                }
                if (engineInstance == null)
                {
                    var engineInstanceField = engineType2.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (engineInstanceField != null) engineInstance = engineInstanceField.GetValue(null);
                }
                if (engineInstance == null)
                {
                    string[] enginePropNames = new string[] { "Instance", "Active", "Current", "EngineInstance", "instance" };
                    for (int epn = 0; epn < enginePropNames.Length; epn++)
                    {
                        var altProp = engineType2.GetProperty(enginePropNames[epn], BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                        if (altProp != null)
                        {
                            try
                            {
                                object altInst = altProp.GetValue(null, null);
                                if (altInst != null)
                                {
                                    engineInstance = altInst;
                                    Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine instance found via " + enginePropNames[epn] + " property");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                if (engineInstance == null)
                {
                    string[] engineFieldNames = new string[] { "instance", "_instance", "engine", "_engine", "current", "_current" };
                    for (int efn = 0; efn < engineFieldNames.Length; efn++)
                    {
                        var altField = engineType2.GetField(engineFieldNames[efn], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (altField != null)
                        {
                            try
                            {
                                object altInst2 = altField.GetValue(null);
                                if (altInst2 != null)
                                {
                                    engineInstance = altInst2;
                                    Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine instance found via " + engineFieldNames[efn] + " field");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                if (engineInstance == null)
                {
                    try
                    {
                        if (typeof(UnityEngine.Object).IsAssignableFrom(engineType2))
                        {
                            var engineObjects2 = UnityEngine.Object.FindObjectsOfType(engineType2);
                            if (engineObjects2 != null && engineObjects2.Length > 0)
                            {
                                engineInstance = engineObjects2[0];
                                Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine instance found via FindObjectsOfType(engineType)");
                            }
                        }
                    }
                    catch { }
                }
                if (engineInstance == null)
                {
                    var engineObjects = UnityEngine.Object.FindObjectsOfType(typeof(Component));
                    for (int eoi = 0; eoi < engineObjects.Length; eoi++)
                    {
                        if (engineObjects[eoi] != null && engineObjects[eoi].GetType() == engineType2)
                        {
                            engineInstance = engineObjects[eoi];
                            break;
                        }
                    }
                }
                if (engineInstance != null)
                {
                    Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine instance obtained, type=" + engineInstance.GetType().Name);
                    MethodInfo[] engineMethods = engineType2.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int emi = 0; emi < engineMethods.Length; emi++)
                    {
                        if (engineMethods[emi].Name == "GetService")
                        {
                            ParameterInfo[] emParams = engineMethods[emi].GetParameters();
                            if (emParams.Length == 1 && emParams[0].ParameterType == typeof(Type))
                            {
                                try
                                {
                                    object spInstance = engineMethods[emi].Invoke(engineInstance, new object[] { spType });
                                    if (spInstance != null)
                                    {
                                        Debug.Log("[TEXTVIS] GetScriptPlayerInstance: got ScriptPlayer via Engine.GetService(Type) non-generic");
                                        return spInstance;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    for (int emi2 = 0; emi2 < engineMethods.Length; emi2++)
                    {
                        if (engineMethods[emi2].Name == "GetService" && engineMethods[emi2].IsGenericMethod)
                        {
                            try
                            {
                                MethodInfo genericGetService = engineMethods[emi2].MakeGenericMethod(spType);
                                object spInstance2 = genericGetService.Invoke(engineInstance, null);
                                if (spInstance2 != null)
                                {
                                    Debug.Log("[TEXTVIS] GetScriptPlayerInstance: got ScriptPlayer via Engine.GetService<T>() generic");
                                    return spInstance2;
                                }
                            }
                            catch { }
                        }
                    }
                    var servicesProp = engineType2.GetProperty("Services", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (servicesProp != null)
                    {
                        try
                        {
                            object servicesObj = servicesProp.GetValue(engineInstance, null);
                            if (servicesObj != null)
                            {
                                Debug.Log("[TEXTVIS] GetScriptPlayerInstance: Engine.Services property found, type=" + servicesObj.GetType().Name);
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    Debug.LogWarning("[TEXTVIS] GetScriptPlayerInstance: Engine instance not found");
                }
            }
            else
            {
                Debug.LogWarning("[TEXTVIS] GetScriptPlayerInstance: Engine type not found in any assembly");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TEXTVIS] GetScriptPlayerInstance exception: " + ex.Message);
        }
        Debug.LogWarning("[TEXTVIS] GetScriptPlayerInstance: ScriptPlayer instance not found");
        return null;
    }

    private static string GetScriptResourcePath()
    {
        try
        {
            var assets = Resources.LoadAll("naninovel/scripts", typeof(TextAsset));
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].name.Contains(".nani")) return assets[i].name;
            }
            return "naninovel/scripts/main";
        }
        catch { return null; }
    }

    private static bool InvokeScriptPlayerExec(MethodInfo execMethod, string scriptPath)
    {
        if (execMethod == null || string.IsNullOrEmpty(scriptPath)) return false;
        try
        {
            object target = null;
            if (!execMethod.IsStatic)
            {
                target = GetScriptPlayerInstance(scriptPlayerType);
                if (target == null) { Debug.LogWarning("[TEXTVIS] InvokeScriptPlayerExec: ScriptPlayer instance not found via GetScriptPlayerInstance"); return false; }
            }
            var parms = execMethod.GetParameters();
            object[] args = null;
            if (parms.Length > 0)
            {
                args = new object[parms.Length];
                for (int pi = 0; pi < parms.Length; pi++)
                {
                    Type paramType = parms[pi].ParameterType;
                    if (paramType == typeof(string))
                    {
                        args[pi] = scriptPath;
                    }
                    else if (paramType == typeof(bool))
                    {
                        args[pi] = true;
                    }
                    else if (paramType == typeof(int))
                    {
                        args[pi] = 0;
                    }
                    else if (paramType == typeof(float))
                    {
                        args[pi] = 0f;
                    }
                    else if (paramType.IsEnum || paramType.IsValueType)
                    {
                        args[pi] = Activator.CreateInstance(paramType);
                    }
                    else
                    {
                        args[pi] = null;
                    }
                }
            }
            execMethod.Invoke(target, args);
            Debug.Log("[TEXTVIS] InvokeScriptPlayerExec: invoked " + execMethod.Name + " with " + (parms.Length > 0 ? parms.Length + " params" : "no params") + ", scriptPath=" + scriptPath);
            return true;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] InvokeScriptPlayerExec failed: " + ex.Message); return false; }
    }

    private static Type FindCustomVariablesType()
    {
        if (customVariablesType != null) return customVariablesType;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.FullName == "Naninovel.CustomVariables" || t.FullName == "Elringus.Naninovel.CustomVariables")
                    {
                        customVariablesType = t;
                        return t;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static MethodInfo GetCustomVariablesInitMethod(Type cvType)
    {
        if (cvType == null) return null;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try
        {
            var mi = cvType.GetMethod("Initialize", flags);
            if (mi != null) return mi;
            mi = cvType.GetMethod("Start", flags);
            if (mi != null) return mi;
        }
        catch { }
        return null;
    }

    private static MethodInfo GetVariableReplaceMethod(Type cvType)
    {
        if (cvType == null) return null;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try
        {
            var mi = cvType.GetMethod("ReplaceVariables", flags);
            if (mi != null) return mi;
            mi = cvType.GetMethod("Substitute", flags);
            if (mi != null) return mi;
            mi = cvType.GetMethod("Evaluate", flags);
            if (mi != null) return mi;
        }
        catch { }
        return null;
    }

    private static FixChangeRecord FixPlaceholderNotReplaced(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "placeholder";
        rec.OldValue = "%0";
        try
        {
            var textComp = dt.GetComponent<Text>();
            if (textComp == null) return null;
            if (!textComp.text.Contains("%0")) return null;
            if (variableReplaceCalled) return null;
            var cvType = FindCustomVariablesType();
            if (cvType == null) { Debug.LogWarning("[TEXTVIS] CustomVariables type not found"); return null; }
            var initMethod = GetCustomVariablesInitMethod(cvType);
            var replaceMethod = GetVariableReplaceMethod(cvType);
            if (replaceMethod != null)
            {
                try
                {
                    object target = null;
                    if (!replaceMethod.IsStatic)
                    {
                        var instanceProp = cvType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceProp != null) target = instanceProp.GetValue(null, null);
                    }
                    var parms = replaceMethod.GetParameters();
                    object[] args = parms.Length > 0 ? new object[] { textComp.text } : null;
                    var result = replaceMethod.Invoke(target, args);
                    if (result is string) textComp.text = (string)result;
                }
                catch (Exception ex) { Debug.LogWarning("[TEXTVIS] Variable replace failed: " + ex.Message); }
            }
            variableReplaceCalled = true;
            textComp.SetVerticesDirty();
            rec.NewValue = textComp.text.Contains("%0") ? "%0 still present after replace" : "placeholder replaced: " + (textComp.text.Length > 30 ? textComp.text.Substring(0, 30) : textComp.text);
            rec.Summary = "Triggered CustomVariables variable replacement";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixPlaceholderNotReplaced: " + ex.Message); }
        return rec;
    }

    private static HasMovedRootCauseAnalysis AnalyzeHasMovedFalseRootCause(Transform dt, Text textComp, bool engineInitialized)
    {
        var result = new HasMovedRootCauseAnalysis();
        if (dt == null || textComp == null || !engineInitialized) return result;
        try
        {
            result.TextEmpty = string.IsNullOrEmpty(textComp.text);
            result.TextContent = textComp.text != null ? (textComp.text.Length > 50 ? textComp.text.Substring(0, 50) : textComp.text) : "";
            if (result.TextEmpty)
            {
                result.RootCauseSummary = "textComp.text empty, @print not executed";
            }
            else
            {
                result.RootCauseSummary = "textComp.text non-empty but hasMoved=false, RevealableUIText or ClipRect issue";
            }
            try
            {
                var components = dt.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null && components[i].GetType().Name.Contains("RevealableUIText"))
                    {
                        result.RevealableUITextFound = true;
                        var behProp = components[i].GetType().GetProperty("enabled");
                        if (behProp != null) result.RevealableUITextEnabled = (bool)behProp.GetValue(components[i], null);
                        var progressProp = components[i].GetType().GetProperty("RevealProgress");
                        if (progressProp != null) result.RevealProgress = (float)progressProp.GetValue(components[i], null);
                        break;
                    }
                }
                if (!result.RevealableUITextFound)
                {
                    for (int ci = 0; ci < dt.childCount; ci++)
                    {
                        var childComp = dt.GetChild(ci).GetComponents<Component>();
                        for (int cci = 0; cci < childComp.Length; cci++)
                        {
                            if (childComp[cci] != null && childComp[cci].GetType().Name.Contains("RevealableUIText"))
                            {
                                result.RevealableUITextFound = true;
                                break;
                            }
                        }
                        if (result.RevealableUITextFound) break;
                    }
                }
            }
            catch { }
            try
            {
                var crProp = textComp.GetType().GetProperty("clipRect");
                if (crProp != null)
                {
                    var cr = (Vector4)crProp.GetValue(textComp, null);
                    result.ClipRectValue = cr;
                    result.ClipRectIsZero = (cr.x == 0 && cr.y == 0 && cr.z == 0 && cr.w == 0);
                }
            }
            catch { }
            textComp.SetVerticesDirty();
            result.SetVerticesDirtyCalled = true;
            Debug.LogWarning("[TEXTVIS] AnalyzeHasMovedFalseRootCause: " + result.RootCauseSummary + " revealableFound=" + result.RevealableUITextFound + " clipRectZero=" + result.ClipRectIsZero);
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] AnalyzeHasMovedFalseRootCause: " + ex.Message); }
        return result;
    }

    private static void CheckAndCacheRebuildSignature(Type revealableType)
    {
        try
        {
            if (rebuildSignatureChecked) return;
            if (revealableType == null)
            {
                Debug.LogWarning("[TEXTVIS] CheckAndCacheRebuildSignature: revealableType is null, skip");
                return;
            }
            MethodInfo noParamMethod = null;
            try
            {
                noParamMethod = revealableType.GetMethod("Rebuild", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }
            catch (Exception exNoParam)
            {
                Debug.LogWarning("[TEXTVIS] CheckAndCacheRebuildSignature no-param lookup exception: " + exNoParam.Message);
            }
            if (noParamMethod != null)
            {
                cachedRebuildMethod = noParamMethod;
                cachedRebuildParamInfos = new ParameterInfo[0];
                rebuildSignatureChecked = true;
                Debug.Log("[TEXTVIS] CheckAndCacheRebuildSignature: no-param Rebuild overload found and cached");
                return;
            }
            MethodInfo[] allMethods = revealableType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo selectedMethod = null;
            int minParamCount = int.MaxValue;
            for (int i = 0; i < allMethods.Length; i++)
            {
                if (allMethods[i].Name == "Rebuild")
                {
                    ParameterInfo[] paramsInfo = allMethods[i].GetParameters();
                    if (paramsInfo.Length < minParamCount)
                    {
                        minParamCount = paramsInfo.Length;
                        selectedMethod = allMethods[i];
                    }
                }
            }
            if (selectedMethod != null)
            {
                cachedRebuildMethod = selectedMethod;
                cachedRebuildParamInfos = selectedMethod.GetParameters();
                rebuildSignatureChecked = true;
                Debug.Log("[TEXTVIS] CheckAndCacheRebuildSignature: Rebuild overload with " + cachedRebuildParamInfos.Length + " params selected and cached");
            }
            else
            {
                rebuildSignatureChecked = true;
                Debug.LogWarning("[TEXTVIS] CheckAndCacheRebuildSignature: Rebuild method not found in any overload");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TEXTVIS] CheckAndCacheRebuildSignature exception: " + ex.Message);
        }
    }

    private static FixChangeRecord FixRevealableUITextRevealProcess(Transform dt, Text textComp)
    {
        FixChangeRecord rec = new FixChangeRecord();
        rec.ObjectPath = dt != null ? dt.gameObject.name : "null";
        rec.PropertyName = "RevealableUITextRevealProcess";
        rec.OldValue = "hasMoved=false";
        try
        {
            if (dt == null || textComp == null) return null;
            if (string.IsNullOrEmpty(textComp.text)) return null;
            if (!enableRevealableUITextFix || revealableFixCalled) return null;
            Component revealableComp = null;
            Component[] comps = dt.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().Name == "RevealableUIText")
                {
                    revealableComp = comps[i];
                    break;
                }
            }
            if (revealableComp == null)
            {
                Debug.LogWarning("[TEXTVIS] FixRevealableUITextRevealProcess: RevealableUIText component not found");
                return null;
            }
            Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: start, text=" + (textComp.text.Length > 50 ? textComp.text.Substring(0, 50) + "..." : textComp.text));
            textComp.SetVerticesDirty();
            textComp.SetLayoutDirty();
            Type revealableType = revealableComp.GetType();
            CheckAndCacheRebuildSignature(revealableType);
            if (cachedRebuildMethod != null)
            {
                try
                {
                    if (cachedRebuildParamInfos == null || cachedRebuildParamInfos.Length == 0)
                    {
                        cachedRebuildMethod.Invoke(revealableComp, null);
                        Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: Rebuild() called with no params");
                    }
                    else
                    {
                        object[] paramArray = new object[cachedRebuildParamInfos.Length];
                        for (int pi = 0; pi < cachedRebuildParamInfos.Length; pi++)
                        {
                            Type paramType = cachedRebuildParamInfos[pi].ParameterType;
                            if (paramType == typeof(bool))
                            {
                                paramArray[pi] = true;
                            }
                            else if (paramType == typeof(int))
                            {
                                paramArray[pi] = 0;
                            }
                            else if (paramType == typeof(float))
                            {
                                paramArray[pi] = 0f;
                            }
                            else if (paramType.IsEnum)
                            {
                                paramArray[pi] = Activator.CreateInstance(paramType);
                            }
                            else if (paramType.IsValueType)
                            {
                                paramArray[pi] = Activator.CreateInstance(paramType);
                            }
                            else
                            {
                                paramArray[pi] = null;
                            }
                        }
                        cachedRebuildMethod.Invoke(revealableComp, paramArray);
                        Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: Rebuild() called with " + paramArray.Length + " params");
                    }
                }
                catch (Exception exRebuild)
                {
                    Debug.LogWarning("[TEXTVIS] FixRevealableUITextRevealProcess Rebuild invoke exception: " + exRebuild.Message + ", skip Rebuild, continue subsequent fixes");
                }
            }
            else
            {
                Debug.LogWarning("[TEXTVIS] FixRevealableUITextRevealProcess: cachedRebuildMethod is null, skip Rebuild call");
            }
            PropertyInfo revealProgressProp = revealableType.GetProperty("RevealProgress", BindingFlags.Public | BindingFlags.Instance);
            if (revealProgressProp != null && revealProgressProp.CanWrite)
            {
                revealProgressProp.SetValue(revealableComp, 1.0f);
                Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: RevealProgress set to 1.0f");
            }
            else
            {
                PropertyInfo revealProgressPropNP = revealableType.GetProperty("RevealProgress", BindingFlags.NonPublic | BindingFlags.Instance);
                if (revealProgressPropNP != null && revealProgressPropNP.CanWrite)
                {
                    revealProgressPropNP.SetValue(revealableComp, 1.0f);
                    Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: RevealProgress set to 1.0f (NonPublic)");
                }
            }
            PropertyInfo clipRectProp = revealableType.GetProperty("ClipRect", BindingFlags.Public | BindingFlags.Instance);
            if (clipRectProp != null && clipRectProp.CanWrite)
            {
                object clipRectVal = clipRectProp.GetValue(revealableComp, null);
                if (clipRectVal is Vector4)
                {
                    Vector4 cr = (Vector4)clipRectVal;
                    if (cr.z <= 0f || cr.w <= 0f)
                    {
                        Vector4 fullScreenRect = new Vector4(0f, 0f, (float)Screen.width, (float)Screen.height);
                        clipRectProp.SetValue(revealableComp, fullScreenRect);
                        Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: ClipRect set to full screen");
                    }
                }
            }
            Graphic graphic = dt.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
                graphic.SetLayoutDirty();
            }
            revealableFixCalled = true;
            revealableFixWaitFrameCount = 0;
            rec.NewValue = "hasMoved fix applied, RevealProgress=1.0f";
            rec.Summary = "FixRevealableUITextRevealProcess applied";
            Debug.Log("[TEXTVIS] FixRevealableUITextRevealProcess: completed, revealableFixCalled=true");
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixRevealableUITextRevealProcess exception: " + ex.Message); }
        return rec;
    }

    private static void RegisterLogMessageReceivedCallback()
    {
        if (callbackRegistered) return;
        try
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceived += OnLogMessageReceived;
            callbackRegistered = true;
            Debug.Log("[TEXTVIS] RegisterLogMessageReceivedCallback: log callback registered (remove then add for idempotency)");
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] RegisterLogMessageReceivedCallback failed: " + ex.Message); }
    }

    private static void OnLogMessageReceived(string message, string stackTrace, LogType logType)
    {
        if (logType != LogType.Error && logType != LogType.Exception) return;
        if (realtimeErrorEntries == null) return;
        if (realtimeErrorEntries.Count >= errorCollectionMaxCount) return;
        try
        {
            if (enableStencilErrorSuppress && logType == LogType.Error && message != null && message.Contains("Material doesn't have a float or range property"))
            {
                bool isStencilError = false;
                if (message.Contains("_StencilRef") || message.Contains("_StencilPass") || message.Contains("_StencilComp") || message.Contains("_StencilOp") || message.Contains("_StencilWriteMask") || message.Contains("_StencilReadMask"))
                {
                    isStencilError = true;
                }
                if (isStencilError)
                {
                    stencilErrorSuppressCount++;
                    if (stencilErrorSuppressCount % 100 == 0)
                    {
                        Debug.Log("[TEXTVIS] Stencil property errors suppressed: " + stencilErrorSuppressCount);
                    }
                    return;
                }
            }
        }
        catch (Exception exStencil)
        {
            Debug.LogWarning("[TEXTVIS] OnLogMessageReceived Stencil error suppress exception: " + exStencil.Message);
        }
        try
        {
            if (enableFindObjectTypeSuppress && message != null && message.Contains("FindAllObjectsOfType") && (message.Contains("TextRevealBehaviou") || message.Contains("TextRevealBehaviour")))
            {
                findObjectTypeSuppressCount++;
                if (findObjectTypeSuppressCount % 100 == 0)
                    Debug.Log("[TEXTVIS] FindAllObjectsOfType TextRevealBehaviour errors suppressed: " + findObjectTypeSuppressCount);
                return;
            }
            if (enableBatchErrorSuppress && message != null)
            {
                string msgKey = message.Length > 100 ? message.Substring(0, 100) : message;
                int occurrenceCount = 0;
                if (errorOccurrenceCounts.ContainsKey(msgKey))
                {
                    occurrenceCount = errorOccurrenceCounts[msgKey];
                    errorOccurrenceCounts[msgKey] = occurrenceCount + 1;
                }
                else
                {
                    if (errorOccurrenceCounts.Count < errorOccurrenceMaxCount)
                        errorOccurrenceCounts[msgKey] = 1;
                    occurrenceCount = 1;
                }
                if (occurrenceCount > 10)
                {
                    bool dialogueRelated = message.Contains("DialogueText") || message.Contains("Naninovel") || message.Contains("RevealableUIText") || message.Contains("ScriptPlayer") || message.Contains("TextPrinter") || message.Contains("hasMoved") || message.Contains("effectColor") || message.Contains("Lorem ipsum");
                    if (!dialogueRelated)
                    {
                        batchErrorSuppressCount++;
                        if (batchErrorSuppressCount % 100 == 0)
                            Debug.Log("[TEXTVIS] Repeated error suppressed: " + msgKey + " x" + (occurrenceCount + 1));
                        return;
                    }
                }
            }
            var entry = new ErrorContentEntry();
            entry.Message = message != null ? message : "";
            entry.StackTrace = stackTrace != null ? stackTrace : "";
            entry.ErrorType = logType.ToString();
            if (message.Contains("Shader")) entry.Category = ErrorCategory.ShaderCompilation;
            else if (message.Contains("Resources") || message.Contains("Load")) entry.Category = ErrorCategory.ResourceLoading;
            else if (message.Contains("NullReference")) entry.Category = ErrorCategory.NullReference;
            else if (message.Contains("MissingReference")) entry.Category = ErrorCategory.MissingReference;
            else if (message.Contains("Naninovel")) entry.Category = ErrorCategory.NaninovelRuntime;
            else if (message.Contains("TypeLoad") || message.Contains("Could not load") || message.Contains("type load")) entry.Category = ErrorCategory.TypeLoad;
            else entry.Category = ErrorCategory.Other;
            entry.RelatedToDialogueText = message.Contains("ScriptPlayer") || message.Contains("@print") || message.Contains("CustomVariables") || message.Contains("textComp") || message.Contains("Revealable") || message.Contains("Printer") || message.Contains("Dialogue") || message.Contains("Text") || message.Contains("Shader");
            realtimeErrorEntries.Add(entry);
        }
        catch { }
    }

    private static void SuppressDuplicateErrors(ErrorContentSnapshot errorSnapshot)
    {
        if (errorSnapshot.Entries == null) return;
        try
        {
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < errorSnapshot.Entries.Count; i++)
            {
                var msg = errorSnapshot.Entries[i].Message;
                if (!counts.ContainsKey(msg)) counts[msg] = 0;
                counts[msg]++;
            }
            int suppressedCount = 0;
            for (int i = 0; i < errorSnapshot.Entries.Count; i++)
            {
                var msg = errorSnapshot.Entries[i].Message;
                if (counts.ContainsKey(msg) && counts[msg] > 10)
                {
                    var entry = errorSnapshot.Entries[i];
                    entry.Suppressed = true;
                    entry.OccurrenceCount = counts[msg];
                    errorSnapshot.Entries[i] = entry;
                    suppressedCount++;
                }
            }
            if (suppressedCount > 0) Debug.LogWarning("[TEXTVIS] Suppressed " + suppressedCount + " duplicate errors");
        }
        catch { }
    }

    private static FixChangeRecord FixRuntimeErrors(ErrorContentSnapshot errorSnapshot)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = "RuntimeErrors";
        rec.PropertyName = "ErrorCount";
        rec.OldValue = errorSnapshot.Entries != null ? errorSnapshot.Entries.Count.ToString() : "0";
        try
        {
            SuppressDuplicateErrors(errorSnapshot);
            if (errorSnapshot.CategoryCounts != null)
            {
                if (errorSnapshot.CategoryCounts.ContainsKey(ErrorCategory.ResourceLoading) && errorSnapshot.CategoryCounts[ErrorCategory.ResourceLoading] > 50)
                    Debug.LogWarning("[TEXTVIS] Many ResourceLoading errors, check Naninovel resource path configuration");
                if (errorSnapshot.CategoryCounts.ContainsKey(ErrorCategory.TypeLoad) && errorSnapshot.CategoryCounts[ErrorCategory.TypeLoad] > 50)
                    Debug.LogWarning("[TEXTVIS] Many TypeLoad errors, check TypeAssemblies configuration and DLL loading");
                if (errorSnapshot.CategoryCounts.ContainsKey(ErrorCategory.NullReference) && errorSnapshot.CategoryCounts[ErrorCategory.NullReference] > 50)
                    Debug.LogWarning("[TEXTVIS] Many NullReference errors, analyze specific locations");
            }
            rec.NewValue = "Processed " + (errorSnapshot.Entries != null ? errorSnapshot.Entries.Count : 0) + " errors";
            rec.Summary = "Runtime error fix applied";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixRuntimeErrors: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixStencilPropertyMissing(Material material)
    {
        FixChangeRecord rec = new FixChangeRecord();
        rec.ObjectPath = "Material";
        rec.PropertyName = "StencilProperties";
        rec.OldValue = "missing";
        try
        {
            if (material == null) return null;
            if (!enableStencilPropertyFix) return null;
            int matInstanceId = material.GetInstanceID();
            if (stencilFixedMaterialIds.Contains(matInstanceId)) return null;
            if (material.HasProperty("_StencilRef")) return null;
            string matName = material.name;
            material.SetFloat("_StencilRef", 0f);
            material.SetFloat("_StencilPass", 0f);
            material.SetFloat("_StencilComp", 0f);
            material.SetFloat("_StencilOp", 0f);
            material.SetFloat("_StencilWriteMask", 0f);
            material.SetFloat("_StencilReadMask", 0f);
            stencilFixedMaterialIds.Add(matInstanceId);
            stencilFixCount++;
            rec.NewValue = "stencil properties added";
            rec.Summary = "Stencil properties added to material " + matName;
            Debug.Log("[TEXTVIS] Stencil properties added to material: " + matName + " instanceId=" + matInstanceId + " totalFixed=" + stencilFixCount);
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixStencilPropertyMissing exception: " + ex.Message); }
        return rec;
    }

    private static void DiagnoseTextPreviewState()
    {
        try
        {
            if (!enableTextPreviewDiagnosis) return;
            Type textPrinterManagerType = cachedTextPrinterManagerType;
            if (textPrinterManagerType == null && !textPreviewTypeLookupDone)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type[] types = null;
                    try { types = assemblies[i].GetTypes(); }
                    catch (Exception) { continue; }
                    for (int j = 0; j < types.Length; j++)
                    {
                        string typeName = types[j].Name;
                        string typeFullName = types[j].FullName;
                        if (typeName == "TextPrinterManager" || (typeFullName != null && typeFullName.Contains("TextPrinterManager")))
                        {
                            textPrinterManagerType = types[j];
                            cachedTextPrinterManagerType = textPrinterManagerType;
                            break;
                        }
                    }
                    if (textPrinterManagerType != null) break;
                }
                textPreviewTypeLookupDone = true;
            }
            if (textPrinterManagerType == null) return;
            string printerId = "";
            string printerText = "";
            bool printerFound = false;
            Type engineTypeLocal = null;
            Assembly[] engineAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < engineAssemblies.Length; i++)
            {
                Type[] engineTypes = null;
                try { engineTypes = engineAssemblies[i].GetTypes(); }
                catch (Exception) { continue; }
                for (int j = 0; j < engineTypes.Length; j++)
                {
                    if (engineTypes[j].Name == "Engine") { engineTypeLocal = engineTypes[j]; break; }
                }
                if (engineTypeLocal != null) break;
            }
            if (engineTypeLocal != null)
            {
                PropertyInfo engineInstanceProp = engineTypeLocal.GetProperty("Engine", BindingFlags.Public | BindingFlags.Static);
                if (engineInstanceProp == null)
                    engineInstanceProp = engineTypeLocal.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (engineInstanceProp != null)
                {
                    object engineInstance = engineInstanceProp.GetValue(null, null);
                    if (engineInstance != null)
                    {
                        PropertyInfo printerManagerProp = engineInstance.GetType().GetProperty("TextPrinterManager", BindingFlags.Public | BindingFlags.Instance);
                        if (printerManagerProp != null)
                        {
                            object printerManager = printerManagerProp.GetValue(engineInstance, null);
                            if (printerManager != null)
                            {
                                printerFound = true;
                                PropertyInfo defaultPrinterProp = printerManager.GetType().GetProperty("DefaultPrinter", BindingFlags.Public | BindingFlags.Instance);
                                if (defaultPrinterProp != null)
                                {
                                    object defaultPrinter = defaultPrinterProp.GetValue(printerManager, null);
                                    if (defaultPrinter != null)
                                    {
                                        PropertyInfo printerIdProp = defaultPrinter.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                                        if (printerIdProp != null)
                                        {
                                            object printerIdVal = printerIdProp.GetValue(defaultPrinter, null);
                                            printerId = printerIdVal != null ? printerIdVal.ToString() : "";
                                        }
                                        PropertyInfo textProp = defaultPrinter.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                                        if (textProp != null)
                                        {
                                            object textVal = textProp.GetValue(defaultPrinter, null);
                                            printerText = textVal != null ? textVal.ToString() : "";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            string textPreview = printerText;
            if (textPreview != null && textPreview.Length > 100)
                textPreview = textPreview.Substring(0, 100) + "...";
            Debug.Log("[TEXTVIS] TEXT Preview: PrinterId=" + printerId + " Text=" + textPreview + " PrinterGPrinterFound=" + printerFound);
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] DiagnoseTextPreviewState exception: " + ex.Message); }
    }

    private static void DiagnosePlaceholderTextSource(Transform dt, Text textComp)
    {
        try
        {
            if (dt == null || textComp == null) return;
            string textContent = textComp.text;
            Debug.Log("[TEXTVIS] Placeholder text detected: " + (textContent.Length > 80 ? textContent.Substring(0, 80) + "..." : textContent));
            Component revealableComp = null;
            Component[] comps = dt.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().Name == "RevealableUIText")
                {
                    revealableComp = comps[i];
                    break;
                }
            }
            if (revealableComp != null)
            {
                Type revealableType = revealableComp.GetType();
                string[] defaultTextPropNames = new string[] { "DefaultText", "PlaceholderText", "InitialText", "Text" };
                for (int pi = 0; pi < defaultTextPropNames.Length; pi++)
                {
                    PropertyInfo propInfo = null;
                    try
                    {
                        propInfo = revealableType.GetProperty(defaultTextPropNames[pi], BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    }
                    catch (Exception) { }
                    if (propInfo != null)
                    {
                        try
                        {
                            object defaultTextVal = propInfo.GetValue(revealableComp, null);
                            string defaultTextStr = defaultTextVal != null ? defaultTextVal.ToString() : "null";
                            Debug.Log("[TEXTVIS] DiagnosePlaceholderTextSource: RevealableUIText property " + defaultTextPropNames[pi] + " = " + (defaultTextStr.Length > 80 ? defaultTextStr.Substring(0, 80) + "..." : defaultTextStr));
                        }
                        catch (Exception exGet)
                        {
                            Debug.LogWarning("[TEXTVIS] DiagnosePlaceholderTextSource get property " + defaultTextPropNames[pi] + " exception: " + exGet.Message);
                        }
                    }
                }
            }
            else
            {
                Debug.Log("[TEXTVIS] DiagnosePlaceholderTextSource: RevealableUIText component not found");
            }
            Type scriptPlayerType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int ai = 0; ai < assemblies.Length; ai++)
            {
                Type[] types = null;
                try
                {
                    types = assemblies[ai].GetTypes();
                }
                catch (Exception) { continue; }
                for (int tj = 0; tj < types.Length; tj++)
                {
                    string typeName = types[tj].Name;
                    if (typeName == "ScriptPlayer")
                    {
                        scriptPlayerType = types[tj];
                        break;
                    }
                }
                if (scriptPlayerType != null) break;
            }
            if (scriptPlayerType != null)
            {
                Debug.Log("[TEXTVIS] DiagnosePlaceholderTextSource: ScriptPlayer type found: " + scriptPlayerType.FullName);
                string[] scriptPlayerPropNames = new string[] { "PlayedScript", "CurrentScript", "IsPlaying", "ScriptPath" };
                for (int sp = 0; sp < scriptPlayerPropNames.Length; sp++)
                {
                    PropertyInfo spProp = null;
                    try
                    {
                        spProp = scriptPlayerType.GetProperty(scriptPlayerPropNames[sp], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    }
                    catch (Exception) { }
                    if (spProp != null)
                    {
                        try
                        {
                            object spVal = null;
                            if (spProp.GetGetMethod(true) != null && spProp.GetGetMethod(true).IsStatic)
                            {
                                spVal = spProp.GetValue(null, null);
                            }
                            Debug.Log("[TEXTVIS] DiagnosePlaceholderTextSource: ScriptPlayer property " + scriptPlayerPropNames[sp] + " = " + (spVal != null ? spVal.ToString() : "null"));
                        }
                        catch (Exception exSp)
                        {
                            Debug.LogWarning("[TEXTVIS] DiagnosePlaceholderTextSource ScriptPlayer property " + scriptPlayerPropNames[sp] + " exception: " + exSp.Message);
                        }
                    }
                }
            }
            else
            {
                Debug.Log("[TEXTVIS] DiagnosePlaceholderTextSource: ScriptPlayer type not found");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TEXTVIS] DiagnosePlaceholderTextSource exception: " + ex.Message);
        }
    }

    private static void TriggerActualDialogueContentLoad(Transform dt, Text textComp)
    {
        try
        {
            if (dt == null || textComp == null) return;
            Debug.Log("[TEXTVIS] TriggerActualDialogueContentLoad: start");
            Type spType = FindScriptPlayerType();
            bool scriptPlayerTriggered = false;
            if (spType != null)
            {
                MethodInfo execMethod = EnumerateAndSelectScriptPlayerExecMethod(spType);
                if (execMethod != null)
                {
                    try
                    {
                        object target = null;
                        if (!execMethod.IsStatic)
                        {
                            target = GetScriptPlayerInstance(spType);
                        }
                        if (execMethod.IsStatic || target != null)
                        {
                            ParameterInfo[] execParams = execMethod.GetParameters();
                            object[] args = null;
                            if (execParams.Length > 0)
                            {
                                args = new object[execParams.Length];
                                for (int pi = 0; pi < execParams.Length; pi++)
                                {
                                    Type paramType = execParams[pi].ParameterType;
                                    if (paramType == typeof(string)) args[pi] = GetScriptResourcePath();
                                    else if (paramType == typeof(bool)) args[pi] = true;
                                    else if (paramType == typeof(int)) args[pi] = 0;
                                    else if (paramType == typeof(float)) args[pi] = 0f;
                                    else if (paramType.IsEnum || paramType.IsValueType) args[pi] = Activator.CreateInstance(paramType);
                                    else args[pi] = null;
                                }
                            }
                            execMethod.Invoke(target, args);
                            scriptPlayerTriggered = true;
                            scriptExecTriggered = true;
                            Debug.Log("[TEXTVIS] TriggerActualDialogueContentLoad: ScriptPlayer." + execMethod.Name + " triggered with " + (execParams.Length > 0 ? execParams.Length + " params" : "no params"));
                        }
                        else
                        {
                            Debug.LogWarning("[TEXTVIS] TriggerActualDialogueContentLoad: ScriptPlayer instance not found for non-static method " + execMethod.Name);
                        }
                    }
                    catch (Exception exExec)
                    {
                        Debug.LogWarning("[TEXTVIS] TriggerActualDialogueContentLoad ScriptPlayer." + execMethod.Name + " invoke exception: " + exExec.Message);
                    }
                }
                else
                {
                    Debug.LogWarning("[TEXTVIS] TriggerActualDialogueContentLoad: no exec method found via enumeration");
                }
            }
            if (!scriptPlayerTriggered)
            {
                string savedText = textComp.text;
                textComp.text = "";
                textComp.SetVerticesDirty();
                textComp.SetLayoutDirty();
                Debug.Log("[TEXTVIS] TriggerActualDialogueContentLoad: ScriptPlayer trigger failed, cleared textComp.text (was: " + (savedText.Length > 50 ? savedText.Substring(0, 50) + "..." : savedText) + "), waiting for Naninovel to set actual content");
            }
            placeholderDiagWaitFrameCount = 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TEXTVIS] TriggerActualDialogueContentLoad exception: " + ex.Message);
        }
    }

    private static FixChangeRecord FixShadowEffectBlack(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "Shadow.enabled";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null) return null;
            var shadow = dt.GetComponent<Shadow>();
            if (shadow == null) return null;
            rec.OldValue = "shadow.enabled=" + shadow.enabled + " effectColor=" + shadow.effectColor;
            bool textHasVertices = graphic.canvasRenderer != null && graphic.canvasRenderer.hasMoved;
            var ec = shadow.effectColor;
            bool isBlack = ec.r < 0.01f && ec.g < 0.01f && ec.b < 0.01f;
            if (isBlack)
            {
                var ed = shadow.effectDistance;
                bool isOverlay = Mathf.Abs(ed.x) < 2f && Mathf.Abs(ed.y) < 2f;
                if (isOverlay)
                {
                    rec.OldValue = rec.OldValue + " -> effectColor=" + ec;
                    shadow.effectColor = new Color(1f, 1f, 1f, 0f);
                    rec.NewValue = "Fixed effectColor black overlay to transparent";
                    rec.Summary = "Set Shadow.effectColor to transparent (was black overlay)";
                    graphic.SetVerticesDirty();
                    Debug.LogWarning("[TEXTVIS] FixShadowEffectBlack: effectColor fixed before hasMoved logic, oldColor=" + ec + " newColor=" + shadow.effectColor + " hasMoved=" + textHasVertices + " shadow.enabled=" + shadow.enabled);
                }
            }
            if (!textHasVertices && shadow.enabled)
            {
                shadow.enabled = false;
                if (string.IsNullOrEmpty(rec.NewValue)) rec.NewValue = "shadow disabled (text has no vertices)";
                else rec.NewValue += "; shadow disabled (text has no vertices)";
                rec.Summary = "Disabled Shadow to eliminate black shadow when text not rendering";
            }
            else if (textHasVertices && !shadow.enabled)
            {
                shadow.enabled = true;
                if (string.IsNullOrEmpty(rec.NewValue)) rec.NewValue = "shadow re-enabled (text has vertices)";
                else rec.NewValue += "; shadow re-enabled (text has vertices)";
                rec.Summary = "Re-enabled Shadow now text is rendering";
            }
            else
            {
                if (string.IsNullOrEmpty(rec.NewValue)) rec.NewValue = "no change needed";
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixShadowEffectBlack: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixShadowEffectBlackDirectCall(Transform dt, ShadowDiagnosisSnapshot shadowDiag)
    {
        FixChangeRecord rec = null;
        try
        {
            if (dt == null || !enableShadowDirectFix) return null;
            if (shadowDisabled) return null;
            if (!shadowDiag.IsShadowBlack) return null;
            float absDistX = Mathf.Abs(shadowDiag.EffectDistance.x);
            float absDistY = Mathf.Abs(shadowDiag.EffectDistance.y);
            if (absDistX >= 2f || absDistY >= 2f) return null;
            if (enableShadowDisable && shadowContinuousFixCount > shadowContinuousFixThreshold)
            {
                try
                {
                    Graphic graphicForDisable = dt.GetComponent<Graphic>();
                    if (graphicForDisable != null)
                    {
                        Shadow shadowForDisable = graphicForDisable.GetComponent<Shadow>();
                        if (shadowForDisable != null && shadowForDisable.enabled)
                        {
                            Color oldEffectColor = shadowForDisable.effectColor;
                            shadowForDisable.enabled = false;
                            shadowDisabled = true;
                            graphicForDisable.SetVerticesDirty();
                            Debug.Log("[TEXTVIS] FixShadowEffectBlackDirectCall: Shadow disabled after " + shadowContinuousFixCount + " continuous fixes, effectColor reset by Naninovel each frame, oldEffectColor=" + oldEffectColor);
                            rec = new FixChangeRecord();
                            rec.ObjectPath = dt != null ? dt.gameObject.name : "null";
                            rec.PropertyName = "ShadowEnabled";
                            rec.OldValue = "true";
                            rec.NewValue = "false";
                            rec.Summary = "Shadow disabled after " + shadowContinuousFixCount + " continuous fixes";
                            return rec;
                        }
                    }
                }
                catch (Exception exDisable)
                {
                    Debug.LogWarning("[TEXTVIS] FixShadowEffectBlackDirectCall Shadow disable exception: " + exDisable.Message);
                }
            }
            Debug.Log("[TEXTVIS] FixShadowEffectBlackDirectCall: effectColor black detected, direct call FixShadowEffectBlack");
            rec = FixShadowEffectBlack(dt);
            if (enableShadowContinuousFix)
            {
                if (rec != null)
                {
                    shadowContinuousFixCount++;
                    Debug.Log("[TEXTVIS] FixShadowEffectBlackDirectCall: continuous fix applied, count=" + shadowContinuousFixCount + ", summary=" + rec.Summary);
                    try
                    {
                        Graphic graphicForVerify = dt.GetComponent<Graphic>();
                        if (graphicForVerify != null)
                        {
                            Shadow shadowForVerify = graphicForVerify.GetComponent<Shadow>();
                            if (shadowForVerify != null && shadowForVerify.effectColor.r < 0.01f && shadowForVerify.effectColor.g < 0.01f && shadowForVerify.effectColor.b < 0.01f)
                            {
                                Debug.Log("[TEXTVIS] FixShadowEffectBlackDirectCall: effectColor reset by Naninovel each frame, continuous fix count=" + shadowContinuousFixCount);
                            }
                            else
                            {
                                shadowContinuousFixCount = 0;
                                Debug.Log("[TEXTVIS] FixShadowEffectBlackDirectCall: effectColor not black, reset shadowContinuousFixCount=0");
                            }
                        }
                    }
                    catch (Exception exVerify)
                    {
                        Debug.LogWarning("[TEXTVIS] FixShadowEffectBlackDirectCall verify exception: " + exVerify.Message);
                    }
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixShadowEffectBlackDirectCall exception: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixFontTextureMissing(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "font.texture";
        rec.OldValue = "missing/invalid";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            var textComp = graphic as Text;
            if (textComp == null || textComp.font == null) return null;
            textComp.font.RequestCharactersInTexture(textComp.text, textComp.fontSize, textComp.fontStyle);
            var mat = graphic.material;
            if (mat != null && textComp.font.material != null && textComp.font.material.mainTexture != null)
            {
                mat.SetTexture("_MainTex", textComp.font.material.mainTexture);
                rec.NewValue = textComp.font.material.mainTexture.name;
            }
            rec.Summary = "Requested characters in texture and synced _MainTex";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixFontTextureMissing: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixShaderModificationNotEffective(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "shader";
        rec.OldValue = "not effective";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null || graphic.material == null) return null;
            var shader = Shader.Find("Naninovel/RevealableText");
            if (shader != null && shader.isSupported)
            {
                graphic.material.shader = shader;
                rec.NewValue = "Naninovel/RevealableText";
                rec.Summary = "Reassigned Naninovel/RevealableText shader";
            }
            else
            {
                var fallback = Shader.Find("UI/Default");
                if (fallback != null)
                {
                    graphic.material.shader = fallback;
                    rec.NewValue = "UI/Default (fallback)";
                    rec.Summary = "Shader not available, using UI/Default fallback";
                    Debug.LogWarning("[TEXTVIS] Naninovel/RevealableText shader not available, using UI/Default fallback");
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixShaderModificationNotEffective: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixFontMaterialConfigError(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "font/material";
        rec.OldValue = "invalid";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            var textComp = graphic as Text;
            if (textComp == null) return null;
            var shader = Shader.Find("Naninovel/RevealableText");
            if (shader != null && graphic.material != null)
            {
                graphic.material.shader = shader;
            }
            rec.NewValue = "shader corrected";
            rec.Summary = "Corrected material shader to Naninovel/RevealableText";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixFontMaterialConfigError: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixClipRectZeroVector(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "_CharClipRect/_LineClipRect";
        rec.OldValue = "(0,0,0,0)";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            if (graphic == null || graphic.material == null) return null;
            var safeRect = new Vector4(-32767f, -32767f, 32767f, 32767f);
            graphic.material.SetVector(charClipRectId, safeRect);
            graphic.material.SetVector(lineClipRectId, safeRect);
            rec.NewValue = "(-32767,-32767,32767,32767)";
            rec.Summary = "Set clip rects to safe default (no clipping)";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixClipRectZeroVector: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord FixClipRectUpdateLogicDefect(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "UpdateClipRects";
        rec.OldValue = "not triggered";
        try
        {
            if (textRevealBehaviourType == null || !typeof(UnityEngine.Object).IsAssignableFrom(textRevealBehaviourType)) return null;
            var comps = UnityEngine.Object.FindObjectsOfType(textRevealBehaviourType);
            if (comps == null || comps.Length == 0) return null;
            var updateMethod = textRevealBehaviourType.GetMethod("UpdateClipRects", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (updateMethod != null)
            {
                for (int i = 0; i < comps.Length; i++)
                {
                    updateMethod.Invoke(comps[i], null);
                }
                rec.NewValue = "UpdateClipRects invoked";
                rec.Summary = "Invoked UpdateClipRects on " + comps.Length + " instances";
            }
            else
            {
                var graphic = dt.GetComponent<Graphic>();
                if (graphic != null && graphic.material != null)
                {
                    var safeRect = new Vector4(-32767f, -32767f, 32767f, 32767f);
                    graphic.material.SetVector(charClipRectId, safeRect);
                    graphic.material.SetVector(lineClipRectId, safeRect);
                }
                rec.NewValue = "manual safe default";
                rec.Summary = "UpdateClipRects method not found, set safe default";
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixClipRectUpdateLogicDefect: " + ex.Message); }
        return rec;
    }

    private static Type FindRuntimeInitializerType()
    {
        if (runtimeInitializerType != null) return runtimeInitializerType;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.FullName == "Naninovel.RuntimeInitializer" || t.FullName == "Elringus.Naninovel.RuntimeInitializer")
                    {
                        runtimeInitializerType = t;
                        return t;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static MethodInfo GetOnApplicationLoadedMethod(Type rtType)
    {
        if (rtType == null) return null;
        try
        {
            var mi = rtType.GetMethod("OnApplicationLoaded", BindingFlags.NonPublic | BindingFlags.Static);
            if (mi != null) return mi;
            mi = rtType.GetMethod("OnApplicationLoaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return mi;
        }
        catch { return null; }
    }

    private static object ConstructConfigProvider(string resourcesPath)
    {
        try
        {
            if (configProviderType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.FullName == "Naninovel.ProjectConfigurationProvider" || t.FullName == "Elringus.Naninovel.ProjectConfigurationProvider")
                            {
                                configProviderType = t;
                                break;
                            }
                        }
                    }
                    catch { }
                    if (configProviderType != null) break;
                }
            }
            if (configProviderType == null) return null;
            var ctor = configProviderType.GetConstructor(new Type[] { typeof(string) });
            if (ctor != null) return ctor.Invoke(new object[] { resourcesPath });
            var defaultCtor = configProviderType.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null) return defaultCtor.Invoke(null);
            return null;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] ConstructConfigProvider failed: " + ex.Message); return null; }
    }

    private static object ConstructRuntimeBehaviour(bool dontDestroyOnLoad)
    {
        try
        {
            if (runtimeBehaviourType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.FullName == "Naninovel.RuntimeBehaviour" || t.FullName == "Elringus.Naninovel.RuntimeBehaviour")
                            {
                                runtimeBehaviourType = t;
                                break;
                            }
                        }
                    }
                    catch { }
                    if (runtimeBehaviourType != null) break;
                }
            }
            if (runtimeBehaviourType == null) return null;
            var createMethod = runtimeBehaviourType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(bool) }, null);
            if (createMethod != null) return createMethod.Invoke(null, new object[] { dontDestroyOnLoad });
            var defaultCtor = runtimeBehaviourType.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null) return defaultCtor.Invoke(null);
            return null;
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] ConstructRuntimeBehaviour failed: " + ex.Message); return null; }
    }

    private static void StartUniTaskCoroutine(object taskObj)
    {
        if (taskObj == null) return;
        try
        {
            var taskType = taskObj.GetType();
            var toCoroutineMethod = taskType.GetMethod("ToCoroutine", BindingFlags.Public | BindingFlags.Instance);
            if (toCoroutineMethod != null)
            {
                var coroutine = toCoroutineMethod.Invoke(taskObj, null) as System.Collections.IEnumerator;
                if (coroutine != null)
                {
                    EditorApplication.update += () => { try { if (!coroutine.MoveNext()) { } } catch { } };
                }
            }
        }
        catch { }
    }

    private static bool CheckEngineInitialized()
    {
        try
        {
            if (engineType == null) return false;
            var initProp = engineType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
            if (initProp != null) return (bool)initProp.GetValue(null, null);
        }
        catch { }
        return false;
    }

    private static FixChangeRecord FixNaninovelInitFailure(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "Engine.Init";
        rec.OldValue = "not initialized";
        rec.NewValue = "no action taken";
        try
        {
            if (engineInitConfirmed)
            {
                return null;
            }
            if (engineType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.FullName == "Naninovel.Engine" || t.FullName == "Elringus.Naninovel.Engine")
                            {
                                engineType = t;
                                break;
                            }
                        }
                    }
                    catch { }
                    if (engineType != null) break;
                }
            }
            if (engineType == null) { rec.NewValue = "Engine type not found"; return rec; }

            if (engineInitCalled)
            {
                engineInitWaitFrameCount++;
                if (engineInitWaitFrameCount >= 30)
                {
                    bool initialized = CheckEngineInitialized();
                    if (initialized)
                    {
                        engineInitConfirmed = true;
                        rec.NewValue = "Engine initialized successfully";
                        rec.Summary = "Init confirmed after " + engineInitWaitFrameCount + " frames (Plan " + currentInitPlan + ")";
                        Debug.Log("[TEXTVIS] Engine init confirmed, stop repeated reporting. waitFrameCount=" + engineInitWaitFrameCount);
                        return rec;
                    }
                    if (currentInitPlan == 1 && !triedPlanB)
                    {
                        engineInitCalled = false;
                        engineInitWaitFrameCount = 0;
                    }
                    else if (currentInitPlan == 2 && !triedPlanC)
                    {
                        engineInitCalled = false;
                        engineInitWaitFrameCount = 0;
                    }
                    else
                    {
                        rec.NewValue = "All init plans failed. Tried: A=" + triedPlanA + " B=" + triedPlanB + " C=" + triedPlanC;
                        rec.Summary = "Engine still not initialized after exhausting all plans";
                        return rec;
                    }
                }
                else
                {
                    rec.NewValue = "Waiting for init (" + engineInitWaitFrameCount + "/30 frames, Plan " + currentInitPlan + ")";
                    return rec;
                }
            }

            if (!triedPlanA)
            {
                var rtType = FindRuntimeInitializerType();
                if (rtType != null)
                {
                    var onAppMethod = GetOnApplicationLoadedMethod(rtType);
                    if (onAppMethod != null)
                    {
                        try
                        {
                            onAppMethod.Invoke(null, null);
                            triedPlanA = true;
                            currentInitPlan = 1;
                            engineInitCalled = true;
                            engineInitCallFrame = frameCount;
                            engineInitWaitFrameCount = 0;
                            rec.NewValue = "PlanA OnApplicationLoaded invoked";
                            rec.Summary = "Called RuntimeInitializer.OnApplicationLoaded() at frame " + frameCount;
                            Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure: PlanA OnApplicationLoaded invoked");
                            return rec;
                        }
                        catch (Exception exA)
                        {
                            Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanA failed: " + exA.Message);
                            triedPlanA = true;
                        }
                    }
                    else
                    {
                        triedPlanA = true;
                        Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanA: OnApplicationLoaded method not found");
                    }
                }
                else
                {
                    triedPlanA = true;
                    Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanA: RuntimeInitializer type not found");
                }
            }

            if (!triedPlanB)
            {
                try
                {
                    var initMethod = engineType.GetMethod("InitializeAsync", BindingFlags.Public | BindingFlags.Static);
                    if (initMethod != null && initMethod.GetParameters().Length == 3)
                    {
                        var configProvider = ConstructConfigProvider("naninovel/configuration");
                        var behaviour = ConstructRuntimeBehaviour(true);
                        if (configProvider != null && behaviour != null)
                        {
                            var args = new object[] { configProvider, behaviour, null };
                            var taskObj = initMethod.Invoke(null, args);
                            triedPlanB = true;
                            currentInitPlan = 2;
                            engineInitCalled = true;
                            engineInitCallFrame = frameCount;
                            engineInitWaitFrameCount = 0;
                            rec.NewValue = "PlanB Engine.InitializeAsync(3 params) invoked";
                            rec.Summary = "Called Engine.InitializeAsync(config, behaviour, null) at frame " + frameCount;
                            StartUniTaskCoroutine(taskObj);
                            Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure: PlanB Engine.InitializeAsync invoked with 3 params");
                            return rec;
                        }
                        else
                        {
                            Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanB: param construction failed (config=" + (configProvider != null) + " behaviour=" + (behaviour != null) + ")");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanB: InitializeAsync method not found or param count != 3");
                    }
                    triedPlanB = true;
                }
                catch (Exception exB)
                {
                    Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanB failed: " + exB.Message);
                    triedPlanB = true;
                }
            }

            if (!triedPlanC)
            {
                try
                {
                    var rtType = FindRuntimeInitializerType();
                    if (rtType != null)
                    {
                        var rtInitMethod = rtType.GetMethod("InitializeAsync", BindingFlags.Public | BindingFlags.Static);
                        if (rtInitMethod != null && rtInitMethod.GetParameters().Length == 2)
                        {
                            var configProvider = ConstructConfigProvider("naninovel/configuration");
                            if (configProvider != null)
                            {
                                var args = new object[] { configProvider, null };
                                var taskObj = rtInitMethod.Invoke(null, args);
                                triedPlanC = true;
                                currentInitPlan = 3;
                                engineInitCalled = true;
                                engineInitCallFrame = frameCount;
                                engineInitWaitFrameCount = 0;
                                rec.NewValue = "PlanC RuntimeInitializer.InitializeAsync(2 params) invoked";
                                rec.Summary = "Called RuntimeInitializer.InitializeAsync(config, null) at frame " + frameCount;
                                StartUniTaskCoroutine(taskObj);
                                Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure: PlanC RuntimeInitializer.InitializeAsync invoked with 2 params");
                                return rec;
                            }
                            else
                            {
                                Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanC: config provider construction failed");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanC: RuntimeInitializer.InitializeAsync not found or param count != 2");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanC: RuntimeInitializer type not found");
                    }
                    triedPlanC = true;
                }
                catch (Exception exC)
                {
                    Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure PlanC failed: " + exC.Message);
                    triedPlanC = true;
                }
            }

            rec.NewValue = "All init plans failed. Tried: A=" + triedPlanA + " B=" + triedPlanB + " C=" + triedPlanC;
            rec.Summary = "No init method could be successfully invoked";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixNaninovelInitFailure: " + ex.Message); rec.NewValue = "Exception: " + ex.Message; }
        return rec;
    }

    private static FixChangeRecord FixTextPropertyNotSet(Transform dt)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = GetParentChain(dt);
        rec.PropertyName = "text";
        rec.OldValue = "empty";
        try
        {
            var graphic = dt.GetComponent<Graphic>();
            var textComp = graphic as Text;
            if (textComp == null) return null;
            if (string.IsNullOrEmpty(textComp.text))
            {
                if (enableScriptPlayerTriggerFix && !scriptPlayerExecCalled)
                {
                    bool engineReady = CheckEngineInitialized();
                    if (engineReady)
                    {
                        var spType = FindScriptPlayerType();
                        if (spType != null)
                        {
                            var execMethod = GetScriptPlayerExecMethod(spType);
                            if (execMethod == null && enableScriptPlayerMethodEnum)
                            {
                                execMethod = EnumerateAndSelectScriptPlayerExecMethod(spType);
                            }
                            var scriptPath = GetScriptResourcePath();
                            if (execMethod != null && scriptPath != null)
                            {
                                bool invoked = InvokeScriptPlayerExec(execMethod, scriptPath);
                                if (invoked)
                                {
                                    scriptPlayerExecCalled = true;
                                    scriptExecTriggered = true;
                                    rec.NewValue = "ScriptPlayer exec invoked, path=" + scriptPath + " method=" + execMethod.Name;
                                    rec.Summary = "Called ScriptPlayer." + execMethod.Name + "() at frame " + frameCount;
                                    textComp.SetVerticesDirty();
                                    return rec;
                                }
                            }
                            else if (execMethod == null)
                            {
                                Debug.LogWarning("[TEXTVIS] FixTextPropertyNotSet: no exec method found after enumeration, ScriptPlayer methods logged above");
                            }
                        }
                    }
                }
                textComp.text = " ";
                rec.NewValue = "space (placeholder)";
                rec.Summary = "Set placeholder text to trigger generation";
            }
            textComp.SetVerticesDirty();
            rec.NewValue = textComp.text != null ? textComp.text.Substring(0, System.Math.Min(30, textComp.text.Length)) : "null";
            rec.Summary = "SetVerticesDirty called";
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixTextPropertyNotSet: " + ex.Message); }
        return rec;
    }

    private static FixChangeRecord ApplyTextVisibilityFix(RootCauseResult cause, Transform dt, BaselineSnapshot b, DialogueTextVisibilitySnapshot snap)
    {
        if (cause == null || dt == null) return null;
        FixChangeRecord rec = null;
        try
        {
            switch (cause.PrimaryCause)
            {
                case RootCauseType.BlackText: rec = FixBlackText(dt, b); FixColorBlackOnly(dt); break;
                case RootCauseType.TextAlphaTooLow: rec = FixAlphaTooLow(dt, b); break;
                case RootCauseType.TextTemporalClear: rec = FixTemporalClear(dt, snap.TemporalTrace); break;
                case RootCauseType.RenderOcclusion: rec = FixRenderOcclusion(dt, snap.RenderOcclusion); break;
                case RootCauseType.PrinterBehaviorAnomaly: rec = FixPrinterBehaviorAnomaly(dt, snap.PrinterBehavior, b); break;
                case RootCauseType.MaterialMainTexNotSet: rec = FixMaterialMainTexFromFont(dt); break;
                case RootCauseType.MainTexMismatchWithFontTexture: rec = FixMaterialMainTexFromFont(dt); break;
                case RootCauseType.ShadowEffectBlack: rec = FixShadowEffectBlack(dt); break;
                case RootCauseType.FontTextureMissing: rec = FixFontTextureMissing(dt); break;
                case RootCauseType.ShaderModificationNotEffective: rec = FixShaderModificationNotEffective(dt); break;
                case RootCauseType.FontMaterialConfigError: rec = FixFontMaterialConfigError(dt); break;
                case RootCauseType.ClipRectZeroVector: rec = FixClipRectZeroVector(dt); break;
                case RootCauseType.ClipRectUpdateLogicDefect: rec = FixClipRectUpdateLogicDefect(dt); break;
                case RootCauseType.NaninovelInitFailure: rec = FixNaninovelInitFailure(dt); break;
                case RootCauseType.TextPropertyNotSet: rec = FixTextPropertyNotSet(dt); break;
                case RootCauseType.PlaceholderNotReplaced: rec = FixPlaceholderNotReplaced(dt); break;
                default: break;
            }
            if (rec == null && enableResourcePathFix && cause.ResourcePathMismatch && snap != null)
            {
                rec = FixResourcePathConfig(snap.ResourcePath);
            }
            if (cause.TMProShaderThresholdMissing)
            {
                Debug.LogWarning("[TEXTVIS] RevealableTMProText shader missing 60000 threshold check. Fix should have been applied during export stage (EnableTMProShaderThresholdFix). Zero clip rect may cause text clipping.");
            }
            if (cause.EngineInitFailure)
            {
                if (!engineInitConfirmed)
                {
                    Debug.LogWarning("[TEXTVIS] Naninovel engine init failure detected. Check: Elringus.Naninovel.Runtime.dll loading, resource path configuration, UITextPrinter instantiation. DLL is not modified by this fix.");
                }
            }
            if (cause.ComponentTypeMismatch)
            {
                Debug.LogWarning("[TEXTVIS] Component type mismatch detected. Runtime component type may differ from Naninovel config. Check: RevealableUIText vs RevealableTMProText, shader matching.");
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] ApplyTextVisibilityFix: " + ex.Message); }
        if (rec != null)
        {
            Debug.Log("[TEXTVIS] Applied fix: " + cause.PrimaryCause + " -> " + rec.Summary + " (old=" + rec.OldValue + " new=" + rec.NewValue + ")");
        }
        return rec;
    }

    private static FixChangeRecord FixResourcePathConfig(ResourcePathSnapshot snap)
    {
        var rec = new FixChangeRecord();
        rec.ObjectPath = "Naninovel.TextPrintersConfiguration";
        rec.PropertyName = "PathPrefix";
        rec.OldValue = snap.PathPrefix;
        try
        {
            Type configType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Naninovel.Configuration.TextPrintersConfiguration");
                if (t != null) { configType = t; break; }
            }
            if (configType == null)
            {
                Debug.LogWarning("[TEXTVIS] FixResourcePathConfig: TextPrintersConfiguration type not found");
                rec.Summary = "Config type not found";
                return rec;
            }
            string newPathPrefix = "myui/textprinters 1";
            var pathPrefixField = configType.GetField("PathPrefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pathPrefixField != null)
            {
                var instances = UnityEngine.Object.FindObjectsOfType(configType);
                if (instances != null && instances.Length > 0)
                {
                    pathPrefixField.SetValue(instances[0], newPathPrefix);
                    rec.NewValue = newPathPrefix;
                    rec.Summary = "PathPrefix updated to match actual resource path";
                    Debug.Log("[TEXTVIS] FixResourcePathConfig: changed PathPrefix from \"" + snap.PathPrefix + "\" to \"" + newPathPrefix + "\"");
                }
                else
                {
                    rec.Summary = "No config instance found";
                }
            }
            else
            {
                var pathPrefixProp = configType.GetProperty("PathPrefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pathPrefixProp != null)
                {
                    var instances = UnityEngine.Object.FindObjectsOfType(configType);
                    if (instances != null && instances.Length > 0)
                    {
                        pathPrefixProp.SetValue(instances[0], newPathPrefix, null);
                        rec.NewValue = newPathPrefix;
                        rec.Summary = "PathPrefix updated to match actual resource path";
                        Debug.Log("[TEXTVIS] FixResourcePathConfig: changed PathPrefix from \"" + snap.PathPrefix + "\" to \"" + newPathPrefix + "\"");
                    }
                    else
                    {
                        rec.Summary = "No config instance found";
                    }
                }
                else
                {
                    rec.Summary = "PathPrefix field/property not found";
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[TEXTVIS] FixResourcePathConfig: " + ex.Message); rec.Summary = "Failed: " + ex.Message; }
        return rec;
    }

    private static bool ShouldExecuteFixThisFrame(int frame, int earlyCount, int steadyInterval)
    {
        return frame <= earlyCount || frame % steadyInterval == 0;
    }

    private static bool VerifyTextVisibility(Transform dt)
    {
        if (dt == null) return false;
        var snap = CollectVisibilitySnapshot(dt);
        if (snap == null || !snap.IsValid) return false;
        bool visible = !snap.MaterialMainTex.MainTexIsNull
            && snap.FontTexturePixel.IsTextureValid
            && !snap.ClipRectUpdate.IsCharClipRectZero
            && !snap.TextProp.TextIsNullOrEmpty
            && snap.NaninovelInit.ActivePrinterNotNull
            && !snap.TextColor.IsBlack
            && snap.TextColor.TextCompColor.a > 0.01f
            && snap.TextProp.CharacterCountVisible > 0
            && !snap.RenderOcclusion.IsOccluded;
        if (visible) textVisConsecutiveVisibleFrames++;
        else textVisConsecutiveVisibleFrames = 0;
        return textVisConsecutiveVisibleFrames >= 60;
    }
}
""";
			File.WriteAllText(scriptPath, scriptContent);
			Logger.Info(LogCategory.Export, $"Generated DialogueRenderOrderFixer.cs at {scriptPath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to generate DialogueRenderOrderFixer.cs: {ex}");
		}
	}

	private static void GenerateRenderOrderDiagnosticScript(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string scriptPath = fileSystem.Path.Join(editorDir, "RenderOrderDiagnostic.cs");
			string scriptContent = """
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class RenderOrderDiagnostic
{
    [MenuItem("Tools/Render Order Diagnostic")]
    public static void Diagnose()
    {
        Debug.Log("=== Canvas Diagnostic ===");
        var canvases = Object.FindObjectsOfType<Canvas>();
        foreach (var c in canvases)
        {
            Debug.Log($"Canvas: {c.name}, sortingOrder={c.sortingOrder}, renderMode={c.renderMode}, planeDistance={c.planeDistance}, layer={c.gameObject.layer}");
        }

        Debug.Log("=== Camera Diagnostic ===");
        var cameras = Object.FindObjectsOfType<Camera>();
        foreach (var cam in cameras)
        {
            Debug.Log($"Camera: {cam.name}, depth={cam.depth}, cullingMask=0x{cam.cullingMask:X}, clearFlags={cam.clearFlags}, ortho={cam.orthographic}");
        }

        Debug.Log("=== Graphic Diagnostic ===");
        var graphics = Object.FindObjectsOfType<Graphic>();
        foreach (var g in graphics)
        {
            string matName = g.material != null && g.material.shader != null ? g.material.shader.name : "null";
            Debug.Log($"Graphic: {g.name}, active={g.gameObject.activeInHierarchy}, color=({g.color.r:F2},{g.color.g:F2},{g.color.b:F2},{g.color.a:F2}), mat={matName}, queue={g.material?.renderQueue ?? -1}");
        }
    }
}
""";
			File.WriteAllText(scriptPath, scriptContent);
			Logger.Info(LogCategory.Export, $"Generated RenderOrderDiagnostic.cs at {scriptPath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to generate RenderOrderDiagnostic.cs: {ex}");
		}
	}

	private static void GenerateBaselineCollectorScript(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string scriptPath = fileSystem.Path.Join(editorDir, "BaselineCollector.cs");
			string scriptContent = """
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class BaselineCollector
{
    [MenuItem("Tools/Collect Baseline")]
    public static void CollectBaseline()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BaselineCollector] Must be in PlayMode to collect runtime baseline. Enter PlayMode and try again.");
            return;
        }
        Type revealableTextType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Naninovel.UI.RevealableUIText");
            if (t != null) { revealableTextType = t; break; }
        }
        if (revealableTextType == null)
        {
            Debug.LogWarning("[BaselineCollector] RevealableUIText type not found, cannot collect baseline.");
            return;
        }
        var components = UnityEngine.Object.FindObjectsOfType(revealableTextType);
        if (components == null || components.Length == 0)
        {
            Debug.LogWarning("[BaselineCollector] No RevealableUIText components found in scene.");
            return;
        }
        var comp = components[0] as Component;
        if (comp == null) return;
        var graphic = comp as Graphic;
        if (graphic == null) return;
        var mat = graphic.material;
        var textComp = graphic as Text;
        string json = "{\n";
        json += "  \"baselineSource\": \"Runtime\",\n";
        json += "  \"objectPath\": \"" + GetPath(comp.transform) + "\",\n";
        json += "  \"materialShaderName\": \"" + (mat != null && mat.shader != null ? mat.shader.name : "null") + "\",\n";
        json += "  \"mainTexIsNull\": " + (mat == null || mat.GetTexture("_MainTex") == null).ToString().ToLower() + ",\n";
        if (mat != null && mat.GetTexture("_MainTex") != null) json += "  \"mainTexName\": \"" + mat.GetTexture("_MainTex").name + "\",\n";
        if (textComp != null)
        {
            json += "  \"fontName\": \"" + (textComp.font != null ? textComp.font.name : "null") + "\",\n";
            json += "  \"textLength\": " + (textComp.text != null ? textComp.text.Length : 0) + ",\n";
            if (textComp.font != null && textComp.font.material != null && textComp.font.material.mainTexture != null)
            {
                json += "  \"fontMaterialMainTexName\": \"" + textComp.font.material.mainTexture.name + "\",\n";
                var tex = textComp.font.material.mainTexture as Texture2D;
                if (tex != null) json += "  \"fontMaterialMainTexSize\": \"" + tex.width + "x" + tex.height + "\",\n";
            }
        }
        if (mat != null)
        {
            var charClip = mat.GetVector("_CharClipRect");
            var lineClip = mat.GetVector("_LineClipRect");
            json += "  \"charClipRect\": [" + charClip.x + "," + charClip.y + "," + charClip.z + "," + charClip.w + "],\n";
            json += "  \"lineClipRect\": [" + lineClip.x + "," + lineClip.y + "," + lineClip.z + "," + lineClip.w + "],\n";
        }
        Type engineType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Naninovel.Engine");
            if (t != null) { engineType = t; break; }
        }
        if (engineType != null)
        {
            var initProp = engineType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
            if (initProp != null)
            {
                try { json += "  \"engineInitialized\": " + ((bool)initProp.GetValue(null, null)).ToString().ToLower() + ",\n"; } catch { }
            }
        }
        json += "  \"frameCount\": " + Time.frameCount + "\n";
        json += "}";
        string outputPath = Path.Combine(Application.dataPath, "..", "BaselineSnapshot.json");
        File.WriteAllText(outputPath, json);
        Debug.Log("[BaselineCollector] Baseline snapshot written to: " + outputPath);
        Debug.Log("[BaselineCollector] Content:\n" + json);
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "";
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }
}
""";
			File.WriteAllText(scriptPath, scriptContent);
			Logger.Info(LogCategory.Export, $"Generated BaselineCollector.cs at {scriptPath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to generate BaselineCollector.cs: {ex}");
		}
	}

	private static void GenerateResourcePreloaderScript(string assetsPath, FileSystem fileSystem)
	{
		string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
		if (!fileSystem.Directory.Exists(editorDir))
		{
			Directory.CreateDirectory(editorDir);
		}

		string scriptPath = fileSystem.Path.Join(editorDir, "NaninovelResourcePreloader.cs");
		string scriptContent = """"
using UnityEngine;

public static class NaninovelResourcePreloader
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void PreloadResources()
    {
        Debug.Log("[NANI_PRELOAD] Pre-loading all Naninovel resources synchronously...");
        float startTime = Time.realtimeSinceStartup;

        Object[] naninovelResources = Resources.LoadAll<Object>("naninovel");
        Debug.Log($"[NANI_PRELOAD] Loaded {naninovelResources.Length} resources from 'naninovel' in {Time.realtimeSinceStartup - startTime:0.###}s");

        Object[] allResources = Resources.LoadAll<Object>("");
        Debug.Log($"[NANI_PRELOAD] Loaded {allResources.Length} total resources in {Time.realtimeSinceStartup - startTime:0.###}s");
    }
}
"""";

		File.WriteAllText(scriptPath, scriptContent);
		Logger.Info(LogCategory.Export, $"Generated NaninovelResourcePreloader.cs at {scriptPath}");
	}

	private static void InvalidateUnityLibraryCache(FullConfiguration settings, FileSystem fileSystem)
	{
		string libraryPath = fileSystem.Path.Join(settings.ProjectRootPath, "Library");
		if (fileSystem.Directory.Exists(libraryPath))
		{
			try
			{
				Directory.Delete(libraryPath, recursive: true);
				Logger.Info(LogCategory.Export, "Deleted Unity Library cache to force full reimport on next open.");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to delete Library cache: {ex.Message}");
			}
		}

		string tempPath = fileSystem.Path.Join(settings.ProjectRootPath, "Temp");
		if (fileSystem.Directory.Exists(tempPath))
		{
			try
			{
				Directory.Delete(tempPath, recursive: true);
				Logger.Info(LogCategory.Export, "Deleted Unity Temp cache to force full reimport on next open.");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to delete Temp cache: {ex.Message}");
			}
		}

		string objPath = fileSystem.Path.Join(settings.ProjectRootPath, "obj");
		if (fileSystem.Directory.Exists(objPath))
		{
			try
			{
				Directory.Delete(objPath, recursive: true);
				Logger.Info(LogCategory.Export, "Deleted Unity obj cache to force full reimport on next open.");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to delete obj cache: {ex.Message}");
			}
		}

		string logsPath = fileSystem.Path.Join(settings.ProjectRootPath, "Logs");
		if (fileSystem.Directory.Exists(logsPath))
		{
			try
			{
				Directory.Delete(logsPath, recursive: true);
				Logger.Info(LogCategory.Export, "Deleted Unity Logs to force full reimport on next open.");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to delete Logs: {ex.Message}");
			}
		}
	}

	private static void FixPrefabEffectColorBlack(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			int fixedCount = 0;
			var prefabFiles = fileSystem.Directory.EnumerateFiles(assetsPath, "*.prefab", System.IO.SearchOption.AllDirectories);
			foreach (var prefabPath in prefabFiles)
			{
				try
				{
					string content = fileSystem.File.ReadAllText(prefabPath);
					if (!content.Contains("m_EffectColor: {r: 0, g: 0, b: 0"))
						continue;
					string newContent = content;
					var matches = System.Text.RegularExpressions.Regex.Matches(newContent, @"m_EffectColor: \{r: 0, g: 0, b: 0, a: [0-9.]+\}");
					if (matches.Count > 0)
					{
						newContent = System.Text.RegularExpressions.Regex.Replace(newContent, @"m_EffectColor: \{r: 0, g: 0, b: 0, a: [0-9.]+\}", "m_EffectColor: {r: 1, g: 1, b: 1, a: 0}");
						fileSystem.File.WriteAllText(prefabPath, newContent);
						fixedCount++;
					}
				}
				catch { }
			}
			Logger.Info(LogCategory.Export, $"FixPrefabEffectColorBlack: fixed m_EffectColor from black to transparent in {fixedCount} prefab(s).");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"FixPrefabEffectColorBlack failed: {ex}");
		}
	}

	private static void FixCorruptedPngFiles(string assetsPath, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(assetsPath, "Resources");
		if (!fileSystem.Directory.Exists(resourcesPath))
		{
			return;
		}

		byte[] validPlaceholderPng = CreateValidPlaceholderPng();
		byte[] backgroundPlaceholderPng = CreateSizedPlaceholderPng(128, 128);
		int fixedCount = 0;

		foreach (string pngFile in fileSystem.Directory.EnumerateFiles(resourcesPath, "*.png", System.IO.SearchOption.AllDirectories))
		{
			if (!IsPngFileValid(pngFile))
			{
				try
				{
					bool isBackground = pngFile.Contains("Backgrounds", StringComparison.OrdinalIgnoreCase);
					File.WriteAllBytes(pngFile, isBackground ? backgroundPlaceholderPng : validPlaceholderPng);
					fixedCount++;
					if (isBackground)
					{
						Logger.Info(LogCategory.Export, $"Fixed corrupted background PNG with 128x128 placeholder: {pngFile}");
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"Failed to fix corrupted PNG {pngFile}: {ex.Message}");
				}
			}
		}

		if (fixedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Fixed {fixedCount} corrupted PNG file(s) with valid placeholder.");
		}
	}

	private static byte[] CreateSizedPlaceholderPng(int width, int height)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
		writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		byte[] ihdr = new byte[13];
		ihdr[0] = (byte)((width >> 24) & 0xFF);
		ihdr[1] = (byte)((width >> 16) & 0xFF);
		ihdr[2] = (byte)((width >> 8) & 0xFF);
		ihdr[3] = (byte)(width & 0xFF);
		ihdr[4] = (byte)((height >> 24) & 0xFF);
		ihdr[5] = (byte)((height >> 16) & 0xFF);
		ihdr[6] = (byte)((height >> 8) & 0xFF);
		ihdr[7] = (byte)(height & 0xFF);
		ihdr[8] = 8;
		ihdr[9] = 6;
		ihdr[10] = 0;
		ihdr[11] = 0;
		ihdr[12] = 0;
		WritePngChunk(writer, "IHDR", ihdr);
		byte[] idatData = CreateSizedIdatData(width, height);
		WritePngChunk(writer, "IDAT", idatData);
		WritePngChunk(writer, "IEND", []);
		return stream.ToArray();
	}

	private static byte[] CreateSizedIdatData(int width, int height)
	{
		using MemoryStream rawStream = new();
		using BinaryWriter rawWriter = new(rawStream, System.Text.Encoding.ASCII, leaveOpen: true);
		for (int y = 0; y < height; y++)
		{
			rawWriter.Write((byte)0);
			for (int x = 0; x < width; x++)
			{
				rawWriter.Write((byte)128);
				rawWriter.Write((byte)128);
				rawWriter.Write((byte)128);
				rawWriter.Write((byte)255);
			}
		}
		byte[] rawData = rawStream.ToArray();
		using MemoryStream compressedStream = new();
		using (System.IO.Compression.DeflateStream deflateStream = new(compressedStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
		{
			deflateStream.Write(rawData, 0, rawData.Length);
		}
		byte[] deflateData = compressedStream.ToArray();
		byte[] zlibData = new byte[2 + deflateData.Length + 4];
		zlibData[0] = 0x78;
		zlibData[1] = 0x9C;
		Buffer.BlockCopy(deflateData, 0, zlibData, 2, deflateData.Length);
		uint adler32 = ComputeAdler32(rawData);
		zlibData[^4] = (byte)((adler32 >> 24) & 0xFF);
		zlibData[^3] = (byte)((adler32 >> 16) & 0xFF);
		zlibData[^2] = (byte)((adler32 >> 8) & 0xFF);
		zlibData[^1] = (byte)(adler32 & 0xFF);
		return zlibData;
	}

	private static byte[] CreateValidPlaceholderPng()
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
		writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		WritePngChunk(writer, "IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0 });
		byte[] idatData = CreateValidIdatData();
		WritePngChunk(writer, "IDAT", idatData);
		WritePngChunk(writer, "IEND", []);
		return stream.ToArray();
	}

	private static byte[] CreateValidIdatData()
	{
		using MemoryStream rawStream = new();
		using BinaryWriter rawWriter = new(rawStream, System.Text.Encoding.ASCII, leaveOpen: true);
		rawWriter.Write((byte)0);
		rawWriter.Write(new byte[4]);
		byte[] rawData = rawStream.ToArray();
		using MemoryStream compressedStream = new();
		using (System.IO.Compression.DeflateStream deflateStream = new(compressedStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
		{
			deflateStream.Write(rawData, 0, rawData.Length);
		}
		byte[] deflateData = compressedStream.ToArray();
		byte[] zlibData = new byte[2 + deflateData.Length + 4];
		zlibData[0] = 0x78;
		zlibData[1] = 0x9C;
		Buffer.BlockCopy(deflateData, 0, zlibData, 2, deflateData.Length);
		uint adler32 = ComputeAdler32(rawData);
		zlibData[^4] = (byte)((adler32 >> 24) & 0xFF);
		zlibData[^3] = (byte)((adler32 >> 16) & 0xFF);
		zlibData[^2] = (byte)((adler32 >> 8) & 0xFF);
		zlibData[^1] = (byte)(adler32 & 0xFF);
		return zlibData;
	}

	private static uint ComputeAdler32(byte[] data)
	{
		const uint MOD_ADLER = 65521;
		uint a = 1, b = 0;
		for (int i = 0; i < data.Length; i++)
		{
			a = (a + data[i]) % MOD_ADLER;
			b = (b + a) % MOD_ADLER;
		}
		return (b << 16) | a;
	}

	private static void WritePngChunk(BinaryWriter writer, string type, byte[] data)
	{
		writer.Write(System.BitConverter.IsLittleEndian ? System.BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length)) : System.BitConverter.GetBytes(data.Length));
		byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
		writer.Write(typeBytes);
		writer.Write(data);
		using MemoryStream crcStream = new();
		crcStream.Write(typeBytes, 0, 4);
		crcStream.Write(data, 0, data.Length);
		uint crc = ComputeCrc32(crcStream.ToArray());
		byte[] crcBytes = new byte[4];
		crcBytes[0] = (byte)((crc >> 24) & 0xFF);
		crcBytes[1] = (byte)((crc >> 16) & 0xFF);
		crcBytes[2] = (byte)((crc >> 8) & 0xFF);
		crcBytes[3] = (byte)(crc & 0xFF);
		writer.Write(crcBytes);
	}

	private static uint[]? crcTable;
	private static uint ComputeCrc32(byte[] data)
	{
		crcTable ??= BuildCrc32Table();
		uint crc = 0xFFFFFFFF;
		for (int i = 0; i < data.Length; i++)
		{
			crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		}
		return crc ^ 0xFFFFFFFF;
	}

	private static uint[] BuildCrc32Table()
	{
		uint[] table = new uint[256];
		for (uint i = 0; i < 256; i++)
		{
			uint c = i;
			for (int j = 0; j < 8; j++)
			{
				c = (c & 1) != 0 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
			}
			table[i] = c;
		}
		return table;
	}

	private static bool IsPngFileValid(string path)
	{
		try
		{
			byte[] data = File.ReadAllBytes(path);
			if (data.Length < 8)
			{
				return false;
			}
			if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
			{
				return false;
			}
			int pos = 8;
			bool hasIdat = false;
			while (pos + 8 <= data.Length)
			{
				int chunkLen = System.BitConverter.ToInt32(data, pos);
				if (System.BitConverter.IsLittleEndian)
				{
					chunkLen = System.Net.IPAddress.HostToNetworkOrder(chunkLen);
				}
				if (chunkLen < 0 || pos + 12 + chunkLen > data.Length)
				{
					return false;
				}
				string chunkType = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
				if (chunkType == "IDAT")
				{
					hasIdat = true;
					byte[] idatData = new byte[chunkLen];
					Buffer.BlockCopy(data, pos + 8, idatData, 0, chunkLen);
					using MemoryStream decompressStream = new();
					using (System.IO.Compression.DeflateStream deflateStream = new(new MemoryStream(idatData, 2, chunkLen - 2), System.IO.Compression.CompressionMode.Decompress))
					{
						deflateStream.CopyTo(decompressStream);
					}
				}
				pos += 12 + chunkLen;
				if (chunkType == "IEND")
				{
					break;
				}
			}
			return hasIdat;
		}
		catch
		{
			return false;
		}
	}

	private static bool SyncNaninovelDlls(GameData gameData, FullConfiguration settings, string pluginsPath, FileSystem fileSystem)
	{
		string? originalManagedPath = gameData.PlatformStructure?.ManagedPath;
		if (string.IsNullOrEmpty(originalManagedPath) || !Directory.Exists(originalManagedPath))
		{
			Logger.Info(LogCategory.Export, $"PlatformStructure.ManagedPath not available ({originalManagedPath ?? "null"}), trying SourceDataPath fallback");
			originalManagedPath = LocateManagedPath(settings, fileSystem);
		}

		if (string.IsNullOrEmpty(originalManagedPath) || !Directory.Exists(originalManagedPath))
		{
			Logger.Warning(LogCategory.Export, $"Could not locate original Managed path for DLL sync. Last tried: {originalManagedPath}");
			return false;
		}

		Logger.Info(LogCategory.Export, $"Syncing Naninovel DLLs from original Managed path: {originalManagedPath}");

		bool anySynced = false;
		foreach (string dllName in NaninovelDllsToSync)
		{
			string exportedDllPath = fileSystem.Path.Join(pluginsPath, dllName);
			string originalDllPath = Path.Join(originalManagedPath, dllName);

			if (!File.Exists(originalDllPath))
			{
				Logger.Warning(LogCategory.Export, $"Original DLL not found: {originalDllPath}");
				continue;
			}

			if (!fileSystem.File.Exists(exportedDllPath))
			{
				Logger.Warning(LogCategory.Export, $"Exported DLL not found: {exportedDllPath}");
				continue;
			}

			string originalMd5 = ComputeMD5(originalDllPath);
			string exportedMd5 = ComputeMD5(exportedDllPath);

			if (originalMd5 == exportedMd5)
			{
				Logger.Info(LogCategory.Export, $"DLL '{dllName}' already in sync (MD5: {originalMd5})");
				continue;
			}

			Logger.Info(LogCategory.Export, $"DLL '{dllName}' differs (original MD5: {originalMd5}, exported MD5: {exportedMd5}), replacing with original DLL to preserve runtime behavior");
			File.Copy(originalDllPath, exportedDllPath, overwrite: true);
			anySynced = true;
		}

		return anySynced;
	}

	private static string? LocateManagedPath(FullConfiguration settings, FileSystem fileSystem)
	{
		if (!string.IsNullOrEmpty(settings.SourceDataPath))
		{
			string sourcePath = settings.SourceDataPath;
			if (fileSystem.Directory.Exists(sourcePath))
			{
				string directPath = Path.Join(sourcePath, "Managed");
				if (Directory.Exists(directPath))
				{
					return directPath;
				}
			}

			string? parentDir = Path.GetDirectoryName(sourcePath);
			if (parentDir is not null)
			{
				string siblingPath = Path.Join(parentDir, Path.GetFileName(sourcePath), "Managed");
				if (Directory.Exists(siblingPath))
				{
					return siblingPath;
				}
			}
		}

		return null;
	}

	private static string ComputeMD5(string filePath)
	{
		using MD5 md5 = MD5.Create();
		using FileStream stream = File.OpenRead(filePath);
		byte[] hash = md5.ComputeHash(stream);
		return Convert.ToHexString(hash);
	}

	/// <summary>
	/// 2026-10-23 防回归：导出后 touch 全部运行时 .cs 脚本时间戳。
	/// 根因：导出覆盖已有 Library 项目时，AssetImportState 缓存认为脚本未变化跳过重导入，
	/// 而 ScriptAssemblies 部分重建（如 Editor 重编译）后主程序集（Assembly-CSharp.dll）不再生成，
	/// 造成 174 个 Missing Script + 游戏无法进入 UI（ResolutionMaster/AutoDisableControl 等全失效）。
	/// touch 时间戳强制 Unity 打开项目时全量重编译运行时程序集。
	/// </summary>
	private static void TouchAllRuntimeScripts(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			int touched = 0;
			string[] scriptRoots =
			[
				fileSystem.Path.Join(assetsPath, "Scripts"),
				fileSystem.Path.Join(assetsPath, "Plugins"),
			];
			foreach (string root in scriptRoots)
			{
				if (!fileSystem.Directory.Exists(root))
				{
					continue;
				}
				foreach (string csFile in fileSystem.Directory.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
				{
					try
					{
						File.SetLastWriteTimeUtc(csFile, DateTime.UtcNow);
						touched++;
					}
					catch
					{
					}
				}
			}
			Logger.Info(LogCategory.Export, $"TouchAllRuntimeScripts: touched {touched} .cs files (force recompile of runtime assemblies on next Unity open).");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"TouchAllRuntimeScripts failed: {ex.Message}");
		}
	}

	private static void FixResourceFolderCasing(string assetsPath, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(assetsPath, ResourcesFolderName);
		Logger.Info(LogCategory.Export, $"FixResourceFolderCasing: resourcesPath={resourcesPath}, exists={fileSystem.Directory.Exists(resourcesPath)}");
		if (!fileSystem.Directory.Exists(resourcesPath))
		{
			return;
		}

		string? existingFolder = null;
		foreach (string dir in fileSystem.Directory.EnumerateDirectories(resourcesPath))
		{
			string name = fileSystem.Path.GetFileName(dir);
			if (string.Equals(name, UnityCommonFolderName, StringComparison.OrdinalIgnoreCase))
			{
				existingFolder = dir;
				Logger.Info(LogCategory.Export, $"FixResourceFolderCasing: found folder '{name}' at '{dir}'");
				break;
			}
		}

		if (existingFolder is null)
		{
			return;
		}

		string actualName = fileSystem.Path.GetFileName(existingFolder);
		if (string.Equals(actualName, UnityCommonFolderName, StringComparison.Ordinal))
		{
			return;
		}

		string expectedPath = fileSystem.Path.Join(resourcesPath, UnityCommonFolderName);
		try
		{
			string tempPath = fileSystem.Path.Join(resourcesPath, UnityCommonFolderName + "_temp_" + Guid.NewGuid().ToString("N")[..8]);
			Directory.Move(existingFolder, tempPath);
			Directory.Move(tempPath, expectedPath);
			Logger.Info(LogCategory.Export, $"Renamed resource folder '{actualName}' to '{UnityCommonFolderName}' for Resources.Load casing match.");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to rename resource folder '{actualName}' to '{UnityCommonFolderName}': {ex.Message}");
		}
	}

	private static void PatchNaninovelDll(string dllPath, NaninovelPatchSettings patchSettings)
	{
		ModuleDefinition module = ModuleDefinition.FromFile(dllPath);

		TypeDefinition? projectResources = module.GetAllTypes().FirstOrDefault(t => t.Name == ProjectResourcesTypeName);
		if (projectResources is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResources type not found in Naninovel DLL");
			return;
		}

		MethodDefinition? getMethod = projectResources.Methods.FirstOrDefault(m => m.Name == GetMethodName && m.IsStatic);
		if (getMethod is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResources.Get() method not found");
			return;
		}

		MethodDefinition? locateAllMethod = projectResources.Methods.FirstOrDefault(m => m.Name == LocateAllResourcesMethodName);
		if (locateAllMethod is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResources.LocateAllResources() method not found");
			return;
		}

		PatchGetMethod(module, projectResources, getMethod);
		PatchInitializationUI(module);
		PatchProjectResourceProviderConstructor(module);
		PatchIOUtils(module);
		PatchLocalizationLoadState(module);
		if (patchSettings.EnableUIManagerInstantiatePatch)
		{
			PatchUIManagerInstantiateUIs(module);
		}
		else
		{
			Logger.Info(LogCategory.Export, "UIManager InstantiateUIs patching disabled, skipping.");
		}

 		PatchPostInitTaskLogging(module);
 		// R3: PatchAllInitializeAsyncLogging (NANI_UIINIT trace) removed 2026-10-22 — log noise reduction for 0-error target.
 		PatchCGGalleryPanelInit(module);
 		LogProjectResourceLoaderStructure(module);
 		// R3: trace-log patches removed 2026-10-22 — diagnostics mission complete, log noise reduction for 0-error target.
 		PatchLoadAllAsyncNullCheck(module);

		if (patchSettings.EnableScriptPlayerWaitPatch)
		{
			PatchScriptPlayerWaitMethods(module);
		}
		else
		{
			Logger.Info(LogCategory.Export, "ScriptPlayer wait methods patching disabled, skipping.");
		}

		if (patchSettings.EnableSpriteRendererPatch)
		{
			PatchSpriteRendererUpdate(module, patchSettings);
		}
		else
		{
			Logger.Info(LogCategory.Export, "SpriteRenderer patching disabled, skipping.");
		}

		string tempPath = dllPath + ".tmp";
		module.Write(tempPath);
		File.Copy(tempPath, dllPath, overwrite: true);
		File.Delete(tempPath);
	}

	private static void PatchUniRxAsyncDll(string dllPath, NaninovelPatchSettings patchSettings)
	{
		Logger.Info(LogCategory.Export, "PatchUniRxAsyncDll: patching YieldAwaitable+Awaiter to call continuations synchronously...");

		ModuleDefinition module = ModuleDefinition.FromFile(dllPath);

		TypeDefinition? awaiterType = null;
		IMethodDescriptor? actionInvoke = null;

		foreach (TypeDefinition t in module.GetAllTypes())
		{
			if (t.Name?.Value == "Awaiter" && t.DeclaringType?.Name?.Value == "YieldAwaitable")
			{
				awaiterType = t;
			}

			if (actionInvoke is null)
			{
				foreach (MethodDefinition m in t.Methods)
				{
					if (m.CilMethodBody is null) continue;
					foreach (CilInstruction instr in m.CilMethodBody.Instructions)
					{
						if (instr.Operand is IMethodDescriptor md && md.Name?.ToString() == "Invoke" && md.DeclaringType?.Name?.ToString() == "Action")
						{
							actionInvoke = md;
							break;
						}
					}
					if (actionInvoke is not null) break;
				}
			}
			if (actionInvoke is not null && awaiterType is not null) break;
		}

		if (awaiterType is null)
		{
			Logger.Warning(LogCategory.Export, "PatchUniRxAsyncDll: YieldAwaitable+Awaiter not found");
			return;
		}

		if (actionInvoke is null)
		{
			Logger.Warning(LogCategory.Export, "PatchUniRxAsyncDll: Action.Invoke not found");
			return;
		}

		int patchedCount = 0;
		int skippedCount = 0;
		foreach (MethodDefinition m in awaiterType.Methods)
		{
			if (m.Name?.Value is "UnsafeOnCompleted" or "OnCompleted")
			{
				if (patchSettings.LogMethodBodySummary)
				{
					LogMethodBodySummary(m, "BEFORE");
				}

				if (patchSettings.EnableIdempotencyCheck && m.CilMethodBody is not null)
				{
					CilInstructionCollection existingInstrs = m.CilMethodBody.Instructions;
					if (existingInstrs.Count == 3 &&
						existingInstrs[0].OpCode == CilOpCodes.Ldarg_1 &&
						existingInstrs[1].OpCode == CilOpCodes.Callvirt &&
						existingInstrs[2].OpCode == CilOpCodes.Ret)
					{
						skippedCount++;
						continue;
					}
				}
				m.CilMethodBody = new CilMethodBody();
				m.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg_1);
				m.CilMethodBody.Instructions.Add(CilOpCodes.Callvirt, actionInvoke);
				m.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
				m.CilMethodBody.MaxStack = 1;
				patchedCount++;

				if (patchSettings.LogMethodBodySummary)
				{
					LogMethodBodySummary(m, "AFTER");
				}
			}
		}

		string tempPath = dllPath + ".tmp";
		module.Write(tempPath);
		File.Copy(tempPath, dllPath, overwrite: true);
		File.Delete(tempPath);

		Logger.Info(LogCategory.Export, $"PatchUniRxAsyncDll: patched {patchedCount} methods, skipped {skippedCount} already-patched methods");
	}

	private static void PatchRunAsyncToSync(ModuleDefinition module)
	{
		Logger.Info(LogCategory.Export, "PatchRunAsyncToSync: patching ProjectResourceLoader.RunAsync to use synchronous Resources.Load...");
		IMethodDescriptor? debugLog = null, resLoad = null, completedTask = null, isNullOrEmpty = null, concat3 = null;
		IMethodDescriptor? setResult = null, getTypeFromHandle = null;
		IFieldDescriptor? rootPath = null, pathField = null;

		foreach (TypeDefinition t in module.GetAllTypes())
		{
			foreach (MethodDefinition m in t.Methods)
			{
				if (m.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction i in m.CilMethodBody.Instructions)
				{
					if (i.Operand is IMethodDescriptor md)
					{
						string? n = md.Name, dn = md.DeclaringType?.Name;
						if (dn == "Debug" && n == "Log" && debugLog is null) debugLog = md;
						if (dn == "Resources" && n == "Load" && md.Signature?.ParameterTypes.Count == 1 && resLoad is null) resLoad = md;
						if (n == "get_CompletedTask" && completedTask is null) completedTask = md;
						if (n == "IsNullOrEmpty" && isNullOrEmpty is null) isNullOrEmpty = md;
						if (n == "Concat" && md.Signature?.ParameterTypes.Count == 3 && concat3 is null) concat3 = md;
						if (n == "SetResult" && md.Signature?.ParameterTypes.Count == 1 && setResult is null) setResult = md;
						if (n == "GetTypeFromHandle" && getTypeFromHandle is null) getTypeFromHandle = md;
					}
					if (i.Operand is IFieldDescriptor fd)
					{
						string? fn = fd.Name, ftn = fd.DeclaringType?.Name;
						if (fn == "RootPath" && rootPath is null) rootPath = fd;
						if (fn == "Path" && ftn?.Contains("ResourceRunner") == true && pathField is null) pathField = fd;
					}
				}
			}
		}

		if (debugLog is null || resLoad is null || completedTask is null || isNullOrEmpty is null || concat3 is null || setResult is null || rootPath is null || pathField is null)
		{
			Logger.Warning(LogCategory.Export, "PatchRunAsyncToSync: missing method/field references, skipping");
			return;
		}

		int patched = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.ToString().Contains("ProjectResourceLoader") != true || type.GenericParameters.Count == 0)
			{
				continue;
			}

			MethodDefinition? runAsync = type.Methods.FirstOrDefault(m => m.Name == "RunAsync");
			if (runAsync is null)
			{
				continue;
			}

			CilMethodBody body = new();
			CilInstructionCollection il = body.Instructions;

			CilLocalVariable localPath = new(module.CorLibTypeFactory.String);
			CilLocalVariable localResult = new(module.CorLibTypeFactory.Object);
			body.LocalVariables.Add(localPath);
			body.LocalVariables.Add(localResult);

			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldfld, rootPath);
			il.Add(CilOpCodes.Call, isNullOrEmpty);
			il.Add(new CilInstruction(CilOpCodes.Brfalse_S, null));
			int br1 = il.Count - 1;

			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldfld, pathField);
			il.Add(new CilInstruction(CilOpCodes.Br_S, null));
			int br2 = il.Count - 1;

			int concatStart = il.Count;
			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldfld, rootPath);
			il.Add(CilOpCodes.Ldstr, "/");
			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldfld, pathField);
			il.Add(CilOpCodes.Call, concat3);

			il.Add(CilOpCodes.Stloc_0);
			int storeIdx = il.Count - 1;

			il[br1].Operand = il[concatStart].CreateLabel();
			il[br2].Operand = il[storeIdx].CreateLabel();

			il.Add(CilOpCodes.Ldloc_0);
			il.Add(CilOpCodes.Call, resLoad);
			il.Add(CilOpCodes.Stloc_1);

			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldloc_1);
			il.Add(CilOpCodes.Call, setResult);

			il.Add(CilOpCodes.Ldstr, "NANI_PATCH: RunAsync sync COMPLETED");
			il.Add(CilOpCodes.Call, debugLog);
			il.Add(CilOpCodes.Call, completedTask);
			il.Add(CilOpCodes.Ret);

			body.MaxStack = 4;

			runAsync.CilMethodBody = body;
			patched++;
			Logger.Info(LogCategory.Export, $"PatchRunAsyncToSync: patched {type.FullName}");
		}

		Logger.Info(LogCategory.Export, $"PatchRunAsyncToSync: patched {patched} ProjectResourceLoader.RunAsync method(s) to use synchronous Resources.Load");
	}

	private static void PatchInitializationUI(ModuleDefinition module)
	{
#pragma warning disable CS8602
		TypeDefinition? runtimeInitType = module.GetAllTypes().FirstOrDefault(t => t.Name == "RuntimeInitializer");
		if (runtimeInitType is null)
		{
			Logger.Warning(LogCategory.Export, "RuntimeInitializer type not found in Naninovel DLL");
			return;
		}

		TypeDefinition? stateMachineType = runtimeInitType.NestedTypes.FirstOrDefault(t => t.Name.Contains("InitializeAsync"));
		if (stateMachineType is null)
		{
			Logger.Warning(LogCategory.Export, "InitializeAsync state machine type not found");
			return;
		}

		MethodDefinition? moveNextMethod = stateMachineType.Methods.FirstOrDefault(m => m.Name == "MoveNext");
		if (moveNextMethod is null || moveNextMethod.CilMethodBody is null)
		{
			Logger.Warning(LogCategory.Export, "MoveNext method not found or has no CIL body");
			return;
		}

		FieldDefinition? initUIField = stateMachineType.Fields.FirstOrDefault(f => f.Name.Contains("initializationUI"));
#pragma warning restore CS8602
		if (initUIField is null)
		{
			Logger.Warning(LogCategory.Export, "initializationUI field not found in state machine");
			return;
		}

		CilInstructionCollection instructions = moveNextMethod.CilMethodBody.Instructions;

		int stfldIndex = -1;
		for (int i = 1; i < instructions.Count; i++)
		{
			if (instructions[i].OpCode == CilOpCodes.Stfld && instructions[i].Operand is FieldDefinition fd && fd == initUIField)
			{
				if (instructions[i - 1].Operand is IMethodDescriptor prevMethod && prevMethod.Name?.Value == "Instantiate")
				{
					stfldIndex = i;
					break;
				}
			}
		}

		if (stfldIndex == -1)
		{
			for (int i = instructions.Count - 1; i >= 0; i--)
			{
				if (instructions[i].OpCode == CilOpCodes.Stfld && instructions[i].Operand is FieldDefinition fd && fd == initUIField)
				{
					stfldIndex = i;
					break;
				}
			}
		}

		if (stfldIndex == -1)
		{
			Logger.Warning(LogCategory.Export, "stfld instruction for initializationUI after Instantiate not found");
			return;
		}

		IMethodDescriptor? getGameObjectMethod = null;
		IMethodDescriptor? dontDestroyMethod = null;
		foreach (CilInstruction instruction in instructions)
		{
			if (instruction.Operand is IMethodDescriptor methodDesc)
			{
				if (methodDesc.Name?.Value == "get_gameObject" && methodDesc.DeclaringType?.FullName?.Contains("Component") == true)
				{
					getGameObjectMethod ??= methodDesc;
				}
				if (methodDesc.Name?.Value == "DontDestroyOnLoad")
				{
					dontDestroyMethod ??= methodDesc;
				}
			}
		}

		if (getGameObjectMethod is null)
		{
			Logger.Warning(LogCategory.Export, "Component.get_gameObject method reference not found in MoveNext IL");
			return;
		}

		if (dontDestroyMethod is null)
		{
			foreach (var type in module.GetAllTypes())
			{
				foreach (var method in type.Methods)
				{
					if (method.CilMethodBody is not null)
					{
						foreach (var instr in method.CilMethodBody.Instructions)
						{
							if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "DontDestroyOnLoad")
							{
								dontDestroyMethod = md;
								break;
							}
						}
					}
					if (dontDestroyMethod is not null) break;
				}
				if (dontDestroyMethod is not null) break;
			}
		}

		if (dontDestroyMethod is null)
		{
			Logger.Warning(LogCategory.Export, "GameObject.DontDestroyOnLoad method could not be resolved");
			return;
		}

		foreach (CilInstruction instr in instructions)
		{
			if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "DontDestroyOnLoad")
			{
				Logger.Info(LogCategory.Export, "InitializeAsync already patched with DontDestroyOnLoad, skipping");
				return;
			}
		}

		instructions.Insert(stfldIndex + 1, CilOpCodes.Ldarg_0);
		instructions.Insert(stfldIndex + 2, CilOpCodes.Ldfld, initUIField);
		instructions.Insert(stfldIndex + 3, CilOpCodes.Callvirt, getGameObjectMethod);
		instructions.Insert(stfldIndex + 4, CilOpCodes.Call, dontDestroyMethod);

		Logger.Info(LogCategory.Export, "Patched InitializeAsync to call DontDestroyOnLoad on EngineInitializationUI");
	}

	private static void PatchProjectResourceProviderConstructor(ModuleDefinition module)
	{
		TypeDefinition? providerType = module.GetAllTypes().FirstOrDefault(t => t.Name == "ProjectResourceProvider");
		if (providerType is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResourceProvider type not found in Naninovel DLL");
			return;
		}

		bool hasParameterlessCtor = providerType.Methods.Any(m => m.Name == ".ctor" && m.Parameters.Count == 0);
		if (hasParameterlessCtor)
		{
			Logger.Info(LogCategory.Export, "ProjectResourceProvider already has a parameterless constructor, skipping.");
			return;
		}

		MethodDefinition? existingCtor = providerType.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1);
		if (existingCtor is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResourceProvider constructor with string parameter not found");
			return;
		}

		MethodSignature voidSignature = MethodSignature.CreateInstance(module.CorLibTypeFactory.Void);
		MethodDefinition newCtor = new(".ctor", existingCtor.Attributes, voidSignature);
		newCtor.CilMethodBody = new CilMethodBody();
		newCtor.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg_0);
		newCtor.CilMethodBody.Instructions.Add(CilOpCodes.Ldnull);
		newCtor.CilMethodBody.Instructions.Add(CilOpCodes.Call, existingCtor);
		newCtor.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
		providerType.Methods.Add(newCtor);

		Logger.Info(LogCategory.Export, "Added parameterless constructor to ProjectResourceProvider");
	}

	private static void PatchGetMethod(ModuleDefinition module, TypeDefinition projectResources, MethodDefinition getMethod)
	{
		// Original IL:
		//   call Application.get_isEditor()
		//   brtrue.s IL_0013
		//   ldstr "UnityCommon/ProjectResources"
		//   call Resources.Load<ProjectResources>(string)
		//   br.s IL_0018
		//   IL_0013: call ScriptableObject.CreateInstance<ProjectResources>()
		//   IL_0018: stloc.0
		//   call Application.get_isEditor()
		//   brfalse.s IL_0026
		//   ldloc.0
		//   callvirt LocateAllResources()
		//   IL_0026: ldloc.0
		//   ret
		//
		// Patched IL (always load from Resources, skip LocateAllResources):
		//   ldstr "UnityCommon/ProjectResources"
		//   call Resources.Load<ProjectResources>(string)
		//   ret

		// Find the Resources.Load<T> method reference used in the original IL
		IMethodDescriptor? resourcesLoadMethod = null;
		foreach (CilInstruction instruction in getMethod.CilMethodBody!.Instructions)
		{
			if (instruction.Operand is IMethodDescriptor methodDesc &&
			    methodDesc.Name?.Value == "Load" &&
			    methodDesc.DeclaringType?.FullName?.Contains("Resources") == true)
			{
				resourcesLoadMethod = methodDesc;
				break;
			}
		}

		if (resourcesLoadMethod is null)
		{
			Logger.Warning(LogCategory.Export, "Resources.Load method reference not found in Get() IL");
			return;
		}

		// Find the string literal "UnityCommon/ProjectResources" used in the original IL
		string? resourcePath = null;
		foreach (CilInstruction instruction in getMethod.CilMethodBody.Instructions)
		{
			if (instruction.OpCode == CilOpCodes.Ldstr && instruction.Operand is string s)
			{
				resourcePath = s;
				break;
			}
		}

		if (resourcePath is null)
		{
			Logger.Warning(LogCategory.Export, "Resource path string literal not found in Get() IL");
			return;
		}

		// Replace the method body
		getMethod.CilMethodBody = new CilMethodBody();
		CilInstructionCollection instructions = getMethod.CilMethodBody.Instructions;

		instructions.Add(CilOpCodes.Ldstr, resourcePath);
		instructions.Add(CilOpCodes.Call, resourcesLoadMethod);
		instructions.Add(CilOpCodes.Ret);

		Logger.Info(LogCategory.Export, $"Patched ProjectResources.Get() to always call Resources.Load(\"{resourcePath}\")");
	}

	private static void PatchIOUtils(ModuleDefinition module)
	{
		TypeDefinition? ioUtilsType = module.GetAllTypes().FirstOrDefault(t => t.Name == "IOUtils");
		if (ioUtilsType is null)
		{
			Logger.Warning(LogCategory.Export, "IOUtils type not found in Naninovel DLL");
			return;
		}

		IMethodDescriptor? fileReadAllText = null;
		IMethodDescriptor? fileWriteAllText = null;
		IMethodDescriptor? fileReadAllBytes = null;
		IMethodDescriptor? fileWriteAllBytes = null;
		IMethodDefOrRef? fromResultMethod = null;
		IMethodDescriptor? getCompletedTask = null;

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md)
					{
						continue;
					}

					string? methodName = md.Name?.Value;
					string? declTypeName = md.DeclaringType?.Name;

					if (declTypeName == "File")
					{
						if (methodName == "ReadAllText" && fileReadAllText is null)
						{
							fileReadAllText = md;
						}
						else if (methodName == "WriteAllText" && fileWriteAllText is null)
						{
							fileWriteAllText = md;
						}
						else if (methodName == "ReadAllBytes" && fileReadAllBytes is null)
						{
							fileReadAllBytes = md;
						}
						else if (methodName == "WriteAllBytes" && fileWriteAllBytes is null)
						{
							fileWriteAllBytes = md;
						}
					}
					else if (methodName == "FromResult" && fromResultMethod is null)
					{
						if (md is MethodSpecification spec)
						{
							fromResultMethod = spec.Method;
						}
						else if (md is IMethodDefOrRef mdr)
						{
							fromResultMethod = mdr;
						}
					}
					else if (methodName == "get_CompletedTask" && getCompletedTask is null)
					{
						getCompletedTask = md;
					}
				}
			}
		}

		if (fileReadAllText is null || fileWriteAllText is null || fileReadAllBytes is null || fileWriteAllBytes is null)
		{
			Logger.Warning(LogCategory.Export, "File method references not found in module, cannot patch IOUtils");
			return;
		}

		if (fromResultMethod is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.FromResult method reference not found, cannot patch IOUtils");
			return;
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.CompletedTask reference not found, cannot patch IOUtils");
			return;
		}

		TypeSignature stringTypeSig = module.CorLibTypeFactory.String;
		TypeSignature byteArrayTypeSig = new SzArrayTypeSignature(module.CorLibTypeFactory.Byte);
		MethodSpecification fromResultString = new(fromResultMethod, new GenericInstanceMethodSignature(new[] { stringTypeSig }));
		MethodSpecification fromResultByteArray = new(fromResultMethod, new GenericInstanceMethodSignature(new[] { byteArrayTypeSig }));

		PatchIOUtilsMethod(ioUtilsType, "ReadTextFileAsync", body =>
		{
			body.Instructions.Add(CilOpCodes.Ldarg_0);
			body.Instructions.Add(CilOpCodes.Call, fileReadAllText);
			body.Instructions.Add(CilOpCodes.Call, fromResultString);
			body.Instructions.Add(CilOpCodes.Ret);
		});

		PatchIOUtilsMethod(ioUtilsType, "WriteTextFileAsync", body =>
		{
			body.Instructions.Add(CilOpCodes.Ldarg_0);
			body.Instructions.Add(CilOpCodes.Ldarg_1);
			body.Instructions.Add(CilOpCodes.Call, fileWriteAllText);
			body.Instructions.Add(CilOpCodes.Call, getCompletedTask);
			body.Instructions.Add(CilOpCodes.Ret);
		});

		PatchIOUtilsMethod(ioUtilsType, "ReadFileAsync", body =>
		{
			body.Instructions.Add(CilOpCodes.Ldarg_0);
			body.Instructions.Add(CilOpCodes.Call, fileReadAllBytes);
			body.Instructions.Add(CilOpCodes.Call, fromResultByteArray);
			body.Instructions.Add(CilOpCodes.Ret);
		});

		PatchIOUtilsMethod(ioUtilsType, "WriteFileAsync", body =>
		{
			body.Instructions.Add(CilOpCodes.Ldarg_0);
			body.Instructions.Add(CilOpCodes.Ldarg_1);
			body.Instructions.Add(CilOpCodes.Call, fileWriteAllBytes);
			body.Instructions.Add(CilOpCodes.Call, getCompletedTask);
			body.Instructions.Add(CilOpCodes.Ret);
		});

		Logger.Info(LogCategory.Export, "Patched IOUtils to use synchronous file I/O");
	}

	private static void PatchIOUtilsMethod(TypeDefinition ioUtilsType, string methodName, Action<CilMethodBody> buildBody)
	{
		MethodDefinition? method = ioUtilsType.Methods.FirstOrDefault(m => m.Name == methodName);
		if (method is null)
		{
			Logger.Warning(LogCategory.Export, $"IOUtils.{methodName} method not found");
			return;
		}

		if (method.CilMethodBody is null)
		{
			Logger.Warning(LogCategory.Export, $"IOUtils.{methodName} has no CIL body");
			return;
		}

		method.CilMethodBody = new CilMethodBody();
		buildBody(method.CilMethodBody);
		Logger.Info(LogCategory.Export, $"Patched IOUtils.{methodName} to use synchronous file I/O");
	}

	private static void PatchLocalizationLoadState(ModuleDefinition module)
	{
		TypeDefinition? locManagerType = module.GetAllTypes().FirstOrDefault(t => t.Name == "LocalizationManager");
		if (locManagerType is null)
		{
			Logger.Warning(LogCategory.Export, "LocalizationManager type not found in Naninovel DLL");
			return;
		}

		MethodDefinition? loadStateMethod = locManagerType.Methods.FirstOrDefault(m => m.Name == "LoadServiceStateAsync");
		if (loadStateMethod is null)
		{
			Logger.Warning(LogCategory.Export, "LocalizationManager.LoadServiceStateAsync method not found");
			return;
		}

		IMethodDescriptor? getCompletedTask = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md)
					{
						continue;
					}

					if (md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
					{
						getCompletedTask = md;
					}
				}
			}
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.CompletedTask reference not found, cannot patch LocalizationManager.LoadServiceStateAsync");
			return;
		}

		loadStateMethod.CilMethodBody = new CilMethodBody();
		loadStateMethod.CilMethodBody.Instructions.Add(CilOpCodes.Call, getCompletedTask);
		loadStateMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
		loadStateMethod.CilMethodBody.MaxStack = 1;
		Logger.Info(LogCategory.Export, "Patched LocalizationManager.LoadServiceStateAsync to skip SelectLocaleAsync (returns CompletedTask)");
	}

	private static void PatchUIManagerInstantiateUIs(ModuleDefinition module)
	{
		TypeDefinition? uiManagerType = module.GetAllTypes().FirstOrDefault(t => t.Name == "UIManager");
		if (uiManagerType is null)
		{
			Logger.Warning(LogCategory.Export, "UIManager type not found in Naninovel DLL");
			return;
		}

		MethodDefinition? instantiateMethod = uiManagerType.Methods.FirstOrDefault(m => m.Name == "InstantiateUIsAsync");
		if (instantiateMethod is null || instantiateMethod.CilMethodBody is null)
		{
			Logger.Warning(LogCategory.Export, "UIManager.InstantiateUIsAsync method not found or has no body");
			return;
		}

		foreach (CilInstruction instr in instantiateMethod.CilMethodBody.Instructions)
		{
			if (instr.Operand is IMethodDescriptor md && md.Name?.ToString() == "get_isBatchMode")
			{
				Logger.Info(LogCategory.Export, "InstantiateUIsAsync already patched with isBatchMode check, skipping");
				return;
			}
		}

		IMethodDescriptor? isPlayingRef = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.DeclaringType?.Name?.ToString() == "Application" && md.Name?.ToString() == "get_isPlaying")
					{
						isPlayingRef = md;
						break;
					}
				}

				if (isPlayingRef is not null)
				{
					break;
				}
			}

			if (isPlayingRef is not null)
			{
				break;
			}
		}

		if (isPlayingRef is null || isPlayingRef.DeclaringType is null)
		{
			Logger.Warning(LogCategory.Export, "UnityEngine.Application.get_isPlaying reference not found in Naninovel DLL");
			return;
		}

		var isBatchModeRef = new MemberReference((IMemberRefParent)isPlayingRef.DeclaringType, "get_isBatchMode", isPlayingRef.Signature);

		IMethodDescriptor? getCompletedTask = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md)
					{
						continue;
					}

					if (md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
					{
						getCompletedTask = md;
					}
				}
			}
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.CompletedTask reference not found, cannot patch UIManager.InstantiateUIsAsync");
			return;
		}

		CilInstructionCollection instructions = instantiateMethod.CilMethodBody.Instructions;

		FieldDefinition startedField = new(
			"_naniUIInstantiateStarted",
			FieldAttributes.Static | FieldAttributes.Private,
			module.CorLibTypeFactory.Boolean
		);
		uiManagerType.Fields.Add(startedField);

		CilInstruction checkStartedInstr = new(CilOpCodes.Ldsfld, startedField);
		CilInstruction setStartedInstr = new(CilOpCodes.Ldc_I4_1);

		instructions.Insert(0, CilOpCodes.Call, (IMethodDescriptor)isBatchModeRef);
		instructions.Insert(1, CilOpCodes.Brfalse, checkStartedInstr.CreateLabel());
		instructions.Insert(2, CilOpCodes.Call, getCompletedTask);
		instructions.Insert(3, CilOpCodes.Ret);
		instructions.Insert(4, checkStartedInstr);
		instructions.Insert(5, CilOpCodes.Brfalse, setStartedInstr.CreateLabel());
		instructions.Insert(6, CilOpCodes.Call, getCompletedTask);
		instructions.Insert(7, CilOpCodes.Ret);
		instructions.Insert(8, setStartedInstr);
		instructions.Insert(9, CilOpCodes.Stsfld, startedField);

		instantiateMethod.CilMethodBody.MaxStack = Math.Max(instantiateMethod.CilMethodBody.MaxStack, 1);
		Logger.Info(LogCategory.Export, "Patched UIManager.InstantiateUIsAsync with runtime isBatchMode conditional + re-entry guard (skip if already started)");

	}

	private static void PatchPostInitTaskLogging(ModuleDefinition module)
	{
		IMethodDescriptor? debugLogMethod = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
				{
					continue;
				}

				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md)
					{
						continue;
					}

					if (md.DeclaringType?.Name == "Debug" && md.Name?.Value == "Log")
					{
						debugLogMethod = md;
						break;
					}
				}

				if (debugLogMethod is not null)
				{
					break;
				}
			}

			if (debugLogMethod is not null)
			{
				break;
			}
		}

		if (debugLogMethod is null)
		{
			AssemblyReference? unityCoreModule = null;
			foreach (AssemblyReference ar in module.AssemblyReferences)
			{
				if (ar.Name?.ToString() == "UnityEngine.CoreModule")
				{
					unityCoreModule = ar;
					break;
				}
			}

			if (unityCoreModule is not null)
			{
				TypeReference debugTypeRef = new(unityCoreModule, "UnityEngine", "Debug");
				TypeSignature stringSig = module.CorLibTypeFactory.String;
				MethodSignature logSignature = MethodSignature.CreateStatic(module.CorLibTypeFactory.Void, [stringSig]);
				debugLogMethod = new MemberReference(debugTypeRef, "Log", logSignature);
				Logger.Info(LogCategory.Export, "Created Debug.Log reference from UnityEngine.CoreModule assembly reference");
			}
			else
			{
				Logger.Warning(LogCategory.Export, "UnityEngine.CoreModule assembly reference not found, cannot create Debug.Log reference");
			}
		}

		if (debugLogMethod is null)
		{
			Logger.Warning(LogCategory.Export, "Debug.Log method not found and could not create reference, cannot patch post-init task logging");
			return;
		}

		// R3: NANI_POSTINIT trace patches removed 2026-10-22 — initialization flow verified (Engine.Initialized=True),
		// trace logging flooded Unity Console. Only exception logging (PatchInitExceptionLogging) is retained.
		PatchInitExceptionLogging(module, debugLogMethod);
	}

	private static void PatchAllInitializeAsyncLogging(ModuleDefinition module)
	{
		IMethodDescriptor? debugLogMethod = FindOrCreateDebugLogMethod(module);
		if (debugLogMethod is null)
		{
			Logger.Warning(LogCategory.Export, "Debug.Log method not found, cannot patch InitializeAsync logging");
			return;
		}

		int patchCount = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			MethodDefinition? initMethod = type.Methods.FirstOrDefault(m => m.Name == "InitializeAsync" && m.CilMethodBody is not null);
			if (initMethod is null)
			{
				continue;
			}

			string entryMsg = $"NANI_UIINIT: ENTER {type.Name}.InitializeAsync";
			CilInstructionCollection instructions = initMethod.CilMethodBody.Instructions;
			instructions.Insert(0, CilOpCodes.Ldstr, entryMsg);
			instructions.Insert(1, CilOpCodes.Call, debugLogMethod);
			patchCount++;
		}

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<InitializeAsync>") != true || !type.Methods.Any(m => m.Name == "MoveNext"))
			{
				continue;
			}

			MethodDefinition? moveNextMethod = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
			if (moveNextMethod is null || moveNextMethod.CilMethodBody is null)
			{
				continue;
			}

			string? parentTypeName = type.DeclaringType?.Name?.ToString();
			if (parentTypeName is null)
			{
				continue;
			}

			string completionMsg = $"NANI_UIINIT: COMPLETED {parentTypeName}.InitializeAsync";
			CilInstructionCollection instructions = moveNextMethod.CilMethodBody.Instructions;

			for (int i = instructions.Count - 1; i >= 0; i--)
			{
				if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
					instructions[i].Operand is IMethodDescriptor md && md.Name?.Value == "SetResult")
				{
					instructions.Insert(i, CilOpCodes.Ldstr, completionMsg);
					instructions.Insert(i + 1, CilOpCodes.Call, debugLogMethod);
				}
			}
		}

		Logger.Info(LogCategory.Export, $"Patched {patchCount} InitializeAsync methods with entry logging");
	}

	private static void PatchInitExceptionLogging(ModuleDefinition module, IMethodDescriptor debugLogMethod)
	{
		IMethodDescriptor? toStringMethod = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			foreach (MethodDefinition m in t.Methods)
			{
				if (m.CilMethodBody is null) continue;
				foreach (CilInstruction instr in m.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "ToString" && md.Signature?.ReturnType?.IsTypeOf("System", "String") == true)
					{
						toStringMethod = md;
						break;
					}
				}
				if (toStringMethod is not null) break;
			}
			if (toStringMethod is not null) break;
		}

		Logger.Info(LogCategory.Export, $"PatchInitExceptionLogging: ToString method {(toStringMethod is not null ? "found" : "not found")}");

		int patchCount = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<InitializeAsync>") != true || type.DeclaringType?.Name?.Value != "RuntimeInitializer")
				continue;

			MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext" && m.CilMethodBody is not null);
			if (moveNext is null) continue;

			CilInstructionCollection instructions = moveNext.CilMethodBody.Instructions;
			for (int i = 0; i < instructions.Count; i++)
			{
				if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
					instructions[i].Operand is IMethodDescriptor md && md.Name?.Value == "SetException")
				{
					instructions.Insert(i, CilOpCodes.Ldstr, "NANI_INIT_EXCEPTION: caught in RuntimeInitializer.InitializeAsync");
					instructions.Insert(i + 1, CilOpCodes.Call, debugLogMethod);
					if (toStringMethod is not null)
					{
						instructions.Insert(i + 2, CilOpCodes.Dup);
						instructions.Insert(i + 3, CilOpCodes.Callvirt, toStringMethod);
						instructions.Insert(i + 4, CilOpCodes.Call, debugLogMethod);
					}
					patchCount++;
					break;
				}
			}
		}

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<InitializeAsync>") != true || type.DeclaringType?.Name?.Value != "Engine")
				continue;

			MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext" && m.CilMethodBody is not null);
			if (moveNext is null) continue;

			CilInstructionCollection instructions = moveNext.CilMethodBody.Instructions;
			for (int i = 0; i < instructions.Count; i++)
			{
				if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
					instructions[i].Operand is IMethodDescriptor md && md.Name?.Value == "SetException")
				{
					instructions.Insert(i, CilOpCodes.Ldstr, "NANI_INIT_EXCEPTION: caught in Engine.InitializeAsync");
					instructions.Insert(i + 1, CilOpCodes.Call, debugLogMethod);
					if (toStringMethod is not null)
					{
						instructions.Insert(i + 2, CilOpCodes.Dup);
						instructions.Insert(i + 3, CilOpCodes.Callvirt, toStringMethod);
						instructions.Insert(i + 4, CilOpCodes.Call, debugLogMethod);
					}
					patchCount++;
					break;
				}
			}
		}

 		Logger.Info(LogCategory.Export, $"PatchInitExceptionLogging: patched {patchCount} catch blocks with exception logging");
 	}

 	private static void PatchLoadAllAsyncNullCheck(ModuleDefinition module)
 	{
 		int patchCount = 0;
 		foreach (TypeDefinition type in module.GetAllTypes())
 		{
 			if (type.Name?.Value != "<LoadAllAsync>d__17") continue;
 			MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext" && m.CilMethodBody is not null);
 			if (moveNext is null) continue;

 			CilInstructionCollection instructions = moveNext.CilMethodBody.Instructions;
 			for (int i = 0; i < instructions.Count - 1; i++)
 			{
 				if (instructions[i].OpCode != CilOpCodes.Call) continue;
 				if (instructions[i].Operand is not IMethodDescriptor getResultMd) continue;
 				if (getResultMd.Name?.Value != "GetResult") continue;
 				if (instructions[i + 1].OpCode == CilOpCodes.Dup) { patchCount++; break; }
 				if (instructions[i + 1].OpCode != CilOpCodes.Callvirt) continue;
 				if (instructions[i + 1].Operand is not IMethodDescriptor getEnumMd) continue;
 				if (getEnumMd.Name?.Value != "GetEnumerator") continue;

 				int brIdx = -1;
 				for (int j = i + 2; j < Math.Min(i + 6, instructions.Count); j++)
 				{
 					if (instructions[j].OpCode == CilOpCodes.Br) { brIdx = j; break; }
 				}
 				if (brIdx == -1) continue;

 				CilInstruction? nextSourceCheck = null;
 				for (int j = brIdx + 1; j < instructions.Count; j++)
 				{
 					if (instructions[j].OpCode == CilOpCodes.Initobj &&
 						instructions[j].Operand is ITypeDefOrRef tdr &&
 						tdr.Name?.Value == "ProvisionSource")
 					{
 						if (j + 1 < instructions.Count && instructions[j + 1].OpCode == CilOpCodes.Ldarg_0)
 						{
 							nextSourceCheck = instructions[j + 1];
 							break;
 						}
 					}
 				}
 				if (nextSourceCheck is null) continue;

 				CilInstruction nullHandler = new(CilOpCodes.Pop);
 				CilInstruction brToNext = new(CilOpCodes.Br, nextSourceCheck.CreateLabel());

 				instructions.Insert(i + 1, CilOpCodes.Dup);
 				instructions.Insert(i + 2, CilOpCodes.Brfalse_S, nullHandler.CreateLabel());
 				instructions.Insert(brIdx + 3, nullHandler);
 				instructions.Insert(brIdx + 4, brToNext);

 				patchCount++;
 				Logger.Info(LogCategory.Export, $"PatchLoadAllAsyncNullCheck: patched null check at IL offset {instructions[i].Offset:X4}");
 				break;
 			}
 		}
 		Logger.Info(LogCategory.Export, $"PatchLoadAllAsyncNullCheck: patched {patchCount} methods");
 	}

	private static void PatchCGGalleryPanelInit(ModuleDefinition module)
	{
		TypeDefinition? cgGalleryPanelType = module.GetAllTypes().FirstOrDefault(t => t.Name == "CGGalleryPanel");
		if (cgGalleryPanelType is null)
		{
			Logger.Warning(LogCategory.Export, "CGGalleryPanel type not found in Naninovel DLL");
			return;
		}

		MethodDefinition? initMethod = cgGalleryPanelType.Methods.FirstOrDefault(m => m.Name == "InitializeAsync");
		if (initMethod is null)
		{
			Logger.Warning(LogCategory.Export, "CGGalleryPanel.InitializeAsync method not found");
			return;
		}

		IMethodDescriptor? getCompletedTask = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
					continue;
				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
						getCompletedTask = md;
				}
			}
			if (getCompletedTask is not null)
				break;
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.CompletedTask reference not found, cannot patch CGGalleryPanel.InitializeAsync");
			return;
		}

		initMethod.CilMethodBody = new CilMethodBody();
		initMethod.CilMethodBody.Instructions.Add(CilOpCodes.Call, getCompletedTask);
		initMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
		initMethod.CilMethodBody.MaxStack = 1;
		Logger.Info(LogCategory.Export, "Patched CGGalleryPanel.InitializeAsync to return CompletedTask immediately (no CG resources in exported project)");
	}

	private static IMethodDescriptor? FindOrCreateDebugLogMethod(ModuleDefinition module)
	{
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
					continue;
				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.DeclaringType?.Name == "Debug" && md.Name?.Value == "Log")
						return md;
				}
			}
		}

		AssemblyReference? unityCoreModule = null;
		foreach (AssemblyReference ar in module.AssemblyReferences)
		{
			if (ar.Name?.ToString() == "UnityEngine.CoreModule")
			{
				unityCoreModule = ar;
				break;
			}
		}

		if (unityCoreModule is not null)
		{
			TypeReference debugTypeRef = new(unityCoreModule, "UnityEngine", "Debug");
			TypeSignature stringSig = module.CorLibTypeFactory.String;
			MethodSignature logSignature = MethodSignature.CreateStatic(module.CorLibTypeFactory.Void, [stringSig]);
			return new MemberReference(debugTypeRef, "Log", logSignature);
		}

		return null;
	}

	private static void LogProjectResourceLoaderStructure(ModuleDefinition module)
	{
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("ProjectResourceLoader") != true)
				continue;

			Logger.Info(LogCategory.Export, $"=== ProjectResourceLoader type: {type.FullName} ===");

			foreach (FieldDefinition field in type.Fields)
			{
				Logger.Info(LogCategory.Export, $"  Field: {field.Name} : {field.Signature?.FieldType?.FullName}");
			}

			foreach (MethodDefinition method in type.Methods)
			{
				Logger.Info(LogCategory.Export, $"  Method: {method.Name} (params: {method.Parameters.Count})");
				if (method.Name == "RunAsync" && method.CilMethodBody is not null)
				{
					Logger.Info(LogCategory.Export, $"  RunAsync IL ({method.CilMethodBody.Instructions.Count} instructions):");
					for (int i = 0; i < method.CilMethodBody.Instructions.Count && i < 50; i++)
					{
						CilInstruction instr = method.CilMethodBody.Instructions[i];
						Logger.Info(LogCategory.Export, $"    [{i}] {instr.OpCode} {instr.Operand}");
					}
				}
			}
		}

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<RunAsync>") != true || type.DeclaringType?.Name?.Contains("ProjectResourceLoader") != true)
				continue;

			Logger.Info(LogCategory.Export, $"=== RunAsync state machine: {type.FullName} ===");
			foreach (FieldDefinition field in type.Fields)
			{
				Logger.Info(LogCategory.Export, $"  Field: {field.Name} : {field.Signature?.FieldType?.FullName}");
			}

			MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
			if (moveNext?.CilMethodBody is not null)
			{
				Logger.Info(LogCategory.Export, $"  MoveNext IL ({moveNext.CilMethodBody.Instructions.Count} instructions):");
				for (int i = 0; i < moveNext.CilMethodBody.Instructions.Count && i < 200; i++)
				{
					CilInstruction instr = moveNext.CilMethodBody.Instructions[i];
					Logger.Info(LogCategory.Export, $"    [{i}] {instr.OpCode} {instr.Operand}");
				}
			}
		}
	}

	private static void PatchRunResourceLoaderLogging(ModuleDefinition module)
	{
		IMethodDescriptor? debugLog = FindOrCreateDebugLogMethod(module);
		if (debugLog is null)
		{
			Logger.Warning(LogCategory.Export, "Debug.Log not found, cannot patch RunResourceLoader logging");
			return;
		}

		TypeDefinition? providerType = module.GetAllTypes().FirstOrDefault(t => t.Name == "ResourceProvider" && !t.IsInterface);
		if (providerType is null)
		{
			Logger.Warning(LogCategory.Export, "ResourceProvider type not found for RunResourceLoader logging");
			return;
		}

		foreach (MethodDefinition method in providerType.Methods)
		{
			if (method.Name != "RunResourceLoader" || method.CilMethodBody is null)
				continue;

			CilInstructionCollection instrs = method.CilMethodBody.Instructions;
			List<CilInstruction> original = new(instrs);
			instrs.Clear();

			instrs.Add(CilOpCodes.Ldstr, "NANI_RR: ENTER RunResourceLoader");
			instrs.Add(CilOpCodes.Call, debugLog);

			foreach (CilInstruction instr in original)
				instrs.Add(instr);

			method.CilMethodBody.MaxStack = Math.Max(method.CilMethodBody.MaxStack, 4);
			Logger.Info(LogCategory.Export, $"Patched RunResourceLoader logging in {providerType.Name}");
			break;
		}
	}

	private static void PatchInstantiateUIsAsyncLogging(ModuleDefinition module)
	{
		IMethodDescriptor? debugLogMethod = FindOrCreateDebugLogMethod(module);
		if (debugLogMethod is null)
			return;

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<InstantiateUIsAsync>") != true || type.DeclaringType?.Name?.Value != "UIManager")
				continue;

			MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
			if (moveNext?.CilMethodBody is null)
				continue;

			var instrs = moveNext.CilMethodBody.Instructions;
			int patched = 0;

			for (int i = 0; i < instrs.Count; i++)
			{
				if (instrs[i].Operand is IMethodDescriptor md && md.Name?.Value == "WhenAll")
				{
					instrs.Insert(i, CilOpCodes.Call, debugLogMethod);
					instrs.Insert(i, CilOpCodes.Ldstr, "NANI_WHENALL: before WhenAll in InstantiateUIsAsync");
					patched++;
					break;
				}
			}

			for (int i = 0; i < instrs.Count; i++)
			{
				if (instrs[i].Operand is IMethodDescriptor md && md.Name?.Value == "GetEnumerator")
				{
					instrs.Insert(i, CilOpCodes.Call, debugLogMethod);
					instrs.Insert(i, CilOpCodes.Ldstr, "NANI_FOREACH: before foreach in InstantiateUIsAsync");
					patched++;
					break;
				}
			}

			moveNext.CilMethodBody.MaxStack = Math.Max(moveNext.CilMethodBody.MaxStack, 1);
			Logger.Info(LogCategory.Export, $"Patched InstantiateUIsAsync state machine with {patched} log inserts");
			break;
		}
	}

	private static void PatchStateMachineSkipSuspension(ModuleDefinition module)
	{
		int patched = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<RunAsync>") == true && type.DeclaringType?.Name?.Contains("ProjectResourceLoader") == true)
			{
				MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
				if (moveNext?.CilMethodBody is null)
					continue;

				var instrs = moveNext.CilMethodBody.Instructions;
				for (int i = 0; i < instrs.Count; i++)
				{
					bool isBranch = instrs[i].OpCode == CilOpCodes.Brfalse || instrs[i].OpCode == CilOpCodes.Brfalse_S ||
					                 instrs[i].OpCode == CilOpCodes.Brtrue || instrs[i].OpCode == CilOpCodes.Brtrue_S;
					if (!isBranch)
						continue;

					for (int j = Math.Max(0, i - 5); j < i; j++)
					{
						if (instrs[j].Operand is IMethodDescriptor md && md.Name?.Value == "get_IsCompleted")
						{
							instrs[i].OpCode = CilOpCodes.Pop;
							instrs[i].Operand = null!;
							patched++;
							Logger.Info(LogCategory.Export, $"  Patched {instrs[i].OpCode} at index {i} (after get_IsCompleted at index {j}) in {type.Name}");
							break;
						}
					}
				}
			}
		}
		Logger.Info(LogCategory.Export, $"Patched ProjectResourceLoader state machine: {patched} branch->pop replacements to skip LoadAsync suspension");
	}

	private static void PatchProjectResourceLoaderSyncLoad(ModuleDefinition module)
	{
		TypeDefinition? loaderType = module.GetAllTypes().FirstOrDefault(t => t.Name?.Contains("ProjectResourceLoader") == true && !t.Name.Contains("d__"));
		if (loaderType is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResourceLoader type not found");
			return;
		}

		MethodDefinition? runAsyncMethod = loaderType.Methods.FirstOrDefault(m => m.Name == "RunAsync");
		if (runAsyncMethod is null)
		{
			Logger.Warning(LogCategory.Export, "ProjectResourceLoader.RunAsync method not found");
			return;
		}

		IMethodDescriptor? resourcesLoadAsync = null;
		IMethodDescriptor? resourceCtor = null;
		IMethodDescriptor? setResult = null;
		IMethodDescriptor? getCompletedTask = null;
		IMethodDescriptor? stringConcat = null;
		IMethodDescriptor? stringIsNullOrEmpty = null;
		IMethodDescriptor? resourcesLoadString = null;

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains("<RunAsync>") == true && type.DeclaringType?.Name?.Contains("ProjectResourceLoader") == true)
			{
				MethodDefinition? moveNext = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
				if (moveNext?.CilMethodBody is not null)
				{
					foreach (CilInstruction instr in moveNext.CilMethodBody.Instructions)
					{
						if (instr.Operand is not IMethodDescriptor md)
							continue;

					if (md.Name?.Value == "LoadAsync" && md.DeclaringType?.FullName?.Contains("Resources") == true)
						resourcesLoadAsync = md;
					if (md.Name == ".ctor" && md.DeclaringType?.Name?.Contains("Resource`1") == true)
						resourceCtor = md;
					if (md.Name?.Value == "SetResult" && md.DeclaringType?.Name?.Contains("ResourceRunner") == true)
						setResult = md;
						if (md.Name?.Value == "Concat" && md.Signature?.ParameterTypes.Count == 3)
							stringConcat = md;
						if (md.Name?.Value == "IsNullOrEmpty")
							stringIsNullOrEmpty = md;
					}
				}
			}
		}

		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
					continue;
				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md)
						continue;
					if (md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
						getCompletedTask = md;
				}
			}
			if (getCompletedTask is not null)
				break;
		}

		if (resourcesLoadAsync is null || resourceCtor is null || setResult is null || getCompletedTask is null || stringConcat is null || stringIsNullOrEmpty is null)
		{
			Logger.Warning(LogCategory.Export, $"Missing method refs: LoadAsync={resourcesLoadAsync is not null}, Ctor={resourceCtor is not null}, SetResult={setResult is not null}, CompletedTask={getCompletedTask is not null}, Concat={stringConcat is not null}, IsNullOrEmpty={stringIsNullOrEmpty is not null}");
			return;
		}

		IMemberRefParent? resourcesParent = resourcesLoadAsync.DeclaringType as IMemberRefParent;
		if (resourcesParent is null)
		{
			Logger.Warning(LogCategory.Export, "Resources type reference not found");
			return;
		}

		TypeSignature objectSig = module.CorLibTypeFactory.Object;
		TypeSignature stringSig = module.CorLibTypeFactory.String;

		MethodSignature loadStringSignature = MethodSignature.CreateStatic(objectSig, [stringSig]);
		IMethodDescriptor resourcesLoadStr = new MemberReference(resourcesParent, "Load", loadStringSignature);
		resourcesLoadStr = module.DefaultImporter.ImportMethod(resourcesLoadStr);

		FieldDefinition? rootPathField = loaderType.Fields.FirstOrDefault(f => f.Name == "RootPath");
		FieldDefinition? pathField = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			if (t.Name?.Contains("ResourceRunner") == true && !t.Name.Contains("`1"))
			{
				pathField = t.Fields.FirstOrDefault(f => f.Name == "Path");
				break;
			}
		}
		if (pathField is null)
		{
			foreach (TypeDefinition t in module.GetAllTypes())
			{
				if (t.Name?.Contains("ResourceRunner") == true)
				{
					pathField = t.Fields.FirstOrDefault(f => f.Name == "Path");
					if (pathField is not null)
						break;
				}
			}
		}

		if (rootPathField is null || pathField is null)
		{
			Logger.Warning(LogCategory.Export, $"RootPath field: {rootPathField is not null}, Path field: {pathField is not null}");
			return;
		}

		var genericParamSig = new GenericParameterSignature(module, GenericParameterType.Type, 0);
		var typeSpec = new TypeSpecification(genericParamSig);
		IMethodDescriptor? debugLog = FindOrCreateDebugLogMethod(module);

		runAsyncMethod.CilMethodBody = new CilMethodBody();
		runAsyncMethod.CilMethodBody.LocalVariables.Add(new CilLocalVariable(genericParamSig));
		CilInstructionCollection instructions = runAsyncMethod.CilMethodBody.Instructions;

		var tReturnSig = new GenericParameterSignature(module, GenericParameterType.Method, 0);
		MethodSignature loadGenericSig = MethodSignature.CreateStatic(tReturnSig, [stringSig]);
		loadGenericSig.GenericParameterCount = 1;
		IMethodDescriptor loadGenericRef = new MemberReference(resourcesParent, "Load", loadGenericSig);
		loadGenericRef = module.DefaultImporter.ImportMethod(loadGenericRef);
		var loadGenericSpec = new MethodSpecification(
			(IMethodDefOrRef)loadGenericRef,
			new GenericInstanceMethodSignature([genericParamSig]));
		loadGenericSpec = module.DefaultImporter.ImportMethod(loadGenericSpec);

		// Debug.Log("NANI_SYNC: ENTER RunAsync")
		if (debugLog is not null)
		{
			instructions.Add(CilOpCodes.Ldstr, "NANI_SYNC: ENTER RunAsync");
			instructions.Add(CilOpCodes.Call, debugLog);
		}

		// if (string.IsNullOrEmpty(RootPath)) path = Path; else path = RootPath + "/" + Path;
		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Ldfld, rootPathField);
		instructions.Add(CilOpCodes.Call, stringIsNullOrEmpty);
		CilInstruction brFalse = new(CilOpCodes.Brfalse, null!);
		instructions.Add(brFalse);

		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Ldfld, pathField);
		CilInstruction brAfterPath = new(CilOpCodes.Br, null!);
		instructions.Add(brAfterPath);

		instructions.Add(CilOpCodes.Ldarg_0);
		brFalse.Operand = instructions[instructions.Count - 1].CreateLabel();
		instructions.Add(CilOpCodes.Ldfld, rootPathField);
		instructions.Add(CilOpCodes.Ldstr, "/");
		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Ldfld, pathField);
		instructions.Add(CilOpCodes.Call, stringConcat);

		// After path resolution (stack: path) - call Resources.Load<T>(string) which returns T
		instructions.Add(CilOpCodes.Call, loadGenericSpec);
		brAfterPath.Operand = instructions.Last().CreateLabel();
		instructions.Add(CilOpCodes.Pop);

		// this.SetResult(new Resource<T>(Path, null))
		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Ldfld, pathField);
		instructions.Add(CilOpCodes.Ldnull);
		instructions.Add(CilOpCodes.Newobj, resourceCtor);
		instructions.Add(CilOpCodes.Call, setResult);

		// return UniTask.CompletedTask
		if (debugLog is not null)
		{
			instructions.Add(CilOpCodes.Ldstr, "NANI_SYNC: COMPLETED RunAsync");
			instructions.Add(CilOpCodes.Call, debugLog);
		}
		instructions.Add(CilOpCodes.Call, getCompletedTask);
		instructions.Add(CilOpCodes.Ret);

		runAsyncMethod.CilMethodBody.MaxStack = 4;
		Logger.Info(LogCategory.Export, "Patched ProjectResourceLoader.RunAsync to SetResult(null) (Load<T> call caused JIT error, using null fallback)");
	}

	private static void PatchScriptPlayerWaitMethods(ModuleDefinition module)
	{
		TypeDefinition? scriptPlayerType = module.GetAllTypes().FirstOrDefault(t => t.Name == "ScriptPlayer");
		if (scriptPlayerType is null)
		{
			Logger.Warning(LogCategory.Export, "ScriptPlayer type not found in Naninovel DLL");
			return;
		}

		IMethodDescriptor? getCompletedTask = null;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (method.CilMethodBody is null)
					continue;
				foreach (CilInstruction instr in method.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
						getCompletedTask = md;
				}
			}
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "UniTask.CompletedTask reference not found, cannot patch ScriptPlayer wait methods");
			return;
		}

		string[] waitMethodNames = ["WaitForAutoPlayDelayAsync", "WaitForWaitForInputDisabledAsync"];
		foreach (string methodName in waitMethodNames)
		{
			MethodDefinition? waitMethod = scriptPlayerType.Methods.FirstOrDefault(m => m.Name == methodName);
			if (waitMethod is null)
			{
				Logger.Warning(LogCategory.Export, $"ScriptPlayer.{methodName} method not found");
				continue;
			}

			waitMethod.CilMethodBody = new CilMethodBody();
			waitMethod.CilMethodBody.Instructions.Add(CilOpCodes.Call, getCompletedTask);
			waitMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
			waitMethod.CilMethodBody.MaxStack = 1;
			Logger.Info(LogCategory.Export, $"Patched ScriptPlayer.{methodName} to return CompletedTask immediately");
		}
	}

	private static void PatchMethodEntryLog(ModuleDefinition module, string typeName, string methodName, IMethodDescriptor debugLogMethod, string logMessage)
	{
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name != typeName)
			{
				continue;
			}

			MethodDefinition? method = type.Methods.FirstOrDefault(m => m.Name == methodName);
			if (method is null || method.CilMethodBody is null)
			{
				continue;
			}

			CilInstructionCollection instructions = method.CilMethodBody.Instructions;
			instructions.Insert(0, CilOpCodes.Ldstr, logMessage);
			instructions.Insert(1, CilOpCodes.Call, debugLogMethod);

			Logger.Info(LogCategory.Export, $"Patched {type.FullName}.{methodName} with entry logging");
			return;
		}

		Logger.Warning(LogCategory.Export, $"{typeName}.{methodName} method not found for entry logging patch");
	}

	private static void PatchMethodEntryLogContains(ModuleDefinition module, string typeNameContains, string methodName, IMethodDescriptor debugLogMethod, string logMessage)
	{
		int patchCount = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains(typeNameContains) != true)
			{
				continue;
			}

			MethodDefinition? method = type.Methods.FirstOrDefault(m => m.Name == methodName);
			if (method is null || method.CilMethodBody is null)
			{
				continue;
			}

			CilInstructionCollection instructions = method.CilMethodBody.Instructions;
			instructions.Insert(0, CilOpCodes.Ldstr, logMessage);
			instructions.Insert(1, CilOpCodes.Call, debugLogMethod);

			Logger.Info(LogCategory.Export, $"Patched {type.FullName}.{methodName} with entry logging");
			patchCount++;
		}

		if (patchCount == 0)
		{
			Logger.Warning(LogCategory.Export, $"No type containing '{typeNameContains}' with method '{methodName}' found for entry logging");
		}
	}

	private static void PatchAsyncMethodCompletionLog(ModuleDefinition module, string methodName, IMethodDescriptor debugLogMethod, string logMessage)
	{
		int totalPatchCount = 0;
		int stateMachineCount = 0;
		foreach (TypeDefinition type in module.GetAllTypes())
		{
			if (type.Name?.Contains($"<{methodName}>") != true || !type.Methods.Any(m => m.Name == "MoveNext"))
			{
				continue;
			}

			MethodDefinition? moveNextMethod = type.Methods.FirstOrDefault(m => m.Name == "MoveNext");
			if (moveNextMethod is null || moveNextMethod.CilMethodBody is null)
			{
				continue;
			}

			CilInstructionCollection instructions = moveNextMethod.CilMethodBody.Instructions;

			int patchCount = 0;
			for (int i = instructions.Count - 1; i >= 0; i--)
			{
				if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
					instructions[i].Operand is IMethodDescriptor md && md.Name?.Value == "SetResult")
				{
					instructions.Insert(i, CilOpCodes.Ldstr, logMessage);
					instructions.Insert(i + 1, CilOpCodes.Call, debugLogMethod);
					patchCount++;
				}
			}

			totalPatchCount += patchCount;
			stateMachineCount++;
		}

		if (totalPatchCount == 0)
		{
			Logger.Warning(LogCategory.Export, $"No SetResult calls found in any state machine for {methodName}");
		}
		else
		{
			Logger.Info(LogCategory.Export, $"Patched {methodName} in {stateMachineCount} state machine(s) with {totalPatchCount} completion log(s)");
		}
	}

	private static readonly string[] EditorOnlyAssemblies =
	[
		"Assembly-CSharp-Editor",
		"Elringus.Naninovel.Editor",
		"Elringus.NaninovelInventory.Editor",
	];

	private static void RemoveEditorAssembliesFromConfig(string assetsPath, FileSystem fileSystem)
	{
		string configPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration", "EngineConfiguration.asset");
		if (!fileSystem.File.Exists(configPath))
		{
			return;
		}

		try
		{
			string content = fileSystem.File.ReadAllText(configPath);
			string[] lines = content.Split('\n');
			StringBuilder sb = new();
			foreach (string line in lines)
			{
				bool shouldRemove = false;
				foreach (string editorAssembly in EditorOnlyAssemblies)
				{
					if (line.TrimStart().StartsWith("- ") && line.Contains(editorAssembly))
					{
						shouldRemove = true;
						break;
					}
				}

				if (!shouldRemove)
				{
					sb.Append(line);
					if (!line.EndsWith('\n') && !line.EndsWith('\r'))
					{
						sb.Append('\n');
					}
				}
			}

			fileSystem.File.WriteAllText(configPath, sb.ToString().TrimEnd('\n', '\r') + "\n");
			Logger.Info(LogCategory.Export, "Removed Editor-only assemblies from EngineConfiguration TypeAssemblies.");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to remove Editor assemblies from EngineConfiguration: {ex.Message}");
		}
	}

	private static void SanitizeProviderTypes(string assetsPath, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(assetsPath, "Resources");
		if (!fileSystem.Directory.Exists(resourcesPath))
		{
			return;
		}

		int modifiedCount = 0;
		Queue<string> directories = new();
		directories.Enqueue(resourcesPath);

		while (directories.Count > 0)
		{
			string currentDir = directories.Dequeue();
			foreach (string dir in fileSystem.Directory.EnumerateDirectories(currentDir))
			{
				directories.Enqueue(dir);
			}

			foreach (string filePath in fileSystem.Directory.EnumerateFiles(currentDir, "*.asset"))
			{
				try
				{
					string content = fileSystem.File.ReadAllText(filePath);
					if (!content.Contains("ProviderTypes"))
					{
						continue;
					}

					string[] lines = content.Split('\n');
					bool modified = false;
					StringBuilder sb = new();
					for (int i = 0; i < lines.Length; i++)
					{
						string trimmed = lines[i].TrimStart();
						if (trimmed.StartsWith("- ") && trimmed.Length > 2 && string.IsNullOrWhiteSpace(trimmed.Substring(2)))
						{
							modified = true;
							continue;
						}
						if (trimmed == "-")
						{
							modified = true;
							continue;
						}

						sb.Append(lines[i]);
						if (i < lines.Length - 1 && !lines[i].EndsWith('\n') && !lines[i].EndsWith('\r'))
						{
							sb.Append('\n');
						}
					}

					if (modified)
					{
						fileSystem.File.WriteAllText(filePath, sb.ToString().TrimEnd('\n', '\r') + "\n");
						modifiedCount++;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"Failed to sanitize config file {filePath}: {ex.Message}");
				}
			}
		}

		if (modifiedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Sanitized empty ProviderTypes entries in {modifiedCount} configuration file(s).");
		}
	}

	private static void RemoveAddressableResourceProvider(string assetsPath, FileSystem fileSystem)
	{
		int modifiedCount = 0;
		string[] searchDirs = new[]
		{
			fileSystem.Path.Join(assetsPath, "Resources"),
			fileSystem.Path.Join(assetsPath, "Naninovel"),
		};

		foreach (string searchDir in searchDirs)
		{
			if (!fileSystem.Directory.Exists(searchDir))
			{
				continue;
			}

			Queue<string> directories = new();
			directories.Enqueue(searchDir);

			while (directories.Count > 0)
			{
				string currentDir = directories.Dequeue();
				foreach (string dir in fileSystem.Directory.EnumerateDirectories(currentDir))
				{
					directories.Enqueue(dir);
				}

				foreach (string filePath in fileSystem.Directory.EnumerateFiles(currentDir, "*.asset").Concat(fileSystem.Directory.EnumerateFiles(currentDir, "*.prefab")))
				{
					try
					{
						string content = fileSystem.File.ReadAllText(filePath);
						if (!content.Contains("AddressableResourceProvider"))
						{
							continue;
						}

						string[] lines = content.Split('\n');
						StringBuilder sb = new();
						foreach (string line in lines)
						{
							if (!line.Contains("AddressableResourceProvider"))
							{
								sb.Append(line);
								if (!line.EndsWith('\n') && !line.EndsWith('\r'))
								{
									sb.Append('\n');
								}
							}
						}

						fileSystem.File.WriteAllText(filePath, sb.ToString().TrimEnd('\n', '\r') + "\n");
						modifiedCount++;
					}
					catch (Exception ex)
					{
						Logger.Warning(LogCategory.Export, $"Failed to process config file {filePath}: {ex.Message}");
					}
				}
			}
		}

		if (modifiedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Removed AddressableResourceProvider from {modifiedCount} Naninovel configuration/prefab file(s).");
		}
	}

	private const string BackgroundsFolderName = "Backgrounds";
	private const string ProjectResourcesFileName = "ProjectResources.asset";
	private const string Texture2DType = "UnityEngine.Texture2D, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
	private static readonly string[] TextureExtensions = [".jpg", ".png", ".tga", ".bmp", ".exr", ".tif", ".tiff"];

	/// <summary>
	/// Scans Assets/Resources/Backgrounds/ for texture files and registers their paths
	/// into ProjectResources.asset. Naninovel's LocalResourceProvider reads this manifest
	/// to locate background resources; without registration, ResourceExistsAsync returns false
	/// and background images don't render.
	/// </summary>
	internal static void RegisterBackgroundsResources(string assetsPath, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(assetsPath, ResourcesFolderName);
		string backgroundsPath = fileSystem.Path.Join(resourcesPath, BackgroundsFolderName);
		if (!fileSystem.Directory.Exists(backgroundsPath))
		{
			Logger.Info(LogCategory.Export, $"Backgrounds directory not found at {backgroundsPath}, skipping resource registration.");
			return;
		}

		string projectResourcesPath = fileSystem.Path.Join(resourcesPath, UnityCommonFolderName, ProjectResourcesFileName);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Warning(LogCategory.Export, $"ProjectResources.asset not found at {projectResourcesPath}, cannot register Backgrounds resources.");
			return;
		}

		string existingContent = fileSystem.File.ReadAllText(projectResourcesPath);
		HashSet<string> existingPaths = new(StringComparer.Ordinal);
		ParseExistingResourcePaths(existingContent, existingPaths);

		List<(string Path, string Type)> newEntries = new();
		Queue<string> directories = new();
		directories.Enqueue(backgroundsPath);

		while (directories.Count > 0)
		{
			string currentDir = directories.Dequeue();
			foreach (string dir in fileSystem.Directory.EnumerateDirectories(currentDir))
			{
				directories.Enqueue(dir);
			}

			foreach (string filePath in fileSystem.Directory.EnumerateFiles(currentDir))
			{
				string extension = fileSystem.Path.GetExtension(filePath).ToLowerInvariant();
				if (!TextureExtensions.Contains(extension))
				{
					continue;
				}

				string relativePath = fileSystem.Path.GetRelativePath(resourcesPath, filePath);
				string resourcePath = relativePath.Substring(0, relativePath.Length - extension.Length).Replace('\\', '/');

				if (existingPaths.Contains(resourcePath))
				{
					continue;
				}

				newEntries.Add((resourcePath, Texture2DType));
				existingPaths.Add(resourcePath);
			}
		}

		if (newEntries.Count == 0)
		{
			Logger.Info(LogCategory.Export, "No new Backgrounds resources to register.");
			return;
		}

		AppendResourceEntries(projectResourcesPath, newEntries, fileSystem);
		Logger.Info(LogCategory.Export, $"Registered {newEntries.Count} Backgrounds resource paths into ProjectResources.asset.");
	}

	internal static void ParseExistingResourcePaths(string content, HashSet<string> existingPaths)
	{
		string[] lines = content.Split('\n');
		foreach (string line in lines)
		{
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith("- Path: "))
			{
				string path = trimmed.Substring("- Path: ".Length).Trim();
				existingPaths.Add(path);
			}
		}
	}

	internal static void AppendResourceEntries(string projectResourcesPath, List<(string Path, string Type)> entries, FileSystem fileSystem)
	{
		StringBuilder sb = new();
		sb.Append(fileSystem.File.ReadAllText(projectResourcesPath).TrimEnd('\n', '\r'));
		sb.Append('\n');

		foreach (var entry in entries)
		{
			sb.Append("  - Path: ").Append(entry.Path).Append('\n');
			sb.Append("    Type: ").Append(entry.Type).Append('\n');
		}

		fileSystem.File.WriteAllText(projectResourcesPath, sb.ToString());
	}

	private static void LogMethodBodySummary(MethodDefinition method, string label, int maxInstructions = 20)
	{
		if (method.CilMethodBody is null) return;
		CilInstructionCollection instructions = method.CilMethodBody.Instructions;
		Logger.Info(LogCategory.Export, $"[MethodBodySummary] {label} {method.FullName}: {instructions.Count} instructions");
		int count = Math.Min(instructions.Count, maxInstructions);
		for (int i = 0; i < count; i++)
		{
			CilInstruction instr = instructions[i];
			Logger.Info(LogCategory.Export, $"  [{i}] {instr.OpCode} {instr.Operand}");
		}
	}

	private static void PatchSpriteRendererUpdate(ModuleDefinition module, NaninovelPatchSettings patchSettings)
	{
		Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: replacing RenderToTexture call with Graphics.Blit in TransitionalSpriteRenderer.Update...");

		TypeDefinition? spriteRendererType = null;
		TypeDefinition? baseRendererType = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			if (t.Name?.Value == "TransitionalSpriteRenderer")
				spriteRendererType = t;
			if (t.Name?.Value == "TransitionalRenderer")
				baseRendererType = t;
		}

		if (spriteRendererType is null || baseRendererType is null)
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: TransitionalSpriteRenderer or TransitionalRenderer type not found");
			return;
		}

		MethodDefinition? updateMethod = null;
		foreach (MethodDefinition m in spriteRendererType.Methods)
		{
			if (m.Name?.Value == "Update")
			{
				updateMethod = m;
				break;
			}
		}

		if (updateMethod is null || updateMethod.CilMethodBody is null)
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: Update method not found or has no IL body");
			return;
		}

		IMethodDescriptor? getMainTexture = null;
		IMethodDescriptor? getMaterial = null;
		foreach (MethodDefinition m in baseRendererType.Methods)
		{
			if (m.Name?.Value == "get_MainTexture")
				getMainTexture = m;
			if (m.Name?.Value == "get_Material")
				getMaterial = m;
		}

		if (getMainTexture is null || getMaterial is null)
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: get_MainTexture or get_Material not found");
			return;
		}

		IFieldDescriptor? renderTextureField = null;
		foreach (FieldDefinition f in spriteRendererType.Fields)
		{
			if (f.Name?.Value == "renderTexture")
			{
				renderTextureField = f;
				break;
			}
		}

		if (renderTextureField is null)
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: renderTexture field not found");
			return;
		}

		IMethodDescriptor? graphicsBlit = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			foreach (MethodDefinition m in t.Methods)
			{
				if (m.CilMethodBody is null) continue;
				foreach (CilInstruction instr in m.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md) continue;
					if (md.Name?.Value != "Blit") continue;
					if (md.DeclaringType?.Name?.ToString() != "Graphics") continue;
					int paramCount = md.Signature?.ParameterTypes?.Count ?? 0;
					if (paramCount == 3 && graphicsBlit is null)
						graphicsBlit = md;
				}
			}
		}

		if (graphicsBlit is null)
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: Graphics.Blit not found");
			return;
		}

		IMethodDescriptor? renderTextureGetActive = null;
		IMethodDescriptor? renderTextureSetActive = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			foreach (MethodDefinition m in t.Methods)
			{
				if (m.CilMethodBody is null) continue;
				foreach (CilInstruction instr in m.CilMethodBody.Instructions)
				{
					if (instr.Operand is not IMethodDescriptor md) continue;
					if (md.DeclaringType?.Name?.ToString() != "RenderTexture") continue;
					if (md.Name?.Value == "get_active" && renderTextureGetActive is null)
						renderTextureGetActive = md;
					if (md.Name?.Value == "set_active" && renderTextureSetActive is null)
						renderTextureSetActive = md;
				}
			}
		}

		bool enableRenderStateIsolation = patchSettings.EnableRenderStateIsolation;
		if (enableRenderStateIsolation && (renderTextureGetActive is null || renderTextureSetActive is null))
		{
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: RenderTexture.get_active/set_active not found, skipping render state isolation");
			enableRenderStateIsolation = false;
		}

		CilInstructionCollection instructions = updateMethod.CilMethodBody.Instructions;
		int callIndex = -1;
		for (int i = 0; i < instructions.Count; i++)
		{
			if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
				instructions[i].Operand is IMethodDescriptor md &&
				md.Name?.Value == "RenderToTexture")
			{
				callIndex = i;
				break;
			}
		}

		if (callIndex < 4)
		{
			if (patchSettings.EnableIdempotencyCheck)
			{
				bool alreadyPatched = false;
				for (int i = 0; i < instructions.Count; i++)
				{
					if ((instructions[i].OpCode == CilOpCodes.Call || instructions[i].OpCode == CilOpCodes.Callvirt) &&
						instructions[i].Operand is IMethodDescriptor md &&
						md.Name?.Value == "Blit" &&
						md.DeclaringType?.Name?.ToString() == "Graphics")
					{
						alreadyPatched = true;
						break;
					}
				}
				if (alreadyPatched)
				{
					Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: DLL already patched (Blit found, RenderToTexture absent), skipping idempotently");
					return;
				}
			}
			Logger.Warning(LogCategory.Export, "PatchSpriteRendererUpdate: RenderToTexture call not found in Update IL");
			return;
		}

		Logger.Info(LogCategory.Export, $"PatchSpriteRendererUpdate: found RenderToTexture call at IL index {callIndex}");

		if (patchSettings.LogMethodBodySummary)
		{
			LogMethodBodySummary(updateMethod, "BEFORE");
		}

		for (int i = 0; i < 5; i++)
			instructions.RemoveAt(callIndex - 4);

		int insertAt = callIndex - 4;

		int blitBase = insertAt;
		CilLocalVariable? activeRtLocal = null;

		if (enableRenderStateIsolation)
		{
			TypeSignature renderTextureTypeSig = renderTextureGetActive!.Signature!.ReturnType!;
			activeRtLocal = new CilLocalVariable(renderTextureTypeSig);
			updateMethod.CilMethodBody.LocalVariables.Add(activeRtLocal);

			instructions.Insert(blitBase + 0, new CilInstruction(CilOpCodes.Call, renderTextureGetActive!));
			instructions.Insert(blitBase + 1, new CilInstruction(CilOpCodes.Stloc, activeRtLocal));
			blitBase += 2;
			Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: inserted RenderTexture.active save before Blit");
		}

		instructions.Insert(blitBase + 0, new CilInstruction(CilOpCodes.Ldarg_0));
		instructions.Insert(blitBase + 1, new CilInstruction(CilOpCodes.Call, getMainTexture));
		instructions.Insert(blitBase + 2, new CilInstruction(CilOpCodes.Ldarg_0));
		instructions.Insert(blitBase + 3, new CilInstruction(CilOpCodes.Ldfld, renderTextureField));
		instructions.Insert(blitBase + 4, new CilInstruction(CilOpCodes.Ldarg_0));
		instructions.Insert(blitBase + 5, new CilInstruction(CilOpCodes.Call, getMaterial));
		instructions.Insert(blitBase + 6, new CilInstruction(CilOpCodes.Call, graphicsBlit));
		blitBase += 7;

		if (enableRenderStateIsolation && activeRtLocal is not null)
		{
			instructions.Insert(blitBase + 0, new CilInstruction(CilOpCodes.Ldloc, activeRtLocal));
			instructions.Insert(blitBase + 1, new CilInstruction(CilOpCodes.Call, renderTextureSetActive!));
			Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: inserted RenderTexture.active restore after Blit");
			blitBase += 2;
		}

		if (patchSettings.EnableAspectCorrection)
		{
			IMethodDescriptor? setTextureOffset = null;
			IMethodDescriptor? getOffset = null;
			IMethodDescriptor? getCorrectAspect = null;
			foreach (TypeDefinition t in module.GetAllTypes())
			{
				foreach (MethodDefinition m in t.Methods)
				{
					if (m.Name?.Value == "GetOffset" && m.IsStatic && getOffset is null)
						getOffset = m;
					if (m.Name?.Value == "get_CorrectAspect" && getCorrectAspect is null)
						getCorrectAspect = m;
					if (m.CilMethodBody is null) continue;
					foreach (CilInstruction instr in m.CilMethodBody.Instructions)
					{
						if (instr.Operand is not IMethodDescriptor md) continue;
						if (md.Name?.Value == "SetTextureOffset" && setTextureOffset is null)
							setTextureOffset = md;
					}
				}
			}

			if (setTextureOffset is not null && getOffset is not null && getCorrectAspect is not null)
			{
				Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: AspectCorrection references found, IL injection requires conditional branch implementation");
			}
			else
			{
				Logger.Warning(LogCategory.Export, $"PatchSpriteRendererUpdate: AspectCorrection skipped (SetTextureOffset={setTextureOffset is not null}, GetOffset={getOffset is not null}, CorrectAspect={getCorrectAspect is not null})");
			}
		}

		if (patchSettings.EnableBlurIntensity)
		{
			IFieldDescriptor? blurFilterField = null;
			IMethodDescriptor? blurTextureMethod = null;
			IMethodDescriptor? getBlurIntensity = null;
			foreach (FieldDefinition f in baseRendererType.Fields)
			{
				if (f.Name?.Value == "blurFilter")
				{
					blurFilterField = f;
					break;
				}
			}
			foreach (TypeDefinition t in module.GetAllTypes())
			{
				foreach (MethodDefinition m in t.Methods)
				{
					if (m.Name?.Value == "get_BlurIntensity" && getBlurIntensity is null)
						getBlurIntensity = m;
					if (m.Name?.Value == "BlurTexture" && blurTextureMethod is null)
						blurTextureMethod = m;
				}
			}

			if (blurFilterField is not null && blurTextureMethod is not null && getBlurIntensity is not null)
			{
				Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: BlurIntensity references found, IL injection requires conditional branch implementation");
			}
			else
			{
				Logger.Warning(LogCategory.Export, $"PatchSpriteRendererUpdate: BlurIntensity skipped (blurFilter={blurFilterField is not null}, BlurTexture={blurTextureMethod is not null}, get_BlurIntensity={getBlurIntensity is not null})");
			}
		}

		if (updateMethod.CilMethodBody.MaxStack < 4)
			updateMethod.CilMethodBody.MaxStack = 4;

		Logger.Info(LogCategory.Export, "PatchSpriteRendererUpdate: successfully replaced RenderToTexture with Graphics.Blit");

		if (patchSettings.LogMethodBodySummary)
		{
			LogMethodBodySummary(updateMethod, "AFTER");
		}
	}

	private static void PatchTweenerInstantComplete(ModuleDefinition module)
	{
		Logger.Info(LogCategory.Export, "PatchTweenerInstantComplete: making all tweens complete instantly...");

		TypeDefinition? tweenerType = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			if (t.Name?.Value == "Tweener`1")
			{
				tweenerType = t;
				break;
			}
		}

		if (tweenerType is null)
		{
			Logger.Warning(LogCategory.Export, "PatchTweenerInstantComplete: Tweener`1 type not found");
			return;
		}

		MethodDefinition? tweenAsyncMethod = null;
		MethodDefinition? completeInstantlyMethod = null;
		foreach (MethodDefinition m in tweenerType.Methods)
		{
			if (m.Name?.Value == "TweenAsync")
				tweenAsyncMethod = m;
			if (m.Name?.Value == "CompleteInstantly")
				completeInstantlyMethod = m;
		}

		if (tweenAsyncMethod is null || completeInstantlyMethod is null)
		{
			Logger.Warning(LogCategory.Export, "PatchTweenerInstantComplete: TweenAsync or CompleteInstantly not found");
			return;
		}

		IMethodDescriptor? getCompletedTask = null;
		foreach (TypeDefinition t in module.GetAllTypes())
		{
			foreach (MethodDefinition m in t.Methods)
			{
				if (m.CilMethodBody is null) continue;
				foreach (CilInstruction instr in m.CilMethodBody.Instructions)
				{
					if (instr.Operand is IMethodDescriptor md && md.Name?.Value == "get_CompletedTask" && getCompletedTask is null)
						getCompletedTask = md;
				}
			}
		}

		if (getCompletedTask is null)
		{
			Logger.Warning(LogCategory.Export, "PatchTweenerInstantComplete: get_CompletedTask not found");
			return;
		}

		tweenAsyncMethod.CilMethodBody = new CilMethodBody();
		tweenAsyncMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg_0);
		tweenAsyncMethod.CilMethodBody.Instructions.Add(CilOpCodes.Call, completeInstantlyMethod);
		tweenAsyncMethod.CilMethodBody.Instructions.Add(CilOpCodes.Call, getCompletedTask);
		tweenAsyncMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
		tweenAsyncMethod.CilMethodBody.MaxStack = 1;

		Logger.Info(LogCategory.Export, "PatchTweenerInstantComplete: successfully patched TweenAsync to CompleteInstantly + return CompletedTask");
	}
}
