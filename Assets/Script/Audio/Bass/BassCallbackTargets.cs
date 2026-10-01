#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Maps the user pointer handed to a BASS callback back to the managed object it belongs to.
    /// </summary>
    /// <remarks>
    ///     IL2CPP can only marshal delegates that point to static methods, so BASS callbacks are static
    ///     <c>[MonoPInvokeCallback]</c> methods that look up their target here. A callback that races with
    ///     <see cref="Remove"/> finds nothing instead of dereferencing a freed GCHandle.
    /// </remarks>
    internal sealed class BassCallbackTargets<T> where T : class
    {
        private readonly ConcurrentDictionary<long, T> _targets = new();
        private          long                          _nextId;

        /// <summary>Registers <paramref name="target"/> and returns a non-zero user pointer for it.</summary>
        public IntPtr Add(T target)
        {
            long id = Interlocked.Increment(ref _nextId);
            _targets[id] = target;
            return new IntPtr(id);
        }

        /// <summary>Unregisters a user pointer. Unknown and zero pointers are ignored.</summary>
        public void Remove(IntPtr user) => _targets.TryRemove(user.ToInt64(), out _);

        public T? Get(IntPtr user) => _targets.TryGetValue(user.ToInt64(), out var target) ? target : null;
    }
}
