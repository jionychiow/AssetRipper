namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>数据源归类：主数据 / sharedassets / level场景 / Addressables bundle / 文件系统。</summary>
internal static class SourceCategoryClassifier
{
	public const string MainData = "main data";
	public const string SharedAssets = "sharedassets";
	public const string LevelScene = "level scene";
	public const string AddressablesBundle = "Addressables bundle";
	public const string FileSystem = "file system";

	public static string ClassifyCollection(string collectionName)
	{
		if (collectionName.StartsWith("cab-", StringComparison.OrdinalIgnoreCase))
		{
			return AddressablesBundle;
		}
		if (collectionName.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase))
		{
			return SharedAssets;
		}
		if (collectionName.StartsWith("level", StringComparison.OrdinalIgnoreCase))
		{
			return LevelScene;
		}
		return MainData;
	}
}