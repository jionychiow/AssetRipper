namespace AssetRipper.Export.UnityProjects.Project;

internal static class UniTaskPlayerLoopInitializerTemplate
{
	public static string GetTemplate()
	{
		return """
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;

[InitializeOnLoad]
public static class UniTaskPlayerLoopInitializer
{
	private static bool _wasPlaying = false;
	private static Action[] _yielderRunDelegates = null;
	private static Action[] _runnerRunDelegates = null;
	private static Type _pltTypeStatic;
	private static FieldInfo _yieldersFieldStatic;
	private static FieldInfo _runnersFieldStatic;
	private static MethodInfo _initMethodStatic;
	private static Array _cachedYieldersStatic;
	private static Array _cachedRunnersStatic;

	static UniTaskPlayerLoopInitializer()
	{
		MainThreadSyncContext.Install();
		InitializePlayerLoop();
		EditorApplication.update += OnEditorUpdate;
	}

	private static void InitializePlayerLoop()
	{
		try
		{
			Type plt = Type.GetType("UniRx.Async.PlayerLoopHelper, UniRx.Async");
			if (plt == null) { Debug.LogError("[UniTaskPL] PlayerLoopHelper not found."); return; }

			MethodInfo init = plt.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static);
			init?.Invoke(null, null);
			CacheRunDelegates(plt);
			Debug.Log("[UniTaskPL] Initialized.");
		}
		catch (Exception ex) { Debug.LogError($"[UniTaskPL] Init failed: {ex}"); }
	}

	private static void CacheRunDelegates(Type plt)
	{
		try
		{
			_pltTypeStatic = plt;
			_yieldersFieldStatic = plt.GetField("yielders", BindingFlags.NonPublic | BindingFlags.Static);
			_runnersFieldStatic = plt.GetField("runners", BindingFlags.NonPublic | BindingFlags.Static);
			_initMethodStatic = plt.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static);
			if (_yieldersFieldStatic == null || _runnersFieldStatic == null) return;

			FieldInfo yf = _yieldersFieldStatic;
			FieldInfo rf = _runnersFieldStatic;

			Array ya = yf.GetValue(null) as Array;
			Array ra = rf.GetValue(null) as Array;
			if (ya == null || ra == null) return;

			_cachedYieldersStatic = ya;
			_cachedRunnersStatic = ra;
			_yielderRunDelegates = new Action[ya.Length];
			for (int i = 0; i < ya.Length; i++)
			{
				object y = ya.GetValue(i);
				if (y == null) continue;
				MethodInfo rm = y.GetType().GetMethod("Run");
				if (rm != null) _yielderRunDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), y, rm);
			}

			_runnerRunDelegates = new Action[ra.Length];
			for (int i = 0; i < ra.Length; i++)
			{
				object r = ra.GetValue(i);
				if (r == null) continue;
				MethodInfo rm = r.GetType().GetMethod("Run");
				if (rm != null) _runnerRunDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), r, rm);
			}
		}
		catch (Exception ex) { Debug.LogError($"[UniTaskPL] Cache failed: {ex}"); }
	}

	private static void RefreshStaticDelegates()
	{
		if (_yieldersFieldStatic == null || _runnersFieldStatic == null) return;
		try
		{
			Array ya = _yieldersFieldStatic.GetValue(null) as Array;
			Array ra = _runnersFieldStatic.GetValue(null) as Array;
			if (ya == null || ra == null)
			{
				_initMethodStatic?.Invoke(null, null);
				ya = _yieldersFieldStatic.GetValue(null) as Array;
				ra = _runnersFieldStatic.GetValue(null) as Array;
			}
			if (ya == null || ra == null) return;
			if (ya == _cachedYieldersStatic && ra == _cachedRunnersStatic && _yielderRunDelegates != null) return;

			_cachedYieldersStatic = ya;
			_cachedRunnersStatic = ra;
			_yielderRunDelegates = new Action[ya.Length];
			for (int i = 0; i < ya.Length; i++)
			{
				object y = ya.GetValue(i);
				if (y == null) continue;
				MethodInfo rm = y.GetType().GetMethod("Run");
				if (rm != null) _yielderRunDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), y, rm);
			}
			_runnerRunDelegates = new Action[ra.Length];
			for (int i = 0; i < ra.Length; i++)
			{
				object r = ra.GetValue(i);
				if (r == null) continue;
				MethodInfo rm = r.GetType().GetMethod("Run");
				if (rm != null) _runnerRunDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), r, rm);
			}
		}
		catch { }
	}

	private static void ProcessFallback()
	{
		RefreshStaticDelegates();
		if (_yielderRunDelegates != null)
			for (int i = 0; i < _yielderRunDelegates.Length; i++)
				_yielderRunDelegates[i]?.Invoke();
		if (_runnerRunDelegates != null)
			for (int i = 0; i < _runnerRunDelegates.Length; i++)
				_runnerRunDelegates[i]?.Invoke();
	}

	private static void OnEditorUpdate()
	{
		bool isPlaying = EditorApplication.isPlaying;
		if (isPlaying && !_wasPlaying)
		{
			InitializePlayerLoop();
		}
		_wasPlaying = isPlaying;

		MainThreadSyncContext.Instance?.ProcessQueue();
		ProcessFallback();
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void OnRuntimeInitialize()
	{
		InitializePlayerLoop();
		GameObject go = new GameObject("[UniTaskPL]");
		go.AddComponent<UniTaskPlayerLoopFallbackRunner>();
		GameObject.DontDestroyOnLoad(go);
		go.hideFlags = HideFlags.HideInHierarchy;
	}
}

public class MainThreadSyncContext : SynchronizationContext
{
	private readonly ConcurrentQueue<(SendOrPostCallback, object)> _queue = new ConcurrentQueue<(SendOrPostCallback, object)>();
	private static MainThreadSyncContext _instance;
	private static SynchronizationContext _previous;
	private static int _mainThreadId;

	public static MainThreadSyncContext Instance => _instance;

	public static void Install()
	{
		if (_instance != null) return;
		_mainThreadId = Thread.CurrentThread.ManagedThreadId;
		_previous = SynchronizationContext.Current;
		_instance = new MainThreadSyncContext();
		SynchronizationContext.SetSynchronizationContext(_instance);
		Debug.Log($"[UniTaskPL] MainThreadSyncContext installed. Previous: {(_previous?.GetType().Name ?? "null")}");
	}

	public override void Post(SendOrPostCallback d, object state)
	{
		if (d == null) return;
		_queue.Enqueue((d, state));
	}

	public override void Send(SendOrPostCallback d, object state)
	{
		if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
			d(state);
		else
		{
			using (ManualResetEventSlim done = new ManualResetEventSlim())
			{
				SendOrPostCallback cb = s => { d(s); done.Set(); };
				_queue.Enqueue((cb, state));
				done.Wait();
			}
		}
	}

	public void ProcessQueue()
	{
		while (_queue.TryDequeue(out var item))
		{
			try { item.Item1(item.Item2); }
			catch (Exception ex) { Debug.LogError($"[UniTaskPL] SyncContext error: {ex}"); }
		}
	}

	public override SynchronizationContext CreateCopy() => this;
}

public class UniTaskPlayerLoopFallbackRunner : MonoBehaviour
{
	private static UniTaskPlayerLoopFallbackRunner _instance;
	private Type _pltType;
	private FieldInfo _yieldersField;
	private FieldInfo _runnersField;
	private MethodInfo _initMethod;
	private Array _cachedYielders;
	private Array _cachedRunners;
	private Action[] _yielderDelegates;
	private Action[] _runnerDelegates;
	private int _frameCount;
	private bool _functionsRegistered;
	private bool _reflectionInitialized;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void EnsureInstance()
	{
		if (_instance == null)
		{
			GameObject go = new GameObject("[UniTaskPL]");
			_instance = go.AddComponent<UniTaskPlayerLoopFallbackRunner>();
			GameObject.DontDestroyOnLoad(go);
			go.hideFlags = HideFlags.HideInHierarchy;
		}
	}

	private void Awake()
	{
		_instance = this;
		InitReflection();
		RefreshDelegates();
	}

	private void InitReflection()
	{
		if (_reflectionInitialized) return;
		try
		{
			_pltType = Type.GetType("UniRx.Async.PlayerLoopHelper, UniRx.Async");
			if (_pltType == null) return;
			_yieldersField = _pltType.GetField("yielders", BindingFlags.NonPublic | BindingFlags.Static);
			_runnersField = _pltType.GetField("runners", BindingFlags.NonPublic | BindingFlags.Static);
			_initMethod = _pltType.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static);
			_reflectionInitialized = (_yieldersField != null && _runnersField != null);
		}
		catch (Exception ex) { Debug.LogError($"[UniTaskPL] InitReflection: {ex}"); }
	}

	private void EnsureInit()
	{
		if (_initMethod == null) return;
		Array ya = _yieldersField.GetValue(null) as Array;
		if (ya == null)
		{
			try { _initMethod.Invoke(null, null); } catch { }
		}
	}

	private void RefreshDelegates()
	{
		if (!_reflectionInitialized) { InitReflection(); if (!_reflectionInitialized) return; }
		try
		{
			EnsureInit();
			Array ya = _yieldersField.GetValue(null) as Array;
			Array ra = _runnersField.GetValue(null) as Array;
			if (ya == null || ra == null) return;

			_cachedYielders = ya;
			_cachedRunners = ra;
			_yielderDelegates = new Action[ya.Length];
			for (int i = 0; i < ya.Length; i++)
			{
				object y = ya.GetValue(i);
				if (y == null) continue;
				MethodInfo rm = y.GetType().GetMethod("Run");
				if (rm != null) _yielderDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), y, rm);
			}
			_runnerDelegates = new Action[ra.Length];
			for (int i = 0; i < ra.Length; i++)
			{
				object r = ra.GetValue(i);
				if (r == null) continue;
				MethodInfo rm = r.GetType().GetMethod("Run");
				if (rm != null) _runnerDelegates[i] = (Action)Delegate.CreateDelegate(typeof(Action), r, rm);
			}
		}
		catch (Exception ex) { Debug.LogError($"[UniTaskPL] RefreshDelegates: {ex}"); }
	}

	private void Update()
	{
		if (!_reflectionInitialized) { InitReflection(); }
		if (!_reflectionInitialized) return;

		MainThreadSyncContext.Instance?.ProcessQueue();

		Array curYa = _yieldersField.GetValue(null) as Array;
		Array curRa = _runnersField.GetValue(null) as Array;

		if (curYa == null || curRa == null)
		{
			EnsureInit();
			curYa = _yieldersField.GetValue(null) as Array;
			curRa = _runnersField.GetValue(null) as Array;
		}

		if (curYa != _cachedYielders || curRa != _cachedRunners || _yielderDelegates == null)
		{
			RefreshDelegates();
		}

		if (_yielderDelegates != null)
			for (int i = 0; i < _yielderDelegates.Length; i++)
				_yielderDelegates[i]?.Invoke();

		if (_runnerDelegates != null)
			for (int i = 0; i < _runnerDelegates.Length; i++)
				_runnerDelegates[i]?.Invoke();

		if (!_functionsRegistered)
			TryRegisterExpressionFunctions();

		_frameCount++;
	}

	private void TryRegisterExpressionFunctions()
	{
		try
		{
			Type engineType = Type.GetType("Naninovel.Engine, Elringus.Naninovel.Runtime");
			if (engineType == null) return;
			PropertyInfo initProp = engineType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
			if (initProp == null || !(bool)initProp.GetValue(null)) return;

			Type evalType = Type.GetType("Naninovel.ExpressionEvaluator, Elringus.Naninovel.Runtime");
			if (evalType == null) return;

			FieldInfo typesCacheField = engineType.GetField("typesCache", BindingFlags.NonPublic | BindingFlags.Static);
			typesCacheField?.SetValue(null, null);

			FieldInfo functionsField = evalType.GetField("functions", BindingFlags.NonPublic | BindingFlags.Static);
			if (functionsField != null)
			{
				var functionsList = functionsField.GetValue(null) as System.Collections.IList;
				functionsList?.Clear();
			}

			MethodInfo initMethod = evalType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
			initMethod?.Invoke(null, null);

			_functionsRegistered = true;
			Debug.Log("[UniTaskPL] Expression functions re-registered (including custom functions from Assembly-CSharp).");
		}
		catch (Exception ex)
		{
			Debug.LogError($"[UniTaskPL] Expression function registration failed: {ex.Message}");
		}
	}

	private void OnDestroy()
	{
		if (_instance == this) _instance = null;
	}
}
""";
	}
}