// ReactorSim.Core - per-connection newline-delimited frame reader with a hard size bound (Phase 3, U1, KTD4).

using System;
using System.Collections.Generic;
using System.Text;

namespace ReactorSim.Session
{
    public sealed class SessionFrame
    {
        // Decoded UTF-8 text without the newline. A trailing '\r' is not stripped: the protocol ends frames with '\n' only.
        public string Line;
        // UTF-8 byte length of the frame, newline excluded.
        public int ByteLength;
        // None for a complete frame; otherwise the connection is faulted and this is the last frame returned.
        public SessionErrorCode Fault;

        public bool IsFault { get { return Fault != SessionErrorCode.None; } }
    }

    public sealed class SessionFrameReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly int _maxFrameBytes;
        private byte[] _buffer = new byte[256];
        private int _length;

        public SessionFrameReader(int maxFrameBytes)
        {
            if (maxFrameBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
            _maxFrameBytes = maxFrameBytes;
        }

        public SessionErrorCode Fault { get; private set; }

        // Bytes currently held for an unfinished frame; never exceeds maxFrameBytes + 1.
        public int BufferedBytes { get { return _length; } }

        public List<SessionFrame> Feed(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));

            var frames = new List<SessionFrame>();
            if (Fault != SessionErrorCode.None)
                return frames;

            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte b = buffer[i];
                if (b == (byte)'\n')
                {
                    string text;
                    try
                    {
                        text = StrictUtf8.GetString(_buffer, 0, _length);
                    }
                    catch (ArgumentException)
                    {
                        FaultWith(SessionErrorCode.ProtocolViolation, frames);
                        return frames;
                    }
                    frames.Add(new SessionFrame { Line = text, ByteLength = _length, Fault = SessionErrorCode.None });
                    _length = 0;
                    continue;
                }

                if (_length >= _maxFrameBytes)
                {
                    // This byte would be the limit + 1 th byte of the frame, so no newline came within the limit.
                    Append(b);
                    FaultWith(SessionErrorCode.FrameTooLarge, frames);
                    return frames;
                }
                Append(b);
            }
            return frames;
        }

        private void Append(byte b)
        {
            if (_length == _buffer.Length)
            {
                int next = Math.Min(_buffer.Length * 2, _maxFrameBytes + 1);
                Array.Resize(ref _buffer, next);
            }
            _buffer[_length++] = b;
        }

        private void FaultWith(SessionErrorCode code, List<SessionFrame> frames)
        {
            Fault = code;
            frames.Add(new SessionFrame { Line = null, ByteLength = _length, Fault = code });
        }
    }
}
