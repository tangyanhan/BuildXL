// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Interface for batch processing items based on time or a number of items.
    /// </summary>
    public interface INagleQueue<T> : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Suspends processing data until the resulting object is disposed.
        /// </summary>
        IDisposable Suspend();

        /// <summary>
        /// Starts the processing.
        /// </summary>
        void Start();

        /// <summary>
        /// Adds an <paramref name="item"/> for asynchronous processing.
        /// </summary>
        void Enqueue(T item);

        /// <summary>
        /// Adds <paramref name="items"/> for asynchronous processing.
        /// </summary>
        void EnqueueAll(IEnumerable<T> items);
    }

    /// <summary>
    /// Nagling queue for processing data in batches based on the size or a time interval.
    /// </summary>
    public class NagleQueue<T> : INagleQueue<T>
    {
        private bool _disposed;
        private bool _batchIsCompleted;
        private Func<T[], Task>? _processBatch;
        private readonly TimeSpan _timerInterval;
        private readonly BatchBlock<T> _batchBlock;
        private readonly ActionBlock<T[]> _actionBlock;
        private readonly Timer _intervalTimer;

        // Move into SuspendableNagleQueue?
        private bool _eventsSuspended;
        private readonly ConcurrentQueue<T> _suspendedEvents = new ConcurrentQueue<T>();

        /// <nodoc />
        public int BatchSize => _batchBlock.BatchSize;

        /// <summary>
        /// Creates an instance of a nagle queue.
        /// </summary>
        protected NagleQueue(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            Contract.Requires(maxDegreeOfParallelism > 0);
            Contract.Requires(batchSize > 0);

            _timerInterval = interval;

            _batchBlock = new BatchBlock<T>(batchSize);
            _actionBlock = new ActionBlock<T[]>(ProcessBatchAsync, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            });

            _processBatch = processBatch;

            _intervalTimer = new Timer(SendIncompleteBatch, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Creates a unstarted nagle queue which is not started until <see cref="Start()"/> is called.
        /// </summary>
        public static NagleQueue<T> CreateUnstarted(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            if (maxDegreeOfParallelism == 1 && batchSize == 1)
            {
                return new SynchronousNagleQueue<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
            }

            return new NagleQueue<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
        }

        /// <summary>
        /// Creates a fully functioning nagle queue.
        /// </summary>
        public static NagleQueue<T> Create(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            var queue = CreateUnstarted(processBatch, maxDegreeOfParallelism, interval, batchSize);
            queue.Start();
            return queue;
        }

        /// <summary>
        /// Suspends the processing of new elements and returns a <see cref="IDisposable"/> instance that will resume the processing on <see cref="IDisposable.Dispose"/> call.
        /// </summary>
        public IDisposable Suspend()
        {
            // Intentionally not checking _disposed flag here because
            // it is possible to call this method when the disposal is already started.
            // In this case we still return ResumeBlockDisposable that will do nothing at the end.

            _eventsSuspended = true;
            return new DisposeAction<NagleQueue<T>>(this, @this => @this.Resume());
        }

        private void Resume()
        {
            _eventsSuspended = false;

            // We should not be processing suspended events if Dispose method was already called.
            while (!_eventsSuspended && !_disposed)
            {
                if (_suspendedEvents.TryDequeue(out var item))
                {
                    EnqueueCore(item, fromResume: true);
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Starts data processing.
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            _batchBlock.LinkTo(_actionBlock);
            ResetTimer();
        }

        /// <inheritdoc />
        public void Enqueue(T item)
        {
            ThrowIfDisposed();
            if (_eventsSuspended)
            {
                _suspendedEvents.Enqueue(item);
            }
            else
            {
                EnqueueCore(item);
            }
        }

        /// <nodoc />
        protected virtual void EnqueueCore(T item, bool fromResume = false)
        {
            var result = _batchBlock.Post(item);
            // Resume method adds all the items from the suspended queue into the actual queue.
            // The method loops over all the suspended items and stops when the suspended queue is empty
            // or the current instance is disposed.
            // But because the logic is lock free, its possible that the instance is disposed after that check
            // and before this method is called.
            // To avoid a runtime errors the assertion below should respect this case
            // and ignore if the item was not added to the batch block because the queue is disposed.
            Contract.Assert(result || (fromResume && Volatile.Read(ref _batchIsCompleted)));
        }

        /// <inheritdoc />
        public void EnqueueAll(IEnumerable<T> items)
        {
            Contract.Requires(items != null);

            ThrowIfDisposed();
            _batchBlock.PostAll(items);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _batchBlock.TriggerBatch();
            _batchBlock.Complete();

            _batchIsCompleted = true;

            await _batchBlock.Completion;

            _actionBlock.Complete();
            await _actionBlock.Completion;

            _intervalTimer.Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        private async Task ProcessBatchAsync(T[] batch)
        {
            try
            {
                SuspendTimer();

                Contract.Assert(_processBatch != null);
                await _processBatch(batch);
            }
            finally
            {
                ResetTimer();
            }
        }

        private void SendIncompleteBatch(object? obj)
        {
            // Even though the callback (ProcessBatchAsync) resets the timer,
            // we still need to reset the timer in the case when the batch is empty now.
            // in this case _batchBlock.TriggerBatch won't trigger the callback at all.
            ResetTimer();

            _batchBlock.TriggerBatch();
        }

        private void SuspendTimer()
        {
            _intervalTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void ResetTimer()
        {
            // The callback method can be called even when the _intervalTimer.Dispose method is called.
            // We do our best and not change the timer if the whole instance is disposed, but
            // the race is still possible so to avoid potential crashes we still have to swallow ObjectDisposedException here.
            if (!_disposed)
            {
                try { _intervalTimer.Change(_timerInterval, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }
        }

        /// <nodoc />
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NagleQueue<T>));
            }
        }
    }
}
