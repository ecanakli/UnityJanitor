#if UNITY_EDITOR
using System.Collections.Generic;

namespace Ecanakli.Janitor
{
    // The "recently disposed" ring buffer: the last RecentCapacity lifetimes, as plain structs in a preallocated array.
    // It is static, so it outlives a play session (and, with domain reload off, a play mode exit); with reload on, the
    // editor persists it through ExportState and ImportState. Main thread only.
    public static partial class LifetimeDiagnostics
    {
        private static readonly RecentLifetime[] RecentRing = new RecentLifetime[RecentCapacity];
        private static int s_recentNext;
        private static int s_recentCount;
        private static int s_recentVersion;

        internal static int RecentCount => s_recentCount;

        internal static int RecentVersion => s_recentVersion;

        // Copies the lifetime's data; allocates nothing.
        internal static void RecordDisposed(Lifetime lifetime)
        {
            var parent = lifetime.Parent;
            PushRecent(new RecentLifetime
            {
                Id = lifetime.DiagId,
                ParentId = parent == null ? 0 : parent.DiagId,
                Kind = lifetime.Kind,
                Generation = lifetime.Generation,
                CancelCount = lifetime.DiagCancelCount,
                TotalRegistered = lifetime.DiagTotalRegistered,
                RefusedCount = lifetime.DiagRefusedCount,
                CreatedFrame = lifetime.DiagCreatedFrame,
                DisposedFrame = lifetime.DiagDisposedFrame,
                Session = s_session,
                Name = lifetime.Name,
                Member = lifetime.CreatorMember,
                Line = lifetime.CreatorLine,
            });
        }

        internal static void PushRecent(in RecentLifetime entry)
        {
            RecentRing[s_recentNext] = entry;
            s_recentNext = (s_recentNext + 1) % RecentCapacity;
            if (s_recentCount < RecentCapacity)
            {
                s_recentCount++;
            }

            s_recentVersion++;
        }

        // Newest first.
        internal static void CopyRecent(List<RecentLifetime> into)
        {
            into.Clear();
            for (var i = 0; i < s_recentCount; i++)
            {
                var index = (s_recentNext - 1 - i + RecentCapacity) % RecentCapacity;
                into.Add(RecentRing[index]);
            }
        }

        internal static void ClearRecent()
        {
            System.Array.Clear(RecentRing, 0, RecentRing.Length);
            s_recentNext = 0;
            s_recentCount = 0;
            s_recentVersion++;
        }
    }
}
#endif
