// Dev bridge: lets an agent or script run static editor methods while this project is open in the editor
// (batch mode is locked out then). Drop Temp/ClaudeBridge/<id>.cmd holding one line "Namespace.Type.Method"
// (optionally "|arg1|arg2" for string args); the result or exception is written to <id>.out. An empty file
// Temp/ClaudeBridge/refresh.req triggers AssetDatabase.Refresh (recompiles after code edits made outside Unity).
// Temp/ClaudeBridge/alive.txt is rewritten on every domain reload. Editor-only; safe to delete.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class ClaudeBridge
{
    const string Dir = "Temp/ClaudeBridge";
    static double next;

    static ClaudeBridge()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "alive.txt"), DateTime.Now.ToString("o"));
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.5;
        if (File.Exists(Path.Combine(Dir, "refresh.req")))
        {
            File.Delete(Path.Combine(Dir, "refresh.req"));
            AssetDatabase.Refresh();
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        foreach (var cmd in Directory.GetFiles(Dir, "*.cmd"))
        {
            var line = File.ReadAllText(cmd).Trim();
            File.Delete(cmd);
            var outPath = Path.ChangeExtension(cmd, ".out");
            try
            {
                var parts = line.Split('|');
                int dot = parts[0].LastIndexOf('.');
                var typeName = parts[0].Substring(0, dot);
                var method = parts[0].Substring(dot + 1);
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null)
                           ?? throw new Exception("Type not found: " + typeName);
                var args = parts.Skip(1).ToArray();
                var m = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length)
                        ?? throw new Exception("Method not found: " + method + " with " + args.Length + " args");
                var result = m.Invoke(null, args.Cast<object>().ToArray());
                string text = result is Array arr ? string.Join("\n", arr.Cast<object>()) : result?.ToString() ?? "ok";
                File.WriteAllText(outPath, "OK\n" + text);
            }
            catch (Exception e)
            {
                File.WriteAllText(outPath, "ERR\n" + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e));
            }
        }
    }
}
