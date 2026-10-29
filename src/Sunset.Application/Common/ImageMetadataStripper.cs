using System.Buffers.Binary;

namespace Sunset.Application.Common;

/// <summary>
/// Removes privacy-relevant metadata (EXIF - notably GPS coordinates -, XMP, IPTC, comments,
/// timestamps) from JPEG and PNG files <b>without re-encoding</b>: it only walks the container
/// structure and copies the segments/chunks worth keeping, so pixels and quality are untouched.
/// </summary>
/// <remarks>
/// The one EXIF value that is deliberately kept is the JPEG orientation flag, rewritten into a
/// minimal EXIF segment that carries nothing else: phones store portrait shots rotated with this
/// flag, and dropping it would make those photos show up sideways. Everything after the end of the
/// image (trailing data) is dropped too. Input that is not a well-formed JPEG/PNG yields null.
/// </remarks>
public static class ImageMetadataStripper
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly string[] PngMetadataChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    public static byte[]? TryStrip(byte[] data, out string? contentType)
    {
        contentType = null;

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            var jpeg = StripJpeg(data);
            if (jpeg is not null)
                contentType = "image/jpeg";
            return jpeg;
        }

        if (data.Length >= PngSignature.Length && data.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            var png = StripPng(data);
            if (png is not null)
                contentType = "image/png";
            return png;
        }

        return null;
    }

    // ---------------------------------------------------------------- JPEG

    private static byte[]? StripJpeg(byte[] d)
    {
        var body = new MemoryStream(d.Length);
        var orientation = 1;
        var exifInsertOffset = 0;
        var sawFrame = false;
        var pos = 2;

        while (true)
        {
            // Fill bytes (extra 0xFF) are allowed before a marker.
            if (pos >= d.Length || d[pos] != 0xFF)
                return null;
            while (pos + 1 < d.Length && d[pos + 1] == 0xFF)
                pos++;
            if (pos + 1 >= d.Length)
                return null;

            var marker = d[pos + 1];

            if (marker == 0xD9) // EOI: end of image, anything after it is dropped
            {
                if (!sawFrame)
                    return null;
                body.WriteByte(0xFF);
                body.WriteByte(0xD9);
                break;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) // standalone markers, no length
            {
                body.Write(d, pos, 2);
                pos += 2;
                continue;
            }

            if (pos + 3 >= d.Length)
                return null;
            var length = (d[pos + 2] << 8) | d[pos + 3];
            var segmentEnd = pos + 2 + length;
            if (length < 2 || segmentEnd > d.Length)
                return null;

            var payloadStart = pos + 4;
            var payloadLength = length - 2;

            if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
                sawFrame = true;

            if (marker == 0xE1)
            {
                if (orientation == 1)
                    orientation = ReadExifOrientation(d, payloadStart, payloadLength);
                // segment dropped (EXIF / XMP)
            }
            else if (!ShouldDropJpegSegment(marker, d, payloadStart, payloadLength))
            {
                var isFirstSegment = body.Length == 0;
                body.Write(d, pos, segmentEnd - pos);
                if (isFirstSegment && marker == 0xE0)
                    exifInsertOffset = (int)body.Length;
            }

            pos = segmentEnd;

            if (marker == 0xDA) // SOS: entropy-coded scan data follows up to the next real marker
            {
                var scanStart = pos;
                while (pos + 1 < d.Length)
                {
                    if (d[pos] == 0xFF)
                    {
                        var next = d[pos + 1];
                        if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                        {
                            pos += 2;
                            continue;
                        }
                        if (next == 0xFF)
                        {
                            pos++;
                            continue;
                        }
                        break;
                    }
                    pos++;
                }

                if (pos + 1 >= d.Length)
                    return null;
                body.Write(d, scanStart, pos - scanStart);
            }
        }

        if (!sawFrame)
            return null;

        var bodyBytes = body.ToArray();
        using var output = new MemoryStream(bodyBytes.Length + 40);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);

        if (orientation is > 1 and <= 8)
        {
            output.Write(bodyBytes, 0, exifInsertOffset);
            output.Write(BuildOrientationOnlyExif(orientation));
            output.Write(bodyBytes, exifInsertOffset, bodyBytes.Length - exifInsertOffset);
        }
        else
        {
            output.Write(bodyBytes);
        }

        return output.ToArray();
    }

    private static bool ShouldDropJpegSegment(byte marker, byte[] d, int payloadStart, int payloadLength)
    {
        switch (marker)
        {
            case 0xE0: // keep JFIF, drop JFXX (extension thumbnail) and anything else on APP0
                return !HasAsciiPrefix(d, payloadStart, payloadLength, "JFIF\0");
            case 0xE2: // keep the ICC colour profile (affects how colours render), drop other APP2 data
                return !HasAsciiPrefix(d, payloadStart, payloadLength, "ICC_PROFILE\0");
            case 0xEE: // Adobe APP14 carries the colour transform needed to decode CMYK/YCCK
                return false;
            case 0xFE: // comment
                return true;
            default:
                return marker is >= 0xE1 and <= 0xEF; // APP1 (handled), APP3..APP13, APP15: EXIF/XMP/IPTC/...
        }
    }

    private static bool HasAsciiPrefix(byte[] d, int start, int available, string prefix)
    {
        if (available < prefix.Length)
            return false;
        for (var i = 0; i < prefix.Length; i++)
            if (d[start + i] != prefix[i])
                return false;
        return true;
    }

    // Reads only the Orientation tag (0x0112) of IFD0 from an EXIF APP1 payload; 1 when absent/unreadable.
    private static int ReadExifOrientation(byte[] d, int start, int length)
    {
        if (!HasAsciiPrefix(d, start, length, "Exif\0\0"))
            return 1;

        var tiff = start + 6;
        var end = start + length;
        if (tiff + 8 > end)
            return 1;

        bool littleEndian;
        if (d[tiff] == 'I' && d[tiff + 1] == 'I')
            littleEndian = true;
        else if (d[tiff] == 'M' && d[tiff + 1] == 'M')
            littleEndian = false;
        else
            return 1;

        ushort U16(int at) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at, 2));
        uint U32(int at) => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at, 4));

        if (U16(tiff + 2) != 0x002A)
            return 1;

        var ifd = (long)tiff + U32(tiff + 4);
        if (ifd + 2 > end)
            return 1;

        var entries = U16((int)ifd);
        for (var i = 0; i < entries; i++)
        {
            var entry = ifd + 2 + (long)i * 12;
            if (entry + 12 > end)
                return 1;

            if (U16((int)entry) == 0x0112)
            {
                var type = U16((int)entry + 2);
                var value = U16((int)entry + 8);
                return type == 3 && value is >= 1 and <= 8 ? value : 1;
            }
        }

        return 1;
    }

    // APP1/Exif with a single IFD0 entry: Orientation. Big-endian, 34 bytes including the marker.
    private static byte[] BuildOrientationOnlyExif(int orientation) =>
    [
        0xFF, 0xE1, 0x00, 0x22,
        (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
        (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
        0x00, 0x01,
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ];

    // ---------------------------------------------------------------- PNG

    private static byte[]? StripPng(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(PngSignature);

        var pos = PngSignature.Length;
        var first = true;
        var sawData = false;

        while (pos + 12 <= d.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos, 4));
            if (length > int.MaxValue - 12 || pos + 12L + length > d.Length)
                return null;

            var total = 12 + (int)length;
            var type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);

            if (first && type != "IHDR")
                return null;
            first = false;

            if (type == "IDAT")
                sawData = true;

            if (!PngMetadataChunks.Contains(type))
                output.Write(d, pos, total);

            pos += total;

            if (type == "IEND") // anything after IEND is dropped
                return sawData ? output.ToArray() : null;
        }

        return null; // no IEND: truncated
    }
}
