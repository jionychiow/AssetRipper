using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Shaders.Enhance;

public sealed class TransitionalShaderEnhancer : IPostExporter
{
	private const string ShaderRelativePath = "Resources/naninovel/shaders/transitionaltexture.shader";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			string shaderPath = fileSystem.Path.Join(settings.AssetsPath, ShaderRelativePath);
			if (!fileSystem.File.Exists(shaderPath))
			{
				Logger.Info(LogCategory.Export, $"Transitional shader not found at {shaderPath}, skipping enhancement.");
				return;
			}

			string content = File.ReadAllText(shaderPath);
			if (!NeedsEnhancement(content))
			{
				Logger.Info(LogCategory.Export, "Transitional shader already enhanced, skipping.");
				return;
			}

			if (!content.Contains("_TintColor"))
			{
				Logger.Warning(LogCategory.Export, "Transitional shader missing _TintColor — unexpected, enhancement may break tint behavior.");
			}

			string enhancedContent = InjectTransitionLogic(content);
			if (string.Equals(enhancedContent, content, StringComparison.Ordinal))
			{
				Logger.Info(LogCategory.Export, "Transitional shader enhancement produced no changes, skipping write.");
				return;
			}

			File.WriteAllText(shaderPath, enhancedContent);
			Logger.Info(LogCategory.Export, $"Enhanced transitional shader at {shaderPath}");
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"TransitionalShaderEnhancer failed: {ex}");
		}
	}

	private static bool NeedsEnhancement(string shaderContent)
	{
		return !shaderContent.Contains("tex2D(_TransitionTex");
	}

	private static string InjectTransitionLogic(string shaderContent)
	{
		int subShaderIndex = shaderContent.IndexOf("SubShader", StringComparison.Ordinal);
		if (subShaderIndex == -1)
		{
			Logger.Warning(LogCategory.Export, "Could not locate SubShader block in transitional shader, skipping enhancement.");
			return shaderContent;
		}

		int braceStart = shaderContent.IndexOf('{', subShaderIndex);
		if (braceStart == -1)
		{
			Logger.Warning(LogCategory.Export, "Could not locate SubShader opening brace in transitional shader, skipping enhancement.");
			return shaderContent;
		}

		int braceEnd = FindMatchingBrace(shaderContent, braceStart);
		if (braceEnd == -1)
		{
			Logger.Warning(LogCategory.Export, "Could not locate SubShader closing brace in transitional shader, skipping enhancement.");
			return shaderContent;
		}

		string beforeSubShader = shaderContent[..subShaderIndex];
		string afterSubShader = shaderContent[(braceEnd + 1)..];

		string enhancedSubShader = TransitionalShaderTemplate.GetEnhancedShaderBody();
		return beforeSubShader + enhancedSubShader + afterSubShader;
	}

	private static int FindMatchingBrace(string text, int openIndex)
	{
		int depth = 0;
		for (int i = openIndex; i < text.Length; i++)
		{
			if (text[i] == '{')
			{
				depth++;
			}
			else if (text[i] == '}')
			{
				depth--;
				if (depth == 0)
				{
					return i;
				}
			}
		}

		return -1;
	}
}