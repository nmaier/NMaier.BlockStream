using System;
using System.IO;
using System.Runtime.CompilerServices;

using JetBrains.Annotations;

using NMaier.BlockStream.Internal;
using NMaier.BlockStream.Transformers;

using static System.Buffers.Binary.BinaryPrimitives;

namespace NMaier.BlockStream
{
  /// <summary>
  ///   A general readwrite block stream.
  /// </summary>
  /// <remarks>
  ///   Compatible with output from <see cref="BlockRandomAccessStream" /> and <see cref="BlockWriteOnceStream" />
  /// </remarks>
  [PublicAPI]
  public sealed class BlockRandomAccessStream : BlockStream
  {
    private readonly byte[] currentBlock = new byte[short.MaxValue];

    private bool blockDirty;
    private long currentIndex = -1;

    /// <summary>
    ///   Wraps a generic read+write stream.
    /// </summary>
    /// <remarks>
    ///   <list type="bullet">
    ///     <item>
    ///       <description>All wrapped streams must be seekable</description>
    ///     </item>
    ///     <item>
    ///       <description>
    ///         You should prefer <see cref="BlockReadOnlyStream" />/<see cref="BlockWriteOnceStream" /> or the
    ///         sequential versions when possible.
    ///       </description>
    ///     </item>
    ///   </list>
    /// </remarks>
    /// <param name="wrappedStream">Stream to be wrapped</param>
    /// <param name="blockSize">Block size to use</param>
    /// <param name="leaveOpen">Leave the wrapped stream open when disposing this block stream</param>
    /// <param name="cache">Optional block cache</param>
    public BlockRandomAccessStream(Stream wrappedStream, bool leaveOpen = false,
      short blockSize = BLOCK_SIZE, IBlockCache? cache = null) : this(
      wrappedStream,
      new NoneBlockTransformer(),
      leaveOpen,
      blockSize,
      cache)
    {
    }

    /// <summary>
    ///   Wraps a generic read+write stream.
    /// </summary>
    /// <remarks>
    ///   <list type="bullet">
    ///     <item>
    ///       <description>All wrapped streams must be seekable</description>
    ///     </item>
    ///     <item>
    ///       <description>
    ///         You should prefer <see cref="BlockReadOnlyStream" />/<see cref="BlockWriteOnceStream" /> or the
    ///         sequential versions when possible.
    ///       </description>
    ///     </item>
    ///   </list>
    /// </remarks>
    /// <param name="wrappedStream">Stream to be wrapped</param>
    /// <param name="transformer">Block transformer to use</param>
    /// <param name="leaveOpen">Leave the wrapped stream open when disposing this block stream</param>
    /// <param name="blockSize">Block size to use</param>
    /// <param name="cache">Optional block cache</param>
    public BlockRandomAccessStream(Stream wrappedStream, IBlockTransformer transformer,
      bool leaveOpen = false, short blockSize = BLOCK_SIZE, IBlockCache? cache = null) :
      base(wrappedStream, transformer, leaveOpen, blockSize, cache)
    {
      if (!wrappedStream.CanSeek) {
        throw new ArgumentException("Streams must be seekable", nameof(wrappedStream));
      }

      if (!wrappedStream.CanRead) {
        throw new ArgumentException("Streams must be readable", nameof(wrappedStream));
      }

      if (!wrappedStream.CanWrite) {
        throw new ArgumentException("Streams must be writable", nameof(wrappedStream));
      }

      ReadIndex();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;

    public override bool CanTimeout => WrappedStream.CanTimeout;

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

    /// <summary>
    ///   Like <see cref="Flush()" />, but allows to force a disk flush if the underlying stream is a <see cref="FileStream" />
    /// </summary>
    /// <param name="flushToDisk">Force a full disk flush</param>
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

      switch (currentIndex) {
        case >= 0: {
          // Update already allocated block
          if (!Extents.TryGetValue(currentIndex, out var extent)) {
            ThrowHelpers.ThrowBadExtent();
          }

          var transformed = Transformer.TransformBlock(currentBlock.AsSpan(0, BlockSize));
          var isNotLast = currentIndex != Extents.Count - 1;
          if (transformed.Length > extent.Length && isNotLast) {
            ThrowHelpers.ThrowLargeBlock();
          }

          if (WrappedStream.Seek(extent.Offset, SeekOrigin.Begin) != extent.Offset) {
            ThrowHelpers.ThrowSeekFailed();
          }

          WrappedStream.Write(transformed);
          Extents[currentIndex] = new Extent(extent.Offset, (short)transformed.Length);
          if (transformed.Length != extent.Length) {
            if (isNotLast) {
              ThrowHelpers.ThrowNoRandom();
            }
            else {
              WriteFooter();
            }
          }
          else if (CurrentFooterLength != CurrentLength) {
            WriteFooterLength();
          }

          break;
        }
        case -1: {
          var newindex = Extents.Count;
          var last = newindex > 0 ? Extents[newindex - 1] : new Extent(0, 0);
          var transformed = Transformer.TransformBlock(currentBlock.AsSpan(0, BlockSize));
          if (transformed.Length > short.MaxValue) {
            ThrowHelpers.ThrowBlockTooLarge();
          }

          var extent = new Extent(last.Offset + last.Length, (short)transformed.Length);
          // Write a placeholder (which moves the footer)
          Extents[newindex] = new Extent(-1, (short)transformed.Length);
          WriteFooter();

          // Write full block
          if (WrappedStream.Seek(extent.Offset, SeekOrigin.Begin) != extent.Offset) {
            ThrowHelpers.ThrowSeekFailed();
          }

          WrappedStream.Write(transformed);
          Extents[newindex] = extent;
          WriteFooter();
          break;
        }
        default:
          ThrowHelpers.ThrowCorruptCurrentIndex();
          break;
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
      if (Position >= Length) {
        Position = Length;
        return 0;
      }

      var read = 0;
      for (;;) {
        var block = CurrentPosition / BlockSize;
        if (!FillBlock(block)) {
          return read;
        }

        var bpos = CurrentPosition % BlockSize;
        // Must not over-read
        var rem = Math.Min(
          Math.Min(CurrentLength - CurrentPosition, BlockSize - bpos),
          buffer.Length);
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
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must be positive");
          }

          CurrentPosition = offset;
          break;
        case SeekOrigin.Current:
          if (offset + CurrentPosition < 0) {
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must result in a positive position");
          }

          CurrentPosition += offset;
          break;
        case SeekOrigin.End:
          if (offset + CurrentLength < 0) {
            ThrowHelpers.ThrowArgumentOutOfRangeException(
              nameof(offset),
              offset,
              "Offset must result in a positive position");
          }

          CurrentPosition = CurrentLength + offset;
          break;
        default:
          ThrowHelpers.ThrowArgumentOutOfRangeException(
            nameof(origin),
            origin,
            "Invalid Origin");
          break;
      }

      return CurrentPosition;
    }

