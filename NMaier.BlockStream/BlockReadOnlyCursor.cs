using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace NMaier.BlockStream
{
  internal sealed class BlockReadOnlyCursor : ReaderEnhancedStream
  {
    private readonly BlockReadOnlyStream stream;
    private byte[] currentBlock = ArrayPool<byte>.Shared.Rent(short.MaxValue);
    private long currentIndex = -1;
    private long currentPosition;

    internal BlockReadOnlyCursor(BlockReadOnlyStream stream)
    {
      this.stream = stream;
    }

    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => stream.CanSeek;

    public override bool CanTimeout => stream.CanTimeout;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => stream.Length;

    public override long Position
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => currentPosition;
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(byte[] buffer, int offset, int count)
    {
      return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
      var read = 0;
      for (;;) {
        var block = currentPosition / stream.BlockSize;
        if (currentIndex != block) {
          if (!stream.FillBlock(block, currentBlock, ref currentIndex)) {
            return read;
          }
        }

        var bpos = currentPosition % stream.BlockSize;
        // Must not over-read
        var rem = Math.Min(
          Math.Min(Length - currentPosition, stream.BlockSize - bpos),
          buffer.Length);
        if (rem == 0) {
          return read;
        }

        currentBlock.AsSpan((int)bpos, (int)rem).CopyTo(buffer);
        read += (int)rem;
        currentPosition += rem;
        if (buffer.Length == rem) {
          return read;
        }

        buffer = buffer.Slice((int)rem);
      }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      switch (origin) {
        case SeekOrigin.Begin:
          if (offset < 0) {
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must be positive");
          }

          currentPosition = offset;
          break;
        case SeekOrigin.Current:
          if (offset + currentPosition < 0) {
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must result in a positive position");
          }

          currentPosition += offset;
          break;
        case SeekOrigin.End:
          if (offset + Length < 0) {
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must result in a positive position");
          }

          currentPosition = Length + offset;
          break;
        default:
          ThrowHelpers.ThrowArgumentOutOfRangeException(
            nameof(origin),
            origin,
            "Invalid origin");
          break;
      }

      return currentPosition;
    }


    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

#if !NETFRAMEWORK
    public override void Write(ReadOnlySpan<byte> buffer)
    {
      throw new NotSupportedException();
    }
#endif

    protected override void Dispose(bool disposing)
    {
      if (currentBlock.Length > 0) {
        ArrayPool<byte>.Shared.Return(currentBlock);
        currentBlock = Array.Empty<byte>();
      }

      base.Dispose(disposing);
    }
  }
}
