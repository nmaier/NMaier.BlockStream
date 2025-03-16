using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

using JetBrains.Annotations;

using NMaier.BlockStream.Internal;
using NMaier.BlockStream.Transformers;

namespace NMaier.BlockStream;

/// <summary>
///   A general reader block stream.
/// </summary>
/// <remarks>
///   Compatible with output from <see cref="BlockRandomAccessStream" /> and <see cref="BlockWriteOnceStream" />
/// </remarks>
[PublicAPI]
public sealed class BlockReadOnlyStream : BlockStream
{
  private readonly ReaderEnhancedStream cursor;
  private readonly MemoryMappedFile? memoryMapped;

  public BlockReadOnlyStream(Stream wrappedStream, IBlockCache? cache = null) : this(
    wrappedStream,
    new NoneBlockTransformer(),
    cache: cache)
  {
  }

  public BlockReadOnlyStream(
    Stream wrappedStream,
    IBlockTransformer transformer,
    bool leaveOpen = false,
    short blockSize = BLOCK_SIZE,
    IBlockCache? cache = null) : base(wrappedStream, transformer, leaveOpen, blockSize, cache)
  {
    if (!wrappedStream.CanSeek) {
      throw new ArgumentException("Streams must be seekable", nameof(wrappedStream));
    }

    if (!wrappedStream.CanRead) {
      throw new ArgumentException("Streams must be readable", nameof(wrappedStream));
    }

    ReadIndex();
    if (wrappedStream is FileStream fileStream) {
      memoryMapped = MemoryMappedFile.CreateFromFile(
        fileStream,
        null,
        0,
        MemoryMappedFileAccess.Read,
        HandleInheritability.None,
        true);
    }

    cursor = CreateCursor();
  }

  public override bool CanRead => true;
  public override bool CanSeek => true;
  public override bool CanTimeout => WrappedStream.CanTimeout;
  public override bool CanWrite => false;
  public override long Length => CurrentLength;

  public override long Position
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => cursor.Position;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => cursor.Position = value;
  }

  protected override void Dispose(bool disposing)
  {
    memoryMapped?.Dispose();
    cursor.Dispose();
    base.Dispose(disposing);
  }

  public override void Flush()
  {
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public override int Read(byte[] buffer, int offset, int count)
  {
    return cursor.Read(buffer.AsSpan(offset, count));
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public override int Read(Span<byte> buffer)
  {
    return cursor.Read(buffer);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public override long Seek(long offset, SeekOrigin origin)
  {
    return cursor.Seek(offset, origin);
  }

  public override void SetLength(long value)
  {
    throw new NotSupportedException();
  }

  public override void Write(byte[] buffer, int offset, int count)
  {
    throw new NotSupportedException();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ReaderEnhancedStream CreateCursor()
  {
    return new BlockReadOnlyCursor(this);
  }

  internal bool FillBlock(in long block, byte[] currentBlock, ref long currentIndex)
  {
    if (currentIndex == block) {
      return true;
    }

    if (!Extents.TryGetValue(block, out var extent)) {
      // Block does not exist (yet)
      return false;
    }

    var blockSpan = currentBlock.AsSpan(0, BlockSize);

    if (extent.Length == 0) {
      if (!Transformer.MayChangeSize) {
        ThrowHelpers.ThrowInvalidExtent();
      }

      // Empty placeholder block
      blockSpan.Clear();

      return true;
    }

    var extentSpan = currentBlock.AsSpan(0, extent.Length);
    blockSpan.Clear();
    if (Cache == null || !Cache.TryReadBlock(blockSpan, block)) {
      if (memoryMapped != null) {
        using var view = memoryMapped.CreateViewAccessor(extent.Offset, extentSpan.Length, MemoryMappedFileAccess.Read);
        if (view.ReadArray(0, currentBlock, 0, extentSpan.Length) != extentSpan.Length) {
          ThrowHelpers.ThrowTruncatedRead();
        }
      }
      else {
        WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
#if NETFRAMEWORK
            WrappedStream.ReadFullBlock(currentBlock, extentSpan.Length);
#else
        WrappedStream.ReadFullBlock(extentSpan);
#endif
      }

      if (Transformer.UntransformBlock(extentSpan, currentBlock) != BlockSize) {
        ThrowHelpers.ThrowCorruptBlock();
      }

      Cache?.Cache(blockSpan, block);
    }

    currentIndex = block;

    return true;
  }

  private void ReadIndex()
  {
    if (WrappedStream.Length == 0) {
      return;
    }

    _ = WrappedStream.Seek(-(sizeof(long) * 2), SeekOrigin.End);
    Span<byte> footerLocation = stackalloc byte[sizeof(long) * 2];
    WrappedStream.ReadFullBlock(footerLocation);
    var footerLength = BinaryPrimitives.ReadInt64LittleEndian(footerLocation);
    if (footerLength < 0) {
      throw new IOException("Block stream has corrupt footer");
    }

    CurrentFooterLength = CurrentLength = BinaryPrimitives.ReadInt64LittleEndian(footerLocation.Slice(sizeof(long)));
    _ = WrappedStream.Seek(-(sizeof(long) * 2) - footerLength, SeekOrigin.End);
    var footer = footerLength < 4096 ? stackalloc byte[(int)footerLength] : new byte[footerLength];
    WrappedStream.ReadFullBlock(footer);

    var block = 0L;
    while (footer.Length > 0) {
      var extent = new Extent(
        BinaryPrimitives.ReadInt64LittleEndian(footer),
        BinaryPrimitives.ReadInt16LittleEndian(footer.Slice(sizeof(long))));
#pragma warning disable IDE0078 // Use pattern matching
      if (extent.Offset < 0L || extent.Length < 0) {
#pragma warning restore IDE0078 // Use pattern matching
        // Tombstone extents can only happen in the very end of the file, when the program was interrupted before being able to full allocate new blocks.
        // We ignore them and do not actually allocate blocks
        // FillBlockWithInit/WriteFooter will later fix up the stuff, as needed.
        continue;
      }

      Extents[block++] = extent;
      footer = footer.Slice(sizeof(long) + sizeof(short));
    }
  }
}
