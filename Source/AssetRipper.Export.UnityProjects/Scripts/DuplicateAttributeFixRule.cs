namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class DuplicateAttributeFixRule : IPostProcessRule
{
	public string Name => "DuplicateAttributeFix";

	private static readonly string[] NonRepeatableAttributes =
	[
		"NonSerialized",
		"Serializable",
		"Obsolete",
		"SerializeField",
		"HideInInspector",
		"SerializeReference",
		"NonReorderable",
		"DisallowMultipleComponent",
		"ExecuteInEditMode",
		"CreateAssetMenu",
		"HelpURL",
		"AddComponentMenu",
		"DefaultExecutionOrder",
		"CanEditMultipleObjects",
		"CustomEditor",
		"CustomPropertyDrawer",
		"InitializeOnLoad",
		"InitializeOnLoadMethod",
		"UnityEditorCallback",
		"PreferenceItem",
		"DrawGizmo",
		"GizmoColor",
		"Icon",
		"AssemblyIsEditorAssembly",
	];

	private static readonly string[] RepeatableAttributes =
	[
		"Conditional",
		"AttributeUsage",
		"SuppressMessage",
		"Tooltip",
		"Header",
		"Space",
		"FormerlySerializedAs",
		"MenuItem",
		"ContextMenu",
		"RequireComponent",
	];

	public bool TryFix(string content, out string fixedContent, out string fixRecord)
	{
		fixedContent = content;
		fixRecord = string.Empty;

		if (string.IsNullOrEmpty(content))
		{
			return false;
		}

		string[] lines = content.Split('\n');
		List<string> result = new(lines.Length);
		HashSet<string> seenAttributes = [];
		int removedCount = 0;

		foreach (string line in lines)
		{
			string trimmed = line.Trim();
			string? attributeName = ExtractAttributeName(trimmed);

			if (attributeName is not null && IsNonRepeatable(attributeName))
			{
				if (seenAttributes.Contains(attributeName))
				{
					removedCount++;
					continue;
				}
				seenAttributes.Add(attributeName);
			}
			else
			{
				seenAttributes.Clear();
			}

			result.Add(line);
		}

		if (removedCount == 0)
		{
			return false;
		}

		fixedContent = string.Join("\n", result);
		fixRecord = $"Removed {removedCount} duplicate attribute(s)";
		return true;
	}

	private static string? ExtractAttributeName(string trimmedLine)
	{
		if (!trimmedLine.StartsWith("[", StringComparison.Ordinal) || !trimmedLine.EndsWith("]", StringComparison.Ordinal))
		{
			return null;
		}

		string inner = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
		int parenIndex = inner.IndexOf('(');
		if (parenIndex >= 0)
		{
			inner = inner.Substring(0, parenIndex).Trim();
		}

		return inner.Length > 0 ? inner : null;
	}

	private static bool IsNonRepeatable(string attributeName)
	{
		foreach (string repeatable in RepeatableAttributes)
		{
			if (attributeName.Equals(repeatable, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		foreach (string nonRepeatable in NonRepeatableAttributes)
		{
			if (attributeName.Equals(nonRepeatable, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}