using System.Text;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniTextGenerator
{
	private readonly ScriptLineDecoder lineDecoder;

	public NaniTextGenerator(ScriptLineDecoder lineDecoder)
	{
		this.lineDecoder = lineDecoder;
	}

	public byte[] Generate(List<int> lineRids, Dictionary<long, NaniManagedReference> referenceMap)
	{
		StringBuilder sb = new();
		for (int i = 0; i < lineRids.Count; i++)
		{
			int rid = lineRids[i];
			if (!referenceMap.TryGetValue(rid, out NaniManagedReference? lineRef))
			{
				sb.Append($";[NaninovelExport] Dangling reference rid={rid} at line {i}").Append('\n');
				continue;
			}

			string lineText = lineDecoder.Decode(lineRef);
			sb.Append(lineText).Append('\n');
		}

		return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
	}
}