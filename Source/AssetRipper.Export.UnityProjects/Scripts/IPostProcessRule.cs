namespace AssetRipper.Export.UnityProjects.Scripts;

public interface IPostProcessRule
{
	string Name { get; }

	bool TryFix(string content, out string fixedContent, out string fixRecord);
}