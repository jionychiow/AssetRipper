using AssetRipper.Assets;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.Yaml;
using System.Text;
using System.Threading;

namespace AssetRipper.Export.UnityProjects.Project;

public abstract class YamlExporterBase : IAssetExporter
{
	public abstract bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection);

	public bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		using (Stream fileStream = fileSystem.File.Create(path))
		{
			using InvariantStreamWriter streamWriter = new InvariantStreamWriter(fileStream, UTF8);
			YamlWriter writer = new();
			ProjectYamlWalker walker = new(container);
			YamlDocument doc = walker.ExportYamlDocument(asset);
			writer.AddDocument(doc);
			writer.Write(streamWriter);
		}

		TryFixEmptyFile(path, fileSystem);
		return true;
	}

	public void Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem, Action<IExportContainer, IUnityObjectBase, string, FileSystem>? callback)
	{
		Export(container, asset, path, fileSystem);
		callback?.Invoke(container, asset, path, fileSystem);
	}

	public bool Export(IExportContainer container, IEnumerable<IUnityObjectBase> assets, string path, FileSystem fileSystem)
	{
		using (Stream fileStream = fileSystem.File.Create(path))
		{
			using InvariantStreamWriter streamWriter = new InvariantStreamWriter(fileStream, UTF8);
			YamlWriter writer = new();
			writer.WriteHead(streamWriter);
			ProjectYamlWalker walker = new(container);
			foreach (IUnityObjectBase asset in assets)
			{
				YamlDocument doc = walker.ExportYamlDocument(asset);
				writer.WriteDocument(doc);
			}
			writer.WriteTail(streamWriter);
		}

		TryFixEmptyFile(path, fileSystem);
		return true;
	}

	private void TryFixEmptyFile(string path, FileSystem fileSystem)
	{
		for (int i = 0; i < 3; i++)
		{
			try
			{
				if (new FileInfo(path).Length == 0)
				{
					Logger.Error(LogCategory.Export, $"空 .asset 文件检测: path='{path}' -> 写入最小 YAML 占位");
					fileSystem.File.WriteAllBytes(path, UTF8.GetBytes("%YAML 1.1\n--- {}\n"));
				}
				return;
			}
			catch (IOException)
			{
				Thread.Sleep(100);
			}
		}
	}

	public void Export(IExportContainer container, IEnumerable<IUnityObjectBase> assets, string path, FileSystem fileSystem, Action<IExportContainer, IUnityObjectBase, string, FileSystem>? callback)
	{
		throw new NotSupportedException("Yaml supports only single file export");
	}

	public AssetType ToExportType(IUnityObjectBase asset)
	{
		return AssetType.Serialized;
	}

	public bool ToUnknownExportType(Type type, out AssetType assetType)
	{
		assetType = AssetType.Serialized;
		return true;
	}

	private static readonly Encoding UTF8 = new UTF8Encoding(false);
}
