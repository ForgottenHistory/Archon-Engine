using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Core.Common
{
    /// <summary>
    /// ENGINE: compiles an assembly's methods ahead of their first call, on a background thread.
    ///
    /// Under Mono (the editor and Mono players) a method is compiled the first time it runs. A simulation
    /// step that runs for the first time (the first daily tick, the first battle, the first war) pays for
    /// every method it reaches in that one frame: a visible hitch that later runs don't have. Start this
    /// while the game loads and those methods are already compiled when they are first needed.
    ///
    /// It only compiles: no static constructor runs and no game state is touched, so it is safe beside
    /// loading. Open generic methods and types are skipped (there is nothing to compile until they are
    /// closed). IL2CPP builds are compiled ahead of time, so there it does nothing.
    ///
    /// The GAME decides which assemblies and when: <c>JitWarmup.Start(typeof(MyGame).Assembly)</c>.
    /// </summary>
    public static class JitWarmup
    {
        static int running;          // threads still working
        static int methods;          // methods compiled so far
        static long elapsedTicks;    // summed over the threads
        static int generation;       // bumps when play mode restarts: older threads stop

        /// <summary>False where code is compiled ahead of time (IL2CPP): Start does nothing.</summary>
        public static bool Supported
        {
            get
            {
#if ENABLE_IL2CPP
                return false;
#else
                return true;
#endif
            }
        }

        /// <summary>True while a warm-up thread is still compiling.</summary>
        public static bool IsRunning => Volatile.Read(ref running) > 0;
        /// <summary>Methods compiled since play mode started.</summary>
        public static int MethodCount => Volatile.Read(ref methods);
        /// <summary>Background thread time spent compiling, in milliseconds.</summary>
        public static double ElapsedMs => Volatile.Read(ref elapsedTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Interlocked.Increment(ref generation); // a thread from the last play session stops at its next method
            Volatile.Write(ref methods, 0);
            Volatile.Write(ref elapsedTicks, 0);
        }

        /// <summary>Compiles every method of the assemblies on one background thread, in the order given.
        /// Returns at once. Null entries are skipped.</summary>
        public static void Start(params Assembly[] assemblies)
        {
            if (!Supported || assemblies == null || assemblies.Length == 0) return;
            var list = new List<Assembly>();
            foreach (var a in assemblies)
                if (a != null && !list.Contains(a)) list.Add(a);
            if (list.Count == 0) return;
            int mine = Volatile.Read(ref generation);
            Interlocked.Increment(ref running);
            var thread = new Thread(() =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    foreach (var a in list) Compile(a, mine);
                }
                catch (Exception)
                {
                    // a warm-up must never take the game down: whatever is left compiles on first call, as before
                }
                finally
                {
                    Interlocked.Add(ref elapsedTicks, watch.ElapsedTicks);
                    Interlocked.Decrement(ref running);
                }
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "JitWarmup" };
            thread.Start();
        }

        const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.Public | BindingFlags.NonPublic;

        static void Compile(Assembly assembly, int mine)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; } // the ones that did load
            if (types == null) return;
            foreach (var type in types)
            {
                if (type == null || type.IsInterface || type.ContainsGenericParameters) continue;
                MethodBase[] found;
                try
                {
                    var m = type.GetMethods(All);
                    var c = type.GetConstructors(All);
                    found = new MethodBase[m.Length + c.Length];
                    m.CopyTo(found, 0);
                    c.CopyTo(found, m.Length);
                }
                catch (Exception) { continue; }
                foreach (var method in found)
                {
                    if (Volatile.Read(ref generation) != mine) return; // play mode restarted
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                    var impl = method.GetMethodImplementationFlags();
                    if ((impl & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime | MethodImplAttributes.Native)) != 0) continue;
                    try
                    {
                        // asking for the entry point makes Mono compile the method
                        method.MethodHandle.GetFunctionPointer();
                        Interlocked.Increment(ref methods);
                    }
                    catch (Exception)
                    {
                        // a method the runtime can't compile on its own: left for its first call
                    }
                }
            }
        }
    }
}
