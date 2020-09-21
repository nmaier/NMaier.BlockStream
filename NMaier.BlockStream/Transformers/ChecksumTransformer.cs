using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  /// <summary>
  ///   Transforms data, adding a simple checksum for some integrity checking
  /// </summary>
  [PublicAPI]
  public sealed class ChecksumTransformer : IBlockTransformer
  {
    private static readonly ulong[] utable = GenerateTable(0xD800000000000000);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeChecksum(ReadOnlySpan<byte> data, ulong initialValue = ulong.MaxValue,
      ulong finalXor = ulong.MaxValue)
    {
      var table = utable.AsSpan();
      for (int i = 0, e = data.Length; i < e; i++) {
        var t = data[i];
        initialValue = (initialValue >> 8) ^ table[(byte)initialValue ^ t];
      }

      return initialValue ^ finalXor;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong[] GenerateTable(ulong poly)
    {
      var rv = new ulong[256];
      for (uint i = 0; i < 256; ++i) {
        ulong crc = i;

        for (uint j = 0; j < 8; ++j) {
          if ((crc & 1) == 1) {
            crc = (crc >> 1) ^ poly;
          }
          else {
            crc >>= 1;
          }
        }

        rv[i] = crc;
      }

      return rv;
    }

    /// <inheritdoc />
    public bool MayChangeSize => true;

    /// <inheritdoc />
    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      var rv = new byte[block.Length + sizeof(ulong)];
      var sum = ComputeChecksum(block);
      block.CopyTo(rv);
      BinaryPrimitives.WriteUInt64LittleEndian(rv.AsSpan(block.Length), sum);
      return rv;
    }

    /// <inheritdoc />
    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      var sum = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(input.Length - sizeof(ulong)));
      if (sum != ComputeChecksum(input.Slice(0, input.Length - sizeof(ulong)))) {
        throw new IOException("Mismatched checksum");
      }

      if (!input.Overlaps(block, out var off) || off != 0) {
        input.Slice(0, input.Length - sizeof(ulong)).CopyTo(block);
      }

      return input.Length - sizeof(ulong);
    }
  }
}