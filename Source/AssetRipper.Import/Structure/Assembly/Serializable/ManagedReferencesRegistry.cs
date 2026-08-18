using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.SerializationLogic;
using System.Collections.Generic;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public sealed class ManagedReferencesRegistry
{
	public int Version { get; set; }
	public List<ManagedReference> References { get; } = new();

	private readonly Dictionary<long, ManagedReference> _referenceMap = new();

	public void Read(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags, IAssemblyManager assemblyManager)
	{
		long start = reader.Position;
		int versionNumber = reader.ReadInt32();
		
		Logger.Info(LogCategory.Import, $"ManagedReferencesRegistry.Read: Start={start}, Version={versionNumber}, Position={reader.Position}");

		if (versionNumber == 1)
		{
			ReadVersion1(ref reader, version, flags, assemblyManager);
		}
		else if (versionNumber == 2)
		{
			ReadVersion2(ref reader, version, flags, assemblyManager);
		}
		else
		{
			throw new NotSupportedException($"ManagedReferencesRegistry version {versionNumber} is not supported!");
		}
	}
	
	private void ReadVersion1(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags, IAssemblyManager assemblyManager)
	{
		// Version 1 format:
		// - List of ReferencedObject (no size prefix, terminated by rid=0)
		// - Each ReferencedObject:
		//   - ReferencedManagedType type
		//     - string class
		//     - string ns
		//     - string asm
		//   - ReferencedObjectData data
		// - Terminator: rid=0
		
		int index = 0;
		while (true)
		{
			long startPos = reader.Position;
			ManagedReference reference = new();
			
			Logger.Info(LogCategory.Import, $"  Reference {index}: Position={reader.Position}");

			// Read type information
			string @class = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Class={@class}, Position={reader.Position}");
			
			string @namespace = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Namespace={@namespace}, Position={reader.Position}");
			
			string assembly = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Assembly={assembly}, Position={reader.Position}");
			
			// Check for terminator (empty class name or "Terminus" class)
			if (string.IsNullOrEmpty(@class) || @class == "Terminus")
			{
				Logger.Info(LogCategory.Import, $"    Terminator found at position {reader.Position}");
				break;
			}
			
			reference.Class = @class;
			reference.Namespace = @namespace;
			reference.Assembly = assembly;
			reference.Rid = index + 1;

			// Read data
			if (assemblyManager != null)
			{
				try
				{
					ScriptIdentifier scriptID = assemblyManager.GetScriptID(assembly, @namespace, @class);
					if (assemblyManager.TryGetSerializableType(scriptID, version, out SerializableType? serializableType, out string? failureReason))
					{
						Logger.Info(LogCategory.Import, $"      Reading data for type {@class}");
						Logger.Info(LogCategory.Import, $"      Field count: {serializableType.Fields.Count}");
						for (int f = 0; f < serializableType.Fields.Count; f++)
						{
							SerializableType.Field field = serializableType.Fields[f];
							Logger.Info(LogCategory.Import, $"        Field {f}: {field.Type.Name} {field.Name} (ArrayDepth={field.ArrayDepth}, Type={field.Type.Type})");
						}
						SerializableStructure structure = new(serializableType, 0);
						structure.Read(ref reader, version, flags);
						reference.Data = structure;
						Logger.Info(LogCategory.Import, $"      Finished reading data, position now {reader.Position}");
					}
					else
					{
						Logger.Warning(LogCategory.Import, $"      Could not resolve type: {failureReason}");
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Import, $"      Failed to resolve type: {ex.Message}");
				}
			}

			References.Add(reference);
			_referenceMap[reference.Rid] = reference;
			index++;
		}
	}
	
	private void ReadVersion2(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags, IAssemblyManager assemblyManager)
	{
		// Version 2 format:
		// - int size
		// - Array of ReferencedObject
		//   - Each ReferencedObject:
		//     - SInt64 rid
		//     - ReferencedManagedType type
		//       - string class
		//       - string ns
		//       - string asm
		//     - ReferencedObjectData data
		
		int count = reader.ReadInt32();
		Logger.Info(LogCategory.Import, $"  Count={count}, Position={reader.Position}");

		for (int i = 0; i < count; i++)
		{
			long startPos = reader.Position;
			ManagedReference reference = new();
			
			// Read rid
			reference.Rid = reader.ReadInt64();
			
			Logger.Info(LogCategory.Import, $"  Reference {i}: Rid={reference.Rid}, Position={reader.Position}");

			// Read type information
			string @class = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Class={@class}, Position={reader.Position}");
			
			string @namespace = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Namespace={@namespace}, Position={reader.Position}");
			
			string assembly = ReadLengthPrefixedString(ref reader);
			Logger.Info(LogCategory.Import, $"    Assembly={assembly}, Position={reader.Position}");
			
			reference.Class = @class;
			reference.Namespace = @namespace;
			reference.Assembly = assembly;

			// Read data
			if (!string.IsNullOrEmpty(@class) && @class != "Terminus" && assemblyManager != null)
			{
				try
				{
					ScriptIdentifier scriptID = assemblyManager.GetScriptID(assembly, @namespace, @class);
					if (assemblyManager.TryGetSerializableType(scriptID, version, out SerializableType? serializableType, out string? failureReason))
					{
						Logger.Info(LogCategory.Import, $"      Reading data for type {@class}");
						SerializableStructure structure = new(serializableType, 0);
						structure.Read(ref reader, version, flags);
						reference.Data = structure;
						Logger.Info(LogCategory.Import, $"      Finished reading data, position now {reader.Position}");
					}
					else
					{
						Logger.Warning(LogCategory.Import, $"      Could not resolve type: {failureReason}");
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Import, $"      Failed to resolve type: {ex.Message}");
				}
			}

			References.Add(reference);
			_referenceMap[reference.Rid] = reference;
		}
	}

	private static string ReadLengthPrefixedString(ref EndianSpanReader reader)
	{
		// Read the length (int32)
		int length = reader.ReadInt32();
		long posAfterLength = reader.Position;
		
		Logger.Info(LogCategory.Import, $"      ReadLengthPrefixedString: length={length}, position={posAfterLength}");
		
		if (length == 0)
		{
			return string.Empty;
		}
		
		if (length < 0 || length > reader.Length - reader.Position)
		{
			throw new ArgumentOutOfRangeException($"Invalid string length: {length}. Remaining bytes: {reader.Length - reader.Position}");
		}
		
		// Read the characters
		System.ReadOnlySpan<byte> bytesSpan = reader.ReadBytesExact(length);
		byte[] bytes = bytesSpan.ToArray();
		string result = System.Text.Encoding.UTF8.GetString(bytes);
		long posAfterData = reader.Position;
		
		Logger.Info(LogCategory.Import, $"      ReadLengthPrefixedString: result={result}, position before align={posAfterData}");
		
		// Align to 4-byte boundary
		reader.Align();
		long posAfterAlign = reader.Position;
		
		Logger.Info(LogCategory.Import, $"      ReadLengthPrefixedString: position after align={posAfterAlign}, aligned by {posAfterAlign - posAfterData} bytes");
		
		return result;
	}

	private static string ReadNullTerminatedString(ref EndianSpanReader reader)
	{
		List<byte> bytes = new();
		while (true)
		{
			byte b = reader.ReadByte();
			if (b == 0)
			{
				break;
			}
			bytes.Add(b);
		}
		
		// Align to 4-byte boundary
		reader.Align();
		
		return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
	}

	public ManagedReference? GetReference(long rid)
	{
		return _referenceMap.TryGetValue(rid, out ManagedReference? reference) ? reference : null;
	}

	public void AddReference(ManagedReference reference)
	{
		References.Add(reference);
		_referenceMap[reference.Rid] = reference;
	}
}
