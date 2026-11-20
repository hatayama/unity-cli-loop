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

        public int NestedInitializerGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedNestedInitializerOuter { Inner = { Count = 7351 } }.ReadInner();
            return read();
        }

        public async Task<int> NestedInitializerGetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedNestedInitializerOuter { Inner = { Count = 7361 } }.ReadInner();
        }

        public int WithInitializerInitInLambda()
        {
            Func<int> read = () => (new HotReloadUnpassedInternalInitRecord() with { Length = 7371 }).Length;
            return read();
        }

        public async Task<int> WithInitializerInitInAsync()
        {
            await Task.CompletedTask;
            return (new HotReloadUnpassedInternalInitRecord() with { Length = 7381 }).Length;
        }

        public int IndexInitializerSetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedIndexerInternalSetterHolder { [0] = 7391 }.Peek(0);
            return read();
        }

        public async Task<int> IndexInitializerSetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedIndexerInternalSetterHolder { [0] = 7431 }.Peek(0);
        }

        public int CompoundAssignmentOfInternalGetterInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
                holder.Value += 7441;
                return holder.ReadValue();
            };
            return read();
        }

        public int IncrementOfInternalGetterInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalGetterHolder holder = new HotReloadUnpassedInternalGetterHolder();
                holder.Value++;
                return holder.ReadValue() + 7451;
            };
            return read();
        }

        public int DeconstructionIntoInternalSetterInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalSetterHolder holder = new HotReloadUnpassedInternalSetterHolder();
                int other;
                (holder.Length, other) = (7461, 1);
                return holder.ReadLength() + other;
            };
            return read();
        }

        public int WrongIndexTypeInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedIntIndexerInternalGetterHolder()[7471 - 7471];
            return read();
        }

        public int UndefinedIndexInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedIntIndexerInternalGetterHolder()[7481 - 7481];
            return read();
        }

        public int StringIndexerReadInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedStringIndexerInternalGetterHolder()["7621"];
            return read();
        }

        public int GetOnlyWriteInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedGetOnlyHolder holder = new HotReloadUnpassedGetOnlyHolder();
                int value = 7491;
                return value + holder.Fixed;
            };
            return read();
        }

        public int ReadOnlyIndexerWriteBesideInternalGetterOverloadInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedOverloadedIndexerHolder holder = new HotReloadUnpassedOverloadedIndexerHolder();
                int value = 7521;
                return value + holder[0];
            };
            return read();
        }

        public int PropertyPatternGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalGetterHolder() is { Value: 7 } ? 7571 : 0;
            return read();
        }

        public async Task<int> PropertyPatternGetterInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalGetterHolder() is { Value: 7 } ? 7581 : 0;
        }

        public int NestedPropertyPatternGetterInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedNestedInitializerOuter() is { Inner: { Count: 7591 } } ? 1 : 0;
            return read();
        }

        public int PropertyPatternGetterInOwnStatements()
        {
            return new HotReloadUnpassedInternalGetterHolder() is { Value: 7 } ? 7601 : 0;
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

        public int RangeThroughInternalRangeIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalRangeIndexerHolder()[1..3][0] + 7631;
            return read();
        }

        public async Task<int> RangeThroughInternalRangeIndexerInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalRangeIndexerHolder()[1..3][0] + 7641;
        }

        public int IndexThroughInternalIndexIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalIndexIndexerHolder()[^1] + 7651;
            return read();
        }

        public async Task<int> IndexThroughInternalIndexIndexerInAsync()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalIndexIndexerHolder()[^1] + 7661;
        }

        public int IndexWriteThroughInternalIndexIndexerInLambda()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedInternalIndexIndexerHolder holder = new HotReloadUnpassedInternalIndexIndexerHolder();
                holder[^1] = 7671;
                return holder[0];
            };
            return read();
        }

        public int RangeThroughInternalRangeIndexerInOwnStatements()
        {
            return new HotReloadUnpassedInternalRangeIndexerHolder()[1..3][0] + 7681;
        }

        public int RangeInOwnStatementsThenInLambda()
        {
            int own = new HotReloadUnpassedInternalRangeIndexerHolder()[1..3][0];
            Func<int> read = () => new HotReloadUnpassedInternalRangeIndexerHolder()[1..3][0] + 7711;
            return own + read();
        }

        public int IndexWithIntArgumentBesideInternalIndexIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalIndexIndexerHolder()[0] + 7691;
            return read();
        }

        public int ConditionalIndexThroughInternalIndexIndexerInLambda()
        {
            HotReloadUnpassedInternalIndexIndexerHolder holder = new HotReloadUnpassedInternalIndexIndexerHolder();
            Func<int> read = () => (holder?[^1] ?? 0) + 7721;
            return read();
        }

        public int RangeThroughInternalRangeIndexerOfBaseInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedInternalRangeIndexerDerived()[1..3][0] + 7731;
            return read();
        }

        public int IndexThroughInternalGenericKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedGenericKeyIndexerHolder<Index>()[^1] + 7741;
            return read();
        }

        public int IndexThroughInternalObjectKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedObjectKeyIndexerHolder()[^1] + 7751;
            return read();
        }

        public int IndexBesideInternalGenericArrayKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedGenericArrayKeyIndexerHolder<int>()[^1] + 7771;
            return read();
        }

        public int IndexThroughInternalConvertedKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedConvertedKeyIndexerHolder()[^1] + 7781;
            return read();
        }

        public int IndexBesideInternalDerivedKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedDerivedKeyIndexerHolder()[^1] + 7791;
            return read();
        }

        public int IndexThroughInternalGenericConvertedKeyIndexerInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedGenericConvertedKeyIndexerHolder<Index>()[^1] + 7801;
            return read();
        }

        public int IndexBesideInternalGenericConvertedKeyIndexerOfStringInLambda()
        {
            Func<int> read = () => new HotReloadUnpassedGenericConvertedKeyIndexerHolder<string>()[^1] + 7811;
            return read();
        }

        public int RangeThroughConstrainedTypeParameterInLocalFunction()
        {
            static int Read<T>(T holder) where T : HotReloadUnpassedInternalRangeIndexerBase
            {
                return holder[1..3][0] + 7761;
            }

            return Read(new HotReloadUnpassedInternalRangeIndexerDerived());
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


    /// <summary>
    /// A type in the edited file that writes, by their bare names, the public properties with an
    /// internal setter that it inherits from a base type in a file no run passes.
    /// </summary>
    public sealed class HotReloadUnpassedAccessorDerivedHost : HotReloadUnpassedAccessorBase
    {
        public int BareSetterWriteInOwnStatements()
        {
            Length = 7531;
            return ReadLength();
        }

        public int BareSetterWriteInLambda()
        {
            Func<int> read = () =>
            {
                Length = 7541;
                return ReadLength();
            };
            return read();
        }

        public async Task<int> BareSetterWriteInAsync()
        {
            await Task.CompletedTask;
            Length = 7551;
            return ReadLength();
        }

        public int BareStaticSetterWriteInOwnStatements()
        {
            Shared = 7561;
            return Shared;
        }
    }

    /// <summary>
    /// A type in the edited file that hides, with a get-only property, an inherited property whose
    /// setter is internal to a base type in a file no run passes, so a write is a genuine error.
    /// </summary>
    public sealed class HotReloadUnpassedAccessorHidingHost : HotReloadUnpassedAccessorBase
    {
        public new int Length => 3;

        public int HidingPropertyWriteInLambda()
        {
            Func<int> read = () =>
            {
                int value = 7611;
                return value + this.Length;
            };
            return read();
        }
    }
}
