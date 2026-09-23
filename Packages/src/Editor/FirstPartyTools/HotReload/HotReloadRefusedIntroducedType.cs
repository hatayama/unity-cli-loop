using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A type the run's preparation refused to introduce, with the notice that says why.
    /// </summary>
    internal sealed class HotReloadRefusedIntroducedType
    {
        internal HotReloadRefusedIntroducedType(string metadataName, string noticeText)
        {
            MetadataName = metadataName ?? string.Empty;
            NoticeText = noticeText ?? string.Empty;
        }

        // The worker's metadata name: nested types use '/', generic types keep the backtick arity.
        internal string MetadataName { get; }

        internal string NoticeText { get; }

        internal static IReadOnlyList<HotReloadRefusedIntroducedType> CollectFrom(
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices)
        {
            List<HotReloadRefusedIntroducedType> refused = new List<HotReloadRefusedIntroducedType>();
            if (notices == null)
            {
                return refused;
            }

            for (int index = 0; index < notices.Count; index++)
            {
                HotReloadIntroducedTypeNotice notice = notices[index];
                if (string.IsNullOrEmpty(notice.RefusedTypeMetadataName))
                {
                    continue;
                }

                refused.Add(new HotReloadRefusedIntroducedType(notice.RefusedTypeMetadataName, notice.Text));
            }

            return refused;
        }
    }
}
