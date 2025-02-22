// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Tests.Linux {
    export declare const qualifier : 
        {configuration: "debug" | "release", targetFramework: "net6.0", targetRuntime: "linux-x64"};

    const sharedBinFolderName = a`shared_bin`;

    const tests = createAllDefs();

    function createAllDefs() : TestDeploymentDefinition[] {
        return [
            // Utilities
            createDef(importFrom("BuildXL.Utilities.Instrumentation.UnitTests").Core.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Collections.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Configuration.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Ipc.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").KeyValueStoreTests.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.Untracked.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").ToolSupport.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Core.dll, true),

            // // Cache
            // // DistributedTest
            // createDef(importFrom("BuildXL.Cache.ContentStore").DistributedTest.dll, true,
            //     /* deploySeparately */ false,
            //     /* testClasses */ undefined,
            //     /* categories */ [],
            //     /* noCategories */ ["LongRunningTest", "Simulation", "Performance"]
            // ),

            createDef(importFrom("BuildXL.Cache.ContentStore").Test.dll, true),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").Test.dll, true),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").InterfacesTest.dll, true),
            createDef(importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll, true),

            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Analyzer.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").BasicFilesystem.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").InputListFilter.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Interfaces.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").MemoizationStoreAdapter.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").VerticalAggregator.dll, true),

            // Pips
            createDef(importFrom("BuildXL.Pips.UnitTests").Core.dll, true),

            // Engine
            createDef(importFrom("BuildXL.Core.UnitTests").Cache.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Cache.Plugin.Core.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Processes.test_BuildXL_Processes_dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").ExternalToolTest.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Scheduler.IntegrationTest.dll, true,
                /* deploySeparately */ false,
                /* testClasses */ undefined,
                /* categories */ [
                    "AllowedUndeclaredReadsTests",
                    "BaselineTests",
                    "FileAccessPolicyTests",
                    "LazyMaterializationTests",
                    "NonStandardOptionsTests",
                    "PreserveOutputsTests",
                    "PreserveOutputsReuseOutputsTests",
                    "ReparsePointTests",
                    "OpaqueDirectoryTests",
                    "SharedOpaqueDirectoryTests",
                    "StoreNoOutputsToCacheTests",
                    "AllowlistTests"
                ]),

            // createDef(importFrom("BuildXL.Core.UnitTests").Engine.dll, true,
            //     /* deploySeparately */ false,
            //     /* testClasses */ undefined,
            //     /* categories */ [ "ValuePipTests", "DirectoryArtifactIncrementalBuildTests" ]
            // ),
            // createDef(importFrom("BuildXL.Core.UnitTests").Test.BuildXL.FingerprintStore.dll, true),
            // createDef(importFrom("BuildXL.Core.UnitTests").Ide.Generator.dll, true),
            // 
            // createDef(importFrom("BuildXL.Core.UnitTests").Scheduler.dll, true,
            //     /* deploySeparately */ false,
            //     /* testClasses */ undefined,
            //     /* categories */ importFrom("BuildXL.Core.UnitTests").Scheduler.categoriesToRunInParallel
            // ),
            

            // // App
            // createDef(importFrom("BuildXL.App").UnitTests.Bxl.dll, true),

            // // Frontend
            // createDef(importFrom("BuildXL.FrontEnd.SdkTesting").TestGenerator.dll, true),
            // createDef(importFrom("BuildXL.FrontEnd").TypeScript.Net.UnitTests.testDll, true, /* deploySeparately */ true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Core.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Download.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Nuget.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Ambients.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.dll, true,
            //     /* deploySeparately */ false,
            //     /* testClasses */ undefined,
            //     /* categories */ importFrom("BuildXL.FrontEndUnitTests").Script.categoriesToRunInParallel
            // ),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Debugger.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.ErrorHandling.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Interpretation.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.PrettyPrinter.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Script.V2Tests.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").Workspaces.dll, true),
            // createDef(importFrom("BuildXL.FrontEndUnitTests").FrontEnd.Sdk.dll, true),

            // // Ide
            // createDef(importFrom("BuildXL.Ide").LanguageService.Server.test, true),

            // // Tools
            // createDef(importFrom("BuildXL.Tools.UnitTests").Test.Tool.Analyzers.dll, true),
            // createDef(importFrom("BuildXL.Tools.UnitTests").Test.BxlScriptAnalyzer.dll, true),
            // createDef(importFrom("BuildXL.Tools.UnitTests").Test.Tool.SandboxExec.dll, true),
        ];
    }

    interface TestDeploymentDefinition extends Deployment.NestedDefinition {
        assembly: File;
        subfolder: PathAtom;
        enabled: boolean;
        testClasses: string[];
        categoriesToRunInParallel: string[];
        categoriesToNeverRun: string[];
        runSuppliedCategoriesOnly: boolean;
    }

    function createDef(testResult: BuildXLSdk.TestResult, enabled: boolean, deploySeparately?: boolean,
            testClasses?: string[], categories?: string[], noCategories?: string[], runSuppliedCategoriesOnly?: boolean) : TestDeploymentDefinition {
        let assembly = testResult.testDeployment.primaryFile;
        return <TestDeploymentDefinition>{
            subfolder: deploySeparately
                ? assembly.nameWithoutExtension // deploying separately because some files clash with shared_bin deployment
                : sharedBinFolderName,          // deploying into shared_bin (together with 'coreDrop' BuildXL binaries) to save space for test deployment
            contents: [
                testResult.testDeployment.deployedDefinition
            ],

            assembly: assembly,
            testAssemblies: [],
            enabled: enabled,
            testClasses: testClasses,
            categoriesToRunInParallel: categories,
            categoriesToNeverRun: noCategories,
            runSuppliedCategoriesOnly: runSuppliedCategoriesOnly || false
        };
    }

    function genXUnitExtraArgs(definition: TestDeploymentDefinition): string {
        return [
            ...(definition.testClasses || []).map(testClass => `-class ${testClass}`),
            ...(definition.categoriesToNeverRun || []).map(cat => `-notrait "Category=${cat}"`)
        ].join(" ");
    }

    function quoteString(str: string): string {
        return '"' + str.replace('"', '\\"') + '"';
    }

    function generateStringArrayProperty(propertyName: string, array: string[], indent: string): string[] {
        return generateArrayProperty(propertyName, (array || []).map(quoteString), indent);
    }

    function generateArrayProperty(propertyName: string, literals: string[], indent: string): string[] {
        if ((literals || []).length === 0) {
            return [];
        }

        return [
            `${indent}${propertyName}: [`,
            ...literals.map(str => `${indent}${indent}${str},`),
            `${indent}]`
        ];
    }

    function generateDsVarForTest(def: TestDeploymentDefinition): string {
        const varName = def.assembly.name.toString().replace(".", "_");
        const indent = "    ";
        return [
            `@@public export const xunit_${varName} = runXunit({`,
            `    testAssembly: f\`${def.subfolder}/${def.assembly.name}\`,`,
            `    runSuppliedCategoriesOnly: ${def.runSuppliedCategoriesOnly},`,
            ...generateStringArrayProperty("classes", def.testClasses, indent),
            ...generateStringArrayProperty("categories", def.categoriesToRunInParallel, indent),
            ...generateStringArrayProperty("noCategories", def.categoriesToNeverRun, indent),
            `});`,
            ``
        ].join("\n");
    }

    function createSpecFile(definitions: TestDeploymentDefinition[]): string {
        return tests
            .filter(def => def.enabled)
            .map(generateDsVarForTest)
            .join("\n");
    }

    function getRunXunitCommands(def: TestDeploymentDefinition): string[] {
        const base: string = `run_xunit "\${MY_DIR}/TestProj/tests/${def.subfolder}"${' '}${def.assembly.name}${' '}${genXUnitExtraArgs(def)}`;
        const traits: string[] = (def.categoriesToRunInParallel || [])
            .map(cat => `${base} -trait "Category=${cat}"`);
        const rest: string = [
            base,
            ...(def.categoriesToRunInParallel || []).map(cat => `-notrait "Category=${cat}"`)
        ].join(" ");
        return def.runSuppliedCategoriesOnly
            ? traits
            : [...traits, rest];
    }

    function createUnixTestRunnerScript(definitions: TestDeploymentDefinition[]): string {
        const runTestCommands = tests
            .filter(def => def.enabled)
            .mapMany(getRunXunitCommands);

        return [
            "#!/bin/bash",
            "",
            "MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)",
            "source $MY_DIR/xunit_runner.sh",
            "",
            "find . \\( -name SandboxedProcessExecutor -o -name Test.BuildXL.Executables.InfiniteWaiter -o -name Test.BuildXL.Executables.TestProcess \\) -print0 | xargs -0 chmod +x",
            "",
            "numTestFailures=0",
            "trap \"((numTestFailures++))\" ERR",
            "",
            ...runTestCommands,
            "",
            "exit $numTestFailures"
        ].join("\n");
    }

    function renderFileLiteral(file: string): string {
        if (file === undefined) return "undefined";
        return "f`" + file + "`";
    }

    function createModuleConfig(projectFiles?: string[]): string {
        return [
            'module({',
            '    name: "BuildXLXUnitTests",',
            ...generateArrayProperty("projects", (projectFiles || []).map(renderFileLiteral), "    "),
            '});'
        ].join("\n");
    }

    function writeFile(fileName: PathAtom, content: string): DerivedFile {
        return Transformer.writeAllText({
            outputPath: p`${Context.getNewOutputDirectory("standalone-tests")}/${fileName}`,
            text: content
        });
    }

    /*
        Folder layout:

            ├── [TestProj]
            │   └── [tests]
            │       ├── [shared_bin]
            |       │   └── ... (BuildXL core drop + most of the test deployments)
            │       └── [Test.TypeScript.Net]
            |           └── ... (only Test.TypeScript.Net is deployed separately because of some file clashes)
            ├── bash_runner.sh
            ├── env.sh
            └── xunit_runner.sh
    */
    export const deployment : Deployment.Definition = {
        contents: [
            f`xunit_runner.sh`,
            writeFile(a`bash_runner.sh`, createUnixTestRunnerScript(tests)),
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/env.sh`,
            {
                subfolder: a`TestProj`,
                contents: [
                    {
                        subfolder: r`tests`,
                        contents: [
                            // deploying BuildXL core drop (needed to execute tests)
                            {
                                subfolder: sharedBinFolderName,
                                contents: [ BuildXL.deployment ]
                            },
                            // some of these tests may also get deployed into `sharedBinFolderName` folder to save space
                            ...tests,
                        ]
                    }
                ]
            }
        ]};
}
