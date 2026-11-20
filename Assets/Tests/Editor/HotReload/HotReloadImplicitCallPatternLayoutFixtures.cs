using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose lambdas and async methods reach an internal member of a type in a file
    /// the run does not pass, only through an interpolated string handler, the implicit Index or
    /// Range support, the awaiter of await using and await foreach, or an extension method whose
    /// this parameter is a type parameter. Each body holds a 4-digit token unique in this file,
    /// which the tests edit. Every awaiter completes synchronously, so no continuation is queued.
    /// </summary>
    public sealed class HotReloadImplicitCallPatternLayoutHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalCtor()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureCtor($"ab{7501}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalCtor()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureCtor($"ab{7511}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalAppendLiteral()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureLiteral($"ab{7521}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalAppendLiteral()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureLiteral($"ab{7531}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted($"ab{7541}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted($"ab{7551}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsRefHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureRefFormatted($"ab{7561}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsRefHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureRefFormatted($"ab{7571}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternIndexInternalLength()[^1] + 7601;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternIndexInternalLength()[^1] + 7611;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalCount()
        {
            Func<int> read = () => new HotReloadPatternIndexInternalCount()[^1] + 7621;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalCount()
        {
            await Task.CompletedTask;
            return new HotReloadPatternIndexInternalCount()[^1] + 7631;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalIndexer()
        {
            Func<int> read = () => new HotReloadUnpassedIndexInternalIndexer()[^1] + 7641;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalIndexer()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedIndexInternalIndexer()[^1] + 7651;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesRangeWithInternalSlice()
        {
            Func<int> read = () => new HotReloadUnpassedRangeInternalSlice()[1..2] + 7661;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesRangeWithInternalSlice()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedRangeInternalSlice()[1..2] + 7671;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesRangeWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[1..^1] + 7681;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesRangeWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[1..^1] + 7691;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalGetAwaiter())
                {
                }

                return 7701;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalGetAwaiter()
        {
            await using (new HotReloadPatternResourceInternalGetAwaiter())
            {
            }

            return 7711;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalIsCompleted()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalIsCompleted())
                {
                }

                return 7721;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalIsCompleted()
        {
            await using (new HotReloadPatternResourceInternalIsCompleted())
            {
            }

            return 7731;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalGetResult()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalGetResult())
                {
                }

                return 7741;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalGetResult()
        {
            await using (new HotReloadPatternResourceInternalGetResult())
            {
            }

            return 7751;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsingDeclarationWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                await using HotReloadPatternResourceInternalGetAwaiter resource =
                    new HotReloadPatternResourceInternalGetAwaiter();
                return 7761;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsingDeclarationWithInternalGetAwaiter()
        {
            await using HotReloadPatternResourceInternalGetAwaiter resource =
                new HotReloadPatternResourceInternalGetAwaiter();
            return 7771;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7801;
                await foreach (int value in new HotReloadPatternSequenceInternalGetAwaiter())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalGetAwaiter()
        {
            int sum = 7811;
            await foreach (int value in new HotReloadPatternSequenceInternalGetAwaiter())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalIsCompleted()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7821;
                await foreach (int value in new HotReloadPatternSequenceInternalIsCompleted())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalIsCompleted()
        {
            int sum = 7831;
            await foreach (int value in new HotReloadPatternSequenceInternalIsCompleted())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalGetResult()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7841;
                await foreach (int value in new HotReloadPatternSequenceInternalGetResult())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalGetResult()
        {
            int sum = 7851;
            await foreach (int value in new HotReloadPatternSequenceInternalGetResult())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7861;
                await foreach (int value in new HotReloadPatternSequenceDisposeInternalGetAwaiter())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter()
        {
            int sum = 7871;
            await foreach (int value in new HotReloadPatternSequenceDisposeInternalGetAwaiter())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughConstrainedGenericExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedGenericPair();
                return first + second + 7901;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsThroughConstrainedGenericExtension()
        {
            await Task.CompletedTask;
            (int first, int second) = new HotReloadUnpassedGenericPair();
            return first + second + 7911;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsThroughConstrainedGenericExtension()
        {
            Func<int> read = () =>
            {
                int sum = 7921;
                foreach (int value in new HotReloadUnpassedGenericRange())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncLoopsThroughConstrainedGenericExtension()
        {
            await Task.CompletedTask;
            int sum = 7931;
            foreach (int value in new HotReloadUnpassedGenericRange())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughUnconstrainedGenericExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second, int third) = new HotReloadUnpassedGenericTriple();
                return first + second + third + 7941;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsThroughUnconstrainedGenericExtension()
        {
            await Task.CompletedTask;
            (int first, int second, int third) = new HotReloadUnpassedGenericTriple();
            return first + second + third + 7951;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerFromInterpolatedAddition()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted($"ab" + $"{7581}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerFromInterpolatedAddition()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted($"ab" + $"{7591}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesConditionallyFromEndWithInternalLength()
        {
            HotReloadPatternIndexInternalLength values = new HotReloadPatternIndexInternalLength();
            Func<int> read = () => (values?[^1] ?? 0) + 7881;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesConditionallyFromEndWithInternalLength()
        {
            await Task.CompletedTask;
            HotReloadPatternIndexInternalLength values = new HotReloadPatternIndexInternalLength();
            return (values?[^1] ?? 0) + 7891;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesFromStartWithInternalLength()
        {
            // Why this stays patchable: a range whose endpoints both count from the start is lowered
            // to Slice(start, end - start) without reading Length.
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[1..2] + 7961;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesFromStartWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[1..2] + 7971;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesOpenStartWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[..2] + 7981;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesOpenStartWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[..2] + 7991;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachDeconstructsThroughConstrainedGenericExtension()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7781;
                await foreach ((int first, int second) in new HotReloadUnpassedPairSequence())
                {
                    sum += first + second;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachDeconstructsThroughConstrainedGenericExtension()
        {
            int sum = 7791;
            await foreach ((int first, int second) in new HotReloadUnpassedPairSequence())
            {
                sum += first + second;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCastsToHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted(
                (HotReloadPatternHandlerInternalAppendFormatted)$"ab{7505}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCastsToHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted(
                (HotReloadPatternHandlerInternalAppendFormatted)$"ab{7515}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsBesideAThreeOutOverload()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedArityPair();
                return first + second + 7525;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsBesideAThreeOutOverload()
        {
            await Task.CompletedTask;
            (int first, int second) = new HotReloadUnpassedArityPair();
            return first + second + 7535;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesPositionalPatternBesideAThreeOutOverload()
        {
            Func<int> read = () => new HotReloadUnpassedArityPair() is (5, 6) ? 7545 : 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesNullableStructFromEndWithInternalLength()
        {
            HotReloadUnpassedStructInternalLength? values = new HotReloadUnpassedStructInternalLength();
            Func<int> read = () => (values?[^1] ?? 0) + 7555;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesNullableStructWithInternalLength()
        {
            await Task.CompletedTask;
            HotReloadUnpassedStructInternalLength? values = new HotReloadUnpassedStructInternalLength();
            return (values?[1..^1] ?? 0) + 7565;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughClassConstrainedExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second, int third, int fourth) = new HotReloadUnpassedQuad();
                return first + second + third + fourth + 7575;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughStructConstrainedExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second, int third, int fourth, int fifth) = new HotReloadUnpassedQuint();
                return first + second + third + fourth + fifth + 7585;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesInternalDisposeAsyncBesideAPublicExtension()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadUnpassedResourceInternalDisposeAsync())
                {
                }

                return 7595;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesInternalDisposeAsyncBesideAPublicExtension()
        {
            await using (new HotReloadUnpassedResourceInternalDisposeAsync())
            {
            }

            return 7605;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsBesideANewConstrainedExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second, int third, int fourth, int fifth, int sixth) =
                    new HotReloadUnpassedNoDefaultConstructor(1);
                return first + second + third + fourth + fifth + sixth + 7625;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsBesideAnUnmanagedConstrainedExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second, int third, int fourth, int fifth, int sixth, int seventh) =
                    new HotReloadUnpassedManagedStruct();
                return first + second + third + fourth + fifth + sixth + seventh + 7635;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverMarkedByTheSecondTypeArgument()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedMarkedBySecond();
                return first + second + 7805;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsASelfReferencingReceiver()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedSelfReferencing();
                return first + second + 7815;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAProducerOfADerivedType()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedDerivedProducer();
                return first + second + 7825;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAnArrayOfADerivedType()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedProducedDerived[1];
                return first + second + 7835;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverMarkedByANamedTuple()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedMarkedByNamedTuple();
                return first + second + 7845;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverTypedAsAMarkerOfDynamic()
        {
            IHotReloadUnpassedMarker<dynamic> marked = new HotReloadUnpassedMarkedByObject();
            Func<int> read = () =>
            {
                (int first, int second) = marked;
                return first + second + 7855;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverTaggedByTheSecondTypeArgument()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedTaggedBySecond();
                return first + second + 7865;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverMarkedByMemory()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedMarkedByMemory();
                return first + second + 7875;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsALocalTypedAsAMarkerOfMemory()
        {
            IHotReloadUnpassedMarker<Memory<int>> marked = new HotReloadUnpassedMarkedByMemory();
            Func<int> read = () =>
            {
                (int first, int second) = marked;
                return first + second + 7885;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverTaggedByMemory()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedTaggedByMemory();
                return first + second + 7895;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsALocalTypedAsATagOfMemory()
        {
            IHotReloadUnpassedTag<Memory<int>> tagged = new HotReloadUnpassedTaggedByMemory();
            Func<int> read = () =>
            {
                (int first, int second) = tagged;
                return first + second + 7905;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverMarkedByAnArrayOfTheSecond()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedMarkedBySecondArray();
                return first + second + 7915;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAReceiverOfTheInnerOfTheSecond()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedInnerOfSecond();
                return first + second + 7925;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesTwoArgumentsWithInternalIndexer()
        {
            Func<int> read = () => new HotReloadUnpassedTwoArgumentIndexer()[0, ^1] + 7615;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesTwoArgumentsWithInternalIndexer()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedTwoArgumentIndexer()[0, ^1] + 7645;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesWithInternalIntIndexer()
        {
            Func<int> read = () => new HotReloadUnpassedInternalIntIndexer()[0] + 7655;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesWithInternalIntIndexer()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalIntIndexer()[0] + 7665;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsANullableStructBesideAStructConstrainedExtension()
        {
            Func<int> read = () =>
            {
                HotReloadUnpassedQuint? value = new HotReloadUnpassedQuint();
                (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8) = value;
                return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + 7675;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesThroughInternalRangeIndexer()
        {
            Func<int> read = () => new HotReloadUnpassedInternalRangeIndexer()[1..2] + 7685;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesThroughInternalRangeIndexer()
        {
            await Task.CompletedTask;
            return new HotReloadUnpassedInternalRangeIndexer()[1..2] + 7695;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAnArrayThroughGenericExtension()
        {
            Func<int> read = () =>
            {
                (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9) = new int[1];
                return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + 7705;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsBesideAnOverloadWithARequiredArgument()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedRequiredArgumentDerived();
                return first + second + 7715;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsAnArrayThroughArrayExtension()
        {
            Func<int> read = () =>
            {
                (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10) = new string[1];
                return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + 7725;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsAnArrayThroughArrayExtension()
        {
            await Task.CompletedTask;
            (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10) = new string[1];
            return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + 7735;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsATypeParameterWithNew()
        {
            Func<int> read = () =>
            {
                int Sum<T>()
                    where T : new()
                {
                    (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11) = new T();
                    return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11;
                }

                return Sum<object>() + 7745;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsATypeParameterWithNew()
        {
            await Task.CompletedTask;
            int Sum<T>()
                where T : new()
            {
                (int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11) = new T();
                return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11;
            }

            return Sum<object>() + 7755;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsPassedFileHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPassedFileHandlerSinks.Measure($"ab{7765}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsPassedFileHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPassedFileHandlerSinks.Measure($"ab{7775}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAddsToPassedFileHandlerCallOfCapturedLocal()
        {
            int value = 2;
            Func<int> read = () => HotReloadPassedFileHandlerSinks.Measure($"ab{value}") + 7785;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAddsToPassedFileHandlerCallOfLocal()
        {
            int value = 2;
            await Task.CompletedTask;
            return HotReloadPassedFileHandlerSinks.Measure($"ab{value}") + 7795;
        }

    }

    /// <summary>
    /// Handler declared in the file the run passes, whose only AppendFormatted is internal. Its
    /// InterpolatedStringHandler attribute is the internal declaration in a file the run does not
    /// pass, as in a project whose base class library lacks that attribute.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HotReloadPassedFileHandlerInternalAppendFormatted
    {
        public int Total;

        public HotReloadPassedFileHandlerInternalAppendFormatted(int literalLength, int formattedCount)
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
    /// Takes the passed-file handler, so an interpolated string argument is built through it.
    /// </summary>
    public static class HotReloadPassedFileHandlerSinks
    {
        public static int Measure(HotReloadPassedFileHandlerInternalAppendFormatted handler)
        {
            return handler.Total;
        }
    }
}
