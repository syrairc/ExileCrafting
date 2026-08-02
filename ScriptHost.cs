using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ExileCrafting;

public sealed class ScriptHost
{
    sealed class CacheEntry
    {
        public DateTime LastWriteUtc;
        public Script<object> Script;
        public string CompileError;
    }

    readonly Dictionary<string, CacheEntry> _cache = new();
    readonly string _scriptsDirectory;

    // runs once per loaded copy of this assembly - fine in practice since ExileAPI has no
    // plugin-reload API that would load a second copy. without this, a script trying to resolve
    // our own dynamically-loaded assembly by name can fail to find it and throw at RunAsync time
    // instead of at compile time.
    static ScriptHost()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            name.Name == typeof(CraftContext).Assembly.GetName().Name ? typeof(CraftContext).Assembly : null;
    }

    public ScriptHost(string scriptsDirectory)
    {
        _scriptsDirectory = scriptsDirectory;
        Directory.CreateDirectory(_scriptsDirectory);
    }

    public IReadOnlyList<string> DiscoverScripts() =>
        Directory.GetFiles(_scriptsDirectory, "*.csx")
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToList();

    public bool TryGetOrCompile(string fileName, out Script<object> script, out string error)
    {
        var path = Path.Combine(_scriptsDirectory, fileName);
        var lastWrite = File.GetLastWriteTimeUtc(path);

        if (!_cache.TryGetValue(path, out var entry) || entry.LastWriteUtc != lastWrite)
        {
            entry = Compile(path);
            entry.LastWriteUtc = lastWrite;
            _cache[path] = entry;
        }

        script = entry.Script;
        error = entry.CompileError;
        return entry.CompileError == null;
    }

    // ponytail: recompiled scripts stay loaded in the default assembly context (no unload) - fine for
    // occasional file edits. add a collectible AssemblyLoadContext if scripts get reloaded constantly
    // in one session and memory growth becomes an actual problem.
    CacheEntry Compile(string path)
    {
        var code = File.ReadAllText(path);
        var options = ScriptOptions.Default
            .WithReferences(typeof(CraftContext).Assembly, typeof(ExileCore.GameController).Assembly)
            .WithImports("System", "System.Linq", "System.Threading.Tasks", "ExileCrafting",
                "ExileCore.PoEMemory.Components", "ExileCore.Shared.Enums",
                "ExileCore.PoEMemory.MemoryObjects", "ExileCore.PoEMemory.Models");

        try
        {
            var script = CSharpScript.Create<object>(code, options, typeof(CraftContext));
            var errors = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            return errors.Count > 0
                ? new CacheEntry { CompileError = string.Join("\n", errors.Select(d => d.ToString())) }
                : new CacheEntry { Script = script };
        }
        catch (Exception e)
        {
            return new CacheEntry { CompileError = e.Message };
        }
    }
}
