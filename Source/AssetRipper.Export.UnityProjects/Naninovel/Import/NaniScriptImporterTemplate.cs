namespace AssetRipper.Export.UnityProjects.Naninovel.Import;

internal static class NaniScriptImporterTemplate
{
	public static string GetTemplate()
	{
		return """
using UnityEngine;
using UnityEditor;
using Naninovel;
using System.IO;
using System.Reflection;

[InitializeOnLoad]
public static class NaniScriptImporter
{
	const string DoneKey = "NaniScriptImporter_Done_v7";
	static int _retryCount = 0;
	const int MaxRetries = 120;

	static NaniScriptImporter()
	{
		EditorApplication.update += OnEditorUpdate;
	}

	static void OnEditorUpdate()
	{
		if (EditorPrefs.GetBool(DoneKey, false))
		{
			EditorApplication.update -= OnEditorUpdate;
			return;
		}
		_retryCount++;
		if (_retryCount >= MaxRetries)
		{
			EditorApplication.update -= OnEditorUpdate;
			Debug.LogError("NaniScriptImporter: Max retries reached. Proceeding with best effort.");
			ResetEngineCaches();
			ConvertAllNaniToScript();
			EditorPrefs.SetBool(DoneKey, true);
			return;
		}
		TryConvert();
	}

	static void TryConvert()
	{
		EngineConfiguration config = null;
		try { config = Resources.Load<EngineConfiguration>("Naninovel/Configuration/EngineConfiguration"); } catch { }
		if (config == null)
			try { config = Resources.Load<EngineConfiguration>("naninovel/configuration/EngineConfiguration"); } catch { }
		if (config == null)
		{
			string configPath = "Assets/Resources/naninovel/configuration/EngineConfiguration.asset";
			try { config = AssetDatabase.LoadAssetAtPath<EngineConfiguration>(configPath); } catch { }
		}
		if (config == null)
			return;


		EditorApplication.update -= OnEditorUpdate;
		ResetEngineCaches();
		ConvertAllNaniToScript();
		EditorPrefs.SetBool(DoneKey, true);
	}

	static void ResetEngineCaches()
	{
		try
		{
			var engineType = typeof(Engine);
			var typesCacheField = engineType.GetField("typesCache", BindingFlags.NonPublic | BindingFlags.Static);
			if (typesCacheField != null)
				typesCacheField.SetValue(null, null);
			var commandType = typeof(Command);
			var cmdCacheField = commandType.GetField("commandTypesCache", BindingFlags.NonPublic | BindingFlags.Static);
			if (cmdCacheField != null)
				cmdCacheField.SetValue(null, null);
		}
		catch { }
	}

	static void ConvertAllNaniToScript()
	{
		try
		{
			string resourcesPath = Path.Combine(Application.dataPath, "Resources");
			if (!Directory.Exists(resourcesPath))
				return;

			string[] naniFiles = Directory.GetFiles(resourcesPath, "*.nani", SearchOption.AllDirectories);
			int created = 0, skipped = 0;
			foreach (string fullNaniPath in naniFiles)
			{
				string assetPath = "Assets" + fullNaniPath.Substring(Application.dataPath.Length).Replace('\\', '/');
				string scriptAssetPath = assetPath.Substring(0, assetPath.Length - ".nani".Length) + ".asset";

				var existing = AssetDatabase.LoadMainAssetAtPath(scriptAssetPath);
				if (existing is Script)
				{
					skipped++;
					continue;
				}
				if (existing != null)
					AssetDatabase.DeleteAsset(scriptAssetPath);

				try
				{
					string scriptText = File.ReadAllText(fullNaniPath);
					string scriptName = Path.GetFileNameWithoutExtension(fullNaniPath);
					Script script = Script.FromScriptText(scriptName, scriptText);
					if (script == null)
						continue;
					AssetDatabase.CreateAsset(script, scriptAssetPath);
					created++;
				}
				catch { }
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"NaniScriptImporter: Created {created} script assets, skipped {skipped} existing.");
		}
		catch { }
	}

	[MenuItem("Tools/Naninovel/Reset Script Importer")]
	public static void Reset()
	{
		EditorPrefs.SetBool(DoneKey, false);
		Debug.Log("NaniScriptImporter: Reset. Will re-convert on next editor update.");
	}
}

public class NaniScriptConverter
{
	public static void Convert()
	{
		EngineConfiguration config = null;
		try { config = Resources.Load<EngineConfiguration>("Naninovel/Configuration/EngineConfiguration"); } catch { }
		if (config == null)
			try { config = Resources.Load<EngineConfiguration>("naninovel/configuration/EngineConfiguration"); } catch { }
		if (config == null)
		{
			try { config = AssetDatabase.LoadAssetAtPath<EngineConfiguration>("Assets/Resources/naninovel/configuration/EngineConfiguration.asset"); } catch { }
		}
		if (config == null)
		{
			Debug.LogError("NaniScriptConverter: Failed to load EngineConfiguration.");
			return;
		}

		try
		{
			var engineType = typeof(Engine);
			var typesCacheField = engineType.GetField("typesCache", BindingFlags.NonPublic | BindingFlags.Static);
			if (typesCacheField != null) typesCacheField.SetValue(null, null);
			var commandType = typeof(Command);
			var cmdCacheField = commandType.GetField("commandTypesCache", BindingFlags.NonPublic | BindingFlags.Static);
			if (cmdCacheField != null) cmdCacheField.SetValue(null, null);
		}
		catch { }

		string resourcesPath = Path.Combine(Application.dataPath, "Resources");
		if (!Directory.Exists(resourcesPath)) return;

		string[] naniFiles = Directory.GetFiles(resourcesPath, "*.nani", SearchOption.AllDirectories);
		int created = 0, skipped = 0;
		foreach (string fullNaniPath in naniFiles)
		{
			string assetPath = "Assets" + fullNaniPath.Substring(Application.dataPath.Length).Replace('\\', '/');
			string scriptAssetPath = assetPath.Substring(0, assetPath.Length - ".nani".Length) + ".asset";
			var existing = AssetDatabase.LoadMainAssetAtPath(scriptAssetPath);
			if (existing is Script)
			{
				skipped++;
				continue;
			}
			if (existing != null)
				AssetDatabase.DeleteAsset(scriptAssetPath);
			try
			{
				string scriptText = File.ReadAllText(fullNaniPath);
				string scriptName = Path.GetFileNameWithoutExtension(fullNaniPath);
				Script script = Script.FromScriptText(scriptName, scriptText);
				if (script == null) continue;
				AssetDatabase.CreateAsset(script, scriptAssetPath);
				created++;
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"NaniScriptConverter: Failed {assetPath}: {ex.Message}");
			}
		}
		AssetDatabase.SaveAssets();
		EditorPrefs.SetBool("NaniScriptImporter_Done_v2", true);
		Debug.Log($"NaniScriptConverter: Created {created} script assets, skipped {skipped} existing, from {naniFiles.Length} .nani files.");
	}
}
""";
	}
}
