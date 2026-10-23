using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GhostDesk;

namespace GhostDeskRegressionTests
{
    internal static class StorageLifecycleTests
    {
        private static readonly Assembly GhostDeskAssembly =
            typeof(DesktopItem).Assembly;
        private static readonly Type ItemStorageType =
            GhostDeskAssembly.GetType("GhostDesk.ItemStorage", true);
        private static int _passed;
        private static int _failed;
        private static int _skipped;

        [STAThread]
        private static int Main()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string desktop = Path.Combine(root, "TestDesktop");
            string sources = Path.Combine(root, "TestSources");
            string data = Path.Combine(root, "TestData");
            Directory.CreateDirectory(desktop);
            Directory.CreateDirectory(sources);
            Directory.CreateDirectory(data);

            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_DESKTOP_DIR",
                desktop);
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_DISABLE_LEGACY_MIGRATION",
                "1");
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_FORCE_COPY_MOVE",
                "1");
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_SUPPRESS_DIALOGS",
                "1");
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_DATA_DIR",
                data);

            Run("file moves into Storage", delegate
            {
                TestFileMovesIntoStorage(sources);
            });
            Run("folder moves into Storage", delegate
            {
                TestFolderMovesIntoStorage(sources);
            });
            Run("same-volume move uses atomic rename", delegate
            {
                TestSameVolumeAtomicMove(sources);
            });
            Run("duplicate names are preserved", delegate
            {
                TestDuplicateNames(sources);
            });
            Run("Storage item moves back to desktop", delegate
            {
                TestMoveBackToDesktop(sources, desktop);
            });
            Run("shortcut file moves without its target", delegate
            {
                TestShortcutFile(sources);
            });
            Run("failed Storage move keeps source", delegate
            {
                TestUnavailableStorage(sources);
            });
            Run("failed config save is reported", delegate
            {
                TestConfigSaveFailureReported();
            });
            Run("failed config save rolls back Storage move", delegate
            {
                TestFailedConfigRollsBackStorageMove(sources);
            });
            Run("failed config save rolls back desktop return", delegate
            {
                TestFailedConfigRollsBackDesktopReturn(sources, desktop);
            });
            Run("moved path can be rolled back", delegate
            {
                TestMovedPathRollback(sources);
            });
            Run("cross-volume copy preserves data", delegate
            {
                TestCrossVolumeMove();
            });
            Run("source cleanup residue is reported", delegate
            {
                TestSourceCleanupWarning(sources);
            });
            Run("config load never moves real files", delegate
            {
                TestConfigLoadDoesNotMove(sources);
            });
            Run("Git metadata and hidden files survive move", delegate
            {
                TestGitMetadataAndHiddenFiles(sources);
            });
            Run("interrupted copy preserves source", delegate
            {
                TestInterruptedCopy(sources);
            });
            Run("drag-away waits for committed move", delegate
            {
                TestExternalMoveCommitCheck(sources);
            });
            Run("desktop reference leaves region after desktop drop", delegate
            {
                TestDesktopReferenceDropRemovesOnlyRegionRecord(desktop);
            });
            Run("desktop reference drag decision rejects cancellation", delegate
            {
                TestDesktopReferenceDragDecision(desktop);
            });
            Run("failed config save keeps desktop reference record", delegate
            {
                TestDesktopReferenceDropSaveFailureRollback(desktop);
            });
            Run("time picker has two numeric fields", delegate
            {
                TestTimePicker();
            });
            Run("shutdown type keeps custom values", delegate
            {
                TestScheduledTaskTypeChange();
            });
            Run("hidden attribute toggles cleanly", delegate
            {
                TestHiddenAttributeToggle(sources);
            });
            Run("storage manifest records and removes entries", delegate
            {
                TestStorageManifestLifecycle(data);
            });
            Run("locked file is not stored", delegate
            {
                TestLockedFileIsNotStored(sources);
            });
            Run("failed config save rolls back manifest entry", delegate
            {
                TestFailedConfigRollsBackManifest(sources, data);
            });
            Run("desktop item stays in place, hidden and marked", delegate
            {
                TestStoredItemMarkedHidden(desktop);
            });
            Run("external path referenced by shortcut, record removal keeps source", delegate
            {
                TestAddAndRestoreManifest(sources, data);
            });
            Run("visibility sync follows the show-hidden switch", delegate
            {
                TestVisibilitySync();
            });
            Run("public desktop paths are rejected", delegate
            {
                TestPublicDesktopCheck();
            });
            Run("bulk storage stays responsive", delegate
            {
                TestBulkStorage(sources);
            });

            Console.WriteLine();
            Console.WriteLine(
                "Regression result: " + _passed + " passed, " +
                _failed + " failed" +
                (_skipped > 0 ? ", " + _skipped + " skipped." : "."));
            return _failed == 0 ? 0 : 1;
        }

        private static void TestFileMovesIntoStorage(string sources)
        {
            string source = Path.Combine(
                sources,
                "report-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "alpha");

            object[] arguments = new object[] { source, null };
            string destination = (string)InvokeStatic(
                ItemStorageType,
                "MoveIntoStorage",
                arguments);

            AssertFalse(File.Exists(source), "source file still exists");
            AssertTrue(File.Exists(destination), "destination file is missing");
            AssertEqual("alpha", File.ReadAllText(destination), "file content");
            AssertEqual(
                Path.GetFullPath(source),
                (string)arguments[1],
                "original path");
            AssertTrue(
                (bool)InvokeStatic(
                    ItemStorageType,
                    "IsManagedPath",
                    destination),
                "destination is not managed");
        }

        private static void TestFolderMovesIntoStorage(string sources)
        {
            string source = Path.Combine(
                sources,
                "folder-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(source, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "item.txt"), "beta");

            object[] arguments = new object[] { source, null };
            string destination = (string)InvokeStatic(
                ItemStorageType,
                "MoveIntoStorage",
                arguments);

            AssertFalse(Directory.Exists(source), "source folder still exists");
            AssertTrue(Directory.Exists(destination), "destination folder is missing");
            AssertEqual(
                "beta",
                File.ReadAllText(Path.Combine(destination, "nested", "item.txt")),
                "nested file content");
        }

        private static void TestDuplicateNames(string sources)
        {
            string firstFolder = Path.Combine(
                sources,
                "first-" + Guid.NewGuid().ToString("N"));
            string secondFolder = Path.Combine(
                sources,
                "second-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(firstFolder);
            Directory.CreateDirectory(secondFolder);

            string name = "same-" + Guid.NewGuid().ToString("N") + ".txt";
            string first = Path.Combine(firstFolder, name);
            string second = Path.Combine(secondFolder, name);
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");

            string firstStored = MoveIntoStorage(first);
            string secondStored = MoveIntoStorage(second);

            AssertFalse(
                string.Equals(
                    firstStored,
                    secondStored,
                    StringComparison.OrdinalIgnoreCase),
                "duplicate move reused the same path");
            AssertEqual("first", File.ReadAllText(firstStored), "first content");
            AssertEqual("second", File.ReadAllText(secondStored), "second content");
        }

        private static void TestSameVolumeAtomicMove(string sources)
        {
            string previous = Environment.GetEnvironmentVariable(
                "GHOSTDESK_TEST_FORCE_COPY_MOVE");
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_FORCE_COPY_MOVE",
                null);
            try
            {
                string source = Path.Combine(
                    sources,
                    "atomic-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(source);
                File.WriteAllText(
                    Path.Combine(source, "same-volume.txt"),
                    "atomic");

                string stored = MoveIntoStorage(source);

                AssertFalse(
                    Directory.Exists(source),
                    "same-volume source still exists");
                AssertEqual(
                    "atomic",
                    File.ReadAllText(
                        Path.Combine(stored, "same-volume.txt")),
                    "same-volume content");
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "GHOSTDESK_TEST_FORCE_COPY_MOVE",
                    previous ?? "1");
            }
        }

        private static void TestMoveBackToDesktop(
            string sources,
            string desktop)
        {
            string name = "return-" + Guid.NewGuid().ToString("N") + ".txt";
            string source = Path.Combine(sources, name);
            File.WriteAllText(source, "stored");
            string stored = MoveIntoStorage(source);

            string occupied = Path.Combine(desktop, name);
            File.WriteAllText(occupied, "existing");

            DesktopItem item = new DesktopItem();
            item.Path = stored;
            item.Name = name;
            string destination = (string)InvokeStatic(
                ItemStorageType,
                "MoveToDesktop",
                item);

            string expected = Path.Combine(
                desktop,
                Path.GetFileNameWithoutExtension(name) + " (2)" +
                Path.GetExtension(name));
            AssertEqual(expected, destination, "desktop destination");
            AssertFalse(File.Exists(stored), "Storage copy still exists");
            AssertEqual("stored", File.ReadAllText(destination), "moved content");
            AssertEqual("existing", File.ReadAllText(occupied), "existing desktop file");
        }

        private static void TestShortcutFile(string sources)
        {
            string target = Path.Combine(
                sources,
                "shortcut-target-" + Guid.NewGuid().ToString("N") + ".txt");
            string shortcut = Path.Combine(
                sources,
                "shortcut-" + Guid.NewGuid().ToString("N") + ".lnk");
            File.WriteAllText(target, "target");
            File.WriteAllText(shortcut, "shortcut bytes");

            string stored = MoveIntoStorage(shortcut);

            AssertTrue(File.Exists(target), "shortcut target was moved");
            AssertEqual("target", File.ReadAllText(target), "shortcut target content");
            AssertFalse(File.Exists(shortcut), "shortcut source still exists");
            AssertTrue(File.Exists(stored), "shortcut was not stored");
        }

        private static void TestUnavailableStorage(string sources)
        {
            string storage = (string)GetStaticProperty(
                ItemStorageType,
                "StorageDirectory");
            string available = storage + ".available";
            if (Directory.Exists(available))
            {
                Directory.Delete(available, true);
            }
            if (Directory.Exists(storage))
            {
                Directory.Move(storage, available);
            }
            File.WriteAllText(storage, "blocked");

            string source = Path.Combine(
                sources,
                "survives-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "source survives");
            try
            {
                try
                {
                    MoveIntoStorage(source);
                    throw new Exception("Storage move unexpectedly succeeded");
                }
                catch (IOException)
                {
                }

                AssertTrue(File.Exists(source), "source was lost after failure");
                AssertEqual(
                    "source survives",
                    File.ReadAllText(source),
                    "source content after failure");
            }
            finally
            {
                if (File.Exists(storage))
                {
                    File.Delete(storage);
                }
                if (Directory.Exists(available))
                {
                    Directory.Move(available, storage);
                }
            }
        }

        private static void TestConfigSaveFailureReported()
        {
            string configPath = ConfigStore.ConfigPath;
            string backupPath = configPath + ".save-test-backup";
            if (!File.Exists(configPath))
            {
                AssertTrue(
                    ConfigStore.Save(ConfigStore.CreateDefault()),
                    "test config could not be initialized");
            }
            File.Copy(configPath, backupPath, true);
            File.Delete(configPath);
            Directory.CreateDirectory(configPath);
            try
            {
                bool saved = ConfigStore.Save(ConfigStore.CreateDefault());
                AssertFalse(saved, "failed config write was reported as success");
                AssertTrue(
                    ConfigStore.LastSaveError != null,
                    "failed config write did not expose an error");
            }
            finally
            {
                if (Directory.Exists(configPath))
                {
                    Directory.Delete(configPath, false);
                }
                File.Move(backupPath, configPath);
            }
        }

        private static void TestFailedConfigRollsBackStorageMove(string sources)
        {
            string source = Path.Combine(
                sources,
                "config-rollback-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "must return");

            RegionConfig region = CreateEmptyRegion();
            object control = CreateRegionControl(region, delegate { return false; });
            InvokeInstance(control, "AddPath", source);

            AssertTrue(
                File.Exists(source),
                "source file was not restored after config failure");
            AssertEqual(
                "must return",
                File.ReadAllText(source),
                "restored source content");
            AssertEqual(
                0,
                region.Items.Count,
                "failed add remained in the region model");
        }

        private static void TestFailedConfigRollsBackDesktopReturn(
            string sources,
            string desktop)
        {
            string name =
                "desktop-return-" + Guid.NewGuid().ToString("N") + ".txt";
            string source = Path.Combine(sources, name);
            File.WriteAllText(source, "still stored");
            string stored = MoveIntoStorage(source);

            RegionConfig region = CreateEmptyRegion();
            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = stored;
            item.OriginalPath = source;
            item.Name = name;
            item.ExternalReference = false;
            region.Items.Add(item);

            object control = CreateRegionControl(region, delegate { return false; });
            bool restored = (bool)InvokeInstance(
                control,
                "RestoreItemToDesktop",
                item);

            AssertFalse(restored, "failed desktop return reported success");
            AssertTrue(
                File.Exists(stored),
                "Storage file was not restored after config failure");
            AssertEqual(
                "still stored",
                File.ReadAllText(stored),
                "restored Storage content");
            AssertFalse(
                File.Exists(Path.Combine(desktop, name)),
                "desktop copy remained after rollback");
            AssertEqual(1, region.Items.Count, "region item count after rollback");
        }

        private static void TestMovedPathRollback(string sources)
        {
            string source = Path.Combine(
                sources,
                "rollback-source-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "rollback");
            string stored = MoveIntoStorage(source);
            string restored = (string)InvokeStatic(
                ItemStorageType,
                "RollbackMovedPath",
                stored,
                source);

            AssertEqual(source, restored, "rollback destination");
            AssertTrue(File.Exists(source), "rollback source is missing");
            AssertFalse(File.Exists(stored), "rollback left a Storage copy");
            AssertEqual(
                "rollback",
                File.ReadAllText(source),
                "rollback content");
        }

        private static void TestCrossVolumeMove()
        {
            string storage = (string)GetStaticProperty(
                ItemStorageType,
                "StorageDirectory");
            string storageRoot = Path.GetPathRoot(Path.GetFullPath(storage));
            string sourceRoot = FindWritableVolumeOtherThan(storageRoot);
            if (string.IsNullOrEmpty(sourceRoot))
            {
                throw new SkipException(
                    "没有可写的第二个分区，存储位于 " + storageRoot);
            }

            string source = Path.Combine(
                sourceRoot,
                "ghostdesk-cross-volume-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(source, "nested");
            string stored = null;
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "data.txt"), "cross-volume");
            try
            {
                stored = MoveIntoStorage(source);

                AssertFalse(Directory.Exists(source), "cross-volume source remains");
                AssertEqual(
                    "cross-volume",
                    File.ReadAllText(Path.Combine(stored, "nested", "data.txt")),
                    "cross-volume content");
            }
            finally
            {
                if (Directory.Exists(source))
                {
                    Directory.Delete(source, true);
                }
                if (!string.IsNullOrEmpty(stored) && Directory.Exists(stored))
                {
                    Directory.Delete(stored, true);
                }
            }
        }

        /// <summary>找一个与给定分区不同、且当前进程可写的固定磁盘分区。</summary>
        private static string FindWritableVolumeOtherThan(string volumeRoot)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    {
                        continue;
                    }

                    string root = drive.RootDirectory.FullName;
                    if (string.Equals(
                            root,
                            volumeRoot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string probe = Path.Combine(
                        root,
                        "ghostdesk-volume-probe-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(probe);
                    Directory.Delete(probe, true);
                    return root;
                }
                catch
                {
                    // 该分区不可写，继续找下一个
                }
            }

            return null;
        }

        private static void TestSourceCleanupWarning(string sources)
        {
            string source = Path.Combine(
                sources,
                "cleanup-warning-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "data.txt"), "warning");
            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_FAIL_SOURCE_CLEANUP",
                "1");
            string stored = null;
            try
            {
                stored = MoveIntoStorage(source);
                string warning = (string)GetStaticProperty(
                    ItemStorageType,
                    "LastMoveWarning");
                AssertTrue(
                    !string.IsNullOrEmpty(warning),
                    "source cleanup residue was not reported");
                AssertTrue(
                    warning.Contains(".ghostdesk-source-"),
                    "cleanup warning did not contain the quarantine path");
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "GHOSTDESK_TEST_FAIL_SOURCE_CLEANUP",
                    null);
                if (!string.IsNullOrEmpty(stored) && Directory.Exists(stored))
                {
                    Directory.Delete(stored, true);
                }
                foreach (string residue in Directory.GetDirectories(
                    sources,
                    ".ghostdesk-source-*",
                    SearchOption.TopDirectoryOnly))
                {
                    File.SetAttributes(residue, FileAttributes.Directory);
                    Directory.Delete(residue, true);
                }
            }
        }

        private static RegionConfig CreateEmptyRegion()
        {
            RegionConfig region = new RegionConfig();
            region.Id = Guid.NewGuid().ToString("N");
            region.Name = "Transaction";
            region.ShowExtensions = "true";
            region.DockEdge = RegionConfig.DockNone;
            region.Items = new List<DesktopItem>();
            region.LaunchItems = new List<DesktopItem>();
            return region;
        }

        private static object CreateRegionControl(
            RegionConfig model,
            Func<bool> saveCallback)
        {
            return CreateRegionControlWithManifest(
                model,
                saveCallback,
                null,
                null);
        }

        private static object CreateRegionControlWithManifest(
            RegionConfig model,
            Func<bool> saveCallback,
            object manifest)
        {
            return CreateRegionControlWithManifest(
                model,
                saveCallback,
                manifest,
                null);
        }

        private static object CreateRegionControlWithManifest(
            RegionConfig model,
            Func<bool> saveCallback,
            object manifest,
            Func<DesktopItem, bool> synchronizeVisibilityCallback)
        {
            Type regionType = GhostDeskAssembly.GetType(
                "GhostDesk.RegionControl",
                true);
            Type actionRegionType = typeof(Action<>).MakeGenericType(regionType);
            MethodInfo ignoreMethod = typeof(StorageLifecycleTests)
                .GetMethod(
                    "IgnoreRegion",
                    BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(regionType);
            Delegate removeCallback = Delegate.CreateDelegate(
                actionRegionType,
                ignoreMethod);

            return Activator.CreateInstance(
                regionType,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                new object[]
                {
                    model,
                    removeCallback,
                    saveCallback,
                    new Action<DesktopItem>(delegate { }),
                    new Canvas(),
                    AppConfig.CollapseHoverButton,
                    new Func<List<RegionConfig>>(
                        delegate { return new List<RegionConfig> { model }; }),
                    new Action<DesktopItem, string>(delegate { }),
                    synchronizeVisibilityCallback,
                    manifest
                },
                null);
        }

        private static void IgnoreRegion<T>(T value)
        {
        }

        private static void TestConfigLoadDoesNotMove(string sources)
        {
            string source = Path.Combine(
                sources,
                "config-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "config migration");

            AppConfig config = ConfigStore.CreateDefault();
            config.Regions.Clear();
            RegionConfig region = new RegionConfig();
            region.Id = Guid.NewGuid().ToString("N");
            region.Name = "Regression";
            region.ShowExtensions = "true";
            region.Items = new List<DesktopItem>();
            region.LaunchItems = new List<DesktopItem>();
            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = source;
            item.OriginalPath = "";
            item.Name = Path.GetFileName(source);
            item.ExternalReference = true;
            region.Items.Add(item);
            config.Regions.Add(region);
            ConfigStore.Save(config);

            AppConfig loaded = ConfigStore.Load();
            DesktopItem loadedItem = loaded.Regions[0].Items[0];
            AssertTrue(
                File.Exists(source),
                "loading config moved the source file");
            AssertEqual(
                Path.GetFullPath(source),
                loadedItem.Path,
                "load changed the item path");

            object migrated = InvokeStatic(
                ItemStorageType,
                "MigrateLegacyItems",
                loaded);
            AssertTrue((bool)migrated, "explicit migration did not run");
            AssertFalse(File.Exists(source), "explicit migration kept source");
            AssertTrue(File.Exists(loadedItem.Path), "migrated file is missing");
            AssertEqual(
                Path.GetFullPath(source),
                loadedItem.OriginalPath,
                "saved original path");
            AssertFalse(
                loadedItem.ExternalReference,
                "migrated item is still external");
            AssertTrue(
                File.ReadAllText(ConfigStore.ConfigPath).Contains(
                    "\"OriginalPath\""),
                "config JSON does not contain OriginalPath");

            ConfigStore.Save(loaded);
            string migratedPath = loadedItem.Path;
            AppConfig reloaded = ConfigStore.Load();
            DesktopItem reloadedItem = reloaded.Regions[0].Items[0];
            AssertEqual(
                migratedPath,
                reloadedItem.Path,
                "second load moved the item again");
            AssertTrue(
                File.Exists(reloadedItem.Path),
                "reloaded item is missing");
        }

        private static void TestGitMetadataAndHiddenFiles(string sources)
        {
            string source = Path.Combine(
                sources,
                "git-folder-" + Guid.NewGuid().ToString("N"));
            string gitDirectory = Path.Combine(source, ".git");
            string objectsDirectory = Path.Combine(gitDirectory, "objects");
            string emptyDirectory = Path.Combine(source, "empty");
            Directory.CreateDirectory(objectsDirectory);
            Directory.CreateDirectory(emptyDirectory);
            File.WriteAllText(
                Path.Combine(gitDirectory, "HEAD"),
                "ref: refs/heads/main");
            File.WriteAllText(
                Path.Combine(objectsDirectory, "object.bin"),
                "git object");

            string hiddenFile = Path.Combine(source, "desktop.ini");
            File.WriteAllText(hiddenFile, "hidden");
            File.SetAttributes(
                hiddenFile,
                File.GetAttributes(hiddenFile) |
                FileAttributes.Hidden |
                FileAttributes.System);
            File.SetAttributes(
                gitDirectory,
                File.GetAttributes(gitDirectory) |
                FileAttributes.Hidden);

            string stored = MoveIntoStorage(source);

            AssertTrue(
                Directory.Exists(emptyDirectory.Replace(source, stored)),
                "empty directory was not copied");
            AssertEqual(
                "ref: refs/heads/main",
                File.ReadAllText(Path.Combine(stored, ".git", "HEAD")),
                "Git HEAD content");
            AssertEqual(
                "git object",
                File.ReadAllText(
                    Path.Combine(stored, ".git", "objects", "object.bin")),
                "Git object content");
            AssertTrue(
                Directory.Exists(Path.Combine(stored, ".git")),
                "Git directory is missing");
            AssertTrue(
                File.Exists(Path.Combine(stored, "desktop.ini")),
                "hidden desktop.ini is missing");
            AssertTrue(
                (File.GetAttributes(Path.Combine(stored, ".git")) &
                    FileAttributes.Hidden) != 0,
                "Git directory hidden attribute was lost");
        }

        private static void TestInterruptedCopy(string sources)
        {
            string source = Path.Combine(
                sources,
                "interrupted-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "one.txt"), "one");
            File.WriteAllText(Path.Combine(source, "two.txt"), "two");
            File.WriteAllText(Path.Combine(source, "three.txt"), "three");

            Environment.SetEnvironmentVariable(
                "GHOSTDESK_TEST_FAIL_COPY_AFTER_FILES",
                "1");
            try
            {
                try
                {
                    MoveIntoStorage(source);
                    throw new Exception(
                        "interrupted copy unexpectedly succeeded");
                }
                catch (IOException)
                {
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "GHOSTDESK_TEST_FAIL_COPY_AFTER_FILES",
                    null);
            }

            AssertEqual(
                "one",
                File.ReadAllText(Path.Combine(source, "one.txt")),
                "first source file after interruption");
            AssertEqual(
                "two",
                File.ReadAllText(Path.Combine(source, "two.txt")),
                "second source file after interruption");
            AssertEqual(
                "three",
                File.ReadAllText(Path.Combine(source, "three.txt")),
                "third source file after interruption");

            string storage = (string)GetStaticProperty(
                ItemStorageType,
                "StorageDirectory");
            string[] staging = Directory.GetFileSystemEntries(
                storage,
                ".ghostdesk-copy-*",
                SearchOption.TopDirectoryOnly);
            AssertEqual(
                0,
                staging.Length,
                "interrupted staging copy was not removed");
        }

        private static void TestExternalMoveCommitCheck(string sources)
        {
            string source = Path.Combine(
                sources,
                "external-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "external");
            DesktopItem item = new DesktopItem();
            item.Path = source;

            bool beforeMove = (bool)InvokeStatic(
                ItemStorageType,
                "WasExternalMoveCommitted",
                item);
            AssertFalse(
                beforeMove,
                "drag-away removal ran while source still existed");

            File.Delete(source);
            bool afterMove = (bool)InvokeStatic(
                ItemStorageType,
                "WasExternalMoveCommitted",
                item);
            AssertTrue(
                afterMove,
                "drag-away removal did not recognize committed move");
        }

        private static void TestDesktopReferenceDropRemovesOnlyRegionRecord(
            string desktop)
        {
            string path = Path.Combine(
                desktop,
                "desktop-reference-" + Guid.NewGuid().ToString("N") + ".bat");
            File.WriteAllText(path, "desktop content");

            RegionConfig region = CreateEmptyRegion();
            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = path;
            item.Name = Path.GetFileName(path);
            item.ExternalReference = true;
            region.Items.Add(item);

            object control = CreateRegionControl(region, delegate { return true; });
            bool removed = (bool)InvokeInstance(
                control,
                "RemoveItemAfterDesktopDrop",
                item);

            AssertTrue(removed, "desktop reference record was not removed");
            AssertEqual(0, region.Items.Count, "region record count");
            AssertTrue(File.Exists(path), "desktop file was removed");
            AssertEqual(
                "desktop content",
                File.ReadAllText(path),
                "desktop file content");
        }

        private static void TestDesktopReferenceDragDecision(string desktop)
        {
            string path = Path.Combine(desktop, "decision.bat");
            File.WriteAllText(path, "decision");
            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = path;
            item.ExternalReference = true;

            Type regionType = GhostDeskAssembly.GetType(
                "GhostDesk.RegionControl",
                true);
            MethodInfo decide = regionType.GetMethod(
                "ShouldRemoveItemAfterExternalDrop",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(decide != null, "drag removal decision method is missing");

            object[] desktopDrop =
            {
                item,
                DragDropEffects.None,
                false,
                false,
                true,
                true,
                false
            };
            AssertTrue(
                (bool)decide.Invoke(null, desktopDrop),
                "desktop drop did not remove external reference");

            object[] cancel = (object[])desktopDrop.Clone();
            cancel[2] = true;
            AssertFalse(
                (bool)decide.Invoke(null, cancel),
                "Escape cancellation removed external reference");

            object[] insideRegion = (object[])desktopDrop.Clone();
            insideRegion[3] = true;
            AssertFalse(
                (bool)decide.Invoke(null, insideRegion),
                "drop inside region removed external reference");

            // 当前规则（docs/兼容与升级约定.md：拖出分区范围 = 移除记录），与落点是否在桌面无关
            object[] outsideDesktop = (object[])desktopDrop.Clone();
            outsideDesktop[5] = false;
            AssertTrue(
                (bool)decide.Invoke(null, outsideDesktop),
                "dragging out of the region kept the reference");

            MethodInfo desktopTarget = regionType.GetMethod(
                "IsDesktopDropWindow",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(desktopTarget != null, "desktop target detector is missing");
            IntPtr overlayWindow = new IntPtr(1234);
            AssertTrue(
                (bool)desktopTarget.Invoke(
                    null,
                    new object[] { overlayWindow, overlayWindow, overlayWindow }),
                "GhostDesk desktop overlay was not treated as the desktop");
            AssertFalse(
                (bool)desktopTarget.Invoke(
                    null,
                    new object[] { IntPtr.Zero, IntPtr.Zero, overlayWindow }),
                "unrelated window was treated as the desktop");

            // 外部路径的项（分区引用的是我们代建的 .lnk）拖出分区时只移除记录，源文件不动
            DesktopItem external = new DesktopItem();
            external.Type = "file";
            external.Path = path;
            external.ExternalReference = true;
            object[] externalAway =
            {
                external,
                DragDropEffects.Move,
                false,
                false,
                true,
                false,
                true
            };
            AssertTrue(
                (bool)decide.Invoke(null, externalAway),
                "external reference was kept after dragging out of the region");
        }

        private static void TestDesktopReferenceDropSaveFailureRollback(
            string desktop)
        {
            string path = Path.Combine(
                desktop,
                "desktop-reference-save-" +
                Guid.NewGuid().ToString("N") + ".bat");
            File.WriteAllText(path, "must remain");

            RegionConfig region = CreateEmptyRegion();
            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = path;
            item.Name = Path.GetFileName(path);
            item.ExternalReference = true;
            region.Items.Add(item);
            region.Items.Add(new DesktopItem
            {
                Type = "file",
                Path = Path.Combine(desktop, "next-item.txt"),
                Name = "next-item.txt"
            });

            object control = CreateRegionControl(region, delegate { return false; });
            bool removed = (bool)InvokeInstance(
                control,
                "RemoveItemAfterDesktopDrop",
                item);

            AssertFalse(removed, "failed config save reported record removal");
            AssertEqual(2, region.Items.Count, "region item count after rollback");
            AssertTrue(
                ReferenceEquals(item, region.Items[0]),
                "record was not restored at its original index");
            AssertTrue(File.Exists(path), "desktop file changed after save failure");
            AssertEqual(
                "must remain",
                File.ReadAllText(path),
                "desktop file content after save failure");
        }

        private static void TestTimePicker()
        {
            Type pickerType = GhostDeskAssembly.GetType(
                "GhostDesk.TimePickerControl",
                true);
            object picker = Activator.CreateInstance(pickerType, true);
            List<TextBox> inputs = FindVisualChildren<TextBox>(
                (DependencyObject)picker);
            List<TextBlock> textBlocks = FindVisualChildren<TextBlock>(
                (DependencyObject)picker);

            AssertEqual(2, inputs.Count, "time picker text box count");
            bool foundColon = false;
            foreach (TextBlock block in textBlocks)
            {
                if (string.Equals(block.Text, ":", StringComparison.Ordinal))
                {
                    foundColon = true;
                    AssertFalse(
                        block.IsHitTestVisible,
                        "colon is still hit-test visible");
                }
            }
            AssertTrue(foundColon, "colon separator is missing");

            TextBox hour = FindInput(inputs, "小时");
            TextBox minute = FindInput(inputs, "分钟");
            AssertTrue(hour != null, "hour input is missing");
            AssertTrue(minute != null, "minute input is missing");
            hour.Text = "99";
            minute.Text = "30";

            object[] valueArguments = new object[] { null };
            bool valid = (bool)InvokeInstance(
                picker,
                "TryGetValue",
                valueArguments);
            AssertFalse(valid, "invalid hour was accepted");
            InvokeInstance(picker, "CommitInput", new object[0]);
            AssertFalse(
                string.Equals(hour.Text, "99", StringComparison.Ordinal),
                "invalid hour was not restored");
        }

        private static void TestScheduledTaskTypeChange()
        {
            Type windowType = GhostDeskAssembly.GetType(
                "GhostDesk.ScheduledTaskWindow",
                true);
            object window = Activator.CreateInstance(windowType, true);
            TextBox name = (TextBox)GetInstanceField(window, "_nameInput");
            TextBox countdown = (TextBox)GetInstanceField(
                window,
                "_countdownInput");
            System.Windows.Controls.RadioButton shutdown =
                (System.Windows.Controls.RadioButton)GetInstanceField(
                    window,
                    "_shutdownRadio");

            name.Text = "自定义任务名";
            countdown.Text = "0";
            shutdown.IsChecked = true;

            AssertEqual("自定义任务名", name.Text, "custom task name");
            AssertEqual("0", countdown.Text, "custom countdown");
        }

        private static string MoveIntoStorage(string source)
        {
            object[] arguments = new object[] { source, null };
            return (string)InvokeStatic(
                ItemStorageType,
                "MoveIntoStorage",
                arguments);
        }

        private static object CreateManifest(string manifestPath)
        {
            Type manifestType = GhostDeskAssembly.GetType(
                "GhostDesk.StorageManifest",
                true);

            object manifest = Activator.CreateInstance(
                manifestType,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                new object[] { manifestPath },
                null);

            InvokeInstance(manifest, "Load");
            return manifest;
        }

        private static int CountManifestLines(string path)
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            int count = 0;
            foreach (string line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    count++;
                }
            }

            return count;
        }

        private static void TestHiddenAttributeToggle(string sources)
        {
            string path = Path.Combine(
                sources,
                "hidden-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "hidden");

            Type safetyType = GhostDeskAssembly.GetType(
                "GhostDesk.StorageSafety",
                true);

            bool hidden = (bool)InvokeStatic(
                safetyType,
                "SetHiddenAttribute",
                path,
                true,
                null);
            AssertTrue(hidden, "SetHiddenAttribute reported failure");
            AssertEqual(
                (int)FileAttributes.Hidden,
                (int)(File.GetAttributes(path) & FileAttributes.Hidden),
                "HIDDEN attribute was not applied");

            bool cleared = (bool)InvokeStatic(
                ItemStorageType,
                "SetHiddenByGhostDesk",
                path,
                false);
            AssertTrue(cleared, "SetHiddenByGhostDesk(false) reported failure");

            FileAttributes attributes = File.GetAttributes(path);
            AssertEqual(
                0,
                (int)(attributes & FileAttributes.Hidden),
                "HIDDEN attribute survived clearing");
            AssertEqual(
                0,
                (int)(attributes & FileAttributes.System),
                "SYSTEM attribute survived clearing");

            File.Delete(path);
        }

        private static void TestStorageManifestLifecycle(string data)
        {
            string manifestPath = Path.Combine(
                data,
                "manifest-" + Guid.NewGuid().ToString("N") + ".tsv");
            string storageRoot = Path.Combine(
                data,
                "manifest-storage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storageRoot);

            string stored = Path.Combine(storageRoot, "stored.txt");
            File.WriteAllText(stored, "stored");

            object manifest = CreateManifest(manifestPath);
            InvokeInstance(
                manifest,
                "Add",
                stored,
                Path.Combine(data, "desktop-source.txt"),
                false);

            AssertTrue(
                File.Exists(manifestPath),
                "manifest file was not written");
            AssertTrue(
                InvokeInstance(manifest, "FindByStored", stored) != null,
                "entry missing after Add");

            object reloaded = CreateManifest(manifestPath);
            AssertTrue(
                InvokeInstance(reloaded, "FindByStored", stored) != null,
                "entry missing after reload");

            System.Collections.IList orphans =
                (System.Collections.IList)InvokeInstance(
                    reloaded,
                    "FindOrphans",
                    storageRoot,
                    new List<string>());
            AssertEqual(1, orphans.Count, "orphan count with an empty known set");

            List<string> known = new List<string>();
            known.Add(stored);
            System.Collections.IList matched =
                (System.Collections.IList)InvokeInstance(
                    reloaded,
                    "FindOrphans",
                    storageRoot,
                    known);
            AssertEqual(0, matched.Count, "orphan count when the item is known");

            InvokeInstance(reloaded, "Remove", stored);
            AssertTrue(
                InvokeInstance(reloaded, "FindByStored", stored) == null,
                "entry survived Remove");

            object afterRemove = CreateManifest(manifestPath);
            AssertTrue(
                InvokeInstance(afterRemove, "FindByStored", stored) == null,
                "removed entry came back after reload");

            File.Delete(stored);
            Directory.Delete(storageRoot, true);
            File.Delete(manifestPath);
        }

        private static void TestLockedFileIsNotStored(string sources)
        {
            string source = Path.Combine(
                sources,
                "locked-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "locked");

            RegionConfig region = CreateEmptyRegion();
            object control = CreateRegionControl(
                region,
                delegate { return true; });

            using (FileStream stream = new FileStream(
                source,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                InvokeInstance(control, "AddPath", source);
            }

            AssertTrue(File.Exists(source), "locked source file disappeared");
            AssertEqual("locked", File.ReadAllText(source), "locked source content");
            AssertEqual(0, region.Items.Count, "locked file entered the region");

            File.Delete(source);
        }

        private static void TestFailedConfigRollsBackManifest(
            string sources,
            string data)
        {
            string source = Path.Combine(
                sources,
                "manifest-rollback-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "manifest rollback");

            string manifestPath = Path.Combine(
                data,
                "manifest-" + Guid.NewGuid().ToString("N") + ".tsv");
            object manifest = CreateManifest(manifestPath);

            RegionConfig region = CreateEmptyRegion();
            object control = CreateRegionControlWithManifest(
                region,
                delegate { return false; },
                manifest);
            InvokeInstance(control, "AddPath", source);

            AssertTrue(
                File.Exists(source),
                "source file was not restored after config failure");
            AssertEqual(
                "manifest rollback",
                File.ReadAllText(source),
                "restored source content");
            AssertEqual(0, region.Items.Count, "region item count after rollback");
            AssertEqual(
                0,
                CountManifestLines(manifestPath),
                "manifest kept an entry after rollback");

            File.Delete(source);
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }

        private static void TestStoredItemMarkedHidden(string desktop)
        {
            string source = Path.Combine(
                desktop,
                "managed-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(source, "managed");

            RegionConfig region = CreateEmptyRegion();
            Func<DesktopItem, bool> synchronize = delegate(DesktopItem item)
            {
                return (bool)InvokeStatic(
                    ItemStorageType,
                    "SetHiddenByGhostDesk",
                    item.Path,
                    true);
            };

            object control = CreateRegionControlWithManifest(
                region,
                delegate { return true; },
                null,
                synchronize);
            try
            {
                InvokeInstance(control, "AddPath", source);

                AssertEqual(1, region.Items.Count, "region item count");
                DesktopItem stored = region.Items[0];
                AssertTrue(
                    stored.HiddenByGhostDesk,
                    "desktop item was not marked as GhostDesk-managed");
                AssertEqual(
                    source,
                    stored.Path,
                    "desktop item should reference the file in place");
                AssertTrue(
                    File.Exists(source),
                    "desktop file left its original path");
                AssertEqual(
                    (int)FileAttributes.Hidden,
                    (int)(File.GetAttributes(source) & FileAttributes.Hidden),
                    "desktop file was not hidden");
            }
            finally
            {
                if (File.Exists(source))
                {
                    InvokeStatic(
                        ItemStorageType,
                        "ClearHiddenSystemAttributes",
                        source);
                    File.Delete(source);
                }
            }
        }

        private static void TestAddAndRestoreManifest(
            string sources,
            string data)
        {
            string name = "reference-" + Guid.NewGuid().ToString("N") + ".txt";
            string source = Path.Combine(sources, name);
            File.WriteAllText(source, "reference me");

            string manifestPath = Path.Combine(
                data,
                "manifest-" + Guid.NewGuid().ToString("N") + ".tsv");
            object manifest = CreateManifest(manifestPath);
            string referencePath = null;

            RegionConfig region = CreateEmptyRegion();
            object control = CreateRegionControlWithManifest(
                region,
                delegate { return true; },
                manifest);
            try
            {
                InvokeInstance(control, "AddPath", source);

                AssertEqual(1, region.Items.Count, "region item count");
                AssertTrue(
                    File.Exists(source),
                    "source file left its original path");

                DesktopItem item = region.Items[0];
                referencePath = item.Path;
                AssertTrue(
                    referencePath.EndsWith(
                        ".lnk",
                        StringComparison.OrdinalIgnoreCase),
                    "external item should reference a shortcut");
                AssertEqual(
                    source,
                    item.OriginalPath,
                    "original path was not recorded");
                AssertTrue(File.Exists(referencePath), "shortcut file is missing");
                AssertEqual(
                    source,
                    (string)InvokeStatic(
                        ItemStorageType,
                        "ReadShortcutTarget",
                        referencePath),
                    "shortcut target");
                AssertEqual(
                    0,
                    CountManifestLines(manifestPath),
                    "in-place item should not write the legacy Storage manifest");

                bool removed = (bool)InvokeInstance(
                    control,
                    "RemoveItemAfterDesktopDrop",
                    item);
                AssertTrue(removed, "removing the reference reported failure");
                AssertEqual(
                    0,
                    region.Items.Count,
                    "region item count after removal");
                AssertTrue(
                    File.Exists(source),
                    "source file was touched by record removal");
                AssertEqual(
                    "reference me",
                    File.ReadAllText(source),
                    "source content");
            }
            finally
            {
                if (!string.IsNullOrEmpty(referencePath) &&
                    File.Exists(referencePath))
                {
                    File.Delete(referencePath);
                }
                if (File.Exists(source))
                {
                    File.Delete(source);
                }
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
        }

        private static void TestVisibilitySync()
        {
            string storage = (string)GetStaticProperty(
                ItemStorageType,
                "StorageDirectory");
            Directory.CreateDirectory(storage);

            string path = Path.Combine(
                storage,
                "visibility-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "visibility");

            DesktopItem item = new DesktopItem();
            item.Type = "file";
            item.Path = path;
            item.Name = Path.GetFileName(path);
            item.HiddenByGhostDesk = true;

            RegionConfig region = CreateEmptyRegion();
            region.Items.Add(item);
            AppConfig config = new AppConfig();
            config.Regions = new List<RegionConfig>();
            config.Regions.Add(region);

            bool hidden = (bool)InvokeStatic(
                ItemStorageType,
                "SynchronizeManagedVisibility",
                config,
                false);
            AssertTrue(hidden, "hiding the switch reported failure");
            AssertEqual(
                (int)FileAttributes.Hidden,
                (int)(File.GetAttributes(path) & FileAttributes.Hidden),
                "HIDDEN attribute was not applied by the switch");

            bool shown = (bool)InvokeStatic(
                ItemStorageType,
                "SynchronizeManagedVisibility",
                config,
                true);
            AssertTrue(shown, "showing the switch reported failure");
            AssertEqual(
                0,
                (int)(File.GetAttributes(path) &
                    (FileAttributes.Hidden | FileAttributes.System)),
                "hidden attributes survived the switch");

            File.Delete(path);
        }

        private static void TestPublicDesktopCheck()
        {
            Type safetyType = GhostDeskAssembly.GetType(
                "GhostDesk.StorageSafety",
                true);

            string publicPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonDesktopDirectory),
                "ghostdesk-probe.txt");
            AssertTrue(
                (bool)InvokeStatic(
                    safetyType,
                    "IsOnPublicDesktop",
                    publicPath),
                "public desktop path was not detected");

            string userPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory),
                "ghostdesk-probe.txt");
            AssertFalse(
                (bool)InvokeStatic(
                    safetyType,
                    "IsOnPublicDesktop",
                    userPath),
                "user desktop path was misreported as public");
        }

        private static void TestBulkStorage(string sources)
        {
            string folder = Path.Combine(
                sources,
                "bulk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            const int count = 25;
            for (int index = 0; index < count; index++)
            {
                File.WriteAllText(
                    Path.Combine(
                        folder,
                        "bulk-" + index.ToString() + ".txt"),
                    "bulk");
            }

            RegionConfig region = CreateEmptyRegion();
            object control = CreateRegionControl(
                region,
                delegate { return true; });

            Stopwatch watch = Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                InvokeInstance(
                    control,
                    "AddPath",
                    Path.Combine(
                        folder,
                        "bulk-" + index.ToString() + ".txt"));
            }
            watch.Stop();

            AssertEqual(count, region.Items.Count, "bulk stored item count");
            AssertTrue(
                watch.Elapsed.TotalSeconds < 10.0,
                "bulk storage took " +
                watch.Elapsed.TotalSeconds.ToString("0.0") + "s");

            foreach (DesktopItem storedItem in region.Items)
            {
                if (File.Exists(storedItem.Path))
                {
                    File.Delete(storedItem.Path);
                }
            }

            Directory.Delete(folder, true);
        }

        private static TextBox FindInput(
            IEnumerable<TextBox> inputs,
            string toolTip)
        {
            foreach (TextBox input in inputs)
            {
                if (string.Equals(
                        input.ToolTip as string,
                        toolTip,
                        StringComparison.Ordinal))
                {
                    return input;
                }
            }
            return null;
        }

        private static List<T> FindVisualChildren<T>(
            DependencyObject parent)
            where T : DependencyObject
        {
            List<T> results = new List<T>();
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                T match = child as T;
                if (match != null)
                {
                    results.Add(match);
                }
                results.AddRange(FindVisualChildren<T>(child));
            }
            return results;
        }

        private static object GetStaticProperty(Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                throw new MissingMemberException(type.FullName, propertyName);
            }
            return property.GetValue(null, null);
        }

        private static object GetInstanceField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    instance.GetType().FullName,
                    fieldName);
            }
            return field.GetValue(instance);
        }

        private static object InvokeStatic(
            Type type,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        private static object InvokeInstance(
            object instance,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(
                    instance.GetType().FullName,
                    methodName);
            }

            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS: " + name);
            }
            catch (SkipException ex)
            {
                _skipped++;
                Console.WriteLine("SKIP: " + name + " (" + ex.Message + ")");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL: " + name);
                Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        private sealed class SkipException : Exception
        {
            public SkipException(string message)
                : base(message)
            {
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            AssertTrue(!condition, message);
        }

        private static void AssertEqual(
            object expected,
            object actual,
            string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(
                    "Assertion failed: " + message +
                    "\nExpected: " + (expected ?? "<null>") +
                    "\nActual:   " + (actual ?? "<null>"));
            }
        }
    }
}
