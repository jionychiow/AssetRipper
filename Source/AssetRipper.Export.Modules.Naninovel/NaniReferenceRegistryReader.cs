using AssetRipper.Import.Logging;
using AssetRipper.IO.Endian;

namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniReferenceRegistryReader
{
	private readonly NaniTypeLayoutTable layoutTable;

	public NaniReferenceRegistryReader(NaniTypeLayoutTable layoutTable)
	{
		this.layoutTable = layoutTable;
	}

	public Dictionary<long, NaniManagedReference> Read(ref EndianSpanReader reader)
	{
		Dictionary<long, NaniManagedReference> map = new();
		int versionNumber = reader.ReadInt32();

		switch (versionNumber)
		{
			case 1:
				ReadVersion1(ref reader, map);
				break;
			case 2:
				ReadVersion2(ref reader, map);
				break;
			default:
				throw new NotSupportedException($"ManagedReferencesRegistry version {versionNumber} is not supported!");
		}

		return map;
	}

	private void ReadVersion1(ref EndianSpanReader reader, Dictionary<long, NaniManagedReference> map)
	{
		int index = 0;
		while (true)
		{
			string @class = ReadLengthPrefixedString(ref reader);
			string @namespace = ReadLengthPrefixedString(ref reader);
			string assembly = ReadLengthPrefixedString(ref reader);

			if (string.IsNullOrEmpty(@class) || @class == "Terminus")
			{
				break;
			}

			NaniManagedReference reference = new()
			{
				Rid = index,
				Class = @class,
				Namespace = @namespace,
				Assembly = assembly,
			};

			ReadReferencedObjectData(ref reader, reference);
			map[reference.Rid] = reference;
			index++;
		}
	}

	private void ReadVersion2(ref EndianSpanReader reader, Dictionary<long, NaniManagedReference> map)
	{
		int count = reader.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			long rid = reader.ReadInt64();
			string @class = ReadLengthPrefixedString(ref reader);
			string @namespace = ReadLengthPrefixedString(ref reader);
			string assembly = ReadLengthPrefixedString(ref reader);

			NaniManagedReference reference = new()
			{
				Rid = rid,
				Class = @class,
				Namespace = @namespace,
				Assembly = assembly,
			};

			if (!string.IsNullOrEmpty(@class) && @class != "Terminus")
			{
				ReadReferencedObjectData(ref reader, reference);
			}

			map[reference.Rid] = reference;
		}
	}

	private static string ReadLengthPrefixedString(ref EndianSpanReader reader)
	{
		reader.Align();
		int length = reader.ReadInt32();
		if (length == 0)
		{
			return string.Empty;
		}

		if (length < 0 || length > reader.Length - reader.Position)
		{
			throw new ArgumentOutOfRangeException($"Invalid string length: {length}. Remaining bytes: {reader.Length - reader.Position}");
		}

		System.ReadOnlySpan<byte> bytesSpan = reader.ReadBytesExact(length);
		string result = System.Text.Encoding.UTF8.GetString(bytesSpan);
		reader.Align();
		return result;
	}

	private static bool ReadAlignedBoolean(ref EndianSpanReader reader)
	{
		reader.Align();
		return reader.ReadBoolean();
	}

	private static int ReadAlignedArraySize(ref EndianSpanReader reader)
	{
		reader.Align();
		return reader.ReadInt32();
	}

	private void ReadReferencedObjectData(ref EndianSpanReader reader, NaniManagedReference reference)
	{
		NaniTypeLayout? layout = layoutTable.GetLayout(reference.Class);
		if (layout is null)
		{
			SkipUnknownTypeData(ref reader, reference);
			return;
		}

		try
		{
			Dictionary<string, object?> fields = new();
			foreach (NaniTypeField field in layout.Fields)
			{
				fields[field.Name] = ReadField(ref reader, field);
			}
			reference.Fields = fields;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"[NaninovelExport] Failed to decode {reference.Class}: {ex.Message}");
			reference.Fields = null;
		}
	}

	private static void SkipUnknownTypeData(ref EndianSpanReader reader, NaniManagedReference reference)
	{
		throw new NotSupportedException($"[NaninovelExport] Unknown type '{reference.Class}' (ns={reference.Namespace}, asm={reference.Assembly}) in registry. Cannot skip unknown data — add this type to NaniTypeLayoutTable.");
	}

	private static object? ReadField(ref EndianSpanReader reader, NaniTypeField field)
	{
		switch (field.FieldType)
		{
			case NaniFieldType.Int32:
				return reader.ReadInt32();

			case NaniFieldType.String:
				return ReadLengthPrefixedString(ref reader);

			case NaniFieldType.Boolean:
				return ReadAlignedBoolean(ref reader);

			case NaniFieldType.Single:
				return reader.ReadSingle();

			case NaniFieldType.ManagedReference:
				if (field.IsArray)
				{
					int size = ReadAlignedArraySize(ref reader);
					List<long> rids = new(size);
					for (int i = 0; i < size; i++)
					{
						rids.Add(reader.ReadInt32());
					}
					return rids;
				}
				else
				{
					return (long)reader.ReadInt32();
				}

			case NaniFieldType.PlaybackSpot:
				return ReadPlaybackSpot(ref reader);

			case NaniFieldType.CommandParameter:
				return ReadCommandParameter(ref reader, field.ParameterSubType);

			case NaniFieldType.DynamicValue:
				return ReadDynamicValue(ref reader);

			default:
				throw new NotSupportedException($"Field type {field.FieldType} is not supported.");
		}
	}

	private static NaniPlaybackSpot ReadPlaybackSpot(ref EndianSpanReader reader)
	{
		string scriptName = ReadLengthPrefixedString(ref reader);
		int lineIndex = reader.ReadInt32();
		int inlineIndex = reader.ReadInt32();
		return new NaniPlaybackSpot(scriptName, lineIndex, inlineIndex);
	}

	private static NaniParameterField ReadCommandParameter(ref EndianSpanReader reader, string parameterSubType)
	{
		NaniParameterField param = new()
		{
			ParameterAlias = string.Empty,
		};

		switch (parameterSubType)
		{
		case "BooleanParameter":
			param.Value = ReadAlignedBoolean(ref reader);
			param.HasValue = ReadAlignedBoolean(ref reader);
			ReadDynamicValue(ref reader, param);
			break;

		case "StringParameter":
			param.Value = ReadLengthPrefixedString(ref reader);
			param.HasValue = ReadAlignedBoolean(ref reader);
			ReadDynamicValue(ref reader, param);
			break;

		case "IntegerParameter":
			param.Value = reader.ReadInt32();
			param.HasValue = ReadAlignedBoolean(ref reader);
			ReadDynamicValue(ref reader, param);
			break;

		case "DecimalParameter":
			param.Value = reader.ReadSingle();
			param.HasValue = ReadAlignedBoolean(ref reader);
			ReadDynamicValue(ref reader, param);
			break;

		case "NamedStringParameter":
			param.IsNamed = true;
			string nameValue = ReadLengthPrefixedString(ref reader);
			bool nameHasValue = ReadAlignedBoolean(ref reader);
			string namedValue = ReadLengthPrefixedString(ref reader);
			bool namedValueHasValue = ReadAlignedBoolean(ref reader);
			param.NamedName = nameHasValue ? nameValue : null;
			param.NamedValue = namedValueHasValue ? namedValue : null;
			param.NamedValueHasValue = namedValueHasValue;
			param.HasValue = ReadAlignedBoolean(ref reader);
			ReadDynamicValue(ref reader, param);
			break;

	case "StringListParameter":
		param.IsList = true;
		int listSize = ReadAlignedArraySize(ref reader);
		List<string> items = new(listSize);
		for (int i = 0; i < listSize; i++)
		{
			string itemValue = ReadLengthPrefixedString(ref reader);
			bool itemHasValue = ReadAlignedBoolean(ref reader);
			items.Add(itemHasValue ? itemValue : null!);
		}
		param.Value = items;
		param.HasValue = ReadAlignedBoolean(ref reader);
		ReadDynamicValue(ref reader, param);
		break;

	case "DecimalListParameter":
		param.IsList = true;
		int decListSize = ReadAlignedArraySize(ref reader);
		List<string> decItems = new(decListSize);
		for (int i = 0; i < decListSize; i++)
		{
			float itemVal = reader.ReadSingle();
			bool itemHasVal = ReadAlignedBoolean(ref reader);
			decItems.Add(itemHasVal ? itemVal.ToString() : null!);
		}
		param.Value = decItems;
		param.HasValue = ReadAlignedBoolean(ref reader);
		ReadDynamicValue(ref reader, param);
		break;

	case "NamedBooleanParameter":
		param.IsNamed = true;
		string nbName = ReadLengthPrefixedString(ref reader);
		bool nbNameHas = ReadAlignedBoolean(ref reader);
		bool nbVal = ReadAlignedBoolean(ref reader);
		bool nbValHas = ReadAlignedBoolean(ref reader);
		param.NamedName = nbNameHas ? nbName : null;
		param.NamedValueHasValue = nbValHas;
		param.HasValue = ReadAlignedBoolean(ref reader);
		ReadDynamicValue(ref reader, param);
		break;

	case "NamedDecimalListParameter":
		param.IsList = true;
		param.IsNamed = true;
		int ndlSize = ReadAlignedArraySize(ref reader);
		List<string> ndlItems = new(ndlSize);
		for (int i = 0; i < ndlSize; i++)
		{
			ReadLengthPrefixedString(ref reader);
			ReadAlignedBoolean(ref reader);
			float ndlVal = reader.ReadSingle();
			bool ndlValHas = ReadAlignedBoolean(ref reader);
			ReadAlignedBoolean(ref reader);
			ndlItems.Add(ndlValHas ? ndlVal.ToString() : null!);
		}
		param.Value = ndlItems;
		param.HasValue = ReadAlignedBoolean(ref reader);
		ReadDynamicValue(ref reader, param);
		break;

	case "NamedBooleanListParameter":
		param.IsList = true;
		param.IsNamed = true;
		int nblSize = ReadAlignedArraySize(ref reader);
		List<string> nblItems = new(nblSize);
		for (int i = 0; i < nblSize; i++)
		{
			ReadLengthPrefixedString(ref reader);
			ReadAlignedBoolean(ref reader);
			ReadAlignedBoolean(ref reader);
			ReadAlignedBoolean(ref reader);
			ReadAlignedBoolean(ref reader);
		}
		param.Value = nblItems;
		param.HasValue = ReadAlignedBoolean(ref reader);
		ReadDynamicValue(ref reader, param);
		break;

		default:
			throw new NotSupportedException($"Parameter sub type {parameterSubType} is not supported.");
		}

		return param;
	}

	private static void ReadDynamicValue(ref EndianSpanReader reader, NaniParameterField param)
	{
		NaniPlaybackSpot playbackSpot = ReadPlaybackSpot(ref reader);
		string valueText = ReadLengthPrefixedString(ref reader);
		int expressionsSize = ReadAlignedArraySize(ref reader);
		string[] expressions = new string[expressionsSize];
		for (int i = 0; i < expressionsSize; i++)
		{
			expressions[i] = ReadLengthPrefixedString(ref reader);
		}

		param.ValueText = valueText;
		param.Expressions = expressions;
		param.DynamicValue = expressions.Length > 0;
	}

	private static NaniParameterField ReadDynamicValue(ref EndianSpanReader reader)
	{
		NaniParameterField param = new();
		ReadDynamicValue(ref reader, param);
		return param;
	}
}