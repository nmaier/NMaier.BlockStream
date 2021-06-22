using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  /// <summary>
  ///   A sequential writer block stream. This kind of stream is not seekable, however it has more lax requirements for
  ///   blocks.
  /// </summary>
  /// <remarks>Output must be consumed with <see cref="SequentialBlockReadOnlyStream" /></remarks>
  [PublicAPI]
  public sealed class SequentialBlockWriteOnceStream : BlockStream
  {
    private byte[] currentBlock = ArrayPool<byte>.Shared.Rent(short.MaxValue);
    private short fill;


    /// <summary>
    ///   Wraps a generic stream into a sequential writer block stream. These streams are not seekable.
    /// </summary>
    /// <param name="wrappedStream">Stream to wrap</param>
    /// <param name="transformer">Block transformer to use</param>
    /// <param name="leaveOpen">Leave the wrapped stream open when disposing this block stream</param>
    /// <param name="blockSize">Block size to use</param>
    public SequentialBlockWriteOnceStream([NotNull] Stream wrappedStream,
      [NotNull] IBlockTransformer transformer, bool leaveOpen = false,
      short blockSize = BLOCK_SIZE) : base(
      wrappedStream,
      transformer,
      leaveOpen,
      blockSize)
    {
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => CurrentPosition;
      set => throw new NotSupportedException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush()
    {
      Flush(false);
    }

    /// <summary>
    ///   Like <see cref="Flush()" />, but allows to force a disk flush if the underlying stream is a <see cref="FileStream" />
    /// </summary>
    /// <param name="flushToDisk">Force a full disk flush</param>
    public void Flush(bool flushToDisk)
    {
      if (fill > 0) {
        var transformed = Transformer.TransformBlock(currentBlock.AsSpan(0, fill));
        if (transformed.Length > short.MaxValue) {
          ThrowHelpers.ThrowBlockTooLarge();
        }

        var size = new[] {
          (short)transformed.Length
        };
        WrappedStream.Write(MemoryMarshal.Cast<short, byte>(size.AsSpan()));
        WrappedStream.Write(transformed);
        fill = 0;
      }

      switch (WrappedStream) {
        case FileStream fs:
          fs.Flush(flushToDisk);
          break;
        default:
          WrappedStream.Flush();
          break;
      }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
      throw new NotSupportedException();
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
      Write(buffer.AsSpan(offset, count));
    }

#if NETFRAMEWORK
    /// <summary>
    ///   See <see cref="Write(byte[],int,int)" />
    /// </summary>
    /// <param name="buffer"></param>
    public void Write(ReadOnlySpan<byte> buffer)
#else
    public override void Write(ReadOnlySpan<byte> buffer)
#endif
    {
      while (buffer.Length > 0) {
        var copyable = Math.Min(BlockSize - fill, buffer.Length);
        buffer.Slice(0, copyable).CopyTo(currentBlock.AsSpan(fill));
        fill += (short)copyable;
        CurrentPosition += copyable;
        buffer = buffer.Slice(copyable);
        if (fill == BlockSize) {
          Flush();
        }
      }
    }

    protected override void Dispose(bool disposing)
    {
      Flush();
      base.Dispose(disposing);

      if (currentBlock.Length <= 0) {
        return;
      }

      ArrayPool<byte>.Shared.Return(currentBlock);
      currentBlock = Array.Empty<byte>();
    }
  }
}
