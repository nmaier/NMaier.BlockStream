using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.MemoryMappedFiles;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class BlockReadOnlyStream : BlockStream
  {
    private readonly byte[] currentBlock = new byte[short.MaxValue];
    private long currentIndex = -1;
    private readonly MemoryMappedFile? mmap;

    public BlockReadOnlyStream(Stream wrappedStream, IBlockCache? cache = null)
      : this(wrappedStream, new NoneBlockTransformer(), cache: cache)
    {
    }

    public BlockReadOnlyStream(Stream wrappedStream, IBlockTransformer transformer, short blockSize = BLOCK_SIZE,
      IBlockCache? cache = null)
      : base(wrappedStream, transformer, blockSize, cache)
    {
      ReadIndex();
      if (wrappedStream is FileStream fstream) {
        mmap = MemoryMappedFile.CreateFromFile(fstream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None,
                                               true);
      }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
    public override long Length => CurrentLength;

    public override long Position
    {
      get => CurrentPosition;
      set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      return Read(buffer.AsSpan(offset, count));
    }

#if NET48
    public int Read(Span<byte> buffer)
#else
    public override int Read(Span<byte> buffer)
#endif
    {
      var read = 0;
      for (;;) {
        var block = CurrentPosition / BlockSize;
        if (currentIndex != block) {
          if (!FillBlock(block)) {
            return read;
          }
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
      base.Dispose(disposing);
    }

    private bool FillBlock(in long block)
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
        if (mmap != null) {
          using var view = mmap.CreateViewAccessor(extent.Offset, extentSpan.Length, MemoryMappedFileAccess.Read);
          if (view.ReadArray(0, currentBlock, 0, extentSpan.Length) != extentSpan.Length) {
            throw new IOException("Truncated read");
          }
        }
        else {
          WrappedStream.Seek(extent.Offset, SeekOrigin.Begin);
#if NET48
          WrappedStream.ReadFullBlock(currentBlock, extentSpan.Length);
#else
          WrappedStream.ReadFullBlock(extentSpan);
#endif
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
      var footer = footerLength < 4096
        ? stackalloc byte[(int)footerLength]
        : new byte[footerLength];
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