using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Song.Network
{
    /// <summary>
    /// Runs tasks on its own background threads instead of the thread pool. Parallel loops that pass
    /// <see cref="TaskScheduler.Current"/> in their options (YARG.Core's CacheHandler does) stay on these threads.
    /// </summary>
    /// <remarks>
    /// SMBLibrary completes every request from a socket callback that needs a free thread-pool thread. A scan's
    /// nested Parallel.ForEach on the pool fills it with threads blocked on those requests, so each reply waits
    /// for the pool to inject a thread, about two per second.
    /// </remarks>
    internal sealed class DedicatedThreadScheduler : TaskScheduler, IDisposable
    {
        [ThreadStatic]
        private static DedicatedThreadScheduler _owner;

        private readonly Queue<Task> _queue = new();
        private readonly Thread[] _threads;
        private bool _completed;

        internal DedicatedThreadScheduler(int threadCount, string name)
        {
            _threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threads[i] = new Thread(Work)
                {
                    IsBackground = true,
                    Name = name,
                };
                _threads[i].Start();
            }
        }

        public override int MaximumConcurrencyLevel => _threads.Length;

        /// <summary>
        /// Runs <paramref name="work"/> on one of the scheduler's threads and blocks until it finishes.
        /// </summary>
        internal T Run<T>(Func<T> work)
        {
            return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, this)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Lets the threads finish the queued tasks, then exit.
        /// </summary>
        public void Dispose()
        {
            lock (_queue)
            {
                _completed = true;
                Monitor.PulseAll(_queue);
            }
        }

        protected override void QueueTask(Task task)
        {
            lock (_queue)
            {
                if (!_completed)
                {
                    _queue.Enqueue(task);
                    Monitor.Pulse(_queue);
                    return;
                }
            }
            ThreadPool.UnsafeQueueUserWorkItem(_ => TryExecuteTask(task), null);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (_owner != this)
            {
                return false;
            }

            if (taskWasPreviouslyQueued && !TryDequeue(task))
            {
                return false;
            }
            return TryExecuteTask(task);
        }

        protected override bool TryDequeue(Task task)
        {
            lock (_queue)
            {
                if (!_queue.Contains(task))
                {
                    return false;
                }

                int count = _queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var queued = _queue.Dequeue();
                    if (queued != task)
                    {
                        _queue.Enqueue(queued);
                    }
                }
                return true;
            }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            lock (_queue)
            {
                return _queue.ToArray();
            }
        }

        private void Work()
        {
            _owner = this;
            while (true)
            {
                Task task;
                lock (_queue)
                {
                    while (_queue.Count == 0)
                    {
                        if (_completed)
                        {
                            return;
                        }
                        Monitor.Wait(_queue);
                    }
                    task = _queue.Dequeue();
                }
                TryExecuteTask(task);
            }
        }
    }
}
