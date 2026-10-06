using System.Runtime.InteropServices;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public static class FmodDllSelector
{
	public static string GetDllName() => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.X64 or Architecture.Arm64 => "fmod_x64.dll",
		Architecture.X86 => "fmod_x86.dll",
		_ => IntPtr.Size == 8 ? "fmod_x64.dll" : "fmod_x86.dll",
	};

	public static string GetArchitecture() => IntPtr.Size == 8 ? "x64" : "x86";
}