using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.SourceGenerated.Classes.ClassID_1031;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using System.Text.Json;

namespace AssetRipper.Export.UnityProjects.Miscellaneous;

public sealed class TextAssetExportCollection : AssetExportCollection<ITextAsset>
{
	private const string DllExtension = "dll";
	private const string XmlExtension = "xml";
	private const string JsonExtension = "json";
	private const string TxtExtension = "txt";
	private const string BytesExtension = "bytes";

	private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		"cs", "dll", "exe", "bat", "cmd", "ps1", "vbs", "js", "msi", "scr", "com"
	};

	private static readonly HashSet<string> UnrecognizedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{

	};

	public TextAssetExportCollection(TextAssetExporter assetExporter, ITextAsset asset) : base(assetExporter, asset)
	{
	}

	protected override string GetExportExtension(IUnityObjectBase asset)
	{
		string? extension = asset.GetBestExtension();
		if (extension is not null)
		{
			if (DangerousExtensions.Contains(extension) || UnrecognizedExtensions.Contains(extension))
			{
				return TxtExtension;
			}
			return extension;
		}
		return ((TextAssetExporter)AssetExporter).ExportMode switch
		{
			TextExportMode.Txt => TxtExtension,
			TextExportMode.Parse => GetExtension((ITextAsset)asset),
			_ => BytesExtension,
		};
	}

	private static string GetExtension(ITextAsset asset)
	{
		ReadOnlySpan<byte> data = asset.Script_C49.Data;
		if (IsXml(data))
		{
			return XmlExtension;
		}
		else if (IsPeDll(data))
		{
			return DllExtension;
		}
		string text = asset.Script_C49.String;
		if (IsValidJson(text))
		{
			return JsonExtension;
		}
		else if (IsPlainText(text))
		{
			return TxtExtension;
		}
		else
		{
			return BytesExtension;
		}
	}

	private static bool IsXml(ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty)
		{
			return false;
		}
		if (data[0] == 0x3C)
		{
			return true;
		}
		if (data.Length >= 4 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF && data[3] == 0x3C)
		{
			return true;
		}
		return false;
	}

	private static bool IsPeDll(ReadOnlySpan<byte> data)
	{
		if (data.Length < 2)
		{
			return false;
		}
		return data[0] == 0x4D && data[1] == 0x5A;
	}

	private static bool IsValidJson(string text)
	{
		try
		{
			using JsonDocument? parsed = JsonDocument.Parse(text);
			return parsed != null;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsPlainText(string text) => text.All(c => !char.IsControl(c) || char.IsWhiteSpace(c));

	protected override ITextScriptImporter CreateImporter(IExportContainer container)
	{
		ITextScriptImporter importer = TextScriptImporter.Create(container.File, container.ExportVersion);
		if (importer.Has_AssetBundleName_R() && Asset.AssetBundleName is not null)
		{
			importer.AssetBundleName_R = Asset.AssetBundleName;
		}
		return importer;
	}
}
