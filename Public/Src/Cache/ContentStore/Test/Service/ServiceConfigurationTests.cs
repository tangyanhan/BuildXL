// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using BuildXL.Utilities;
using FluentAssertions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using PathGeneratorUtilities = BuildXL.Cache.ContentStore.InterfacesTest.Utils.PathGeneratorUtilities;

namespace ContentStoreTest.Service
{
    public class ServiceConfigurationTests
    {
        private static readonly string DriveLetter = "C";

        // JSON will escape forward slashes, leading to weird paths: "\\/path1"
        private static readonly string FilePrefixJson = OperatingSystemHelper.IsUnixOS ? "\\/" : @"C:\\";

        private static readonly string ValidDataRoot = "data";
        private static readonly string Path1 = "path1";
        private static readonly string Path2 = "path2";

        private static readonly string GoodJson =
            $@"{{""BufferSizeForGrpcCopies"":1000,""DataRootPath"":""{FilePrefixJson}{ValidDataRoot}"",""EncryptedGrpcPort"":780,""GracefulShutdownSeconds"":44,""GrpcPort"":779,""GrpcPortFileName"":""MyTest"",""NamedCacheRoots"":{{""name1"":""{FilePrefixJson}{Path1}"",""name2"":""{FilePrefixJson}{Path2}""}}}}";

        private const uint GracefulShutdownSeconds = 44;
        private const int GrpcPort = 779;
        private const int EncryptedGrpcPort = 780;
        private const string GrpcPortFileName = "MyTest";
        private readonly int? _bufferSizeForCopies = 1000;
        private readonly int? _grpcBarrierSizeForGrpcCopies = 65536;

        private static readonly AbsolutePath ValidDataRootPath = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath(DriveLetter, ValidDataRoot));

        private static readonly Dictionary<string, AbsolutePath> NamedRoots = new Dictionary<string, AbsolutePath>
        {
            {"name1", new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath(DriveLetter, Path1))},
            {"name2", new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath(DriveLetter, Path2))}
        };

        [Fact]
        public void ToJson()
        {
            NamedRoots["name1"].Path.Should().Be(OperatingSystemHelper.IsUnixOS ? "/path1" : @"C:\path1");
            NamedRoots["name2"].Path.Should().Be(OperatingSystemHelper.IsUnixOS ? "/path2" : @"C:\path2");

            var configuration = new ServiceConfiguration(
                NamedRoots, ValidDataRootPath, GracefulShutdownSeconds, GrpcPort, GrpcPortFileName, _bufferSizeForCopies, _grpcBarrierSizeForGrpcCopies);
            configuration.EncryptedGrpcPort = EncryptedGrpcPort;

            var json = configuration.SerializeToJSON();
            json.Should().Be(GoodJson);
        }

        [Fact]
        public void FromJson()
        {
            using (var stream = GoodJson.ToUTF8Stream())
            {
                var configuration = stream.DeserializeFromJSON<ServiceConfiguration>();
                configuration.NamedCacheRoots.Should().BeEquivalentTo(NamedRoots);
                configuration.GracefulShutdownSeconds.Should().Be(GracefulShutdownSeconds);
                configuration.GrpcPortFileName.Should().Be(GrpcPortFileName);
                configuration.BufferSizeForGrpcCopies.Should().Be(_bufferSizeForCopies);
            }
        }

        [Fact]
        public void FromJsonEmpty()
        {
            using (var stream = @"{}".ToUTF8Stream())
            {
                var configuration = stream.DeserializeFromJSON<ServiceConfiguration>();
                configuration.NamedCacheRoots.Count.Should().Be(0);
                configuration.GracefulShutdownSeconds.Should().Be(ServiceConfiguration.DefaultGracefulShutdownSeconds);
                configuration.GrpcPortFileName.Should().Be(null);
            }
        }

        [Fact]
        public void FromJsonInvalidNamedCacheRootPath()
        {
            var json = GoodJson.Replace(FilePrefixJson + Path1, "nonabsolute");
            using (var stream = json.ToUTF8Stream())
            {
                var configuration = stream.DeserializeFromJSON<ServiceConfiguration>();
                configuration.IsValid.Should().BeFalse();
                configuration.Error.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void FromJsonInvalidDataRootPath()
        {
            var json = GoodJson.Replace(FilePrefixJson + ValidDataRoot, "nonabsolute");
            using (var stream = json.ToUTF8Stream())
            {
                var configuration = stream.DeserializeFromJSON<ServiceConfiguration>();
                configuration.IsValid.Should().BeFalse();
                configuration.Error.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void Roundtrip()
        {
            var configuration = new ServiceConfiguration(NamedRoots, ValidDataRootPath, GracefulShutdownSeconds, GrpcPort, GrpcPortFileName);
            configuration.EncryptedGrpcPort = EncryptedGrpcPort;
            using (var ms = new MemoryStream())
            {
                configuration.SerializeToJSON(ms);
                ms.Position = 0;
                var configuration2 = ms.DeserializeFromJSON<ServiceConfiguration>();

                configuration2.NamedCacheRoots.Should().BeEquivalentTo(NamedRoots);
            }
        }
    }
}
