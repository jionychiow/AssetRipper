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

		GenerateRevealableTextFixerScript(settings.AssetsPath, fileSystem);

		if (_patchSettings.EnableRenderOrderFix)
		{
			FixWidePrefabRenderOrder(settings.AssetsPath, fileSystem, _patchSettings.TargetCanvasSortingOrder);
			FixNaninovelShaderRenderConfig(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableRuntimeRenderOrderFixer)
		{
			GenerateDialogueRenderOrderFixer(settings.AssetsPath, fileSystem, _patchSettings.TargetCanvasSortingOrder);
		}
		if (_patchSettings.EnableRenderOrderDiagnostic)
		{
			GenerateRenderOrderDiagnosticScript(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableShaderIncludeFix)
		{
			FixNaninovelShaderIncludePath(settings.AssetsPath, fileSystem);
		}
		if (_patchSettings.EnableAudioListenerFix)
		{
			GenerateAudioListenerFixer(settings.AssetsPath, fileSystem);
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
			if (!fileSystem.Directory.Exists(naniShaderDir) || !fileSystem.Directory.Exists(tmpShaderDir))
			{
				Logger.Warning(LogCategory.Export, $"Shader directories not found: nani={naniShaderDir}, tmp={tmpShaderDir}");
				return;
			}

			string[] cgincFiles = ["TMPro_Properties.cginc", "TMPro.cginc", "TMPro_Surface.cginc"];
			foreach (string cgincFile in cgincFiles)
			{
				string srcPath = fileSystem.Path.Join(tmpShaderDir, cgincFile);
				string dstPath = fileSystem.Path.Join(naniShaderDir, cgincFile);
				if (fileSystem.File.Exists(srcPath) && !fileSystem.File.Exists(dstPath))
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

	private static void GenerateDialogueRenderOrderFixer(string assetsPath, FileSystem fileSystem, int targetSortingOrder)
	{
		try
		{
			string editorDir = fileSystem.Path.Join(assetsPath, "Editor");
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string scriptPath = fileSystem.Path.Join(editorDir, "DialogueRenderOrderFixer.cs");
			string scriptContent = $$"""
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

[InitializeOnLoad]
public static class DialogueRenderOrderFixer
{
    private static bool isPlaying;
    private static int frameCount;
    private static Type revealableTextType;
    private static int charClipRectId;

    static DialogueRenderOrderFixer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            isPlaying = true;
            frameCount = 0;
            charClipRectId = Shader.PropertyToID("_CharClipRect");
            revealableTextType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Naninovel.UI.RevealableUIText");
                if (t != null) { revealableTextType = t; break; }
            }
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
            FixCanvasSortingOrder();
            FixTextHierarchy();
            FixTextMaterialAndColor();
            FixCharClipRect();
            if (frameCount <= 5 || frameCount % 60 == 0) LogDiagnosticInfo();
        }
        catch { }
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
        if (revealableTextType == null) return;
        var components = UnityEngine.Object.FindObjectsOfType(revealableTextType);
        foreach (var comp in components)
        {
            var graphic = comp as Graphic;
            if (graphic == null) continue;

            var c = graphic.color;
            if (c.r < 0.5f || c.g < 0.5f || c.b < 0.5f || c.a < 0.5f)
            {
                c.r = 1f; c.g = 1f; c.b = 1f; c.a = 1f;
                graphic.color = c;
            }

            var mat = graphic.material;
            if (mat == null || mat.shader == null || mat.shader.name != "Naninovel/RevealableText")
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
        if (revealableTextType == null) return;
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
 		PatchAllInitializeAsyncLogging(module);
 		PatchCGGalleryPanelInit(module);
 		LogProjectResourceLoaderStructure(module);
 		PatchRunResourceLoaderLogging(module);
 		PatchInstantiateUIsAsyncLogging(module);
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

		PatchMethodEntryLog(module, "StateManager", "PerformPostEngineInitializationTasks", debugLogMethod, "NANI_POSTINIT: ENTER PerformPostEngineInitializationTasks");
		PatchMethodEntryLog(module, "UIManager", "InstantiateUIsAsync", debugLogMethod, "NANI_POSTINIT: ENTER InstantiateUIsAsync");
		PatchMethodEntryLog(module, "ResourceLoader", "LoadAllAsync", debugLogMethod, "NANI_POSTINIT: ENTER ResourceLoader.LoadAllAsync");

		PatchAsyncMethodCompletionLog(module, "PerformPostEngineInitializationTasks", debugLogMethod, "NANI_POSTINIT: COMPLETED PerformPostEngineInitializationTasks");
		PatchAsyncMethodCompletionLog(module, "InstantiateUIsAsync", debugLogMethod, "NANI_POSTINIT: COMPLETED InstantiateUIsAsync");
		PatchAsyncMethodCompletionLog(module, "LoadAllAsync", debugLogMethod, "NANI_POSTINIT: COMPLETED ResourceLoader.LoadAllAsync");
		PatchAsyncMethodCompletionLog(module, "RunAsync", debugLogMethod, "NANI_TRACE: COMPLETED RunAsync");
		PatchAsyncMethodCompletionLog(module, "LoadResourceAsync", debugLogMethod, "NANI_TRACE: COMPLETED LoadResourceAsync");
		PatchAsyncMethodCompletionLog(module, "ApplyManagedTextAsync", debugLogMethod, "NANI_TRACE: COMPLETED ApplyManagedTextAsync");
		PatchAsyncMethodCompletionLog(module, "SelectLocaleAsync", debugLogMethod, "NANI_TRACE: COMPLETED SelectLocaleAsync");
		PatchMethodEntryLogContains(module, "ProjectResourceLoader", "RunAsync", debugLogMethod, "NANI_TRACE: ENTER ProjectResourceLoader.RunAsync");
		PatchMethodEntryLogContains(module, "ResourceProvider", "LoadResourceAsync", debugLogMethod, "NANI_TRACE: ENTER LoadResourceAsync");
		PatchMethodEntryLogContains(module, "ResourceLoader", "LoadAndHoldAllAsync", debugLogMethod, "NANI_POSTINIT: ENTER LoadAndHoldAllAsync");
		PatchAsyncMethodCompletionLog(module, "LoadAndHoldAllAsync", debugLogMethod, "NANI_POSTINIT: COMPLETED LoadAndHoldAllAsync");
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
