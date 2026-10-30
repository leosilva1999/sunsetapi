using System.Buffers.Binary;

namespace Sunset.Application.Common;

/// <summary>Pixel size of an image as a viewer shows it (EXIF rotation already applied).</summary>
public sealed record ImageDimensions(int Width, int Height);

/// <summary>
/// Reads the width and height from a JPEG/PNG header, without decoding the pixels. For a JPEG the
/// orientation flag is honoured: phones store portrait shots rotated 90 degrees with that flag, and
/// browsers display them upright, so the displayed width and height are swapped for orientations 5-8.
/// Meant for bytes that <see cref="ImageMetadataStripper"/> already accepted.
/// </summary>
public static class ImageDimensionReader
{
    public static ImageDimensions? TryRead(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            return ReadJpeg(data);

        // 8-byte signature, then the IHDR chunk: length(4) "IHDR"(4) width(4) height(4)
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[12] == 'I' && data[13] == 'H' && data[14] == 'D' && data[15] == 'R')
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
            return Valid(width, height);
        }

        return null;
    }

    private static ImageDimensions? ReadJpeg(byte[] d)
    {
        var orientation = 1;
        uint width = 0, height = 0;
        var pos = 2;

        while (pos + 3 < d.Length)
        {
            if (d[pos] != 0xFF)
                return null;
            var marker = d[pos + 1];
            if (marker == 0xFF) { pos++; continue; }                                     // fill byte
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) { pos += 2; continue; } // no length
            if (marker is 0xD9 or 0xDA)
                break;                                                                   // end / scan: no more headers

            var length = (d[pos + 2] << 8) | d[pos + 3];
            var end = pos + 2 + length;
            if (length < 2 || end > d.Length)
                return null;

            if (marker == 0xE1 && orientation == 1)
                orientation = ImageMetadataStripper.ReadExifOrientation(d, pos + 4, length - 2);

            // Start-of-frame: precision(1) height(2) width(2)
            if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC && length >= 8)
            {
                height = (uint)((d[pos + 5] << 8) | d[pos + 6]);
                width = (uint)((d[pos + 7] << 8) | d[pos + 8]);
            }

            pos = end;
        }

        if (width == 0 || height == 0)
            return null;

        return orientation is >= 5 and <= 8 ? Valid(height, width) : Valid(width, height);
    }

    private static ImageDimensions? Valid(uint width, uint height) =>
        width is > 0 and <= 100_000 && height is > 0 and <= 100_000
            ? new ImageDimensions((int)width, (int)height)
            : null;
}
