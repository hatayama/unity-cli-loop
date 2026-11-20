using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Bodies that use an internal accessor of a public property or indexer declared in
    /// HotReloadUnpassedAccessorHolders.cs, a file no run passes, so the transform worker reads it
    /// through the public surface only. Each body holds a 4-digit token unique in this file, which
    /// a test edits to make that one method changed.
    /// </summary>
    public sealed class HotReloadUnpassedAccessorHost
    {
        public int SetterWriteInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
                holder.Length = 7101;
                return holder.ReadLength();
            };
            return read();
        }

        public async Task<int> SetterWriteInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
            holder.Length = 7111;
            return holder.ReadLength();
        }

        public int GetterReadInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
                int value = holder.Value;
                return value + 7121;
            };
            return read();
        }

        public async Task<int> GetterReadInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
            int value = holder.Value;
            return value + 7131;
        }

        public int CompoundAssignmentInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
                holder.Length += 7141;
                return holder.ReadLength();
            };
            return read();
        }

        public async Task<int> CompoundAssignmentInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
            holder.Length += 7151;
            return holder.ReadLength();
        }

        public int IncrementInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
                holder.Length++;
                return holder.ReadLength() + 7161;
            };
            return read();
        }

        public async Task<int> IncrementInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
            holder.Length++;
            return holder.ReadLength() + 7171;
        }

        public int IndexerSetterWriteInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedIndexerInternalSetterHolder holder = new HotReloadUnpassedIndexerInternalSetterHolder();
                holder[0] = 7181;
                return holder.Peek(0);
            };
            return read();
        }

        public async Task<int> IndexerSetterWriteInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedIndexerInternalSetterHolder holder = new HotReloadUnpassedIndexerInternalSetterHolder();
            holder[0] = 7191;
            return holder.Peek(0);
        }

        public int IndexerGetterReadInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedIndexerInternalGetterHolder holder = new HotReloadUnpassedIndexerInternalGetterHolder();
                int value = holder[0];
                return value + 7201;
            };
            return read();
        }

        public async Task<int> IndexerGetterReadInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedIndexerInternalGetterHolder holder = new HotReloadUnpassedIndexerInternalGetterHolder();
            int value = holder[0];
            return value + 7211;
        }

        public int InitializerSetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalSetterHolder { Length = 7221 }.ReadLength();
            return read();
        }

        public async Task<int> InitializerSetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalSetterHolder { Length = 7231 }.ReadLength();
        }

        public int InitializerInitInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalInitHolder { Length = 7241 }.Length;
            return read();
        }

        public async Task<int> InitializerInitInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalInitHolder { Length = 7251 }.Length;
        }

        public int IndexFromEndThroughLengthGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedLengthInternalGetterHolder()[^1] + 7261;
            return read();
        }

        public async Task<int> IndexFromEndThroughLengthGetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedLengthInternalGetterHolder()[^1] + 7271;
        }

        public int IndexFromEndThroughIndexerGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedIndexerInternalGetterHolder()[^1] + 7281;
            return read();
        }

        public async Task<int> IndexFromEndThroughIndexerGetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedIndexerInternalGetterHolder()[^1] + 7291;
        }

        public int RangeThroughLengthGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedLengthInternalGetterHolder()[1..^1][0] + 7301;
            return read();
        }

        public async Task<int> RangeThroughLengthGetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedLengthInternalGetterHolder()[1..^1][0] + 7311;
        }

        public int StaticSetterWriteInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedStaticInternalSetterHolder.Shared = 7321;
                return HotReloadUnpassedStaticInternalSetterHolder.Shared;
            };
            return read();
        }

        public async Task<int> StaticSetterWriteInAsync()
        {
            await Task.CompletedTask;
            HotReloadUnpassedStaticInternalSetterHolder.Shared = 7331;
            return HotReloadUnpassedStaticInternalSetterHolder.Shared;
        }

        public int GenericIndexerSetterWriteInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedGenericIndexerInternalSetterHolder<int> holder = new HotReloadUnpassedGenericIndexerInternalSetterHolder<int>();
                holder[0] = 7341;
                return holder[0];
            };
            return read();
        }

        public int GetterReadOfInternalSetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalSetterHolder().Length + 7401;
            return read();
        }

        public int SetterWriteOfInternalGetterInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
                holder.Value = 7411;
                return holder.ReadValue();
            };
            return read();
        }

        public int RangeWithoutLengthInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedLengthInternalGetterHolder()[1..2][0] + 7421;
            return read();
        }

        public int SetterWriteInOwnStatements()
        {
            HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
            holder.Length = 7501;
            return holder.ReadLength();
        }

        public int GetterReadInOwnStatements()
        {
            HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
            int value = holder.Value;
            return value + 7511;
        }
    }

}
