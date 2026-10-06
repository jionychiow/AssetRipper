using AsmResolver.DotNet;

namespace AssetRipper.Export.UnityProjects.Scripts;

/// <summary>
/// Detects third-party library assemblies by name patterns and assembly metadata.
/// Detected assemblies should be saved as DLLs rather than decompiled.
/// </summary>
public static class ThirdPartyAssemblyDetector
{
	private static readonly StringComparer OrdinalIgnoreCase = StringComparer.OrdinalIgnoreCase;

	private static readonly string[] KnownThirdPartyPrefixes =
	[
		"DOTween",
		"Naninovel",
		"Newtonsoft",
		"Zenject",
		"Extenject",
		"Klak",
		"ICSharpCode",
		"Google",
		"protobuf-net",
		"Ionic",
		"MessagePack",
		"UniRx",
		"Elringus",
		"NLayer",
	];

	private static readonly HashSet<string> KnownThirdPartyExactNames = new(OrdinalIgnoreCase)
	{
		"websocket-sharp",
		"CsvHelper",
		"System.Memory",
		"System.Buffers",
		"System.Numerics.Vectors",
	};

	/// <summary>
	/// System DLLs that Unity already includes internally.
	/// These should not be exported as precompiled DLLs because they can prevent Unity from compiling .cs files.
	/// </summary>
	private static readonly HashSet<string> SystemDllsToSkip = new(OrdinalIgnoreCase)
	{
		"System.Runtime.CompilerServices.Unsafe",
		"System.Runtime.CompilerServices.Unsafe.dll",
	};

	private static readonly HashSet<string> KnownThirdPartyCompanies = new(OrdinalIgnoreCase)
	{
		"Demigiant",
		"Newtonsoft",
		"Modest Tree",
		"Keijiro",
		"Keijiro Takahashi",
		"Google Inc.",
		"Google LLC",
		"Microsoft Corporation",
	};

	/// <summary>
	/// Checks if an assembly should be skipped entirely (not exported as DLL and not decompiled).
	/// These are system DLLs that Unity already includes internally.
	/// </summary>
	public static bool ShouldSkipAssembly(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return false;
		}

		return SystemDllsToSkip.Contains(assemblyName);
	}

	public static bool IsThirdPartyByName(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return false;
		}

		if (ShouldSkipAssembly(assemblyName))
		{
			return false;
		}

		if (ReferenceAssemblies.IsPredefinedAssembly(assemblyName))
		{
			return false;
		}

		if (UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(assemblyName))
		{
			return false;
		}

		if (KnownThirdPartyExactNames.Contains(assemblyName))
		{
			return true;
		}

		foreach (string prefix in KnownThirdPartyPrefixes)
		{
			if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public static bool IsThirdPartyAssembly(AssemblyDefinition? assembly)
	{
		if (assembly is null)
		{
			return false;
		}

		string? name = assembly.Name;
		if (ShouldSkipAssembly(name))
		{
			return false;
		}

		if (IsThirdPartyByName(name))
		{
			return true;
		}

		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		if (ReferenceAssemblies.IsPredefinedAssembly(name))
		{
			return false;
		}

		if (UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(name))
		{
			return false;
		}

		string? company = TryGetAssemblyCompany(assembly);
		if (company is not null && KnownThirdPartyCompanies.Contains(company))
		{
			return true;
		}

		if (IsStrongNamed(assembly))
		{
			return true;
		}

		return false;
	}

	private static string? TryGetAssemblyCompany(AssemblyDefinition assembly)
	{
		for (int i = 0; i < assembly.CustomAttributes.Count; i++)
		{
			CustomAttribute attribute = assembly.CustomAttributes[i];
			ITypeDefOrRef? type = attribute.Constructor?.DeclaringType;
			if (type is null)
			{
				continue;
			}

			if (type.Namespace?.Value == "System.Reflection" && type.Name?.Value == "AssemblyCompanyAttribute")
			{
				if (attribute.Signature is { FixedArguments.Count: >= 1 })
				{
					if (attribute.Signature.FixedArguments[0].Element is string company && !string.IsNullOrWhiteSpace(company))
					{
						return company;
					}
				}
			}
		}

		return null;
	}

	private static bool IsStrongNamed(AssemblyDefinition assembly)
	{
		byte[]? publicKey = assembly.PublicKey;
		if (publicKey is null || publicKey.Length == 0)
		{
			return false;
		}

		return !IsAllZeroes(publicKey);
	}

	private static bool IsAllZeroes(byte[] bytes)
	{
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] != 0)
			{
				return false;
			}
		}

		return true;
	}
}