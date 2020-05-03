using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using JetBrains.Annotations;
#if NET48
using System.Buffers;
#endif

namespace NMaier.BlockStream
{
  [PublicAPI]
  public static class Extensions
  {
    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not en9ough data to fill the block is available
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    /// <param name="length">Length to read. If this value is less than 0, then the length of the buffer will be used</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadFullBlock(this Stream stream, byte[] buffer, int length = -1)
    {
      ReadFullBlock(stream, buffer, 0, length);
    }

    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not en9ough data to fill the block is available
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadFullBlock(this Stream stream, ArraySegment<byte> buffer)
    {
      ReadFullBlock(stream, buffer.Array, buffer.Offset, buffer.Count);
    }


    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not en9ough data to fill the block is available
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    /// <param name="offset">Offset in the target buffer</param>
    /// <param name="length">Length to read.</param>
    public static void ReadFullBlock(this Stream stream, byte[] buffer, int offset, int length)
    {
      if (offset < 0) {
        throw new ArgumentOutOfRangeException(nameof(offset));
      }

      if (length < 0) {
        length = buffer.Length;
      }

      var remaining = length;
      if (remaining > buffer.Length) {
        throw new ArgumentException("Insufficient buffer", nameof(buffer));
      }

      while (remaining > 0) {
        var read = stream.Read(buffer, offset + length - remaining, remaining);
        if (read == remaining) {
          return;
        }

        if (read == 0) {
          throw new IOException("Truncated read");
        }

        remaining -= read;
      }
    }

#if NET48
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static void ReadFullBlock(this Stream stream, Span<byte> buffer, int length = -1)
    {
      length = length <= 0 ? buffer.Length : length;
#if NET48
      var temp = ArrayPool<byte>.Shared.Rent(length);
      try {
        ReadFullBlock(stream, temp, 0, length);
        temp.AsSpan(0, length).CopyTo(buffer);
      }
      finally {
        ArrayPool<byte>.Shared.Return(temp);
      }
#else
      var remaining = length;
      if (remaining > buffer.Length) {
        throw new ArgumentException("Insufficient buffer", nameof(buffer));
      }

      while (remaining > 0) {
        var read = stream.Read(buffer);
        if (read == remaining) {
          return;
        }

        if (read == 0) {
          throw new IOException("Truncated read");
        }

        remaining -= read;
        buffer = buffer.Slice(read);
      }
#endif
    }

    internal static byte[] DeriveKeyBytesReasonablySafeNotForStorage(this byte[] passphrase, int length)
    {
      // This is essentially OK. We never store the pass phrase or the derived key ourselves.
      // This will still allow an to try pass phrases fast-ish, if that's what he's after.
      using var der =
        new Rfc2898DeriveBytes(passphrase, new byte[] { 0xf9, 0x03, 0x02, 0xea, 0x42, 0x23, 0xab, 0xff }, 100);
      return der.GetBytes(length);
    }

#if NET48
    internal static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
      stream.Write(buffer.ToArray(), 0, buffer.Length);
    }
#endif
  }
}