// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as ManagedSdk from "Sdk.Managed";

namespace VfsTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.ContentStore.Vfs.Test",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
                parallelBucketCount: 8,
            },
        skipTestRun: !BuildXLSdk.isHostOsWin || BuildXLSdk.restrictTestRunToSomeQualifiers,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.Net.Primitives.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.dll,
                NetFx.System.Data.dll
            ),
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            InterfacesTest.dll,
            Library.dll,
            VfsLibrary.dll,
            Test.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
    });
}
