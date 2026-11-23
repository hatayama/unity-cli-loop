using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal interface declared apart from its implementers, so a reload given only the
    /// implementers' file sees it through the compiled assembly alone.
    /// </summary>
    internal interface IHotReloadHiddenRunner
    {
        int Run();
    }

    /// <summary>Internal interface with a property, declared apart from its implementer.</summary>
    internal interface IHotReloadHiddenValue
    {
        int Value { get; }
    }

    /// <summary>Internal interface with an indexer, declared apart from its implementer.</summary>
    internal interface IHotReloadHiddenIndexed
    {
        int this[int index] { get; set; }
    }

    /// <summary>Internal interface with an init-only property, declared apart from its implementer.</summary>
    internal interface IHotReloadHiddenInitValue
    {
        int Value { get; init; }
    }

    /// <summary>Internal interface with an event, declared apart from its implementer.</summary>
    internal interface IHotReloadHiddenNotifier
    {
        event Action Changed;
    }

    /// <summary>
    /// Internal interface whose method takes and returns a type the reloaded file declares, so its
    /// signature names the compiled copy of that type.
    /// </summary>
    internal interface IHotReloadHiddenCopier
    {
        HotReloadHiddenCopierFixture Copy(HotReloadHiddenCopierFixture value, ref int count);
    }

    /// <summary>Internal interface with two members of one signature, so a match must use the name.</summary>
    internal interface IHotReloadHiddenSiblings
    {
        int Run();

        int Stop();
    }

    /// <summary>Internal interface with overloads that differ by parameter type only.</summary>
    internal interface IHotReloadHiddenOverloads
    {
        int Take(int value);

        int Take(string value);
    }

    /// <summary>Extension on an internal interface, so a call on an implementer needs the conversion.</summary>
    internal static class HotReloadHiddenRunnerExtensions
    {
        internal static int Doubled(this IHotReloadHiddenRunner runner)
        {
            return runner.Run() * 2;
        }
    }

    /// <summary>Indexer that takes an internal interface, so an implementer as the index needs the conversion.</summary>
    internal class HotReloadHiddenRunnerIndexed
    {
        internal int this[IHotReloadHiddenRunner runner] => runner.Run() + 1;
    }

    /// <summary>Declares an internal interface nested in a type, apart from its implementers.</summary>
    internal class HotReloadHiddenOuter
    {
        /// <summary>Internal interface nested in a type.</summary>
        internal interface IInner
        {
            int Run();
        }
    }
}
