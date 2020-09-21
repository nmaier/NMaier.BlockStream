using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
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
    private readonly MemoryMappedFile? mmap;

    public BlockReadOnlyStream(Stream wrappedStream, IBlockCache? cache = null) : this(
      wrappedStream, new NoneBlockTransformer(), cache: cache)
    {
    }

    public BlockReadOnlyStream(Stream wrappedStream, IBlockTransformer transformer, short blockSize = BLOCK_SIZE,
      IBlockCache? cache = null) : base(wrappedStream, transformer, blockSize, cache)
    {
      ReadIndex();
      if (wrappedStream is FileStream fstream) {
        mmap = MemoryMappedFile.CreateFromFile(fstream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None,
                                               true);
      }

      cursor = CreateCursor();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
    public override long Length => CurrentLength;

    public override long Position
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => cursor.Position;
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set => cursor.Position = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReaderEnhancedStream CreateCursor()
    {
      return new BlockReadOnlyCursor(this);
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

#if !NET48
    public override void Write(ReadOnlySpan<byte> buffer)
    {
      throw new NotSupportedException();
    }
#endif

    protected override void Dispose(bool disposing)
    {
      mmap?.Dispose();
      cursor.Dispose();
      base.Dispose(disposing);
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
          throw new IOException("Invalid extent");
        }

        // Empty placeholder block
        blockSpan.Clear();
        return true;
      }

      var extentSpan = currentBlock.AsSpan(0, extent.Length);
      blockSpan.Clear();
      if (Cache == null || !Cache.TryReadBlock(blockSpan, block)) {
        if (mmap != null) {
          using var view = mmap.CreateViewAccessor(extent.Offset, extentSpan.Length, MemoryMappedFileAccess.Read);
          if (view.ReadArray(0, currentBlock, 0, extentSpan.Length) != extentSpan.Length) {
            throw new IOException("Truncated read");
          }
        }
        else {
          lock (WrappedStream) {
            WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
#if NET48
            WrappedStream.ReadFullBlock(currentBlock, extentSpan.Length);
#else
            WrappedStream.ReadFullBlock(extentSpan);
#endif
          }
        }

        if (Transformer.UntransformBlock(extentSpan, currentBlock) != BlockSize) {
          throw new IOException("Corrupt transformed block");
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

      WrappedStream.Seek(-(sizeof(long) * 2), SeekOrigin.End);
      Span<byte> blen = stackalloc byte[sizeof(long) * 2];
      WrappedStream.ReadFullBlock(blen);
      var footerLength = BinaryPrimitives.ReadInt64LittleEndian(blen);
      CurrentFooterLength = CurrentLength = BinaryPrimitives.ReadInt64LittleEndian(blen.Slice(sizeof(long)));
      WrappedStream.Seek(-(sizeof(long) * 2) - footerLength, SeekOrigin.End);
      var footer = footerLength < 4096 ? stackalloc byte[(int)footerLength] : new byte[footerLength];
      WrappedStream.ReadFullBlock(footer);

      var block = 0L;
      while (footer.Length > 0) {
        var extent = new Extent(BinaryPrimitives.ReadInt64LittleEndian(footer),
                                BinaryPrimitives.ReadInt16LittleEndian(footer.Slice(sizeof(long))));
        if (extent.Offset < 0 || extent.Length < 0) {
          continue;
        }

        Extents[block++] = extent;
        footer = footer.Slice(sizeof(long) + sizeof(short));
      }
    }
  }
}