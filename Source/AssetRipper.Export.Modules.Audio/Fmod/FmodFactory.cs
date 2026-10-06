namespace AssetRipper.Export.Modules.Audio.Fmod;

public static class FmodFactory
{
	public static RESULT System_Create(out FmodSystem? system)
	{
		system = null;
		RESULT result = FmodNative.FMOD_System_Create(out IntPtr ptr);
		if (result == RESULT.OK)
		{
			system = new FmodSystem(ptr);
		}
		return result;
	}
}