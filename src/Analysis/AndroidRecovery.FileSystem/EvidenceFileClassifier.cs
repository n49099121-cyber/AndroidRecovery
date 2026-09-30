using System.Text;
using AndroidRecovery.Models;

namespace AndroidRecovery.FileSystem;

public static class EvidenceFileClassifier
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] SqliteSignature = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private static readonly byte[] PdfSignature = Encoding.ASCII.GetBytes("%PDF-");
    private static readonly byte[] MatroskaSignature = [0x1A, 0x45, 0xDF, 0xA3];
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] GzipSignature = [0x1F, 0x8B];
    private static readonly byte[] Rar4Signature = Encoding.ASCII.GetBytes("Rar!\x1A\x07\x00");
    private static readonly byte[] Rar5Signature = Encoding.ASCII.GetBytes("Rar!\x1A\x07\x01\x00");

    public static EvidenceFileClassification Classify(string fileName, ReadOnlySpan<byte> header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var extension = Path.GetExtension(fileName);

        if (header.StartsWith(PngSignature)) return Known(fileName, EvidenceFileType.Image, "PNG");
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return Known(fileName, EvidenceFileType.Image, "JPEG");
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
            return Known(fileName, EvidenceFileType.Image, "GIF");
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
            return Known(fileName, EvidenceFileType.Image, "WebP");
        if (header.Length >= 2 && header[..2].SequenceEqual("BM"u8))
            return Known(fileName, EvidenceFileType.Image, "BMP");

        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(header.Slice(8, 4));
            if (brand.StartsWith("hei", StringComparison.OrdinalIgnoreCase)
                || brand is "avif" or "avis" or "mif1" or "msf1")
                return Known(fileName, EvidenceFileType.Image, brand.ToUpperInvariant());
            return Known(fileName, EvidenceFileType.Video, "ISO Base Media");
        }

        if (header.StartsWith(SqliteSignature)) return Known(fileName, EvidenceFileType.Database, "SQLite");
        if (header.StartsWith(PdfSignature)) return Known(fileName, EvidenceFileType.Document, "PDF");
        if (header.StartsWith(MatroskaSignature))
            return Known(fileName, EvidenceFileType.Video, "Matroska/WebM");
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8))
        {
            if (header.Slice(8, 4).SequenceEqual("WAVE"u8)) return Known(fileName, EvidenceFileType.Audio, "WAVE");
            if (header.Slice(8, 4).SequenceEqual("AVI "u8)) return Known(fileName, EvidenceFileType.Video, "AVI");
        }

        if (header.StartsWith("fLaC"u8)) return Known(fileName, EvidenceFileType.Audio, "FLAC");
        if (header.StartsWith("OggS"u8)) return Known(fileName, EvidenceFileType.Audio, "Ogg");
        if (header.StartsWith("ID3"u8)) return Known(fileName, EvidenceFileType.Audio, "MP3");
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return Known(fileName, EvidenceFileType.Audio, "MPEG audio");

        if (header.Length >= 4 && header[..4].SequenceEqual("PK\x03\x04"u8))
        {
            var type = extension.ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx"
                ? EvidenceFileType.Document
                : EvidenceFileType.Archive;
            return Known(fileName, type, type == EvidenceFileType.Document ? "Office Open XML" : "ZIP");
        }

        if (header.StartsWith(SevenZipSignature)) return Known(fileName, EvidenceFileType.Archive, "7-Zip");
        if (header.StartsWith(GzipSignature)) return Known(fileName, EvidenceFileType.Archive, "GZIP");
        if (header.StartsWith(Rar4Signature) || header.StartsWith(Rar5Signature))
            return Known(fileName, EvidenceFileType.Archive, "RAR");

        if (LooksLikeUtf8Text(header))
            return new(EvidenceFileType.Text, "UTF-8 text", AnalysisConfidence.Medium);

        return new(EvidenceFileType.Unknown, "Unrecognized signature", AnalysisConfidence.Unknown);
    }

    private static EvidenceFileClassification Known(string fileName, EvidenceFileType type, string format)
    {
        var extension = format.ToUpperInvariant() switch
        {
            "JPEG" => ".jpg",
            "PNG" => ".png",
            "GIF" => ".gif",
            "WEBP" => ".webp",
            "BMP" => ".bmp",
            "PDF" => ".pdf",
            "SQLITE" => ".db",
            "MP3" => ".mp3",
            "FLAC" => ".flac",
            "WAVE" => ".wav",
            "ZIP" or "OFFICE OPEN XML" => null,
            _ => null
        };
        var extensionMismatch = extension is not null
            && !string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase)
            && !(extension == ".jpg" && Path.GetExtension(fileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
        var reason = extensionMismatch
            ? $"The {format} content signature does not match the {Path.GetExtension(fileName)} extension."
            : $"The file has a recognized {format} content signature.";
        return new(type, format, AnalysisConfidence.High)
        {
            ExtensionMismatch = extensionMismatch,
            ConfidenceReason = reason
        };
    }

    private static bool LooksLikeUtf8Text(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return false;
        }

        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.All(character => !char.IsControl(character) || character is '\t' or '\r' or '\n' or '\f');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}