using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MC.Shared;

// Me force JIT every method in mod dll at startup (Debug build only).
// Why: we touch private game stuff through publicized dll. If game update rename field/method (and dll not
// rebuilt), or an API exist in .NET Fx refs but not in Unity Mono, boom only happen when code first run in world.
// Smoke test only see main menu. JIT at load = boom early = smoke test catch it.
// NOTE: RuntimeHelpers.PrepareMethod is EMPTY in Unity Mono (does nothing). GetFunctionPointer() really compile
// (Mono icall -> mono_jit_compile_method), same trick MonoMod use.
internal static class JitCheck
{
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Run(Assembly assembly, string[] optionalAssemblies = null)
    {
        var failures = 0;
        var compiled = 0;
        var skipped = 0;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            // Me cannot even load types. Very bad. Tell each one.
            foreach (var le in e.LoaderExceptions)
            {
                Log.Error($"JitCheck: type load failed: {le?.Message}");
            }
            return;
        }

        foreach (var type in types)
        {
            if (type.ContainsGenericParameters)
            {
                continue;
            }

            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
                {
                    continue;
                }

                try
                {
                    method.MethodHandle.GetFunctionPointer();
                    compiled++;
                }
                catch (Exception e)
                {
                    // Method use an optional mod that is not installed = expected, framework keep feature off.
                    if (IsMissingOptional(e, optionalAssemblies))
                    {
                        skipped++;
                        continue;
                    }
                    failures++;
                    Log.Error($"JitCheck: {type.FullName}.{method.Name} failed to compile: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        // Info, not Debug: disk log skip Debug by default, and smoke test log should prove check ran.
        Log.Info($"JitCheck: JIT-compiled {compiled} methods, {failures} failures, {skipped} skipped (optional mods missing).");
    }

    private static bool IsMissingOptional(Exception e, string[] optional)
    {
        if (optional == null || optional.Length == 0)
        {
            return false;
        }
        var message = e is FileNotFoundException f ? f.FileName + " " + f.Message : e.Message;
        return (e is FileNotFoundException || e is FileLoadException || e is TypeLoadException)
               && optional.Any(o => message != null && message.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
