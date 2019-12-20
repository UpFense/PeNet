﻿using System;
using PeNet.Structures;

namespace PeNet.Parser
{
    internal class ImageResourceDirectoryParser : SafeParser<IMAGE_RESOURCE_DIRECTORY>
    {
        internal ImageResourceDirectoryParser(byte[] buff, uint offset)
            : base(buff, offset)
        {
        }

        protected override IMAGE_RESOURCE_DIRECTORY ParseTarget()
        {
            if (_offset == 0)
                return null;

            // Parse the root directory.
            var root = new IMAGE_RESOURCE_DIRECTORY(_buff, _offset, _offset);

            // Parse the second stage (type)
            foreach (var de in root.DirectoryEntries)
            {

                de.ResourceDirectory = new IMAGE_RESOURCE_DIRECTORY(
                    _buff,
                    _offset + de.OffsetToDirectory,
                    _offset
                );

                var sndLevel = de?.ResourceDirectory?.DirectoryEntries;
                if(sndLevel is null)
                    continue;

                // Parse the third stage (name/IDs)
                foreach (var de2 in sndLevel)
                {
                    de2.ResourceDirectory = new IMAGE_RESOURCE_DIRECTORY(
                        _buff,
                        _offset + de2.OffsetToDirectory,
                        _offset
                        );

                    var thrdLevel = de2?.ResourceDirectory?.DirectoryEntries;
                    if(thrdLevel is null)
                        continue;

                    // Parse the forth stage (language) with the data.
                    foreach (var de3 in thrdLevel)
                    {
                        de3.ResourceDataEntry = new IMAGE_RESOURCE_DATA_ENTRY(_buff,
                            _offset + de3.OffsetToData);
                    }
                }
            }

            return root;
        }
    }
}