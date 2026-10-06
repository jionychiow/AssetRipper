namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class AccessModifierFixRule : IPostProcessRule
{
	public string Name => "AccessModifierFix";

	private static readonly (string Pattern, string Replacement, string Description)[] FixPatterns =
	[
		("public override void Dispose(bool", "protected override void Dispose(bool", "public→protected override void Dispose(bool)"),
		("internal override void Dispose(bool", "protected internal override void Dispose(bool", "internal→protected internal override void Dispose(bool)"),

	];

	public bool TryFix(string content, out string fixedContent, out string fixRecord)
	{
		fixedContent = content;
		fixRecord = string.Empty;

		if (string.IsNullOrEmpty(content))
		{
			return false;
		}

		List<string> appliedFixes = [];
		foreach ((string pattern, string replacement, string description) in FixPatterns)
		{
			if (content.Contains(pattern, StringComparison.Ordinal))
			{
				content = content.Replace(pattern, replacement);
				appliedFixes.Add(description);
			}
		}

		if (appliedFixes.Count == 0)
		{
			return false;
		}

		fixedContent = content;
		fixRecord = string.Join("; ", appliedFixes);
		return true;
	}
}