using System.Runtime.InteropServices;

namespace AssetRipper.Export.Modules.Audio.Fmod;

#pragma warning disable SYSLIB1054
internal static class FmodNativeX64
{
	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Create(out IntPtr system);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Init(IntPtr system, int maxchannels, INITFLAGS flags, IntPtr extradriverdata);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_CreateSound(IntPtr system, byte[] data, MODE mode, ref CREATESOUNDEXINFO exinfo, out IntPtr sound);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Release(IntPtr system);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetSubSound(IntPtr sound, int index, out IntPtr subsound);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetFormat(IntPtr sound, out SOUND_TYPE type, out SOUND_FORMAT format, out int channels, out int bits);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetDefaults(IntPtr sound, out float frequency, out int priority);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetLength(IntPtr sound, out uint length, TIMEUNIT lengthtype);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Lock(IntPtr sound, uint offset, uint length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Unlock(IntPtr sound, IntPtr ptr1, IntPtr ptr2, uint len1, uint len2);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Release(IntPtr sound);

	[DllImport("fmod_x64.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_GetVersion(IntPtr system, out uint version);
}

internal static class FmodNativeX86
{
	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Create(out IntPtr system);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Init(IntPtr system, int maxchannels, INITFLAGS flags, IntPtr extradriverdata);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_CreateSound(IntPtr system, byte[] data, MODE mode, ref CREATESOUNDEXINFO exinfo, out IntPtr sound);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_Release(IntPtr system);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetSubSound(IntPtr sound, int index, out IntPtr subsound);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetFormat(IntPtr sound, out SOUND_TYPE type, out SOUND_FORMAT format, out int channels, out int bits);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetDefaults(IntPtr sound, out float frequency, out int priority);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_GetLength(IntPtr sound, out uint length, TIMEUNIT lengthtype);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Lock(IntPtr sound, uint offset, uint length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Unlock(IntPtr sound, IntPtr ptr1, IntPtr ptr2, uint len1, uint len2);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_Sound_Release(IntPtr sound);

	[DllImport("fmod_x86.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern RESULT FMOD_System_GetVersion(IntPtr system, out uint version);
}
#pragma warning restore SYSLIB1054

internal static class FmodNative
{
	private static readonly bool IsX64 = IntPtr.Size == 8;

	public static RESULT FMOD_System_Create(out IntPtr system) =>
		IsX64 ? FmodNativeX64.FMOD_System_Create(out system) : FmodNativeX86.FMOD_System_Create(out system);

	public static RESULT FMOD_System_Init(IntPtr system, int maxchannels, INITFLAGS flags, IntPtr extradriverdata) =>
		IsX64 ? FmodNativeX64.FMOD_System_Init(system, maxchannels, flags, extradriverdata) : FmodNativeX86.FMOD_System_Init(system, maxchannels, flags, extradriverdata);

	public static RESULT FMOD_System_CreateSound(IntPtr system, byte[] data, MODE mode, ref CREATESOUNDEXINFO exinfo, out IntPtr sound) =>
		IsX64 ? FmodNativeX64.FMOD_System_CreateSound(system, data, mode, ref exinfo, out sound) : FmodNativeX86.FMOD_System_CreateSound(system, data, mode, ref exinfo, out sound);

	public static RESULT FMOD_System_Release(IntPtr system) =>
		IsX64 ? FmodNativeX64.FMOD_System_Release(system) : FmodNativeX86.FMOD_System_Release(system);

	public static RESULT FMOD_Sound_GetSubSound(IntPtr sound, int index, out IntPtr subsound) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_GetSubSound(sound, index, out subsound) : FmodNativeX86.FMOD_Sound_GetSubSound(sound, index, out subsound);

	public static RESULT FMOD_Sound_GetFormat(IntPtr sound, out SOUND_TYPE type, out SOUND_FORMAT format, out int channels, out int bits) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_GetFormat(sound, out type, out format, out channels, out bits) : FmodNativeX86.FMOD_Sound_GetFormat(sound, out type, out format, out channels, out bits);

	public static RESULT FMOD_Sound_GetDefaults(IntPtr sound, out float frequency, out int priority) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_GetDefaults(sound, out frequency, out priority) : FmodNativeX86.FMOD_Sound_GetDefaults(sound, out frequency, out priority);

	public static RESULT FMOD_Sound_GetLength(IntPtr sound, out uint length, TIMEUNIT lengthtype) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_GetLength(sound, out length, lengthtype) : FmodNativeX86.FMOD_Sound_GetLength(sound, out length, lengthtype);

	public static RESULT FMOD_Sound_Lock(IntPtr sound, uint offset, uint length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_Lock(sound, offset, length, out ptr1, out ptr2, out len1, out len2) : FmodNativeX86.FMOD_Sound_Lock(sound, offset, length, out ptr1, out ptr2, out len1, out len2);

	public static RESULT FMOD_Sound_Unlock(IntPtr sound, IntPtr ptr1, IntPtr ptr2, uint len1, uint len2) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_Unlock(sound, ptr1, ptr2, len1, len2) : FmodNativeX86.FMOD_Sound_Unlock(sound, ptr1, ptr2, len1, len2);

	public static RESULT FMOD_Sound_Release(IntPtr sound) =>
		IsX64 ? FmodNativeX64.FMOD_Sound_Release(sound) : FmodNativeX86.FMOD_Sound_Release(sound);

	public static RESULT FMOD_System_GetVersion(IntPtr system, out uint version) =>
		IsX64 ? FmodNativeX64.FMOD_System_GetVersion(system, out version) : FmodNativeX86.FMOD_System_GetVersion(system, out version);
}