using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidRecentTaskParserTests
{
    /// <summary>Captured from `adb shell dumpsys activity recents` on Android 17 (API 37).</summary>
    private const string ModernRecentsDump =
        """
        ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)
        mRecentsUid=10190
        mRecentsComponent=ComponentInfo{com.google.android.apps.nexuslauncher/com.android.quickstep.RecentsActivity}
        mFreezeTaskListReordering=false
          Recent tasks:
          * Recent #0: Task{6168258 #100 type=standard A=10257:com.appeey.soable}
            userId=0 effectiveUid=u0a257 mCallingUid=2000 mUserSetupComplete=true mCallingPackage=com.android.shell
            affinity=10257:com.appeey.soable
            intent={act=android.intent.action.MAIN cat=[android.intent.category.LAUNCHER] flg=0x10200000 cmp=com.appeey.soable/crc649980cb1396df4861.MainActivity}
            mActivityComponent=com.appeey.soable/crc649980cb1396df4861.MainActivity
            autoRemoveRecents=false isPersistable=true activityType=1
            Activities=[ActivityRecord{244833083 u0 com.appeey.soable/crc649980cb1396df4861.MainActivity t100}]
            mRootProcess=ProcessRecord{299cb0f 4202:com.appeey.soable/u0a257}
            taskId=100 rootTaskId=100
            mHasBeenVisible=true
          * Recent #1: Task{6a37efa #99 type=standard A=1000:com.android.settings.root}
            userId=0 effectiveUid=1000 mCallingUid=2000 mUserSetupComplete=true
            affinity=1000:com.android.settings.root
            intent={act=android.settings.SETTINGS flg=0x10000000 xflg=0x4 cmp=com.android.settings/.homepage.SettingsHomepageActivity}
            mActivityComponent=com.android.settings/.homepage.SettingsHomepageActivity
            Activities=[ActivityRecord{254950693 u0 com.android.settings/.homepage.SettingsHomepageActivity t99}]
            taskId=99 rootTaskId=99
          * Recent #2: Task{3cacf4d #2 type=home I=com.google.android.apps.nexuslauncher/.NexusLauncherActivity}
            userId=0 effectiveUid=u0a190 mCallingUid=0
            intent={act=android.intent.action.MAIN cat=[android.intent.category.HOME] flg=0x10000100 cmp=com.google.android.apps.nexuslauncher/.NexusLauncherActivity}
            mActivityComponent=com.google.android.apps.nexuslauncher/.NexusLauncherActivity
            taskId=2 rootTaskId=1
            mHasBeenVisible=true

        """;

    /// <summary>Older releases print `TaskRecord` with a single taskId and no `type` field.</summary>
    private const string LegacyRecentsDump =
        """
        ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)
          Recent tasks:
          * Recent #0: TaskRecord{6f52b4c #12 A=10257:com.example.app U=0 StackId=1 sz=1}
            userId=0 effectiveUid=10257 mCallingUid=2000 mUserSetupComplete=true
            affinity=10257:com.example.app
            intent={act=android.intent.action.MAIN cat=[android.intent.category.LAUNCHER] flg=0x10200000 cmp=com.example.app/.MainActivity}
            realActivity=com.example.app/.MainActivity
            autoRemoveRecents=false isPersistable=true
            Activities=[ActivityRecord{9c3a1 u0 com.example.app/.MainActivity t12}]
            taskId=12

        """;

    [Test]
    public async Task Parse_ReadsEveryTaskWithItsIdsAndOwner()
    {
        var tasks = AndroidRecentTaskParser.Parse(ModernRecentsDump);

        using (Assert.Multiple())
        {
            await Assert.That(tasks.Count).IsEqualTo(3);
            await Assert.That(tasks[0].TaskId).IsEqualTo(100);
            await Assert.That(tasks[0].RootTaskId).IsEqualTo(100);
            await Assert.That(tasks[0].Type).IsEqualTo("standard");
            await Assert.That(tasks[0].PackageName).IsEqualTo("com.appeey.soable");
            await Assert.That(tasks[0].Component).IsEqualTo("com.appeey.soable/crc649980cb1396df4861.MainActivity");
            await Assert.That(tasks[1].PackageName).IsEqualTo("com.android.settings");
            await Assert.That(tasks[2].Type).IsEqualTo("home");
            await Assert.That(tasks[2].TaskId).IsEqualTo(2);
            await Assert.That(tasks[2].RootTaskId).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Parse_ReadsLegacyTaskRecordShape()
    {
        var task = AndroidRecentTaskParser.Parse(LegacyRecentsDump).Single();

        using (Assert.Multiple())
        {
            await Assert.That(task.TaskId).IsEqualTo(12);
            await Assert.That(task.RootTaskId).IsEqualTo(12);
            await Assert.That(task.PackageName).IsEqualTo("com.example.app");
            await Assert.That(task.Type).IsEmpty();
            await Assert.That(task.IsDismissable).IsTrue();
        }
    }

    [Test]
    public async Task Parse_HandlesEmptyAndUnrelatedOutput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(AndroidRecentTaskParser.Parse(null).Count).IsEqualTo(0);
            await Assert.That(AndroidRecentTaskParser.Parse(string.Empty).Count).IsEqualTo(0);
            await Assert.That(AndroidRecentTaskParser.Parse("mRecentsUid=10190").Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task FindDismissable_SelectsOnlyTheRequestedPackage()
    {
        var tasks = AndroidRecentTaskParser.Parse(ModernRecentsDump);

        var dismissable = AndroidRecentTaskParser.FindDismissable(tasks, "com.appeey.soable");

        using (Assert.Multiple())
        {
            await Assert.That(dismissable.Count).IsEqualTo(1);
            await Assert.That(dismissable[0].RootTaskId).IsEqualTo(100);
        }
    }

    [Test]
    public async Task FindDismissable_NeverSelectsTheHomeOrRecentsSurface()
    {
        var tasks = AndroidRecentTaskParser.Parse(ModernRecentsDump);

        using (Assert.Multiple())
        {
            await Assert.That(AndroidRecentTaskParser.FindDismissable(tasks, "com.google.android.apps.nexuslauncher").Count).IsEqualTo(0);
            await Assert.That(AndroidRecentTaskParser.Parse(ModernRecentsDump)[2].IsDismissable).IsFalse();
        }
    }

    [Test]
    public async Task FindDismissable_IsCaseInsensitiveAndIgnoresBlankPackages()
    {
        var tasks = AndroidRecentTaskParser.Parse(ModernRecentsDump);

        using (Assert.Multiple())
        {
            await Assert.That(AndroidRecentTaskParser.FindDismissable(tasks, "COM.APPeEY.SOABLE").Count).IsEqualTo(1);
            await Assert.That(AndroidRecentTaskParser.FindDismissable(tasks, "  ").Count).IsEqualTo(0);
            await Assert.That(AndroidRecentTaskParser.FindDismissable(tasks, null).Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task FindDismissable_KeepsOneEntryPerRootTask()
    {
        var tasks = new[]
        {
            new AndroidRecentTask(10, 10, "standard", "com.example.app", "com.example.app/.MainActivity"),
            new AndroidRecentTask(11, 10, "standard", "com.example.app", "com.example.app/.DetailsActivity")
        };

        var dismissable = AndroidRecentTaskParser.FindDismissable(tasks, "com.example.app");

        using (Assert.Multiple())
        {
            await Assert.That(dismissable.Count).IsEqualTo(1);
            await Assert.That(dismissable[0].RootTaskId).IsEqualTo(10);
        }
    }

    [Test]
    public async Task FindDismissable_SkipsTasksWithoutAUsableRootTask()
    {
        var tasks = new[]
        {
            new AndroidRecentTask(0, 0, "standard", "com.example.app", "com.example.app/.MainActivity")
        };

        await Assert.That(AndroidRecentTaskParser.FindDismissable(tasks, "com.example.app").Count).IsEqualTo(0);
    }
}
