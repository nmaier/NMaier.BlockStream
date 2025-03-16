using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using JetBrains.Annotations;

using NMaier.BlockStream.Internal;

namespace NMaier.BlockStream.Transformers;

/// <summary>
///   Transforms data using ChaCha20 and verifies data using a Poly1305 MAC.
/// </summary>
[PublicAPI]
public sealed class ChaChaAndPolyTransformer : IBlockTransformer
{
  private const int NONCE_LENGTH = 12;
  private const int TAG_LENGTH = 16;
  private readonly uint cstate04;
  private readonly uint cstate05;
  private readonly uint cstate06;
  private readonly uint cstate07;
  private readonly uint cstate08;
  private readonly uint cstate09;
  private readonly uint cstate10;
  private readonly uint cstate11;
  private readonly RandomNumberGenerator nonceGenerator = RandomNumberGenerator.Create();
  private readonly uint polyr0;
  private readonly uint polyr1;
  private readonly uint polyr2;
  private readonly uint polyr3;
  private readonly uint polyr4;
  private readonly uint polys0;
  private readonly uint polys1;
  private readonly uint polys2;
  private readonly uint polys3;

  /// <summary>
  ///   Create a new transformer given the specified key.
  /// </summary>
  /// <remarks>
  ///   The key is not used verbatim, but used as input into a KDF deriving the actual keys for ChaCha20 and Poly1305
  ///   MAC
  /// </remarks>
  /// <param name="key">Key to use</param>
  public ChaChaAndPolyTransformer(string key) : this(Encoding.UTF8.GetBytes(key))
  {
  }

  /// <summary>
  ///   Create a new transformer given the specified key.
  /// </summary>
  /// <remarks>
  ///   The key is not used verbatim, but used as input into a KDF deriving the actual keys for ChaCha20 and Poly1305
  ///   MAC
  /// </remarks>
  /// <param name="key">Key to use</param>
  [SuppressMessage(
    "Security",
    "CA5379:Ensure Key Derivation Function algorithm is sufficiently strong",
    Justification = "<Pending>")]
  public ChaChaAndPolyTransformer(byte[] key)
  {
    // This is essentially OK. We never store the pass phrase or the derived key ourselves.
    // This will still allow one to try pass phrases fast-ish, if that's what he's after.
    using var derSalt = new Rfc2898DeriveBytes(
      key,
      [
        0xf9,
        0x03,
        0x02,
        0xea,
        0x42,
        0x23,
        0xab,
        0xff
      ],
      1,
      HashAlgorithmName.SHA1);
    using var der = new Rfc2898DeriveBytes(derSalt.GetBytes(64), derSalt.GetBytes(12), 100, HashAlgorithmName.SHA1);

    var cipherKey = der.GetBytes(32);
    cstate04 = (uint)(cipherKey[0] | (cipherKey[1] << 8) | (cipherKey[2] << 16) | (cipherKey[3] << 24));
    cstate05 = (uint)(cipherKey[4] | (cipherKey[5] << 8) | (cipherKey[6] << 16) | (cipherKey[7] << 24));
    cstate06 = (uint)(cipherKey[8] | (cipherKey[9] << 8) | (cipherKey[10] << 16) | (cipherKey[11] << 24));
    cstate07 = (uint)(cipherKey[12] | (cipherKey[13] << 8) | (cipherKey[14] << 16) | (cipherKey[15] << 24));
    cstate08 = (uint)(cipherKey[16] | (cipherKey[17] << 8) | (cipherKey[18] << 16) | (cipherKey[19] << 24));
    cstate09 = (uint)(cipherKey[20] | (cipherKey[21] << 8) | (cipherKey[22] << 16) | (cipherKey[23] << 24));
    cstate10 = (uint)(cipherKey[24] | (cipherKey[25] << 8) | (cipherKey[26] << 16) | (cipherKey[27] << 24));
    cstate11 = (uint)(cipherKey[28] | (cipherKey[29] << 8) | (cipherKey[30] << 16) | (cipherKey[31] << 24));

    var tagKey = der.GetBytes(32);
    polyr0 = (uint)(tagKey[0] | (tagKey[1] << 8) | (tagKey[2] << 16) | (tagKey[3] << 24)) & 0x3ffffff;
    polyr1 = ((uint)(tagKey[3] | (tagKey[4] << 8) | (tagKey[5] << 16) | (tagKey[6] << 24)) >> 2) & 0x3ffff03;
    polyr2 = ((uint)(tagKey[6] | (tagKey[7] << 8) | (tagKey[8] << 16) | (tagKey[9] << 24)) >> 4) & 0x3ffc0ff;
    polyr3 = ((uint)(tagKey[9] | (tagKey[10] << 8) | (tagKey[11] << 16) | (tagKey[12] << 24)) >> 6) & 0x3f03fff;
    polyr4 = ((uint)(tagKey[12] | (tagKey[13] << 8) | (tagKey[14] << 16) | (tagKey[15] << 24)) >> 8) & 0x00fffff;

    polys0 = (uint)(tagKey[16] | (tagKey[17] << 8) | (tagKey[18] << 16) | (tagKey[19] << 24));
    polys1 = (uint)(tagKey[20] | (tagKey[21] << 8) | (tagKey[22] << 16) | (tagKey[23] << 24));
    polys2 = (uint)(tagKey[24] | (tagKey[25] << 8) | (tagKey[26] << 16) | (tagKey[27] << 24));
    polys3 = (uint)(tagKey[28] | (tagKey[29] << 8) | (tagKey[30] << 16) | (tagKey[31] << 24));
  }

