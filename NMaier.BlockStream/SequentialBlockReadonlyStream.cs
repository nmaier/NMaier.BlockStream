using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  /// <summary>
  ///   A sequential reader block stream. This kind of stream is not seek-able, however it has more lax requirements for
  ///   blocks.
  /// </summary>
  /// <remarks>Compatible with output produced by <see cref="SequentialBlockWriteOnceStream" /></remarks>
  [PublicAPI]
  public sealed class SequentialBlockReadOnlyStream : BlockStream
  {
    private byte[] currentBlock = ArrayPool<byte>.Shared.Rent(short.MaxValue);
    private short fill;
    private short pos;


    public SequentialBlockReadOnlyStream(Stream wrappedStream,
      IBlockTransformer transformer, bool leaveOpen = false, short blockSize = BLOCK_SIZE)
      : base(wrappedStream, transformer, leaveOpen, blockSize)
    {
      if (!wrappedStream.CanRead) {
        throw new ArgumentException("Streams must be readable", nameof(wrappedStream));
      }
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanTimeout => WrappedStream.CanTimeout;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => CurrentPosition;
      set => throw new NotSupportedException();
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
      try {
        while (buffer.Length > 0) {
          ReadNextBlock();
          var copyable = Math.Min(fill - pos, buffer.Length);
          if (copyable <= 0) {
            continue;
          }

          currentBlock.AsSpan(pos, copyable).CopyTo(buffer);
          pos += (short)copyable;
          CurrentPosition += copyable;
          read += copyable;
          buffer = buffer.Slice(copyable);
        }
      }
      catch (EndOfStreamException) {
        // ignored
      }

      return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
      if (currentBlock.Length > 0) {
        ArrayPool<byte>.Shared.Return(currentBlock);
        currentBlock = Array.Empty<byte>();
      }

      base.Dispose(disposing);
    }

    private void ReadNextBlock()
    {
      if (fill > pos) {
        return;
      }

      Span<short> size = stackalloc short[1];
      WrappedStream.ReadFullBlock(MemoryMarshal.Cast<short, byte>(size));
      if (size[0] <= 0) {
        ThrowHelpers.ThrowInvalidBlock();
      }

      var buffer = currentBlock.AsSpan(0, size[0]);
      WrappedStream.ReadFullBlock(buffer);
      var transformed = Transformer.UntransformBlock(buffer, currentBlock);
      if (transformed > BlockSize || transformed <= 0) {
        ThrowHelpers.ThrowInvalidBlock();
      }

      fill = (short)transformed;
      pos = 0;
    }
  }
}
