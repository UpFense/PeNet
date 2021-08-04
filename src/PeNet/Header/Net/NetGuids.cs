﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeNet.Header.Net
{
    /// <summary>
    /// https://www.virusbulletin.com/virusbulletin/2015/06/using-net-guids-help-hunt-malware/
    ///
    /// These guids can be used to correlate related CLR PE files.
    /// e.g. using virustotal's 'netguid' search field.
    /// </summary>
    internal class NetGuids
    {
        public NetGuids(PeFile peFile)
        {
            ModuleVersionIds = ParseModuleVersionIds(peFile);
            ComTypeLibId = ParseComTypeLibId(peFile);
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/dotnet/api/system.reflection.module.moduleversionid
        /// A Module Version ID is generated at build time, resulting in a new GUID for each 
        /// unique build of a module.
        /// </summary>
        public List<Guid>? ModuleVersionIds { get; }

        /// <summary>
        /// A COM TypeLib ID is generated by Visual Studio at project creation 
        /// e.g. Properties/AssemblyInfo.cs - [assembly: Guid("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"]
        ///
        /// It can be used to correlate different builds of the same project.
        /// </summary>
        public Guid? ComTypeLibId { get; }

        private List<Guid>? ParseModuleVersionIds(PeFile peFile)
        {
            try
            {
                return peFile.MetaDataStreamTablesHeader?.Tables.Module?.Select(m =>
                        peFile.MetaDataStreamGuid?.GetGuidAtIndex(m.Mvid) ?? Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToList() ?? new List<Guid>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A COM TypeLib ID is generated by Visual Studio at project creation 
        /// e.g. Properties/AssemblyInfo.cs - [assembly: Guid("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"]
        ///
        /// It can be used to correlate different builds of the same project.
        /// </summary>
        public Guid? ParseComTypeLibId(PeFile peFile)
        {
            try
            {
                // we need to walk a *lot* of .NET metadata to find this...

                // 0. a bitfield in the #~ stream tells us the width of the index entries in the other streams
                if (peFile.MetaDataStreamTablesHeader is null)
                    throw new ArgumentException("Meta Data Stream Tables Header must not be null.",
                        nameof(peFile.MetaDataStreamTablesHeader));
                var blobIndexSize = new HeapSizes(peFile.MetaDataStreamTablesHeader.HeapSizes).Blob;

                // 1. find the index of "GuidAttribute" in the TypeRef table
                var typeRefTable = peFile.MetaDataStreamTablesHeader?.Tables.TypeRef;
                var stringsStream = peFile.MetaDataStreamString;

                var typeRefTableIndex = 1; // .NET metadata tables are 1-based...
                for (; typeRefTableIndex <= typeRefTable?.Count; typeRefTableIndex++)
                {
                    var typeRefTableRow = typeRefTable[typeRefTableIndex - 1]; // ...but .NET arrays are 0-based.
                    if ("GuidAttribute" == stringsStream?.GetStringAtIndex(typeRefTableRow.TypeName)
                        && "System.Runtime.InteropServices" ==
                        stringsStream?.GetStringAtIndex(typeRefTableRow.TypeNamespace))
                    {
                        break;
                    }
                }

                if (typeRefTableIndex <= typeRefTable?.Count)
                {
                    // we found the TypeRef for "GuidAttribute"!

                    // 2. now find the row in the MemberRef table that points to this TypeRef
                    var memberRefTable = peFile?.MetaDataStreamTablesHeader?.Tables.MemberRef;
                    var memberRefTableIndex = 1;
                    for (; memberRefTableIndex <= memberRefTable?.Count; memberRefTableIndex++)
                    {
                        var memberRefTableRow = memberRefTable[memberRefTableIndex - 1];
                        if ((memberRefTableRow?.Class & 0x7) == 0x1 // parent is a TypeRef
                            && (memberRefTableRow?.Class >> 3) == typeRefTableIndex)
                        {
                            break;
                        }
                    }

                    if (memberRefTableIndex <= memberRefTable?.Count)
                    {
                        // we found the MemberRef for the TypeRef for "GuidAttribute"!

                        // 3. now find the row in the CustomAttribute table whose Type points to this MemberRef
                        var customAttributeTable = peFile?.MetaDataStreamTablesHeader?.Tables?.CustomAttribute;
                        if (customAttributeTable is null)
                            return null;

                        foreach (var row in customAttributeTable)
                        {
                            if ((row.Type & 0x7) == 0x3 // parent is a MemberRef
                                && row.Type >> 3 == memberRefTableIndex)
                            {
                                // we found the CustomAttribute matching the MemberRef for the TypeRef for "GuidAttribute"!

                                // the Blob pointed at by this row's Value contains the TypeLib ID string
                                var blobIndex = row.Value;

                                var guidStart = blobIndex + blobIndexSize + 2; // +2 as stored as a counted string
                                var guidLength = 36;
                                if (guidStart + guidLength < peFile?.MetaDataStreamBlob?.Length
                                    && peFile?.MetaDataStreamBlob?[guidStart - 1] == guidLength) // check the count
                                {
                                    var guidBytes = peFile.MetaDataStreamBlob.Skip((int) guidStart).Take(guidLength)
                                        .ToArray();
                                    var s = Encoding.ASCII.GetString(guidBytes);
                                    return new Guid(s);
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}