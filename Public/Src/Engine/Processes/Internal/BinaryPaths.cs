// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Reflection;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Internal
{
    internal sealed class BinaryPaths
    {
        public readonly string DllNameX64;
        public readonly string DllNameX86;
        public readonly string DllDirectoryX64;
        public readonly string DllDirectoryX86;

        public BinaryPaths()
        {
            string directory = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(BinaryPaths).GetTypeInfo().Assembly)) ?? string.Empty;

            DllNameX86 = Path.Combine(directory, Native.Processes.Windows.ExternDll.DetoursServices32);
            DllNameX64 = Path.Combine(directory, Native.Processes.Windows.ExternDll.DetoursServices64);

            VerifyFileExists(DllNameX86);
            VerifyFileExists(DllNameX64);

            DllDirectoryX86 = Path.GetDirectoryName(DllNameX86) ?? string.Empty;
            DllDirectoryX64 = Path.GetDirectoryName(DllNameX64) ?? string.Empty;
        }

        private static void VerifyFileExists(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new BuildXLException($"Cannot find file '{fileName}' needed to detour processes. Did you build all configurations?", rootCause: ExceptionRootCause.MissingRuntimeDependency);
            }
        }
    }
}