  /// <inheritdoc />
  public bool MayChangeSize => true;

  /// <inheritdoc />
  public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
  {
    var rv = new byte[NONCE_LENGTH + TAG_LENGTH + block.Length];
    nonceGenerator.GetBytes(rv, 0, NONCE_LENGTH);
    block.CopyTo(rv.AsSpan(NONCE_LENGTH + TAG_LENGTH));
    Cipher(
      rv.AsSpan(NONCE_LENGTH + TAG_LENGTH),
      rv.AsSpan(0, NONCE_LENGTH),
      rv.AsSpan(NONCE_LENGTH, TAG_LENGTH),
      false);

    return rv;
  }

  /// <inheritdoc />
  public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
  {
    var overlaps = input.Overlaps(block);
    var nonce = !overlaps ? input.Slice(0, NONCE_LENGTH) : input.Slice(0, NONCE_LENGTH).ToArray();
    var tag = input.Slice(NONCE_LENGTH, TAG_LENGTH).ToArray();
    var inBlock = input.Slice(NONCE_LENGTH + TAG_LENGTH);
    inBlock.CopyTo(block);
    Cipher(block.Slice(0, inBlock.Length), nonce, tag, true);

    return inBlock.Length;
  }

  private void Cipher(Span<byte> bytes, ReadOnlySpan<byte> nonce, Span<byte> tag, bool decrypt)
  {
    // chacha20 and poly1305 per https://tools.ietf.org/html/rfc7539
    Span<uint> tmpValues = stackalloc uint[16];
    Span<byte> padded = stackalloc byte[64];
    var tmp = MemoryMarshal.AsBytes(tmpValues);
    var length = bytes.Length;
    if (length <= 0) {
      return;
    }

    var s1 = polyr1 * 5;
    var s2 = polyr2 * 5;
    var s3 = polyr3 * 5;
    var s4 = polyr4 * 5;

    var h0 = 0U;
    var h1 = 0U;
    var h2 = 0U;
    var h3 = 0U;
    var h4 = 0U;

    const int BLOCK_SIZE = 16;

    const uint STATE0 = 0x61707865;
    const uint STATE1 = 0x3320646e;
    const uint STATE2 = 0x79622d32;
    const uint STATE3 = 0x6b206574;

    var counter = 1U;
    var n0 = (uint)(nonce[0] | (nonce[1] << 8) | (nonce[2] << 16) | (nonce[3] << 24));
    var n1 = (uint)(nonce[4] | (nonce[5] << 8) | (nonce[6] << 16) | (nonce[7] << 24));
    var n2 = (uint)(nonce[8] | (nonce[9] << 8) | (nonce[10] << 16) | (nonce[11] << 24));

    unchecked {
      for (var l = 0; l < length; l += 64) {
        var x0 = STATE0;
        var x1 = STATE1;
        var x2 = STATE2;
        var x3 = STATE3;
        var x4 = cstate04;
        var x5 = cstate05;
        var x6 = cstate06;
        var x7 = cstate07;
        var x8 = cstate08;
        var x9 = cstate09;
        var x10 = cstate10;
        var x11 = cstate11;
        var x12 = counter;
        var x13 = n0;
        var x14 = n1;
        var x15 = n2;

        for (var i = 0; i < 10; ++i) {
          x0 += x4;
          x12 = ((x12 ^ x0) << 16) | ((x12 ^ x0) >> 16);

          x8 += x12;
          x4 = ((x4 ^ x8) << 12) | ((x4 ^ x8) >> 20);

          x0 += x4;
          x12 = ((x12 ^ x0) << 8) | ((x12 ^ x0) >> 24);

          x8 += x12;
          x4 = ((x4 ^ x8) << 7) | ((x4 ^ x8) >> 25);

          x1 += x5;
          x13 = ((x13 ^ x1) << 16) | ((x13 ^ x1) >> 16);

          x9 += x13;
          x5 = ((x5 ^ x9) << 12) | ((x5 ^ x9) >> 20);

          x1 += x5;
          x13 = ((x13 ^ x1) << 8) | ((x13 ^ x1) >> 24);

          x9 += x13;
          x5 = ((x5 ^ x9) << 7) | ((x5 ^ x9) >> 25);

          x2 += x6;
          x14 = ((x14 ^ x2) << 16) | ((x14 ^ x2) >> 16);

          x10 += x14;
          x6 = ((x6 ^ x10) << 12) | ((x6 ^ x10) >> 20);

          x2 += x6;
          x14 = ((x14 ^ x2) << 8) | ((x14 ^ x2) >> 24);

          x10 += x14;
          x6 = ((x6 ^ x10) << 7) | ((x6 ^ x10) >> 25);

          x3 += x7;
          x15 = ((x15 ^ x3) << 16) | ((x15 ^ x3) >> 16);

          x11 += x15;
          x7 = ((x7 ^ x11) << 12) | ((x7 ^ x11) >> 20);

          x3 += x7;
          x15 = ((x15 ^ x3) << 8) | ((x15 ^ x3) >> 24);

          x11 += x15;
          x7 = ((x7 ^ x11) << 7) | ((x7 ^ x11) >> 25);

          x0 += x5;
          x15 = ((x15 ^ x0) << 16) | ((x15 ^ x0) >> 16);

          x10 += x15;
          x5 = ((x5 ^ x10) << 12) | ((x5 ^ x10) >> 20);

          x0 += x5;
          x15 = ((x15 ^ x0) << 8) | ((x15 ^ x0) >> 24);

          x10 += x15;
          x5 = ((x5 ^ x10) << 7) | ((x5 ^ x10) >> 25);

          x1 += x6;
          x12 = ((x12 ^ x1) << 16) | ((x12 ^ x1) >> 16);

          x11 += x12;
          x6 = ((x6 ^ x11) << 12) | ((x6 ^ x11) >> 20);

          x1 += x6;
          x12 = ((x12 ^ x1) << 8) | ((x12 ^ x1) >> 24);

          x11 += x12;
          x6 = ((x6 ^ x11) << 7) | ((x6 ^ x11) >> 25);

          x2 += x7;
          x13 = ((x13 ^ x2) << 16) | ((x13 ^ x2) >> 16);

          x8 += x13;
          x7 = ((x7 ^ x8) << 12) | ((x7 ^ x8) >> 20);

          x2 += x7;
          x13 = ((x13 ^ x2) << 8) | ((x13 ^ x2) >> 24);

          x8 += x13;
          x7 = ((x7 ^ x8) << 7) | ((x7 ^ x8) >> 25);

          x3 += x4;
          x14 = ((x14 ^ x3) << 16) | ((x14 ^ x3) >> 16);

          x9 += x14;
          x4 = ((x4 ^ x9) << 12) | ((x4 ^ x9) >> 20);

          x3 += x4;
          x14 = ((x14 ^ x3) << 8) | ((x14 ^ x3) >> 24);

          x9 += x14;
          x4 = ((x4 ^ x9) << 7) | ((x4 ^ x9) >> 25);
        }

        tmpValues[0] = x0 + STATE0;
        tmpValues[1] = x1 + STATE1;
        tmpValues[2] = x2 + STATE2;
        tmpValues[3] = x3 + STATE3;
        tmpValues[4] = x4 + cstate04;
        tmpValues[5] = x5 + cstate05;
        tmpValues[6] = x6 + cstate06;
        tmpValues[7] = x7 + cstate07;
        tmpValues[8] = x8 + cstate08;
        tmpValues[9] = x9 + cstate09;
        tmpValues[10] = x10 + cstate10;
        tmpValues[11] = x11 + cstate11;
        tmpValues[12] = x12 + counter;
        tmpValues[13] = x13 + n0;
        tmpValues[14] = x14 + n1;
        tmpValues[15] = x15 + n2;

        counter++;

        if (decrypt) {
          if (l + 64 > length) {
            bytes.Slice(l).CopyTo(padded);
            Poly(padded, 64);
          }
          else {
            Poly(bytes.Slice(l), 64);
          }
        }

        var slice = bytes.Slice(l);
        var sliceLength = Math.Min(slice.Length, 64);
        for (var i = 0; i < sliceLength; ++i) {
          slice[i] = (byte)(slice[i] ^ tmp[i]);
        }

        if (decrypt) {
          continue;
        }

        if (l + 64 > length) {
          bytes.Slice(l).CopyTo(padded);
          Poly(padded, 64);
        }
        else {
          Poly(bytes.Slice(l), 64);
        }
      }
    }

    h0 += 1;

    var d0 = ((ulong)h0 * polyr0) + ((ulong)h1 * s4) + ((ulong)h2 * s3) + ((ulong)h3 * s2) + ((ulong)h4 * s1);
    var d1 = ((ulong)h0 * polyr1) + ((ulong)h1 * polyr0) + ((ulong)h2 * s4) + ((ulong)h3 * s3) + ((ulong)h4 * s2);
    var d2 = ((ulong)h0 * polyr2) + ((ulong)h1 * polyr1) + ((ulong)h2 * polyr0) + ((ulong)h3 * s4) + ((ulong)h4 * s3);
    var d3 = ((ulong)h0 * polyr3) +
             ((ulong)h1 * polyr2) +
             ((ulong)h2 * polyr1) +
             ((ulong)h3 * polyr0) +
             ((ulong)h4 * s4);
    var d4 = ((ulong)h0 * polyr4) +
             ((ulong)h1 * polyr3) +
             ((ulong)h2 * polyr2) +
             ((ulong)h3 * polyr1) +
             ((ulong)h4 * polyr0);

    var c = (uint)(d0 >> 26);
    h0 = (uint)d0 & 0x3ffffff;
    d1 += c;
    c = (uint)(d1 >> 26);
    h1 = (uint)d1 & 0x3ffffff;
    d2 += c;
    c = (uint)(d2 >> 26);
    h2 = (uint)d2 & 0x3ffffff;
    d3 += c;
    c = (uint)(d3 >> 26);
    h3 = (uint)d3 & 0x3ffffff;
    d4 += c;
    c = (uint)(d4 >> 26);
    h4 = (uint)d4 & 0x3ffffff;
    h0 += c * 5;
    c = h0 >> 26;
    h0 &= 0x3ffffff;
    h1 += c;

    c = h1 >> 26;
    h1 &= 0x3ffffff;
    h2 += c;
    c = h2 >> 26;
    h2 &= 0x3ffffff;
    h3 += c;
    c = h3 >> 26;
    h3 &= 0x3ffffff;
    h4 += c;
    c = h4 >> 26;
    h4 &= 0x3ffffff;
    h0 += c * 5;
    c = h0 >> 26;
    h0 &= 0x3ffffff;
    h1 += c;

    var g0 = h0 + 5;
    c = g0 >> 26;
    g0 &= 0x3ffffff;
    var g1 = h1 + c;
    c = g1 >> 26;
    g1 &= 0x3ffffff;
    var g2 = h2 + c;
    c = g2 >> 26;
    g2 &= 0x3ffffff;
    var g3 = h3 + c;
    c = g3 >> 26;
    g3 &= 0x3ffffff;
    var g4 = (uint)(h4 + c - (1UL << 26));

    var mask = (g4 >> ((sizeof(uint) * 8) - 1)) - 1;
    g0 &= mask;
    g1 &= mask;
    g2 &= mask;
    g3 &= mask;
    g4 &= mask;
    mask = ~mask;
    h0 = (h0 & mask) | g0;
    h1 = (h1 & mask) | g1;
    h2 = (h2 & mask) | g2;
    h3 = (h3 & mask) | g3;
    h4 = (h4 & mask) | g4;

    h0 = (h0 | (h1 << 26)) & 0xffffffff;
    h1 = ((h1 >> 6) | (h2 << 20)) & 0xffffffff;
    h2 = ((h2 >> 12) | (h3 << 14)) & 0xffffffff;
    h3 = ((h3 >> 18) | (h4 << 8)) & 0xffffffff;

    var f = (ulong)h0 + polys0;
    h0 = (uint)f;
    f = (ulong)h1 + polys1 + (f >> 32);
    h1 = (uint)f;
    f = (ulong)h2 + polys2 + (f >> 32);
    h2 = (uint)f;
    f = (ulong)h3 + polys3 + (f >> 32);
    h3 = (uint)f;

    var uMAC = MemoryMarshal.Cast<byte, uint>(tag);
    if (!decrypt) {
      if (BitConverter.IsLittleEndian) {
        uMAC[0] = h0;
        uMAC[1] = h1;
        uMAC[2] = h2;
        uMAC[3] = h3;
      }
      else {
        uMAC[0] = BinaryPrimitives.ReverseEndianness(h0);
        uMAC[1] = BinaryPrimitives.ReverseEndianness(h1);
        uMAC[2] = BinaryPrimitives.ReverseEndianness(h2);
        uMAC[3] = BinaryPrimitives.ReverseEndianness(h3);
      }

      return;
    }

    if (BitConverter.IsLittleEndian) {
      if (uMAC[0] != h0 || uMAC[1] != h1 || uMAC[2] != h2 || uMAC[3] != h3) {
        ThrowHelpers.ThrowCorruptData();
      }
    }
    else {
      if (uMAC[0] != BinaryPrimitives.ReverseEndianness(h0) ||
          uMAC[1] != BinaryPrimitives.ReverseEndianness(h1) ||
          uMAC[2] != BinaryPrimitives.ReverseEndianness(h2) ||
          uMAC[3] != BinaryPrimitives.ReverseEndianness(h3)) {
        ThrowHelpers.ThrowCorruptData();
      }
    }

    return;

    void Poly(Span<byte> buffer, int blockLen)
    {
      var off = 0;
      for (var l = blockLen; l >= BLOCK_SIZE; l -= BLOCK_SIZE) {
        h0 += (uint)(buffer[off] | (buffer[off + 1] << 8) | (buffer[off + 2] << 16) | (buffer[off + 3] << 24)) &
              0x3ffffff;
        h1 += ((uint)(buffer[off + 3] | (buffer[off + 4] << 8) | (buffer[off + 5] << 16) | (buffer[off + 6] << 24)) >>
               2) &
              0x3ffffff;
        h2 += ((uint)(buffer[off + 6] | (buffer[off + 7] << 8) | (buffer[off + 8] << 16) | (buffer[off + 9] << 24)) >>
               4) &
              0x3ffffff;
        h3 += ((uint)(buffer[off + 9] |
                      (buffer[off + 10] << 8) |
                      (buffer[off + 11] << 16) |
                      (buffer[off + 12] << 24)) >>
               6) &
              0x3ffffff;
        h4 += ((uint)(buffer[off + 12] |
                      (buffer[off + 13] << 8) |
                      (buffer[off + 14] << 16) |
                      (buffer[off + 15] << 24)) >>
               8) |
              (1U << 24);

        d0 = ((ulong)h0 * polyr0) + ((ulong)h1 * s4) + ((ulong)h2 * s3) + ((ulong)h3 * s2) + ((ulong)h4 * s1);
        d1 = ((ulong)h0 * polyr1) + ((ulong)h1 * polyr0) + ((ulong)h2 * s4) + ((ulong)h3 * s3) + ((ulong)h4 * s2);
        d2 = ((ulong)h0 * polyr2) + ((ulong)h1 * polyr1) + ((ulong)h2 * polyr0) + ((ulong)h3 * s4) + ((ulong)h4 * s3);
        d3 = ((ulong)h0 * polyr3) +
             ((ulong)h1 * polyr2) +
             ((ulong)h2 * polyr1) +
             ((ulong)h3 * polyr0) +
             ((ulong)h4 * s4);
        d4 = ((ulong)h0 * polyr4) +
             ((ulong)h1 * polyr3) +
             ((ulong)h2 * polyr2) +
             ((ulong)h3 * polyr1) +
             ((ulong)h4 * polyr0);

        c = (uint)(d0 >> 26);
        h0 = (uint)d0 & 0x3ffffff;
        d1 += c;
        c = (uint)(d1 >> 26);
        h1 = (uint)d1 & 0x3ffffff;
        d2 += c;
        c = (uint)(d2 >> 26);
        h2 = (uint)d2 & 0x3ffffff;
        d3 += c;
        c = (uint)(d3 >> 26);
        h3 = (uint)d3 & 0x3ffffff;
        d4 += c;
        c = (uint)(d4 >> 26);
        h4 = (uint)d4 & 0x3ffffff;
        h0 += c * 5;
        c = h0 >> 26;
        h0 &= 0x3ffffff;
        h1 += c;

        off += BLOCK_SIZE;
      }
    }
  }
}
