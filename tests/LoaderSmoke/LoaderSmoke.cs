using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

// Metadata-only smoke test. Does not instantiate Unity objects or launch the game.
class LoaderSmoke
{
    static int Main(string[] args)
    {
        try
        {
            string[] references = args.Skip(1).ToArray();
            AssemblyLoadContext.Default.Resolving += (context, name) => {
                if (name.Name == "ModMenu") throw new FileNotFoundException("Mod Menu deliberately absent for this test.");
                foreach (string folder in references)
                {
                    string file = Path.Combine(folder, name.Name + ".dll");
                    if (File.Exists(file)) return context.LoadFromAssemblyPath(Path.GetFullPath(file));
                }
                return null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
            var types = assembly.GetTypes();
            var plugin = types.Single(t => t.FullName == "RouteRunner.Plugin");
            Assert(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin");
            var callbacks = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic;
            Assert(plugin.GetMethod("OnGUI", callbacks) == null);
            Assert(types.Single(t => t.FullName == "RouteRunner.PanelView").GetMethod("OnGUI", callbacks) != null);
            var attributes = plugin.GetCustomAttributesData();
            var dependency = attributes.Single(a => a.AttributeType.Name == "BepInDependency");
            Assert((string)dependency.ConstructorArguments[0].Value == "kestrel.straftat.modmenu");
            // BepInEx flags: HardDependency=1, SoftDependency=2.
            Assert((int)dependency.ConstructorArguments[1].Value == 2);
            Assert(!AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "ModMenu"));
            Console.WriteLine("PASS: all plugin types and loader metadata resolve without ModMenu.dll; dependency is optional.");
            Console.WriteLine("PASS: IMGUI callback belongs only to the independently disabled PanelView.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Assert(bool condition) { if (!condition) throw new Exception("Optional dependency loader check failed."); }
}
