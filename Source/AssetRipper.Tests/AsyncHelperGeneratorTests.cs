using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.IO.Files;

namespace AssetRipper.Tests;

public class AsyncHelperGeneratorTests
{
	private string CreateTempDir()
	{
		string path = Path.Combine(Path.GetTempPath(), "AsyncHelperTest_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private void CleanupTempDir(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, true);
		}
	}

	[Test]
	public void TryGenerateHelpers_WhenNoAwaitPattern_ReturnsFalse()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string testFile = fileSystem.Path.Join(tempDir, "TestScript.cs");
			fileSystem.File.WriteAllText(testFile, "public class TestScript { }");

			bool result = AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			Assert.That(result, Is.False);
			string helperPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs");
			Assert.That(fileSystem.File.Exists(helperPath), Is.False);
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}

	[Test]
	public void TryGenerateHelpers_WhenAwaitPatternPresent_ReturnsTrue()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string testFile = fileSystem.Path.Join(tempDir, "VideoControl.cs");
			fileSystem.File.WriteAllText(testFile, """
				using UnityEngine;
				public class VideoControl : MonoBehaviour
				{
					private async void LoadVideoAsync(string v)
					{
						var obj = await Resources.LoadAsync<VideoClip>("Video/" + v);
					}
				}
				""");

			bool result = AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			Assert.That(result, Is.True);
			string helperPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs");
			Assert.That(fileSystem.File.Exists(helperPath), Is.True);
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}

	[Test]
	public void TryGenerateHelpers_WhenAwaitPatternInSubdirectory_GeneratesInSubdirectory()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string subDir = fileSystem.Path.Join(tempDir, "SubFolder");
			fileSystem.Directory.Create(subDir);
			string testFile = fileSystem.Path.Join(subDir, "Loader.cs");
			fileSystem.File.WriteAllText(testFile, "var obj = await Resources.LoadAsync<VideoClip>(\"path\");");

			bool result = AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			Assert.That(result, Is.True);
			string helperPath = fileSystem.Path.Join(subDir, "ResourceRequestAwaiterExtensions.cs");
			Assert.That(fileSystem.File.Exists(helperPath), Is.True);
			string rootHelperPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs");
			Assert.That(fileSystem.File.Exists(rootHelperPath), Is.False);
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}

	[Test]
	public void TryGenerateHelpers_WhenHelperAlreadyExists_ReturnsFalse()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string testFile = fileSystem.Path.Join(tempDir, "VideoControl.cs");
			fileSystem.File.WriteAllText(testFile, "var obj = await Resources.LoadAsync<VideoClip>(\"path\");");
			string helperPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs");
			fileSystem.File.WriteAllText(helperPath, "// existing helper");

			bool result = AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			Assert.That(result, Is.False);
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}

	[Test]
	public void TryGenerateHelpers_WhenDirectoryDoesNotExist_ReturnsFalse()
	{
		FileSystem fileSystem = LocalFileSystem.Instance;
		string nonExistentPath = fileSystem.Path.Join(Path.GetTempPath(), "NonExistentDir_" + Guid.NewGuid().ToString("N"));

		bool result = AsyncHelperGenerator.TryGenerateHelpers(nonExistentPath, fileSystem);

		Assert.That(result, Is.False);
	}

	[Test]
	public void TryGenerateHelpers_GeneratesMetaFile()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string testFile = fileSystem.Path.Join(tempDir, "VideoControl.cs");
			fileSystem.File.WriteAllText(testFile, "var obj = await Resources.LoadAsync<VideoClip>(\"path\");");

			AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			string metaPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs.meta");
			Assert.That(fileSystem.File.Exists(metaPath), Is.True);
			string metaContent = fileSystem.File.ReadAllText(metaPath);
			Assert.That(metaContent, Does.Contain("fileFormatVersion: 2"));
			Assert.That(metaContent, Does.Contain("guid:"));
			Assert.That(metaContent, Does.Contain("MonoImporter"));
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}

	[Test]
	public void TryGenerateHelpers_GeneratedFileContainsGetAwaiterExtension()
	{
		string tempDir = CreateTempDir();
		try
		{
			FileSystem fileSystem = LocalFileSystem.Instance;
			string testFile = fileSystem.Path.Join(tempDir, "VideoControl.cs");
			fileSystem.File.WriteAllText(testFile, "var obj = await Resources.LoadAsync<VideoClip>(\"path\");");

			AsyncHelperGenerator.TryGenerateHelpers(tempDir, fileSystem);

			string helperPath = fileSystem.Path.Join(tempDir, "ResourceRequestAwaiterExtensions.cs");
			string helperContent = fileSystem.File.ReadAllText(helperPath);
			Assert.That(helperContent, Does.Contain("GetAwaiter"));
			Assert.That(helperContent, Does.Contain("ResourceRequest"));
			Assert.That(helperContent, Does.Contain("INotifyCompletion"));
			Assert.That(helperContent, Does.Contain("IsCompleted"));
			Assert.That(helperContent, Does.Contain("GetResult"));
		}
		finally
		{
			CleanupTempDir(tempDir);
		}
	}
}
