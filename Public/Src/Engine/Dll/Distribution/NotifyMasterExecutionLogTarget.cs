// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Logging target on master for sending execution logs in distributed builds
    /// </summary>
    public class NotifyMasterExecutionLogTarget : ExecutionLogFileTarget
    {
        private volatile bool m_isDisposed = false;
        private readonly NotifyStream m_notifyStream;
        private readonly BinaryLogger m_logger;

        internal NotifyMasterExecutionLogTarget(WorkerNotificationManager notificationManager, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex)
            : this(new NotifyStream(notificationManager), context, logId, lastStaticAbsolutePathIndex)
        {
        }

        private NotifyMasterExecutionLogTarget(NotifyStream notifyStream, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex) 
            : this(CreateBinaryLogger(notifyStream, context, logId, lastStaticAbsolutePathIndex))
        {
            m_notifyStream = notifyStream;
        }

        private NotifyMasterExecutionLogTarget(BinaryLogger logger)
            : base(logger, closeLogFileOnDispose: true)
        {
            m_logger = logger;
        }

        private static BinaryLogger CreateBinaryLogger(NotifyStream stream, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex)
        {
            return new BinaryLogger(
                stream,
                context,
                logId,
                lastStaticAbsolutePathIndex,
                closeStreamOnDispose: true);
        }

        internal Task FlushAsync()
        {
            return m_logger.FlushAsync();
        }

        internal void Deactivate()
        {
            m_notifyStream.Deactivate();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!m_isDisposed)
            {
                m_isDisposed = true;
                base.Dispose();
            }
        }

        /// <inheritdoc />
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            if (m_isDisposed)
            {
                return;
            }

            base.ReportUnhandledEvent(data);
        }

        private class NotifyStream : Stream
        {
            private MemoryStream m_eventDataBuffer = new MemoryStream();

            private readonly WorkerNotificationManager m_notificationManager;

            /// <summary>
            /// If deactivated, functions stop writing or flushing <see cref="m_eventDataBuffer"/>.
            /// </summary>
            private bool m_isDeactivated = false;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public NotifyStream(WorkerNotificationManager notificationManager)
            {
                m_notificationManager = notificationManager;
            }

            public override void Flush()
            {
                if (m_eventDataBuffer.Length != 0)
                {
                    m_notificationManager.FlushExecutionLog(m_eventDataBuffer);

                    // Reset the buffer 
                    m_eventDataBuffer.SetLength(0);
                }
            }

            public override void Close()
            {
                // Send residual data to master on close
                if (m_eventDataBuffer.Length != 0)
                {
                    Flush();
                }

                m_eventDataBuffer = null;
                base.Close();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (m_isDeactivated)
                {
                    return;
                }

                m_eventDataBuffer.Write(buffer, offset, count);
            }

            public void Deactivate()
            {
                m_isDeactivated = true;
            }
        }
    }
}