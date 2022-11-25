using System;
using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Resource
{
    /// <summary>
    ///     Information about Icons.
    /// </summary>
    public class Icon : AbstractStructure
    {
        public uint Size { get; }
        public uint Id { get; }
        private Resources Parent { get; }
        private GroupIconDirectoryEntry? AssociatedGroupIconDirectoryEntry { get; }

        private const uint IcoHeaderSize = 6;
        private const uint IcoDirectorySize = 16;
        private static readonly byte[] PNGHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        ///     Creates a new Icon instance and sets Size and ID.
        /// </summary>
        /// <param name="peFile">A PE file.</param>
        /// <param name="offset">Offset of the Icon image in the PE file.</param>
        /// <param name="size">Size of the Icon image in the PE file.</param>
        /// <param name="id">ID of the Icon.</param>
        /// <param name="parent">Resources parent of the Icon</param>
        public Icon(IRawFile peFile, long offset, uint size, uint id, Resources parent)
            : base(peFile, offset)
        {
            Size = size;
            Id = id;
            Parent = parent;
            AssociatedGroupIconDirectoryEntry = GetAssociatedGroupIconDirectoryEntry();
        }

        /// <summary>
        ///     Byte span of the icon image.
        /// </summary>
        public Span<byte> AsRawSpan()
        {
            return PeFile.AsSpan(Offset, Size);
        }

        private bool IsPng => AsRawSpan().Slice(0, 8).SequenceEqual(PNGHeader);
        private bool IsIco => !IsPng && AssociatedGroupIconDirectoryEntry is not null && !AsRawSpan().IsEmpty;

        /// <summary>
        ///     Adding .ICO-Header to the bytes of the icon image.
        ///     Reference: https://docs.fileformat.com/image/ico/
        /// </summary>
        /// <returns>Bytes of the icon image as .ICO file.</returns>
        public byte[]? AsIco()
        {
            var raw = AsRawSpan();
            if (IsPng) return raw.ToArray(); // TODO: CFF does not use an additional header for .PNG. But this would not break anything.
            if (!IsIco) return null;

            var header = GenerateIcoHeader();
            var directory = GenerateIcoDirectory();
            var icoBytes = new byte[header.Length + directory.Length + raw.Length];

            icoBytes.WriteBytes(0, header);
            icoBytes.WriteBytes(header.Length, directory);
            icoBytes.WriteBytes(header.Length + directory.Length, raw);
            return icoBytes;
        }

        private static byte[] GenerateIcoHeader()
        {
            var header = new byte[IcoHeaderSize];
            header.WriteBytes(0, ((ushort) 0).LittleEndianBytes().AsSpan());
            header.WriteBytes(2, ((ushort) 1).LittleEndianBytes().AsSpan());
            header.WriteBytes(4, ((ushort) 1).LittleEndianBytes().AsSpan());
            return header;
        }

        private byte[] GenerateIcoDirectory()
        {
            var directory = new byte[IcoDirectorySize];
            directory[0] = AssociatedGroupIconDirectoryEntry!.BWidth; //Width
            directory[1] = AssociatedGroupIconDirectoryEntry!.BHeight; //Height

            //Information not included in the GroupIconDirectoryEntry, only in the image byte array for .BMP. By default 0x00.
            directory[2] = AsRawSpan()[32]; //Number of Colors in color palette
            directory[3] = 0x00; //Res

            directory.WriteBytes(4, AssociatedGroupIconDirectoryEntry!.WPlanes.LittleEndianBytes());
            directory.WriteBytes(6, AssociatedGroupIconDirectoryEntry!.WBitCount.LittleEndianBytes());

            directory.WriteBytes(8, AssociatedGroupIconDirectoryEntry!.DwBytesInRes.LittleEndianBytes());

            directory.WriteBytes(12, (IcoHeaderSize + IcoDirectorySize).LittleEndianBytes());
            return directory;
        }

        private GroupIconDirectoryEntry? GetAssociatedGroupIconDirectoryEntry()
        {
            return Parent.GroupIconDirectories?
                .SelectMany(groupIconDirectory => groupIconDirectory.DirectoryEntries.OrEmpty())
                .FirstOrDefault(groupIconDirectoryEntry => groupIconDirectoryEntry.NId == Id);
        }
    }
}
