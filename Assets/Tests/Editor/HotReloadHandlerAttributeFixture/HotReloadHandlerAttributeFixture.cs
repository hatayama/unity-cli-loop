using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadHandlerAttributeFixture
{
    /// <summary>
    /// Handler whose only AppendFormatted is internal. Its attribute is the internal declaration in
    /// another file of this assembly, which a reload of this file alone does not pass.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HandlerAttributeFixtureHandler
    {
        public int Total;

        public HandlerAttributeFixtureHandler(int literalLength, int formattedCount)
        {
            Total = literalLength + formattedCount;
        }

        public void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        internal void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Takes the handler, so an interpolated string argument is built through it.
    /// </summary>
    public static class HandlerAttributeFixtureSinks
    {
        public static int Measure(HandlerAttributeFixtureHandler handler)
        {
            return handler.Total;
        }
    }

    /// <summary>
    /// Compiled host whose lambda and async body build the handler. Each body holds a token unique
    /// in this file, which the tests edit.
    /// </summary>
    public sealed class HandlerAttributeFixtureHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandler()
        {
            Func<int> read = () => HandlerAttributeFixtureSinks.Measure($"ab{1105}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandler()
        {
            await Task.CompletedTask;
            return HandlerAttributeFixtureSinks.Measure($"ab{1115}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAddsToHandlerCallOfCapturedLocal()
        {
            int value = 2;
            Func<int> read = () => HandlerAttributeFixtureSinks.Measure($"ab{value}") + 1125;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAddsToHandlerCallOfLocal()
        {
            int value = 2;
            await Task.CompletedTask;
            return HandlerAttributeFixtureSinks.Measure($"ab{value}") + 1135;
        }
    }
}
