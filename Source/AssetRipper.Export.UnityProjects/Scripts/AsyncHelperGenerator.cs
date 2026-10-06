using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Scripts;

public static class AsyncHelperGenerator
{
	private const string ResourceRequestAwaiterFileName = "ResourceRequestAwaiterExtensions.cs";
	private const string AwaitLoadAsyncPattern = "await Resources.LoadAsync";

	private const string HelperFileContent = """
		using System;
		using System.Runtime.CompilerServices;
		using UnityEngine;

		public static class ResourceRequestAwaiterExtensions
		{
			public static ResourceRequestAwaiter GetAwaiter(this ResourceRequest request)
			{
				return new ResourceRequestAwaiter(request);
			}
		}

		public struct ResourceRequestAwaiter : INotifyCompletion
		{
			private readonly ResourceRequest request;

			public ResourceRequestAwaiter(ResourceRequest request)
			{
				this.request = request;
			}

			public bool IsCompleted => request.isDone;

			public void OnCompleted(Action continuation)
			{
				request.completed += _ => continuation();
			}

			public UnityEngine.Object GetResult()
			{
				return request.asset;
			}
		}
		""";

	public static bool TryGenerateHelpers(string scriptsDirectory, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(scriptsDirectory))
		{
			return false;
		}

		string? targetDirectory = FindDirectoryWithPattern(scriptsDirectory, fileSystem, AwaitLoadAsyncPattern);

		if (targetDirectory is null)
		{
			return false;
		}

		string helperFilePath = fileSystem.Path.Join(targetDirectory, ResourceRequestAwaiterFileName);
		if (fileSystem.File.Exists(helperFilePath))
		{
			return false;
		}

		fileSystem.File.WriteAllText(helperFilePath, HelperFileContent);
		WriteMetaFile(helperFilePath, fileSystem);
		Logger.Info(LogCategory.Export, $"Generated async helper file in: {targetDirectory}");
		return true;
	}

	private static string? FindDirectoryWithPattern(string directory, FileSystem fileSystem, string pattern)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(directory, "*.cs"))
		{
			try
			{
				string content = fileSystem.File.ReadAllText(file);
				if (content.Contains(pattern, StringComparison.Ordinal))
				{
					return directory;
				}
			}
			catch
			{
			}
		}

		foreach (string subDir in fileSystem.Directory.EnumerateDirectories(directory))
		{
			string? found = FindDirectoryWithPattern(subDir, fileSystem, pattern);
			if (found is not null)
			{
				return found;
			}
		}

		return null;
	}

	private static void WriteMetaFile(string csFilePath, FileSystem fileSystem)
	{
		string metaPath = csFilePath + ".meta";
		string guid = Guid.NewGuid().ToString("N");
		string metaContent = $$"""
			fileFormatVersion: 2
			guid: {{guid}}
			MonoImporter:
			  serializedVersion: 2
			  externalObjects: {}
			  defaultReferences: []
			  executionOrder: 0
			  icon: {fileID: 0}
			  userData:
			  assetBundleName:
			  assetBundleVariant:
			""";
		if (!metaContent.EndsWith('\n'))
		{
			metaContent += Environment.NewLine;
		}
		fileSystem.File.WriteAllText(metaPath, metaContent);
	}
}
