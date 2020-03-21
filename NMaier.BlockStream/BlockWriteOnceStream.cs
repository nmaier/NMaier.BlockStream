using System;
using System.IO;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class BlockWriteOnceStream : BlockStream
  {
    private readonly byte[] currentBlock;
    private int fill;

    public BlockWriteOnceStream(Stream wrappedStream) : this(wrappedStream, new NoneBlockTransformer())
    {
    }

    public BlockWriteOnceStream(Stream wrappedStream, IBlockTransformer transformer, short blockSize = BLOCK_SIZE)
      : base(wrappedStream, transformer, blockSize)
    {
      currentBlock = new byte[BlockSize];
      WrappedStream.Seek(0, SeekOrigin.Begin);
      wrappedStream.SetLength(0);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => CurrentLength;

    public override long Position
    {
      get => CurrentPosition;
      set { throw new NotSupportedException(); }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
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

    public void SkipToNextBlock()
    {
      if (fill == 0) {
        return;
      }

      currentBlock.AsSpan(fill).Clear();
      WriteBlock(currentBlock);
      CurrentPosition = CurrentLength += BlockSize - fill;
      fill = 0;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      Write(buffer.AsSpan(offset, count));
    }

#if NET48
    public void Write(ReadOnlySpan<byte> buffer)
#else
    public override void Write(ReadOnlySpan<byte> buffer)
#endif
    {
      if (fill > 0) {
        // Fill current block
        var span = currentBlock.AsSpan(fill);
        var max = Math.Min(span.Length, buffer.Length);
        buffer.Slice(0, max).CopyTo(span);
        buffer = buffer.Slice(max);
        fill += max;
        CurrentLength = CurrentPosition += max;
        if (fill != BlockSize) {
          return;
        }

        WriteBlock(currentBlock);
        fill = 0;
      }

      while (buffer.Length > BlockSize) {
        WriteBlock(buffer.Slice(0, BlockSize));
        buffer = buffer.Slice(BlockSize);
        CurrentLength = CurrentPosition += BlockSize;
      }

      if (buffer.Length == 0) {
        return;
      }

      currentBlock.AsSpan().Clear();
      buffer.CopyTo(currentBlock);
      fill = buffer.Length;
      CurrentLength = CurrentPosition += fill;
    }

    protected override void Dispose(bool disposing)
    {
      if (fill > 0) {
        // Write last block
        WriteBlock(currentBlock);
        fill = 0;
      }

      WriteFooter();
      base.Dispose(disposing);
    }

    private void WriteBlock(ReadOnlySpan<byte> block)
    {
      var transformed = Transformer.TransformBlock(block);
      if (transformed.Length > short.MaxValue) {
        throw new IOException("Transformed block to large");
      }

      Extents[Extents.Count] = new Extent(WrappedStream.Position, (short)transformed.Length);
      WrappedStream.Write(transformed);
    }
  }
}