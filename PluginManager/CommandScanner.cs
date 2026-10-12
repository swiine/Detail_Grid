using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

namespace PluginManager
{
    /// <summary>Finds [CommandMethod] methods in an assembly the same way NETLOAD does.</summary>
    internal static class CommandScanner
    {
        public static Type[] GetLoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>().ToArray(); }
        }

        public static IEnumerable<(MethodInfo Method, CommandMethodAttribute Attribute)> Find(Assembly asm)
        {
            // If the assembly lists its command classes, only those are scanned.
            var types = asm.GetCustomAttributes<CommandClassAttribute>().Select(a => a.Type).OfType<Type>().ToArray();
            if (types.Length == 0)
                types = GetLoadableTypes(asm);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var type in types)
                foreach (var method in type.GetMethods(flags))
                    foreach (var attr in method.GetCustomAttributes<CommandMethodAttribute>())
                        yield return (method, attr);
        }
    }
}
