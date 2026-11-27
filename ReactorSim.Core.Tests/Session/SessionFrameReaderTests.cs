using System.Collections.Generic;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class SessionFrameReaderTests
    {
        private static byte[] Bytes(string s) { return Encoding.UTF8.GetBytes(s); }

        private static List<SessionFrame> FeedAll(SessionFrameReader r, byte[] b)
        {
            return r.Feed(b, 0, b.Length);
        }

        [Fact]
        public void FrameOfExactlyTheLimitIsAccepted()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes(new string('a', 4096) + "\n"));
            Assert.Single(frames);
            Assert.Equal(SessionErrorCode.None, frames[0].Fault);
            Assert.Equal(4096, frames[0].ByteLength);
            Assert.Equal(4096, frames[0].Line.Length);
            Assert.Equal(SessionErrorCode.None, r.Fault);
        }

        [Fact]
        public void FrameOneByteOverTheLimitFaultsFrameTooLarge()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes(new string('a', 4097) + "\n"));
            Assert.Single(frames);
            Assert.Equal(SessionErrorCode.FrameTooLarge, frames[0].Fault);
            Assert.Equal(SessionErrorCode.FrameTooLarge, r.Fault);
        }

        [Fact]
        public void MultiByteCharactersCountInBytes()
        {
            var ok = new SessionFrameReader(4096);
            var okFrames = FeedAll(ok, Bytes(new string('\u00e9', 2048) + "\n")); // 2 bytes each = 4096
            Assert.Equal(SessionErrorCode.None, okFrames[0].Fault);
            Assert.Equal(4096, okFrames[0].ByteLength);
            Assert.Equal(2048, okFrames[0].Line.Length);

            var over = new SessionFrameReader(4096);
            var overFrames = FeedAll(over, Bytes(new string('\u00e9', 2048) + "a\n"));
            Assert.Equal(SessionErrorCode.FrameTooLarge, overFrames[0].Fault);
        }

        [Fact]
        public void FaultsAtLimitPlusOneWithoutWaitingForNewline()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes(new string('a', 4097)));
            Assert.Single(frames);
            Assert.Equal(SessionErrorCode.FrameTooLarge, frames[0].Fault);
            Assert.True(r.BufferedBytes <= 4097);
        }

        [Fact]
        public void NeverBuffersMoreThanLimitPlusOneEvenForHugeChunks()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes(new string('a', 1000000)));
            Assert.Equal(SessionErrorCode.FrameTooLarge, frames[frames.Count - 1].Fault);
            Assert.True(r.BufferedBytes <= 4097);
        }

        [Fact]
        public void NoFaultWhileUnderTheLimitAndNoNewline()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes(new string('a', 4096)));
            Assert.Empty(frames);
            Assert.Equal(SessionErrorCode.None, r.Fault);
            Assert.Equal(4096, r.BufferedBytes);
        }

        [Fact]
        public void StaysFaultedAfterAFault()
        {
            var r = new SessionFrameReader(16);
            FeedAll(r, Bytes(new string('a', 17)));
            var more = FeedAll(r, Bytes("ok\n"));
            Assert.Empty(more);
            Assert.Equal(SessionErrorCode.FrameTooLarge, r.Fault);
            Assert.True(r.BufferedBytes <= 17);
        }

        [Fact]
        public void CompleteFramesBeforeAFaultAreStillReturned()
        {
            var r = new SessionFrameReader(8);
            var frames = FeedAll(r, Bytes("one\n" + new string('a', 20)));
            Assert.Equal(2, frames.Count);
            Assert.Equal("one", frames[0].Line);
            Assert.Equal(SessionErrorCode.FrameTooLarge, frames[1].Fault);
        }

        [Fact]
        public void TwoFramesInOneChunk()
        {
            var r = new SessionFrameReader(4096);
            var frames = FeedAll(r, Bytes("first\nsecond\n"));
            Assert.Equal(2, frames.Count);
            Assert.Equal("first", frames[0].Line);
            Assert.Equal(5, frames[0].ByteLength);
            Assert.Equal("second", frames[1].Line);
            Assert.Equal(6, frames[1].ByteLength);
        }

        [Fact]
        public void OneFrameSplitAcrossThreeChunksIncludingAMultiByteCharacter()
        {
            var all = Bytes("h\u00e9llo w\u00f6rld\n");
            var r = new SessionFrameReader(4096);
            // 'h' = 1 byte, e-acute = bytes 1..2; split between the two bytes of e-acute.
            var first = r.Feed(all, 0, 2);
            var second = r.Feed(all, 2, 5);
            var third = r.Feed(all, 7, all.Length - 7);
            Assert.Empty(first);
            Assert.Empty(second);
            Assert.Single(third);
            Assert.Equal("h\u00e9llo w\u00f6rld", third[0].Line);
            Assert.Equal(all.Length - 1, third[0].ByteLength);
        }

        [Fact]
        public void OffsetAndCountAreHonored()
        {
            var b = Bytes("XXabc\nYY");
            var r = new SessionFrameReader(64);
            var frames = r.Feed(b, 2, 4);
            Assert.Single(frames);
            Assert.Equal("abc", frames[0].Line);
        }

        [Fact]
        public void EmptyLineIsAFrameWithZeroLength()
        {
            var r = new SessionFrameReader(64);
            var frames = FeedAll(r, Bytes("\nx\n"));
            Assert.Equal(2, frames.Count);
            Assert.Equal("", frames[0].Line);
            Assert.Equal(0, frames[0].ByteLength);
        }

        [Fact]
        public void CarriageReturnIsNotSpecialCased()
        {
            var r = new SessionFrameReader(64);
            var frames = FeedAll(r, Bytes("abc\r\n"));
            Assert.Equal("abc\r", frames[0].Line);
            Assert.Equal(4, frames[0].ByteLength);
        }

        [Fact]
        public void InvalidUtf8FaultsProtocolViolation()
        {
            var r = new SessionFrameReader(64);
            var frames = FeedAll(r, new byte[] { 0x61, 0xFF, 0xFE, 0x0A });
            Assert.Single(frames);
            Assert.Equal(SessionErrorCode.ProtocolViolation, frames[0].Fault);
            Assert.Equal(SessionErrorCode.ProtocolViolation, r.Fault);
            Assert.Empty(FeedAll(r, Bytes("ok\n")));
        }

        [Fact]
        public void TruncatedMultiByteSequenceFaultsProtocolViolation()
        {
            var r = new SessionFrameReader(64);
            var frames = FeedAll(r, new byte[] { 0xC3, 0x0A });
            Assert.Equal(SessionErrorCode.ProtocolViolation, frames[0].Fault);
        }

        [Fact]
        public void BadArgumentsThrowArgumentException()
        {
            var r = new SessionFrameReader(64);
            Assert.ThrowsAny<System.ArgumentException>(() => r.Feed(null, 0, 0));
            Assert.ThrowsAny<System.ArgumentException>(() => r.Feed(new byte[2], 1, 5));
            Assert.ThrowsAny<System.ArgumentException>(() => new SessionFrameReader(0));
        }
    }
}
