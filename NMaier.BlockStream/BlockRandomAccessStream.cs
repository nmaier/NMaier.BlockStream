using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using static System.Buffers.Binary.BinaryPrimitives;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class BlockRandomAccessStream : BlockStream
  {
    private readonly byte[] currentBlock = new byte[short.MaxValue];

    private bool blockDirty;
    private long currentIndex = -1;

    public BlockRandomAccessStream(Stream wrappedStream, IBlockCache? cache = null)
      : this(wrappedStream, new NoneBlockTransformer(), cache: cache)
    {
    }

    public BlockRandomAccessStream(Stream wrappedStream, IBlockTransformer transformer, short blockSize = BLOCK_SIZE,
      IBlockCache? cache = null)
      : base(wrappedStream, transformer, blockSize, cache)
    {
      ReadIndex();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;

    [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
    public override long Length => CurrentLength;

    public override long Position
    {
      get => CurrentPosition;
      set => Seek(value, SeekOrigin.Begin);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush()
    {
      Flush(false);
    }

    public void Flush(bool flushToDisk)
    {
      if (!blockDirty) {
        if (!flushToDisk) {
          return;
        }

        switch (WrappedStream) {
          case FileStream fs:
            fs.Flush(true);
            break;
          default:
            WrappedStream.Flush();
            break;
        }

        return;
      }

      // Update already allocated block
      if (currentIndex >= 0) {
        if (!Extents.TryGetValue(currentIndex, out var extent)) {
          throw new IOException("Cannot write to bad extent");
        }

        var transformed = Transformer.TransformBlock(currentBlock.AsSpan(0, BlockSize));
        var isLast = currentIndex != Extents.Count - 1;
        if (transformed.Length > extent.Length && isLast) {
          throw new IOException("Cannot write new compressable block larger than on disk block");
        }

        WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
        WrappedStream.Write(transformed);
        Extents[currentIndex] = new Extent(extent.Offset, (short)transformed.Length);
        if (transformed.Length != extent.Length) {
          if (isLast) {
            WriteFooterLengthAndLast(extent.Offset, (short)transformed.Length);
          }
          else {
            WriteFooter();
          }
        }
        else if (CurrentFooterLength != CurrentLength) {
          WriteFooterLength();
        }
      }
      else if (currentIndex == -1) {
        var newindex = Extents.Count;
        var last = newindex > 0 ? Extents[newindex - 1] : new Extent(0, 0);
        var transformed = Transformer.TransformBlock(currentBlock.AsSpan(0, BlockSize));
        if (transformed.Length > short.MaxValue) {
          throw new IOException("transformed block too large!");
        }

        var extent = new Extent(last.Offset + last.Length, (short)transformed.Length);
        // Write a placeholder (which moves the footer)
        Extents[newindex] = new Extent(-1, (short)transformed.Length);
        WriteFooter();

        // Write full block
        WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
        WrappedStream.Write(transformed);
        Extents[newindex] = extent;
        WriteFooter();
      }
      else {
        throw new IOException("Corrupt currentIndex");
      }

      MakeClean(flushToDisk);
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
        var block = CurrentPosition / BlockSize;
        if (!FillBlock(block)) {
          return read;
        }

        var bpos = CurrentPosition % BlockSize;
        // Must not over-read
        var rem = Math.Min(Math.Min(CurrentLength - CurrentPosition, BlockSize - bpos), buffer.Length);
        if (rem == 0) {
          return read;
        }

        currentBlock.AsSpan((int)bpos, (int)rem).CopyTo(buffer);
        read += (int)rem;
        CurrentPosition += rem;
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
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be positive");
          }

          CurrentPosition = offset;
          break;
        case SeekOrigin.Current:
          if (offset + CurrentPosition < 0) {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must result in a positive position");
          }

          CurrentPosition += offset;
          break;
        case SeekOrigin.End:
          if (offset + CurrentLength < 0) {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must result in a positive position");
          }

          CurrentPosition = CurrentLength + offset;
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
      }

      return CurrentPosition;
    }

    public override void SetLength(long value)
    {
      if (value < 0) {
        throw new ArgumentOutOfRangeException(nameof(value), value, "Length must be positive");
      }

      // Nothing to be done
      if (value == CurrentLength) {
        return;
      }

      if (value == 0) {
        // fast path: clear Extents and write footer
        Extents.Clear();
        CurrentLength = CurrentFooterLength = 0;
        CurrentPosition = 0;
        WriteFooter();
        MakeClean(false);
        return;
      }

      if (value > CurrentLength) {
        Flush();
        // Grow
        Seek(0, SeekOrigin.End);
        var rem = value - CurrentLength;
        Span<byte> buff = stackalloc byte[4096];
        while (rem > 0) {
          var cur = Math.Min(buff.Length, rem);
          Write(buff.Slice(0, (int)cur));
          rem -= cur;
        }

        Flush();
        return;
      }

      // Shrink
      var maxblock = (long)Math.Ceiling((double)value / BlockSize);
      while (Extents.Count >= maxblock) {
        Extents.Remove(Extents.Count);
      }

      CurrentLength = value;
      WriteFooter();
      MakeClean(false);
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
      if (Transformer.MayChangeSize && CurrentPosition < CurrentLength) {
        throw new IOException("Cannot random write to compressable stream");
      }

      while (buffer.Length > 0) {
        var block = CurrentPosition / BlockSize;
        if (FillBlock(block)) {
          var bpos = CurrentPosition % BlockSize;
          var rem = Math.Min(BlockSize - bpos, buffer.Length);
          buffer.Slice(0, (int)rem).CopyTo(currentBlock.AsSpan((int)bpos));
          CurrentPosition += rem;
          CurrentLength = Math.Max(CurrentLength, CurrentPosition);
          blockDirty = true;
          buffer = buffer.Slice((int)rem);
          Cache?.Invalidate(block);
        }
        else {
          // Need to append a block
          SetLength(CurrentPosition);
          Flush();

          var bpos = CurrentPosition % BlockSize;
          var rem = Math.Min(BlockSize - bpos, buffer.Length);
          buffer.Slice(0, (int)rem).CopyTo(currentBlock.AsSpan((int)bpos));
          CurrentPosition += rem;
          CurrentLength = Math.Max(CurrentLength, CurrentPosition);
          blockDirty = true;
          buffer = buffer.Slice((int)rem);
          Cache?.Invalidate(block);
          // Flush again to materialize the new block in the file
          Flush();
        }
      }
    }

    protected override void Dispose(bool disposing)
    {
      Flush();
      base.Dispose(disposing);
    }

    internal bool FillBlock(in long block)
    {
      if (currentIndex == block) {
        return true;
      }

      if (!Extents.TryGetValue(block, out var extent)) {
        // Block does not exist (yet)
        return false;
      }

      Flush();
      if (extent.Length == 0) {
        if (!Transformer.MayChangeSize) {
          throw new IOException("Invalid extent");
        }

        // Empty placeholder block
        return true;
      }

      var extentSpan = currentBlock.AsSpan(0, extent.Length);
      var blockSpan = currentBlock.AsSpan(0, BlockSize);
      blockSpan.Clear();
      if (Cache == null || !Cache.TryReadBlock(blockSpan, block)) {
        WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
#if NET48
        WrappedStream.ReadFullBlock(currentBlock, extentSpan.Length);
#else
        WrappedStream.ReadFullBlock(extentSpan);
#endif
        if (Transformer.UntransformBlock(extentSpan, currentBlock) != BlockSize) {
          throw new IOException("Corrupt transformed block");
        }

        Cache?.Cache(blockSpan, block);
      }

      currentIndex = block;
      return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MakeClean(bool flushToDisk)
    {
      currentBlock.AsSpan(0, BlockSize).Clear();
      currentIndex = -1;
      blockDirty = false;
      switch (WrappedStream) {
        case FileStream fs:
          fs.Flush(flushToDisk);
          break;
        default:
          WrappedStream.Flush();
          break;
      }
    }

    private void ReadIndex()
    {
      if (WrappedStream.Length == 0) {
        WriteFooter();
        MakeClean(false);
        return;
      }

      WrappedStream.Seek(-(sizeof(long) * 2), SeekOrigin.End);
      Span<byte> blen = stackalloc byte[sizeof(long) * 2];
      WrappedStream.ReadFullBlock(blen);
      var footerLength = ReadInt64LittleEndian(blen);
      CurrentFooterLength = CurrentLength = ReadInt64LittleEndian(blen.Slice(sizeof(long)));
      WrappedStream.Seek(-(sizeof(long) * 2) - footerLength, SeekOrigin.End);
      var footer = footerLength < 4096
        ? stackalloc byte[(int)footerLength]
        : new byte[footerLength];
      WrappedStream.ReadFullBlock(footer);
      var block = 0L;
      while (footer.Length > 0) {
        var extent = new Extent(ReadInt64LittleEndian(footer), ReadInt16LittleEndian(footer.Slice(sizeof(long))));
        if (extent.Offset < 0 || extent.Length < 0) {
          // Tombstone extents can only happen in the very end of the file, when the program was interrupted before being able to full allocate new blocks.
          // We ignore them and do not actually allocate blocks
          // FillBlockWithInit/WriteFooter will later fix up the stuff, as needed
          continue;
        }

        Extents[block++] = extent;
        footer = footer.Slice(sizeof(long) + sizeof(short));
      }

      MakeClean(false);
    }
  }
}