    public override void SetLength(long value)
    {
      if (value < 0) {
        ThrowHelpers.ThrowArgumentOutOfRangeException(
          nameof(value),
          value,
          "Length must be positive");
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
        _ = Seek(0, SeekOrigin.End);
        var rem = value - CurrentLength;
        Span<byte> buff = stackalloc byte[(int)Math.Min(rem, 4096)];
        var pos = Position;
        while (rem > 0) {
          var cur = Math.Min(buff.Length, rem);
          Write(buff.Slice(0, (int)cur));
          rem -= cur;
        }

        _ = Seek(pos, SeekOrigin.Begin);

        Flush();
        return;
      }

      // Shrink
      var maxblock = (long)Math.Ceiling((double)value / BlockSize);
      while (Extents.Count >= maxblock) {
        _ = Extents.Remove(Extents.Count - 1);
      }

      CurrentLength = value;
      if (CurrentPosition > value) {
        _ = Seek(0, SeekOrigin.End);
      }

      WriteFooter();
      MakeClean(false);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      Write(buffer.AsSpan(offset, count));
    }

#if NETFRAMEWORK
    /// <summary>
    ///   See <see cref="Write(byte[],int,int)" />
    /// </summary>
    /// <param name="buffer">Target buffer</param>
    public void Write(ReadOnlySpan<byte> buffer)
#else
    public override void Write(ReadOnlySpan<byte> buffer)
#endif
    {
      if (Transformer.MayChangeSize && CurrentPosition < CurrentLength) {
        ThrowHelpers.ThrowNoRandom();
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
          ThrowHelpers.ThrowInvalidExtent();
        }

        // Empty placeholder block
        return true;
      }

      var extentSpan = currentBlock.AsSpan(0, extent.Length);
      var blockSpan = currentBlock.AsSpan(0, BlockSize);
      blockSpan.Clear();
      if (Cache == null || !Cache.TryReadBlock(blockSpan, block)) {
        WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
#if NETFRAMEWORK
        WrappedStream.ReadFullBlock(currentBlock, extentSpan.Length);
#else
        WrappedStream.ReadFullBlock(extentSpan);
#endif
        if (Transformer.UntransformBlock(extentSpan, currentBlock) != BlockSize) {
          ThrowHelpers.ThrowCorruptBlock();
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

      _ = WrappedStream.Seek(-(sizeof(long) * 2), SeekOrigin.End);
      Span<byte> blen = stackalloc byte[sizeof(long) * 2];
      WrappedStream.ReadFullBlock(blen);
      var footerLength = ReadInt64LittleEndian(blen);
      CurrentFooterLength =
        CurrentLength = ReadInt64LittleEndian(blen.Slice(sizeof(long)));
      _ = WrappedStream.Seek(-(sizeof(long) * 2) - footerLength, SeekOrigin.End);
      var footer = footerLength < 4096
        ? stackalloc byte[(int)footerLength]
        : new byte[footerLength];
      WrappedStream.ReadFullBlock(footer);
      var block = 0L;
      while (footer.Length > 0) {
        var extent = new Extent(
          ReadInt64LittleEndian(footer),
          ReadInt16LittleEndian(footer.Slice(sizeof(long))));
#pragma warning disable IDE0078 // Use pattern matching
        if (extent.Offset < 0 || extent.Length < 0) {
#pragma warning restore IDE0078 // Use pattern matching
          // Tombstone extents can only happen in the very end of the file, when the program was interrupted before being able to full allocate new blocks.
          // We ignore them and do not actually allocate blocks
          // FillBlockWithInit/WriteFooter will later fix up the stuff, as needed.
          continue;
        }

        Extents[block++] = extent;
        footer = footer.Slice(sizeof(long) + sizeof(short));
      }

      MakeClean(false);
    }
  }
}
