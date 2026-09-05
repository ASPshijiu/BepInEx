using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Preloader.Core;
using BepInEx.Preloader.Core.Logging;
using BepInEx.Unity.IL2CPP.Hook;
using BepInEx.Unity.IL2CPP.Logging;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.InteropTypes;
using MonoMod.Utils;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BepInEx.Unity.IL2CPP;

public class IL2CPPChainloader : BaseChainloader<BasePlugin>
{
    private static RuntimeInvokeDetourDelegate originalInvoke;
    private static readonly DispatchCallback DeferredMacOSStart = OnDeferredMacOSStart;
    private static int chainloaderStarted;
    private static IntPtr dispatchLibrary;

    private static readonly ConfigEntry<bool> ConfigUnityLogging = ConfigFile.CoreConfig.Bind(
     "Logging", "UnityLogListening",
     true,
     "Enables showing unity log messages in the BepInEx logging system.");

    private static readonly ConfigEntry<bool> ConfigDiskWriteUnityLog = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "WriteUnityLog",
     false,
     "Include unity log messages in log file output.");


    private static INativeDetour RuntimeInvokeDetour { get; set; }

    public static IL2CPPChainloader Instance { get; set; }

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <typeparam name="T">Type of the component to add.</typeparam>
    public static T AddUnityComponent<T>() where T : Il2CppObjectBase => AddUnityComponent(typeof(T)).Cast<T>();

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <param name="t">Type of the component to add</param>
    public static Il2CppObjectBase AddUnityComponent(Type t) => Il2CppUtils.AddComponent(t);

    /// <summary>
    ///     Occurs after a plugin is instantiated and just before <see cref="BasePlugin.Load"/> is called.
    /// </summary>
    public event Action<PluginInfo, Assembly, BasePlugin> PluginLoad;

    public override void Initialize(string gameExePath = null)
    {
        base.Initialize(gameExePath);
        Instance = this;

        var gameAssembly = PlatformHelper.Is(Platform.MacOS) ? Il2CppInteropManager.GameAssemblyPath : "GameAssembly";
        if (!NativeLibrary.TryLoad(gameAssembly, typeof(IL2CPPChainloader).Assembly, null, out var il2CppHandle))
        {
            Logger.Log(LogLevel.Fatal,
                       "Could not locate Il2Cpp game assembly (GameAssembly.dll, UserAssembly.dll or libil2cpp.so). The game might be obfuscated or use a yet unsupported build of Unity.");
            return;
        }

        if (PlatformHelper.Is(Platform.MacOS))
        {
            PreloaderLogger.Log.Log(LogLevel.Debug, "Scheduling macOS chainloader on the main dispatch queue");
            dispatch_async_f(GetMainDispatchQueue(), IntPtr.Zero, DeferredMacOSStart);
            return;
        }

        var runtimeInvokePtr = NativeLibrary.GetExport(il2CppHandle, "il2cpp_runtime_invoke");
        PreloaderLogger.Log.Log(LogLevel.Debug, $"Runtime invoke pointer: 0x{runtimeInvokePtr.ToInt64():X}");
        RuntimeInvokeDetourDelegate invokeMethodDetour = OnInvokeMethod;

        RuntimeInvokeDetour =
            INativeDetour.CreateAndApply(runtimeInvokePtr, invokeMethodDetour, out originalInvoke);
        PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke patched");
    }

    private static IntPtr GetMainDispatchQueue()
    {
        dispatchLibrary = NativeLibrary.Load("libSystem.B.dylib");
        return NativeLibrary.GetExport(dispatchLibrary, "_dispatch_main_q");
    }

    private static IntPtr OnInvokeMethod(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc)
    {
        var methodName = Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method));

        var unhook = false;

        if (methodName == "Internal_ActiveSceneChanged" &&
            Interlocked.CompareExchange(ref chainloaderStarted, 1, 0) == 0)
            try
            {
                // Unhook up front so the detour fires once even if setup below throws.
                unhook = true;

                // Isolated so a missing-interop JIT failure happens inside the try (caught), not in OnInvokeMethod itself.
                ExecuteChainloader();
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Fatal, "Unable to execute IL2CPP chainloader, no plugins will be loaded");
                Logger.Log(LogLevel.Error, ex);
            }

        var result = originalInvoke(method, obj, parameters, exc);

        if (unhook)
        {
            RuntimeInvokeDetour.Dispose();

            PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke unpatched");
        }

        return result;
    }

    private static void OnDeferredMacOSStart(IntPtr context)
    {
        if (Interlocked.CompareExchange(ref chainloaderStarted, 1, 0) != 0)
            return;

        try
        {
            PreloaderLogger.Log.Log(LogLevel.Debug, "Executing deferred macOS chainloader");
            ExecuteChainloader();
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Fatal, "Unable to execute deferred macOS chainloader, no plugins will be loaded");
            Logger.Log(LogLevel.Error, ex);
        }
        finally
        {
            if (RuntimeInvokeDetour != null)
            {
                RuntimeInvokeDetour.Dispose();
                PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke unpatched");
            }
        }
    }

    private static void ExecuteChainloader()
    {
        SetupUnityLogging();
        Il2CppInteropManager.PreloadInteropAssemblies();
        Instance.Execute();
    }

    // JIT-compiled only when called here, so OnInvokeMethod needs no interop assemblies present to JIT.
    private static void SetupUnityLogging()
    {
        if (!ConfigUnityLogging.Value)
            return;

        Logger.Sources.Add(new IL2CPPUnityLogSource());

        Application.CallLogCallback("Test call after applying unity logging hook", "", LogType.Assert, true);
    }

    protected override void InitializeLoggers()
    {
        base.InitializeLoggers();

        if (!ConfigDiskWriteUnityLog.Value) DiskLogListener.BlacklistedSources.Add("Unity");

        ChainloaderLogHelper.RewritePreloaderLogs();

        Logger.Sources.Add(new IL2CPPLogSource());
    }

    public override BasePlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly)
    {
        var type = pluginAssembly.GetType(pluginInfo.TypeName);

        var pluginInstance = (BasePlugin) Activator.CreateInstance(type);

        PluginLoad?.Invoke(pluginInfo, pluginAssembly, pluginInstance);
        pluginInstance.Load();

        return pluginInstance;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RuntimeInvokeDetourDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DispatchCallback(IntPtr context);

    [DllImport("libSystem.B.dylib")]
    private static extern void dispatch_async_f(IntPtr queue, IntPtr context, DispatchCallback callback);
}
