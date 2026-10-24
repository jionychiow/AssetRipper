using System.Text;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.C;

/// <summary>
/// 轻量 Unity .asset YAML 解析器（任务12）：缩进层级 + `字段名: 值` 语法逐行解析，
/// 不引入完整 YAML 库（规避 Unity YAML 锚点/标签兼容问题），
/// 产出与原始侧同构的"点分路径 → 值"表。
/// </summary>
internal sealed class UnityAssetYamlParser
{
	/// <summary>解析 .asset 文件为（点分路径 → 值）表；仅解析 MonoBehaviour 主体字段段。</summary>
	public static Dictionary<string, string> Parse(string assetPath)
	{
		Dictionary<string, string> fields = new(StringComparer.Ordinal);
		string[] lines = File.ReadAllLines(assetPath, Encoding.UTF8);

		// 跳过 YAML 头（%YAML / --- !u!114），找到 MonoBehaviour: 段后的字段区
		Stack<(int Indent, string Name)> stack = new();
		bool inMonoBehaviour = false;
		bool fieldsStarted = false;

		foreach (string rawLine in lines)
		{
			string line = rawLine;
			if (line.Length == 0)
			{
				continue;
			}
			if (line.StartsWith("%YAML", StringComparison.Ordinal) || line.StartsWith("--- !u!", StringComparison.Ordinal))
			{
				stack.Clear();
				inMonoBehaviour = false;
				fieldsStarted = false;
				continue;
			}
			if (!inMonoBehaviour)
			{
				if (line.TrimEnd().EndsWith("MonoBehaviour:", StringComparison.Ordinal))
				{
					inMonoBehaviour = true;
				}
				continue;
			}

			int indent = GetIndent(line);
			string trimmed = line.Trim();

			// m_Script / m_GameObject 等引擎字段属于 MonoBehaviour 元数据，跳过但记录
			int colon = FindColon(trimmed);
			if (colon < 0)
			{
				continue;
			}
			string name = trimmed[..colon].Trim();
			string value = trimmed[(colon + 1)..].Trim();

			// 弹出栈中缩进 >= 当前的项
			while (stack.Count > 0 && stack.Peek().Indent >= indent)
			{
				stack.Pop();
			}
			string path = stack.Count == 0 ? name : $"{string.Join(".", stack.Reverse().Select(s => s.Name))}.{name}";

			if (value.Length == 0)
			{
				// 复杂字段头（无值）——可能是嵌套结构、数组或键值对容器
				stack.Push((indent, name));
				fields[path] = "";
			}
			else if (value == "[]")
			{
				fields[path] = "array[0]";
			}
			else
			{
				fields[path] = value;
			}
			fieldsStarted = true;
		}

		_ = fieldsStarted;
		return fields;
	}

	private static int GetIndent(string line)
	{
		int count = 0;
		while (count < line.Length && line[count] == ' ')
		{
			count++;
		}
		return count;
	}

	private static int FindColon(string trimmed)
	{
		// 在引号外找第一个冒号（Unity YAML 标准格式 `name: value`）
		bool inQuotes = false;
		for (int i = 0; i < trimmed.Length; i++)
		{
			char c = trimmed[i];
			if (c == '"')
			{
				inQuotes = !inQuotes;
			}
			else if (c == ':' && !inQuotes)
			{
				return i;
			}
		}
		return -1;
	}
}